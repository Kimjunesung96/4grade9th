using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;

public struct OriginalPosition : IComponentData { public float3 Value; }

[BurstCompile]
public partial struct ResetStressJob : IJobEntity
{
    public void Execute(ref BlockStress stress) { stress.TargetStress = 0.0f; }
}

[BurstCompile]
public partial struct CalculateJointStressJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<BlockStress> StressLookup;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA; Entity entityB = pair.EntityB;
        if (!TransformLookup.HasComponent(entityA) || !TransformLookup.HasComponent(entityB)) return;

        var transA = TransformLookup[entityA]; var transB = TransformLookup[entityB];
        float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
        float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);

        float dist = math.distance(pivotA, pivotB);
        float tensile = 400.0f;
        float finalStress = dist * tensile;

        if (StressLookup.HasComponent(entityA)) { var s = StressLookup[entityA]; s.TargetStress += finalStress; StressLookup[entityA] = s; }
        if (StressLookup.HasComponent(entityB)) { var s = StressLookup[entityB]; s.TargetStress += finalStress; StressLookup[entityB] = s; }
    }
}

[BurstCompile]
public partial struct ApplyExternalLoadJob : IJobEntity
{
    public bool IsWeightScanMode;
    public float BaseWeight;
    public float DynamicSensitivity;
    public float DeltaTime;
    public float QuakeX;
    public float QuakeZ;

    public void Execute(in PhysicsMass mass, ref BlockStress stress, ref PhysicsVelocity velRW)
    {
        float additionalStress = 0.0f;
        if (IsWeightScanMode) { float realMass = mass.InverseMass > 0.0f ? (1.0f / mass.InverseMass) : 1.0f; additionalStress = BaseWeight * realMass * 0.01f; }
        else { velRW.Linear += new float3(QuakeX, 0.0f, QuakeZ) * DeltaTime; additionalStress = math.lengthsq(velRW.Linear) * DynamicSensitivity; }
        stress.TargetStress += additionalStress;
    }
}

[BurstCompile]
public partial struct SmoothStressJob : IJobEntity
{
    public float DeltaTime; public float SmoothSpeed;
    public void Execute(ref BlockStress stress) { float currentSmoothed = math.lerp(stress.SmoothedStress, stress.TargetStress, DeltaTime * SmoothSpeed); stress.SmoothedStress = math.max(stress.SmoothedStress, currentSmoothed); }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
public partial struct StressVisualizationSystem : ISystem
{
    private float scanTimer;
    private bool isScanning;
    private bool needsColorUpdate;
    private bool isWeightScanMode;
    private EntityQuery jointQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BlockStress>();
        jointQuery = state.GetEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>(), ComponentType.ReadOnly<PhysicsJoint>());
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Input.GetKeyDown(KeyCode.V)) { isWeightScanMode = true; StartScan(ref state); }
        else if (Input.GetKeyDown(KeyCode.B)) { isWeightScanMode = false; StartScan(ref state); }

        if (isScanning)
        {
            scanTimer -= SystemAPI.Time.DeltaTime;
            if (scanTimer <= 0.0f) { isScanning = false; needsColorUpdate = true; StopPhysics(ref state); return; }

            state.Dependency = new ResetStressJob().ScheduleParallel(state.Dependency);
            state.Dependency = new CalculateJointStressJob { TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true), StressLookup = SystemAPI.GetComponentLookup<BlockStress>(false) }.Schedule(jointQuery, state.Dependency);

            float time = (float)SystemAPI.Time.ElapsedTime;
            state.Dependency = new ApplyExternalLoadJob { IsWeightScanMode = isWeightScanMode, BaseWeight = 1.0f, DynamicSensitivity = 0.5f, DeltaTime = SystemAPI.Time.DeltaTime, QuakeX = !isWeightScanMode ? math.sin(time * 35.0f) * 5.0f : 0.0f, QuakeZ = !isWeightScanMode ? math.cos(time * 28.0f) * 5.0f : 0.0f }.ScheduleParallel(state.Dependency);
            state.Dependency = new SmoothStressJob { DeltaTime = SystemAPI.Time.DeltaTime, SmoothSpeed = 3.0f }.ScheduleParallel(state.Dependency);
        }

        if (needsColorUpdate) { UpdateResults(ref state); }
    }

    private void StartScan(ref SystemState state)
    {
        scanTimer = 5.0f; isScanning = true; needsColorUpdate = false;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (stress, transform, color, entity) in SystemAPI.Query<RefRW<BlockStress>, RefRO<LocalTransform>, RefRW<URPMaterialPropertyBaseColor>>().WithEntityAccess())
        {
            color.ValueRW.Value = new float4(1.0f, 1.0f, 1.0f, 1.0f);
            stress.ValueRW.SmoothedStress = 0.0f; stress.ValueRW.TargetStress = 0.0f;
            float3 p = transform.ValueRO.Position;
            float3 perfectPos = new float3(math.round((p.x - 1.5f) / 3.0f) * 3.0f + 1.5f, math.round((p.y - 1.5f) / 3.0f) * 3.0f + 1.5f, math.round((p.z - 1.5f) / 3.0f) * 3.0f + 1.5f);

            if (!SystemAPI.HasComponent<OriginalPosition>(entity)) ecb.AddComponent(entity, new OriginalPosition { Value = perfectPos });
            if (SystemAPI.HasComponent<PhysicsVelocity>(entity)) { var vel = SystemAPI.GetComponent<PhysicsVelocity>(entity); vel.Linear.y -= 0.01f; ecb.SetComponent(entity, vel); }
            if (SystemAPI.HasComponent<PhysicsGravityFactor>(entity)) { ecb.SetComponent(entity, new PhysicsGravityFactor { Value = 1.0f }); }
        }
        ecb.Playback(state.EntityManager); ecb.Dispose();
    }

    private void StopPhysics(ref SystemState state)
    {
        foreach (var (transform, originalPos, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<OriginalPosition>>().WithEntityAccess())
        {
            transform.ValueRW.Position = originalPos.ValueRO.Value; transform.ValueRW.Rotation = quaternion.identity;
            if (SystemAPI.HasComponent<PhysicsVelocity>(entity)) { SystemAPI.SetComponent(entity, new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero }); }
            if (SystemAPI.HasComponent<PhysicsGravityFactor>(entity)) { SystemAPI.SetComponent(entity, new PhysicsGravityFactor { Value = 0.0f }); }
        }
    }

    public void UpdateResults(ref SystemState state)
    {
        state.Dependency.Complete();
        string dirPath = Path.Combine(Application.dataPath, "StressBlock");
        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
        string path = Path.Combine(dirPath, "CurrentStress.csv");

        string header = "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tool";
        Dictionary<string, string> masterData = new Dictionary<string, string>();

        if (File.Exists(path))
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length > 0) masterData[cols] = lines[i];
            }
        }

        using (StreamWriter writer = new StreamWriter(path, false))
        {
            writer.WriteLine(header);
            foreach (var (stress, color, pos, entity) in SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<OriginalPosition>>().WithEntityAccess())
            {
                float curStress = stress.ValueRO.SmoothedStress;
                string matName = SystemAPI.HasComponent<BlockMaterial>(entity) ? SystemAPI.GetComponent<BlockMaterial>(entity).MaterialName.ToString() : "Default";
                float tensile = SystemAPI.HasComponent<BlockMaterial>(entity) ? SystemAPI.GetComponent<BlockMaterial>(entity).TensileStiffness : 400f;
                float limit = math.max(1.0f, tensile);
                float t = math.clamp(curStress / limit, 0.0f, 1.0f);

                float3 p = pos.ValueRO.Value;
                int ix = (int)math.round(p.x * 10f);
                int iy = (int)math.round(p.y * 10f);
                int iz = (int)math.round(p.z * 10f);
                string id = $"{ix}_{iz}_{iy}";

                string tool = "Existing";
                if (masterData.ContainsKey(id))
                {
                    string[] oldCols = masterData[id].Split(',');
                    [cite_start]if (oldCols.Length >= 9) tool = oldCols[8];
                }

                writer.WriteLine($"{id},{p.x:F2},{p.y:F2},{p.z:F2},{curStress:F2},{(t >= 0.8f ? "DANGER" : "SAFE")},{(t >= 0.8f ? "Y" : "N")},{matName},{tool}");
            }
        }
        needsColorUpdate = false;
        Debug.Log("📊 [시각화] CurrentStress.csv 표준화 업데이트 완료!");
    }
}
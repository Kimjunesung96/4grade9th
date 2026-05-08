using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using System.IO;
using System;
using Unity.Burst;
using Unity.Collections;

public struct OriginalPosition : IComponentData { public float3 Value; }

[BurstCompile]
public partial struct ResetStressJob : IJobEntity
{
    public void Execute(ref BlockStress stress)
    {
        stress.TargetStress = 0.0f;
    }
}

[BurstCompile]
public partial struct CalculateJointStressJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    [ReadOnly] public ComponentLookup<BlockMaterial> MaterialLookup;
    public ComponentLookup<BlockStress> StressLookup;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA;
        Entity entityB = pair.EntityB;

        if (!TransformLookup.HasComponent(entityA) || !TransformLookup.HasComponent(entityB)) return;
        if (!MaterialLookup.HasComponent(entityA) || !MaterialLookup.HasComponent(entityB)) return;
        if (!StressLookup.HasComponent(entityA) || !StressLookup.HasComponent(entityB)) return;

        var matA = MaterialLookup[entityA];
        var matB = MaterialLookup[entityB];

        // ⭐ 재질 초기화 전(0)일 경우를 대비해 최소 강성 보장 및 증폭 계수 적용
        float tensile = math.max(10.0f, (matA.TensileStiffness + matB.TensileStiffness) * 0.5f);

        var transA = TransformLookup[entityA];
        var transB = TransformLookup[entityB];

        float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
        float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);

        // 조인트 변위 거리 계산
        float dist = math.distance(pivotA, pivotB);

        // 물리적 스트레스 계산 (방어력 체급에 맞게 시각화되도록 계수 조정 가능)
        float finalStress = dist * tensile * 100.0f;

        var sA = StressLookup[entityA]; sA.TargetStress += finalStress; StressLookup[entityA] = sA;
        var sB = StressLookup[entityB]; sB.TargetStress += finalStress; StressLookup[entityB] = sB;
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
        if (IsWeightScanMode)
        {
            float realMass = mass.InverseMass > 0.0f ? (1.0f / mass.InverseMass) : 1.0f;
            // 환경 하중 비중을 조절하여 물리 계산값이 묻히지 않게 함
            additionalStress = BaseWeight * realMass * 0.01f;
        }
        else
        {
            velRW.Linear += new float3(QuakeX, 0.0f, QuakeZ) * DeltaTime;
            additionalStress = math.lengthsq(velRW.Linear) * DynamicSensitivity;
        }
        stress.TargetStress += additionalStress;
    }
}

[BurstCompile]
public partial struct SmoothStressJob : IJobEntity
{
    public float DeltaTime;
    public float SmoothSpeed;
    public void Execute(ref BlockStress stress)
    {
        float currentSmoothed = math.lerp(stress.SmoothedStress, stress.TargetStress, DeltaTime * SmoothSpeed);
        stress.SmoothedStress = math.max(stress.SmoothedStress, currentSmoothed);
    }
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

            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var stressLookup = SystemAPI.GetComponentLookup<BlockStress>(false);
            var materialLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true);

            state.Dependency = new CalculateJointStressJob
            {
                TransformLookup = transformLookup,
                StressLookup = stressLookup,
                MaterialLookup = materialLookup
            }.Schedule(jointQuery, state.Dependency);

            float time = (float)SystemAPI.Time.ElapsedTime;
            state.Dependency = new ApplyExternalLoadJob
            {
                IsWeightScanMode = isWeightScanMode,
                BaseWeight = 1.0f,
                DynamicSensitivity = 0.5f,
                DeltaTime = SystemAPI.Time.DeltaTime,
                QuakeX = !isWeightScanMode ? math.sin(time * 35.0f) * 5.0f : 0.0f,
                QuakeZ = !isWeightScanMode ? math.cos(time * 28.0f) * 5.0f : 0.0f
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new SmoothStressJob { DeltaTime = SystemAPI.Time.DeltaTime, SmoothSpeed = 3.0f }.ScheduleParallel(state.Dependency);
        }

        if (needsColorUpdate) { UpdateResults(ref state); }
    }

    private void StartScan(ref SystemState state)
    {
        scanTimer = 5.0f; isScanning = true; needsColorUpdate = false;
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (color, gravity, velocity, stress, transform, entity) in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>, RefRW<PhysicsGravityFactor>, RefRW<PhysicsVelocity>, RefRW<BlockStress>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            color.ValueRW.Value = new float4(1.0f, 1.0f, 1.0f, 1.0f);
            gravity.ValueRW.Value = 1.0f;
            velocity.ValueRW.Linear.y -= 0.01f;
            stress.ValueRW.SmoothedStress = 0.0f;
            stress.ValueRW.TargetStress = 0.0f;

            float3 p = transform.ValueRO.Position;
            float3 perfectPos = new float3(math.round((p.x - 1.5f) / 3.0f) * 3.0f + 1.5f, math.round((p.y - 1.5f) / 3.0f) * 3.0f + 1.5f, math.round((p.z - 1.5f) / 3.0f) * 3.0f + 1.5f);
            if (!SystemAPI.HasComponent<OriginalPosition>(entity)) ecb.AddComponent(entity, new OriginalPosition { Value = perfectPos });
        }
        ecb.Playback(state.EntityManager); ecb.Dispose();
    }

    private void StopPhysics(ref SystemState state)
    {
        foreach (var (transform, velocity, gravity, originalPos) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRO<OriginalPosition>>())
        {
            gravity.ValueRW.Value = 0.0f; velocity.ValueRW.Linear = float3.zero; velocity.ValueRW.Angular = float3.zero;
            transform.ValueRW.Position = originalPos.ValueRO.Value; transform.ValueRW.Rotation = quaternion.identity;
        }
    }

    private void UpdateResults(ref SystemState state)
    {
        state.Dependency.Complete();
        string currentStressPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        using (StreamWriter writer = new StreamWriter(currentStressPath, false))
        {
            writer.WriteLine("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Defense");

            foreach (var (stress, color, health, originalPos, mat) in
                     SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<BlockHealth>, RefRO<OriginalPosition>, RefRO<BlockMaterial>>())
            {
                float3 p = originalPos.ValueRO.Value;
                float curStress = stress.ValueRO.SmoothedStress;

                // ⭐ 각 블록의 실제 방어력(Defense)을 기준으로 그라데이션 비율(t) 계산
                float defense = math.max(1.0f, health.ValueRO.Defense);
                float t = math.clamp(curStress / defense, 0.0f, 1.0f);

                if (isWeightScanMode)
                    color.ValueRW.Value = new float4(1.0f, 1.0f - t, 1.0f - t, 1.0f);
                else
                    color.ValueRW.Value = new float4(1.0f - t, 1.0f, 1.0f - t, 1.0f);

                string risk = t >= 0.8f ? "DANGER" : (t >= 0.5f ? "WARNING" : "SAFE");
                string id = $"{(int)math.round(p.x * 10.0f)}_{(int)math.round(p.z * 10.0f)}_{(int)math.round(p.y * 10.0f)}";

                writer.WriteLine($"{id},{p.x:F2},{p.y:F2},{p.z:F2},{curStress:F2},{risk},{(risk == "DANGER" ? "Y" : "")},{mat.ValueRO.MaterialName},{defense:F1}");
            }
        }
        needsColorUpdate = false;
        Debug.Log("📊 [방어력 비례 시각화] 스트레스 분석 리포트 생성 완료!");
    }
}
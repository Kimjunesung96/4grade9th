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
public partial struct ResetStressJob : IJobEntity { public void Execute(ref BlockStress stress) { stress.TargetStress = 0f; } }

[BurstCompile]
public partial struct CalculateJointStressJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<BlockStress> StressLookup;
    public bool IsWeightScanMode;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA; Entity entityB = pair.EntityB;
        if (TransformLookup.HasComponent(entityA) && TransformLookup.HasComponent(entityB))
        {
            var transA = TransformLookup[entityA]; var transB = TransformLookup[entityB];
            float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
            float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);
            float3 deltaDisplacement = pivotA - pivotB; float jointStressResult = 0f;

            if (IsWeightScanMode) { float axialStiffness = 15.0f; jointStressResult = math.abs(deltaDisplacement.y) * axialStiffness; }
            else { float shearStiffness = 10.0f; float bendingStiffness = 15.0f; float dotProduct = math.abs(math.dot(transA.Rotation.value, transB.Rotation.value)); float angleDiff = 2.0f * math.acos(math.clamp(dotProduct, -1f, 1f)); float bendingStress = angleDiff * bendingStiffness; float shearStress = math.length(new float2(deltaDisplacement.x, deltaDisplacement.z)) * shearStiffness; jointStressResult = bendingStress + shearStress; }

            if (StressLookup.HasComponent(entityA)) { var stressA = StressLookup[entityA]; stressA.TargetStress += jointStressResult; StressLookup[entityA] = stressA; }
            if (StressLookup.HasComponent(entityB)) { var stressB = StressLookup[entityB]; stressB.TargetStress += jointStressResult; StressLookup[entityB] = stressB; }
        }
    }
}

[BurstCompile]
public partial struct ApplyExternalLoadJob : IJobEntity
{
    public bool IsWeightScanMode; public float DeltaTime; public float QuakeX; public float QuakeZ; public float BaseWeight; public float DynamicSensitivity;
    public void Execute(in LocalTransform transform, ref BlockStress stress, ref PhysicsVelocity velRW) { float additionalStress = 0f; if (IsWeightScanMode) { additionalStress = BaseWeight; } else { velRW.Linear += new float3(QuakeX, 0, QuakeZ) * DeltaTime; additionalStress = math.lengthsq(velRW.Linear) * DynamicSensitivity; } stress.TargetStress += additionalStress; }
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
    private float scanTimer; private bool isScanning; private bool needsColorUpdate; private bool isWeightScanMode;

    public void OnCreate(ref SystemState state) { state.RequireForUpdate<BlockStress>(); }
    public void OnDestroy(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        bool startScan = false;
        if (Input.GetKeyDown(KeyCode.V)) { isWeightScanMode = true; startScan = true; Debug.Log("⚖️ V키 측정 시작!"); }
        else if (Input.GetKeyDown(KeyCode.B)) { isWeightScanMode = false; startScan = true; Debug.Log("🌪️ B키 지진 시작!"); }

        if (startScan)
        {
            scanTimer = 5.0f; isScanning = true; needsColorUpdate = false;
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            foreach (var (color, gravity, velocity, stress, transform, entity) in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>, RefRW<PhysicsGravityFactor>, RefRW<PhysicsVelocity>, RefRW<BlockStress>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                color.ValueRW.Value = new float4(1f, 1f, 1f, 1f); gravity.ValueRW.Value = 1f; velocity.ValueRW.Linear.y -= 0.01f; stress.ValueRW.SmoothedStress = 0f;

                // ⭐ V키 엇갈림 완벽 복원: 다시 3.0 절대 스냅으로 복귀! (1.5 오차 보정 반영)
                float snappedX = math.round((transform.ValueRO.Position.x - 1.5f) / 3.0f) * 3.0f + 1.5f;
                float snappedY = math.round((transform.ValueRO.Position.y - 1.5f) / 3.0f) * 3.0f + 1.5f;
                float snappedZ = math.round((transform.ValueRO.Position.z - 1.5f) / 3.0f) * 3.0f + 1.5f;
                float3 perfectPos = new float3(snappedX, snappedY, snappedZ);

                if (!SystemAPI.HasComponent<OriginalPosition>(entity)) { ecb.AddComponent(entity, new OriginalPosition { Value = perfectPos }); }
            }
            ecb.Playback(state.EntityManager); ecb.Dispose();
        }

        if (isScanning)
        {
            scanTimer -= SystemAPI.Time.DeltaTime;
            if (scanTimer <= 0f)
            {
                isScanning = false; needsColorUpdate = true; Debug.Log("✅ 물리 엔진 정지! 위치 교정.");
                foreach (var (transform, velocity, gravity, originalPos) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRO<OriginalPosition>>())
                {
                    gravity.ValueRW.Value = 0f; velocity.ValueRW.Linear = float3.zero; velocity.ValueRW.Angular = float3.zero;
                    transform.ValueRW.Position = originalPos.ValueRO.Value; transform.ValueRW.Rotation = quaternion.identity;
                }
            }
        }

        if (!isScanning && !needsColorUpdate) return;

        if (isScanning)
        {
            state.Dependency = new ResetStressJob().ScheduleParallel(state.Dependency);
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true); var stressLookup = SystemAPI.GetComponentLookup<BlockStress>(false);
            var jointJob = new CalculateJointStressJob { TransformLookup = transformLookup, StressLookup = stressLookup, IsWeightScanMode = isWeightScanMode };
            state.Dependency = jointJob.Schedule(state.Dependency);
            float time = (float)SystemAPI.Time.ElapsedTime; float quakePower = 5.0f;
            var externalLoadJob = new ApplyExternalLoadJob { IsWeightScanMode = isWeightScanMode, DeltaTime = SystemAPI.Time.DeltaTime, QuakeX = !isWeightScanMode ? math.sin(time * 35f) * quakePower : 0f, QuakeZ = !isWeightScanMode ? math.cos(time * 28f) * quakePower : 0f, BaseWeight = 1.0f, DynamicSensitivity = 0.5f };
            state.Dependency = externalLoadJob.ScheduleParallel(state.Dependency);
            var smoothJob = new SmoothStressJob { DeltaTime = SystemAPI.Time.DeltaTime, SmoothSpeed = 3f };
            state.Dependency = smoothJob.ScheduleParallel(state.Dependency);
        }

        if (needsColorUpdate)
        {
            state.Dependency.Complete();
            string reportType = isWeightScanMode ? "WEIGHT" : "SHAKE"; string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string mainDir = Path.Combine(Application.dataPath, "StressBlock"); string allDir = Path.Combine(mainDir, "All"); string dangerDir = Path.Combine(mainDir, "danger");
            if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir); if (!Directory.Exists(allDir)) Directory.CreateDirectory(allDir); if (!Directory.Exists(dangerDir)) Directory.CreateDirectory(dangerDir);
            string currentStressPath = Path.Combine(mainDir, "CurrentStress.csv"); string allFilePath = Path.Combine(allDir, $"{reportType}_All_{timestamp}.csv"); string stressFilePath = Path.Combine(dangerDir, $"{reportType}_StressOnly_{timestamp}.csv");

            using (StreamWriter allWriter = new StreamWriter(allFilePath)) using (StreamWriter stressWriter = new StreamWriter(stressFilePath)) using (FileStream fs = new FileStream(currentStressPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) using (StreamWriter currentWriter = new StreamWriter(fs))
            {
                string header = $"BlockID,PosX,PosY,PosZ,{reportType}_Stress,RiskLevel,Prescription";
                allWriter.WriteLine(header); stressWriter.WriteLine(header); currentWriter.WriteLine(header);

                // ⭐ Query에 BlockHealth를 추가하여 블록의 진짜 방어력을 같이 가져옵니다!
                foreach (var (stress, color, originalPos, health) in SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<OriginalPosition>, RefRO<BlockHealth>>().WithAll<BlockTag>())
                {
                    float3 perfectPos = originalPos.ValueRO.Value;
                    float currentStress = stress.ValueRO.SmoothedStress;

                    // ⭐ 하드코딩된 값 대신 엑셀에서 가져온 해당 블록의 진짜 방어력(Defense) 사용!
                    float blockDefense = health.ValueRO.Defense;

                    // 방어력 대비 현재 스트레스의 비율을 계산 (0.0 ~ 1.0)
                    float t = math.clamp(currentStress / blockDefense, 0f, 1f);

                    if (isWeightScanMode) color.ValueRW.Value = new float4(1f, 1f - t, 1f - t, 1f); else color.ValueRW.Value = new float4(1f, 1f - t, 1f, 1f);
                    string risk = t >= 0.8f ? "DANGER" : (t >= 0.5f ? "WARNING" : "SAFE"); string prescription = risk == "DANGER" ? "Y" : (risk == "WARNING" ? "U" : "");

                    // ⭐ 1원칙: 스냅은 무조건 ID로 잡는다! (X_Z_Y 포맷 절대 유지)
                    // ⭐ 2원칙: 모든 엑셀 데이터는 Float(f)로 계산 및 기입!
                    float ix = math.round(perfectPos.x * 10f);
                    float iz = math.round(perfectPos.z * 10f);
                    float iy = math.round(perfectPos.y * 10f);

                    string strX = $"{(ix < 0f ? "-" : "0")}{math.abs(ix):000}";
                    string strZ = $"{(iz < 0f ? "-" : "0")}{math.abs(iz):000}";
                    string strY = $"{(iy < 0f ? "-" : "0")}{math.abs(iy):000}";

                    string blockID = $"{strX}_{strZ}_{strY}";
                    string lineData = $"{blockID},{perfectPos.x:F2},{perfectPos.y:F2},{perfectPos.z:F2},{currentStress:F2},{risk},{prescription}";

                    allWriter.WriteLine(lineData);
                    if (t >= 0.5f) stressWriter.WriteLine(lineData);
                    currentWriter.WriteLine(lineData);
                }
            }
            needsColorUpdate = false;
        }
    }
}
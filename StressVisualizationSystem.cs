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

    // ⭐ 블록 몸체에 박힌 스펙을 실시간으로 꺼내볼 수 있는 돋보기 장착!
    [ReadOnly] public ComponentLookup<BlockMaterial> MaterialLookup;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA; Entity entityB = pair.EntityB;
        if (TransformLookup.HasComponent(entityA) && TransformLookup.HasComponent(entityB) &&
            MaterialLookup.HasComponent(entityA) && MaterialLookup.HasComponent(entityB))
        {
            var transA = TransformLookup[entityA]; var transB = TransformLookup[entityB];
            var matA = MaterialLookup[entityA]; var matB = MaterialLookup[entityB];

            // ⭐ 양쪽 블록의 재질 정보를 융합해서 조인트(연결부)의 강성을 결정!
            bool isBrittle = matA.IsBrittle || matB.IsBrittle; // 둘 중 하나라도 콘크리트면 찢어짐에 취약함
            float tensile = (matA.TensileStiffness + matB.TensileStiffness) * 0.5f;
            float compressive = (matA.CompressiveStiffness + matB.CompressiveStiffness) * 0.5f;
            float shear = (matA.ShearStiffness + matB.ShearStiffness) * 0.5f;
            float bending = (matA.BendingStiffness + matB.BendingStiffness) * 0.5f;
            float torsion = (matA.TorsionStiffness + matB.TorsionStiffness) * 0.5f;

            float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
            float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);
            float3 localDelta = math.mul(math.inverse(transA.Rotation), (pivotA - pivotB));

            float axialDisplacement = localDelta.y;

            // ⭐ 엑셀에서 받아온 강성 수치 적용!
            float normalStress_Axial = axialDisplacement > 0 ? axialDisplacement * tensile : math.abs(axialDisplacement) * compressive;
            float shearStress_Linear = math.length(new float2(localDelta.x, localDelta.z)) * shear;

            quaternion relRot = math.mul(math.inverse(transA.Rotation), transB.Rotation);
            float w = math.clamp(relRot.value.w, -1f, 1f);
            float angle = 2.0f * math.acos(w);
            float sinHalfAngle = math.sqrt(1.0f - w * w);
            float3 axis = sinHalfAngle > 0.001f ? (relRot.value.xyz / sinHalfAngle) : new float3(0, 1, 0);
            float3 eulerVector = axis * angle;

            float normalStress_Bending = math.length(new float2(eulerVector.x, eulerVector.z)) * bending;
            float shearStress_Torsion = math.abs(eulerVector.y) * torsion;

            float sigma = normalStress_Axial + normalStress_Bending;
            float tau = shearStress_Linear + shearStress_Torsion;

            float finalStress = 0f;

            // ⭐ 폰 미제스(강철) / 랭킨(콘크리트) 투트랙 공식 발동!
            if (isBrittle)
            {
                float principalStress1 = (sigma / 2f) + math.sqrt((sigma * sigma / 4f) + (tau * tau));
                if (principalStress1 > 0 && axialDisplacement > 0) finalStress = principalStress1 * 2.0f;
                else finalStress = math.sqrt((sigma * sigma) + 3.0f * (tau * tau));
            }
            else
            {
                finalStress = math.sqrt((sigma * sigma) + 3.0f * (tau * tau));
            }

            if (StressLookup.HasComponent(entityA)) { var stressA = StressLookup[entityA]; stressA.TargetStress += finalStress; StressLookup[entityA] = stressA; }
            if (StressLookup.HasComponent(entityB)) { var stressB = StressLookup[entityB]; stressB.TargetStress += finalStress; StressLookup[entityB] = stressB; }
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
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var stressLookup = SystemAPI.GetComponentLookup<BlockStress>(false);
            var materialLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true); // ⭐ 돋보기 장착!

            var jointJob = new CalculateJointStressJob
            {
                TransformLookup = transformLookup,
                StressLookup = stressLookup,
                MaterialLookup = materialLookup // ⭐ Job에 재질 돋보기 넘겨줌
            };
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
            float yieldLimit = isWeightScanMode ? 5.0f : 8.0f; string reportType = isWeightScanMode ? "WEIGHT" : "SHAKE";
            string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string mainDir = Path.Combine(Application.dataPath, "StressBlock"); string allDir = Path.Combine(mainDir, "All"); string dangerDir = Path.Combine(mainDir, "danger");
            if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir); if (!Directory.Exists(allDir)) Directory.CreateDirectory(allDir); if (!Directory.Exists(dangerDir)) Directory.CreateDirectory(dangerDir);

            string currentStressPath = Path.Combine(mainDir, "CurrentStress.csv");
            string allFilePath = Path.Combine(allDir, $"{reportType}_All_{dateStamp}.csv");
            string stressFilePath = Path.Combine(dangerDir, $"{reportType}_StressOnly_{dateStamp}.csv");

            // ⭐ 기존 CSV의 BlueprintManager 파싱이 고장나지 않도록, 'Material'을 8번째 맨 끝 칸에 추가합니다!
            string header = $"BlockID,PosX,PosY,PosZ,{reportType}_Stress,RiskLevel,Prescription,Material";

            using (StreamWriter allWriter = new StreamWriter(allFilePath, false))
            using (StreamWriter stressWriter = new StreamWriter(stressFilePath, false))
            using (StreamWriter currentWriter = new StreamWriter(currentStressPath, false))
            {
                allWriter.WriteLine(header);
                stressWriter.WriteLine(header);
                currentWriter.WriteLine(header);

                // ⭐ 엑셀에 쓸 재질(MaterialName)을 같이 읽어옵니다.
                foreach (var (stress, color, originalPos, mat) in SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<OriginalPosition>, RefRO<BlockMaterial>>())
                {
                    float3 perfectPos = originalPos.ValueRO.Value;
                    float currentStress = stress.ValueRO.SmoothedStress; float t = math.clamp(currentStress / yieldLimit, 0f, 1f);
                    if (isWeightScanMode) color.ValueRW.Value = new float4(1f, 1f - t, 1f - t, 1f); else color.ValueRW.Value = new float4(1f, 1f - t, 1f, 1f);
                    string risk = t >= 0.8f ? "DANGER" : (t >= 0.5f ? "WARNING" : "SAFE"); string prescription = risk == "DANGER" ? "Y" : (risk == "WARNING" ? "U" : "");

                    float ix = math.round(perfectPos.x * 10f); float iz = math.round(perfectPos.z * 10f); float iy = math.round(perfectPos.y * 10f);
                    string strX = $"{(ix < 0 ? "-" : "0")}{math.abs(ix):000}"; string strZ = $"{(iz < 0 ? "-" : "0")}{math.abs(iz):000}"; string strY = $"{(iy < 0 ? "-" : "0")}{math.abs(iy):000}";

                    string blockID = $"{strX}_{strZ}_{strY}";

                    // ⭐ 맨 끝에 재질 이름(mat.ValueRO.MaterialName) 기록!
                    string lineData = $"{blockID},{perfectPos.x:F2},{perfectPos.y:F2},{perfectPos.z:F2},{currentStress:F2},{risk},{prescription},{mat.ValueRO.MaterialName}";

                    allWriter.WriteLine(lineData);
                    if (t >= 0.5f) stressWriter.WriteLine(lineData);
                    currentWriter.WriteLine(lineData);
                }
            }
            needsColorUpdate = false;
        }
    }
}
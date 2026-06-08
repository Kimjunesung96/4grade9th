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
    // 읽기 전용 돋보기들
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    [ReadOnly] public ComponentLookup<BlockMaterial> MaterialLookup;
    // 쓰기 가능 돋보기 (스트레스 누적용)
    public ComponentLookup<BlockStress> StressLookup;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA;
        Entity entityB = pair.EntityB;

        // 1. 엔티티 존재 여부와 컴포넌트 보유 여부를 더 꼼꼼하게 체크
        if (!TransformLookup.HasComponent(entityA) || !TransformLookup.HasComponent(entityB)) return;
        if (!MaterialLookup.HasComponent(entityA) || !MaterialLookup.HasComponent(entityB)) return;
        if (!StressLookup.HasComponent(entityA) || !StressLookup.HasComponent(entityB)) return;

        var transA = TransformLookup[entityA];
        var transB = TransformLookup[entityB];
        var matA = MaterialLookup[entityA];
        var matB = MaterialLookup[entityB];

        // 2. 물리 스펙 융합 (모든 수치는 float)
        bool isBrittle = matA.IsBrittle || matB.IsBrittle;
        float tensile = (matA.TensileStiffness + matB.TensileStiffness) * 0.5f;
        float compressive = (matA.CompressiveStiffness + matB.CompressiveStiffness) * 0.5f;
        float shear = (matA.ShearStiffness + matB.ShearStiffness) * 0.5f;
        float bending = (matA.BendingStiffness + matB.BendingStiffness) * 0.5f;
        float torsion = (matA.TorsionStiffness + matB.TorsionStiffness) * 0.5f;

        // 3. 변위 계산
        float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
        float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);
        float3 localDelta = math.mul(math.inverse(transA.Rotation), (pivotA - pivotB));

        float axialDisplacement = localDelta.y;
        float normalStress_Axial = axialDisplacement > 0.0f ? axialDisplacement * tensile : math.abs(axialDisplacement) * compressive;
        float shearStress_Linear = math.length(new float2(localDelta.x, localDelta.z)) * shear;

        // 4. 회전 변위 계산
        quaternion relRot = math.mul(math.inverse(transA.Rotation), transB.Rotation);
        float w = math.clamp(relRot.value.w, -1.0f, 1.0f);
        float angle = 2.0f * math.acos(w);
        float sinHalfAngle = math.sqrt(1.0f - w * w);
        float3 axis = sinHalfAngle > 0.001f ? (relRot.value.xyz / sinHalfAngle) : new float3(0.0f, 1.0f, 0.0f);
        float3 eulerVector = axis * angle;

        float normalStress_Bending = math.length(new float2(eulerVector.x, eulerVector.z)) * bending;
        float shearStress_Torsion = math.abs(eulerVector.y) * torsion;

        float sigma = normalStress_Axial + normalStress_Bending;
        float tau = shearStress_Linear + shearStress_Torsion;

        // 5. 최종 스트레스 산출 (폰 미제스 / 랭킨)
        float finalStress = 0.0f;
        if (isBrittle)
        {
            float principalStress1 = (sigma / 2.0f) + math.sqrt((sigma * sigma / 4.0f) + (tau * tau));
            finalStress = (principalStress1 > 0.0f && axialDisplacement > 0.0f) ? principalStress1 * 2.0f : math.sqrt((sigma * sigma) + 3.0f * (tau * tau));
        }
        else
        {
            finalStress = math.sqrt((sigma * sigma) + 3.0f * (tau * tau));
        }

        // 6. 결과 기록
        var sA = StressLookup[entityA]; sA.TargetStress += finalStress; StressLookup[entityA] = sA;
        var sB = StressLookup[entityB]; sB.TargetStress += finalStress; StressLookup[entityB] = sB;
    }
}

[BurstCompile]
public partial struct ApplyExternalLoadJob : IJobEntity
{
    public bool IsWeightScanMode;
    public float DeltaTime;
    public float QuakeX;
    public float QuakeZ;
    public float BaseWeight;
    public float DynamicSensitivity;

    // ⭐ 질량 정보를 읽어오기 위해 추가
    public void Execute(in LocalTransform transform, in PhysicsMass mass, ref BlockStress stress, ref PhysicsVelocity velRW)
    {
        float additionalStress = 0.0f;

        if (IsWeightScanMode)
        {
            // ⭐ 단순히 1.0f를 더하는 게 아니라, 블록의 실제 질량을 고려!
            // InverseMass가 0이면(고정 블록) 기본값 1.0f 적용, 아니면 실제 질량 적용
            float realMass = mass.InverseMass > 0.0f ? (1.0f / mass.InverseMass) : 1.0f;
            additionalStress = BaseWeight * realMass * 0.1f; // 0.1f는 수치 조정용 계수
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
    public float DeltaTime; public float SmoothSpeed;
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
    private float scanTimer; private bool isScanning; private bool needsColorUpdate; private bool isWeightScanMode;

    public void OnCreate(ref SystemState state) { state.RequireForUpdate<BlockStress>(); }

    public void OnUpdate(ref SystemState state)
    {
        if (Input.GetKeyDown(KeyCode.V)) { isWeightScanMode = true; StartScan(ref state); }
        else if (Input.GetKeyDown(KeyCode.B)) { isWeightScanMode = false; StartScan(ref state); }

        if (isScanning)
        {
            scanTimer -= SystemAPI.Time.DeltaTime;
            if (scanTimer <= 0.0f)
            {
                isScanning = false; needsColorUpdate = true;
                StopPhysics(ref state);
            }

            // Job 실행
            state.Dependency = new ResetStressJob().ScheduleParallel(state.Dependency);

            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var stressLookup = SystemAPI.GetComponentLookup<BlockStress>(false);
            var materialLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true);

            state.Dependency = new CalculateJointStressJob
            {
                TransformLookup = transformLookup,
                StressLookup = stressLookup,
                MaterialLookup = materialLookup
            }.Schedule(state.Dependency);

            float time = (float)SystemAPI.Time.ElapsedTime;
            state.Dependency = new ApplyExternalLoadJob
            {
                IsWeightScanMode = isWeightScanMode,
                DeltaTime = SystemAPI.Time.DeltaTime,
                QuakeX = !isWeightScanMode ? math.sin(time * 35.0f) * 5.0f : 0.0f,
                QuakeZ = !isWeightScanMode ? math.cos(time * 28.0f) * 5.0f : 0.0f,
                BaseWeight = 1.0f,
                DynamicSensitivity = 0.5f
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
        float yieldLimit = isWeightScanMode ? 5.0f : 8.0f;
        string currentStressPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        using (StreamWriter writer = new StreamWriter(currentStressPath, false))
        {
            writer.WriteLine("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material");
            foreach (var (stress, color, originalPos, mat) in SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<OriginalPosition>, RefRO<BlockMaterial>>())
            {
                float3 p = originalPos.ValueRO.Value;
                float curStress = stress.ValueRO.SmoothedStress;
                float t = math.clamp(curStress / yieldLimit, 0.0f, 1.0f);
                color.ValueRW.Value = isWeightScanMode ? new float4(1.0f, 1.0f - t, 1.0f - t, 1.0f) : new float4(1.0f, 1.0f - t, 1.0f, 1.0f);

                string risk = t >= 0.8f ? "DANGER" : (t >= 0.5f ? "WARNING" : "SAFE");
                string id = $"{(int)math.round(p.x * 10.0f)}_{(int)math.round(p.z * 10.0f)}_{(int)math.round(p.y * 10.0f)}";
                writer.WriteLine($"{id},{p.x:F2},{p.y:F2},{p.z:F2},{curStress:F2},{risk},{(risk == "DANGER" ? "Y" : "")},{mat.ValueRO.MaterialName}");
            }
        }
        needsColorUpdate = false;
        Debug.Log("📊 스트레스 분석 리포트 생성 완료!");
    }
}
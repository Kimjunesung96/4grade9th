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

// ⭐ 십장님 특명! 찌그러지기 전의 '완벽했던 원래 위치'를 기억할 꼬리표
public struct OriginalPosition : IComponentData
{
    public float3 Value;
}

// ⭐ 스트레스 초기화를 위한 초경량 Job
[BurstCompile]
public partial struct ResetStressJob : IJobEntity
{
    public void Execute(ref BlockStress stress)
    {
        stress.TargetStress = 0f;
    }
}

// ⭐ 조인트 응력을 멀티코어로 미친듯이 빠르게 계산하는 Job
[BurstCompile]
public partial struct CalculateJointStressJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<BlockStress> StressLookup;
    public bool IsWeightScanMode;

    public void Execute(in PhysicsConstrainedBodyPair pair, in PhysicsJoint joint)
    {
        Entity entityA = pair.EntityA;
        Entity entityB = pair.EntityB;

        if (TransformLookup.HasComponent(entityA) && TransformLookup.HasComponent(entityB))
        {
            var transA = TransformLookup[entityA];
            var transB = TransformLookup[entityB];

            float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
            float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);
            float3 deltaDisplacement = pivotA - pivotB;

            float jointStressResult = 0f;

            if (IsWeightScanMode)
            {
                float axialStiffness = 15.0f;
                jointStressResult = math.abs(deltaDisplacement.y) * axialStiffness;
            }
            else
            {
                float shearStiffness = 10.0f;
                float bendingStiffness = 15.0f;
                float dotProduct = math.abs(math.dot(transA.Rotation.value, transB.Rotation.value));
                float angleDiff = 2.0f * math.acos(math.clamp(dotProduct, -1f, 1f));
                float bendingStress = angleDiff * bendingStiffness;
                float shearStress = math.length(new float2(deltaDisplacement.x, deltaDisplacement.z)) * shearStiffness;

                jointStressResult = bendingStress + shearStress;
            }

            if (StressLookup.HasComponent(entityA))
            {
                var stressA = StressLookup[entityA];
                stressA.TargetStress += jointStressResult;
                StressLookup[entityA] = stressA;
            }
            if (StressLookup.HasComponent(entityB))
            {
                var stressB = StressLookup[entityB];
                stressB.TargetStress += jointStressResult;
                StressLookup[entityB] = stressB;
            }
        }
    }
}

// ⭐ 외부 하중 및 지진을 멀티코어로 적용하는 Job
[BurstCompile]
public partial struct ApplyExternalLoadJob : IJobEntity
{
    public bool IsWeightScanMode;
    public float DeltaTime;
    public float QuakeX;
    public float QuakeZ;
    public float BaseWeight;
    public float DynamicSensitivity;

    public void Execute(in LocalTransform transform, ref BlockStress stress, ref PhysicsVelocity velRW)
    {
        float additionalStress = 0f;

        if (IsWeightScanMode)
        {
            additionalStress = BaseWeight;
        }
        else
        {
            velRW.Linear += new float3(QuakeX, 0, QuakeZ) * DeltaTime;
            additionalStress = math.lengthsq(velRW.Linear) * DynamicSensitivity;
        }
        stress.TargetStress += additionalStress;
    }
}

// ⭐ 십장님 특명 반영: 최대치(Max) 스트레스 영구 기록 Job
[BurstCompile]
public partial struct SmoothStressJob : IJobEntity
{
    public float DeltaTime;
    public float SmoothSpeed;

    public void Execute(ref BlockStress stress)
    {
        float currentSmoothed = math.lerp(stress.SmoothedStress, stress.TargetStress, DeltaTime * SmoothSpeed);

        // 🚨 핵심: 5초 동안 흔들리면서 겪은 '가장 높은 스트레스 값(Max)'을 갱신하고 줄어들지 않게 고정합니다!
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

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BlockStress>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // =========================================================
        // 1. 입력 감지부 (V / B)
        // =========================================================
        bool startScan = false;

        if (Input.GetKeyDown(KeyCode.V))
        {
            isWeightScanMode = true;
            startScan = true;
            Debug.Log("⚖️ [무게 진단] 5초간 실제 중력을 가동하여 최대 비틀림을 측정합니다!");
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            isWeightScanMode = false;
            startScan = true;
            Debug.Log("🌪️ [진동대 시험] 5초간 인공 지진을 가동하여 최대 비틀림을 측정합니다!");
        }

        if (startScan)
        {
            scanTimer = 5.0f;
            isScanning = true;
            needsColorUpdate = false;

            // 🚨 구조적 변경을 안전하게 처리하기 위한 버퍼 생성
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 🚨 스캔 시작 시: 색상 초기화 + 중력 ON + 발로 차서 깨우기 + 스트레스 기록 초기화 + 원본 위치 스냅샷!
            foreach (var (color, gravity, velocity, stress, transform, entity) in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>, RefRW<PhysicsGravityFactor>, RefRW<PhysicsVelocity>, RefRW<BlockStress>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                color.ValueRW.Value = new float4(1f, 1f, 1f, 1f);
                gravity.ValueRW.Value = 1f;               // 중력 켜기
                velocity.ValueRW.Linear.y -= 0.01f;       // 잠든 물리엔진 깨우기
                stress.ValueRW.SmoothedStress = 0f;       // Max 기록기 0으로 리셋

                // ⭐ [스냅샷] 건물이 무너지기 전 완벽한 위치를 저장합니다!
                if (!SystemAPI.HasComponent<OriginalPosition>(entity))
                {
                    ecb.AddComponent(entity, new OriginalPosition { Value = transform.ValueRO.Position });
                }
                else
                {
                    ecb.SetComponent(entity, new OriginalPosition { Value = transform.ValueRO.Position });
                }
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        // =========================================================
        // 2. 타이머 체크 & 5초 땡! (타임 스톱 및 칼각 복구)
        // =========================================================
        if (isScanning)
        {
            scanTimer -= SystemAPI.Time.DeltaTime;
            if (scanTimer <= 0f)
            {
                isScanning = false;
                needsColorUpdate = true;
                Debug.Log("✅ [스캔 완료] 물리 엔진 정지! 뼈대를 원래 위치로 교정합니다.");

                // 🚨 십장님 특명: 저장해둔 완벽한 원본 위치(OriginalPosition)로 복구!!
                foreach (var (transform, velocity, gravity, originalPos) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRO<OriginalPosition>>())
                {
                    // 중력 및 관성 100% 제거 (얼음!)
                    gravity.ValueRW.Value = 0f;
                    velocity.ValueRW.Linear = float3.zero;
                    velocity.ValueRW.Angular = float3.zero;

                    // 찌그러진 현재 위치 말고, 테스트 시작 전 완벽했던 원래 위치를 가져옵니다!
                    float3 pos = originalPos.ValueRO.Value;

                    int snappedX = (int)math.round((pos.x - 1.5f) / 3.0f) * 30 + 15;
                    int snappedY = (int)math.round((pos.y - 1.5f) / 3.0f) * 30 + 15;
                    int snappedZ = (int)math.round((pos.z - 1.5f) / 3.0f) * 30 + 15;
                    snappedY = math.max(15, snappedY);

                    // 원래 도면 자리로 반듯하게 꽂아 넣습니다.
                    transform.ValueRW.Position = new float3(snappedX / 10f, snappedY / 10f, snappedZ / 10f);
                    transform.ValueRW.Rotation = quaternion.identity;
                }
            }
        }

        if (!isScanning && !needsColorUpdate) return;

        // =========================================================
        // ⚙️ [정밀 해석 모드] 멀티코어 Job System 가동! (5초 동안만 돎)
        // =========================================================
        if (isScanning)
        {
            state.Dependency = new ResetStressJob().ScheduleParallel(state.Dependency);

            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var stressLookup = SystemAPI.GetComponentLookup<BlockStress>(false);

            var jointJob = new CalculateJointStressJob
            {
                TransformLookup = transformLookup,
                StressLookup = stressLookup,
                IsWeightScanMode = isWeightScanMode
            };
            state.Dependency = jointJob.Schedule(state.Dependency);

            float time = (float)SystemAPI.Time.ElapsedTime;
            float quakePower = 5.0f;

            var externalLoadJob = new ApplyExternalLoadJob
            {
                IsWeightScanMode = isWeightScanMode,
                DeltaTime = SystemAPI.Time.DeltaTime,
                QuakeX = !isWeightScanMode ? math.sin(time * 35f) * quakePower : 0f,
                QuakeZ = !isWeightScanMode ? math.cos(time * 28f) * quakePower : 0f,
                BaseWeight = 1.0f,
                DynamicSensitivity = 0.5f
            };
            state.Dependency = externalLoadJob.ScheduleParallel(state.Dependency);

            var smoothJob = new SmoothStressJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                SmoothSpeed = 3f
            };
            state.Dependency = smoothJob.ScheduleParallel(state.Dependency);
        }

        // =========================================================
        // 🎨 [진단 완료] 최고점(Max) 색상 부여 & 엑셀 추출
        // =========================================================
        if (needsColorUpdate)
        {
            state.Dependency.Complete();

            float yieldLimit = isWeightScanMode ? 5.0f : 8.0f;
            string reportType = isWeightScanMode ? "WEIGHT" : "SHAKE";
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string mainDir = Path.Combine(Application.dataPath, "StressBlock");
            string allDir = Path.Combine(mainDir, "All");
            string dangerDir = Path.Combine(mainDir, "danger");

            if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir);
            if (!Directory.Exists(allDir)) Directory.CreateDirectory(allDir);
            if (!Directory.Exists(dangerDir)) Directory.CreateDirectory(dangerDir);

            string currentStressPath = Path.Combine(mainDir, "CurrentStress.csv");
            string allFilePath = Path.Combine(allDir, $"{reportType}_All_{timestamp}.csv");
            string stressFilePath = Path.Combine(dangerDir, $"{reportType}_StressOnly_{timestamp}.csv");

            using (StreamWriter allWriter = new StreamWriter(allFilePath))
            using (StreamWriter stressWriter = new StreamWriter(stressFilePath))
            using (FileStream fs = new FileStream(currentStressPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter currentWriter = new StreamWriter(fs))
            {
                string header = $"BlockID,PosX,PosY,PosZ,{reportType}_Stress,RiskLevel,Prescription";
                allWriter.WriteLine(header);
                stressWriter.WriteLine(header);
                currentWriter.WriteLine(header);

                // 🚨 찌그러진 현재 위치(transform)가 아니라 원본 위치(OriginalPosition)를 기반으로 엑셀을 작성합니다!
                // 🚨 찌그러진 현재 위치(transform)와 원본 위치(OriginalPosition)를 모두 가져옵니다!
                foreach (var (stress, color, transform, originalPos) in SystemAPI.Query<RefRO<BlockStress>, RefRW<URPMaterialPropertyBaseColor>, RefRO<LocalTransform>, RefRO<OriginalPosition>>())
                {
                    float3 finalPos = transform.ValueRO.Position; // 💥 마지막에 찌그러진 실제 위치 (엑셀 Pos 기록용)
                    float3 initialPos = originalPos.ValueRO.Value; // 🏗️ 5초 전 멀쩡했던 도면 위치 (ID 생성용)

                    // 🌟 5초 동안 갱신된 '최대치' 스트레스를 가져옵니다!
                    float currentStress = stress.ValueRO.SmoothedStress;
                    float t = math.clamp(currentStress / yieldLimit, 0f, 1f);

                    // 색상 고정 박제!
                    if (isWeightScanMode) color.ValueRW.Value = new float4(1f, 1f - t, 1f - t, 1f);
                    else color.ValueRW.Value = new float4(1f, 1f - t, 1f, 1f);

                    string risk = t >= 0.8f ? "DANGER" : (t >= 0.5f ? "WARNING" : "SAFE");
                    string prescription = risk == "DANGER" ? "Y" : (risk == "WARNING" ? "U" : "");

                    // 🌟 완벽한 원본 위치(initialPos)를 바탕으로 ID 생성 연동! (보강 타설 시 렉/겹침 완벽 방지)
                    int snappedX = (int)math.round((initialPos.x - 1.5f) / 3.0f) * 30 + 15;
                    int snappedY = (int)math.round((initialPos.y - 1.5f) / 3.0f) * 30 + 15;
                    int snappedZ = (int)math.round((initialPos.z - 1.5f) / 3.0f) * 30 + 15;
                    snappedY = math.max(15, snappedY);
                    string blockID = $"{snappedX:0000}_{snappedZ:0000}_{snappedY:0000}";

                    // 🚨 엑셀의 PosX, PosY, PosZ에는 지진을 맞고 찌그러진 '최종 위치(finalPos)'를 기록합니다!
                    string lineData = $"{blockID},{finalPos.x:F2},{finalPos.y:F2},{finalPos.z:F2},{currentStress:F2},{risk},{prescription}";

                    // All 파일: 전체 저장
                    allWriter.WriteLine(lineData);

                    // WARNING 이상만 stressWriter에 저장
                    if (t >= 0.5f)
                        stressWriter.WriteLine(lineData);

                    // 🚨 CurrentStress: SAFE 포함 전체 블록 저장 (보강 건물 재건용)
                    currentWriter.WriteLine(lineData);
                }
            }
        }
    }
}
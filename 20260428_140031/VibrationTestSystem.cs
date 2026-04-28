using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Rendering;
using Unity.Collections;
using System.IO;
using System;

// ⭐ 블록의 원래 위치와 '최대 흔들림(Max Displacement)'을 기억하는 센서
public struct VibrationTracker : IComponentData
{
    public float3 OriginalPos;
    public quaternion OriginalRot;
    public float MaxDisplacement;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct VibrationTestSystem : ISystem
{
    // ⭐ 스포너에게 지금 세팅 중인지 알려주는 글로벌 전광판 스위치!
    public static bool IsBModeActive = false;

    private bool isBMode;
    private int vibeLevel; // 진도 1 ~ 8단계
    private float actualVibePower; // 실제 적용되는 물리적 힘 (2배씩 증가)

    private bool isVibrating;
    private float vibeTimer;
    private const float MAX_VIBE_TIME = 5.0f; // ⭐ 소장님 지시: 정확히 5초간 지진 발생!

    public void OnCreate(ref SystemState state)
    {
        isBMode = false;
        IsBModeActive = false;

        vibeLevel = 1;
        actualVibePower = 1f;

        isVibrating = false;
        vibeTimer = 0f;
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        // =========================================================
        // 1. [B키] 세팅 모드 진입 (안전핀 해제)
        // =========================================================
        if (Input.GetKeyDown(KeyCode.B) && !isVibrating)
        {
            isBMode = !isBMode;
            IsBModeActive = isBMode; // 스포너에게 "대기해!" 라고 알림

            if (isBMode)
            {
                Debug.Log($"🚨 [지진 세팅 모드] B모드 켜짐! 마우스 휠로 진도(1~8)를 조절하고 G를 눌러 격발하세요! (현재 진도: {vibeLevel}단계)");

                // ⭐ B 누를 때마다 현장 세차 (이전 테스트 색상 하얗게 초기화)
                foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
                {
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }
            else
            {
                Debug.Log("✅ [지진 세팅 모드] B모드 취소.");
            }
        }

        // =========================================================
        // 2. [마우스 휠] 진도 1~8단계 조절 (2배씩 파워업!)
        // =========================================================
        if (isBMode && Input.mouseScrollDelta.y != 0)
        {
            int scrollDir = (int)math.sign(Input.mouseScrollDelta.y);
            vibeLevel += scrollDir;

            // 진도는 1에서 8까지만 막아둠
            vibeLevel = math.clamp(vibeLevel, 1, 8);

            // 2의 (vibeLevel - 1) 제곱으로 힘 계산 (1, 2, 4, 8, 16, 32, 64, 128)
            actualVibePower = math.pow(2f, vibeLevel - 1);

            Debug.Log($"🌍 [진도 설정] 레벨 {vibeLevel} / 파워: {actualVibePower}배");
        }

        // =========================================================
        // 3. [G키] 격발! (지진 시작 & B모드 종료)
        // =========================================================
        if (isBMode && Input.GetKeyDown(KeyCode.G) && !isVibrating)
        {
            isBMode = false;
            IsBModeActive = false; // G 누르는 순간 B모드 꺼짐! (스포너 봉인 해제)

            isVibrating = true;
            vibeTimer = MAX_VIBE_TIME; // 5.0초 장전!
            Debug.Log($"💥 [격발!] 진도 {vibeLevel} (파워 {actualVibePower}) 강진 발생!! 5초간 흔들립니다!");

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ⭐ 여기서 PhysicsMass를 같이 불러와서 1층 녀석들을 굳혀버립니다!
            foreach (var (transform, mass, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>>().WithAll<BlockTag>().WithEntityAccess())
            {
                // 🚀 소장님 특명: Y좌표가 3.1 이하(1층 바닥)인 블록은 절대 움직이지 않게 땅에 앙카(Anchor) 고정!
                if (transform.ValueRO.Position.y <= 3.1f)
                {
                    mass.ValueRW.InverseMass = 0f;
                    mass.ValueRW.InverseInertia = float3.zero;
                }

                ecb.AddComponent(entity, new VibrationTracker
                {
                    OriginalPos = transform.ValueRO.Position,
                    OriginalRot = transform.ValueRO.Rotation,
                    MaxDisplacement = 0f
                });
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        // =========================================================
        // 4. 지진 진행 중 (5초간 흔들림 & 최대 변위 측정)
        // =========================================================
        if (isVibrating)
        {
            vibeTimer -= SystemAPI.Time.DeltaTime;
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(vibeTimer * 1000f + 1));

            // ⭐ 여기에 color 파라미터를 추가해서 실시간 색상 강제 방어막을 칩니다!
            foreach (var (transform, tracker, velocity, mass, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
            {
                // 최대 거리 측정
                float currentDist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (currentDist > tracker.ValueRO.MaxDisplacement)
                {
                    tracker.ValueRW.MaxDisplacement = currentDist;
                }

                // 5초가 아직 안 끝났다면 계속 흔들기
                if (vibeTimer > 0f)
                {
                    if (mass.ValueRO.InverseMass > 0)
                    {
                        float3 shakeForce = random.NextFloat3Direction() * actualVibePower * 3.0f;
                        shakeForce.y *= 0.3f; // 위아래보단 좌우로 크게 요동치게
                        velocity.ValueRW.Linear += shakeForce * SystemAPI.Time.DeltaTime;
                    }

                    // ⭐ 방어막: 다른 실시간 측정 스크립트가 색깔을 못 바꾸게 강제로 하얀색 고정!
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }

            // =========================================================
            // 5. 5초 경과 후 종료 (원위치, 운동량 0, 결과 색칠, 엑셀 저장)
            // =========================================================
            if (vibeTimer <= 0f)
            {
                isVibrating = false;
                Debug.Log("🛑 [지진 종료] 블록 원위치 복구, 최종 진단 결과 도색 중...");

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPositions = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);

                foreach (var (transform, tracker, velocity, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    // 1. 완벽한 원위치 복구
                    transform.ValueRW.Position = tracker.ValueRO.OriginalPos;
                    transform.ValueRW.Rotation = tracker.ValueRO.OriginalRot;

                    // 2. 운동량(관성) 영구 제거
                    velocity.ValueRW.Linear = float3.zero;
                    velocity.ValueRW.Angular = float3.zero;

                    // ⭐ 3. 5초가 다 끝난 지금 이 순간에만! 최대 흔들림 수치에 따른 최종 도색!
                    float maxDisp = tracker.ValueRO.MaxDisplacement;
                    float4 newColor = new float4(1, 1, 1, 1);

                    if (maxDisp >= 2.0f) newColor = new float4(1, 0, 0, 1); // 2.0 이상: 위험 (빨강)
                    else if (maxDisp >= 0.5f) newColor = new float4(1, 1, 0, 1); // 0.5 이상: 경고 (노랑)

                    color.ValueRW.Value = newColor;

                    finalPositions.Add(tracker.ValueRO.OriginalPos);
                    finalStresses.Add(maxDisp);

                    ecb.RemoveComponent<VibrationTracker>(entity); // 검사 끝났으니 센서 뗌
                }

                // 4. 이중 엑셀 저장 실행!
                SaveVibrationExcel(finalPositions, finalStresses);

                finalPositions.Dispose();
                finalStresses.Dispose();
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
        }
    }

    // =========================================================
    // 💾 엑셀 자동 폴더 생성 및 이중 저장 시스템
    // =========================================================
    private void SaveVibrationExcel(NativeList<float3> positions, NativeList<float> stresses)
    {
        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string vibeDir = Path.Combine(Application.dataPath, "StressBlock", "vibe");
        if (!Directory.Exists(vibeDir))
        {
            Directory.CreateDirectory(vibeDir);
            Debug.Log($"📁 [폴더 생성] {vibeDir} 폴더를 새로 만들었습니다!");
        }

        string historyPath = Path.Combine(vibeDir, $"Vibration_All_{timeStamp}.csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
        lines.Add("BlockID,PosX,PosY,PosZ,WEIGHT_Stress,RiskLevel,Prescription");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i];
            float stress = stresses[i];

            int ix = (int)math.round(pos.x * 10f);
            int iy = (int)math.round(pos.y * 10f);
            int iz = (int)math.round(pos.z * 10f);

            string signX = ix < 0 ? "-" : "0";
            string signZ = iz < 0 ? "-" : "0";
            string signY = iy < 0 ? "-" : "0";

            string id = $"{signX}{math.abs(ix):000}_{signZ}{math.abs(iz):000}_{signY}{math.abs(iy):000}";

            string risk = "Safe";
            string pres = "N";
            if (stress >= 2.0f) { risk = "Danger"; pres = "Y"; }
            else if (stress >= 0.5f) { risk = "Warning"; pres = "N"; }

            lines.Add($"{id},{pos.x:F2},{pos.y:F2},{pos.z:F2},{stress:F2},{risk},{pres}");
        }

        File.WriteAllLines(historyPath, lines);
        File.WriteAllLines(currentPath, lines);

        Debug.Log($"📄 [엑셀 저장 완료] 지진 내진 테스트 결과가 성공적으로 저장되었습니다!\n1. 히스토리: {historyPath}\n2. 현재 도면: {currentPath}\n(이제 Y키를 눌러 보강 도면을 생성하세요!)");
    }
}
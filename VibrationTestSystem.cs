using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Rendering;
using Unity.Collections;
using System.IO;
using System;

public struct VibrationTracker : IComponentData
{
    public float3 OriginalPos;
    public quaternion OriginalRot;
    public float MaxDisplacement;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct VibrationTestSystem : ISystem
{
    public static bool IsBModeActive = false;

    private bool isBMode;
    private int vibeLevel;
    private float actualVibePower;

    private bool isVibrating;
    private float vibeTimer;
    private const float MAX_VIBE_TIME = 5.0f;

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

        if (Input.GetKeyDown(KeyCode.B) && !isVibrating)
        {
            isBMode = !isBMode;
            IsBModeActive = isBMode;

            if (isBMode)
            {
                Debug.Log($"🚨 [지진 세팅 모드] B모드 켜짐! 마우스 휠로 진도(1~8)를 조절하고 G를 눌러 격발하세요! (현재 진도: {vibeLevel}단계)");
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

        if (isBMode && Input.mouseScrollDelta.y != 0)
        {
            int scrollDir = (int)math.sign(Input.mouseScrollDelta.y);
            vibeLevel += scrollDir;
            vibeLevel = math.clamp(vibeLevel, 1, 8);
            actualVibePower = math.pow(2f, vibeLevel - 1);
            Debug.Log($"🌍 [진도 설정] 레벨 {vibeLevel} / 파워: {actualVibePower}배");
        }

        if (isBMode && Input.GetKeyDown(KeyCode.G) && !isVibrating)
        {
            isBMode = false;
            IsBModeActive = false;

            isVibrating = true;
            vibeTimer = MAX_VIBE_TIME;
            Debug.Log($"💥 [격발!] 진도 {vibeLevel} (파워 {actualVibePower}) 강진 발생!! 5초간 흔들립니다!");

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (transform, mass, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>>().WithAll<BlockTag>().WithEntityAccess())
            {
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

        if (isVibrating)
        {
            vibeTimer -= SystemAPI.Time.DeltaTime;
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(vibeTimer * 1000f + 1));

            foreach (var (transform, tracker, velocity, mass, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
            {
                float currentDist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (currentDist > tracker.ValueRO.MaxDisplacement)
                {
                    tracker.ValueRW.MaxDisplacement = currentDist;
                }

                if (vibeTimer > 0f)
                {
                    if (mass.ValueRO.InverseMass > 0)
                    {
                        float3 shakeForce = random.NextFloat3Direction() * actualVibePower * 3.0f;
                        shakeForce.y *= 0.3f;
                        velocity.ValueRW.Linear += shakeForce * SystemAPI.Time.DeltaTime;
                    }
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }

            if (vibeTimer <= 0f)
            {
                isVibrating = false;
                Debug.Log("🛑 [지진 종료] 블록 원위치 복구, 최종 진단 결과 도색 중...");

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPositions = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);

                foreach (var (transform, tracker, velocity, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    transform.ValueRW.Position = tracker.ValueRO.OriginalPos;
                    transform.ValueRW.Rotation = tracker.ValueRO.OriginalRot;
                    velocity.ValueRW.Linear = float3.zero;
                    velocity.ValueRW.Angular = float3.zero;

                    float maxDisp = tracker.ValueRO.MaxDisplacement;
                    float4 newColor = new float4(1, 1, 1, 1);

                    if (maxDisp >= 2.0f) newColor = new float4(1, 0, 0, 1);
                    else if (maxDisp >= 0.5f) newColor = new float4(1, 1, 0, 1);

                    color.ValueRW.Value = newColor;

                    finalPositions.Add(tracker.ValueRO.OriginalPos);
                    finalStresses.Add(maxDisp);

                    ecb.RemoveComponent<VibrationTracker>(entity);
                }

                SaveVibrationExcel(finalPositions, finalStresses);

                finalPositions.Dispose();
                finalStresses.Dispose();
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
        }
    }

    private void SaveVibrationExcel(NativeList<float3> positions, NativeList<float> stresses)
    {
        string dateStamp = DateTime.Now.ToString("yyyyMMdd");
        string vibeDir = Path.Combine(Application.dataPath, "StressBlock", "vibe");
        if (!Directory.Exists(vibeDir)) Directory.CreateDirectory(vibeDir);

        string historyPath = Path.Combine(vibeDir, $"Vibration_Log_{dateStamp}.csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        System.Collections.Generic.Dictionary<string, string> currentData = new System.Collections.Generic.Dictionary<string, string>();
        if (File.Exists(currentPath))
        {
            string[] existingLines = File.ReadAllLines(currentPath);
            for (int i = 1; i < existingLines.Length; i++)
            {
                string[] cols = existingLines[i].Split(',');
                if (cols.Length > 0) currentData[cols[0]] = existingLines[i];
            }
        }

        System.Collections.Generic.List<string> historyLines = new System.Collections.Generic.List<string>();
        if (!File.Exists(historyPath)) historyLines.Add("BlockID,PosX,PosY,PosZ,VIBE_Stress,RiskLevel,Prescription");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i]; float stress = stresses[i];

            int ix = (int)math.round(pos.x * 10f); int iy = (int)math.round(pos.y * 10f); int iz = (int)math.round(pos.z * 10f);
            string signX = ix < 0 ? "-" : "0"; string signZ = iz < 0 ? "-" : "0"; string signY = iy < 0 ? "-" : "0";
            string id = $"{signX}{math.abs(ix):000}_{signZ}{math.abs(iz):000}_{signY}{math.abs(iy):000}";

            string risk = "Safe"; string pres = "N";
            if (stress >= 2.0f) { risk = "Danger"; pres = "Y"; }
            else if (stress >= 0.5f) { risk = "Warning"; pres = "N"; }

            string lineData = $"{id},{pos.x:F2},{pos.y:F2},{pos.z:F2},{stress:F2},{risk},{pres}";

            historyLines.Add(lineData);
            currentData[id] = lineData;
        }

        File.AppendAllLines(historyPath, historyLines);

        System.Collections.Generic.List<string> finalCurrentLines = new System.Collections.Generic.List<string>();
        finalCurrentLines.Add("BlockID,PosX,PosY,PosZ,VIBE_Stress,RiskLevel,Prescription");
        finalCurrentLines.AddRange(currentData.Values);
        File.WriteAllLines(currentPath, finalCurrentLines);

        Debug.Log($"📄 [엑셀 추가기입 완료] 지진 내진 테스트 결과가 성공적으로 누적 저장되었습니다!");
    }
}
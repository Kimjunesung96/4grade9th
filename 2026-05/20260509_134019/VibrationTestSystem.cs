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
    private bool isBMode; private int vibeLevel; private float actualVibePower;
    private bool isVibrating; private float vibeTimer; private const float MAX_VIBE_TIME = 5.0f;

    public void OnCreate(ref SystemState state)
    {
        isBMode = false; IsBModeActive = false; vibeLevel = 1; actualVibePower = 1f; isVibrating = false; vibeTimer = 0f; state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        if (Input.GetKeyDown(KeyCode.B) && !isVibrating)
        {
            isBMode = !isBMode; IsBModeActive = isBMode;
            if (isBMode) { Debug.Log($"🚨 [지진 세팅 모드] B모드 켜짐! (현재 진도: {vibeLevel}단계)"); foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>()) { color.ValueRW.Value = new float4(1, 1, 1, 1); } }
            else { Debug.Log("✅ [지진 세팅 모드] B모드 취소."); }
        }

        if (isBMode && Input.mouseScrollDelta.y != 0)
        {
            vibeLevel = math.clamp(vibeLevel + (int)math.sign(Input.mouseScrollDelta.y), 1, 8); actualVibePower = math.pow(2f, vibeLevel - 1);
            Debug.Log($"🌍 [진도 설정] 레벨 {vibeLevel} / 파워: {actualVibePower}배");
        }

        if (isBMode && Input.GetKeyDown(KeyCode.G) && !isVibrating)
        {
            isBMode = false; IsBModeActive = false; isVibrating = true; vibeTimer = MAX_VIBE_TIME;
            Debug.Log($"💥 [격발!] 진도 {vibeLevel} 강진 발생!!");

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (transform, mass, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>>().WithAll<BlockTag>().WithEntityAccess())
            {
                if (transform.ValueRO.Position.y <= 3.1f) { mass.ValueRW.InverseMass = 0f; mass.ValueRW.InverseInertia = float3.zero; }
                ecb.AddComponent(entity, new VibrationTracker { OriginalPos = transform.ValueRO.Position, OriginalRot = transform.ValueRO.Rotation, MaxDisplacement = 0f });
            }
            ecb.Playback(state.EntityManager); ecb.Dispose();
        }

        if (isVibrating)
        {
            vibeTimer -= SystemAPI.Time.DeltaTime;
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(vibeTimer * 1000f + 1));

            foreach (var (transform, tracker, velocity, mass, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
            {
                float currentDist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (currentDist > tracker.ValueRO.MaxDisplacement) tracker.ValueRW.MaxDisplacement = currentDist;

                if (vibeTimer > 0f)
                {
                    if (mass.ValueRO.InverseMass > 0) { float3 shakeForce = random.NextFloat3Direction() * actualVibePower * 3.0f; shakeForce.y *= 0.3f; velocity.ValueRW.Linear += shakeForce * SystemAPI.Time.DeltaTime; }
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }

            if (vibeTimer <= 0f)
            {
                isVibrating = false; Debug.Log("🛑 [지진 종료] 엑셀 기록 중...");

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPositions = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);
                NativeList<FixedString32Bytes> finalMaterials = new NativeList<FixedString32Bytes>(Allocator.Temp); // ⭐ 재질용 리스트

                // ⭐ 재질(BlockMaterial) 같이 스캔
                foreach (var (transform, tracker, velocity, color, mat, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    transform.ValueRW.Position = tracker.ValueRO.OriginalPos; transform.ValueRW.Rotation = tracker.ValueRO.OriginalRot; velocity.ValueRW.Linear = float3.zero; velocity.ValueRW.Angular = float3.zero;

                    float maxDisp = tracker.ValueRO.MaxDisplacement; float4 newColor = new float4(1, 1, 1, 1);
                    if (maxDisp >= 2.0f) newColor = new float4(1, 0, 0, 1); else if (maxDisp >= 0.5f) newColor = new float4(1, 1, 0, 1);
                    color.ValueRW.Value = newColor;

                    finalPositions.Add(tracker.ValueRO.OriginalPos);
                    finalStresses.Add(maxDisp);
                    finalMaterials.Add(mat.ValueRO.MaterialName); // ⭐ 저장

                    ecb.RemoveComponent<VibrationTracker>(entity);
                }

                SaveVibrationExcel(finalPositions, finalStresses, finalMaterials); // ⭐ 인자 추가

                finalPositions.Dispose(); finalStresses.Dispose(); finalMaterials.Dispose();
                ecb.Playback(state.EntityManager); ecb.Dispose();
            }
        }
    }

    private void SaveVibrationExcel(NativeList<float3> positions, NativeList<float> stresses, NativeList<FixedString32Bytes> materials)
    {
        string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string vibeDir = Path.Combine(Application.dataPath, "StressBlock", "vibe");
        if (!Directory.Exists(vibeDir)) Directory.CreateDirectory(vibeDir);

        string historyPath = Path.Combine(vibeDir, $"Vibration_All_{dateStamp}.csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        System.Collections.Generic.List<string> currentLines = new System.Collections.Generic.List<string>();
        currentLines.Add("BlockID,PosX,PosY,PosZ,VIBE_Stress,RiskLevel,Prescription,Material"); // ⭐ 헤더 변경

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i]; float stress = stresses[i]; string mat = materials[i].ToString();

            int ix = (int)math.round(pos.x * 10f); int iy = (int)math.round(pos.y * 10f); int iz = (int)math.round(pos.z * 10f);
            string signX = ix < 0 ? "-" : "0"; string signZ = iz < 0 ? "-" : "0"; string signY = iy < 0 ? "-" : "0";
            string id = $"{signX}{math.abs(ix):000}_{signZ}{math.abs(iz):000}_{signY}{math.abs(iy):000}";

            string risk = "Safe"; string pres = "N";
            if (stress >= 2.0f) { risk = "Danger"; pres = "Y"; }
            else if (stress >= 0.5f) { risk = "Warning"; pres = "N"; }

            // ⭐ 맨 끝에 재질 추가
            string lineData = $"{id},{pos.x:F2},{pos.y:F2},{pos.z:F2},{stress:F2},{risk},{pres},{mat}";
            currentLines.Add(lineData);
        }

        File.WriteAllLines(historyPath, currentLines);
        File.WriteAllLines(currentPath, currentLines);
        Debug.Log($"📄 [엑셀] B키 진단결과 저장 완료! (재질포함)");
    }
}
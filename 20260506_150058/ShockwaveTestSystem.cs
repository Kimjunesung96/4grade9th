using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Rendering;
using Unity.Collections;
using System.IO;
using System;

public struct ShockTracker : IComponentData { public float3 OriginalPos; public quaternion OriginalRot; public float MaxDisplacement; }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct ShockwaveTestSystem : ISystem
{
    public static bool IsNModeActive = false;
    private bool isNMode; private bool isShocking; private float shockTimer; private const float MAX_SHOCK_TIME = 5.0f; private float3 epicenter;

    public void OnCreate(ref SystemState state) { isNMode = false; IsNModeActive = false; isShocking = false; shockTimer = 0f; epicenter = float3.zero; state.RequireForUpdate<PhysicsWorldSingleton>(); }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        if (Input.GetKeyDown(KeyCode.N) && !isShocking)
        {
            isNMode = !isNMode; IsNModeActive = isNMode;
            if (isNMode) { Debug.Log("☢️ N-모드 시작"); foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>()) { color.ValueRW.Value = new float4(1, 1, 1, 1); } }
        }

        if (isNMode)
        {
            UnityEngine.Debug.DrawLine(epicenter + new float3(-1, 0, 0), epicenter + new float3(1, 0, 0), UnityEngine.Color.red); UnityEngine.Debug.DrawLine(epicenter + new float3(0, -1, 0), epicenter + new float3(0, 1, 0), UnityEngine.Color.red); UnityEngine.Debug.DrawLine(epicenter + new float3(0, 0, -1), epicenter + new float3(0, 0, 1), UnityEngine.Color.red); UnityEngine.Debug.DrawRay(epicenter, UnityEngine.Vector3.up * 5f, UnityEngine.Color.red);
            if (Input.GetMouseButtonDown(1))
            {
                UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); PhysicsWorld physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld; bool hitSuccess = false;
                RaycastInput rayInput = new RaycastInput { Start = ray.origin, End = ray.origin + ray.direction * 500f, Filter = CollisionFilter.Default };
                if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit)) { epicenter = hit.Position; hitSuccess = true; }
                else { UnityEngine.Plane groundPlane = new UnityEngine.Plane(UnityEngine.Vector3.up, UnityEngine.Vector3.zero); if (groundPlane.Raycast(ray, out float enter)) { epicenter = ray.GetPoint(enter); hitSuccess = true; } }
            }
        }

        if (isNMode && Input.GetKeyDown(KeyCode.G) && !isShocking)
        {
            isNMode = false; IsNModeActive = false;
            var ecb = new EntityCommandBuffer(Allocator.Temp); PhysicsWorld physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            int ghostCount = 0; foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess()) { ghostCount++; ecb.DestroyEntity(entity); }
            float explosionPower = ghostCount * 5.0f; float blastRadius = ghostCount * 1.5f; isShocking = true; shockTimer = MAX_SHOCK_TIME;

            foreach (var (transform, mass, velocity, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>, RefRW<PhysicsVelocity>>().WithAll<BlockTag>().WithEntityAccess())
            {
                if (transform.ValueRO.Position.y <= 3.1f) { mass.ValueRW.InverseMass = 0f; mass.ValueRW.InverseInertia = float3.zero; }
                ecb.AddComponent(entity, new ShockTracker { OriginalPos = transform.ValueRO.Position, OriginalRot = transform.ValueRO.Rotation, MaxDisplacement = 0f });
                if (mass.ValueRO.InverseMass > 0)
                {
                    float3 dir = transform.ValueRO.Position - epicenter; float dist = math.length(dir);
                    if (dist <= blastRadius + 1.0f)
                    {
                        if (dist < 0.1f) dir = math.up(); else dir = math.normalize(dir);
                        float finalPower = explosionPower; RaycastInput ray = new RaycastInput { Start = epicenter, End = transform.ValueRO.Position, Filter = CollisionFilter.Default };
                        if (physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit)) { if (hit.Entity != entity) finalPower *= 0.15f; }
                        float forceMag = finalPower / (dist * dist + 0.5f); velocity.ValueRW.Linear += dir * forceMag;
                        float3 toppleAxis = math.cross(math.up(), dir); float heightMult = math.max(1.0f, transform.ValueRO.Position.y * 0.5f); velocity.ValueRW.Angular += toppleAxis * (forceMag * 0.5f * heightMult);
                    }
                }
            }
            ecb.Playback(state.EntityManager); ecb.Dispose();
        }

        if (isShocking)
        {
            shockTimer -= SystemAPI.Time.DeltaTime;
            foreach (var (transform, tracker, color) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ShockTracker>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
            {
                float dist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (dist > tracker.ValueRW.MaxDisplacement) tracker.ValueRW.MaxDisplacement = dist;
                if (shockTimer > 0f) color.ValueRW.Value = new float4(1, 1, 1, 1);
            }
            if (shockTimer <= 0f)
            {
                isShocking = false; var ecb = new EntityCommandBuffer(Allocator.Temp); NativeList<float3> finalPos = new NativeList<float3>(Allocator.Temp); NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);
                foreach (var (tr, tracker, vel, color, ent) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ShockTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    tr.ValueRW.Position = tracker.ValueRO.OriginalPos; tr.ValueRW.Rotation = tracker.ValueRO.OriginalRot; vel.ValueRW.Linear = float3.zero; vel.ValueRW.Angular = float3.zero;
                    float maxDisp = tracker.ValueRO.MaxDisplacement; float4 newCol = new float4(1, 1, 1, 1);
                    if (maxDisp >= 5.0f) newCol = new float4(0.2f, 0.2f, 0.2f, 1); else if (maxDisp >= 2.0f) newCol = new float4(1, 0, 0, 1); else if (maxDisp >= 0.5f) newCol = new float4(1, 1, 0, 1);
                    color.ValueRW.Value = newCol; finalPos.Add(tracker.ValueRO.OriginalPos); finalStresses.Add(maxDisp); ecb.RemoveComponent<ShockTracker>(ent);
                }
                SaveShockwaveExcel(finalPos, finalStresses); finalPos.Dispose(); finalStresses.Dispose(); ecb.Playback(state.EntityManager); ecb.Dispose();
            }
        }
    }

    private void SaveShockwaveExcel(NativeList<float3> positions, NativeList<float> stresses)
    {
        string dateStamp = DateTime.Now.ToString("yyyyMMdd");
        string shockDir = Path.Combine(Application.dataPath, "StressBlock", "shockwave");
        if (!Directory.Exists(shockDir)) Directory.CreateDirectory(shockDir);

        string historyPath = Path.Combine(shockDir, $"Shockwave_Log_{dateStamp}.csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        // ⭐ 1. Current 데이터 읽기
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

        // ⭐ 2. 히스토리 세팅 (파일 없으면 헤더 추가)
        System.Collections.Generic.List<string> historyLines = new System.Collections.Generic.List<string>();
        if (!File.Exists(historyPath)) historyLines.Add("BlockID,PosX,PosY,PosZ,SHOCK_Stress,RiskLevel,Prescription");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i]; float stress = stresses[i];

            float ix = math.round(pos.x * 10f); float iz = math.round(pos.z * 10f); float iy = math.round(pos.y * 10f);
            string strX = $"{(ix < 0 ? "-" : "0")}{math.abs(ix):000}"; string strZ = $"{(iz < 0 ? "-" : "0")}{math.abs(iz):000}"; string strY = $"{(iy < 0 ? "-" : "0")}{math.abs(iy):000}";

            string id = $"{strX}_{strZ}_{strY}";
            string risk = stress >= 2.0f ? "Danger" : (stress >= 0.5f ? "Warning" : "Safe"); string pres = stress >= 2.0f ? "Y" : "N";
            string lineData = $"{id},{pos.x:F2},{pos.y:F2},{pos.z:F2},{stress:F2},{risk},{pres}";

            historyLines.Add(lineData);
            currentData[id] = lineData;
        }

        // ⭐ 3. 파일 출력 (히스토리는 Append, Current는 Update)
        File.AppendAllLines(historyPath, historyLines);

        System.Collections.Generic.List<string> finalCurrentLines = new System.Collections.Generic.List<string>();
        finalCurrentLines.Add("BlockID,PosX,PosY,PosZ,SHOCK_Stress,RiskLevel,Prescription");
        finalCurrentLines.AddRange(currentData.Values);
        File.WriteAllLines(currentPath, finalCurrentLines);

        Debug.Log($"📄 [엑셀 추가기입 완료] 폭파 충격파 테스트 결과가 성공적으로 누적 저장되었습니다!");
    }
}
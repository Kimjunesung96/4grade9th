using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Rendering;
using Unity.Collections;
using System.IO;
using System;

public struct ShockTracker : IComponentData
{
    public float3 OriginalPos;
    public quaternion OriginalRot;
    public float MaxDisplacement;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct ShockwaveTestSystem : ISystem
{
    public static bool IsNModeActive = false;

    private bool isNMode;
    private bool isShocking;
    private float shockTimer;
    private const float MAX_SHOCK_TIME = 5.0f;
    private float3 epicenter;

    public void OnCreate(ref SystemState state)
    {
        isNMode = false;
        IsNModeActive = false;
        isShocking = false;
        shockTimer = 0f;
        epicenter = float3.zero;
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        // 1. [N키] 충격파 모드 진입
        if (Input.GetKeyDown(KeyCode.N) && !isShocking)
        {
            isNMode = !isNMode;
            IsNModeActive = isNMode;

            if (isNMode)
            {
                Debug.Log("☢️ [N-모드] 무한 반복 테스트 준비 완료! \n1. 장약(F) 설치 2. 우클릭 좌표 설정 3. G키 격발");
                foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
                {
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }
            else { Debug.Log("✅ [N-모드] 해제."); }
        }

        // 2. [우클릭] 진앙지 설정 및 시각화 (바닥 투과 방지)
        if (isNMode)
        {
            // ⭐ 폭심지 시각화 (빨간 십자선)
            UnityEngine.Debug.DrawLine(epicenter + new float3(-1, 0, 0), epicenter + new float3(1, 0, 0), UnityEngine.Color.red);
            UnityEngine.Debug.DrawLine(epicenter + new float3(0, -1, 0), epicenter + new float3(0, 1, 0), UnityEngine.Color.red);
            UnityEngine.Debug.DrawLine(epicenter + new float3(0, 0, -1), epicenter + new float3(0, 0, 1), UnityEngine.Color.red);
            UnityEngine.Debug.DrawRay(epicenter, UnityEngine.Vector3.up * 5f, UnityEngine.Color.red);

            if (Input.GetMouseButtonDown(1))
            {
                UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                PhysicsWorld physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
                bool hitSuccess = false;

                // 1순위: DOTS 물리 블록 검사
                RaycastInput rayInput = new RaycastInput { Start = ray.origin, End = ray.origin + ray.direction * 500f, Filter = CollisionFilter.Default };
                if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
                {
                    epicenter = hit.Position;
                    hitSuccess = true;
                }
                else
                {
                    // 2순위: 일반 유니티 바닥(또는 허공) 교차점 검사
                    UnityEngine.Plane groundPlane = new UnityEngine.Plane(UnityEngine.Vector3.up, UnityEngine.Vector3.zero);
                    if (groundPlane.Raycast(ray, out float enter))
                    {
                        epicenter = ray.GetPoint(enter);
                        hitSuccess = true;
                    }
                }

                if (hitSuccess) Debug.Log($"🎯 진앙지 좌표 고정: {epicenter}");
            }
        }

        // 3. [G키] 격발 (철근 유지, 차폐, 역제곱 적용)
        if (isNMode && Input.GetKeyDown(KeyCode.G) && !isShocking)
        {
            isNMode = false;
            IsNModeActive = false;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            PhysicsWorld physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;

            int ghostCount = 0;
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess())
            {
                ghostCount++;
                ecb.DestroyEntity(entity);
            }

            float explosionPower = ghostCount * 5.0f;
            float blastRadius = ghostCount * 1.5f;

            isShocking = true;
            shockTimer = MAX_SHOCK_TIME;

            foreach (var (transform, mass, velocity, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>, RefRW<PhysicsVelocity>>().WithAll<BlockTag>().WithEntityAccess())
            {
                if (transform.ValueRO.Position.y <= 3.1f)
                {
                    mass.ValueRW.InverseMass = 0f;
                    mass.ValueRW.InverseInertia = float3.zero;
                }

                ecb.AddComponent(entity, new ShockTracker
                {
                    OriginalPos = transform.ValueRO.Position,
                    OriginalRot = transform.ValueRO.Rotation,
                    MaxDisplacement = 0f
                });

                if (mass.ValueRO.InverseMass > 0)
                {
                    float3 dir = transform.ValueRO.Position - epicenter;
                    float dist = math.length(dir);

                    if (dist <= blastRadius + 1.0f)
                    {
                        if (dist < 0.1f) dir = math.up();
                        else dir = math.normalize(dir);

                        // 차폐 검사
                        float finalPower = explosionPower;
                        RaycastInput ray = new RaycastInput { Start = epicenter, End = transform.ValueRO.Position, Filter = CollisionFilter.Default };
                        if (physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit))
                        {
                            if (hit.Entity != entity) finalPower *= 0.15f;
                        }

                        // 역제곱 법칙
                        float forceMag = finalPower / (dist * dist + 0.5f);
                        velocity.ValueRW.Linear += dir * forceMag;

                        // 회전력 (파도타기 도미노)
                        float3 toppleAxis = math.cross(math.up(), dir);
                        float heightMult = math.max(1.0f, transform.ValueRO.Position.y * 0.5f);
                        velocity.ValueRW.Angular += toppleAxis * (forceMag * 0.5f * heightMult);
                    }
                }
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        // 4. 시뮬레이션 중
        if (isShocking)
        {
            shockTimer -= SystemAPI.Time.DeltaTime;
            foreach (var (transform, tracker, color) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ShockTracker>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
            {
                float dist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (dist > tracker.ValueRW.MaxDisplacement) tracker.ValueRW.MaxDisplacement = dist;
                if (shockTimer > 0f) color.ValueRW.Value = new float4(1, 1, 1, 1);
            }

            // 5. 복구 (동결 안 함 -> 여러 번 터뜨리기 가능)
            if (shockTimer <= 0f)
            {
                isShocking = false;
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPos = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);

                foreach (var (tr, tracker, vel, color, ent) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ShockTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    tr.ValueRW.Position = tracker.ValueRO.OriginalPos;
                    tr.ValueRW.Rotation = tracker.ValueRO.OriginalRot;
                    vel.ValueRW.Linear = float3.zero;
                    vel.ValueRW.Angular = float3.zero;

                    float maxDisp = tracker.ValueRO.MaxDisplacement;
                    float4 newCol = new float4(1, 1, 1, 1);
                    if (maxDisp >= 5.0f) newCol = new float4(0.2f, 0.2f, 0.2f, 1);
                    else if (maxDisp >= 2.0f) newCol = new float4(1, 0, 0, 1);
                    else if (maxDisp >= 0.5f) newCol = new float4(1, 1, 0, 1);

                    color.ValueRW.Value = newCol;
                    finalPos.Add(tracker.ValueRO.OriginalPos);
                    finalStresses.Add(maxDisp);
                    ecb.RemoveComponent<ShockTracker>(ent);
                }
                SaveShockwaveExcel(finalPos, finalStresses);
                finalPos.Dispose(); finalStresses.Dispose();
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
                Debug.Log("🛑 [복구 완료] 이제 다시 폭탄을 터뜨릴 수 있습니다!");
            }
        }
    }

    private void SaveShockwaveExcel(NativeList<float3> positions, NativeList<float> stresses)
    {
        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string shockDir = Path.Combine(Application.dataPath, "StressBlock", "shockwave");
        if (!Directory.Exists(shockDir)) Directory.CreateDirectory(shockDir);

        string historyPath = Path.Combine(shockDir, $"Shockwave_All_{timeStamp}.csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
        lines.Add("BlockID,PosX,PosY,PosZ,WEIGHT_Stress,RiskLevel,Prescription");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i];
            float stress = stresses[i];
            int ix = (int)math.round(pos.x * 10f); int iy = (int)math.round(pos.y * 10f); int iz = (int)math.round(pos.z * 10f);
            string id = $"{(ix < 0 ? "-" : "0")}{math.abs(ix):000}_{(iz < 0 ? "-" : "0")}{math.abs(iz):000}_{(iy < 0 ? "-" : "0")}{math.abs(iy):000}";
            string risk = stress >= 2.0f ? "Danger" : (stress >= 0.5f ? "Warning" : "Safe");
            string pres = stress >= 2.0f ? "Y" : "N";
            lines.Add($"{id},{pos.x:F2},{pos.y:F2},{pos.z:F2},{stress:F2},{risk},{pres}");
        }
        File.WriteAllLines(historyPath, lines);
        File.WriteAllLines(currentPath, lines);
    }
}
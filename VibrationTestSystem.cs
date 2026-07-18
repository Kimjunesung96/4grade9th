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

    private static readonly int3[] gridDirs = new int3[] { new int3(1, 0, 0), new int3(0, 0, 1), new int3(0, 1, 0) };
    private static readonly float3[] internalDirs = new float3[] { new float3(1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0) };

    public void OnCreate(ref SystemState state)
    {
        isBMode = false; IsBModeActive = false; vibeLevel = 1; actualVibePower = 1f; isVibrating = false; vibeTimer = 0f; state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        if (Input.GetKeyDown(KeyCode.B) && !isVibrating)
        {
            if (!isBMode) 
            {
                isBMode = true; IsBModeActive = true;
                Debug.Log($"🚨 [지진 세팅 모드] B모드 켜짐! (현재 진도: {vibeLevel}단계) 한 번 더 B를 누르면 격발합니다.");
                foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>()) { color.ValueRW.Value = new float4(1, 1, 1, 1); }
            }
            else 
            {
                isBMode = false; IsBModeActive = false;
                isVibrating = true; vibeTimer = MAX_VIBE_TIME;
                Debug.Log($"💥 [격발!] 진도 {vibeLevel} 강진 발생!!");

                var initEcb = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (transform, mass, gravity, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PhysicsMass>, RefRW<PhysicsGravityFactor>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    gravity.ValueRW.Value = 1.0f;
                    if (transform.ValueRO.Position.y <= 3.1f) { 
                        mass.ValueRW.InverseMass = 0f; 
                        mass.ValueRW.InverseInertia = float3.zero; 
                    }
                    else { 
                        mass.ValueRW.InverseMass = 0.1f; 
                        mass.ValueRW.InverseInertia = new float3(0.1f, 0.1f, 0.1f); 
                    }

                    float3 trueOrig = SystemAPI.HasComponent<OriginalPosition>(entity) ? SystemAPI.GetComponent<OriginalPosition>(entity).Value : transform.ValueRO.Position;
                    initEcb.AddComponent(entity, new VibrationTracker { OriginalPos = trueOrig, OriginalRot = quaternion.identity, MaxDisplacement = 0f });
                }
                initEcb.Playback(state.EntityManager); initEcb.Dispose();
            }
        }

        if (isBMode && Input.mouseScrollDelta.y != 0)
        {
            vibeLevel = math.clamp(vibeLevel + (int)math.sign(Input.mouseScrollDelta.y), 1, 8); actualVibePower = math.pow(2f, vibeLevel - 1);
            Debug.Log($"🌍 [진도 설정] 레벨 {vibeLevel} / 파워: {actualVibePower}배");
        }

        if (isVibrating)
        {
            vibeTimer -= SystemAPI.Time.DeltaTime;
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(vibeTimer * 1000f + 1));

            float3 shakeVel = new float3(math.sin(vibeTimer * 10.0f) * actualVibePower, 0, 0);
            foreach (var (transform, tracker, velocity, mass, color, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>().WithEntityAccess())
            {
                float currentDist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (currentDist > tracker.ValueRW.MaxDisplacement) tracker.ValueRW.MaxDisplacement = currentDist;

                if (vibeTimer > 0f)
                {
                    if (transform.ValueRO.Position.y <= 3.1f) velocity.ValueRW.Linear = shakeVel;
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }

            state.Dependency.Complete();
            var breakEcb = new EntityCommandBuffer(Allocator.Temp);
            var transLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var matLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true);

            foreach (var (jointPair, joint, jointEntity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>, RefRO<PhysicsJoint>>().WithAll<JointTag>().WithEntityAccess())
            {
                if (transLookup.HasComponent(jointPair.ValueRO.EntityA) && transLookup.HasComponent(jointPair.ValueRO.EntityB) &&
                    matLookup.HasComponent(jointPair.ValueRO.EntityA) && matLookup.HasComponent(jointPair.ValueRO.EntityB))
                {
                    var transA = transLookup[jointPair.ValueRO.EntityA]; var transB = transLookup[jointPair.ValueRO.EntityB];
                    var matA = matLookup[jointPair.ValueRO.EntityA]; var matB = matLookup[jointPair.ValueRO.EntityB];

                    float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.ValueRO.BodyAFromJoint.Position);
                    float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.ValueRO.BodyBFromJoint.Position);
                    
                    float stretch = math.distance(pivotA, pivotB);
                    if (stretch > 0.05f) 
                    {
                        // ⭐ [재질 강도 영향 반영] 재질의 인장 강도가 높을수록 더 큰 변형을 버팀!
                        float tensileStress = stretch * 5000.0f; 
                        
                        // ⭐ [재질 강도 영향 반영] 
                        // tensileDefense = (matA.TensileStiffness + matB.TensileStiffness) * 0.5f; 
                        // 위 기존 코드에 진동의 강도(actualVibePower)를 대입하여 강도에 따라 파괴 임계값 조정
                        float tensileDefense = (matA.TensileStiffness + matB.TensileStiffness) * 0.5f * math.max(0.1f, (10.0f - actualVibePower));

                        if (tensileStress > tensileDefense) 
                        { 
                            breakEcb.DestroyEntity(jointEntity); 
                        }
                    }
                }
            }
            breakEcb.Playback(state.EntityManager); breakEcb.Dispose();

            if (vibeTimer <= 0f)
            {
                isVibrating = false; Debug.Log("🛑 [지진 종료] 도면(ID) 기준 위치로 원상복구 완료!");

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPositions = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);
                NativeList<FixedString32Bytes> finalMaterials = new NativeList<FixedString32Bytes>(Allocator.Temp);

                foreach (var (transform, tracker, velocity, gravity, mass, color, mat, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRW<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    // ⭐ 도면의 본래 위치(ID 기반)로 강제 귀환! (공중분해 버그 원천 차단)
                    transform.ValueRW.Position = tracker.ValueRO.OriginalPos;
                    transform.ValueRW.Rotation = tracker.ValueRO.OriginalRot;
                    velocity.ValueRW.Linear = float3.zero;
                    velocity.ValueRW.Angular = float3.zero;
                    gravity.ValueRW.Value = 0.0f;
                    mass.ValueRW.InverseMass = 0.0f;
                    mass.ValueRW.InverseInertia = float3.zero;

                    float maxDisp = tracker.ValueRO.MaxDisplacement; 
                    string mName = mat.ValueRO.MaterialName.ToString();

                    // ⭐ [색상 롤백] 스트레스 비주얼 모드와 똑같이 충격량 분수(t)로 부드러운 그라데이션 칠하기!
                    float4 baseCol = new float4(0.7f, 0.7f, 0.7f, 1.0f);
                    if (mName.Contains("Steel")) baseCol = new float4(0.2f, 0.5f, 1.0f, 1.0f); 
                    else if (mName.Contains("Wood") || mName.Contains("Timber")) baseCol = new float4(0.6f, 0.4f, 0.2f, 1.0f); 
                    else if (mName.Contains("Brick")) baseCol = new float4(0.8f, 0.3f, 0.2f, 1.0f);

                    float t = math.clamp(maxDisp / 2.0f, 0.0f, 1.0f);
                    color.ValueRW.Value = math.lerp(baseCol, new float4(1.0f, 0.0f, 0.0f, 1.0f), t);

                    finalPositions.Add(tracker.ValueRO.OriginalPos);
                    finalStresses.Add(maxDisp);
                    finalMaterials.Add(mat.ValueRO.MaterialName);

                    ecb.RemoveComponent<VibrationTracker>(entity);
                }

                SaveVibrationExcel(finalPositions, finalStresses, finalMaterials);

                finalPositions.Dispose(); finalStresses.Dispose(); finalMaterials.Dispose();
                ecb.Playback(state.EntityManager); ecb.Dispose();

                // ⭐ [조인트 전면 재구성] 살아남은/끊어진 조인트 다 지우고, 현재(=복구된) 위치 기준으로 그리드 스캔해서 새로 싹 다 묶음
                RebuildAllJoints(ref state);
            }
        }
    }

    // ⭐ 3유닛 그리드 기준으로 인접한 블록들을 전부 다시 조인트로 묶는다 (O(N) 공간 해시, SpawnerSystem의 조인트 생성 로직과 동일한 방식)
    private void RebuildAllJoints(ref SystemState state)
    {
        var destroyEcb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (pair, jointEntity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
        {
            destroyEcb.DestroyEntity(jointEntity);
        }
        destroyEcb.Playback(state.EntityManager);
        destroyEcb.Dispose();

        var gridMap = new NativeHashMap<int3, Entity>(4096, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>(4096, Allocator.Temp);

        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BlockTag>().WithEntityAccess())
        {
            float3 pos = transform.ValueRO.Position;
            int3 key = new int3(
                (int)math.floor(pos.x / 3f + 0.5f),
                (int)math.floor(pos.y / 3f + 0.5f),
                (int)math.floor(pos.z / 3f + 0.5f));
            gridMap.TryAdd(key, entity);
            posMap.TryAdd(entity, pos);
        }

        var buildEcb = new EntityCommandBuffer(Allocator.Temp);
        var keys = gridMap.GetKeyArray(Allocator.Temp);
        int newJointCount = 0;

        for (int k = 0; k < keys.Length; k++)
        {
            int3 key = keys[k];
            Entity cur = gridMap[key];

            for (int d = 0; d < 3; d++)
            {
                if (gridMap.TryGetValue(key + gridDirs[d], out Entity neighbor) && neighbor != cur)
                {
                    CreateIndestructibleJoint(ref buildEcb, cur, neighbor, internalDirs[d] * 3.0f);
                    newJointCount++;
                }
            }
        }

        buildEcb.Playback(state.EntityManager);
        buildEcb.Dispose();
        keys.Dispose();
        gridMap.Dispose();
        posMap.Dispose();

        Debug.Log($"🔧 [조인트 전면 재구성] 기존 조인트 삭제 후 인접 블록 {newJointCount}쌍 재결합 완료!");
    }

    private void CreateIndestructibleJoint(ref EntityCommandBuffer ecb, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity();
        ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex());
        ecb.AddComponent<JointTag>(jointEntity);
        ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true));
        ecb.AddComponent(jointEntity, PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f)));
    }

    private void SaveVibrationExcel(NativeList<float3> positions, NativeList<float> stresses, NativeList<FixedString32Bytes> materials)
    {
        string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string vibDir = Path.Combine(Application.dataPath, "StressBlock", "vibration");
        if (!Directory.Exists(vibDir)) Directory.CreateDirectory(vibDir);

        string historyPath = Path.Combine(vibDir, "Vibration_All_" + dateStamp + ".csv");
        string currentPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");

        System.Collections.Generic.List<string> currentLines = new System.Collections.Generic.List<string>();
        currentLines.Add("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i]; float stress = stresses[i]; string mat = materials[i].ToString();
            float ix = math.round(pos.x * 10f); float iz = math.round(pos.z * 10f); float iy = math.round(pos.y * 10f);
            string strX = (ix < 0 ? "-" : "0") + math.abs(ix).ToString("000"); string strZ = (iz < 0 ? "-" : "0") + math.abs(iz).ToString("000"); string strY = (iy < 0 ? "-" : "0") + math.abs(iy).ToString("000");
            string id = strX + "_" + strZ + "_" + strY;

            // ⭐ [기록용 로직] 2.0을 초과하면 posXYZ 칸에 "DESTROYED" 기록!
            string posX = stress >= 2.0f ? "DESTROYED" : pos.x.ToString("F2");
            string posY = stress >= 2.0f ? "DESTROYED" : pos.y.ToString("F2");
            string posZ = stress >= 2.0f ? "DESTROYED" : pos.z.ToString("F2");

            string risk = stress >= 2.0f ? "Destroyed" : (stress >= 0.5f ? "Quake_Danger" : "Safe"); 
            string pres = stress >= 2.0f ? "Y" : (stress >= 0.5f ? "Y" : "N");
            string typeStr = pos.y > 1.5f ? "Wall" : "Floor";

            string lineData = id + "," + posX + "," + posY + "," + posZ + "," + stress.ToString("F2") + "," + risk + "," + pres + "," + mat + ",0.0,0.0,Existing," + typeStr;
            currentLines.Add(lineData);
        }
        File.WriteAllLines(historyPath, currentLines); File.WriteAllLines(currentPath, currentLines);
    }
}
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
    private bool isBMode; public static int VibeLevel = 1; /* HUD에서 읽을 진도 레버(1~8) */ private float actualVibePower;
    private bool isVibrating; private float vibeTimer; private float dynCheckTimer;
    public static bool IsSimulationRunning = false; 
    
    private static float MAX_VIBE_TIME => SimulationSettingsProvider.Instance != null ? SimulationSettingsProvider.Instance.vibrationMaxTime : 5.0f;

    private static readonly int3[] gridDirs = new int3[] { new int3(1, 0, 0), new int3(0, 0, 1), new int3(0, 1, 0) };
    private static readonly float3[] internalDirs = new float3[] { new float3(1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0) };

    public void OnCreate(ref SystemState state)
    {
        isBMode = false; IsBModeActive = false; VibeLevel = 1; actualVibePower = 1f; isVibrating = false; vibeTimer = 0f; state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;

        if (TestMode.Selected(2) && !isVibrating)
        {
            if (!isBMode) 
            {
                isBMode = true; IsBModeActive = true;
                Debug.Log($"🚨 [지진 세팅 모드] B모드 켜짐! (현재 진도: {VibeLevel}단계) 한 번 더 B를 누르면 격발합니다.");
                foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>()) { color.ValueRW.Value = new float4(1, 1, 1, 1); }
            }
            else 
            {
                isBMode = false; IsBModeActive = false;
                isVibrating = true; vibeTimer = MAX_VIBE_TIME; dynCheckTimer = 0f;
                IsSimulationRunning = true;
                Debug.Log($"💥 [격발!] 진도 {VibeLevel} 강진 발생!!");

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
            VibeLevel = math.clamp(VibeLevel + (int)math.sign(Input.mouseScrollDelta.y), 1, 8); actualVibePower = math.pow(2f, VibeLevel - 1);
            if (JointStressCalc.UseForceBased) Debug.Log($"🌍 [진도 설정] 레벨 {VibeLevel} / 최대 지반 가속도 {StructuralLoadSolver.PgaForLevel(VibeLevel):0.00}g");
            else Debug.Log($"🌍 [진도 설정] 레벨 {VibeLevel} / 파워: {actualVibePower}배");
        }

        if (isVibrating)
        {
            vibeTimer -= SystemAPI.Time.DeltaTime;
            var random = Unity.Mathematics.Random.CreateFromIndex((uint)(vibeTimer * 1000f + 1));

            // 힘 기반 모드: a(t) = PGA·sin(2πft), 방향은 F5의 지진 방향. 레거시 모드는 기존 식 그대로.
            float3 groundAccel;
            if (JointStressCalc.UseForceBased)
            {
                float dirRad = StructuralLoadSolver.EarthquakeDirDeg * 0.0174532925f;
                float3 dir = new float3(math.cos(dirRad), 0f, math.sin(dirRad));
                float elapsed = MAX_VIBE_TIME - vibeTimer;
                float pgaMs2 = StructuralLoadSolver.PgaForLevel(VibeLevel) * StructuralLoadSolver.Gravity;
                groundAccel = dir * (pgaMs2 * math.sin(2f * math.PI * StructuralLoadSolver.QuakeFreqHz * elapsed));
            }
            else groundAccel = new float3(math.sin(vibeTimer * 10.0f) * actualVibePower, 0, 0);
            foreach (var (transform, tracker, velocity, color) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
            {
                float currentDist = math.distance(transform.ValueRO.Position, tracker.ValueRO.OriginalPos);
                if (currentDist > tracker.ValueRW.MaxDisplacement) tracker.ValueRW.MaxDisplacement = currentDist;

                if (vibeTimer > 0f)
                {
                    if (transform.ValueRO.Position.y > 3.1f)
                    {
                        velocity.ValueRW.Linear += groundAccel * SystemAPI.Time.DeltaTime;
                    }
                    color.ValueRW.Value = new float4(1, 1, 1, 1);
                }
            }

            state.Dependency.Complete();
            var breakEcb = new EntityCommandBuffer(Allocator.Temp);
            var transLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var matLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true);
            var velLookup = SystemAPI.GetComponentLookup<PhysicsVelocity>(false); // ⭐ 조인트 끊기는 순간 반발 속도 리셋용

            foreach (var (jointPair, joint, jointEntity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>, RefRO<PhysicsJoint>>().WithAll<JointTag>().WithEntityAccess())
            {
                Entity eA = jointPair.ValueRO.EntityA; 
                Entity eB = jointPair.ValueRO.EntityB;

                if (eA == Entity.Null || eB == Entity.Null) continue;

                if (transLookup.HasComponent(eA) && transLookup.HasComponent(eB) &&
                    matLookup.HasComponent(eA) && matLookup.HasComponent(eB))
                {
                    var transA = transLookup[eA]; var transB = transLookup[eB];
                    var matA = matLookup[eA]; var matB = matLookup[eB];

                    float tensionScale = SimulationSettingsProvider.Instance != null ? SimulationSettingsProvider.Instance.TensionStressScale : 5000.0f;
                    // 부서짐 판정은 JointStressCalc.ShouldBreak 한 곳에서만 한다. (이 시스템은 하중/타이머/복귀만 담당)
                    if (JointStressCalc.ShouldBreak(joint.ValueRO, transA, transB, matA, matB, 0f, tensionScale, out _))
                    {
                        breakEcb.DestroyEntity(jointEntity);
                        // ⭐ 조인트가 끊기는 프레임에 솔버가 계산해둔 반발 속도를 물려받지 않게 리셋
                        if (velLookup.HasComponent(eA)) velLookup[eA] = new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero };
                        if (velLookup.HasComponent(eB)) velLookup[eB] = new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero };
                    }
                }
            }
            breakEcb.Playback(state.EntityManager); breakEcb.Dispose();

            // [동적 하중] 힘 기반 모드에서는 0.5초마다 진동수·가속도를 하중으로 바꿔 단면 사용률로 부서짐을 정한다.
            dynCheckTimer -= SystemAPI.Time.DeltaTime;
            if (JointStressCalc.UseForceBased && vibeTimer > 0f && dynCheckTimer <= 0f)
            {
                dynCheckTimer = 0.5f;
                EvaluateDynamicQuake(ref state);
            }

            if (vibeTimer <= 0f)
            {
                isVibrating = false; IsSimulationRunning = false; Debug.Log("🛑 [지진 종료] 도면(ID) 기준 위치로 원상복구 완료!");

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                NativeList<float3> finalPositions = new NativeList<float3>(Allocator.Temp);
                NativeList<float> finalStresses = new NativeList<float>(Allocator.Temp);
                NativeList<FixedString32Bytes> finalMaterials = new NativeList<FixedString32Bytes>(Allocator.Temp);

                foreach (var (transform, tracker, velocity, gravity, mass, color, mat, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<VibrationTracker>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRW<PhysicsMass>, RefRW<URPMaterialPropertyBaseColor>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    transform.ValueRW.Position = tracker.ValueRO.OriginalPos;
                    transform.ValueRW.Rotation = tracker.ValueRO.OriginalRot;
                    velocity.ValueRW.Linear = float3.zero;
                    velocity.ValueRW.Angular = float3.zero;
                    gravity.ValueRW.Value = 0.0f;
                    mass.ValueRW.InverseMass = 0.0f;
                    mass.ValueRW.InverseInertia = float3.zero;

                    float maxDisp = tracker.ValueRO.MaxDisplacement; 
                    string mName = mat.ValueRO.MaterialName.ToString();

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

                foreach (var (transform, mat, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithNone<VibrationTracker>().WithEntityAccess())
                {
                    finalPositions.Add(transform.ValueRO.Position);
                    finalStresses.Add(0f);
                    finalMaterials.Add(mat.ValueRO.MaterialName);
                }

                SaveVibrationExcel(finalPositions, finalStresses, finalMaterials);

                finalPositions.Dispose(); finalStresses.Dispose(); finalMaterials.Dispose();
                ecb.Playback(state.EntityManager); ecb.Dispose();

                RebuildAllJoints(ref state);
            }
        }
    }

    // 진동수 f, 최대 지반 가속도 PGA(진도 레벨) -> 구조물 1차 고유진동수 fn 과의 비 r = f/fn 으로 전달률 T 를 구하고,
    // 유효 가속도 PGA·T 를 StructuralLoadSolver 의 관성력에 넣는다. 사용률 > 1 인 블록은 지지 단면의 조인트를 끊는다.
    //   - 바로 아래로 지지되는 블록: 아래 블록과의 조인트
    //   - 옆으로 내민 블록: 지지 쪽으로 이어진 옆 조인트
    // 끊긴 뒤에는 남은 조인트로 하중 경로가 다시 계산돼서 연쇄 붕괴가 그대로 나온다.
    private void EvaluateDynamicQuake(ref SystemState state)
    {
        var ents = new System.Collections.Generic.List<Entity>();
        var grids = new System.Collections.Generic.List<int3>();
        var weights = new System.Collections.Generic.List<float>();
        var grounds = new System.Collections.Generic.List<bool>();
        var fcs = new System.Collections.Generic.List<float>();
        var fts = new System.Collections.Generic.List<float>();
        var eGPa = new System.Collections.Generic.List<float>();
        var fvs = new System.Collections.Generic.List<float>();
        var rhos = new System.Collections.Generic.List<float>();
        var idxOf = new System.Collections.Generic.Dictionary<Entity, int>();

        foreach (var (pos, mat, entity) in SystemAPI.Query<RefRO<OriginalPosition>, RefRO<BlockMaterial>>().WithEntityAccess())
        {
            if (!JointStressCalc.CompressionForceReady(mat.ValueRO) || mat.ValueRO.YoungModulusGPa <= 0f) continue;
            float3 op = pos.ValueRO.Value;
            idxOf[entity] = ents.Count;
            ents.Add(entity);
            grids.Add(new int3((int)math.floor(op.x / 3.0f + 0.5f), (int)math.floor(op.y / 3.0f + 0.5f), (int)math.floor(op.z / 3.0f + 0.5f)));
            weights.Add(StructuralLoadSolver.WeightN(mat.ValueRO.DensityTPerM3));
            grounds.Add(op.y <= 3.1f);
            fcs.Add(mat.ValueRO.CompressiveMPa);
            fts.Add(mat.ValueRO.TensileMPa);
            eGPa.Add(mat.ValueRO.YoungModulusGPa);
            fvs.Add(mat.ValueRO.ShearMPa);
            rhos.Add(mat.ValueRO.DensityTPerM3);
        }

        int n = ents.Count;
        if (n == 0) return;

        var links = new System.Collections.Generic.HashSet<long>();
        foreach (var pair in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>())
        {
            if (idxOf.TryGetValue(pair.ValueRO.EntityA, out int ia) && idxOf.TryGetValue(pair.ValueRO.EntityB, out int ib))
                links.Add(StructuralLoadSolver.LinkKey(ia, ib));
        }

        var gridArr = grids.ToArray();
        var groundArr = grounds.ToArray();
        float fn = StructuralLoadSolver.NaturalFrequencyHz(gridArr, eGPa.ToArray(), rhos.ToArray(), groundArr);
        float r = fn > 0f ? StructuralLoadSolver.QuakeFreqHz / fn : 0f;
        float amplify = StructuralLoadSolver.Transmissibility(r, StructuralLoadSolver.QuakeDamping);
        float effectiveG = StructuralLoadSolver.PgaForLevel(VibeLevel) * amplify;
        StructuralLoadSolver.LastPgaG = StructuralLoadSolver.PgaForLevel(VibeLevel);
        StructuralLoadSolver.LastQuakeFnHz = fn;
        StructuralLoadSolver.LastQuakeAmplify = amplify;
        StructuralLoadSolver.LastQuakeEffectiveG = effectiveG;

        var axial = new float[n];
        var overhang = new bool[n];
        var util = new float[n];
        var dist = new int[n];
        StructuralLoadSolver.Solve(gridArr, weights.ToArray(), groundArr, links, fcs.ToArray(), fts.ToArray(), axial, overhang, util, effectiveG, dist, eGPa: eGPa.ToArray(), fvMPa: fvs.ToArray());

        int failedBlocks = 0;
        for (int i = 0; i < n; i++) if (!groundArr[i] && util[i] > 1.0f) failedBlocks++;
        if (failedBlocks == 0) return;

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var velLookup = SystemAPI.GetComponentLookup<PhysicsVelocity>(false);
        int brokenJoints = 0;
        foreach (var (pair, jointEntity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
        {
            Entity eA = pair.ValueRO.EntityA; Entity eB = pair.ValueRO.EntityB;
            if (!idxOf.TryGetValue(eA, out int ia) || !idxOf.TryGetValue(eB, out int ib)) continue;
            if (!FailedSection(ia, ib, gridArr, util, groundArr, overhang, dist) && !FailedSection(ib, ia, gridArr, util, groundArr, overhang, dist)) continue;

            ecb.DestroyEntity(jointEntity);
            brokenJoints++;
            if (velLookup.HasComponent(eA)) velLookup[eA] = new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero };
            if (velLookup.HasComponent(eB)) velLookup[eB] = new PhysicsVelocity { Linear = float3.zero, Angular = float3.zero };
        }
        ecb.Playback(state.EntityManager); ecb.Dispose();

        if (brokenJoints > 0)
            Debug.Log($"💥 [동적 지진] 진동수 {StructuralLoadSolver.QuakeFreqHz:0.0}Hz / 고유 {fn:0.0}Hz / 증폭 {amplify:0.00}배 / 유효 {effectiveG:0.00}g -> 파괴 블록 {failedBlocks}개, 끊은 조인트 {brokenJoints}개");
    }

    // i 블록의 파괴 단면을 잡고 있는 조인트가 i-j 조인트인가.
    private static bool FailedSection(int i, int j, int3[] grid, float[] util, bool[] ground, bool[] overhang, int[] dist)
    {
        if (ground[i] || util[i] <= 1.0f) return false;
        int3 d = grid[j] - grid[i];
        if (!overhang[i]) return d.x == 0 && d.y == -1 && d.z == 0;   // 바로 아래 블록과의 조인트
        return d.y == 0 && dist[j] == dist[i] - 1;                    // 지지 쪽으로 이어진 옆 조인트
    }

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
        
        int3[] localGridDirs = new int3[] { new int3(1, 0, 0), new int3(0, 0, 1), new int3(0, 1, 0) };
        float3[] localInternalDirs = new float3[] { new float3(1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0) };

        int newJointCount = 0;
        
        var matLookup = SystemAPI.GetComponentLookup<BlockMaterial>(true);

        for (int k = 0; k < keys.Length; k++)
        {
            int3 key = keys[k];
            Entity cur = gridMap[key];

            for (int d = 0; d < 3; d++)
            {
                if (gridMap.TryGetValue(key + localGridDirs[d], out Entity neighbor) && neighbor != cur)
                {
                    CreateMaterialJoint(ref buildEcb, ref matLookup, cur, neighbor, localInternalDirs[d] * 3.0f);
                    newJointCount++;
                }
            }
        }

        buildEcb.Playback(state.EntityManager);
        buildEcb.Dispose();
        keys.Dispose();
        gridMap.Dispose();
        posMap.Dispose();
        
        UnityEngine.Debug.Log($"🔧 [조인트 전면 재구성] 기존 조인트 삭제 후 인접 블록 {newJointCount}쌍 재결합 완료!");
    }

    private void CreateMaterialJoint(ref EntityCommandBuffer ecb, ref ComponentLookup<BlockMaterial> matLookup, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity();
        ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex());
        ecb.AddComponent<JointTag>(jointEntity);
        ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true));

        var joint = PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f));

        float freq = 30f, damp = 0.3f; 
        
        if (matLookup.HasComponent(entityA) && matLookup.HasComponent(entityB))
        {
            var matA = matLookup[entityA];
            var matB = matLookup[entityB];
            if (matA.SpringFrequency > 0f && matB.SpringFrequency > 0f)
            {
                freq = (matA.SpringFrequency + matB.SpringFrequency) * 0.5f;
                damp = (matA.SpringDamping + matB.SpringDamping) * 0.5f;
            }
        }

        var constraints = joint.GetConstraints();
        for (int i = 0; i < constraints.Length; i++)
        {
            var c = constraints[i];
            if (c.Type == ConstraintType.Linear) 
            {
                c.SpringFrequency = freq;
                c.DampingRatio = damp;
            }
            constraints[i] = c;
        }
        joint.SetConstraints(constraints);

        ecb.AddComponent(jointEntity, joint);
    }  

    private void SaveVibrationExcel(NativeList<float3> positions, NativeList<float> stresses, NativeList<FixedString32Bytes> materials)
    {
        string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string vibDir = Path.Combine(StressBlockPaths.StressBlockDir, "vibration");
        if (!Directory.Exists(vibDir)) Directory.CreateDirectory(vibDir);

        string historyPath = Path.Combine(vibDir, "Vibration_All_" + dateStamp + ".csv");
        string currentPath = Path.Combine(StressBlockPaths.StressBlockDir, "CurrentStress.csv");

        var toolMap = new System.Collections.Generic.Dictionary<string, string>();
        if (File.Exists(currentPath))
        {
            var oldLines = File.ReadAllLines(currentPath);
            for (int i = 1; i < oldLines.Length; i++)
            {
                var c = oldLines[i].Split(',');
                if (c.Length >= 12) toolMap[c[0]] = c[10];
            }
        }

        string reinforcePath = Path.Combine(StressBlockPaths.StressBlockDir, "Reinforcement_Plan.csv");
        if (File.Exists(reinforcePath))
        {
            var rLines = File.ReadAllLines(reinforcePath);
            for (int i = 1; i < rLines.Length; i++)
            {
                var c = rLines[i].Split(',');
                if (c.Length < 12) continue;
                string k = c[0];
                if (toolMap.TryGetValue(k, out var existingTag) && existingTag == "Reinforcement" && c[10] != "Reinforcement") continue;
                toolMap[k] = c[10];
            }
        }

        string lastBuildPath = Path.Combine(StressBlockPaths.StressBlockDir, "Last_Building.csv");
        if (File.Exists(lastBuildPath))
        {
            var lLines = File.ReadAllLines(lastBuildPath);
            for (int i = 1; i < lLines.Length; i++)
            {
                var c = lLines[i].Split(',');
                if (c.Length < 12) continue;
                string k = c[0];
                if (toolMap.TryGetValue(k, out var existingTag2) && existingTag2 == "Reinforcement" && c[10] != "Reinforcement") continue;
                toolMap[k] = c[10];
            }
        }

var bpManager = UnityEngine.Object.FindFirstObjectByType<BlueprintManager>();

// 🆕 [바닥/벽 2-pass 필터] 진동 전 원본 배치 기준으로 미리 판정
var allBlocksForFloorCheck = new System.Collections.Generic.List<(float x, float y, float z, string id)>();
var existingBlocksForFloorCheck = new System.Collections.Generic.HashSet<string>();
for (int fi = 0; fi < positions.Length; fi++)
{
    float3 fp = positions[fi];
    string fid = GridUtility.ToBlockID(fp);
    allBlocksForFloorCheck.Add((fp.x, fp.y, fp.z, fid));
    existingBlocksForFloorCheck.Add(fid);
}
var floorSet = GridUtility.ComputeFloorBlocks(allBlocksForFloorCheck, existingBlocksForFloorCheck);

System.Collections.Generic.List<string> currentLines = new System.Collections.Generic.List<string>();
currentLines.Add("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type");

        for (int i = 0; i < positions.Length; i++)
        {
            float3 pos = positions[i]; float stress = stresses[i]; string mat = materials[i].ToString();

            // ⭐ GridUtility를 사용한 클린 ID 포맷팅
            string id = GridUtility.ToBlockID(pos);

            string posX = stress >= 2.0f ? "DESTROYED" : pos.x.ToString("F2");
            string posY = stress >= 2.0f ? "DESTROYED" : pos.y.ToString("F2");
            string posZ = stress >= 2.0f ? "DESTROYED" : pos.z.ToString("F2");

string risk = stress >= 2.0f ? "Quake_Destroyed" : (stress >= 0.5f ? "Quake_Danger" : "Safe");
string pres = stress >= 2.0f ? "Y" : (stress >= 0.5f ? "Y" : "N");
string toolTag = toolMap.ContainsKey(id) ? toolMap[id] : (bpManager != null ? bpManager.GetToolName(id) : "Existing");
string typeStr = toolTag == "Reinforcement" ? "Reinforcement" : (floorSet.Contains(id) ? "Floor" : "Wall");

            string lineData = id + "," + posX + "," + posY + "," + posZ + "," + stress.ToString("F2") + "," + risk + "," + pres + "," + mat + ",0.0,0.0," + toolTag + "," + typeStr;
            currentLines.Add(lineData);
        }
        
        string saveHistoryPath = historyPath;
        string saveCurrentPath = currentPath;
        System.Collections.Generic.List<string> linesToWrite = new System.Collections.Generic.List<string>(currentLines);

        System.Threading.Tasks.Task.Run(() =>
        {
            File.WriteAllLines(saveHistoryPath, linesToWrite);
            File.WriteAllLines(saveCurrentPath, linesToWrite);
        });
    }
}
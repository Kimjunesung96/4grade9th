using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Collections;
using Unity.Rendering;

public struct GhostBlockTag : IComponentData { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    private float3 dragStartPos;
    private float3 dragEndPos;
    private int currentBuildMode;
    private float guideHeight;
    private bool isGuideActive;
    private int nextStructureID;

    private bool isCenterMoved;
    private float3 customCenterPos;

    private NativeList<BlobAssetReference<Unity.Physics.Collider>> _createdColliders;

    private bool isYMode;
    private NativeList<float3> blueprintOffsets;

    private int loadDelayTimer;

    private string GetToolName(int mode)
    {
        switch (mode)
        {
            case 1: return "1_Solid_Wall";
            case 2: return "2_Empty_Frame";
            case 3: return "3_Circular_Pattern";
            case 4: return "4_Pyramid";
            case 5: return "5_Cone";
            default: return "Unknown_Tool";
        }
    }

    public void OnCreate(ref SystemState state)
    {
        currentBuildMode = 1;
        guideHeight = 1f;
        isGuideActive = false;
        nextStructureID = 1;
        isCenterMoved = false;
        customCenterPos = float3.zero;
        _createdColliders = new NativeList<BlobAssetReference<Unity.Physics.Collider>>(Allocator.Persistent);

        isYMode = false;
        blueprintOffsets = new NativeList<float3>(Allocator.Persistent);

        loadDelayTimer = 0;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_createdColliders.IsCreated)
        {
            foreach (var col in _createdColliders) { if (col.IsCreated) col.Dispose(); }
            _createdColliders.Dispose();
        }
        if (blueprintOffsets.IsCreated) blueprintOffsets.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!UnityEngine.Application.isPlaying || Camera.main == null) return;
        if (!SystemAPI.HasSingleton<BuilderStateData>()) return;
        var builderState = SystemAPI.GetSingletonRW<BuilderStateData>();
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsSingleton)) return;

        SpawnerData spawnerData = default;
        bool hasSpawner = false;
        foreach (var data in SystemAPI.Query<RefRO<SpawnerData>>()) { spawnerData = data.ValueRO; hasSpawner = true; break; }
        if (!hasSpawner) return;

        PhysicsWorld physicsWorld = physicsSingleton.PhysicsWorld;

        // [R키]: 현장 대청소 (하지만 Y도면은 유지!)
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (LogManager.Instance != null) { LogManager.Instance.OnPressRKey(); }
            isCenterMoved = false;
            UnityEngine.Debug.Log("🪓 [현장 철거] R키 작동! 건물은 철거되지만 Y도면은 유지됩니다. G키로 재타설 가능합니다!");
        }

        // [Y키]: 보강 도면 스마트 로드
        if (Input.GetKeyDown(KeyCode.Y)) { loadDelayTimer = 5; }

        if (loadDelayTimer > 0)
        {
            loadDelayTimer--;
            if (loadDelayTimer == 0)
            {
                string planCsvPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");

                if (System.IO.File.Exists(planCsvPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(planCsvPath);
                    float minX = 99999f, minZ = 99999f, maxX = -99999f, maxZ = -99999f;
                    System.Collections.Generic.List<float3> tempList = new System.Collections.Generic.List<float3>();

                    // ⭐ 중복 블록 입구컷 명부!
                    System.Collections.Generic.HashSet<string> loadedIDs = new System.Collections.Generic.HashSet<string>();

                    for (int i = 1; i < lines.Length; i++)
                    {
                        string[] cols = lines[i].Split(',');
                        if (cols.Length >= 5 && (cols[4] == "Reinforcement" || cols[4] == "Existing"))
                        {
                            string currentID = cols[0];

                            // ⛔ 중복 ID 컷!
                            if (loadedIDs.Contains(currentID)) continue;

                            string[] idParts = currentID.Split('_');
                            if (idParts.Length >= 3)
                            {
                                float x = float.Parse(idParts[0]) / 10f;
                                float z = float.Parse(idParts[1]) / 10f;
                                float y = float.Parse(idParts[2]) / 10f;

                                tempList.Add(new float3(x, y, z));
                                loadedIDs.Add(currentID);

                                if (x < minX) minX = x; if (x > maxX) maxX = x;
                                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                            }
                        }
                    }

                    if (tempList.Count > 0)
                    {
                        isYMode = true;
                        blueprintOffsets.Clear();
                        tempList.Sort((a, b) => a.y.CompareTo(b.y));

                        float centerX = (minX + maxX) / 2f; float centerZ = (minZ + maxZ) / 2f;
                        centerX = (float)System.Math.Round(centerX / 3.0f) * 3.0f;
                        centerZ = (float)System.Math.Round(centerZ / 3.0f) * 3.0f;

                        foreach (var pos in tempList)
                        {
                            blueprintOffsets.Add(new float3(pos.x - centerX, pos.y, pos.z - centerZ));
                        }
                        UnityEngine.Debug.Log($"🏗️ [스마트 로드 완] {tempList.Count}개 준비 완료! (⛔ 자동 중복 컷 적용)");
                    }
                }
            }
        }

        // ==========================================================
        // ⭐ 소장님 특명: 지진 테스트(B모드) 중일 때는 스포너 올스톱!!
        // ==========================================================
        bool isBMode = VibrationTestSystem.IsBModeActive;

        // B모드가 아닐 때(평상시)만 휠이랑 마우스 클릭이 작동하게 막아버립니다!
        if (!isBMode)
        {
            if (Input.mouseScrollDelta.y != 0) { builderState.ValueRW.GuideHeight += Input.mouseScrollDelta.y; if (builderState.ValueRW.GuideHeight < 1f) builderState.ValueRW.GuideHeight = 1f; }

            if (Input.GetMouseButtonDown(0)) isCenterMoved = false;

            if (Input.GetMouseButtonDown(1))
            {
                UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastInput rayInput = new RaycastInput { Start = ray.origin, End = ray.origin + ray.direction * 500f, Filter = CollisionFilter.Default };
                if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit)) { customCenterPos = hit.Position; isCenterMoved = true; }
            }
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) { builderState.ValueRW.CurrentMode = 1; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { builderState.ValueRW.CurrentMode = 2; }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { builderState.ValueRW.CurrentMode = 3; }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { builderState.ValueRW.CurrentMode = 4; }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { builderState.ValueRW.CurrentMode = 5; }
        if (Input.GetKeyDown(KeyCode.Return)) { if (LogManager.Instance != null) { LogManager.Instance.SaveToMaster(); UnityEngine.Debug.Log("⭐⭐⭐ 마스터 도면 박제 완료!!"); } }

        currentBuildMode = builderState.ValueRO.CurrentMode;
        guideHeight = builderState.ValueRO.GuideHeight;
        dragStartPos = builderState.ValueRO.GuideStartPos;
        dragEndPos = builderState.ValueRO.GuideEndPos;

        // ====================================================================
        // 🔄 도면 90도 회전 시스템 (Q: 반시계, E: 시계) - 근본 단축키 적용!
        // ====================================================================
        if (isYMode && !isBMode)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++)
                {
                    float3 offset = blueprintOffsets[i];
                    blueprintOffsets[i] = new float3(-offset.z, offset.y, offset.x);
                }
                UnityEngine.Debug.Log("🔄 [도면 회전] 반시계 방향(Q) 90도 턴! (G를 눌러 타설하세요)");
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++)
                {
                    float3 offset = blueprintOffsets[i];
                    blueprintOffsets[i] = new float3(offset.z, offset.y, -offset.x);
                }
                UnityEngine.Debug.Log("🔄 [도면 회전] 시계 방향(E) 90도 턴! (G를 눌러 타설하세요)");
            }
        }

        float3 defaultCenter = new float3((dragStartPos.x + dragEndPos.x) / 2f, 0, (dragStartPos.z + dragEndPos.z) / 2f);
        float3 finalCenter = isCenterMoved ? new float3(customCenterPos.x, 0, customCenterPos.z) : defaultCenter;
        float3 centerOffset = finalCenter - defaultCenter;

        float3 actualStartPos = dragStartPos + centerOffset;
        float3 actualEndPos = dragEndPos + centerOffset;
        float blockSize = 3.0f;
        float snapStart = 1.5f;

        actualStartPos.x = (float)System.Math.Round(actualStartPos.x / snapStart) * snapStart;
        actualStartPos.z = (float)System.Math.Round(actualStartPos.z / snapStart) * snapStart;

        float rawEndX = dragEndPos.x + centerOffset.x;
        float rawEndZ = dragEndPos.z + centerOffset.z;

        float diffX = rawEndX - actualStartPos.x;
        float diffZ = rawEndZ - actualStartPos.z;

        actualEndPos.x = actualStartPos.x + ((float)System.Math.Round(diffX / blockSize) * blockSize);
        actualEndPos.z = actualStartPos.z + ((float)System.Math.Round(diffZ / blockSize) * blockSize);

        finalCenter.x = (float)System.Math.Round(finalCenter.x / snapStart) * snapStart;
        finalCenter.z = (float)System.Math.Round(finalCenter.z / snapStart) * snapStart;
        finalCenter.x = (float)System.Math.Round(finalCenter.x / blockSize) * blockSize;
        finalCenter.z = (float)System.Math.Round(finalCenter.z / blockSize) * blockSize;

        isGuideActive = (math.lengthsq(actualStartPos) > 0.001f || math.lengthsq(actualEndPos) > 0.001f) && actualStartPos.x > -10000f;

        var aiBuilder = UnityEngine.Object.FindFirstObjectByType<AI_Builder>();
        if (aiBuilder != null && aiBuilder.isAiHologramActive) { isGuideActive = false; }

        // ==========================================================
        // ⭐ 핵심: B모드 켜져있으면 G키랑 F키를 'False(안 누름)'로 강제 조작해버림!
        // ==========================================================
        // ==========================================================
        // ⭐ 핵심: B모드(지진)나 N모드(충격파)가 켜져있으면 건설용 G, F키 봉인!
        // ==========================================================
        bool isNMode = ShockwaveTestSystem.IsNModeActive;
        bool isFKeyPressed = !isBMode && !isNMode && Input.GetKeyDown(KeyCode.F);
        bool isGKeyPressed = !isBMode && !isNMode && Input.GetKeyDown(KeyCode.G);

        // ====================================================================
        // 🚀 스마트 일괄 타설 (Y키 -> G키) 전용 초고속 빌더!
        // ====================================================================
        if (isYMode && isGKeyPressed)
        {
            float3 baseCenter = new float3((float)System.Math.Round(customCenterPos.x / blockSize) * blockSize, 0, (float)System.Math.Round(customCenterPos.z / blockSize) * blockSize);
            var bpManager = UnityEngine.Object.FindFirstObjectByType<BlueprintManager>();
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            Unity.Collections.NativeHashMap<int3, Entity> gridMap = new Unity.Collections.NativeHashMap<int3, Entity>(blueprintOffsets.Length, Allocator.Temp);
            Unity.Collections.NativeHashMap<Entity, float3> posMap = new Unity.Collections.NativeHashMap<Entity, float3>(blueprintOffsets.Length, Allocator.Temp);

            foreach (var offset in blueprintOffsets)
            {
                float3 pos = new float3(baseCenter.x + offset.x, offset.y, baseCenter.z + offset.z);
                var instance = ecb.Instantiate(spawnerData.Prefab);
                ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, blockSize - 0.05f));
                ecb.AddComponent<BlockTag>(instance);
                ecb.AddComponent<BlockStress>(instance);
                ecb.AddComponent(instance, new StructureID { Value = nextStructureID });
                ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });

                if (pos.y <= 2.0f)
                {
                    PhysicsMass prefabMass = SystemAPI.GetComponent<PhysicsMass>(spawnerData.Prefab);
                    prefabMass.InverseMass = 0; prefabMass.InverseInertia = float3.zero;
                    ecb.SetComponent(instance, prefabMass);
                }

                int3 key = new int3((int)math.floor(pos.x / 3f + 0.5f), (int)math.floor(pos.y / 3f + 0.5f), (int)math.floor(pos.z / 3f + 0.5f));
                gridMap.TryAdd(key, instance); posMap.TryAdd(instance, pos);

                if (bpManager != null)
                {
                    string id = bpManager.VectorToID(new UnityEngine.Vector3(pos.x, pos.y, pos.z));
                    bpManager.AddReinforcementBlock(id, "Reinforcement", new UnityEngine.Vector3(pos.x, pos.y, pos.z));
                }
            }

            var keys = gridMap.GetKeyArray(Allocator.Temp);

            // ⭐ 1. 내부 철근망 (중복 방지용 3방향 뻗기 -> 결과적으로 6방향 완성)
            float3[] internalDirs = new float3[] { math.right(), math.up(), math.forward() };
            int3[] gridDirs = new int3[] { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1) };

            foreach (var key in keys)
            {
                Entity currentEntity = gridMap[key];
                float3 currentPos = posMap[currentEntity];

                for (int d = 0; d < 3; d++)
                {
                    if (gridMap.TryGetValue(key + gridDirs[d], out Entity neighbor))
                    {
                        CreateIndestructibleJoint(ref ecb, currentEntity, neighbor, internalDirs[d] * blockSize);
                    }
                }

                // ⭐ 2. 외부 용접 (기존 건물과 결합하기 위해 위/아래 앵커 고정)
                float3[] anchorDirs = new float3[] { math.up(), math.down() };
                for (int d = 0; d < 2; d++)
                {
                    RaycastInput ray = new RaycastInput { Start = currentPos, End = currentPos + anchorDirs[d] * 2.0f, Filter = CollisionFilter.Default };
                    if (physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit))
                    {
                        if (hit.Entity != Entity.Null && !posMap.ContainsKey(hit.Entity) && SystemAPI.HasComponent<BlockTag>(hit.Entity))
                        {
                            float3 hitPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                            CreateIndestructibleJoint(ref ecb, hit.Entity, currentEntity, currentPos - hitPos);
                        }
                    }
                }
            }

            keys.Dispose(); gridMap.Dispose(); posMap.Dispose();
            UnityEngine.Debug.Log($"🏗️ [스마트 보강 타설 완료] 랙 없이 즉시 배치 및 6방향 철근 뼈대 완성!");
            nextStructureID++;
            if (bpManager != null) bpManager.SaveBlueprint();
            isYMode = false; blueprintOffsets.Clear(); isGuideActive = false;
            return;
        }

        if (AI_Builder.buildQueue != null && AI_Builder.buildQueue.Count > 0)
        {
            var cmd = AI_Builder.buildQueue.Dequeue();
            actualStartPos = cmd.startPos; actualEndPos = cmd.endPos;
            currentBuildMode = cmd.mode; guideHeight = cmd.height;
            actualStartPos.y = cmd.exactY; actualEndPos.y = cmd.exactY;
            isGuideActive = true; isGKeyPressed = true; isFKeyPressed = false;
        }

        // ====================================================================
        // 🚀 일반 드래그 건축 및 렌더링 (5연발 스캐너 적용)
        // ====================================================================
        if (isGuideActive)
        {
            float previewY = actualStartPos.y;
            Entity tempEntity = Entity.Null;
            bool tempHit = false;

            CheckRay(physicsWorld, finalCenter.x, finalCenter.z, blockSize, ref previewY, ref tempEntity, ref tempHit);

            float snappedHighestY = (float)System.Math.Round(previewY / blockSize) * blockSize;
            float3 drawStartPos = actualStartPos; float3 drawEndPos = actualEndPos;
            drawStartPos.y = math.max(actualStartPos.y, snappedHighestY);
            drawEndPos.y = math.max(actualEndPos.y, snappedHighestY);

            DrawGuideWireframe(drawStartPos, drawEndPos, guideHeight, currentBuildMode, blockSize);
        }

        if (isGuideActive && (isFKeyPressed || isGKeyPressed))
        {
            bool isGhost = isFKeyPressed;
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var (ghostTag, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess()) { ecb.DestroyEntity(entity); }

            float highestY = -9999f;
            Entity hitEntity = Entity.Null;
            bool hitAnything = false;

            float3 startXZ = new float3(actualStartPos.x, 0, actualStartPos.z);
            float3 endXZ = new float3(actualEndPos.x, 0, actualEndPos.z);
            float distance = math.distance(startXZ, endXZ);
            float halfSize = blockSize / 2f;

            if (currentBuildMode == 1)
            {
                float3 mode1Start = startXZ + new float3(halfSize, 0, halfSize);
                float3 mode1End = endXZ + new float3(halfSize, 0, halfSize);
                float dist1 = math.distance(mode1Start, mode1End);
                float3 dir = dist1 < 0.1f ? new float3(1, 0, 0) : math.normalize(mode1End - mode1Start);
                int steps = (int)math.max(1, math.round(dist1 / blockSize));
                for (int i = 0; i <= steps; i++)
                {
                    float3 p = mode1Start + dir * (i * (dist1 / math.max(1, steps)));
                    CheckRay(physicsWorld, p.x, p.z, blockSize, ref highestY, ref hitEntity, ref hitAnything);
                }
            }
            else if (currentBuildMode == 2 || currentBuildMode == 4)
            {
                float minX = math.min(startXZ.x, endXZ.x); float maxX = math.max(startXZ.x, endXZ.x);
                float minZ = math.min(startXZ.z, endXZ.z); float maxZ = math.max(startXZ.z, endXZ.z);
                for (float x = minX; x <= maxX + 0.1f; x += blockSize)
                {
                    for (float z = minZ; z <= maxZ + 0.1f; z += blockSize)
                    {
                        if (currentBuildMode == 2) { if (x > minX + (blockSize * 0.5f) && x < maxX - (blockSize * 0.5f) && z > minZ + (blockSize * 0.5f) && z < maxZ - (blockSize * 0.5f)) continue; }
                        CheckRay(physicsWorld, x + halfSize, z + halfSize, blockSize, ref highestY, ref hitEntity, ref hitAnything);
                    }
                }
            }
            else if (currentBuildMode == 3 || currentBuildMode == 5)
            {
                float radius = distance / 2.0f;
                float3 center = (startXZ + endXZ) / 2.0f;
                int baseRadiusCount = (int)math.floor(radius / blockSize);
                for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
                {
                    for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
                    {
                        if (math.sqrt(x * x + z * z) <= baseRadiusCount + 0.5f)
                        {
                            CheckRay(physicsWorld, center.x + (x * blockSize), center.z + (z * blockSize), blockSize, ref highestY, ref hitEntity, ref hitAnything);
                        }
                    }
                }
            }

            if (hitAnything)
            {
                float finalBuildY = math.round(math.max(highestY, actualStartPos.y) / 3.0f) * 3.0f;
                if (LogManager.Instance != null)
                {
                    string toolName = GetToolName(currentBuildMode);
                    string centerStr = $"[{(actualStartPos.x + actualEndPos.x) / 2f:F1}, {finalBuildY:F1}, {(actualStartPos.z + actualEndPos.z) / 2f:F1}]";
                    LogManager.Instance.AddLog(toolName, (int)math.round(guideHeight), isGhost ? "Key_F" : "Key_G", isGhost ? "Preview" : "Build", centerStr, 0, 0);
                }
                ExecuteBuild(ref state, spawnerData, actualStartPos, actualEndPos, finalBuildY, hitEntity, isGhost);
                if (!isGhost) isGuideActive = false;
            }
        }
    }

    private void ExecuteBuild(ref SystemState state, SpawnerData data, float3 actualStart, float3 actualEnd, float targetY, Entity hitEntity, bool isGhost)
    {
        bool hitExistingBlock = SystemAPI.HasComponent<BlockTag>(hitEntity);
        float3 hitEntityPos = hitExistingBlock ? SystemAPI.GetComponent<LocalTransform>(hitEntity).Position : float3.zero;

        switch (currentBuildMode)
        {
            case 1: BuildSolidWall(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, nextStructureID, isGhost); break;
            case 2: BuildEmptyFrame(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, nextStructureID, isGhost); break;
            case 3: BuildCircularPattern(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, nextStructureID, isGhost); break;
            case 4: BuildPyramid(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, nextStructureID, isGhost); break;
            case 5: BuildCone(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, nextStructureID, isGhost); break;
        }
        if (!isGhost) nextStructureID++;
    }

    // 🎯 [소장님 특명] 5연발 정밀 레이저 스캐너 (도넛 구멍 추락 방지 완벽 대응!)
    // 🎯 [소장님 특명] 25연발 융단폭격 레이저 스캐너 (서로 다른 모양 블록 완벽 인식)
    private void CheckRay(PhysicsWorld physicsWorld, float x, float z, float blockSize, ref float highestY, ref Entity hitEntity, ref bool hitAnything)
    {
        float half = (blockSize / 2f) * 0.95f;
        int gridSize = 5;
        float step = (half * 2f) / (gridSize - 1);

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                float px = (x - half) + (i * step);
                float pz = (z - half) + (j * step);
                float3 p = new float3(px, 100f, pz);

                RaycastInput rayInput = new RaycastInput { Start = p, End = p + new float3(0, -200f, 0), Filter = CollisionFilter.Default };
                if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
                {
                    hitAnything = true;
                    float snappedY = math.round(hit.Position.y / 3.0f) * 3.0f;
                    if (snappedY > highestY)
                    {
                        highestY = snappedY;
                        hitEntity = hit.Entity;
                    }
                }
            }
        }
    }

    private void DrawGuideWireframe(float3 start, float3 end, float heightParam, int mode, float blockSize)
    {
        Color color = Color.green;
        float floorHeight = blockSize;

        if (mode == 1 || mode == 2)
        {
            float minX = math.min(start.x, end.x); float maxX = math.max(start.x, end.x) + blockSize;
            float minZ = math.min(start.z, end.z); float maxZ = math.max(start.z, end.z) + blockSize;
            float minY = start.y; float maxY = start.y + (heightParam * floorHeight);

            Vector3 p1 = new Vector3(minX, minY, minZ); Vector3 p2 = new Vector3(maxX, minY, minZ);
            Vector3 p3 = new Vector3(maxX, minY, maxZ); Vector3 p4 = new Vector3(minX, minY, maxZ);

            Debug.DrawLine(p1, p2, color); Debug.DrawLine(p2, p3, color);
            Debug.DrawLine(p3, p4, color); Debug.DrawLine(p4, p1, color);

            if (heightParam > 0)
            {
                Vector3 t1 = new Vector3(minX, maxY, minZ); Vector3 t2 = new Vector3(maxX, maxY, minZ);
                Vector3 t3 = new Vector3(maxX, maxY, maxZ); Vector3 t4 = new Vector3(minX, maxY, maxZ);

                Debug.DrawLine(t1, t2, color); Debug.DrawLine(t2, t3, color);
                Debug.DrawLine(t3, t4, color); Debug.DrawLine(t4, t1, color);
                Debug.DrawLine(p1, t1, color); Debug.DrawLine(p2, t2, color);
                Debug.DrawLine(p3, t3, color); Debug.DrawLine(p4, t4, color);
            }
        }
        else if (mode == 3 || mode == 4 || mode == 5)
        {
            float3 center = (start + end) / 2f; center.x += (blockSize / 2f); center.z += (blockSize / 2f);
            float radius = (math.distance(start, end) / 2f) + (blockSize / 2f);
            float minY = start.y; float maxY = start.y + (heightParam * floorHeight);
            int segments = 24; float angleStep = (2f * math.PI) / segments;
            Vector3 prev = center + new float3(radius, 0, 0); prev.y = minY;

            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = center + new float3(math.cos(i * angleStep) * radius, 0, math.sin(i * angleStep) * radius);
                next.y = minY; Debug.DrawLine(prev, next, color); prev = next;
            }
            if (heightParam > 0) Debug.DrawLine(new Vector3(center.x, minY, center.z), new Vector3(center.x, maxY, center.z), color);
        }
    }

    // ====================================================================
    // 🛠️ 조인트(철근) 생성 함수
    // ====================================================================
    private void CreateIndestructibleJoint(ref EntityCommandBuffer ecb, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity();
        ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex());
        ecb.AddComponent<JointTag>(jointEntity);
        ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true));
        ecb.AddComponent(jointEntity, PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f)));
    }
}
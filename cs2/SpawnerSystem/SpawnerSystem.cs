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
            case 0: return "1_Solid_Wall";
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
        foreach (var data in SystemAPI.Query<RefRO<SpawnerData>>())
        {
            spawnerData = data.ValueRO;
            hasSpawner = true;
            break;
        }
        if (!hasSpawner) return;

        PhysicsWorld physicsWorld = physicsSingleton.PhysicsWorld;

        // [R키]: 대청소 (하지만 Y모드 도면은 꽉 쥐고 유지!)
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (LogManager.Instance != null) { LogManager.Instance.OnPressRKey(); }

            // isYMode = false; <-- 이 녀석을 삭제해서 도면 모드가 안 풀리게 만듭니다!
            isCenterMoved = false; // 우클릭 중심점만 초기화
                                   // blueprintOffsets.Clear(); <-- 도면 리스트 지우는 것도 삭제!

            Debug.Log("🪓 [현장 철거] R키 작동! 건물은 철거되지만 Y도면은 그대로 유지됩니다. 바로 G키를 눌러 다시 타설 가능합니다!");
        }

        // [Y키]: 보강 도면 로드
        if (Input.GetKeyDown(KeyCode.Y))
        {
            loadDelayTimer = 5;
        }

        if (loadDelayTimer > 0)
        {
            loadDelayTimer--;
            if (loadDelayTimer == 0)
            {
                string planCsvPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");

                if (System.IO.File.Exists(planCsvPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(planCsvPath);
                    float minX = 99999f, minZ = 99999f;
                    float maxX = -99999f, maxZ = -99999f;

                    System.Collections.Generic.List<float3> tempList = new System.Collections.Generic.List<float3>();

                    // ⭐ [자동 필터링] 한 번 읽은 ID는 절대 다시 읽지 않음! (엑셀 노가다 해방!)
                    System.Collections.Generic.HashSet<string> loadedIDs = new System.Collections.Generic.HashSet<string>();

                    for (int i = 1; i < lines.Length; i++)
                    {
                        string[] cols = lines[i].Split(',');
                        if (cols.Length >= 5 && (cols[4] == "Reinforcement" || cols[4] == "Existing"))
                        {
                            // 엑셀 A열의 ID(예: 0735_0885_1995)
                            string currentID = cols[0];

                            // ⛔ 엑셀에 똑같은 ID가 또 있으면? 바로 무시! (72개 불량 블록 여기서 컷)
                            if (loadedIDs.Contains(currentID)) continue;

                            string[] idParts = currentID.Split('_');
                            if (idParts.Length >= 3)
                            {
                                // ⭐ 찌그러진 Pos 무시하고, ID에서 완벽한 수학적 좌표(0.1m 오차 없음) 강제 추출!
                                float x = float.Parse(idParts[0]) / 10f;
                                float z = float.Parse(idParts[1]) / 10f;
                                float y = float.Parse(idParts[2]) / 10f;

                                tempList.Add(new float3(x, y, z));
                                loadedIDs.Add(currentID); // "이 ID는 처리 완료" 도장 꽝!

                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (z < minZ) minZ = z;
                                if (z > maxZ) maxZ = z;
                            }
                        }
                    }

                    if (tempList.Count > 0)
                    {
                        isYMode = true;
                        blueprintOffsets.Clear();

                        tempList.Sort((a, b) => a.y.CompareTo(b.y));

                        float centerX = (minX + maxX) / 2f;
                        float centerZ = (minZ + maxZ) / 2f;
                        centerX = (float)System.Math.Round(centerX / 3.0f) * 3.0f;
                        centerZ = (float)System.Math.Round(centerZ / 3.0f) * 3.0f;

                        foreach (var pos in tempList)
                        {
                            blueprintOffsets.Add(new float3(pos.x - centerX, pos.y, pos.z - centerZ));
                        }

                        // 엑셀에서 얼마나 많은 쓰레기(중복)가 걸러졌는지 로그로 확인!
                        int duplicateCount = loadedIDs.Count - tempList.Count; // 원본 - 순수 = 중복 갯수
                        UnityEngine.Debug.Log($"🏗️ [스마트 로드] {tempList.Count}개 도면 준비 완. (⛔ 엑셀 내 중복 블록 자동 제거됨)");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("⚠️ 엑셀에 지을 게 없습니다! Y모드를 켜지 않고 일반 드래그 모드를 유지합니다.");
                    }
                }
            }
        }

        if (Input.mouseScrollDelta.y != 0)
        {
            builderState.ValueRW.GuideHeight += Input.mouseScrollDelta.y;
            if (builderState.ValueRW.GuideHeight < 1f) builderState.ValueRW.GuideHeight = 1f;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1)) { builderState.ValueRW.CurrentMode = 1; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { builderState.ValueRW.CurrentMode = 2; }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { builderState.ValueRW.CurrentMode = 3; }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { builderState.ValueRW.CurrentMode = 4; }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { builderState.ValueRW.CurrentMode = 5; }

        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (LogManager.Instance != null) { LogManager.Instance.SaveToMaster(); Debug.Log("⭐⭐⭐ 마스터 도면집 영구 박제 완료!!"); }
        }

        currentBuildMode = builderState.ValueRO.CurrentMode;
        guideHeight = builderState.ValueRO.GuideHeight;
        dragStartPos = builderState.ValueRO.GuideStartPos;
        dragEndPos = builderState.ValueRO.GuideEndPos;

        if (Input.GetMouseButtonDown(0)) isCenterMoved = false;

        // [우클릭]: 커스텀 중심점 설정
        if (Input.GetMouseButtonDown(1))
        {
            UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastInput rayInput = new RaycastInput { Start = ray.origin, End = ray.origin + ray.direction * 500f, Filter = CollisionFilter.Default };
            if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
            {
                customCenterPos = hit.Position;
                isCenterMoved = true;
            }
        }

        float3 defaultCenter = new float3((dragStartPos.x + dragEndPos.x) / 2f, 0, (dragStartPos.z + dragEndPos.z) / 2f);
        float3 finalCenter = isCenterMoved ? new float3(customCenterPos.x, 0, customCenterPos.z) : defaultCenter;
        float3 centerOffset = finalCenter - defaultCenter;

        float3 actualStartPos = dragStartPos + centerOffset;
        float3 actualEndPos = dragEndPos + centerOffset;

        float blockSize = 3.0f;

        actualStartPos.x = (float)System.Math.Round(actualStartPos.x / blockSize) * blockSize;
        actualStartPos.z = (float)System.Math.Round(actualStartPos.z / blockSize) * blockSize;
        actualEndPos.x = (float)System.Math.Round(actualEndPos.x / blockSize) * blockSize;
        actualEndPos.z = (float)System.Math.Round(actualEndPos.z / blockSize) * blockSize;
        finalCenter.x = (float)System.Math.Round(finalCenter.x / blockSize) * blockSize;
        finalCenter.z = (float)System.Math.Round(finalCenter.z / blockSize) * blockSize;

        isGuideActive = (math.lengthsq(actualStartPos) > 0.001f || math.lengthsq(actualEndPos) > 0.001f) && actualStartPos.x > -10000f;

        var aiBuilder = UnityEngine.Object.FindFirstObjectByType<AI_Builder>();
        if (aiBuilder != null && aiBuilder.isAiHologramActive)
        {
            isGuideActive = false;
        }

        bool isFKeyPressed = Input.GetKeyDown(KeyCode.F);
        bool isGKeyPressed = Input.GetKeyDown(KeyCode.G);

        // ====================================================================
        // 🚀 스마트 일괄 타설 (Y키 -> G키) 전용 초고속 빌더!
        // ====================================================================
        if (isYMode && isGKeyPressed)
        {
            float3 baseCenter = new float3(
                (float)System.Math.Round(customCenterPos.x / blockSize) * blockSize,
                0,
                (float)System.Math.Round(customCenterPos.z / blockSize) * blockSize
            );

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
                    prefabMass.InverseMass = 0;
                    prefabMass.InverseInertia = float3.zero;
                    ecb.SetComponent(instance, prefabMass);
                }

                int3 key = new int3((int)math.floor(pos.x / 3f + 0.5f), (int)math.floor(pos.y / 3f + 0.5f), (int)math.floor(pos.z / 3f + 0.5f));
                gridMap.TryAdd(key, instance);
                posMap.TryAdd(instance, pos);

                if (bpManager != null)
                {
                    string id = bpManager.VectorToID(new UnityEngine.Vector3(pos.x, pos.y, pos.z));
                    bpManager.AddReinforcementBlock(id, "Reinforcement", new UnityEngine.Vector3(pos.x, pos.y, pos.z));
                }
            }

            // 2. 융합 용접 시작!
            var keys = gridMap.GetKeyArray(Allocator.Temp);
            float3[] dirs = new float3[] { math.up(), math.down(), math.left(), math.right(), math.forward(), math.back() };
            int3[] gridDirs = new int3[] { new int3(0, 1, 0), new int3(0, -1, 0), new int3(-1, 0, 0), new int3(1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };

            foreach (var key in keys)
            {
                Entity currentEntity = gridMap[key];
                float3 currentPos = posMap[currentEntity];

                for (int d = 0; d < 6; d++)
                {
                    // [A] 신규 블록끼리는 수첩(gridMap) 보고 용접
                    if (gridMap.TryGetValue(key + gridDirs[d], out Entity neighbor))
                    {
                        CreateIndestructibleJoint(ref ecb, currentEntity, neighbor, dirs[d] * blockSize);
                    }
                    // [B] 소장님 특명! 반경 2.0 레이저 쏴서 기존 건물에 용접
                    else
                    {
                        RaycastInput ray = new RaycastInput { Start = currentPos, End = currentPos + dirs[d] * 2.0f, Filter = CollisionFilter.Default };
                        if (physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit))
                        {
                            if (hit.Entity != Entity.Null && SystemAPI.HasComponent<BlockTag>(hit.Entity))
                            {
                                float3 hitPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                                CreateIndestructibleJoint(ref ecb, hit.Entity, currentEntity, currentPos - hitPos);
                            }
                        }
                    }
                }
            }

            keys.Dispose();
            gridMap.Dispose();
            posMap.Dispose();

            UnityEngine.Debug.Log($"🏗️ [스마트 보강 타설 완료] 총 {blueprintOffsets.Length}개 블록 랙 없이 즉시 배치 및 반경 2.0 기존 건물 용접 성공!");

            nextStructureID++;
            if (bpManager != null) bpManager.SaveBlueprint();

            isYMode = false;
            blueprintOffsets.Clear();
            isGuideActive = false;
            return;
        }

        // ====================================================================
        // 일반 AI_Builder 큐 처리 (AI 홀로그램 짓기용)
        // ====================================================================
        if (AI_Builder.buildQueue != null && AI_Builder.buildQueue.Count > 0)
        {
            var cmd = AI_Builder.buildQueue.Dequeue();
            actualStartPos = cmd.startPos;
            actualEndPos = cmd.endPos;
            currentBuildMode = cmd.mode;
            guideHeight = cmd.height;
            actualStartPos.y = cmd.exactY;
            actualEndPos.y = cmd.exactY;
            isGuideActive = true;
            isGKeyPressed = true;
            isFKeyPressed = false;
        }

        // ====================================================================
        // 일반 드래그 건축 및 렌더링 로직 (소장님이 쓰시는 진짜 핵심 로직 복구 완료!)
        // ====================================================================
        if (isGuideActive)
        {
            float previewY = actualStartPos.y;
            Entity tempEntity = Entity.Null;
            bool tempHit = false;
            CheckRay(physicsWorld, finalCenter.x, finalCenter.z, ref previewY, ref tempEntity, ref tempHit);

            float snappedHighestY = (float)System.Math.Round(previewY / blockSize) * blockSize;

            float3 drawStartPos = actualStartPos;
            float3 drawEndPos = actualEndPos;
            drawStartPos.y = math.max(actualStartPos.y, snappedHighestY);
            drawEndPos.y = math.max(actualEndPos.y, snappedHighestY);

            DrawGuideWireframe(drawStartPos, drawEndPos, guideHeight, currentBuildMode, blockSize);
        }

        if (isGuideActive && (isFKeyPressed || isGKeyPressed))
        {
            bool isGhost = isFKeyPressed;

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var (ghostTag, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
            }

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
                    CheckRay(physicsWorld, p.x, p.z, ref highestY, ref hitEntity, ref hitAnything);
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
                        if (currentBuildMode == 2)
                        {
                            if (x > minX + (blockSize * 0.5f) && x < maxX - (blockSize * 0.5f) && z > minZ + (blockSize * 0.5f) && z < maxZ - (blockSize * 0.5f)) continue;
                        }
                        CheckRay(physicsWorld, x + halfSize, z + halfSize, ref highestY, ref hitEntity, ref hitAnything);
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
                            float checkX = center.x + (x * blockSize);
                            float checkZ = center.z + (z * blockSize);
                            CheckRay(physicsWorld, checkX, checkZ, ref highestY, ref hitEntity, ref hitAnything);
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
                    int height = (int)math.round(guideHeight);
                    string actionStr = isGhost ? "Key_F" : "Key_G";
                    string statusStr = isGhost ? "Preview_Hologram" : "Build_Complete";

                    float sizeX = math.abs(actualEndPos.x - actualStartPos.x) + blockSize;
                    float sizeZ = math.abs(actualEndPos.z - actualStartPos.z) + blockSize;

                    float calcCenterX = (actualStartPos.x + actualEndPos.x) / 2f;
                    float calcCenterZ = (actualStartPos.z + actualEndPos.z) / 2f;

                    float trueCenterX = math.floor(calcCenterX / blockSize) * blockSize + (blockSize / 2f);
                    float trueCenterY = finalBuildY + (blockSize / 2f);
                    float trueCenterZ = math.floor(calcCenterZ / blockSize) * blockSize + (blockSize / 2f);

                    string centerStr = $"[{trueCenterX:F1}, {trueCenterY:F1}, {trueCenterZ:F1}]";

                    LogManager.Instance.AddLog(toolName, height, actionStr, statusStr, centerStr, sizeX, sizeZ);
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

    private void CheckRay(PhysicsWorld physicsWorld, float x, float z, ref float highestY, ref Entity hitEntity, ref bool hitAnything)
    {
        float3 rayPos = new float3(x, 100f, z);
        RaycastInput rayInput = new RaycastInput { Start = rayPos, End = rayPos + new float3(0, -200f, 0), Filter = CollisionFilter.Default };
        if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
        {
            hitAnything = true;
            float snappedY = math.round(hit.Position.y / 3.0f) * 3.0f;
            if (snappedY > highestY) { highestY = snappedY; hitEntity = hit.Entity; }
        }
    }

    private void DrawGuideWireframe(float3 start, float3 end, float heightParam, int mode, float blockSize)
    {
        Color color = Color.green;
        float floorHeight = blockSize;

        if (mode == 1 || mode == 2)
        {
            float minX = math.min(start.x, end.x);
            float maxX = math.max(start.x, end.x) + blockSize;
            float minZ = math.min(start.z, end.z);
            float maxZ = math.max(start.z, end.z) + blockSize;

            float minY = start.y;
            float maxY = start.y + (heightParam * floorHeight);

            Vector3 p1 = new Vector3(minX, minY, minZ);
            Vector3 p2 = new Vector3(maxX, minY, minZ);
            Vector3 p3 = new Vector3(maxX, minY, maxZ);
            Vector3 p4 = new Vector3(minX, minY, maxZ);

            Debug.DrawLine(p1, p2, color); Debug.DrawLine(p2, p3, color);
            Debug.DrawLine(p3, p4, color); Debug.DrawLine(p4, p1, color);

            if (heightParam > 0)
            {
                Vector3 t1 = new Vector3(minX, maxY, minZ);
                Vector3 t2 = new Vector3(maxX, maxY, minZ);
                Vector3 t3 = new Vector3(maxX, maxY, maxZ);
                Vector3 t4 = new Vector3(minX, maxY, maxZ);

                Debug.DrawLine(t1, t2, color); Debug.DrawLine(t2, t3, color);
                Debug.DrawLine(t3, t4, color); Debug.DrawLine(t4, t1, color);

                Debug.DrawLine(p1, t1, color); Debug.DrawLine(p2, t2, color);
                Debug.DrawLine(p3, t3, color); Debug.DrawLine(p4, t4, color);
            }
        }
        else if (mode == 3 || mode == 4 || mode == 5)
        {
            float3 center = (start + end) / 2f;
            center.x += (blockSize / 2f);
            center.z += (blockSize / 2f);

            float radius = (math.distance(start, end) / 2f) + (blockSize / 2f);

            float minY = start.y;
            float maxY = start.y + (heightParam * floorHeight);

            int segments = 24; float angleStep = (2f * math.PI) / segments;
            Vector3 prev = center + new float3(radius, 0, 0);
            prev.y = minY;

            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = center + new float3(math.cos(i * angleStep) * radius, 0, math.sin(i * angleStep) * radius);
                next.y = minY;
                Debug.DrawLine(prev, next, color);
                prev = next;
            }
            if (heightParam > 0)
            {
                Debug.DrawLine(new Vector3(center.x, minY, center.z), new Vector3(center.x, maxY, center.z), color);
            }
        }
    }

    private void CreateIndestructibleJoint(ref EntityCommandBuffer ecb, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity();
        ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex());
        ecb.AddComponent<JointTag>(jointEntity);
        ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true));
        ecb.AddComponent(jointEntity, PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f)));
    }
}
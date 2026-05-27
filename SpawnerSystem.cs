using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Burst;
using System.IO;
using System.Text;
using System.Linq;

public struct GhostBlockTag : IComponentData { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    public static System.Collections.Generic.List<float3> ExternalBlueprintData = new System.Collections.Generic.List<float3>();

    // ⭐ 재질을 보존하기 위한 새로운 리스트 추가!
    public static System.Collections.Generic.List<string> blueprintMaterials = new System.Collections.Generic.List<string>();

    public static bool isOMode = false;
    public static bool isLMode = false;
    public static bool isUMode = false;

    public static bool isManualModeEnabled = true;
    public static bool isAimMoveEnabled = true;
    public static bool isHeightScrollEnabled = true;
    public static bool isPreviewFEnabled = true;
    public static bool isBuildGEnabled = true;
    public static bool isClearREnabled = true;

    public static float backupIDToQuery = -1f;

    private float3 dragStartPos;
    private float3 dragEndPos;
    private float currentBuildMode;
    private float guideHeight;
    private bool isGuideActive;
    private float nextStructureID;
    private bool isCenterMoved;
    private float3 customCenterPos;
    private NativeList<BlobAssetReference<Unity.Physics.Collider>> _createdColliders;
    private bool isYMode;

    private NativeList<float4> blueprintOffsets;
    public static float loadDelayTimer;
    private bool pendingJointCleanup;

    // 임시 파싱용 구조체
    private struct TempSpawnData
    {
        public float3 Pos;
        public float IsReinforce;
        public string MatName;
    }

    private string GetToolName(float mode)
    {
        if (math.abs(mode - 1f) < 0.01f) return "1_Solid_Wall";
        else if (math.abs(mode - 2f) < 0.01f) return "2_Empty_Frame";
        else if (math.abs(mode - 3f) < 0.01f) return "3_Circular_Pattern";
        else if (math.abs(mode - 4f) < 0.01f) return "4_Pyramid";
        else if (math.abs(mode - 5f) < 0.01f) return "5_Cone";
        else return "Unknown_Tool";
    }

    public void OnCreate(ref SystemState state)
    {
        currentBuildMode = 1f; guideHeight = 1f; isGuideActive = false; nextStructureID = 1f;
        isCenterMoved = false; customCenterPos = float3.zero;
        _createdColliders = new NativeList<BlobAssetReference<Unity.Physics.Collider>>(Allocator.Persistent);
        isYMode = false;
        blueprintOffsets = new NativeList<float4>(Allocator.Persistent);
        loadDelayTimer = 0f; isOMode = false; isLMode = false; isUMode = false; backupIDToQuery = -1f;

        blueprintMaterials.Clear();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_createdColliders.IsCreated) { foreach (var col in _createdColliders) { if (col.IsCreated) col.Dispose(); } _createdColliders.Dispose(); }
        if (blueprintOffsets.IsCreated) blueprintOffsets.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!UnityEngine.Application.isPlaying || Camera.main == null) return;

        if (pendingJointCleanup)
        {
            RemoveDuplicateJoints(ref state);
            pendingJointCleanup = false;
            UnityEngine.Debug.Log("🔧 [중복 조인트 정리] 완료!");
        }

        if (!SystemAPI.HasSingleton<BuilderStateData>()) return;
        var builderState = SystemAPI.GetSingletonRW<BuilderStateData>();
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsSingleton)) return;

        SpawnerData spawnerData = default;
        bool hasSpawner = false;
        foreach (var data in SystemAPI.Query<RefRO<SpawnerData>>()) { spawnerData = data.ValueRO; hasSpawner = true; break; }
        if (!hasSpawner) return;

        PhysicsWorld physicsWorld = physicsSingleton.PhysicsWorld;

        if (backupIDToQuery > -1f)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type");
            bool found = false;

            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BlockTag>())
            {
                float px = transform.ValueRO.Position.x;
                float py = transform.ValueRO.Position.y;
                float pz = transform.ValueRO.Position.z;
                string typeStr = py > 1.5f ? "Wall" : "Floor";
                string realId = $"{(int)math.round(px * 10f)}_{(int)math.round(pz * 10f)}_{(int)math.round(py * 10f)}";

                string lineData = realId + "," +
                                  px.ToString("F2") + "," +
                                  py.ToString("F2") + "," +
                                  pz.ToString("F2") + "," +
                                  "0.00,Safe,N,Concrete,0.0,0.0,Existing," + typeStr;

                csv.AppendLine(lineData);
                found = true;
            }

            if (found)
            {
                string path = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
                if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, csv.ToString());
                UnityEngine.Debug.Log("💾 [U모드 스냅샷] 현장의 전체 건축물 통째로 백업 완료! (12칸 표준 규격)");
            }
            backupIDToQuery = -1f;
        }

        if (isClearREnabled && Input.GetKeyDown(KeyCode.R)) { if (LogManager.Instance != null) LogManager.Instance.OnPressRKey(); isCenterMoved = false; UnityEngine.Debug.Log("🪓 [현장 철거] R키 작동! 건물은 철거되지만 도면은 유지됩니다."); }

        bool isBMode = VibrationTestSystem.IsBModeActive; bool isNMode = ShockwaveTestSystem.IsNModeActive;
        bool isAnySpecialMode = isOMode || isLMode || isYMode || isUMode;

        if (isAimMoveEnabled && !isBMode && !isNMode)
        {
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
        }

        if (!isBMode && !isNMode && !isAnySpecialMode)
        {
            if (isHeightScrollEnabled && Input.mouseScrollDelta.y != 0f) { builderState.ValueRW.GuideHeight += Input.mouseScrollDelta.y; if (builderState.ValueRW.GuideHeight < 1f) builderState.ValueRW.GuideHeight = 1f; }
            if (Input.GetMouseButtonDown(0)) isCenterMoved = false;

            if (isManualModeEnabled)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) builderState.ValueRW.CurrentMode = 1;
                if (Input.GetKeyDown(KeyCode.Alpha2)) builderState.ValueRW.CurrentMode = 2;
                if (Input.GetKeyDown(KeyCode.Alpha3)) builderState.ValueRW.CurrentMode = 3;
                if (Input.GetKeyDown(KeyCode.Alpha4)) builderState.ValueRW.CurrentMode = 4;
                if (Input.GetKeyDown(KeyCode.Alpha5)) builderState.ValueRW.CurrentMode = 5;
            }
        }

        if (Input.GetKeyDown(KeyCode.Return)) { if (LogManager.Instance != null) LogManager.Instance.SaveToMaster(); }

        currentBuildMode = (float)builderState.ValueRO.CurrentMode; guideHeight = builderState.ValueRO.GuideHeight; dragStartPos = builderState.ValueRO.GuideStartPos; dragEndPos = builderState.ValueRO.GuideEndPos;

        bool isFKeyPressed = isPreviewFEnabled && !isBMode && !isNMode && Input.GetKeyDown(KeyCode.F);
        bool isGKeyPressed = isBuildGEnabled && !isBMode && !isNMode && Input.GetKeyDown(KeyCode.G);

        bool isAnyBlueprintMode = isOMode || isLMode || isUMode;
        bool triggerSpecialGhost = (isAnyBlueprintMode || isYMode) && (isFKeyPressed || (isAimMoveEnabled && Input.GetMouseButtonDown(1)));
        bool triggerSpecialReal = (isAnyBlueprintMode || isYMode) && isGKeyPressed;

        if (triggerSpecialGhost || triggerSpecialReal)
        {
            float countF = isYMode ? (float)blueprintOffsets.Length : (float)ExternalBlueprintData.Count;
            if (countF > 0f)
            {
                var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                foreach (var (ghostTag, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess()) { ecb.DestroyEntity(entity); }

                float3 baseCenter = new float3(math.round((customCenterPos.x - 1.5f) / 3.0f) * 3.0f + 1.5f, 0f, math.round((customCenterPos.z - 1.5f) / 3.0f) * 3.0f + 1.5f);
                var gridMap = new NativeHashMap<int3, Entity>((int)countF, Allocator.Temp);
                var posMap = new NativeHashMap<Entity, float3>((int)countF, Allocator.Temp);

                for (int i = 0; i < (int)countF; i++)
                {
                    float3 posOffset;
                    bool isReinforceBlock = false;

                    if (isYMode)
                    {
                        float4 data = blueprintOffsets.ElementAt(i);
                        posOffset = new float3(data.x, data.y, data.z);
                        isReinforceBlock = data.w > 0.5f;
                    }
                    else
                    {
                        posOffset = ExternalBlueprintData.ElementAt(i);
                    }

                    float3 finalPos = baseCenter + posOffset;

                    var instance = ecb.Instantiate(spawnerData.Prefab);
                    ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(finalPos, quaternion.identity, 2.95f));

                    // ⭐ 핵심 로직 적용! (CSV에서 읽어온 실제 재료 데이터 사용)
                    string matName = "Concrete";
                    if (isYMode && i < blueprintMaterials.Count)
                    {
                        matName = blueprintMaterials.ElementAt(i);
                    }
                    else
                    {
                        matName = isReinforceBlock ? "Steel" : "Concrete";
                    }

                    ecb.AddComponent(instance, new BlockMaterial { MaterialName = matName });

                    float hp = isReinforceBlock ? 2000f : 1000f;
                    float def = isReinforceBlock ? 600f : 400f;
                    float4 bColor = isReinforceBlock ? new float4(0.2f, 0.5f, 1.0f, 1f) : new float4(0.7f, 0.7f, 0.7f, 1f);

                    // MaterialDataManager 연동
                    if (MaterialDataManager.Instance != null && MaterialDataManager.Instance.MaterialDict.TryGetValue(matName, out var spec))
                    {
                        hp = spec.BaseHP;
                        def = math.min(spec.Tensile, spec.Compressive);
                        bColor = new float4(spec.Color.r, spec.Color.g, spec.Color.b, spec.Color.a);
                    }
                    else
                    {
                        if (matName.Contains("Steel")) bColor = new float4(0.2f, 0.5f, 1.0f, 1.0f);
                        else if (matName.Contains("Wood") || matName.Contains("Timber")) bColor = new float4(0.6f, 0.4f, 0.2f, 1.0f);
                        else if (matName.Contains("Brick")) bColor = new float4(0.8f, 0.3f, 0.2f, 1.0f);
                    }

                    if (triggerSpecialGhost)
                    {
                        ecb.AddComponent<GhostBlockTag>(instance);
                        ecb.RemoveComponent<PhysicsCollider>(instance);
                        ecb.RemoveComponent<PhysicsVelocity>(instance);
                        ecb.RemoveComponent<PhysicsMass>(instance);

                        bColor.w = isReinforceBlock ? 0.8f : 0.4f;
                        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = bColor });
                    }
                    else
                    {
                        ecb.AddComponent<BlockTag>(instance);
                        ecb.AddComponent<BlockStress>(instance);
                        ecb.AddComponent(instance, new StructureID { Value = (int)nextStructureID });

                        ecb.AddComponent(instance, new BlockHealth { MaxHP = hp, CurrentHP = hp, Defense = def });
                        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = bColor });

                        if (finalPos.y <= 2.0f) { PhysicsMass m = SystemAPI.GetComponent<PhysicsMass>(spawnerData.Prefab); m.InverseMass = 0f; m.InverseInertia = float3.zero; ecb.SetComponent(instance, m); }

                        int3 key = new int3((int)math.floor(finalPos.x / 3f + 0.5f), (int)math.floor(finalPos.y / 3f + 0.5f), (int)math.floor(finalPos.z / 3f + 0.5f));
                        gridMap.TryAdd(key, instance);
                        posMap.TryAdd(instance, finalPos);

                        if (isYMode)
                        {
                            var bpManager = UnityEngine.Object.FindFirstObjectByType<BlueprintManager>();
                            if (bpManager != null) { string id = bpManager.VectorToID(new UnityEngine.Vector3(finalPos.x, finalPos.y, finalPos.z)); bpManager.AddReinforcementBlock(id, "Reinforcement", new UnityEngine.Vector3(finalPos.x, finalPos.y, finalPos.z)); }
                        }
                    }
                }

                if (triggerSpecialReal)
                {
                    var keys = gridMap.GetKeyArray(Allocator.Temp);
                    int3[] gridDirs = { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1) };
                    float3[] internalDirs = { math.right(), math.up(), math.forward() };

                    foreach (var key in keys)
                    {
                        Entity cur = gridMap[key]; float3 curPos = posMap[cur];
                        for (int d = 0; d < 3; d++) { if (gridMap.TryGetValue(key + gridDirs[d], out Entity neighbor)) CreateIndestructibleJoint(ref ecb, cur, neighbor, internalDirs[d] * 3.0f); }
                        foreach (var anchorDir in new float3[] { math.up(), math.down() }) { RaycastInput ray = new RaycastInput { Start = curPos, End = curPos + anchorDir * 2.0f, Filter = CollisionFilter.Default }; if (physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit)) { if (hit.Entity != Entity.Null && !posMap.ContainsKey(hit.Entity) && SystemAPI.HasComponent<BlockTag>(hit.Entity)) { float3 hitPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position; CreateIndestructibleJoint(ref ecb, hit.Entity, cur, curPos - hitPos); } } }
                    }
                    keys.Dispose();

                    if (isYMode)
                    {
                        var bpManager = UnityEngine.Object.FindFirstObjectByType<BlueprintManager>();
                        if (bpManager != null) bpManager.SaveBlueprint();

                        blueprintOffsets.Clear();
                        blueprintMaterials.Clear(); // ⭐ 타설 끝났으니 재료 리스트도 깔끔하게 비움
                        isYMode = false;
                        UnityEngine.Debug.Log("🏗️ [보강 공사] 튼튼하게 기존 건물과 보강 철근이 융합되었습니다!");
                    }
                    else
                    {
                        ExternalBlueprintData.Clear();
                        isOMode = false;
                        isLMode = false;
                        isUMode = false;
                        UnityEngine.Debug.Log("🏗️ [도면 공사] O/U 복층 도면 타설 완료!");
                    }

                    nextStructureID += 1f;
                    pendingJointCleanup = true;
                }
                gridMap.Dispose(); posMap.Dispose();
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Y))
        {
            if (BudgetUIManager.Instance != null)
                BudgetUIManager.Instance.OnYKeyPressed();
        }

        if (loadDelayTimer > 0f)
        {
            loadDelayTimer -= 1f;
            if (loadDelayTimer <= 0f)
            {
                string planCsvPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
                if (File.Exists(planCsvPath))
                {
                    var linesList = File.ReadAllLines(planCsvPath).ToList();
                    float minX = 99999f, minZ = 99999f, maxX = -99999f, maxZ = -99999f;

                    System.Collections.Generic.List<TempSpawnData> tempList = new System.Collections.Generic.List<TempSpawnData>();
                    System.Collections.Generic.HashSet<string> loadedIDs = new System.Collections.Generic.HashSet<string>();

                    for (int i = 1; i < linesList.Count; i++)
                    {
                        var cols = linesList.ElementAt(i).Split(',').ToList();

                        if (cols.Count >= 12 && (cols.ElementAt(10) == "Reinforcement" || cols.ElementAt(10) == "Existing"))
                        {
                            var idParts = cols.ElementAt(0).Split('_').ToList();
                            if (idParts.Count >= 3)
                            {
                                float px = float.Parse(idParts.ElementAt(0)) / 10f;
                                float pz = float.Parse(idParts.ElementAt(1)) / 10f;
                                float py = float.Parse(idParts.ElementAt(2)) / 10f;
                                float x = math.floor((px - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
                                float z = math.floor((pz - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
                                float y = math.floor((py - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;

                                string uniqueKey = x.ToString() + "_" + y.ToString() + "_" + z.ToString();
                                if (loadedIDs.Contains(uniqueKey)) continue;

                                float isReinforce = cols.ElementAt(10) == "Reinforcement" ? 1f : 0f;
                                string readMat = cols.ElementAt(7).Replace("\0", "").Trim(); // ⭐ 재질 완벽 파싱

                                tempList.Add(new TempSpawnData { Pos = new float3(x, y, z), IsReinforce = isReinforce, MatName = readMat });
                                loadedIDs.Add(uniqueKey);

                                if (x < minX) minX = x; if (x > maxX) maxX = x;
                                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                            }
                        }
                    }
                    if (tempList.Count > 0)
                    {
                        isYMode = true;

                        blueprintOffsets.Clear();
                        blueprintMaterials.Clear();

                        // Y축 기준으로 가지런히 정렬!
                        tempList.Sort((a, b) => a.Pos.y.CompareTo(b.Pos.y));

                        float centerX = math.round(((minX + maxX) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        float centerZ = math.round(((minZ + maxZ) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        customCenterPos = new float3(centerX, 0f, centerZ);
                        isCenterMoved = true;

                        foreach (var data in tempList)
                        {
                            blueprintOffsets.Add(new float4(data.Pos.x - centerX, data.Pos.y, data.Pos.z - centerZ, data.IsReinforce));
                            blueprintMaterials.Add(data.MatName); // ⭐ 재질도 함께 장전!
                        }
                        UnityEngine.Debug.Log($"🏗️ [스마트 로드 완] {tempList.Count}개의 구조물과 재질 데이터가 장전되었습니다! F키로 확인해 보세요.");
                    }
                }
            }
        }

        if (isYMode && !isBMode)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++)
                {
                    float4 offset = blueprintOffsets.ElementAt(i);
                    blueprintOffsets[i] = new float4(-offset.z, offset.y, offset.x, offset.w);
                }
                UnityEngine.Debug.Log("🔄 Y도면이 왼쪽[Q]으로 90도 회전했습니다!");
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++)
                {
                    float4 offset = blueprintOffsets.ElementAt(i);
                    blueprintOffsets[i] = new float4(offset.z, offset.y, -offset.x, offset.w);
                }
                UnityEngine.Debug.Log("🔄 Y도면이 오른쪽[E]으로 90도 회전했습니다!");
            }
        }

        float3 defaultCenter = new float3((dragStartPos.x + dragEndPos.x) / 2f, 0f, (dragStartPos.z + dragEndPos.z) / 2f);
        float3 finalCenter = isCenterMoved ? new float3(customCenterPos.x, 0f, customCenterPos.z) : defaultCenter;
        float3 centerOffset = finalCenter - defaultCenter;
        float3 actualStartPos = dragStartPos + centerOffset; float3 actualEndPos = dragEndPos + centerOffset;
        float blockSize = 3.0f;

        actualStartPos.x = (float)System.Math.Round((actualStartPos.x - 1.5f) / blockSize) * blockSize + 1.5f;
        actualStartPos.z = (float)System.Math.Round((actualStartPos.z - 1.5f) / blockSize) * blockSize + 1.5f;
        float rawEndX = dragEndPos.x + centerOffset.x; float rawEndZ = dragEndPos.z + centerOffset.z;
        float diffX = rawEndX - actualStartPos.x; float diffZ = rawEndZ - actualStartPos.z;
        actualEndPos.x = actualStartPos.x + ((float)System.Math.Round(diffX / blockSize) * blockSize); actualEndPos.z = actualStartPos.z + ((float)System.Math.Round(diffZ / blockSize) * blockSize);

        finalCenter.x = (float)System.Math.Round((finalCenter.x - 1.5f) / blockSize) * blockSize + 1.5f;
        finalCenter.z = (float)System.Math.Round((finalCenter.z - 1.5f) / blockSize) * blockSize + 1.5f;

        isGuideActive = (math.lengthsq(actualStartPos) > 0.001f || math.lengthsq(actualEndPos) > 0.001f) && actualStartPos.x > -10000f;
        var aiBuilder = UnityEngine.Object.FindFirstObjectByType<AI_Builder>();
        if (aiBuilder != null && aiBuilder.isAiHologramActive) { isGuideActive = false; }

        if (isGuideActive)
        {
            float previewY = actualStartPos.y; Entity tempEntity = Entity.Null; bool tempHit = false;
            CheckRay(physicsWorld, finalCenter.x, finalCenter.z, blockSize, ref previewY, ref tempEntity, ref tempHit);
            float snappedHighestY = (float)System.Math.Round(previewY / blockSize) * blockSize;
            float3 drawStartPos = actualStartPos; float3 drawEndPos = actualEndPos;
            drawStartPos.y = math.max(actualStartPos.y, snappedHighestY); drawEndPos.y = math.max(actualEndPos.y, snappedHighestY);
            DrawGuideWireframe(drawStartPos, drawEndPos, guideHeight, currentBuildMode, blockSize);
        }

        if (isGuideActive && (isFKeyPressed || isGKeyPressed))
        {
            bool isGhost = isFKeyPressed;
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var (ghostTag, entity) in SystemAPI.Query<RefRO<GhostBlockTag>>().WithEntityAccess()) { ecb.DestroyEntity(entity); }

            float highestY = -9999f; Entity hitEntity = Entity.Null; bool hitAnything = false;
            float3 startXZ = new float3(actualStartPos.x, 0f, actualStartPos.z); float3 endXZ = new float3(actualEndPos.x, 0f, actualEndPos.z);
            float distance = math.distance(startXZ, endXZ); float halfSize = blockSize / 2f;

            if (math.abs(currentBuildMode - 1f) < 0.01f) { float3 mode1Start = startXZ + new float3(halfSize, 0f, halfSize); float3 mode1End = endXZ + new float3(halfSize, 0f, halfSize); float dist1 = math.distance(mode1Start, mode1End); float3 dir = dist1 < 0.1f ? new float3(1f, 0f, 0f) : math.normalize(mode1End - mode1Start); float steps = math.max(1f, math.round(dist1 / blockSize)); for (float i = 0f; i <= steps; i += 1f) { float3 p = mode1Start + dir * (i * (dist1 / math.max(1f, steps))); CheckRay(physicsWorld, p.x, p.z, blockSize, ref highestY, ref hitEntity, ref hitAnything); } }
            else if (math.abs(currentBuildMode - 2f) < 0.01f || math.abs(currentBuildMode - 4f) < 0.01f) { float minX = math.min(startXZ.x, endXZ.x); float maxX = math.max(startXZ.x, endXZ.x); float minZ = math.min(startXZ.z, endXZ.z); float maxZ = math.max(startXZ.z, endXZ.z); for (float x = minX; x <= maxX + 0.1f; x += blockSize) { for (float z = minZ; z <= maxZ + 0.1f; z += blockSize) { if (math.abs(currentBuildMode - 2f) < 0.01f) { if (x > minX + (blockSize * 0.5f) && x < maxX - (blockSize * 0.5f) && z > minZ + (blockSize * 0.5f) && z < maxZ - (blockSize * 0.5f)) continue; } CheckRay(physicsWorld, x + halfSize, z + halfSize, blockSize, ref highestY, ref hitEntity, ref hitAnything); } } }
            else if (math.abs(currentBuildMode - 3f) < 0.01f || math.abs(currentBuildMode - 5f) < 0.01f) { float radius = distance / 2.0f; float3 center = (startXZ + endXZ) / 2.0f; float baseRadiusCount = math.floor(radius / blockSize); for (float x = -baseRadiusCount; x <= baseRadiusCount; x += 1f) { for (float z = -baseRadiusCount; z <= baseRadiusCount; z += 1f) { if (math.sqrt(x * x + z * z) <= baseRadiusCount + 0.5f) { CheckRay(physicsWorld, center.x + (x * blockSize), center.z + (z * blockSize), blockSize, ref highestY, ref hitEntity, ref hitAnything); } } } }

            if (hitAnything)
            {
                float finalBuildY = math.round((math.max(highestY, actualStartPos.y) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                if (LogManager.Instance != null) { string toolName = GetToolName(currentBuildMode); string centerStr = $"[{(actualStartPos.x + actualEndPos.x) / 2f:F1}, {finalBuildY:F1}, {(actualStartPos.z + actualEndPos.z) / 2f:F1}]"; LogManager.Instance.AddLog(toolName, (int)math.round(guideHeight), isGhost ? "Key_F" : "Key_G", isGhost ? "Preview" : "Build", centerStr, 0f, 0f); }
                ExecuteBuild(ref state, spawnerData, actualStartPos, actualEndPos, finalBuildY, hitEntity, isGhost);

                if (!isGhost)
                {
                    backupIDToQuery = 1f;
                    isGuideActive = false;
                    pendingJointCleanup = true;
                }
            }
        }
    }

    private void ExecuteBuild(ref SystemState state, SpawnerData data, float3 actualStart, float3 actualEnd, float targetY, Entity hitEntity, bool isGhost)
    {
        bool hitExistingBlock = SystemAPI.HasComponent<BlockTag>(hitEntity); float3 hitEntityPos = hitExistingBlock ? SystemAPI.GetComponent<LocalTransform>(hitEntity).Position : float3.zero;
        if (math.abs(currentBuildMode - 1f) < 0.01f) BuildSolidWall(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, (int)nextStructureID, isGhost);
        else if (math.abs(currentBuildMode - 2f) < 0.01f) BuildEmptyFrame(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, (int)nextStructureID, isGhost);
        else if (math.abs(currentBuildMode - 3f) < 0.01f) BuildCircularPattern(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, (int)nextStructureID, isGhost);
        else if (math.abs(currentBuildMode - 4f) < 0.01f) BuildPyramid(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, (int)nextStructureID, isGhost);
        else if (math.abs(currentBuildMode - 5f) < 0.01f) BuildCone(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, (int)nextStructureID, isGhost);
        if (!isGhost) nextStructureID += 1f;
    }

    private void CheckRay(PhysicsWorld physicsWorld, float x, float z, float blockSize, ref float highestY, ref Entity hitEntity, ref bool hitAnything)
    {
        float half = (blockSize / 2f) * 0.95f; float gridSize = 5f; float step = (half * 2f) / (gridSize - 1f);
        for (float i = 0f; i < gridSize; i += 1f) { for (float j = 0f; j < gridSize; j += 1f) { float px = (x - half) + (i * step); float pz = (z - half) + (j * step); float3 p = new float3(px, 100f, pz); RaycastInput rayInput = new RaycastInput { Start = p, End = p + new float3(0f, -200f, 0f), Filter = CollisionFilter.Default }; if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit)) { hitAnything = true; float snappedY = math.round((hit.Position.y - 1.5f) / 3.0f) * 3.0f + 1.5f; if (snappedY > highestY) { highestY = snappedY; hitEntity = hit.Entity; } } } }
    }

    private void DrawGuideWireframe(float3 start, float3 end, float heightParam, float mode, float blockSize)
    {
        Color color = Color.green; float floorHeight = blockSize;
        if (math.abs(mode - 1f) < 0.01f || math.abs(mode - 2f) < 0.01f) { float minX = math.min(start.x, end.x); float maxX = math.max(start.x, end.x) + blockSize; float minZ = math.min(start.z, end.z); float maxZ = math.max(start.z, end.z) + blockSize; float minY = start.y; float maxY = start.y + (heightParam * floorHeight); Vector3 p1 = new Vector3(minX, minY, minZ); Vector3 p2 = new Vector3(maxX, minY, minZ); Vector3 p3 = new Vector3(maxX, minY, maxZ); Vector3 p4 = new Vector3(minX, minY, maxZ); Debug.DrawLine(p1, p2, color); Debug.DrawLine(p2, p3, color); Debug.DrawLine(p3, p4, color); Debug.DrawLine(p4, p1, color); if (heightParam > 0f) { Vector3 t1 = new Vector3(minX, maxY, minZ); Vector3 t2 = new Vector3(maxX, maxY, minZ); Vector3 t3 = new Vector3(maxX, maxY, maxZ); Vector3 t4 = new Vector3(minX, maxY, maxZ); Debug.DrawLine(t1, t2, color); Debug.DrawLine(t2, t3, color); Debug.DrawLine(t3, t4, color); Debug.DrawLine(t4, t1, color); Debug.DrawLine(p1, t1, color); Debug.DrawLine(p2, t2, color); Debug.DrawLine(p3, t3, color); Debug.DrawLine(p4, t4, color); } }
        else if (math.abs(mode - 3f) < 0.01f || math.abs(mode - 4f) < 0.01f || math.abs(mode - 5f) < 0.01f) { float3 center = (start + end) / 2f; center.x += (blockSize / 2f); center.z += (blockSize / 2f); float radius = (math.distance(start, end) / 2f) + (blockSize / 2f); float minY = start.y; float maxY = start.y + (heightParam * floorHeight); float segments = 24f; float angleStep = (2f * math.PI) / segments; Vector3 prev = center + new float3(radius, 0f, 0f); prev.y = minY; for (float i = 1f; i <= segments; i += 1f) { Vector3 next = center + new float3(math.cos(i * angleStep) * radius, 0f, math.sin(i * angleStep) * radius); next.y = minY; Debug.DrawLine(prev, next, color); prev = next; } if (heightParam > 0f) Debug.DrawLine(new Vector3(center.x, minY, center.z), new Vector3(center.x, maxY, center.z), color); }
    }
    private void RemoveDuplicateJoints(ref SystemState state)
    {
        var seenPairs = new NativeHashSet<int2>(64, Allocator.Temp);
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (pair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>()
            .WithAll<JointTag>().WithEntityAccess())
        {
            int idA = pair.ValueRO.EntityA.Index;
            int idB = pair.ValueRO.EntityB.Index;

            int2 key = idA < idB ? new int2(idA, idB) : new int2(idB, idA);

            if (!seenPairs.Add(key))
            {
                ecb.DestroyEntity(entity);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        seenPairs.Dispose();
    }
    private void CreateIndestructibleJoint(ref EntityCommandBuffer ecb, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity(); ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex()); ecb.AddComponent<JointTag>(jointEntity); ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true)); ecb.AddComponent(jointEntity, PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f)));
    }
}
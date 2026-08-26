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

public struct GhostBlockTag : IComponentData { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SpawnerSystem : ISystem
{
    public static System.Collections.Generic.List<float3> ExternalBlueprintData = new System.Collections.Generic.List<float3>();
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
    private float loadDelayTimer;

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
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_createdColliders.IsCreated) { foreach (var col in _createdColliders) { if (col.IsCreated) col.Dispose(); } _createdColliders.Dispose(); }
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

        if (backupIDToQuery > -1f)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("ID,X,Y,Z,Type");
            bool found = false;

            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BlockTag>())
            {
                float px = transform.ValueRO.Position.x;
                float py = transform.ValueRO.Position.y;
                float pz = transform.ValueRO.Position.z;
                string typeStr = py > 1.5f ? "Wall" : "Floor";

                float ix = math.round(px * 10f); float iy = math.round(py * 10f); float iz = math.round(pz * 10f);
                string strX = $"{(ix < 0f ? "-" : "0")}{math.abs(ix):000}";
                string strZ = $"{(iz < 0f ? "-" : "0")}{math.abs(iz):000}";
                string strY = $"{(iy < 0f ? "-" : "0")}{math.abs(iy):000}";
                string realId = $"{strX}_{strZ}_{strY}";

                csv.AppendLine($"{realId},{px},{py},{pz},{typeStr}");
                found = true;
            }

            if (found)
            {
                string path = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
                if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, csv.ToString());
                UnityEngine.Debug.Log($"💾 [U모드 스냅샷] 현장의 전체 건축물 통째로 백업 완료!");
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
                        float4 data = blueprintOffsets[i];
                        posOffset = new float3(data.x, data.y, data.z);
                        isReinforceBlock = data.w > 0.5f;
                    }
                    else
                    {
                        posOffset = ExternalBlueprintData[i];
                    }

                    float3 finalPos = baseCenter + posOffset;

                    var instance = ecb.Instantiate(spawnerData.Prefab);
                    ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(finalPos, quaternion.identity, 2.95f));

                    if (triggerSpecialGhost)
                    {
                        ecb.AddComponent<GhostBlockTag>(instance);
                        ecb.RemoveComponent<PhysicsCollider>(instance);
                        ecb.RemoveComponent<PhysicsVelocity>(instance);
                        ecb.RemoveComponent<PhysicsMass>(instance);

                        float4 ghostColor = isReinforceBlock ? new float4(0.2f, 0.4f, 1.0f, 0.8f) : new float4(0.7f, 0.7f, 0.7f, 0.4f);
                        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = ghostColor });
                    }
                    else
                    {
                        ecb.AddComponent<BlockTag>(instance);
                        ecb.AddComponent<BlockStress>(instance);
                        ecb.AddComponent(instance, new StructureID { Value = nextStructureID });

                        if (isReinforceBlock)
                        {
                            ecb.AddComponent(instance, new BlockMaterial { MaterialName = "Steel" });
                            ecb.AddComponent(instance, new BlockHealth { MaxHP = 2000f, CurrentHP = 2000f, Defense = 600f });
                            ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0.2f, 0.5f, 1.0f, 1f) });
                        }
                        else
                        {
                            ecb.AddComponent(instance, new BlockMaterial { MaterialName = "Concrete" });
                            ecb.AddComponent(instance, new BlockHealth { MaxHP = 1000f, CurrentHP = 1000f, Defense = 400f });
                            ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0.7f, 0.7f, 0.7f, 1f) });
                        }

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
                    ApplyOptimizedJoints(ref ecb, gridMap, posMap);

                    if (isYMode)
                    {
                        var bpManager = UnityEngine.Object.FindFirstObjectByType<BlueprintManager>();
                        if (bpManager != null) bpManager.SaveBlueprint();
                        blueprintOffsets.Clear();
                        isYMode = false;
                        UnityEngine.Debug.Log("🏗️ [보강 공사] 튼튼하게 기존 건물과 보강 철근이 융합되었습니다!");
                    }
                    else
                    {
                        ExternalBlueprintData.Clear();
                        isOMode = false; isLMode = false; isUMode = false;
                        UnityEngine.Debug.Log("🏗️ [도면 공사] O/U 복층 도면 타설 완료!");
                    }
                    nextStructureID += 1f;
                }
                gridMap.Dispose(); posMap.Dispose();
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Y)) { loadDelayTimer = 5f; }
        if (loadDelayTimer > 0f)
        {
            loadDelayTimer -= 1f;
            if (loadDelayTimer <= 0f) LoadReinforcementPlan();
        }

        if (isYMode && !isBMode)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++) { float4 offset = blueprintOffsets[i]; blueprintOffsets[i] = new float4(-offset.z, offset.y, offset.x, offset.w); }
                UnityEngine.Debug.Log("🔄 Y도면이 왼쪽[Q]으로 90도 회전했습니다!");
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                for (int i = 0; i < blueprintOffsets.Length; i++) { float4 offset = blueprintOffsets[i]; blueprintOffsets[i] = new float4(offset.z, offset.y, -offset.x, offset.w); }
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

                if (!isGhost) { backupIDToQuery = 1f; isGuideActive = false; }
            }
        }
    }

    // ⭐ [중복 방지] 조인트 최적화 모듈
    private void ApplyOptimizedJoints(ref EntityCommandBuffer ecb, NativeHashMap<int3, Entity> gridMap, NativeHashMap<Entity, float3> posMap)
    {
        var jointHistory = new NativeParallelHashSet<long>(gridMap.Count * 6, Allocator.Temp);
        var keys = gridMap.GetKeyArray(Allocator.Temp);
        int3[] dirs = { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1), new int3(-1, 0, 0), new int3(0, -1, 0), new int3(0, 0, -1) };

        foreach (var key in keys)
        {
            Entity cur = gridMap[key];
            for (int i = 0; i < 6; i++)
            {
                if (gridMap.TryGetValue(key + dirs[i], out Entity neighbor))
                {
                    long jKey = CreateJointKey(cur, neighbor);
                    if (!jointHistory.Contains(jKey))
                    {
                        CreateIndestructibleJoint(ref ecb, cur, neighbor, posMap[neighbor] - posMap[cur]);
                        jointHistory.Add(jKey);
                    }
                }
            }
        }
        jointHistory.Dispose(); keys.Dispose();
    }

    private long CreateJointKey(Entity a, Entity b)
    {
        long idA = (long)a.Index; long idB = (long)b.Index; return idA < idB ? (idA << 32) | idB : (idB << 32) | idA;
    }

    // ⭐ [해결됨] 시스템 각주 버그를 우회하는 절대 안전 로드 방식!
    private void LoadReinforcementPlan()
    {
        string path = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        if (!File.Exists(path)) return;

        string[] lines = File.ReadAllLines(path);
        float minX = 99999f, minZ = 99999f, maxX = -99999f, maxZ = -99999f;

        System.Collections.Generic.List<float4> tempList = new System.Collections.Generic.List<float4>();
        System.Collections.Generic.HashSet<string> loadedIDs = new System.Collections.Generic.HashSet<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            string line = (string)lines.GetValue(i);
            string[] cols = line.Split(',');
            if (cols.Length < 5) continue;

            string col0 = (string)cols.GetValue(0); // 버그 방지
            string col4 = (string)cols.GetValue(4); // 버그 방지

            if (col4 == "Reinforcement" || col4 == "Existing")
            {
                string[] idParts = col0.Split('_');
                if (idParts.Length >= 3)
                {
                    string idp0 = (string)idParts.GetValue(0);
                    string idp1 = (string)idParts.GetValue(1);
                    string idp2 = (string)idParts.GetValue(2);

                    float px = float.Parse(idp0) / 10f;
                    float pz = float.Parse(idp1) / 10f;
                    float py = float.Parse(idp2) / 10f;

                    float x = math.round((px - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    float z = math.round((pz - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    float y = math.round((py - 1.5f) / 3.0f) * 3.0f + 1.5f;

                    string uniqueKey = $"{x}_{y}_{z}";
                    if (loadedIDs.Contains(uniqueKey)) continue;

                    float isReinforce = (col4 == "Reinforcement") ? 1f : 0f;
                    tempList.Add(new float4(x, y, z, isReinforce));
                    loadedIDs.Add(uniqueKey);

                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
            }
        }
        if (tempList.Count > 0)
        {
            isYMode = true; blueprintOffsets.Clear(); tempList.Sort((a, b) => a.y.CompareTo(b.y));
            float centerX = math.round(((minX + maxX) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;
            float centerZ = math.round(((minZ + maxZ) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;

            customCenterPos = new float3(centerX, 0f, centerZ);
            isCenterMoved = true;

            foreach (var pos in tempList) { blueprintOffsets.Add(new float4(pos.x - centerX, pos.y, pos.z - centerZ, pos.w)); }
            UnityEngine.Debug.Log($"🏗️ [스마트 로드 완] {tempList.Count}개의 구조물이 도면에 장전되었습니다! F키로 확인해 보세요.");
        }
    }

    private void ExecuteBuild(ref SystemState state, SpawnerData data, float3 actualStart, float3 actualEnd, float targetY, Entity hitEntity, bool isGhost)
    {
        bool hitExistingBlock = SystemAPI.HasComponent<BlockTag>(hitEntity); float3 hitEntityPos = hitExistingBlock ? SystemAPI.GetComponent<LocalTransform>(hitEntity).Position : float3.zero;
        if (math.abs(currentBuildMode - 1f) < 0.01f) BuildSolidWall(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, isGhost);
        else if (math.abs(currentBuildMode - 2f) < 0.01f) BuildEmptyFrame(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, isGhost);
        else if (math.abs(currentBuildMode - 3f) < 0.01f) BuildCircularPattern(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, isGhost);
        else if (math.abs(currentBuildMode - 4f) < 0.01f) BuildPyramid(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, isGhost);
        else if (math.abs(currentBuildMode - 5f) < 0.01f) BuildCone(ref state, data, actualStart, actualEnd, targetY, hitExistingBlock ? hitEntity : Entity.Null, hitEntityPos, isGhost);
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

    private void CreateIndestructibleJoint(ref EntityCommandBuffer ecb, Entity entityA, Entity entityB, float3 offsetToB)
    {
        Entity jointEntity = ecb.CreateEntity(); ecb.AddSharedComponent(jointEntity, new PhysicsWorldIndex()); ecb.AddComponent<JointTag>(jointEntity); ecb.AddComponent(jointEntity, new PhysicsConstrainedBodyPair(entityA, entityB, true)); ecb.AddComponent(jointEntity, PhysicsJoint.CreateFixed(new RigidTransform(quaternion.identity, offsetToB * 0.5f), new RigidTransform(quaternion.identity, -offsetToB * 0.5f)));
    }

    private void BuildSolidWall(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitPos, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f; float halfSize = 1.5f;
        float3 startXZ = new float3(start.x, 0, start.z); float3 endXZ = new float3(end.x, 0, end.z);
        float dist = math.distance(startXZ, endXZ); float3 dir = dist < 0.1f ? new float3(1, 0, 0) : math.normalize(endXZ - startXZ);
        int total = (int)math.max(1, math.round(dist / blockSize));
        int height = (int)math.max(1, math.round(guideHeight));
        var gridMap = new NativeHashMap<int3, Entity>(total * height, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>(total * height, Allocator.Temp);

        for (int i = 0; i <= total; i++)
        {
            for (int h = 0; h < height; h++)
            {
                float3 pos = startXZ + (dir * (i * blockSize)); pos.y = targetY + halfSize + (h * blockSize);
                var inst = ecb.Instantiate(data.Prefab);
                ecb.SetComponent(inst, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 2.95f));
                if (isGhost) { ecb.AddComponent<GhostBlockTag>(inst); ecb.RemoveComponent<PhysicsCollider>(inst); }
                else
                {
                    ecb.AddComponent<BlockTag>(inst); ecb.AddComponent<BlockStress>(inst); ecb.AddComponent(inst, new StructureID { Value = nextStructureID });
                    if (pos.y <= 2.0f) { var m = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); m.InverseMass = 0f; ecb.SetComponent(inst, m); }
                    int3 key = new int3((int)math.floor(pos.x / 3f), (int)math.floor(pos.y / 3f), (int)math.floor(pos.z / 3f));
                    gridMap.TryAdd(key, inst); posMap.TryAdd(inst, pos);
                }
            }
        }
        if (!isGhost) ApplyOptimizedJoints(ref ecb, gridMap, posMap);
        gridMap.Dispose(); posMap.Dispose();
    }

    private void BuildEmptyFrame(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitPos, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f; float halfSize = 1.5f;
        int widthCount = (int)math.max(1, math.round(math.abs(end.x - start.x) / blockSize) + 1);
        int depthCount = (int)math.max(1, math.round(math.abs(end.z - start.z) / blockSize) + 1);
        int heightCount = (int)math.max(1, math.round(guideHeight));
        float3 corner = new float3(math.min(start.x, end.x), 0, math.min(start.z, end.z));
        var gridMap = new NativeHashMap<int3, Entity>(widthCount * heightCount * depthCount, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>(widthCount * heightCount * depthCount, Allocator.Temp);

        for (int h = 0; h < heightCount; h++)
        {
            for (int x = 0; x < widthCount; x++)
            {
                for (int z = 0; z < depthCount; z++)
                {
                    bool isEdge = (x == 0 || x == widthCount - 1 || z == 0 || z == depthCount - 1 || h == 0 || h == heightCount - 1);
                    if (!isEdge) continue;
                    float3 pos = corner + new float3(x * blockSize + halfSize, targetY + halfSize + (h * blockSize), z * blockSize + halfSize);
                    var inst = ecb.Instantiate(data.Prefab);
                    ecb.SetComponent(inst, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 2.95f));
                    if (isGhost) { ecb.AddComponent<GhostBlockTag>(inst); ecb.RemoveComponent<PhysicsCollider>(inst); }
                    else
                    {
                        ecb.AddComponent<BlockTag>(inst); ecb.AddComponent<BlockStress>(inst); ecb.AddComponent(inst, new StructureID { Value = nextStructureID });
                        if (pos.y <= 2.0f) { var m = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); m.InverseMass = 0f; ecb.SetComponent(inst, m); }
                        int3 key = new int3((int)math.floor(pos.x / 3f), (int)math.floor(pos.y / 3f), (int)math.floor(pos.z / 3f));
                        gridMap.TryAdd(key, inst); posMap.TryAdd(inst, pos);
                    }
                }
            }
        }
        if (!isGhost) ApplyOptimizedJoints(ref ecb, gridMap, posMap);
        gridMap.Dispose(); posMap.Dispose();
    }

    private void BuildCircularPattern(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitPos, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f; float halfSize = 1.5f;
        float radius = math.distance(start, end) / 2.0f;
        float3 center = (start + end) / 2.0f; center = new float3(math.floor(center.x / 3f) * 3f + 1.5f, 0, math.floor(center.z / 3f) * 3f + 1.5f);
        int baseCount = (int)math.floor(radius / blockSize);
        int floors = (int)math.max(1, math.round(guideHeight));
        var gridMap = new NativeHashMap<int3, Entity>((baseCount * 2 + 1) * (baseCount * 2 + 1) * floors, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>((baseCount * 2 + 1) * (baseCount * 2 + 1) * floors, Allocator.Temp);

        for (int h = 0; h < floors; h++)
        {
            for (int x = -baseCount; x <= baseCount; x++)
            {
                for (int z = -baseCount; z <= baseCount; z++)
                {
                    float dist = math.sqrt(x * x + z * z);
                    bool inside = (h == 0) ? (dist <= baseCount + 0.5f) : (dist <= baseCount + 0.5f && dist >= baseCount - 0.5f);
                    if (!inside) continue;
                    float3 pos = center + new float3(x * blockSize, targetY + halfSize + (h * blockSize), z * blockSize);
                    var inst = ecb.Instantiate(data.Prefab);
                    ecb.SetComponent(inst, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 2.95f));
                    if (isGhost) { ecb.AddComponent<GhostBlockTag>(inst); ecb.RemoveComponent<PhysicsCollider>(inst); }
                    else
                    {
                        ecb.AddComponent<BlockTag>(inst); ecb.AddComponent<BlockStress>(inst); ecb.AddComponent(inst, new StructureID { Value = nextStructureID });
                        if (pos.y <= 2.0f) { var m = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); m.InverseMass = 0f; ecb.SetComponent(inst, m); }
                        int3 key = new int3((int)math.floor(pos.x / 3f), (int)math.floor(pos.y / 3f), (int)math.floor(pos.z / 3f));
                        gridMap.TryAdd(key, inst); posMap.TryAdd(inst, pos);
                    }
                }
            }
        }
        if (!isGhost) ApplyOptimizedJoints(ref ecb, gridMap, posMap);
        gridMap.Dispose(); posMap.Dispose();
    }

    private void BuildPyramid(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitPos, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f; float halfSize = 1.5f;
        float minX = math.min(start.x, end.x); float maxX = math.max(start.x, end.x);
        float minZ = math.min(start.z, end.z); float maxZ = math.max(start.z, end.z);
        int levels = (int)math.min(math.round((maxX - minX) / blockSize) + 1, math.round((maxZ - minZ) / blockSize) + 1);
        var gridMap = new NativeHashMap<int3, Entity>(1000, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>(1000, Allocator.Temp);

        for (int lv = 0; lv < levels; lv++)
        {
            float inset = lv * blockSize;
            float curMinX = minX + inset; float curMaxX = maxX - inset;
            float curMinZ = minZ + inset; float curMaxZ = maxZ - inset;
            if (curMinX > curMaxX + 0.1f || curMinZ > curMaxZ + 0.1f) break;
            for (float x = curMinX; x <= curMaxX + 0.1f; x += blockSize)
            {
                for (float z = curMinZ; z <= curMaxZ + 0.1f; z += blockSize)
                {
                    float3 pos = new float3(x + halfSize, targetY + halfSize + (lv * blockSize), z + halfSize);
                    var inst = ecb.Instantiate(data.Prefab);
                    ecb.SetComponent(inst, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 2.95f));
                    if (isGhost) { ecb.AddComponent<GhostBlockTag>(inst); ecb.RemoveComponent<PhysicsCollider>(inst); }
                    else
                    {
                        ecb.AddComponent<BlockTag>(inst); ecb.AddComponent<BlockStress>(inst); ecb.AddComponent(inst, new StructureID { Value = nextStructureID });
                        if (pos.y <= 2.0f) { var m = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); m.InverseMass = 0f; ecb.SetComponent(inst, m); }
                        int3 key = new int3((int)math.floor(pos.x / 3f), (int)math.floor(pos.y / 3f), (int)math.floor(pos.z / 3f));
                        gridMap.TryAdd(key, inst); posMap.TryAdd(inst, pos);
                    }
                }
            }
        }
        if (!isGhost) ApplyOptimizedJoints(ref ecb, gridMap, posMap);
        gridMap.Dispose(); posMap.Dispose();
    }

    private void BuildCone(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitPos, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f; float halfSize = 1.5f;
        float radius = math.distance(start, end) / 2.0f;
        float3 center = (start + end) / 2.0f; center = new float3(math.floor(center.x / 3f) * 3f + 1.5f, 0, math.floor(center.z / 3f) * 3f + 1.5f);
        int baseRadiusCount = (int)math.floor(radius / blockSize);
        int floors = (int)math.max(1, math.round(guideHeight));
        var gridMap = new NativeHashMap<int3, Entity>(1000, Allocator.Temp);
        var posMap = new NativeHashMap<Entity, float3>(1000, Allocator.Temp);

        for (int h = 0; h < floors; h++)
        {
            float curRad = math.max(0, (float)baseRadiusCount * (1.0f - (float)h / (float)floors));
            for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
            {
                for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
                {
                    if (math.sqrt(x * x + z * z) <= curRad + 0.5f)
                    {
                        float3 pos = center + new float3(x * blockSize, targetY + halfSize + (h * blockSize), z * blockSize);
                        var inst = ecb.Instantiate(data.Prefab);
                        ecb.SetComponent(inst, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 2.95f));
                        if (isGhost) { ecb.AddComponent<GhostBlockTag>(inst); ecb.RemoveComponent<PhysicsCollider>(inst); }
                        else
                        {
                            ecb.AddComponent<BlockTag>(inst); ecb.AddComponent<BlockStress>(inst); ecb.AddComponent(inst, new StructureID { Value = nextStructureID });
                            if (pos.y <= 2.0f) { var m = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); m.InverseMass = 0f; ecb.SetComponent(inst, m); }
                            int3 key = new int3((int)math.floor(pos.x / 3f), (int)math.floor(pos.y / 3f), (int)math.floor(pos.z / 3f));
                            gridMap.TryAdd(key, inst); posMap.TryAdd(inst, pos);
                        }
                    }
                }
            }
        }
        if (!isGhost) ApplyOptimizedJoints(ref ecb, gridMap, posMap);
        gridMap.Dispose(); posMap.Dispose();
    }
}
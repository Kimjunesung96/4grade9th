using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Collections;
using Unity.Rendering;

public partial struct SpawnerSystem
{
    private void BuildCircularPattern(ref SystemState state, SpawnerData data, float3 startPos, float3 endPos, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f;
        float margin = 0.05f;

        float3 startXZ = new float3(startPos.x, 0, startPos.z);
        float3 endXZ = new float3(endPos.x, 0, endPos.z);
        float radius = math.distance(startXZ, endXZ) / 2.0f;
        float3 center = (startXZ + endXZ) / 2.0f;

        int baseRadiusCount = (int)math.floor(radius / blockSize);
        int floors = Mathf.Max(1, Mathf.RoundToInt(guideHeight));
        if (baseRadiusCount < 1) return;

        int maxGridSize = (baseRadiusCount * 2) + 1;

        var physicsWorld = SystemAPI.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;
        float highestHitY = -9999f;
        NativeArray<Unity.Physics.RaycastHit> floorHits = new NativeArray<Unity.Physics.RaycastHit>(maxGridSize * maxGridSize, Allocator.Temp);

        // ==========================================================
        // 1구역: 레이저로 바닥 높이 재기
        // ==========================================================
        for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
        {
            for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
            {
                int hitIndex = (x + baseRadiusCount) + ((z + baseRadiusCount) * maxGridSize);
                float dist = math.sqrt(x * x + z * z);
                if (dist <= baseRadiusCount + 0.5f)
                {
                    float3 rayStart = center + new float3(x * blockSize, targetY + 100f, z * blockSize);
                    Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput { Start = rayStart, End = rayStart - new float3(0, 200f, 0), Filter = Unity.Physics.CollisionFilter.Default };
                    if (physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                    {
                        floorHits[hitIndex] = hit; // 🛠️ [복구 완료] 십장님이 실수로 지워버린 레이저 맞은 위치 저장!
                        float snappedY = math.round(hit.Position.y / blockSize) * blockSize;
                        if (snappedY > highestHitY) highestHitY = snappedY;
                    }
                    else
                    {
                        floorHits[hitIndex] = new Unity.Physics.RaycastHit { Entity = Entity.Null, Position = new float3(0, -9999f, 0) };
                    }
                }
            }
        }
        if (highestHitY > -9999f) targetY = highestHitY;

        NativeArray<Entity> grid = new NativeArray<Entity>(maxGridSize * floors * maxGridSize, Allocator.Temp);
        for (int i = 0; i < grid.Length; i++) grid[i] = Entity.Null;
        int GetIndex(int px, int py, int pz) => px + (py * maxGridSize * maxGridSize) + (pz * maxGridSize);

        // ==========================================================
        // 2구역: 콘크리트 붓고 용접하기
        // ==========================================================
        for (int y = 0; y < floors; y++)
        {
            for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
            {
                for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
                {
                    float dist = math.sqrt(x * x + z * z);
                    bool insideShape = y == 0 ? (dist <= baseRadiusCount + 0.5f) : (dist <= baseRadiusCount + 0.5f && dist >= baseRadiusCount - 0.5f);

                    if (insideShape)
                    {
                        float3 pos = center + new float3(x * blockSize, targetY + (y * blockSize) + (blockSize / 2f), z * blockSize);
                        var instance = ecb.Instantiate(data.Prefab);
                        ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, blockSize - margin));

                        if (isGhost)
                        {
                            ecb.AddComponent<GhostBlockTag>(instance);
                            ecb.RemoveComponent<PhysicsCollider>(instance);
                            ecb.RemoveComponent<PhysicsVelocity>(instance);
                            ecb.RemoveComponent<PhysicsMass>(instance);
                            ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0, 1, 1, 0.5f) });
                        }
                        else
                        {
                            ecb.AddComponent<BlockTag>(instance);
                            ecb.AddComponent<BlockStress>(instance);
                            ecb.AddComponent(instance, new StructureID { Value = structureID });
                            ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });
                        }

                        grid[GetIndex(x + baseRadiusCount, y, z + baseRadiusCount)] = instance;

                        // ⭐ 용접은 진짜 시공일 때만
                        if (!isGhost && y == 0)
                        {
                            int hitIndex = (x + baseRadiusCount) + ((z + baseRadiusCount) * maxGridSize);
                            var hit = floorHits[hitIndex];

                            // 🛠️ 대상 블록의 완벽한 격자 높이 계산 (안 파고들게!)
                            float snappedHitY = hit.Entity != Entity.Null ? math.round(hit.Position.y / blockSize) * blockSize : -9999f;

                            if (hit.Entity != Entity.Null && math.abs(snappedHitY - highestHitY) < 0.1f && SystemAPI.HasComponent<PhysicsVelocity>(hit.Entity))
                            {
                                // 🛠️ [백신 2호 완료] 껍데기(Position) 싹 무시! 상대 블록의 '진짜 중심점(adjPos)'을 캐내서 용접!
                                float3 adjPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                                CreateIndestructibleJoint(ref ecb, hit.Entity, instance, pos - adjPos);
                            }
                            else
                            {
                                // 🛠️ [중력 부활 완료] 허공에 뜬 처마는 냅두고, 진짜 맨땅(Y=2.0 이하)일 때만 얼려라!
                                if (pos.y <= 2.0f)
                                {
                                    PhysicsMass prefabMass = SystemAPI.GetComponent<PhysicsMass>(data.Prefab);
                                    prefabMass.InverseMass = 0; prefabMass.InverseInertia = float3.zero;
                                    ecb.SetComponent(instance, prefabMass);
                                }
                            }
                        }
                    }
                }
            }
        }

        if (!isGhost)
        {
            for (int y = 0; y < floors; y++)
            {
                for (int x = 0; x < maxGridSize; x++)
                {
                    for (int z = 0; z < maxGridSize; z++)
                    {
                        Entity current = grid[GetIndex(x, y, z)];
                        if (current == Entity.Null) continue;
                        if (x < maxGridSize - 1 && grid[GetIndex(x + 1, y, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x + 1, y, z)], new float3(blockSize, 0, 0));
                        if (z < maxGridSize - 1 && grid[GetIndex(x, y, z + 1)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, y, z + 1)], new float3(0, 0, blockSize));
                        if (x < maxGridSize - 1 && z < maxGridSize - 1 && grid[GetIndex(x + 1, y, z + 1)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x + 1, y, z + 1)], new float3(blockSize, 0, blockSize));
                        if (x > 0 && z < maxGridSize - 1 && grid[GetIndex(x - 1, y, z + 1)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x - 1, y, z + 1)], new float3(-blockSize, 0, blockSize));
                        if (y < floors - 1 && grid[GetIndex(x, y + 1, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, y + 1, z)], new float3(0, blockSize, 0));
                    }
                }
            }
        }
        grid.Dispose();
        floorHits.Dispose();
    }
}

using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Rendering;
using Unity.Physics;

public partial struct SpawnerSystem
{
    private void BuildCone(ref SystemState state, SpawnerData data, float3 startPos, float3 endPos, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID, bool isGhost)
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
        int maxGridSize = (baseRadiusCount * 2) + 1;

        var physicsWorld = SystemAPI.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;
        float highestHitY = -9999f;
        NativeArray<Unity.Physics.RaycastHit> floorHits = new NativeArray<Unity.Physics.RaycastHit>(maxGridSize * maxGridSize, Allocator.Temp);

        for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
        {
            for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
            {
                int hitIndex = (x + baseRadiusCount) + ((z + baseRadiusCount) * maxGridSize);
                float3 rayStart = center + new float3(x * blockSize, targetY + 100f, z * blockSize);
                Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput { Start = rayStart, End = rayStart - new float3(0, 200f, 0), Filter = Unity.Physics.CollisionFilter.Default };
                if (physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                {
                    floorHits[hitIndex] = hit;
                    float snappedY = math.round(hit.Position.y / blockSize) * blockSize;
                    if (snappedY > highestHitY) highestHitY = snappedY;
                }
                else
                {
                    floorHits[hitIndex] = new Unity.Physics.RaycastHit { Entity = Entity.Null, Position = new float3(0, -9999f, 0) };
                }
            }
        }
        if (highestHitY > -9999f) targetY = highestHitY;

        NativeArray<Entity> grid = new NativeArray<Entity>(maxGridSize * floors * maxGridSize, Allocator.Temp);
        for (int i = 0; i < grid.Length; i++) grid[i] = Entity.Null;
        int GetIndex(int px, int ph, int pz) => px + (ph * maxGridSize * maxGridSize) + (pz * maxGridSize);

        for (int h = 0; h < floors; h++)
        {
            float currentLayerRadius = math.max(0, (float)baseRadiusCount * (1.0f - (float)h / (float)floors));

            for (int x = -baseRadiusCount; x <= baseRadiusCount; x++)
            {
                for (int z = -baseRadiusCount; z <= baseRadiusCount; z++)
                {
                    float dist = math.sqrt(x * x + z * z);
                    if (dist <= currentLayerRadius + 0.5f)
                    {
                        float3 pos = center + new float3(x * blockSize, targetY + (h * blockSize) + (blockSize / 2f), z * blockSize);
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

                        grid[GetIndex(x + baseRadiusCount, h, z + baseRadiusCount)] = instance;

                        if (!isGhost && h == 0)
                        {
                            int hitIndex = (x + baseRadiusCount) + ((z + baseRadiusCount) * maxGridSize);
                            var hit = floorHits[hitIndex];
                            float snappedHitY = hit.Entity != Entity.Null ? math.round(hit.Position.y / blockSize) * blockSize : -9999f;

                            if (hit.Entity != Entity.Null && math.abs(snappedHitY - highestHitY) < 0.1f && SystemAPI.HasComponent<PhysicsVelocity>(hit.Entity))
                            {
                                float3 adjPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                                CreateIndestructibleJoint(ref ecb, hit.Entity, instance, pos - adjPos);
                            }
                            else if (pos.y <= 2.0f)
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

        // 🛠️ [복구] 원뿔 내부 블록 간 조인트 (수평/수직)
        if (!isGhost)
        {
            for (int h = 0; h < floors; h++)
            {
                for (int x = 0; x < maxGridSize; x++)
                {
                    for (int z = 0; z < maxGridSize; z++)
                    {
                        Entity current = grid[GetIndex(x, h, z)];
                        if (current == Entity.Null) continue;
                        if (x < maxGridSize - 1 && grid[GetIndex(x + 1, h, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x + 1, h, z)], new float3(blockSize, 0, 0));
                        if (z < maxGridSize - 1 && grid[GetIndex(x, h, z + 1)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, h, z + 1)], new float3(0, 0, blockSize));
                        if (h < floors - 1 && grid[GetIndex(x, h + 1, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, h + 1, z)], new float3(0, blockSize, 0));
                    }
                }
            }
        }
        grid.Dispose();
        floorHits.Dispose();
    }
}
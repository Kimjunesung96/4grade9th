using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Rendering;
using Unity.Physics;

public partial struct SpawnerSystem
{
    private void BuildSolidWall(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        float blockSize = 3.0f;
        float halfSize = blockSize / 2.0f;
        float margin = 0.05f;

        float3 snappedStart = math.floor(new float3(start.x, 0, start.z) / blockSize) * blockSize;
        float3 snappedEnd = math.floor(new float3(end.x, 0, end.z) / blockSize) * blockSize;

        float3 diff = snappedEnd - snappedStart;
        if (math.abs(diff.x) > math.abs(diff.z)) snappedEnd.z = snappedStart.z;
        else snappedEnd.x = snappedStart.x;

        float3 startXZ = snappedStart + new float3(halfSize, 0, halfSize);
        float3 endXZ = snappedEnd + new float3(halfSize, 0, halfSize);

        float distance = math.distance(startXZ, endXZ);
        float3 direction = distance < 0.1f ? new float3(1, 0, 0) : math.normalize(endXZ - startXZ);

        int totalBlockCount = (int)math.max(1, math.round(distance / blockSize));
        int heightCount = (int)math.max(1, math.round(guideHeight));

        var physicsWorld = SystemAPI.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;
        NativeArray<float> hitHeights = new NativeArray<float>(totalBlockCount, Allocator.Temp);

        for (int i = 0; i < totalBlockCount; i++)
        {
            float3 rayStart = startXZ + (direction * (i * blockSize));
            rayStart.y = 500f;
            Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput
            {
                Start = rayStart,
                End = rayStart - new float3(0, 1000f, 0),
                Filter = Unity.Physics.CollisionFilter.Default
            };

            if (physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
            {
                hitHeights[i] = math.round(hit.Position.y / blockSize) * blockSize;
            }
            else
            {
                hitHeights[i] = -9999f;
            }
        }

        float majorityHeight = -9999f;
        int maxCount = 0;
        for (int i = 0; i < totalBlockCount; i++)
        {
            if (hitHeights[i] == -9999f) continue;
            int count = 0;
            for (int j = 0; j < totalBlockCount; j++)
            {
                if (hitHeights[i] == hitHeights[j]) count++;
            }
            if (count > maxCount)
            {
                maxCount = count;
                majorityHeight = hitHeights[i];
            }
        }

        int bestStartIdx = -1;
        int maxLen = 0;
        int currentStart = -1;
        int currentLen = 0;

        for (int i = 0; i < totalBlockCount; i++)
        {
            if (hitHeights[i] == majorityHeight && majorityHeight != -9999f)
            {
                if (currentLen == 0) currentStart = i;
                currentLen++;
                if (currentLen > maxLen)
                {
                    maxLen = currentLen;
                    bestStartIdx = currentStart;
                }
            }
            else
            {
                currentLen = 0;
            }
        }

        hitHeights.Dispose();

        if (bestStartIdx == -1 || maxLen == 0) return;

        NativeArray<Entity> grid = new NativeArray<Entity>(maxLen * heightCount, Allocator.Temp);
        for (int i = 0; i < grid.Length; i++) grid[i] = Entity.Null;

        for (int i = 0; i < maxLen; i++)
        {
            int actualIndex = bestStartIdx + i;
            for (int h = 0; h < heightCount; h++)
            {
                float3 pos = startXZ + (direction * (actualIndex * blockSize));
                pos.y = majorityHeight + halfSize + (h * blockSize);

                Unity.Physics.PointDistanceInput pointInput = new Unity.Physics.PointDistanceInput
                {
                    Position = pos,
                    MaxDistance = 0.1f,
                    Filter = Unity.Physics.CollisionFilter.Default
                };
                if (physicsWorld.CalculateDistance(pointInput, out Unity.Physics.DistanceHit distHit))
                {
                    continue;
                }

                var instance = ecb.Instantiate(data.Prefab);
                ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, blockSize - margin));

                // ⭐ 가설계 분기 처리
                if (isGhost)
                {
                    ecb.AddComponent<GhostBlockTag>(instance);
                    ecb.RemoveComponent<PhysicsCollider>(instance);
                    ecb.RemoveComponent<PhysicsVelocity>(instance);
                    ecb.RemoveComponent<PhysicsMass>(instance);
                    ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0, 1, 1, 0.5f) }); // 파란색 반투명 느낌
                }
                else
                {
                    ecb.AddComponent<BlockTag>(instance);
                    ecb.AddComponent<BlockStress>(instance);
                    ecb.AddComponent(instance, new StructureID { Value = structureID });
                    ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });
                }

                grid[i * heightCount + h] = instance;

                // ⭐ 용접은 진짜 시공일 때만 수행
                if (!isGhost)
                {
                    float3[] dirs = new float3[] { math.up(), math.down(), math.left(), math.right(), math.forward(), math.back() };
                    foreach (float3 dir in dirs)
                    {
                        Unity.Physics.RaycastInput weldInput = new Unity.Physics.RaycastInput
                        {
                            Start = pos,
                            End = pos + dir * (blockSize * 0.7f),
                            Filter = Unity.Physics.CollisionFilter.Default
                        };

                        if (physicsWorld.CastRay(weldInput, out Unity.Physics.RaycastHit weldHit))
                        {
                            if (weldHit.Entity != Entity.Null)
                            {
                                if (SystemAPI.HasComponent<PhysicsVelocity>(weldHit.Entity))
                                {
                                    if (SystemAPI.HasComponent<LocalTransform>(weldHit.Entity))
                                    {
                                        float3 adjPos = SystemAPI.GetComponent<LocalTransform>(weldHit.Entity).Position;
                                        CreateIndestructibleJoint(ref ecb, weldHit.Entity, instance, pos - adjPos);
                                    }
                                }
                                else
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

        // 형제 블록 간의 십자 용접도 진짜 시공일 때만!
        if (!isGhost)
        {
            for (int i = 0; i < maxLen; i++)
            {
                for (int h = 0; h < heightCount; h++)
                {
                    Entity current = grid[i * heightCount + h];
                    if (current == Entity.Null) continue;

                    if (i < maxLen - 1 && grid[(i + 1) * heightCount + h] != Entity.Null)
                        CreateIndestructibleJoint(ref ecb, current, grid[(i + 1) * heightCount + h], direction * blockSize);

                    if (h < heightCount - 1 && grid[i * heightCount + (h + 1)] != Entity.Null)
                        CreateIndestructibleJoint(ref ecb, current, grid[i * heightCount + (h + 1)], new float3(0, blockSize, 0));
                }
            }
        }
        grid.Dispose();
    }
}
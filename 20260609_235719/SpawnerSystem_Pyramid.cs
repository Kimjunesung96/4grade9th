using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Rendering;
using Unity.Physics;

public partial struct SpawnerSystem
{
    // 🔺 [소장님 특명] 1, 9, 24(두께2) 공법 + 초록 홀로그램 + 조인트 보강
    private void BuildPyramid(ref SystemState state, SpawnerData data, float3 actualStart, float3 actualEnd, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f;
        float halfSize = blockSize / 2f;

        float minX = math.min(actualStart.x, actualEnd.x); float maxX = math.max(actualStart.x, actualEnd.x);
        float minZ = math.min(actualStart.z, actualEnd.z); float maxZ = math.max(actualStart.z, actualEnd.z);

        float width = maxX - minX; float depth = maxZ - minZ;
        int levelsX = (int)math.round(width / blockSize) + 1;
        int levelsZ = (int)math.round(depth / blockSize) + 1;
        int maxLevels = math.min(levelsX, levelsZ);

        Unity.Collections.NativeHashMap<int3, Entity> gridMap = new Unity.Collections.NativeHashMap<int3, Entity>(1000, Allocator.Temp);

        for (int level = 0; level < maxLevels; level++)
        {
            // ⭐ 밑에서부터 쌓아 올립니다 - halfSize 오프셋 추가!
            float currentY = targetY + (level * blockSize) + halfSize;

            // ⭐ [핵심] 1.5m가 아니라 3.0m(한 칸)씩 안으로 들여쓰기! (1, 9, 24 비율의 비결)
            float inset = level * blockSize;
            float curMinX = minX + inset; float curMaxX = maxX - inset;
            float curMinZ = minZ + inset; float curMaxZ = maxZ - inset;

            if (curMinX > curMaxX + 0.01f || curMinZ > curMaxZ + 0.01f) break;

            for (float x = curMinX; x <= curMaxX + 0.01f; x += blockSize)
            {
                for (float z = curMinZ; z <= curMaxZ + 0.01f; z += blockSize)
                {
                    // ⭐ [두께 2칸 로직] 테두리에서 2칸 이상 들어온 놈은 파내기
                    int dx = (int)math.round((x - curMinX) / blockSize);
                    int dx2 = (int)math.round((curMaxX - x) / blockSize);
                    int dz = (int)math.round((z - curMinZ) / blockSize);
                    int dz2 = (int)math.round((curMaxZ - z) / blockSize);
                    int minDepth = math.min(math.min(dx, dx2), math.min(dz, dz2));

                    if (minDepth >= 2) continue;

                    float3 pos = new float3(x + halfSize, currentY, z + halfSize);
                    var instance = ecb.Instantiate(data.Prefab);
                    ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, blockSize - 0.05f));
                    ecb.AddComponent<BlockTag>(instance);
                    ecb.AddComponent<BlockStress>(instance);
                    ecb.AddComponent(instance, new StructureID { Value = structureID });

                    if (isGhost)
                    {
                        ecb.AddComponent<GhostBlockTag>(instance);
                        // ⭐ [도색] 다시 초록색 반투명으로 원복!
                        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0, 1, 0, 0.5f) });
                        // ⭐ [승천방지] 홀로그램은 충돌체 제거!
                        ecb.RemoveComponent<PhysicsCollider>(instance);
                    }
                    else
                    {
                        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(1, 1, 1, 1) });
                        if (pos.y <= 2.0f) { PhysicsMass mass = SystemAPI.GetComponent<PhysicsMass>(data.Prefab); mass.InverseMass = 0; mass.InverseInertia = float3.zero; ecb.SetComponent(instance, mass); }

                        int3 key = new int3((int)math.floor(pos.x / 3f + 0.5f), (int)math.floor(pos.y / 3f + 0.5f), (int)math.floor(pos.z / 3f + 0.5f));
                        gridMap.TryAdd(key, instance);
                    }
                }
            }
        }

        if (!isGhost) // 실제 타설 시에만 조인트(철근) 삽입
        {
            var keys = gridMap.GetKeyArray(Allocator.Temp);
            float3[] dirs = new float3[] { math.right(), math.up(), math.forward() };
            int3[] gridDirs = new int3[] { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1) };

            foreach (var key in keys)
            {
                Entity currentEntity = gridMap[key];
                for (int d = 0; d < 3; d++)
                {
                    if (gridMap.TryGetValue(key + gridDirs[d], out Entity neighbor))
                        CreateIndestructibleJoint(ref ecb, currentEntity, neighbor, dirs[d] * blockSize);
                }
            }
            keys.Dispose();
        }
        gridMap.Dispose();
    }
}
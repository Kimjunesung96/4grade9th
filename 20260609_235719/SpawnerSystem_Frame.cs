using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Rendering;
using Unity.Physics;

public partial struct SpawnerSystem
{
    private Entity CreateGiantPlate(ref EntityCommandBuffer ecb, SpawnerData data, float3 pos, float width, float depth, int structureID, bool isFoundation)
    {
        var instance = ecb.Instantiate(data.Prefab);
        float height = 1.0f;
        ecb.SetComponent(instance, LocalTransform.FromPosition(pos));
        ecb.AddComponent(instance, new PostTransformMatrix { Value = float4x4.Scale(width, height, depth) });
        BlobAssetReference<Unity.Physics.Collider> plateCollider = Unity.Physics.BoxCollider.Create(new BoxGeometry { Center = float3.zero, Orientation = quaternion.identity, Size = new float3(width, height, depth), BevelRadius = 0.05f });
        ecb.SetComponent(instance, new PhysicsCollider { Value = plateCollider });

        if (isFoundation) ecb.SetComponent(instance, Unity.Physics.PhysicsMass.CreateKinematic(plateCollider.Value.MassProperties));
        else ecb.SetComponent(instance, Unity.Physics.PhysicsMass.CreateDynamic(plateCollider.Value.MassProperties, 1.0f));

        ecb.AddComponent<BlockTag>(instance);
        ecb.AddComponent<BlockStress>(instance);
        ecb.AddComponent(instance, new StructureID { Value = structureID });
        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(0.2f, 0.2f, 0.2f, 1) });
        return instance;
    }

    private void BuildEmptyFrame(ref SystemState state, SpawnerData data, float3 start, float3 end, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID, bool isGhost)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f;
        float halfSize = blockSize / 2.0f;
        float margin = 0.05f;

        // 🛠️ [수정 1] 1번 벽과 똑같이 3m 격자(Grid)에 무조건 맞물리게 강제 스냅!
        float3 snappedStart = math.floor(new float3(start.x, 0, start.z) / blockSize) * blockSize;
        float3 snappedEnd = math.floor(new float3(end.x, 0, end.z) / blockSize) * blockSize;

        int widthCount = (int)math.max(1, math.round(math.abs(snappedEnd.x - snappedStart.x) / blockSize) + 1);
        int depthCount = (int)math.max(1, math.round(math.abs(snappedEnd.z - snappedStart.z) / blockSize) + 1);
        int heightCount = (int)math.max(1, math.round(guideHeight));
        float3 startCorner = new float3(math.min(snappedStart.x, snappedEnd.x), 0f, math.min(snappedStart.z, snappedEnd.z));

        var physicsWorld = SystemAPI.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;
        float highestHitY = -9999f;
        NativeArray<Unity.Physics.RaycastHit> floorHits = new NativeArray<Unity.Physics.RaycastHit>(widthCount * depthCount, Allocator.Temp);

        // ==========================================================
        // 1구역: 레이저로 바닥 높이 재기
        // ==========================================================
        for (int x = 0; x < widthCount; x++)
        {
            for (int z = 0; z < depthCount; z++)
            {
                float3 rayStart = new float3(startCorner.x + (x * blockSize) + halfSize, targetY + 100f, startCorner.z + (z * blockSize) + halfSize);
                Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput { Start = rayStart, End = rayStart - new float3(0, 200f, 0), Filter = Unity.Physics.CollisionFilter.Default };

                if (physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                {
                    floorHits[x + (z * widthCount)] = hit;
                    // 🛠️ [수정 2] 레이저 오차(2.999m)를 3.0m로 완벽하게 반올림(round) 교정!
                    float snappedY = math.round(hit.Position.y / blockSize) * blockSize;
                    if (snappedY > highestHitY) highestHitY = snappedY;
                }
                else
                {
                    floorHits[x + (z * widthCount)] = new Unity.Physics.RaycastHit { Entity = Entity.Null, Position = new float3(0, -9999f, 0) };
                }
            }
        }

        if (highestHitY > -9999f) targetY = highestHitY;
        else targetY = math.round(targetY / blockSize) * blockSize;

        NativeArray<Entity> grid = new NativeArray<Entity>(widthCount * heightCount * depthCount, Allocator.Temp);
        for (int i = 0; i < grid.Length; i++) grid[i] = Entity.Null;
        int GetIndex(int x, int h, int z) => x + (h * widthCount * depthCount) + (z * widthCount);

        // ==========================================================
        // 2구역: 콘크리트 붓고 용접하기
        // ==========================================================
        for (int h = 0; h < heightCount; h++)
        {
            for (int x = 0; x < widthCount; x++)
            {
                for (int z = 0; z < depthCount; z++)
                {
                    bool isWall = (x == 0 || x == widthCount - 1 || z == 0 || z == depthCount - 1);
                    bool isFloor = (h == 0);
                    bool isCeiling = (h == heightCount - 1);

                    if (isWall || isFloor || isCeiling)
                    {
                        float3 pos = new float3(startCorner.x + (x * blockSize) + halfSize, targetY + halfSize + (h * blockSize), startCorner.z + (z * blockSize) + halfSize);
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

                        grid[GetIndex(x, h, z)] = instance;

                        // ⭐ [수정 3 핵심!!] 용접은 진짜 시공일 때만 (이상접합/파고들기 완벽 해결!)
                        // ⭐ [수정 3 핵심!!] 용접은 진짜 시공일 때만 (이상접합/파고들기 완벽 해결!)
                        if (!isGhost && isFloor)
                        {
                            var hit = floorHits[x + (z * widthCount)];
                            float snappedHitY = hit.Entity != Entity.Null ? math.round(hit.Position.y / blockSize) * blockSize : -9999f;

                            // 1. [정상 용접] 밑에 진짜 블록이 있고, 거기가 최고 높이일 때
                            if (hit.Entity != Entity.Null && math.abs(snappedHitY - highestHitY) < 0.1f && SystemAPI.HasComponent<PhysicsVelocity>(hit.Entity))
                            {
                                // 표면 좌표 금지! 무조건 대상 블록의 중심점(adjPos)을 써서 파고들기 원천 차단!
                                float3 adjPos = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                                CreateIndestructibleJoint(ref ecb, hit.Entity, instance, pos - adjPos);
                            }
                            // 2. [맨땅 앙카] 밑에 블록은 없는데, 내가 지금 짓는 곳이 진짜 바닥(지면)일 때!
                            else if (targetY <= 2.0f) // 🏗️ (1.5 ~ 2.0 부근이 맨땅이라면)
                            {
                                PhysicsMass prefabMass = SystemAPI.GetComponent<PhysicsMass>(data.Prefab);
                                prefabMass.InverseMass = 0;
                                prefabMass.InverseInertia = float3.zero;
                                ecb.SetComponent(instance, prefabMass);
                            }
                            // 3. [중력의 심판] 공중(targetY > 2.0)인데 밑에 아무것도 없을 때!
                            // -> 아무 코드도 안 적습니다! 그러면 마법의 본드가 안 발려서 알아서 추락하거나 옆 블록에 매달려 비명을 지릅니다!
                        }
                    }
                }
            }
        }

        if (!isGhost)
        {
            for (int h = 0; h < heightCount; h++)
            {
                for (int x = 0; x < widthCount; x++)
                {
                    for (int z = 0; z < depthCount; z++)
                    {
                        Entity current = grid[GetIndex(x, h, z)];
                        if (current == Entity.Null) continue;
                        if (x < widthCount - 1 && grid[GetIndex(x + 1, h, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x + 1, h, z)], new float3(blockSize, 0, 0));
                        if (z < depthCount - 1 && grid[GetIndex(x, h, z + 1)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, h, z + 1)], new float3(0, 0, blockSize));
                        if (h < heightCount - 1 && grid[GetIndex(x, h + 1, z)] != Entity.Null) CreateIndestructibleJoint(ref ecb, current, grid[GetIndex(x, h + 1, z)], new float3(0, blockSize, 0));
                    }
                }
            }
        }
        grid.Dispose();
        floorHits.Dispose();
    }
}
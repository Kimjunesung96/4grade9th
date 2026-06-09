using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using Unity.Rendering;
using Unity.Physics;
public partial struct SpawnerSystem
{
    private void BuildMotorPlate(ref SystemState state, SpawnerData data, float3 startPos, float targetY, Entity hitEntity, float3 hitEntityPos, int structureID)
    {
        var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float blockSize = 3.0f;
        float margin = 0.05f;

        float3 pos = new float3(startPos.x, targetY + (blockSize / 2f), startPos.z);

        var instance = ecb.Instantiate(data.Prefab);
        ecb.SetComponent(instance, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, blockSize - margin));
        ecb.AddComponent<BlockTag>(instance);
        ecb.AddComponent<BlockStress>(instance); // ⭐ 모터도 스트레스 받으면 색 변하도록 추가!
        ecb.AddComponent(instance, new StructureID { Value = structureID });
        ecb.AddComponent(instance, new URPMaterialPropertyBaseColor { Value = new float4(1, 0, 0, 1) });

        if (hitEntity != Entity.Null && SystemAPI.HasComponent<PhysicsVelocity>(hitEntity))
        {
            // ⭐ 실제 위치 기반 부드러운 용접
            CreateIndestructibleJoint(ref ecb, hitEntity, instance, pos - hitEntityPos);
        }
    }
}
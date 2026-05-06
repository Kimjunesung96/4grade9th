using Unity.Entities;
using Unity.Collections;
using Unity.Physics;

public struct BlockStress : IComponentData
{
    public float TargetStress;
    public float SmoothedStress;
}

public struct BlockHealth : IComponentData
{
    public float MaxHP;
    public float CurrentHP;
    public float Defense;
}

public struct BlockMaterial : IComponentData
{
    public FixedString32Bytes MaterialName;
}

// ⭐ 1~5번 공법 스포너 수정 없이 작동하는 자동 기입 시스템
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct DefaultMaterialInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (mass, entity) in SystemAPI.Query<RefRW<PhysicsMass>>().WithAll<BlockTag>().WithNone<BlockMaterial>().WithEntityAccess())
        {
            ecb.AddComponent(entity, new BlockMaterial { MaterialName = "Default" });
            ecb.AddComponent(entity, new BlockHealth { MaxHP = 999999f, CurrentHP = 999999f, Defense = 4.0f });

            if (mass.ValueRO.InverseMass > 0f)
            {
                mass.ValueRW.InverseMass = 1.0f / 10.0f;
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
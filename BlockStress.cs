using Unity.Entities;
using Unity.Collections;
using Unity.Physics; // ⭐ 물리 질량(Mass) 제어를 위해 추가

// 기존: 물리 스트레스 수치 저장
public struct BlockStress : IComponentData
{
    public float TargetStress;
    public float SmoothedStress;
}

// ⭐ 신규: 십장님 특명! 재료별 체력과 3대 방어력 추가!
public struct BlockHealth : IComponentData
{
    public float MaxHP;
    public float CurrentHP;
    public float Defense; // 압축/인장/전단 종합 방어력 (이걸 못 넘으면 안 부서짐!)
}

// ⭐ 신규: 엑셀에서 받아온 재질 이름표 (예: "Concrete_Wall")
public struct BlockMaterial : IComponentData
{
    public FixedString32Bytes MaterialName;
}

// ==============================================================
// ⭐ [십장님 특명] 1~5번 공법 수정 없이 작동하는 자동 기입 시스템
// ==============================================================
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct DefaultMaterialInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 핵심 마법: BlockTag는 있는데 BlockMaterial이 없는(값이 비어있는) 방금 스폰된 블록만 싹 다 잡아냅니다!
        foreach (var (mass, entity) in SystemAPI.Query<RefRW<PhysicsMass>>().WithAll<BlockTag>().WithNone<BlockMaterial>().WithEntityAccess())
        {
            // 1. 값이 없으면 무조건 'Default' 스펙(무한 체력, 방어력 4)으로 자동 기입!
            ecb.AddComponent(entity, new BlockMaterial { MaterialName = "Default" });
            ecb.AddComponent(entity, new BlockHealth { MaxHP = 999999f, CurrentHP = 999999f, Defense = 4.0f });

            // 2. 질량 10으로 강제 세팅 (단, 땅에 박혀서 질량이 무한(0)이 된 1층 앙카 블록은 건드리지 않음!)
            if (mass.ValueRO.InverseMass > 0f)
            {
                mass.ValueRW.InverseMass = 1.0f / 10.0f;
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
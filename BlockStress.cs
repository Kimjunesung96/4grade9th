using Unity.Entities;
using Unity.Collections;
using Unity.Physics;

public struct BlockStress : IComponentData { public float TargetStress; public float SmoothedStress; }

public struct BlockHealth : IComponentData { public float MaxHP; public float CurrentHP; public float Defense; }

public struct BlockMaterial : IComponentData
{
    public FixedString32Bytes MaterialName;
    public bool IsBrittle;
    public float TensileStiffness;
    public float CompressiveStiffness;
    public float ShearStiffness;
    public float BendingStiffness;
    public float TorsionStiffness;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct DefaultMaterialInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        // ⭐ PhysicsMass 유무와 상관없이 모든 블록에 기본 재질 라벨 주입
        foreach (var (tag, entity) in SystemAPI.Query<RefRO<BlockTag>>().WithNone<BlockMaterial>().WithEntityAccess())
        {
            ecb.AddComponent(entity, new BlockMaterial { MaterialName = "Default" });
            ecb.AddComponent(entity, new BlockHealth { MaxHP = 999999.0f, CurrentHP = 999999.0f, Defense = 4.0f });
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(DefaultMaterialInitSystem))]
public partial class MaterialPropertyInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var matManager = MaterialDataManager.Instance;
        if (matManager == null || matManager.MaterialDict.Count == 0) return;

        // ⭐ 모든 블록 엔티티를 순회하며 엑셀 데이터 주입
        foreach (var (mat, health, entity) in SystemAPI.Query<RefRW<BlockMaterial>, RefRW<BlockHealth>>().WithAll<BlockTag>().WithEntityAccess())
        {
            // 아직 스펙이 주입되지 않은 깡통 블록들만 처리
            if (mat.ValueRO.TensileStiffness == 0.0f)
            {
                string name = mat.ValueRO.MaterialName.ToString();
                if (!matManager.MaterialDict.ContainsKey(name)) name = "Default";

                var spec = matManager.MaterialDict[name];

                mat.ValueRW.IsBrittle = spec.IsBrittle;
                mat.ValueRW.TensileStiffness = spec.Tensile;
                mat.ValueRW.CompressiveStiffness = spec.Compressive;
                mat.ValueRW.ShearStiffness = spec.Shear;
                mat.ValueRW.BendingStiffness = spec.Bending;
                mat.ValueRW.TorsionStiffness = spec.Torsion;

                health.ValueRW.MaxHP = spec.BaseHP;
                health.ValueRW.CurrentHP = spec.BaseHP;

                // ⭐ [핵심] 질량 계산: PhysicsMass 컴포넌트가 존재할 때만 계산 수행
                if (SystemAPI.HasComponent<PhysicsMass>(entity))
                {
                    var mass = SystemAPI.GetComponentRW<PhysicsMass>(entity);
                    // 동적 블록(고정되지 않은 블록)인 경우에만 질량 설정
                    if (mass.ValueRO.InverseMass > 0.0f)
                    {
                        // 엑셀의 밀도(Density) * 부피(27.0f)를 질량으로 환산
                        float calculatedMass = spec.Density * 27.0f;
                        if (calculatedMass <= 0.0f) calculatedMass = 10.0f;
                        mass.ValueRW.InverseMass = 1.0f / calculatedMass;
                    }
                }
            }
        }
    }
}
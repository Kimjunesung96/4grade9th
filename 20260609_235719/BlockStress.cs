using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using Unity.Mathematics; // ⭐ math 에러 해결을 위해 필수 포함!

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
        foreach (var (tag, entity) in SystemAPI.Query<RefRO<BlockTag>>().WithNone<BlockMaterial>().WithEntityAccess())
        {
            ecb.AddComponent(entity, new BlockMaterial { MaterialName = "Default" });
            // ⭐ 디폴트 방어력 400 셋팅
            ecb.AddComponent(entity, new BlockHealth { MaxHP = 999999.0f, CurrentHP = 999999.0f, Defense = 400.0f });
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

        foreach (var (mat, health, entity) in SystemAPI.Query<RefRW<BlockMaterial>, RefRW<BlockHealth>>().WithAll<BlockTag>().WithEntityAccess())
        {
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

                // ⭐ 방어력을 인장/압축 중 더 약한 쪽(최솟값)으로 자동 세팅
                health.ValueRW.Defense = math.min(spec.Tensile, spec.Compressive);

                if (SystemAPI.HasComponent<PhysicsMass>(entity))
                {
                    var mass = SystemAPI.GetComponentRW<PhysicsMass>(entity);
                    if (mass.ValueRO.InverseMass > 0.0f)
                    {
                        // ⭐ [핵심] 15cm 축척 반영 (3유닛 = 0.15m)
                        float realWorldScale = 0.15f;
                        float realVolume = math.pow(realWorldScale, 3); // 0.003375m^3

                        // 실제 질량 계산 (예: 콘크리트 2.4 * 0.003375 = 0.0081t -> 8.1kg)
                        float calculatedMass = spec.Density * realVolume;

                        if (calculatedMass <= 0.0f) calculatedMass = 0.0001f;
                        mass.ValueRW.InverseMass = 1.0f / calculatedMass;
                    }
                }
            }
        }
    }
}
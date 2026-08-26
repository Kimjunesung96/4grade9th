using Unity.Entities;
using Unity.Collections;
using Unity.Physics;

public struct BlockStress : IComponentData { public float TargetStress; public float SmoothedStress; }

public struct BlockHealth : IComponentData { public float MaxHP; public float CurrentHP; public float Defense; }

// ⭐ 물리 엔진이 실시간으로 읽어갈 수 있도록 재질 스펙을 블록 몸체에 직접 박아넣음!
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
        foreach (var (mass, entity) in SystemAPI.Query<RefRW<PhysicsMass>>().WithAll<BlockTag>().WithNone<BlockMaterial>().WithEntityAccess())
        {
            // 스포너가 재질을 안 정해주면 일단 Default 라벨만 붙임 (수치는 아래 시스템이 채움)
            ecb.AddComponent(entity, new BlockMaterial { MaterialName = "Default" });
            ecb.AddComponent(entity, new BlockHealth { MaxHP = 999999f, CurrentHP = 999999f, Defense = 4.0f });
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

// ⭐ [핵심 공장] 라벨만 붙은 블록을 찾아서 엑셀(MaterialDataManager)의 스펙을 그대로 주입!
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(DefaultMaterialInitSystem))]
public partial class MaterialPropertyInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var matManager = MaterialDataManager.Instance;
        if (matManager == null || matManager.MaterialDict.Count == 0) return;

        foreach (var (mat, mass, health) in SystemAPI.Query<RefRW<BlockMaterial>, RefRW<PhysicsMass>, RefRW<BlockHealth>>().WithAll<BlockTag>())
        {
            // 인장강도가 0이면 아직 엑셀 수치를 못 받은 '깡통 블록'이라는 뜻!
            if (mat.ValueRO.TensileStiffness == 0f)
            {
                string name = mat.ValueRO.MaterialName.ToString();
                if (!matManager.MaterialDict.ContainsKey(name)) name = "Default";

                var spec = matManager.MaterialDict[name];

                // 엑셀 수치 완벽 복사!
                mat.ValueRW.IsBrittle = spec.IsBrittle;
                mat.ValueRW.TensileStiffness = spec.Tensile;
                mat.ValueRW.CompressiveStiffness = spec.Compressive;
                mat.ValueRW.ShearStiffness = spec.Shear;
                mat.ValueRW.BendingStiffness = spec.Bending;
                mat.ValueRW.TorsionStiffness = spec.Torsion;

                health.ValueRW.MaxHP = spec.BaseHP;
                health.ValueRW.CurrentHP = spec.BaseHP;

                // ⭐ 밀도에 따른 질량 자동 계산 (밀도 * 3x3x3 부피)
                if (mass.ValueRO.InverseMass > 0f)
                {
                    float calculatedMass = spec.Density * 27.0f;
                    if (calculatedMass <= 0f) calculatedMass = 10f;
                    mass.ValueRW.InverseMass = 1.0f / calculatedMass;
                }
            }
        }
    }
}
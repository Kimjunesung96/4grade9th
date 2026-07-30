using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using Unity.Mathematics; // ⭐ math 에러 해결을 위해 필수 포함!

public struct BlockStress : IComponentData { 
    public float TargetStress; 
    public float SmoothedStress;
    public float MaxTensileRatio; // ⭐ 5초 동안 조인트가 당겨진 최대 비율을 기억할 공간 추가!
}

// ⭐ 스캔 도중 원래 위치에서 가장 멀리 밀려난 지점을 기록 (CSV PosX/Y/Z에 최종 반영됨)
public struct BlockDisplacement : IComponentData { public float3 MaxPos; public float MaxDist; }

// ⭐ 조인트가 처음 만들어졌을 때의 "자연 길이". 이보다 늘어나면 그만큼 당겨진(인장) 것으로 판정
public struct JointRestLength : IComponentData { public float Value; }

public struct BlockMaterial : IComponentData
{
    public FixedString32Bytes MaterialName;
    public bool IsBrittle;
    public float TensileStiffness;
    public float CompressiveStiffness;
    public float ShearStiffness;
    public float BendingStiffness;
    public float TorsionStiffness;

    // ⭐ [재질별 조인트 탄성] Fixed 조인트를 재질별 스프링으로 대체하기 위한 값.
    //    SpringFrequency: 높을수록 뻣뻣함(변형 적게 허용). 낮을수록 늘어나는 유격이 커짐.
    //    SpringDamping(0~1): 낮으면 잘 안 흔들리다 툭 끊기는 취성(brittle) 느낌,
    //                        높으면 늘어났다 줄었다 버티는 연성(ductile) 느낌.
    public float SpringFrequency;
    public float SpringDamping;
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

        foreach (var (mat, entity) in SystemAPI.Query<RefRW<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
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

                // ⭐ [재질별 조인트 탄성 유도] 새 CSV 컬럼 없이 기존 Bending/IsBrittle 값만으로 계산.
                //    - 휨강도(Bending)가 높을수록 뻣뻣함(SpringFrequency↑) → H_Beam/Steel처럼 잘 안 휘는 재질
                //    - 취성 재질(Concrete/Wood/Glass)은 감쇠를 낮게(잘 안 버티고 툭 끊김),
                //      연성 재질(Steel/RC/H_Beam)은 감쇠를 높게(늘어났다 줄었다 버팀)
                //    ⚠️ 수치(5f~60f, 0.05f~0.6f)는 1차 추정값이라 플레이테스트하면서 조정 필요.
                mat.ValueRW.SpringFrequency = math.clamp(spec.Bending / 10f, 5f, 60f);
                mat.ValueRW.SpringDamping = spec.IsBrittle ? 0.05f : 0.6f;

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
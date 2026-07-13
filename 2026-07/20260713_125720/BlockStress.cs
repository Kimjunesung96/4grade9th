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

// ⭐ 최적화: 초기화가 완료되었음을 알리는 태그 (시스템 클래스 밖에 선언하는 것이 안전합니다)
public struct MatInitTag : IComponentData { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(DefaultMaterialInitSystem))]
public partial class MaterialPropertyInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var matManager = MaterialDataManager.Instance;
        if (matManager == null || matManager.MaterialDict.Count == 0) return;

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // WithNone을 사용해 이미 초기화된 블록은 순회에서 완벽히 배제
        foreach (var (mat, entity) in SystemAPI.Query<RefRW<BlockMaterial>>().WithAll<BlockTag>().WithNone<MatInitTag>().WithEntityAccess())
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
                
                // ⭐ 작업이 끝난 블록에 완료 태그를 달아줍니다.
                ecb.AddComponent<MatInitTag>(entity);
            }
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
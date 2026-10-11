using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// [현실화 v2] 조인트 인장 판정식을 한 곳으로 모은 헬퍼.
// 기존에 StressVisualizationSystem / VibrationTestSystem / ShockwaveTestSystem 3곳에 복사돼 있던
//   stress  = stretch * TensionStressScale
//   defense = (A.Tensile + B.Tensile) / 2
// 를 그대로 유지하되, UseForceBased = true 이면 재료 물성(SI 단위) 기반 MPa 계산으로 바꾼다.
//
// 힘 기반 계산 (임의 보정계수 없음):
//   k = E_eff * A / L      (N/m)   E_eff: 두 블록 E의 직렬 합성, A: 블록 단면적, L: 블록 중심 간 거리(0.15m)
//   F = k * dL             (N)     dL: 조인트가 늘어난 길이(m)
//   stress = F / A         (MPa)   = E_eff * (dL / L)  -> A는 약분되지만 힘(N)을 보기 위해 남겨 둔다
//   defense = 두 블록 인장강도 평균 (MPa)
//
// 기본값은 false (= 기존 동작 그대로). 켜려면 F5 설정창의 토글, 또는 게임 시작 시 한 번:
//   JointStressCalc.UseForceBased = true;
public static class JointStressCalc
{
    public static bool UseForceBased = false;

    const float UnitToMeter = 0.05f;   // 1유닛 = 5cm (블록 3유닛 = 0.15m, MaterialPropertyInitSystem 주석 기준)
    const float BlockSizeM  = 0.15f;
    const float AreaM2      = BlockSizeM * BlockSizeM;
    const float LengthM     = BlockSizeM;   // 인접 블록 중심 간 거리 = 조인트가 이어 주는 구간 길이 L

    // 힘 기반 계산에 필요한 값(E, 인장강도)이 두 블록 모두 채워졌는지
    static bool ForceReady(in BlockMaterial a, in BlockMaterial b)
    {
        return a.YoungModulusGPa > 0f && b.YoungModulusGPa > 0f &&
               a.TensileMPa > 0f && b.TensileMPa > 0f;
    }

    // 상부 하중 누적 압축 판정을 쓸 수 있는가 (힘 기반 모드 + 압축강도/밀도가 채워진 재료)
    public static bool CompressionForceReady(in BlockMaterial m)
    {
        return UseForceBased && m.CompressiveMPa > 0f && m.DensityTPerM3 > 0f;
    }

    // stretch: 유닛 단위 늘어난 길이. legacyScale: 기존 TensionStressScale.
    public static void Evaluate(BlockMaterial a, BlockMaterial b, float stretch, float legacyScale,
                                out float stress, out float defense)
    {
        if (!UseForceBased || !ForceReady(a, b))
        {
            // 레거시: 기존 식 그대로
            stress  = stretch * legacyScale;
            defense = (a.TensileStiffness + b.TensileStiffness) * 0.5f;
            return;
        }

        // E (GPa -> Pa). 서로 다른 재료가 이어진 구간은 각 블록 절반 길이의 직렬 스프링:
        //   1/k = (L/2)/(Ea*A) + (L/2)/(Eb*A)  ->  E_eff = 2*Ea*Eb / (Ea + Eb)
        float eA = a.YoungModulusGPa * 1.0e9f;
        float eB = b.YoungModulusGPa * 1.0e9f;
        float eEff = 2f * eA * eB / (eA + eB);

        float k = eEff * AreaM2 / LengthM;               // N/m
        float forceN = k * (stretch * UnitToMeter);      // N   (보정계수 없음)
        stress  = forceN / AreaM2 / 1.0e6f;              // MPa
        defense = (a.TensileMPa + b.TensileMPa) * 0.5f;  // MPa
    }

    // 이 재료 쌍이 (힘 기반 모드에서) 인장으로 끊어지기 시작하는 늘어난 길이(유닛). 디버그/발표 설명용.
    public static float BreakStretchUnits(in BlockMaterial a, in BlockMaterial b)
    {
        if (!ForceReady(a, b)) return -1f;
        float eA = a.YoungModulusGPa * 1.0e9f;
        float eB = b.YoungModulusGPa * 1.0e9f;
        float eEff = 2f * eA * eB / (eA + eB);
        float defensePa = (a.TensileMPa + b.TensileMPa) * 0.5f * 1.0e6f;
        return (defensePa / eEff) * LengthM / UnitToMeter;
    }

    // ───────── 부서짐 판정 (단일화) ─────────
    // 조인트가 "끊어지는가"는 여기서만 정한다.
    // 테스트 시스템(정적/진동/충격파)은 하중을 주고, 5초 타이머를 돌리고, 복귀만 담당한다.
    // 끊긴 뒤 연출(속도 리셋, 물리 해제)은 각 테스트가 따로 해도 되지만, 끊김 여부는 이 결과만 따른다.
    // 방어값은 위 Evaluate()에서 나오며, 테스트 모드/진도에 따라 달라지지 않는다.

    // 이보다 덜 늘어난 조인트는 끊지 않는다(솔버 떨림 노이즈 방지). 기존 진동/충격파 값 그대로.
    public const float MinBreakStretch = 0.05f;

    // [유리] 취성 재료는 변형이 거의 없다가 한 번에 깨진다. 유리 이론 파단 늘어남은 약 0.002유닛
    //   (인장 41MPa / E 70GPa x L/단위길이 3) 이지만 조인트가 무른 스프링이라 실제로는 그보다 더 늘어나야 하므로
    //   임시값 0.02를 쓴다. 시작하자마자 혼자 깨지면 0.03~0.04로 올리고, 안 깨지면 0.01로 내릴 것.
    //   F5 설정창 슬라이더로 실행 중에 조절할 수 있게 const가 아니라 static으로 둠.
    public static float GlassBreakStretch = 0.02f;

    // [하드 상한] MPa 모드에서도 "재료가 버틸 수 있는 한계 이상으로 늘어나면 끊긴다"를 보장하는 값(유닛).
    //   조인트가 실제 재료보다 훨씬 무른 스프링이라 E·A/L 환산 늘어남으로는 판정이 안 되므로,
    //   재질 종류(취성/연성)별 임시 상한을 둔다. 한쪽이라도 취성이면(콘크리트+철근 등) 취성 상한을 쓴다.
    //   사진처럼 벽이 천처럼 늘어지는 건 이 값을 넘기 전까지 늘어난다는 뜻이므로, 너무 늘어지면 값을 내릴 것.
    //   시작하자마자 멀쩡한 건물이 혼자 무너지면 값을 올릴 것.
    public static float BrittleBreakStretch = 0.10f;   // 콘크리트, 나무 등
    public static float DuctileBreakStretch = 0.30f;   // 철 등

    static bool IsGlass(in BlockMaterial m)
    {
        return m.MaterialName.Equals(new FixedString32Bytes("Glass"));
    }

    // restLength: 평상시 앵커 간 거리. 없으면 0 (재질 조인트는 앵커가 겹치게 만들어져 0에 가깝다).
    // ratio: 응력/방어값 (색·기록용). stretch가 0이면 0.
    // 반환: true면 이 조인트를 끊어야 한다.
    public static bool ShouldBreak(in PhysicsJoint joint, in LocalTransform transA, in LocalTransform transB,
                                   in BlockMaterial matA, in BlockMaterial matB,
                                   float restLength, float legacyScale, out float ratio)
    {
        float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.BodyAFromJoint.Position);
        float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.BodyBFromJoint.Position);
        float stretch = math.max(0.0f, math.distance(pivotA, pivotB) - restLength);

        ratio = 0.0f;
        if (stretch <= 0.0f) return false;

        // 힘 기반 모드: 조인트 스프링은 실제 재료보다 훨씬 무르기 때문에 늘어난 길이로는 재료 변형률을 잴 수 없다.
        // (E·A/L 로 환산하면 콘크리트는 0.00027유닛에서 끊어져야 하지만 판정 하한은 0.05유닛이라 전 재료가 똑같이 끊어진다.)
        // 그래서 힘 기반 모드의 부서짐은 StructuralLoadSolver(하중 누적 + 휨 단면 사용률)가 맡고, 여기서는 끊지 않는다.
        // 단, 유리는 솔버(정적 자중 하중)로는 충격에 안 깨지므로 예외로 두고 아래 늘어남 판정을 탄다.
        //   유리가 한쪽이라도 끼어 있으면(창틀=콘크리트+유리) 약한 쪽 기준으로 끊는다.
        bool glass = IsGlass(matA) || IsGlass(matB);
        if (UseForceBased && ForceReady(matA, matB) && !glass)
        {
            // 솔버 판정은 그대로 두되, 충격/과도한 변형으로 한계 이상 늘어난 조인트는 여기서도 끊는다.
            float cap = (matA.IsBrittle || matB.IsBrittle) ? BrittleBreakStretch : DuctileBreakStretch;
            ratio = stretch / cap;
            return stretch > cap;
        }

        Evaluate(matA, matB, stretch, legacyScale, out float stress, out float defense);
        // 레거시 단위(방어값 수십~수백)는 기존처럼 1 이하로 안 내려가게 막고,
        // MPa 단위(콘크리트 2.9 등)는 바닥을 1e-3으로 낮춰 비율이 왜곡되지 않게 한다.
        float defenseFloor = (UseForceBased && ForceReady(matA, matB)) ? 1.0e-3f : 1.0f;
        ratio = stress / math.max(defenseFloor, defense);

        float minStretch = glass ? GlassBreakStretch : MinBreakStretch;
        return stretch > minStretch && stress > defense;
    }
}
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// [현실화 v1] 조인트 인장 판정식을 한 곳으로 모은 헬퍼.
// 기존에 StressVisualizationSystem / VibrationTestSystem / ShockwaveTestSystem 3곳에 복사돼 있던
//   stress  = stretch * TensionStressScale
//   defense = (A.Tensile + B.Tensile) / 2
// 를 그대로 유지하되, UseForceBased = true 이면 힘 기반 MPa 계산으로 바꾼다.
//
// 기본값은 false (= 기존 동작 그대로). 켜려면 게임 시작 시 한 번:
//   JointStressCalc.UseForceBased = true;
public static class JointStressCalc
{
    public static bool UseForceBased = false;

    // ⚠️ 임시 보정계수. 조인트 스프링(수십 Hz)은 실제 재료 강성(E·A/L)보다 훨씬 무르다.
    //   그대로 F = k·stretch 를 쓰면 실제 단위로는 거의 아무것도 끊기지 않는다.
    //   1000 은 "기존 방식과 비슷한 민감도"로 맞춘 출발값일 뿐이고, 외팔보 검증(11주차)으로 다시 잡아야 한다.
    public static float ForceCalibration = 1000f;

    const float UnitToMeter = 0.05f;   // 1유닛 = 5cm (블록 3유닛 = 0.15m, MaterialPropertyInitSystem 주석 기준)
    const float BlockSizeM  = 0.15f;
    const float AreaM2      = BlockSizeM * BlockSizeM;
    const float VolumeM3    = BlockSizeM * BlockSizeM * BlockSizeM;

    // stretch: 유닛 단위 늘어난 길이. legacyScale: 기존 TensionStressScale.
    public static void Evaluate(BlockMaterial a, BlockMaterial b, float stretch, float legacyScale,
                                out float stress, out float defense)
    {
        bool newColumnsReady = a.TensileMPa > 0f && b.TensileMPa > 0f &&
                               a.DensityTPerM3 > 0f && b.DensityTPerM3 > 0f &&
                               a.SpringFrequency > 0f && b.SpringFrequency > 0f;

        if (!UseForceBased || !newColumnsReady)
        {
            // 레거시: 기존 식 그대로
            stress  = stretch * legacyScale;
            defense = (a.TensileStiffness + b.TensileStiffness) * 0.5f;
            return;
        }

        // 두 블록 질량(kg) -> 환산질량(reduced mass)
        float mA = a.DensityTPerM3 * 1000f * VolumeM3;
        float mB = b.DensityTPerM3 * 1000f * VolumeM3;
        float mEff = (mA * mB) / (mA + mB);

        // 조인트가 실제로 거는 스프링: k = m_eff * (2*pi*f)^2  (f 는 CreateMaterialJoint 와 같은 평균값)
        float f = (a.SpringFrequency + b.SpringFrequency) * 0.5f;
        float w = 2f * math.PI * f;
        float k = mEff * w * w;                          // N/m

        float forceN = k * (stretch * UnitToMeter) * ForceCalibration;
        stress  = forceN / AreaM2 / 1.0e6f;              // MPa
        defense = (a.TensileMPa + b.TensileMPa) * 0.5f;  // MPa
    }

    // ───────── 부서짐 판정 (단일화) ─────────
    // 조인트가 "끊어지는가"는 여기서만 정한다.
    // 테스트 시스템(정적/진동/충격파)은 하중을 주고, 5초 타이머를 돌리고, 복귀만 담당한다.
    // 끊긴 뒤 연출(속도 리셋, 물리 해제)은 각 테스트가 따로 해도 되지만, 끊김 여부는 이 결과만 따른다.
    // 방어값은 위 Evaluate()에서 나오며, 테스트 모드/진도에 따라 달라지지 않는다.

    // 이보다 덜 늘어난 조인트는 끊지 않는다(솔버 떨림 노이즈 방지). 기존 진동/충격파 값 그대로.
    public const float MinBreakStretch = 0.05f;

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

        Evaluate(matA, matB, stretch, legacyScale, out float stress, out float defense);
        ratio = stress / math.max(1.0f, defense);

        return stretch > MinBreakStretch && stress > defense;
    }
}
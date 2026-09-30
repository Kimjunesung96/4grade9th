using Unity.Mathematics;

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
}
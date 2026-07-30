using UnityEngine;

// ⭐ [설정 통합] 여러 파일(ReinforcementManager, VibrationTestSystem, ShockwaveTestSystem,
//    StressVisualizationSystem)에 따로따로 박혀있던 "밸런싱용 수치"만 모아놓은 중앙 설정.
//
//    ⚠️ 여기 없는 값(블록 크기 3.0f, 오프셋 1.5f, 위험도 임계값 0.5f/2.0f 등)은
//    "이미 검증 완료되어 건드리면 안 되는 값"이라 의도적으로 제외함.
//
//    사용법: Assets > Create > VirtualConstruct > Simulation Settings 로 에셋 하나 생성 후,
//    각 매니저/시스템에서 이 에셋을 참조.
[CreateAssetMenu(fileName = "SimulationSettings", menuName = "VirtualConstruct/Simulation Settings")]
public class SimulationSettings : ScriptableObject
{
    [Header("응력 시각화 — 조인트 인장 스트레스 증폭 배율")]
    [Tooltip("500 단위 스텝. 기본값 10칸 = 5000 (원본 VibrationTestSystem/ShockwaveTestSystem 하드코딩값과 동일)")]
    [Range(1, 20)]
    public int tensionStressScaleSteps = 10; // 10 * 500 = 5000 (원본값 그대로)

    public float TensionStressScale => tensionStressScaleSteps * 500f;

    [Header("테스트 지속시간 (초, 정수)")]
    [Tooltip("StressVisualizationSystem.StartScan() 의 scanTimer 원본값 = 5")]
    [Range(1, 15)]
    public int gravityScanMaxTime = 5;

    [Tooltip("VibrationTestSystem.MAX_VIBE_TIME 원본값 = 5")]
    [Range(1, 15)]
    public int vibrationMaxTime = 5;

    [Tooltip("ShockwaveTestSystem.MAX_SHOCK_TIME 원본값 = 5")]
    [Range(1, 15)]
    public int shockwaveMaxTime = 5;

    [Header("보강 시스템 — 타워/격자 간격 (블록 단위)")]
    [Tooltip("3유닛(블록 1칸) 배수. 기본값 4블록 = 12.0f (원본 ReinforcementManager 하드코딩값과 동일)\n" +
             "⚠️ 범위를 3~5로 제한함: 이 값이 격자 스냅/브릿지 연결 판정(±0.1f 허용오차)과 얽혀있어서," +
             " 원래값(4)에서 많이 벗어나면 조인트가 안 이어져 중력 테스트(V) 시 구조 전체가 무너짐(1층만 남음)이 확인됨.")]
    [Range(3, 5)]
    public int towerOffsetBlocks = 4; // 4 * 3.0f = 12.0f (원본값 그대로)

    // 파생값: 블록크기(3.0f)는 이 설정 밖(각 파일)에 여전히 고정 상수로 존재.
    // 여기서는 towerOffsetBlocks와의 계산에만 3.0f를 곱해서 사용.
    private const float BLOCK_SIZE_REF = 3.0f;

    public float TowerOffsetDistance => towerOffsetBlocks * BLOCK_SIZE_REF;
    public float BridgeToleranceMin => TowerOffsetDistance - 0.1f;
    public float BridgeToleranceMax => TowerOffsetDistance + 0.1f;
}
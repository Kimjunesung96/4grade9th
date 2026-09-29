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
    [Header("물리 솔버 반복 횟수 (성능 ↔ 안정성)")]
    [Tooltip("PhysicsStep.SolverIterationCount. 원본 하드코딩값 = 2.\n" +
             "낮을수록 빠르지만 스프링 조인트가 덜 정확하게 수렴함(떨림/어긋남 가능).\n" +
             "Unity Physics 기본값은 4. 하중이 몰리는 구조/스프링 조인트가 많을수록 높여야 안정적.")]
    [Range(1, 10)]
    public int solverIterationCount = 2; // 원본값 그대로 (SpawnerSystem.cs 하드코딩값)

    [Header("응력 시각화 — 조인트 인장 스트레스 증폭 배율")]
    [Tooltip("0 = 1배(증폭 없음), 1부터는 500 단위 스텝. 기본값 10칸 = 5000")]
    [Range(0, 20)] // ⭐ 최소값을 0으로 변경!
    public int tensionStressScaleSteps = 10; 

    // ⭐ 0일 때는 증폭 없이 순수 1배율(1.0f) 적용!
    public float TensionStressScale => tensionStressScaleSteps == 0 ? 1.0f : tensionStressScaleSteps * 500f;

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

    [Header("카메라 — 마우스 좌우 회전 감도")]
    [Tooltip("CameraFollowSystem의 마우스 X 위치 → yaw 회전 반응 배율. 1.0 = 기본(최대 ±90도).\n" +
             "낮추면 마우스를 화면 끝까지 옮겨도 살짝만 돌고, 높이면 조금만 옮겨도 많이 돎.")]
    [Range(0.2f, 3f)]
    public float cameraMouseSensitivity = 1.0f;

    [Header("카메라 — 회전 고정 모드")]
    [Tooltip("체크하면 마우스로 카메라를 돌리지 않고, 옛날처럼 항상 캐릭터 정뒤(고정 각도)에서만 봄.\n" +
             "켜져있는 동안 회전 각도(yaw)는 0(정면)으로 부드럽게 복귀함.")]
    public bool lockCameraRotation = false;

    [Header("카메라 — 내려다보는 각도 (피치)")]
    [Tooltip("CameraFollowSystem의 Pitch 원본값 = 30. 낮추면 수평(눈높이)에 가까워지고, 높이면 위에서 내려다보는 하향뷰가 됨.\n" +
             "거리(Distance)는 고정된 채 이 각도로만 재계산되어 카메라가 항상 캐릭터와 일정 거리를 유지함.")]
    [Range(5f, 80f)]
    public float cameraPitch = 30f;

    [Header("블록 조인트 — 스프링 강성(Hz) 범위")]
    [Tooltip("SpawnerSystem.GetSpringParams의 clamp(Bending/10, min, max) 원본값 = 5~60.\n" +
             "낮을수록 물렁(슬라임처럼 휘청), 높을수록 뻣뻣(강체에 가까움).\n" +
             "재질별 Bending 차이는 유지한 채 전체 범위만 위/아래로 옮김.")]
    [Range(1f, 60f)]
    public float springFrequencyMin = 5f;
    [Range(20f, 200f)]
    public float springFrequencyMax = 60f;

    [Header("블록 조인트 — 감쇠(Damping Ratio)")]
    [Tooltip("SpawnerSystem.GetSpringParams 원본값: Brittle(잘 깨짐) = 0.05, 일반 = 0.6.\n" +
             "낮을수록 충격 후 앞뒤로 오래 왕복 진동(그 진동 응력으로 파괴됨), 높을수록 빨리 진동이 죽음.\n" +
             "0.05는 거의 무감쇠라 비현실적으로 오래 흔들릴 수 있음 — 여기서 현실적인 값을 찾아보기.")]
    [Range(0.01f, 1f)]
    public float springDampingBrittle = 0.05f;
    [Range(0.01f, 1f)]
    public float springDampingNormal = 0.6f;

    // 파생값: 블록크기(3.0f)는 이 설정 밖(각 파일)에 여전히 고정 상수로 존재.
    // 여기서는 towerOffsetBlocks와의 계산에만 3.0f를 곱해서 사용.
    private const float BLOCK_SIZE_REF = 3.0f;

    public float TowerOffsetDistance => towerOffsetBlocks * BLOCK_SIZE_REF;
    public float BridgeToleranceMin => TowerOffsetDistance - 0.1f;
    public float BridgeToleranceMax => TowerOffsetDistance + 0.1f;
}
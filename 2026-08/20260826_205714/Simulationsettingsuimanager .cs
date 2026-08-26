using UnityEngine;

// ⭐ [인게임 설정창] F1 키로 여닫는 SimulationSettings 조절 패널.
//    BudgetUIManager와 동일한 OnGUI 즉시모드 스타일로 작성.
//    실행 중 슬라이더를 움직이면 SimulationSettingsProvider.Instance(=SimulationSettings 에셋)의
//    값이 그 자리에서 바뀌고, VibrationTestSystem/ShockwaveTestSystem/StressVisualizationSystem/
//    ReinforcementManager가 다음 호출 때부터 바로 그 값을 읽어감 (별도 Apply 필요 없음).
public class SimulationSettingsUIManager : MonoBehaviour
{
    public static SimulationSettingsUIManager Instance;

    [Tooltip("설정창을 여닫는 키 (F1은 F1HelpManual이 이미 사용 중이라 F5로 기본 설정)")]
    public KeyCode toggleKey = KeyCode.F5;

    private bool showPanel = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            showPanel = !showPanel;
        }
    }

    void OnGUI()
    {
        if (!showPanel) return;

        var settings = SimulationSettingsProvider.Instance;

        float panelW = 420f;
        float panelH = 340f;
        float panelX = (Screen.width - panelW) / 2f;
        float panelY = (Screen.height - panelH) / 2f;

        GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH), GUI.skin.box);

        GUILayout.Label("⚙ 시뮬레이션 설정 (F5로 닫기)", GUI.skin.box);
        GUILayout.Space(8);

        if (settings == null)
        {
            GUILayout.Label("⚠ SimulationSettings 에셋이 연결되지 않았습니다.");
            GUILayout.Label("SimulationManager의 Settings Asset 슬롯을 확인하세요.");
            GUILayout.EndArea();
            return;
        }

        DrawIntSlider("물리 솔버 반복 횟수", ref settings.solverIterationCount, 1, 10, "회");

        GUILayout.Space(6);
        // ⭐ 슬라이더의 최소 범위를 0으로 변경!
        DrawIntSlider("응력 증폭 배율", ref settings.tensionStressScaleSteps, 0, 20,
            $"= {settings.TensionStressScale:0}");

        GUILayout.Space(6);
        DrawIntSlider("중력(응력) 테스트 지속시간(초)", ref settings.gravityScanMaxTime, 1, 15, "초");
        DrawIntSlider("지진 테스트 지속시간(초)", ref settings.vibrationMaxTime, 1, 15, "초");
        DrawIntSlider("폭발 테스트 지속시간(초)", ref settings.shockwaveMaxTime, 1, 15, "초");

        GUILayout.Space(6);
        DrawIntSlider("보강 타워 간격(블록 단위)", ref settings.towerOffsetBlocks, 3, 5,
            $"= {settings.TowerOffsetDistance:0.0}");

        GUILayout.EndArea();
    }

    // 정수 슬라이더 하나 그리기 (드래그 시 소수점 없이 1단위로 딱딱 스냅)
    private void DrawIntSlider(string label, ref int value, int min, int max, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210));
        float sliderVal = GUILayout.HorizontalSlider((float)value, min, max, GUILayout.Width(120));
        value = Mathf.RoundToInt(sliderVal);
        GUILayout.Label($"{value} {suffix}", GUILayout.Width(70));
        GUILayout.EndHorizontal();
    }
}
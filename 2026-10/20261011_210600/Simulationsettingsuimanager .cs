using System.Globalization;
using System.Text;
using UnityEngine;

// ⭐ [인게임 설정창] F5 키로 여닫는 SimulationSettings 조절 패널.
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

    // 외부(ESC 메뉴 등)에서 설정창을 열 수 있도록 추가한 public 메서드
    public void OpenPanel()
    {
        showPanel = true;
    }

    public void TogglePanel()
    {
        showPanel = !showPanel;
    }

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

    private Vector2 scrollPos;   // 설정 항목이 늘어나 패널 높이를 넘으면 스크롤
    private float copiedAt = -100f;   // 마지막으로 복사한 시각 ("복사됨" 표시용)

    private static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    // ⭐ 현재 설정값 전부를 텍스트로 만들어 클립보드에 넣는다. (붙여넣기 = Ctrl+V)
    private void CopySettingsToClipboard(SimulationSettings st)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[SimulationSettings]");
        sb.AppendLine("solverIterationCount = " + st.solverIterationCount);
        sb.AppendLine("tensionStressScaleSteps = " + st.tensionStressScaleSteps + " (배율 " + F(st.TensionStressScale) + ")");
        sb.AppendLine("gravityScanMaxTime = " + st.gravityScanMaxTime);
        sb.AppendLine("vibrationMaxTime = " + st.vibrationMaxTime);
        sb.AppendLine("shockwaveMaxTime = " + st.shockwaveMaxTime);
        sb.AppendLine("towerOffsetBlocks = " + st.towerOffsetBlocks + " (거리 " + F(st.TowerOffsetDistance) + ")");
        sb.AppendLine("cameraMouseSensitivity = " + F(st.cameraMouseSensitivity));
        sb.AppendLine("lockCameraRotation = " + st.lockCameraRotation);
        sb.AppendLine("cameraPitch = " + F(st.cameraPitch));
        sb.AppendLine("springFrequencyMin = " + F(st.springFrequencyMin));
        sb.AppendLine("springFrequencyMax = " + F(st.springFrequencyMax));
        sb.AppendLine("springDampingBrittle = " + F(st.springDampingBrittle));
        sb.AppendLine("springDampingNormal = " + F(st.springDampingNormal));
        sb.AppendLine();
        sb.AppendLine("[재료 강도 기준]");
        sb.AppendLine("UseForceBased(현실 재료값 MPa 모드) = " + JointStressCalc.UseForceBased);
        sb.AppendLine("MinBreakStretch = " + F(JointStressCalc.MinBreakStretch));
        sb.AppendLine("BrittleBreakStretch = " + F(JointStressCalc.BrittleBreakStretch));
        sb.AppendLine("DuctileBreakStretch = " + F(JointStressCalc.DuctileBreakStretch));
        sb.AppendLine("GlassBreakStretch = " + F(JointStressCalc.GlassBreakStretch));
        sb.AppendLine("IncludeShear = " + StructuralLoadSolver.IncludeShear);
        sb.AppendLine("IncludeBuckling = " + StructuralLoadSolver.IncludeBuckling);
        sb.AppendLine("BucklingK = " + F(StructuralLoadSolver.BucklingK));
        sb.AppendLine("EarthquakeG = " + F(StructuralLoadSolver.EarthquakeG));
        sb.AppendLine("EarthquakeDirDeg = " + F(StructuralLoadSolver.EarthquakeDirDeg));
        sb.AppendLine("QuakeFreqHz = " + F(StructuralLoadSolver.QuakeFreqHz));
        sb.AppendLine("WindSpeedMs = " + F(StructuralLoadSolver.WindSpeedMs));
        sb.AppendLine("WindDirDeg = " + F(StructuralLoadSolver.WindDirDeg));
        sb.AppendLine();
        sb.AppendLine("바람 방향 = " + StressVisualizationSystem.WindClockLabel);
        sb.AppendLine();
        sb.AppendLine("[마지막 판정 결과]");
        sb.AppendLine("바람: 노출 면 " + StructuralLoadSolver.LastWindExposedFaces + "개 / 최대 사용률 " + F(StructuralLoadSolver.LastWindMaxUtil * 100f) + "% / 파괴 블록 " + StructuralLoadSolver.LastWindFailedBlocks + "개");
        sb.AppendLine("B모드: PGA " + F(StructuralLoadSolver.LastPgaG) + "g / 고유진동수 약 " + F(StructuralLoadSolver.LastQuakeFnHz) + "Hz / 증폭 x" + F(StructuralLoadSolver.LastQuakeAmplify));
        sb.AppendLine("최대 사용률 = " + F(StructuralLoadSolver.LastMaxUtilization * 100f) + "% (100% 이상 = 파괴)");
        sb.AppendLine("최대 축력 " + F(StructuralLoadSolver.LastMaxAxialN) + " N / 밑면 전단 " + F(StructuralLoadSolver.LastBaseShearN) + " N / 최대 모멘트 " + F(StructuralLoadSolver.LastMaxMomentNm) + " N·m");
        sb.AppendLine("블록 수 = " + StructuralLoadSolver.LastBlockCount + " (내민/미지지 " + StructuralLoadSolver.LastOverhangCount + ")");

        GUIUtility.systemCopyBuffer = sb.ToString();
        copiedAt = Time.unscaledTime;
    }

    void OnGUI()
    {
        if (!showPanel) return;

        GUI.skin = UIStyleKit.Skin;

        var settings = SimulationSettingsProvider.Instance;

        float panelW = 450f;
        float panelH = Mathf.Min(560f, Screen.height - 20f);
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

        // ⭐ 설정값 복사 버튼 (누르면 클립보드에 전부 복사 → Ctrl+V로 붙여넣기)
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("📋 설정값 복사 (Ctrl+V로 붙여넣기)"))
        {
            CopySettingsToClipboard(settings);
        }
        if (Time.unscaledTime - copiedAt < 2f) GUILayout.Label("복사됨!", GUILayout.Width(60));
        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        scrollPos = GUILayout.BeginScrollView(scrollPos, false, false, GUILayout.Width(panelW - 12f));

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

        GUILayout.Space(6);
        DrawFloatSlider("카메라 마우스 감도", ref settings.cameraMouseSensitivity, 0.2f, 3f, "배");

        GUILayout.Space(6);
        settings.lockCameraRotation = GUILayout.Toggle(settings.lockCameraRotation, " 카메라 회전 고정 (항상 정뒤 시점)");

        GUILayout.Space(6);
        DrawSteppedFloatSlider("카메라 각도(피치)", ref settings.cameraPitch, 5f, 80f, 5f, "도");

        GUILayout.Space(6);
        GUILayout.Label("— 블록 조인트 스프링 —");
        DrawFloatSlider("스프링 강성 최소(Hz)", ref settings.springFrequencyMin, 1f, 60f, "Hz");
        DrawFloatSlider("스프링 강성 최대(Hz)", ref settings.springFrequencyMax, 20f, 200f, "Hz");
        DrawFloatSlider("감쇠 - Brittle(잘 깨짐)", ref settings.springDampingBrittle, 0.01f, 1f, "");
        DrawFloatSlider("감쇠 - 일반", ref settings.springDampingNormal, 0.01f, 1f, "");

        GUILayout.Space(6);
        GUILayout.Label("— 재료 강도 기준 —");
        // 끔 = 기존 게임 수치(TensileStiffness), 켬 = 재료 실측값(MPa) 기반 판정. 다음 판정 틱부터 바로 적용됨.
        JointStressCalc.UseForceBased = GUILayout.Toggle(JointStressCalc.UseForceBased, " 현실 재료값(MPa) 모드");
        if (JointStressCalc.UseForceBased)
        {
            // 보정계수 없이 k = E·A/L, F = k·dL, 응력 = F/A 로 계산한다.
            GUILayout.Label("k=E·A/L → F=k·ΔL → σ=F/A (보정계수 없음)");
            // 판정 항목: 전단(임시 강도 0.6·min(fc,ft)), 오일러 좌굴
            // 늘어남 하드 상한(유닛): 이 이상 늘어난 조인트는 MPa 모드에서도 끊긴다
            DrawFloatSlider3("파단 늘어남 - 취성(콘크리트 등)", ref JointStressCalc.BrittleBreakStretch, 0.03f, 0.5f, "유닛");
            DrawFloatSlider3("파단 늘어남 - 연성(철 등)", ref JointStressCalc.DuctileBreakStretch, 0.1f, 1.5f, "유닛");
            DrawFloatSlider3("파단 늘어남 - 유리", ref JointStressCalc.GlassBreakStretch, 0.005f, 0.1f, "유닛");
            StructuralLoadSolver.IncludeShear    = GUILayout.Toggle(StructuralLoadSolver.IncludeShear, " 전단 판정");
            StructuralLoadSolver.IncludeBuckling = GUILayout.Toggle(StructuralLoadSolver.IncludeBuckling, " 좌굴 판정 (오일러, K=" + StructuralLoadSolver.BucklingK.ToString("0.#") + ")");
            // 지진 (정적 등가): F = m·a. 0이면 자중만 계산한다.
            DrawFloatSlider("지진 가속도 (V/M 스캔용)", ref StructuralLoadSolver.EarthquakeG, 0f, 1.0f, "g");
            DrawSteppedFloatSlider("지진 방향 (0=X, 90=Z)", ref StructuralLoadSolver.EarthquakeDirDeg, 0f, 345f, 15f, "도");
            // B모드(진동) 동적 지진: 진도 -> PGA, 구조물 고유진동수와 가진 주파수로 증폭
            DrawFloatSlider("B모드 가진 주파수", ref StructuralLoadSolver.QuakeFreqHz, 0.5f, 10f, "Hz");
            // T→5 바람 모드: 정적 풍하중 (노출 면 × 풍압)
            DrawFloatSlider("바람 풍속", ref StructuralLoadSolver.WindSpeedMs, 0f, 80f, "m/s");
            GUILayout.Label($"바람 방향: {StressVisualizationSystem.WindClockLabel} (바람 모드에서 좌/우클릭으로 30° 회전)");
            GUILayout.Label($"바람: 노출 면 {StructuralLoadSolver.LastWindExposedFaces}개 / 최대 사용률 {StructuralLoadSolver.LastWindMaxUtil * 100f:0.0}% / 파괴 블록 {StructuralLoadSolver.LastWindFailedBlocks}개");
            GUILayout.Label($"B모드: PGA {StructuralLoadSolver.LastPgaG:0.000}g / 고유진동수 약 {StructuralLoadSolver.LastQuakeFnHz:0.0}Hz / 증폭 x{StructuralLoadSolver.LastQuakeAmplify:0.00}");
            // 마지막 판정 틱(스캔 중 1초마다)의 결과
            GUILayout.Label($"최대 사용률 {StructuralLoadSolver.LastMaxUtilization * 100f:0.0}%  (100% 이상 = 파괴)");
            GUILayout.Label($"최대 축력 {StructuralLoadSolver.LastMaxAxialN:0} N / 밑면 전단 {StructuralLoadSolver.LastBaseShearN:0} N / 최대 모멘트 {StructuralLoadSolver.LastMaxMomentNm:0} N·m");
            GUILayout.Label($"블록 {StructuralLoadSolver.LastBlockCount}개 (내민/미지지 {StructuralLoadSolver.LastOverhangCount}개)");
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 실수 슬라이더인데 step 단위로 딱딱 끊어서(스냅) 표시/저장하는 버전
    private void DrawSteppedFloatSlider(string label, ref float value, float min, float max, float step, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210));
        float sliderVal = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        value = Mathf.Round(sliderVal / step) * step;
        GUILayout.Label($"{value:0} {suffix}", GUILayout.Width(70));
        GUILayout.EndHorizontal();
    }

    // 실수 슬라이더 (소수점 3자리 표시 - 작은 값용)
    private void DrawFloatSlider3(string label, ref float value, float min, float max, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        GUILayout.Label($"{value:0.000} {suffix}", GUILayout.Width(70));
        GUILayout.EndHorizontal();
    }

    // 실수 슬라이더 하나 그리기 (소수점 1자리로 표시)
    private void DrawFloatSlider(string label, ref float value, float min, float max, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        GUILayout.Label($"{value:0.0} {suffix}", GUILayout.Width(70));
        GUILayout.EndHorizontal();
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
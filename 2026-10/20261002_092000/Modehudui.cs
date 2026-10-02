using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

// ⭐ [신규 파일] 기존 KeyboardOverlayUI(F2, 전체 키보드 배치)는 그대로 두고, 사이퍼즈처럼 화면 옆에 붙는 작은 HUD를 따로 추가.
//    - 지금 모드에서 "쓸 수 있는 키"만 키 아이콘 + 짧은 설명으로 세로 나열
//    - 왼쪽 세로 게이지 = 그 모드의 "세기"
//        건설: 높이(GuideHeight) / B: 진도 레벨(VibrationTestSystem.VibeLevel)
//        N: 폭발 위력(고스트 블록 수 x 5) / M: 발사 속도 배율(GravityShootMode.SpeedTier)
//    - 기존 시스템은 읽기만 함 (수정 없음). 읽는 값:
//        VibrationTestSystem.IsBModeActive / VibeLevel, ShockwaveTestSystem.IsNModeActive,
//        StressVisualizationSystem.IsGravityModeActive, GravityShootMode.SpeedTier,
//        BuilderStateData 싱글톤(CurrentMode, GuideHeight), BlockTag / GhostBlockTag 개수
//    - F4로 3단계 순환: 0=꺼짐, 1=모드 키만, 2=+공통 키/디버그 토글 (F2 키보드 오버레이와 별개로 켜고 끔)
//    - V(응력 스캔)는 상태를 외부에서 읽을 방법이 없어서(StressVisualizationSystem 내부 private) 게이지 없이 건설 모드 목록에만 있음.
//    - O/L/U/Y 모드도 SpawnerSystem 내부 private라 예전처럼 수동 토글로 대체 (2단계에서만 보임).
public class ModeHudUI : MonoBehaviour
{
    public static ModeHudUI Instance;

    public int overlayState = 1; // 0=꺼짐, 1=모드 키만, 2=모드 키 + 공통 키/디버그 토글
    public KeyCode toggleKey = KeyCode.F4; // F1/F2/F3/F5는 이미 쓰는 키라 F4. 원하는 키로 바꿔도 됨.

    [Header("게이지 최대값 (이 값에서 꽉 참)")]
    public float heightGaugeMax = 30f;   // 건설 높이
    public float nPowerGaugeMax = 500f;  // 폭발 위력 (고스트 100개 = 500)

    private EntityQuery _builderStateQuery, _blockQuery, _ghostQuery;
    private bool _queriesReady;

    // 수동 토글 (SpawnerSystem 내부 상태를 외부에서 못 읽어서 대체용)
    private bool manualBlueprintMode = false; // O/L/U 모드 흉내
    private bool manualYMode = false;         // Y모드(보강 로딩) 흉내

    private GUIStyle _keyStyle, _descStyle, _titleStyle, _gaugeTextStyle;
    private int _styleScaleKey = -1;

    private struct Hint
    {
        public string key;
        public string desc;
        public bool dim; // 공통 키(2단계)는 흐리게
        public Hint(string k, string d, bool dim = false) { key = k; desc = d; this.dim = dim; }
    }

    private readonly List<Hint> _hints = new List<Hint>(16);
    private static readonly string[] BuildModeNames = { "선택/보호", "벽", "빈 프레임", "원형", "피라미드", "원뿔", "예약" };

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void CycleOverlay()
    {
        overlayState = (overlayState + 1) % 3;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) overlayState = (overlayState + 1) % 3;
    }

    private void EnsureQueries()
    {
        if (_queriesReady) return;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        var em = world.EntityManager;
        _builderStateQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BuilderStateData>());
        _blockQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BlockTag>());
        _ghostQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GhostBlockTag>());
        _queriesReady = true;
    }

    // ── 현재 모드에 맞는 HUD 내용 계산 ────────────────────────────────────────
    private void BuildHud(out string title, out Color accent, out bool hasGauge, out float gauge01, out string gaugeText)
    {
        _hints.Clear();
        title = "건설";
        accent = UIStyleKit.AccentGood;
        hasGauge = false; gauge01 = 0f; gaugeText = "";

        EnsureQueries();

        if (StressVisualizationSystem.IsWindModeActive)
        {
            title = "T5 바람 모드";
            accent = new Color(0.55f, 0.9f, 0.7f, 1f);
            hasGauge = true;
            gauge01 = Mathf.Clamp01(StructuralLoadSolver.WindSpeedMs / 90f);
            gaugeText = " " + StructuralLoadSolver.WindSpeedMs.ToString("0") + "m/s · " + StressVisualizationSystem.WindClockLabel;
            _hints.Add(new Hint("1~9", "풍속 (10m/s 단위)"));
            _hints.Add(new Hint("우클릭", "시계 방향 30°"));
            _hints.Add(new Hint("좌클릭", "반시계 방향 30°"));
            _hints.Add(new Hint("T→5", "종료"));
        }
        else if (StressVisualizationSystem.IsGravityModeActive)
        {
            // M모드: 세기 = 발사 속도 배율
            title = "M · 중력 발사";
            accent = new Color(0.35f, 0.85f, 1f, 1f);
            hasGauge = true;
            gauge01 = Mathf.Clamp01(GravityShootMode.SpeedTier / 9f);
            gaugeText = "속도 x" + GravityShootMode.SpeedTier;
            _hints.Add(new Hint("1~9", "발사 속도 배율"));
            _hints.Add(new Hint("좌클릭", "연사 (누르고 있기)"));
            _hints.Add(new Hint("우·휠클릭", "점사"));
            _hints.Add(new Hint("휠 굴리기", "연사"));
            _hints.Add(new Hint("M", "모드 종료 (복구)"));
        }
        else if (VibrationTestSystem.IsBModeActive)
        {
            // B모드: 세기 = 진도 레벨 (파워는 2^(레벨-1)배)
            int lv = Mathf.Clamp(VibrationTestSystem.VibeLevel, 1, 8);
            title = "B · 진동 테스트";
            accent = UIStyleKit.AccentWarn;
            hasGauge = true;
            gauge01 = lv / 8f;
            gaugeText = "진도 Lv " + lv;
            _hints.Add(new Hint("휠", "진도 조절 (1~8)"));
            _hints.Add(new Hint("B", "격발"));
        }
        else if (ShockwaveTestSystem.IsNModeActive)
        {
            // N모드: 세기 = 폭발 위력 (ShockwaveTestSystem과 같은 공식: 고스트 블록 수 x 5)
            int ghosts = (_queriesReady && !_ghostQuery.IsEmpty) ? _ghostQuery.CalculateEntityCount() : 0;
            float power = ghosts * 5f;
            title = "N · 폭발 테스트";
            accent = new Color(1f, 0.35f, 0.3f, 1f);
            hasGauge = true;
            gauge01 = Mathf.Clamp01(power / Mathf.Max(1f, nPowerGaugeMax));
            gaugeText = "위력 " + power.ToString("0");
            _hints.Add(new Hint("N", "폭발 발사"));
        }
        else
        {
            // 건설 계열
            int currentMode = 1;
            bool hasBuilderState = false;
            float guideHeight = 0f;
            if (_queriesReady && !_builderStateQuery.IsEmpty)
            {
                var bs = _builderStateQuery.GetSingleton<BuilderStateData>();
                currentMode = bs.CurrentMode;
                guideHeight = bs.GuideHeight;
                hasBuilderState = true;
            }
            bool built = _queriesReady && !_blockQuery.IsEmpty;
            bool isAnySpecial = manualBlueprintMode || manualYMode;

            if (isAnySpecial)
            {
                title = manualBlueprintMode ? "블루프린트" : "보강 로딩";
                _hints.Add(new Hint("F", "미리보기"));
                _hints.Add(new Hint("G", "타설 (확정)"));
                if (manualBlueprintMode)
                {
                    _hints.Add(new Hint("1~5", "그리드 크기 10~50"));
                    _hints.Add(new Hint("휠", "높이 조절"));
                }
            }
            else if (hasBuilderState && currentMode == 0)
            {
                title = "선택/보호";
                _hints.Add(new Hint("H", "블록 보호 토글"));
                _hints.Add(new Hint("Del", "블록 즉시 삭제"));
                _hints.Add(new Hint("R", "블록 청소"));
            }
            else
            {
                string name = (currentMode >= 1 && currentMode < BuildModeNames.Length) ? BuildModeNames[currentMode] : "";
                title = string.IsNullOrEmpty(name) ? "건설" : "건설 · " + name;
                if (hasBuilderState)
                {
                    // 건설 모드 세기 = 높이
                    hasGauge = true;
                    gauge01 = Mathf.Clamp01(guideHeight / Mathf.Max(1f, heightGaugeMax));
                    gaugeText = "높이 " + guideHeight.ToString("0") + "층";
                }
                _hints.Add(new Hint("1~5", "건설 모양"));
                _hints.Add(new Hint("휠", "높이(층수) 조절"));
                _hints.Add(new Hint("Enter", "가이드 확정"));
                _hints.Add(new Hint("G", "타설"));
                if (built)
                {
                    _hints.Add(new Hint("V", "응력 스캔"));
                    _hints.Add(new Hint("B", "진동 테스트"));
                    _hints.Add(new Hint("N", "폭발 테스트"));
                    _hints.Add(new Hint("M", "중력 발사 모드"));
                    _hints.Add(new Hint("R", "블록 청소"));
                }
            }
        }

        if (overlayState == 2)
        {
            _hints.Add(new Hint("Q/E", "카메라·설계도 회전", true));
            _hints.Add(new Hint("SPACE", "젯팩", true));
            _hints.Add(new Hint("Y", "예산/보강 UI", true));
            _hints.Add(new Hint("O", "블루프린트 UI", true));
            _hints.Add(new Hint("F1", "도움말", true));
            _hints.Add(new Hint("F5", "설정창", true));
        }
    }

    // ── 그리기 도구 ──────────────────────────────────────────────────────────
    private static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private static void Frame(Rect r, Color c, float t = 1f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // 사이퍼즈 HUD처럼 글자 뒤에 검은 그림자
    private static void ShadowLabel(Rect r, string text, GUIStyle st, Color c)
    {
        Color prevGui = GUI.color;
        GUI.color = Color.white;
        Color prev = st.normal.textColor;
        st.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, st);
        st.normal.textColor = c;
        GUI.Label(r, text, st);
        st.normal.textColor = prev;
        GUI.color = prevGui;
    }

    private void EnsureStyles(float s)
    {
        int scaleKey = Mathf.RoundToInt(s * 100f);
        if (_keyStyle != null && _styleScaleKey == scaleKey) return;
        _styleScaleKey = scaleKey;

        _keyStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
        _descStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        _gaugeTextStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
    }

    void OnGUI()
    {
        if (overlayState == 0) return;

        GUI.skin = UIStyleKit.Skin;
        float s = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.5f);
        EnsureStyles(s);

        BuildHud(out string title, out Color accent, out bool hasGauge, out float gauge01, out string gaugeText);

        float rowH = 34f * s;
        float gaugeW = 14f * s;
        float gaugeH = 220f * s;
        float gap = 12f * s;
        float titleH = 28f * s;
        float leftX = 30f * s;

        float listH = _hints.Count * rowH;
        float bodyH = Mathf.Max(hasGauge ? gaugeH + 26f * s : 0f, listH);
        float totalH = titleH + 6f * s + bodyH;
        float topY = (Screen.height - totalH) * 0.5f;

        // 제목 태그 (Lv. 15 태그 자리)
        float titleW = _titleStyle.CalcSize(new GUIContent(title)).x + 24f * s;
        Rect titleRect = new Rect(leftX, topY, titleW, titleH);
        Fill(titleRect, new Color(0f, 0f, 0f, 0.55f));
        Fill(new Rect(titleRect.x, titleRect.y, 3f * s, titleRect.height), accent);
        ShadowLabel(new Rect(titleRect.x + 12f * s, titleRect.y, titleRect.width - 12f * s, titleRect.height), title, _titleStyle, UIStyleKit.TextColor);

        float bodyY = topY + titleH + 6f * s;
        float listX = leftX;

        // 세로 게이지 (HP 바 자리) — 아래에서 위로 차오름
        if (hasGauge)
        {
            Rect g = new Rect(leftX, bodyY, gaugeW, gaugeH);
            Fill(new Rect(g.x - 2f, g.y - 2f, g.width + 4f, g.height + 4f), new Color(0f, 0f, 0f, 0.6f));
            Fill(g, new Color(1f, 1f, 1f, 0.10f));
            float fillH = g.height * Mathf.Clamp01(gauge01);
            Fill(new Rect(g.x, g.yMax - fillH, g.width, fillH), accent);
            Frame(new Rect(g.x - 2f, g.y - 2f, g.width + 4f, g.height + 4f), new Color(1f, 1f, 1f, 0.35f));

            // 게이지 아래 수치 (HP: 1500 자리)
            Rect gt = new Rect(leftX - 6f * s, g.yMax + 6f * s, 130f * s, 20f * s);
            ShadowLabel(gt, gaugeText, _gaugeTextStyle, accent);

            listX = leftX + gaugeW + gap + 4f;
        }

        // 키 아이콘 + 설명 (E / F / SPACE 자리)
        for (int i = 0; i < _hints.Count; i++)
        {
            var h = _hints[i];
            float y = bodyY + i * rowH;
            float alpha = h.dim ? 0.55f : 1f;

            float keyW = Mathf.Max(34f * s, _keyStyle.CalcSize(new GUIContent(h.key)).x + 16f * s);
            Rect keyRect = new Rect(listX, y + 3f * s, keyW, rowH - 6f * s);
            Fill(keyRect, new Color(0.08f, 0.09f, 0.12f, 0.85f * alpha));
            Frame(keyRect, new Color(0.9f, 0.93f, 1f, 0.75f * alpha));
            ShadowLabel(keyRect, h.key, _keyStyle, new Color(1f, 1f, 1f, alpha));

            Rect descRect = new Rect(keyRect.xMax + 8f * s, y, 260f * s, rowH);
            ShadowLabel(descRect, h.desc, _descStyle, new Color(UIStyleKit.TextColor.r, UIStyleKit.TextColor.g, UIStyleKit.TextColor.b, alpha));
        }

        // 2단계에서만: 수동 토글 (O/L/U, Y 상태를 외부에서 못 읽어서 대체)
        if (overlayState == 2)
        {
            float ty = bodyY + bodyH + 10f * s;
            Fill(new Rect(leftX, ty, 270f * s, 58f * s), new Color(0f, 0f, 0f, 0.45f));
            manualBlueprintMode = GUI.Toggle(new Rect(leftX + 8f * s, ty + 4f * s, 256f * s, 24f * s), manualBlueprintMode, " 블루프린트 모드 (O/L/U) 흉내");
            manualYMode = GUI.Toggle(new Rect(leftX + 8f * s, ty + 30f * s, 256f * s, 24f * s), manualYMode, " Y모드(보강 로딩) 흉내");
        }
    }
}
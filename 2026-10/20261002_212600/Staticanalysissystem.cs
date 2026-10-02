using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

// T → 6 : 정적 구조 분석 모드.
//  - 물리를 돌리지 않고, 지금 쌓은 구조물을 StructuralLoadSolver 로 한 번에 풀어 블록을 사용률 색으로 칠한다.
//      초록(0%) → 노랑(50%) → 빨강(100%) → 어두운 빨강(100% 초과)
//  - 마우스를 블록 위에 올리면 그 블록의 "왜 위험한가"(압축/인장/전단/휨/좌굴 %), 지배 원인, 안전율을 보여준다.
//  - 0.5초마다 다시 계산하므로 블록을 추가/삭제(보강)하면 결과가 바로 바뀐다.
//  - 키: J = 현재 상태를 "이전"으로 저장(보강 전 기록), K = 하중 종류 전환(자중 / 자중+지진 / 자중+바람), X = "이전" 지우기, T→6 = 종료
//    (숫자 키는 건설 모드 선택에 그대로 쓰도록 비워 뒀다 — 분석을 켠 채로 보강 블록을 쌓을 수 있다)
//  - 보강 전·후 비교: 1 로 "이전"을 저장하고 기둥 등을 보강하면, 상단 패널에 최대 사용률/안전율/위험 블록 수의 변화가 표시된다.
public static class StaticAnalysisUI
{
    public static bool Active;
    public static int LoadCase;   // 0 자중, 1 자중 + 지진(F5 설정의 지진 가속도·방향), 2 자중 + 바람(F5 설정의 풍속·방향)
    public static readonly string[] LoadCaseNames = { "자중", "자중 + 지진", "자중 + 바람" };

    public struct Summary
    {
        public bool Valid;
        public int Blocks, Danger, Caution, LoadCase, WorstMode;
        public float MaxUtil, AvgUtil, Safety;
    }
    public static Summary Current, Baseline;

    // 블록별 결과 (StaticAnalysisSystem 이 채운다)
    public static Dictionary<Entity, int> IndexOf = new Dictionary<Entity, int>();
    public static SolveDetail Detail;
    public static string[] Materials;
    public static float[] Util;
    public static int HoverIndex = -1;
    public static Vector2 HoverMouse;

    // 패널 위치 (화면 왼쪽 위 기준, 다른 UI 와 겹치면 여기 숫자를 바꾸면 된다)
    public static float PanelX = 16f, PanelY = 16f;

    static GUIStyle label;
    static int labelSize;

    static string Pct(float v) { return v <= 0f ? "-" : (v * 100f).ToString("0") + "%"; }
    static string Safe(float s) { return s >= 99f ? ">99" : s.ToString("0.00"); }

    static string Fmt(string tag, Summary s)
    {
        return tag + "  최대 사용률 " + (s.MaxUtil * 100f).ToString("0.0") + "% · 안전율 " + Safe(s.Safety)
             + " · 지배 " + SolveDetail.ModeName(s.WorstMode)
             + " · 위험 " + s.Danger + " · 주의 " + s.Caution + " · 블록 " + s.Blocks;
    }

    public static void Draw()
    {
        if (!Active || !Current.Valid) return;
        GUI.skin = UIStyleKit.Skin;
        float s = Mathf.Max(0.75f, Screen.height / 1080f);
        int fs = Mathf.RoundToInt(15f * s);
        if (label == null || labelSize != fs)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.UpperLeft, wordWrap = false, richText = true };
            labelSize = fs;
        }

        var lines = new List<string>();
        lines.Add("<b>T6 정적 구조 분석</b>  ·  하중: " + LoadCaseNames[LoadCase]);
        lines.Add(Fmt("현재", Current));
        if (Baseline.Valid)
        {
            lines.Add(Fmt("이전", Baseline) + "  (" + LoadCaseNames[Baseline.LoadCase] + ")");
            string delta = "변화  최대 사용률 " + ((Current.MaxUtil - Baseline.MaxUtil) * 100f).ToString("+0.0;-0.0;0.0") + "%p"
                         + " · 위험 " + (Current.Danger - Baseline.Danger).ToString("+0;-0;0")
                         + " · 주의 " + (Current.Caution - Baseline.Caution).ToString("+0;-0;0")
                         + " · 블록 " + (Current.Blocks - Baseline.Blocks).ToString("+0;-0;0");
            if (Baseline.LoadCase != Current.LoadCase) delta += "  (하중 종류가 달라 단순 비교 불가)";
            lines.Add(delta);
        }
        lines.Add("J 현재를 '이전'으로 저장 · K 하중 종류 전환 · X '이전' 지우기 · T→6 종료");

        DrawBox(lines, PanelX * s, PanelY * s, s);

        if (HoverIndex >= 0 && Detail != null && Util != null && HoverIndex < Util.Length)
        {
            int i = HoverIndex;
            var h = new List<string>();
            h.Add("<b>" + (Materials != null ? Materials[i] : "블록") + "</b>  사용률 " + (Util[i] * 100f).ToString("0") + "%");
            h.Add("지배 원인: " + SolveDetail.ModeName(Detail.Mode[i]) + " · 안전율 " + Safe(Detail.Safety[i]));
            h.Add("압축 " + Pct(Detail.Comp[i]) + " / 인장 " + Pct(Detail.Tens[i]) + " / 전단 " + Pct(Detail.Shear[i])
                + " / 휨 " + Pct(Detail.Bend[i]) + " / 좌굴 " + Pct(Detail.Buckling[i]));
            DrawBox(h, HoverMouse.x + 18f * s, (Screen.height - HoverMouse.y) + 18f * s, s);
        }
    }

    static void DrawBox(List<string> lines, float x, float y, float s)
    {
        float w = 0f;
        for (int i = 0; i < lines.Count; i++) w = Mathf.Max(w, label.CalcSize(new GUIContent(lines[i])).x);
        float lh = label.fontSize * 1.45f;
        float pad = 8f * s;
        float bw = w + pad * 2f, bh = lines.Count * lh + pad * 2f;
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - bw));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - bh));

        Color old = GUI.color;
        GUI.color = UIStyleKit.PanelBg;
        GUI.DrawTexture(new Rect(x, y, bw, bh), Texture2D.whiteTexture);
        GUI.color = old;
        for (int i = 0; i < lines.Count; i++)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = UIStyleKit.TextColor;
            GUI.Label(new Rect(x + pad, y + pad + i * lh, w + 4f, lh), lines[i], label);
            GUI.contentColor = prev;
        }
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct StaticAnalysisSystem : ISystem
{
    private double nextEval;
    private const double Interval = 0.5;

    public void OnCreate(ref SystemState state)
    {
        StaticAnalysisUI.Active = false;
        nextEval = 0;
    }

    public void OnUpdate(ref SystemState state)
    {
        // 다른 시험(T→1~5)이 시작되면 이 모드는 조용히 끈다.
        bool other = TestMode.Selected(1) || TestMode.Selected(2) || TestMode.Selected(3) || TestMode.Selected(4) || TestMode.Selected(5);
        if (StaticAnalysisUI.Active && other)
        {
            Deactivate(ref state, false);
            return;
        }

        if (TestMode.Selected(6))
        {
            if (StaticAnalysisUI.Active) { Deactivate(ref state, true); return; }

            if (VibrationTestSystem.IsBModeActive || ShockwaveTestSystem.IsNModeActive
                || StressVisualizationSystem.IsGravityModeActive || StressVisualizationSystem.IsWindModeActive)
            {
                UnityEngine.Debug.LogWarning("[정적 분석] 다른 시험 모드가 켜져 있습니다. 먼저 끄세요.");
                return;
            }
            if (!JointStressCalc.UseForceBased)
            {
                UnityEngine.Debug.LogWarning("[정적 분석] 힘 기반 판정 모드가 꺼져 있어 켤 수 없습니다.");
                return;
            }
            StaticAnalysisUI.Active = true;
            StaticAnalysisUI.LoadCase = 0;
            StaticAnalysisUI.Baseline = default(StaticAnalysisUI.Summary);
            nextEval = 0;
            UnityEngine.Debug.Log("[정적 분석] 켜짐 — 블록 위에 마우스를 올리면 원인과 안전율이 보입니다. J 이전 저장 / K 하중 종류 / X 이전 지우기 / T→6 종료");
        }

        if (!StaticAnalysisUI.Active) return;

        bool force = false;
        if (!TestMode.ChordBusy())
        {
            if (Input.GetKeyDown(KeyCode.J))
            {
                if (StaticAnalysisUI.Current.Valid)
                {
                    StaticAnalysisUI.Baseline = StaticAnalysisUI.Current;
                    UnityEngine.Debug.Log("[정적 분석] 현재 상태를 '이전'으로 저장했습니다. 구조물을 보강하면 변화가 표시됩니다.");
                }
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                StaticAnalysisUI.LoadCase = (StaticAnalysisUI.LoadCase + 1) % 3;
                force = true;
                UnityEngine.Debug.Log("[정적 분석] 하중: " + StaticAnalysisUI.LoadCaseNames[StaticAnalysisUI.LoadCase]);
            }
            if (Input.GetKeyDown(KeyCode.X))
            {
                StaticAnalysisUI.Baseline = default(StaticAnalysisUI.Summary);
            }
        }

        double now = SystemAPI.Time.ElapsedTime;
        if (force || now >= nextEval)
        {
            nextEval = now + Interval;
            Evaluate(ref state);
        }
        UpdateHover(ref state);
    }

    private void Deactivate(ref SystemState state, bool log)
    {
        StaticAnalysisUI.Active = false;
        StaticAnalysisUI.HoverIndex = -1;
        StaticAnalysisUI.Current = default(StaticAnalysisUI.Summary);
        StaticAnalysisUI.IndexOf.Clear();
        StaticAnalysisUI.Detail = null;
        StaticAnalysisUI.Util = null;
        StaticAnalysisUI.Materials = null;
        foreach (var color in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>>().WithAll<BlockTag>())
            color.ValueRW.Value = new float4(1, 1, 1, 1);
        if (log) UnityEngine.Debug.Log("[정적 분석] 꺼짐");
    }

    private void Evaluate(ref SystemState state)
    {
        var ents = new List<Entity>();
        var mats = new List<string>();
        var grids = new List<int3>();
        var weights = new List<float>();
        var grounds = new List<bool>();
        var fcs = new List<float>();
        var fts = new List<float>();
        var eGPa = new List<float>();
        var fvs = new List<float>();
        var idxOf = new Dictionary<Entity, int>();

        foreach (var (pos, mat, entity) in SystemAPI.Query<RefRO<OriginalPosition>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
        {
            if (!JointStressCalc.CompressionForceReady(mat.ValueRO) || mat.ValueRO.YoungModulusGPa <= 0f) continue;
            float3 op = pos.ValueRO.Value;
            idxOf[entity] = ents.Count;
            ents.Add(entity);
            mats.Add(mat.ValueRO.MaterialName.ToString());
            grids.Add(new int3((int)math.floor(op.x / 3.0f + 0.5f), (int)math.floor(op.y / 3.0f + 0.5f), (int)math.floor(op.z / 3.0f + 0.5f)));
            weights.Add(StructuralLoadSolver.WeightN(mat.ValueRO.DensityTPerM3));
            grounds.Add(op.y <= 3.1f);
            fcs.Add(mat.ValueRO.CompressiveMPa);
            fts.Add(mat.ValueRO.TensileMPa);
            eGPa.Add(mat.ValueRO.YoungModulusGPa);
            fvs.Add(mat.ValueRO.ShearMPa);
        }

        int n = ents.Count;
        if (n == 0)
        {
            StaticAnalysisUI.Current = default(StaticAnalysisUI.Summary);
            StaticAnalysisUI.IndexOf.Clear();
            return;
        }

        var links = new HashSet<long>();
        foreach (var pair in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>())
        {
            if (idxOf.TryGetValue(pair.ValueRO.EntityA, out int ia) && idxOf.TryGetValue(pair.ValueRO.EntityB, out int ib))
                links.Add(StructuralLoadSolver.LinkKey(ia, ib));
        }

        var gridArr = grids.ToArray();
        int loadCase = StaticAnalysisUI.LoadCase;
        float[] fx = null, fz = null;
        if (loadCase == 2)
        {
            fx = new float[n]; fz = new float[n];
            StructuralLoadSolver.ComputeWind(gridArr, StructuralLoadSolver.WindSpeedMs, StructuralLoadSolver.WindDirDeg, fx, fz, out int exposed);
        }
        // gOverride: -1 이면 F5 설정의 지진 가속도 사용, 0 이면 지진 없음(자중, 자중+바람)
        float g = loadCase == 1 ? -1f : 0f;

        var axial = new float[n];
        var overhang = new bool[n];
        var util = new float[n];
        var detail = new SolveDetail(n);
        StructuralLoadSolver.Solve(gridArr, weights.ToArray(), grounds.ToArray(), links, fcs.ToArray(), fts.ToArray(),
                                   axial, overhang, util, g, null, detail, eGPa.ToArray(), fvs.ToArray(), fx, fz);

        var colorLookup = SystemAPI.GetComponentLookup<URPMaterialPropertyBaseColor>(false);
        int danger = 0, caution = 0, worst = 0;
        float maxUtil = 0f, sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = util[i];
            sum += u;
            if (u > maxUtil) { maxUtil = u; worst = i; }
            if (u >= 1f) danger++; else if (u >= 0.7f) caution++;
            if (colorLookup.HasComponent(ents[i])) colorLookup[ents[i]] = new URPMaterialPropertyBaseColor { Value = UtilColor(u) };
        }

        StaticAnalysisUI.IndexOf = idxOf;
        StaticAnalysisUI.Detail = detail;
        StaticAnalysisUI.Util = util;
        StaticAnalysisUI.Materials = mats.ToArray();
        StaticAnalysisUI.Current = new StaticAnalysisUI.Summary
        {
            Valid = true, Blocks = n, Danger = danger, Caution = caution, LoadCase = loadCase,
            WorstMode = detail.Mode[worst], MaxUtil = maxUtil, AvgUtil = sum / n,
            Safety = maxUtil > 1e-6f ? 1f / maxUtil : 999f
        };
    }

    private void UpdateHover(ref SystemState state)
    {
        StaticAnalysisUI.HoverIndex = -1;
        if (Camera.main == null) return;
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;

        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var input = new RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * 500f,
            Filter = CollisionFilter.Default
        };
        if (physics.PhysicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
        {
            if (StaticAnalysisUI.IndexOf.TryGetValue(hit.Entity, out int idx))
            {
                StaticAnalysisUI.HoverIndex = idx;
                StaticAnalysisUI.HoverMouse = Input.mousePosition;
            }
        }
    }

    // 사용률 → 색. 0 초록, 0.5 노랑, 1 빨강, 1 초과 어두운 빨강
    private static float4 UtilColor(float u)
    {
        if (u > 1f) return new float4(0.5f, 0f, 0f, 1f);
        float t = math.saturate(u);
        return new float4(math.saturate(2f * t), math.saturate(2f * (1f - t)), 0f, 1f);
    }
}
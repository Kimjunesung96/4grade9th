#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// StructuralLoadSolver 검증용 테스트 (메뉴: Tools/구조 솔버 검증).
// 단순한 기둥/외팔보를 "손계산 공식"으로 따로 계산해서 솔버 결과와 비교한다.
// 발표에서 "숫자를 아무렇게나 만든 게 아니다"를 보이는 근거로 쓸 수 있다.
//
// 손계산에 쓰는 공식 (솔버 코드를 복사하지 않고 교과서 식으로 다시 씀):
//   자중 W = ρ·b³·g,  축응력 σn = N/A,  휨응력 σb = M/Z,  전단 τ = 1.5·V/A,  Pcr = π²EI/(KH)²
public static class StructuralSolverValidation
{
    const float B = 0.15f;
    const float A = B * B;
    const float Z = B * B * B / 6f;
    const float I = B * B * B * B / 12f;
    const float G = 9.81f;

    static int pass, fail;

    // 재료 (Material_Specs.csv 의 E / 인장 / 압축, MPa)
    const float ConcreteRho = 2.4f, ConcreteE = 32.8f, ConcreteFt = 2.9f, ConcreteFc = 38f;
    const float SteelRho = 7.8f, SteelE = 210f, SteelFt = 430f, SteelFc = 275f;

    [MenuItem("Tools/구조 솔버 검증")]
    public static void RunAll()
    {
        pass = 0; fail = 0;

        // 다른 테스트에 영향이 없도록 정적 설정을 저장했다가 복원한다.
        float oldDir = StructuralLoadSolver.EarthquakeDirDeg;
        float oldG = StructuralLoadSolver.EarthquakeG;
        bool oldShear = StructuralLoadSolver.IncludeShear, oldBuck = StructuralLoadSolver.IncludeBuckling;
        float oldK = StructuralLoadSolver.BucklingK;
        StructuralLoadSolver.EarthquakeDirDeg = 0f;
        StructuralLoadSolver.EarthquakeG = 0f;
        StructuralLoadSolver.IncludeShear = true;
        StructuralLoadSolver.IncludeBuckling = true;
        StructuralLoadSolver.BucklingK = 2f;

        try
        {
            TestColumnSelfWeight();
            TestColumnEarthquake();
            TestCantileverBlock();
            TestSlenderSteelColumnBuckling();
        }
        finally
        {
            StructuralLoadSolver.EarthquakeDirDeg = oldDir;
            StructuralLoadSolver.EarthquakeG = oldG;
            StructuralLoadSolver.IncludeShear = oldShear;
            StructuralLoadSolver.IncludeBuckling = oldBuck;
            StructuralLoadSolver.BucklingK = oldK;
        }

        string msg = "[구조 솔버 검증] 통과 " + pass + " / 실패 " + fail;
        if (fail == 0) Debug.Log("✅ " + msg); else Debug.LogError("❌ " + msg);
    }

    // ---------- 손계산 ----------
    static float Weight(float rhoTPerM3) { return rhoTPerM3 * 1000f * B * B * B * G; }

    // 한 단면의 사용률 = max(압축, 인장, 전단). N: 압축 +, M: 합 모멘트(N·m), V: 전단력(N)
    static float Expected(float n, float m, float v, float fc, float ft)
    {
        float sn = n / A / 1e6f;
        float sb = m / Z / 1e6f;
        float comp = (sn + sb) / fc;
        float tens = (sb - sn) > 0f ? (sb - sn) / ft : 0f;
        float fv = 0.6f * Mathf.Min(fc, ft);            // 솔버의 전단강도 임시 추정식과 같은 가정
        float shear = 1.5f * v / A / 1e6f / fv;
        return Mathf.Max(comp, Mathf.Max(tens, shear));
    }

    static void Check(string name, float expected, float actual, float relTol = 1e-3f)
    {
        float denom = Mathf.Max(Mathf.Abs(expected), 1e-9f);
        bool ok = Mathf.Abs(expected - actual) / denom <= relTol;
        if (ok) pass++; else fail++;
        string line = (ok ? "  ✔ " : "  ✘ ") + name + "  손계산=" + expected.ToString("G5") + "  솔버=" + actual.ToString("G5");
        if (ok) Debug.Log(line); else Debug.LogError(line);
    }

    // ---------- 모델 만들기 ----------
    class Model
    {
        public int3[] grid; public float[] w, fc, ft, e; public bool[] ground; public HashSet<long> links = new HashSet<long>();
    }

    // 세로 기둥: y = 0 .. n-1, y=0 이 바닥
    static Model Column(int n, float rho, float eGPa, float ftMPa, float fcMPa)
    {
        var m = new Model { grid = new int3[n], w = new float[n], fc = new float[n], ft = new float[n], e = new float[n], ground = new bool[n] };
        for (int i = 0; i < n; i++)
        {
            m.grid[i] = new int3(0, i, 0);
            m.w[i] = StructuralLoadSolver.WeightN(rho);
            m.fc[i] = fcMPa; m.ft[i] = ftMPa; m.e[i] = eGPa;
            m.ground[i] = i == 0;
            if (i > 0) m.links.Add(StructuralLoadSolver.LinkKey(i, i - 1));
        }
        return m;
    }

    static float[] Run(Model m, out SolveDetail d, float gOverride = -1f)
    {
        int n = m.grid.Length;
        var axial = new float[n]; var over = new bool[n]; var util = new float[n];
        d = new SolveDetail(n);
        StructuralLoadSolver.Solve(m.grid, m.w, m.ground, m.links, m.fc, m.ft, axial, over, util, gOverride, null, d, m.e, null);
        return util;
    }

    // ---------- 테스트 ----------
    // 1) 콘크리트 기둥 5블록, 자중만: 바닥 단면 N = 5W, 모멘트 0
    static void TestColumnSelfWeight()
    {
        Debug.Log("[1] 콘크리트 기둥 5블록 (자중)");
        var m = Column(5, ConcreteRho, ConcreteE, ConcreteFt, ConcreteFc);
        var u = Run(m, out var d);
        float w = Weight(ConcreteRho);
        Check("바닥 압축 사용률", Expected(5f * w, 0f, 0f, ConcreteFc, ConcreteFt), u[0]);
        Check("꼭대기 압축 사용률", Expected(1f * w, 0f, 0f, ConcreteFc, ConcreteFt), u[4]);
        Check("안전율 = 1/사용률", 1f / Expected(5f * w, 0f, 0f, ConcreteFc, ConcreteFt), d.Safety[0]);
    }

    // 2) 같은 기둥에 수평 지진 0.35g: 바닥 전단 V = Σ m·a, 바닥 모멘트 M = Σ m·a·(k+0.5)·b
    static void TestColumnEarthquake()
    {
        Debug.Log("[2] 콘크리트 기둥 5블록 + 수평 0.35g (외팔보 모멘트)");
        const int n = 5; const float gq = 0.35f;
        var m = Column(n, ConcreteRho, ConcreteE, ConcreteFt, ConcreteFc);
        var u = Run(m, out var d, gq);
        float w = Weight(ConcreteRho);
        float ma = w / G * gq * G;                 // 블록 하나의 관성력
        float v = n * ma;
        float mom = 0f;
        for (int k = 0; k < n; k++) mom += ma * (k + 0.5f) * B;
        Check("바닥 전단력이 만드는 사용률 포함 전체", Expected(n * w, mom, v, ConcreteFc, ConcreteFt), u[0]);
        Check("지배 모드 = 인장 (콘크리트 인장 약함)", SolveDetail.ModeTension, d.Mode[0], 1e-6f);
    }

    // 3) 옆으로 내민 블록 1개: 조인트 면에서 M = 0.5·b·W, 전단 V = W. 받는 블록은 N = 2W, M = b·W
    static void TestCantileverBlock()
    {
        Debug.Log("[3] 옆으로 내민 블록 (외팔보 조인트)");
        var m = new Model
        {
            grid = new[] { new int3(0, 0, 0), new int3(1, 0, 0) },
            w = new float[2], fc = new float[2], ft = new float[2], e = new float[2], ground = new[] { true, false }
        };
        for (int i = 0; i < 2; i++) { m.w[i] = Weight(ConcreteRho); m.fc[i] = ConcreteFc; m.ft[i] = ConcreteFt; m.e[i] = ConcreteE; }
        m.links.Add(StructuralLoadSolver.LinkKey(0, 1));
        var u = Run(m, out var d);
        float w = Weight(ConcreteRho);
        Check("내민 블록 조인트 면", Expected(0f, 0.5f * B * w, w, ConcreteFc, ConcreteFt), u[1]);
        Check("받는(바닥) 블록 단면", Expected(2f * w, B * w, 0f, ConcreteFc, ConcreteFt), u[0]);
    }

    // 4) 얇고 긴 강철 기둥 200블록(30 m): 압축응력은 항복의 1% 인데 오일러 좌굴이 먼저 온다
    static void TestSlenderSteelColumnBuckling()
    {
        Debug.Log("[4] 강철 기둥 200블록 (오일러 좌굴)");
        const int n = 200;
        var m = Column(n, SteelRho, SteelE, SteelFt, SteelFc);
        var u = Run(m, out var d);
        float w = Weight(SteelRho);
        float nBase = n * w;
        double h = n * B, le = 2.0 * h;
        float pcr = (float)(System.Math.PI * System.Math.PI * SteelE * 1e9 * I / (le * le));
        float expectedBuckling = nBase / pcr;
        Check("좌굴 사용률 N/Pcr", expectedBuckling, d.Buckling[0]);
        Check("좌굴이 압축보다 지배적 → util = 좌굴", expectedBuckling, u[0]);
        Check("지배 모드 = 좌굴", SolveDetail.ModeBuckling, d.Mode[0], 1e-6f);
    }
}
#endif
using System.Collections.Generic;
using Unity.Mathematics;

// [현실화 v2] 간이 구조 하중 계산기 (자중 + 지진 관성력, 축력 + 휨).
//
// 각 블록 아랫면(= 아래 블록과 맞닿은 단면)에 걸리는 값을 위층부터 누적한다.
//   N      축력(N)        = 자중 + 위에서 내려온 하중
//   V      수평 전단력(N) = 지진 관성력 F = m·a 의 누적
//   M      휨모멘트(N·m)  = 수평력 × 높이 팔 + 내민 블록의 자중 × 수평 팔
//   단면   b = 0.15m 정사각,  A = b²,  Z = b³/6
//   σ_휨   = (|Mx| + |Mz|) / Z           (두 방향 휨이 겹치는 모서리 기준)
//   압축측 = N/A + σ_휨   →  압축강도와 비교
//   인장측 = σ_휨 − N/A   →  (0보다 클 때만) 인장강도와 비교. 자중 압축이 휨 인장을 상쇄한다.
//
// 하중 경로 규칙 (간이 모델):
//   1) 위층부터 아래로. 블록은 자기 바로 아래에 조인트로 이어진 블록이 있으면 그쪽으로 모든 값을 넘긴다.
//   2) 바로 아래가 비어 있는(내민) 블록은 같은 층에서 지지되는 블록 쪽으로 옆으로 넘긴다(여러 개면 균등 분배).
//      이때 수직 하중의 수평 팔 때문에 모멘트가 생기고, 옆으로 이어진 조인트 면에서 인장/압축을 검사한다.
//   3) 어디에도 이어지지 않은 블록은 전달하지 않는다.
//   4) 바닥 블록은 지면이 받는다. 지면과 맞닿은 단면이 대개 가장 위험하다.
// 가정: 균등 분배, 정적 등가(지진 동적 증폭 없음).
//
// [v3 추가] 전단 / 좌굴 / 성분별 사용률 / 안전율
//   전단   τ = 1.5·V/A (직사각형 단면 최대 전단응력) → 전단강도 fv 와 비교.
//          바닥 단면은 수평 전단력 V, 내민 블록의 조인트 면은 넘겨주는 수직력(pN)이 전단력이다.
//          fvMPa 를 넘기지 않으면 임시 추정식 fv = 0.6·min(fc, ft) 를 쓴다(재료 전단강도 데이터가 생기면 교체).
//   좌굴   오일러 임계하중 Pcr = π²·E·I / (K·H)²,  I = b⁴/12,  H = 같은 세로 줄(조인트로 이어진 블록)의 전체 높이.
//          가로로 이어진 이웃이 있는 블록(벽)은 폭이 넓어 이 식이 과소평가되므로 검사하지 않는다(독립 기둥만).
//          사용률 = N / Pcr.  eGPa 를 넘기지 않으면 좌굴은 계산하지 않는다.
//   안전율 = 1 / max(모든 사용률).  detail 을 넘기면 블록별 성분(압축/인장/전단/휨/좌굴)과 지배 원인을 돌려준다.
public class SolveDetail
{
    public const int ModeNone = 0, ModeCompression = 1, ModeTension = 2, ModeShear = 3, ModeBuckling = 4;

    public float[] Comp, Tens, Shear, Bend, Buckling, Safety;
    public int[] Mode;   // 지배 파괴 모드 (ModeXxx)

    public SolveDetail(int n)
    {
        Comp = new float[n]; Tens = new float[n]; Shear = new float[n];
        Bend = new float[n]; Buckling = new float[n]; Safety = new float[n]; Mode = new int[n];
        for (int i = 0; i < n; i++) Safety[i] = 999f;
    }

    // 한 단면 검사 결과를 반영한다. 블록마다 여러 단면(조인트 면, 바닥 단면)을 검사하므로 성분별 최대를 남긴다.
    public void Add(int i, float comp, float tens, float shear, float bend)
    {
        if (comp  > Comp[i])  Comp[i]  = comp;
        if (tens  > Tens[i])  Tens[i]  = tens;
        if (shear > Shear[i]) Shear[i] = shear;
        if (bend  > Bend[i])  Bend[i]  = bend;
    }

    public static string ModeName(int mode)
    {
        switch (mode)
        {
            case ModeCompression: return "압축";
            case ModeTension:     return "인장";
            case ModeShear:       return "전단";
            case ModeBuckling:    return "좌굴";
            default:              return "-";
        }
    }
}

public static class StructuralLoadSolver
{
    public const float BlockSizeM = 0.15f;
    public const float AreaM2     = BlockSizeM * BlockSizeM;
    public const float VolumeM3   = BlockSizeM * BlockSizeM * BlockSizeM;
    public const float SectionZ   = BlockSizeM * BlockSizeM * BlockSizeM / 6f;   // 단면계수 b³/6
    public const float Gravity    = 9.81f;

    // 지진: 정적 등가 수평 가속도. F5 창에서 조절한다. 0이면 자중만.
    public static float EarthquakeG      = 0f;     // 지반 가속도 (g 단위)
    public static float EarthquakeDirDeg = 0f;     // 방향 (0 = +X, 90 = +Z)

    // 진동 테스트(B) 동적 하중: 정현파 지반 가속도 a(t) = PGA·sin(2πft) 를 1자유도(SDOF) 응답으로 증폭해 관성력에 쓴다.
    // 전단·좌굴 판정을 util(사용률)에 포함할지. false 로 두면 v2 와 같은 결과(압축/인장만)를 낸다.
    public static bool IncludeShear    = true;
    public static bool IncludeBuckling = true;
    // 좌굴 유효길이 계수 K (1.0 양단 핀, 2.0 = 밑 고정·위 자유 외팔). 자유롭게 서 있는 블록 탑은 2.0 이 보수적이다.
    public static float BucklingK = 2.0f;

    public const float SectionI = BlockSizeM * BlockSizeM * BlockSizeM * BlockSizeM / 12f;   // 단면 2차모멘트 b⁴/12

    public static float QuakeFreqHz  = 2f;      // 지반 진동수 (Hz)
    public static float QuakeDamping = 0.05f;   // 감쇠비 ζ (구조물 전체, 5% 가정)

    // 진도 레벨(1~8) -> 최대 지반 가속도(g). 게임용 단계라 실제 진도 척도와 1:1은 아니다(대략 진도 5~9 구간).
    static readonly float[] LevelPgaG = { 0.02f, 0.05f, 0.1f, 0.2f, 0.35f, 0.5f, 0.8f, 1.2f };

    // F5 창 표시용 진동 테스트 마지막 결과
    public static float LastPgaG;             // 진도 레벨에서 나온 최대 지반 가속도(g)
    public static float LastQuakeFnHz;        // 구조물 1차 고유진동수
    public static float LastQuakeAmplify;     // 절대가속도 전달률 T(r)
    public static float LastQuakeEffectiveG;  // 실제로 관성력에 넣은 가속도 (= PGA × T)

    // F5 창 표시용 마지막 계산 결과
    public static float LastMaxAxialN;
    public static float LastMaxUtilization;
    public static float LastBaseShearN;
    public static float LastMaxMomentNm;
    public static int   LastBlockCount;
    public static int   LastOverhangCount;

    // ---------- 바람 (정적 풍하중) ----------
    // 풍압 q = 0.5·ρ_공기·v²,  바람을 맞는 면 하나가 받는 힘 F = q·Cd·b²·cos²θ  (θ = 바람 방향과 면 법선의 각).
    // 블록의 "바람 쪽 면"이 노출되어 있는지는 같은 높이에서 바람이 오는 쪽으로 다른 블록이 하나라도 있는지로 판정한다
    // (앞 건물에 가려진 블록은 힘을 받지 않는다). 윗면·옆면 항력, 높이에 따른 풍속 증가, 난류는 무시한다.
    // 힘은 블록 중심에 걸린다(블록 중간 높이에 균등 압력 → 지진 관성력과 같은 모멘트 팔).
    public static float WindSpeedMs = 25f;   // 풍속 (m/s)
    public static float WindDirDeg  = 0f;    // 바람이 불어가는 방향 (0 = +X, 90 = +Z)
    public const float AirDensity = 1.225f;  // kg/m3
    public const float WindCd     = 1.2f;    // 항력계수 (평판 정면)

    public static int   LastWindExposedFaces;
    public static int   LastWindFailedBlocks;
    public static float LastWindMaxUtil;

    public static void ComputeWind(int3[] grid, float speedMs, float dirDeg, float[] fx, float[] fz, out int exposedFaces)
    {
        exposedFaces = 0;
        int n = grid.Length;
        for (int i = 0; i < n; i++) { fx[i] = 0f; fz[i] = 0f; }
        if (n == 0 || speedMs <= 0f) return;

        var set = new HashSet<int3>();
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            set.Add(grid[i]);
            if (grid[i].x < minX) minX = grid[i].x; if (grid[i].x > maxX) maxX = grid[i].x;
            if (grid[i].z < minZ) minZ = grid[i].z; if (grid[i].z > maxZ) maxZ = grid[i].z;
        }

        float rad = dirDeg * 0.0174532925f;
        float cx = (float)System.Math.Cos(rad), cz = (float)System.Math.Sin(rad);
        float q = 0.5f * AirDensity * speedMs * speedMs;
        float faceF = q * WindCd * BlockSizeM * BlockSizeM;
        int sx = cx >= 0f ? 1 : -1, sz = cz >= 0f ? 1 : -1;

        for (int i = 0; i < n; i++)
        {
            if (System.Math.Abs(cx) > 1e-4f)
            {
                bool shielded = false;
                for (int k = 1; k <= maxX - minX && !shielded; k++)
                    if (set.Contains(grid[i] + new int3(-sx * k, 0, 0))) shielded = true;
                if (!shielded) { fx[i] += sx * faceF * cx * cx; exposedFaces++; }
            }
            if (System.Math.Abs(cz) > 1e-4f)
            {
                bool shielded = false;
                for (int k = 1; k <= maxZ - minZ && !shielded; k++)
                    if (set.Contains(grid[i] + new int3(0, 0, -sz * k))) shielded = true;
                if (!shielded) { fz[i] += sz * faceF * cz * cz; exposedFaces++; }
            }
        }
    }

    public static float WeightN(float densityTPerM3)
    {
        return densityTPerM3 * 1000f * VolumeM3 * Gravity;   // t/m3 -> kg/m3 -> kg -> N
    }

    public static float PgaForLevel(int level)
    {
        int i = level < 1 ? 0 : (level > LevelPgaG.Length ? LevelPgaG.Length - 1 : level - 1);
        return LevelPgaG[i];
    }

    // 구조물 1차 고유진동수(Hz). 블록 한 줄 기둥(단면 b×b)을 외팔보로 본 오일러-베르누이 식:
    //   fn = (1.8751² / 2π) · sqrt(E·I / (ρ·A·H⁴)),  I/A = b²/12
    //      = 0.5596 · (b/√12) · sqrt(E/ρ) / H²
    // H = 기초 아랫면에서 가장 높은 블록 윗면까지. E, ρ 는 모든 블록의 평균.
    // 폭이 넓은 벽은 실제로 더 뻣뻣해서 고유진동수가 더 높다(여기 값은 낮게 잡힌다).
    public static float NaturalFrequencyHz(int3[] grid, float[] eGPa, float[] densityTPerM3, bool[] isGround)
    {
        int n = grid.Length;
        if (n == 0) return 0f;
        int maxY = int.MinValue, minGroundY = int.MaxValue, minAllY = int.MaxValue;
        double eSum = 0.0, rhoSum = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (grid[i].y > maxY) maxY = grid[i].y;
            if (grid[i].y < minAllY) minAllY = grid[i].y;
            if (isGround[i] && grid[i].y < minGroundY) minGroundY = grid[i].y;
            eSum += eGPa[i];
            rhoSum += densityTPerM3[i];
        }
        int baseY = minGroundY != int.MaxValue ? minGroundY : minAllY;
        float h = (maxY - baseY + 1) * BlockSizeM;
        double e = eSum / n * 1.0e9;         // GPa -> Pa
        double rho = rhoSum / n * 1000.0;    // t/m3 -> kg/m3
        if (e <= 0.0 || rho <= 0.0 || h <= 0f) return 0f;
        double radius = BlockSizeM / System.Math.Sqrt(12.0);   // 회전반경 sqrt(I/A)
        return (float)(0.5596 * radius * System.Math.Sqrt(e / rho) / ((double)h * h));
    }

    // 지반 가속도 대비 구조물 절대가속도 전달률 (감쇠 SDOF 기초가진). r = 가진진동수 / 고유진동수.
    //   T = sqrt(1 + (2ζr)²) / sqrt((1 - r²)² + (2ζr)²)
    // r → 0 이면 1(정적 등가와 같음), r = 1 근처에서 공진해 크게 증폭, r > √2 이면 1보다 작아진다.
    public static float Transmissibility(float r, float zeta)
    {
        float a = 2f * zeta * r;
        float den = (1f - r * r) * (1f - r * r) + a * a;
        if (den < 1.0e-9f) den = 1.0e-9f;
        return (float)System.Math.Sqrt((1f + a * a) / den);
    }

    public static long LinkKey(int a, int b)
    {
        int lo = a < b ? a : b;
        int hi = a < b ? b : a;
        return ((long)lo << 32) | (uint)hi;
    }

    // 한 단면의 사용률. n: 압축이 +. compRatio/tensRatio 는 각각 압축강도/인장강도 대비.
    static void Section(float n, float mx, float mz, float fcMPa, float ftMPa, out float compRatio, out float tensRatio)
    {
        float sb = (System.Math.Abs(mx) + System.Math.Abs(mz)) / SectionZ / 1.0e6f;   // MPa
        float sn = n / AreaM2 / 1.0e6f;                                                // MPa
        float sc = sn + sb;
        float st = sb - sn;
        compRatio = fcMPa > 0f ? sc / fcMPa : 0f;
        tensRatio = (st > 0f && ftMPa > 0f) ? st / ftMPa : 0f;
    }

    // 전단강도 임시 추정 (fvMPa 를 못 받았을 때). 재료 데이터가 생기면 fvMPa 로 대체한다.
    static float ShearStrengthFallback(float fcMPa, float ftMPa)
    {
        float m = fcMPa;
        if (ftMPa > 0f && (m <= 0f || ftMPa < m)) m = ftMPa;
        return 0.6f * m;
    }

    // 성분별 사용률. v: 그 단면의 전단력(N). bend: 휨 응력 / min(압축강도, 인장강도) — 휨 단독 기여도.
    static void Section2(float n, float mx, float mz, float v, float fcMPa, float ftMPa, float fvMPa,
                         out float compRatio, out float tensRatio, out float shearRatio, out float bendRatio)
    {
        Section(n, mx, mz, fcMPa, ftMPa, out compRatio, out tensRatio);
        float sb = (System.Math.Abs(mx) + System.Math.Abs(mz)) / SectionZ / 1.0e6f;   // MPa
        float weak = fcMPa;
        if (ftMPa > 0f && (weak <= 0f || ftMPa < weak)) weak = ftMPa;
        bendRatio = weak > 0f ? sb / weak : 0f;
        float tau = 1.5f * System.Math.Abs(v) / AreaM2 / 1.0e6f;                       // MPa
        shearRatio = fvMPa > 0f ? tau / fvMPa : 0f;
    }

    // grid/weightN/isGround/links: 블록 정보. fcMPa/ftMPa: 재료 강도.
    // 결과: util[i] = 그 블록에서 가장 위험한 단면의 사용률(압축측/인장측 중 큰 값, 1 이상이면 파괴),
    //       axialN[i] = 아래로 전달하는 축력, overhang[i] = 옆으로 넘기거나 지지가 없는 블록
    public static void Solve(int3[] grid, float[] weightN, bool[] isGround, HashSet<long> links,
                             float[] fcMPa, float[] ftMPa,
                             float[] axialN, bool[] overhang, float[] util,
                             float gOverride = -1f, int[] distOut = null,
                             SolveDetail detail = null, float[] eGPa = null, float[] fvMPa = null,
                             float[] windFx = null, float[] windFz = null)
    {
        int n = grid.Length;
        var index = new Dictionary<int3, int>(n);
        for (int i = 0; i < n; i++) index[grid[i]] = i;

        // 지진 관성력 방향
        float rad = EarthquakeDirDeg * 0.0174532925f;
        // gOverride >= 0 이면 F5 정적 지진 대신 그 값을 쓴다(진동 테스트의 동적 유효 가속도).
        float quakeG = gOverride >= 0f ? gOverride : EarthquakeG;
        float accX = quakeG * Gravity * (float)System.Math.Cos(rad);
        float accZ = quakeG * Gravity * (float)System.Math.Sin(rad);

        // top: 위 블록에서 내려온 값(이 블록 윗면 기준). bot: 이 블록 아랫면 기준으로 옆에서 온 값.
        var topN = new float[n]; var topVx = new float[n]; var topVz = new float[n]; var topMx = new float[n]; var topMz = new float[n];
        var N  = new float[n]; var Vx = new float[n]; var Vz = new float[n]; var Mx = new float[n]; var Mz = new float[n];
        var dist = new int[n];
        for (int i = 0; i < n; i++) { axialN[i] = 0f; overhang[i] = false; util[i] = 0f; }

        var levels = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (!levels.TryGetValue(grid[i].y, out var list)) { list = new List<int>(); levels[grid[i].y] = list; }
            list.Add(i);
        }

        var horiz = new int3[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        var keys = new List<int>(levels.Keys);
        keys.Reverse();   // 위층부터

        float maxAxial = 0f, maxUtil = 0f, maxM = 0f, baseShear = 0f; int overhangCount = 0;

        foreach (int y in keys)
        {
            var L = levels[y];

            var queue = new Queue<int>();
            foreach (int i in L)
            {
                bool supported = isGround[i];
                if (!supported && index.TryGetValue(grid[i] + new int3(0, -1, 0), out int b) && links.Contains(LinkKey(i, b)))
                    supported = true;
                dist[i] = supported ? 0 : -1;
                if (supported) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!index.TryGetValue(grid[cur] + horiz[d], out int nb)) continue;
                    if (dist[nb] != -1 || !links.Contains(LinkKey(cur, nb))) continue;
                    dist[nb] = dist[cur] + 1;
                    queue.Enqueue(nb);
                }
            }

            // 이 층 블록의 아랫면 기준 값 = 자중 + 관성력 + 위에서 내려온 값(윗면 -> 아랫면으로 한 블록 높이만큼 팔 추가)
            foreach (int i in L)
            {
                float mass = weightN[i] / Gravity;
                float hx = mass * accX + (windFx != null ? windFx[i] : 0f), hz = mass * accZ + (windFz != null ? windFz[i] : 0f);
                N[i]  += weightN[i] + topN[i];
                Vx[i] += hx + topVx[i];
                Vz[i] += hz + topVz[i];
                Mx[i] += topMx[i] + BlockSizeM * topVz[i] + 0.5f * BlockSizeM * hz;
                Mz[i] += topMz[i] - BlockSizeM * topVx[i] - 0.5f * BlockSizeM * hx;
            }

            var order = new List<int>(L);
            order.Sort((p, q) => dist[q].CompareTo(dist[p]));
            foreach (int i in order)
            {
                if (dist[i] < 0) { overhang[i] = true; overhangCount++; continue; }   // 어디에도 이어지지 않음

                if (dist[i] > 0)
                {
                    // 내민 블록: 옆으로 넘긴다. 수직 하중의 수평 팔 -> 모멘트. 조인트 면(팔 = 반 블록)에서 검사.
                    overhang[i] = true; overhangCount++;
                    int cnt = 0;
                    for (int d = 0; d < 4; d++)
                        if (index.TryGetValue(grid[i] + horiz[d], out int nb) && dist[nb] == dist[i] - 1 && links.Contains(LinkKey(i, nb))) cnt++;

                    for (int d = 0; d < 4; d++)
                    {
                        if (!index.TryGetValue(grid[i] + horiz[d], out int nb) || dist[nb] != dist[i] - 1 || !links.Contains(LinkKey(i, nb))) continue;

                        float pN = N[i] / cnt;
                        // r = 이 블록 중심 - 받는 블록 중심 (수평, m). 힘은 아래 방향 pN.
                        float rx = -horiz[d].x * BlockSizeM, rz = -horiz[d].z * BlockSizeM;
                        float mxShare = Mx[i] / cnt, mzShare = Mz[i] / cnt;

                        // 조인트 면 검사 (팔 = r/2)
                        float jx = mxShare + 0.5f * rz * pN;
                        float jz = mzShare - 0.5f * rx * pN;
                        // 조인트 면의 전단력 = 이 블록이 넘기는 수직력(pN)과 수평 전단력의 합성
                        float vj = (float)System.Math.Sqrt(pN * pN + (Vx[i] / cnt) * (Vx[i] / cnt) + (Vz[i] / cnt) * (Vz[i] / cnt));
                        float fvJ = fvMPa != null ? fvMPa[i] : ShearStrengthFallback(fcMPa[i], ftMPa[i]);
                        Section2(0f, jx, jz, vj, fcMPa[i], ftMPa[i], fvJ, out float cr, out float tr, out float sr, out float br);
                        if (detail != null) detail.Add(i, cr, tr, sr, br);
                        float u = System.Math.Max(cr, tr);
                        if (IncludeShear && sr > u) u = sr;
                        if (u > util[i]) util[i] = u;
                        float mj = System.Math.Abs(jx) + System.Math.Abs(jz);
                        if (mj > maxM) maxM = mj;

                        // 받는 블록 축 기준으로 넘긴다
                        N[nb]  += pN;
                        Vx[nb] += Vx[i] / cnt;
                        Vz[nb] += Vz[i] / cnt;
                        Mx[nb] += mxShare + rz * pN;
                        Mz[nb] += mzShare - rx * pN;
                    }
                    continue;
                }

                // dist == 0: 아랫면 단면 검사
                axialN[i] = N[i];
                float vBase = (float)System.Math.Sqrt(Vx[i] * Vx[i] + Vz[i] * Vz[i]);
                float fvB = fvMPa != null ? fvMPa[i] : ShearStrengthFallback(fcMPa[i], ftMPa[i]);
                Section2(N[i], Mx[i], Mz[i], vBase, fcMPa[i], ftMPa[i], fvB, out float c2, out float t2, out float s2, out float b2);
                if (detail != null) detail.Add(i, c2, t2, s2, b2);
                util[i] = System.Math.Max(c2, t2);
                if (IncludeShear && s2 > util[i]) util[i] = s2;
                if (N[i] > maxAxial) maxAxial = N[i];
                float m2 = System.Math.Abs(Mx[i]) + System.Math.Abs(Mz[i]);
                if (m2 > maxM) maxM = m2;

                if (isGround[i])
                {
                    baseShear += (float)System.Math.Sqrt(Vx[i] * Vx[i] + Vz[i] * Vz[i]);
                    continue;
                }
                if (index.TryGetValue(grid[i] + new int3(0, -1, 0), out int below) && links.Contains(LinkKey(i, below)))
                {
                    topN[below]  += N[i];
                    topVx[below] += Vx[i];
                    topVz[below] += Vz[i];
                    topMx[below] += Mx[i];
                    topMz[below] += Mz[i];
                }
            }
        }

        // 좌굴: 가로 이웃이 없는 독립 기둥만. 같은 세로 줄 전체 높이 H 로 오일러 임계하중을 구한다.
        if (eGPa != null && (IncludeBuckling || detail != null))
        {
            for (int i = 0; i < n; i++)
            {
                if (dist[i] != 0 || axialN[i] <= 0f || eGPa[i] <= 0f) continue;

                bool braced = false;
                for (int d = 0; d < 4 && !braced; d++)
                    if (index.TryGetValue(grid[i] + horiz[d], out int nb) && links.Contains(LinkKey(i, nb))) braced = true;
                if (braced) continue;

                int below = 0, above = 0, cur = i;
                while (!isGround[cur] && index.TryGetValue(grid[cur] + new int3(0, -1, 0), out int bi) && links.Contains(LinkKey(cur, bi))) { cur = bi; below++; }
                cur = i;
                while (index.TryGetValue(grid[cur] + new int3(0, 1, 0), out int ai) && links.Contains(LinkKey(cur, ai))) { cur = ai; above++; }

                float h = (below + above + 1) * BlockSizeM;
                float le = BucklingK * h;
                float pcr = (float)(System.Math.PI * System.Math.PI * eGPa[i] * 1.0e9 * SectionI / ((double)le * le));
                float br = pcr > 0f ? axialN[i] / pcr : 0f;
                if (detail != null) detail.Buckling[i] = br;
                if (IncludeBuckling && br > util[i]) util[i] = br;
            }
        }

        // 안전율 / 지배 모드
        if (detail != null)
        {
            for (int i = 0; i < n && i < detail.Safety.Length; i++)
            {
                float m = 0f; int mode = SolveDetail.ModeNone;
                if (detail.Comp[i]     > m) { m = detail.Comp[i];     mode = SolveDetail.ModeCompression; }
                if (detail.Tens[i]     > m) { m = detail.Tens[i];     mode = SolveDetail.ModeTension; }
                if (IncludeShear    && detail.Shear[i]    > m) { m = detail.Shear[i];    mode = SolveDetail.ModeShear; }
                if (IncludeBuckling && detail.Buckling[i] > m) { m = detail.Buckling[i]; mode = SolveDetail.ModeBuckling; }
                detail.Mode[i] = mode;
                detail.Safety[i] = m > 1.0e-6f ? 1f / m : 999f;
            }
        }

        for (int i = 0; i < n; i++) if (util[i] > maxUtil) maxUtil = util[i];
        // 호출자가 원하면 블록별 지지까지의 옆 거리(0 = 바로 아래가 지지, >0 = 내민 정도, -1 = 미지지)를 돌려준다.
        if (distOut != null) for (int i = 0; i < n && i < distOut.Length; i++) distOut[i] = dist[i];
        LastMaxAxialN = maxAxial;
        LastMaxUtilization = maxUtil;
        LastMaxMomentNm = maxM;
        LastBaseShearN = baseShear;
        LastBlockCount = n;
        LastOverhangCount = overhangCount;
    }
}
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
// 가정: 균등 분배, 정적 등가(지진 동적 증폭 없음), 전단은 계산만 하고 판정하지 않는다(전단강도 데이터 없음).
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

    // F5 창 표시용 마지막 계산 결과
    public static float LastMaxAxialN;
    public static float LastMaxUtilization;
    public static float LastBaseShearN;
    public static float LastMaxMomentNm;
    public static int   LastBlockCount;
    public static int   LastOverhangCount;

    public static float WeightN(float densityTPerM3)
    {
        return densityTPerM3 * 1000f * VolumeM3 * Gravity;   // t/m3 -> kg/m3 -> kg -> N
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

    // grid/weightN/isGround/links: 블록 정보. fcMPa/ftMPa: 재료 강도.
    // 결과: util[i] = 그 블록에서 가장 위험한 단면의 사용률(압축측/인장측 중 큰 값, 1 이상이면 파괴),
    //       axialN[i] = 아래로 전달하는 축력, overhang[i] = 옆으로 넘기거나 지지가 없는 블록
    public static void Solve(int3[] grid, float[] weightN, bool[] isGround, HashSet<long> links,
                             float[] fcMPa, float[] ftMPa,
                             float[] axialN, bool[] overhang, float[] util)
    {
        int n = grid.Length;
        var index = new Dictionary<int3, int>(n);
        for (int i = 0; i < n; i++) index[grid[i]] = i;

        // 지진 관성력 방향
        float rad = EarthquakeDirDeg * 0.0174532925f;
        float accX = EarthquakeG * Gravity * (float)System.Math.Cos(rad);
        float accZ = EarthquakeG * Gravity * (float)System.Math.Sin(rad);

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
                float hx = mass * accX, hz = mass * accZ;
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
                        Section(0f, jx, jz, fcMPa[i], ftMPa[i], out float cr, out float tr);
                        float u = System.Math.Max(cr, tr);
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
                Section(N[i], Mx[i], Mz[i], fcMPa[i], ftMPa[i], out float c2, out float t2);
                util[i] = System.Math.Max(c2, t2);
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

        for (int i = 0; i < n; i++) if (util[i] > maxUtil) maxUtil = util[i];
        LastMaxAxialN = maxAxial;
        LastMaxUtilization = maxUtil;
        LastMaxMomentNm = maxM;
        LastBaseShearN = baseShear;
        LastBlockCount = n;
        LastOverhangCount = overhangCount;
    }
}

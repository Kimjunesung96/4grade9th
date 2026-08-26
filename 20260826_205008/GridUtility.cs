using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public static class GridUtility
{
    public const float BlockSize = 3.0f;
    public const float HalfBlock = 1.5f;

    // DOTS 안전 (Burst 호환) 사사오입 스냅
    public static float Snap(float val)
    {
        return math.floor((val - HalfBlock) / BlockSize + 0.5f) * BlockSize + HalfBlock;
    }

    public static Vector3 Snap(Vector3 pos) => new Vector3(Snap(pos.x), Snap(pos.y), Snap(pos.z));
    public static float3 Snap(float3 pos) => new float3(Snap(pos.x), Snap(pos.y), Snap(pos.z));

    // 통합 블록 ID 생성 규칙
    public static string ToBlockID(float x, float y, float z)
    {
        float sx = Snap(x);
        float sy = Snap(y);
        float sz = Snap(z);

        // 부동소수점 오차 방지를 위해 0.001f 더하기
        float ix = math.round((sx + 0.001f) * 10f);
        float iy = math.round((sy + 0.001f) * 10f);
        float iz = math.round((sz + 0.001f) * 10f);

        string strX = $"{(ix < 0f ? "-" : "0")}{math.abs(ix):000}";
        string strZ = $"{(iz < 0f ? "-" : "0")}{math.abs(iz):000}";
        string strY = $"{(iy < 0f ? "-" : "0")}{math.abs(iy):000}";

        return $"{strX}_{strZ}_{strY}";
    }

    public static string ToBlockID(Vector3 pos) => ToBlockID(pos.x, pos.y, pos.z);
    public static string ToBlockID(float3 pos) => ToBlockID(pos.x, pos.y, pos.z);

    // ============================================================
    // 🆕 바닥/벽 2-pass 필터
    // ============================================================

    // 같은 Y층, XZ 평면 8방향 오프셋 (대각선 포함)
    private static readonly (int dx, int dz)[] XZ8 = new (int, int)[]
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1)
    };

    // 십자(상하좌우) 4방향 오프셋 - 2차 필터용
    private static readonly (int dx, int dz)[] Cross4 = new (int, int)[]
    {
        (1, 0), (-1, 0), (0, 1), (0, -1)
    };

    /// <summary>
    /// 1차 필터: 이 블록 기준으로 같은 Y층 XZ 8방향이 전부 존재하는가.
    /// existingBlocks는 GridUtility.ToBlockID 형식의 HashSet.
    /// </summary>
    public static bool Is1stTierFilled(float x, float y, float z, HashSet<string> existingBlocks)
    {
        for (int i = 0; i < XZ8.Length; i++)
        {
            float nx = x + XZ8[i].dx * BlockSize;
            float nz = z + XZ8[i].dz * BlockSize;
            if (!existingBlocks.Contains(ToBlockID(nx, y, nz))) return false;
        }
        return true;
    }

    /// <summary>
    /// 전체 블록 목록에 대해 2-pass로 "진짜 바닥"인 블록 ID 집합을 반환합니다.
    /// (자기 자신 1차 필터 통과 + 십자 이웃 4개도 각자 1차 필터 통과해야 바닥으로 인정)
    /// 나머지는 전부 벽/기둥/테두리로 간주하면 됩니다.
    /// </summary>
    public static HashSet<string> ComputeFloorBlocks(List<(float x, float y, float z, string id)> allBlocks, HashSet<string> existingBlocks)
    {
        // --- Pass 1: 자기 자신 8칸 검사 → 별도 버퍼에 저장 ---
        // (같은 패스에서 이웃 상태를 바로 참조하면 순서에 따라 오염되므로 반드시 분리)
        HashSet<string> tier1Passed = new HashSet<string>();
        foreach (var b in allBlocks)
        {
            if (Is1stTierFilled(b.x, b.y, b.z, existingBlocks))
                tier1Passed.Add(b.id);
        }

        // --- Pass 2: 자기 자신 + 십자 이웃 4개 모두 tier1Passed에 있어야 통과 ---
        HashSet<string> floorBlocks = new HashSet<string>();
        foreach (var b in allBlocks)
        {
            if (!tier1Passed.Contains(b.id)) continue; // 자기 자신부터 탈락이면 볼 것도 없음

            bool crossOk = true;
            for (int i = 0; i < Cross4.Length; i++)
            {
                float nx = b.x + Cross4[i].dx * BlockSize;
                float nz = b.z + Cross4[i].dz * BlockSize;
                string neighborId = ToBlockID(nx, b.y, nz);

                if (!tier1Passed.Contains(neighborId)) { crossOk = false; break; }
            }

            if (crossOk) floorBlocks.Add(b.id);
        }

        return floorBlocks;
    }
}
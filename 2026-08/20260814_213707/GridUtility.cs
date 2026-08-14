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
}
using System.IO;
using UnityEngine;

/// <summary>
/// 모든 CSV/JSON 데이터 경로를 한 곳에서 관리.
/// Application.dataPath는 에디터에서는 "Assets" 폴더, 빌드(exe)에서는 "게임이름_Data" 폴더를 가리키므로
/// 이전 코드처럼 여기저기서 직접 Path.Combine(Application.dataPath, ...)를 하면 빌드에서 경로가 깨짐.
/// 대신 Application.dataPath의 "부모 폴더"를 기준으로 삼으면:
///   - 에디터: 프로젝트 루트 (Assets와 같은 레벨)
///   - 빌드(exe): exe가 있는 폴더
/// 로 항상 일관되게 매핑되고, Assets 폴더 밖이라 유니티 임포트/DOTS 충돌도 피함.
/// </summary>
public static class PathManager
{
    private static string _dataRoot;

    public static string DataRoot
    {
        get
        {
            if (_dataRoot == null)
            {
                _dataRoot = Directory.GetParent(Application.dataPath).FullName;
            }
            return _dataRoot;
        }
    }

    public static string StressBlockDir => EnsureExists(Path.Combine(DataRoot, "StressBlock"));
    public static string BuildingLogsDir => EnsureExists(Path.Combine(DataRoot, "BuildingLogs"));
    public static string BuildingLogsTempDir => EnsureExists(Path.Combine(BuildingLogsDir, "temp"));
    public static string AiDesignsDir => EnsureExists(Path.Combine(DataRoot, "ai_designs"));
    public static string TempDir => EnsureExists(Path.Combine(DataRoot, "temp"));

    private static string EnsureExists(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }
}

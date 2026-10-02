using System;
using System.IO;
using UnityEngine;

/// <summary>
/// StressBlock / BuildingLogs / ai_designs 등 "Assets 폴더 바깥"에 저장되는
/// 외부 데이터 폴더의 경로를 에디터/빌드 어디서든 안전하게 계산해주는 헬퍼.
///
/// 문제였던 것: 기존 코드는 Path.Combine(Application.dataPath, "StressBlock", ...) 형태로
/// 경로를 만들었는데, 이건 에디터에서는 Application.dataPath == ".../프로젝트/Assets" 라서
/// 결과적으로 Assets 폴더 안(Assets/StressBlock)에 CSV를 쓰게 됨.
///   1) LogManager.cs 주석에도 있듯, Assets 안에 CSV를 쓰면 Unity가 실시간으로 임포트하려다
///      DOTS 쪽과 충돌 나는 문제가 있었음.
///   2) 빌드(exe)에서는 Application.dataPath == ".../빌드폴더/앱이름_Data" 로 바뀌기 때문에
///      "StressBlock" 폴더가 _Data 안에 생김. _Data는 Unity가 내부적으로 관리하는 폴더라
///      쓰기 권한 문제가 날 수도 있고, 빌드를 다시 하면 날아갈 수도 있는 위치라 부적절함.
///
/// 해결: Directory.GetParent(Application.dataPath) 를 기준 루트로 잡으면
///   - 에디터: Assets의 부모 = 프로젝트 루트 폴더
///   - 빌드:   _Data의 부모 = exe가 있는 폴더
/// 로 정확히 대응돼서, 에디터/빌드 어디서든 "프로젝트(또는 실행파일)와 같은 위치"에
/// 폴더가 생기게 됨. 별도 #if UNITY_EDITOR 분기 없이도 동일한 코드로 양쪽 다 처리 가능.
///
/// ⚠️ 주의: 이걸 적용하면 에디터에서 기존에 Assets/StressBlock, Assets/ai_designs 에
/// 있던 CSV/JSON 파일들은 이제 "프로젝트 루트/StressBlock", "프로젝트 루트/ai_designs"
/// 위치를 보게 됨. 기존 파일을 그대로 쓰려면 한 번 폴더를 옮겨줘야 함.
/// </summary>
public static class StressBlockPaths
{
    private static string _projectRoot;

    /// <summary>
    /// 에디터: 프로젝트 루트 폴더 (Assets의 부모)
    /// 빌드:   exe가 있는 폴더 (_Data의 부모)
    /// </summary>
    public static string ProjectRoot
    {
        get
        {
            if (_projectRoot != null) return _projectRoot;
            try
            {
                _projectRoot = Directory.GetParent(Application.dataPath).FullName;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StressBlockPaths] ProjectRoot 계산 실패, dataPath로 대체함: {e.Message}");
                _projectRoot = Application.dataPath;
            }
            return _projectRoot;
        }
    }

    public static string StressBlockDir => EnsureDir(Path.Combine(ProjectRoot, "StressBlock"));
    public static string BuildingLogsDir => EnsureDir(Path.Combine(ProjectRoot, "BuildingLogs"));
    public static string BuildingLogsTempDir => EnsureDir(Path.Combine(BuildingLogsDir, "temp"));
    public static string AiDesignsDir => EnsureDir(Path.Combine(ProjectRoot, "ai_designs"));
    public static string TempDir => EnsureDir(Path.Combine(ProjectRoot, "temp"));

    private static string EnsureDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception e)
        {
            Debug.LogError($"[StressBlockPaths] 폴더 생성 실패: {dir} / {e.Message}");
        }
        return dir;
    }

    // ---------- 안전한 파일 읽기 (2단계: 에러 핸들링 공용 헬퍼) ----------

    /// <summary>
    /// File.Exists 체크 + try/catch까지 포함해서 텍스트 라인 배열을 읽음.
    /// 파일이 없거나 읽기 실패하면 null 반환 (예외를 던지지 않음) — 호출부에서 null 체크로 처리.
    /// </summary>
    public static string[] SafeReadAllLines(string path, string context = "")
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning($"[StressBlockPaths] 파일 없음{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {path}");
            return null;
        }
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[StressBlockPaths] 파일 읽기 실패{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {path} / {e.Message}");
            return null;
        }
    }

    /// <summary>File.Exists + try/catch 포함 텍스트 전체 읽기. 실패 시 null.</summary>
    public static string SafeReadAllText(string path, string context = "")
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning($"[StressBlockPaths] 파일 없음{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {path}");
            return null;
        }
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[StressBlockPaths] 파일 읽기 실패{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {path} / {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 저장 시에도 폴더가 없어서 실패하는 경우를 막기 위한 안전한 쓰기.
    /// 성공하면 true, 실패하면 false.
    /// </summary>
    public static bool SafeWriteAllText(string path, string content, string context = "")
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[StressBlockPaths] 파일 쓰기 실패{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {path} / {e.Message}");
            return false;
        }
    }
}

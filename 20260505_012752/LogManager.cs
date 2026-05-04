using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 1. 단일 기록 (십장님 특허 3합축 기술 적용!)
[Serializable]
public class ActionLog
{
    public string time;
    public string tool;
    public string sizeXZH;
    public string centerXYZ;
    public string action;
    public string status;
    public string pivotXYZ;
}

[Serializable]
public class ProjectAttempt
{
    public int Attempt;
    public string Start_Time;
    public string End_Time;
    public string Status;
    public List<ActionLog> Actions = new List<ActionLog>();
}

[Serializable]
public class DailyRawLog
{
    public string Date;
    public int Total_Attempts;
    public List<string> Reset_Timestamps = new List<string>();
    public List<ProjectAttempt> Projects_Details = new List<ProjectAttempt>();
}

[Serializable]
public class TempLog
{
    public string Notice = "현재 타설 중인 블록입니다. (R키 누르면 날아감)";
    public List<ActionLog> Current_Blocks = new List<ActionLog>();
}

[Serializable]
public class Blueprint
{
    public int Blueprint_ID;
    public string Saved_Time;
    public List<ActionLog> Blocks = new List<ActionLog>();
}

[Serializable]
public class MasterBlueprintLog
{
    public string Date;
    public int Total_Masterpieces;
    public List<Blueprint> Blueprints = new List<Blueprint>();
}

public class LogManager : MonoBehaviour
{
    public static LogManager Instance;

    private DailyRawLog rawDailyLog;
    private TempLog tempLogData;
    private MasterBlueprintLog masterLogData;
    private ProjectAttempt currentAttempt;

    private string rawFilePath;
    private string tempFilePath;
    private string masterFilePath;

    private Transform simulationPivot;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        string dateStr = DateTime.Now.ToString("yyyyMMdd");

        // 🚨 [에러 대참사 완벽 해결!] 
        // Application.dataPath(Assets 폴더)에 저장하면 유니티가 실시간으로 임포트하려다 DOTS와 충돌합니다!
        // Assets 폴더 바깥인 "프로젝트루트경로/BuildingLogs" 폴더로 장부 보관소를 아예 이사시킵니다!
        string basePath = Path.Combine(Application.dataPath, "../BuildingLogs");

        string historyDir = Path.Combine(basePath, "history");
        string tempDir = Path.Combine(basePath, "temp");
        string architectDir = Path.Combine(basePath, "architecture");

        if (!Directory.Exists(historyDir)) Directory.CreateDirectory(historyDir);
        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
        if (!Directory.Exists(architectDir)) Directory.CreateDirectory(architectDir);

        rawFilePath = Path.Combine(historyDir, $"Log_Raw_{dateStr}.json");
        tempFilePath = Path.Combine(tempDir, "Temp_Current_Building.json");
        masterFilePath = Path.Combine(architectDir, $"Master_Blueprint_{dateStr}.json");

        LoadOrCreateLogs();
    }

    void Update()
    {
        if (simulationPivot == null)
        {
            GameObject pivotObj = GameObject.Find("SimulationPivot");
            if (pivotObj != null) simulationPivot = pivotObj.transform;
        }
    }

    void LoadOrCreateLogs()
    {
        if (File.Exists(rawFilePath)) rawDailyLog = JsonUtility.FromJson<DailyRawLog>(File.ReadAllText(rawFilePath));
        else rawDailyLog = new DailyRawLog { Date = DateTime.Now.ToString("yyyy-MM-dd") };

        if (File.Exists(masterFilePath)) masterLogData = JsonUtility.FromJson<MasterBlueprintLog>(File.ReadAllText(masterFilePath));
        else masterLogData = new MasterBlueprintLog { Date = DateTime.Now.ToString("yyyy-MM-dd") };

        tempLogData = new TempLog();
        SaveTempLog();

        if (rawDailyLog.Projects_Details.Count == 0 || rawDailyLog.Projects_Details[rawDailyLog.Projects_Details.Count - 1].Status != "In_Progress")
        {
            StartNewAttempt();
        }
        else
        {
            currentAttempt = rawDailyLog.Projects_Details[rawDailyLog.Projects_Details.Count - 1];
        }
    }

    void StartNewAttempt()
    {
        rawDailyLog.Total_Attempts++;
        currentAttempt = new ProjectAttempt { Attempt = rawDailyLog.Total_Attempts, Start_Time = DateTime.Now.ToString("HH:mm:ss"), Status = "In_Progress" };
        rawDailyLog.Projects_Details.Add(currentAttempt);
        SaveRawLog();
    }

    public void AddLog(string tool, int heightCount, string action, string status, string centerStr, float sizeX, float sizeZ)
    {
        string pivotStr = "Center_Fixed";
        if (simulationPivot != null)
        {
            pivotStr = $"[{simulationPivot.position.x:F2}, {simulationPivot.position.y:F2}, {simulationPivot.position.z:F2}]";
        }

        ActionLog newLog = new ActionLog
        {
            time = DateTime.Now.ToString("HH:mm:ss"),
            tool = tool,
            sizeXZH = $"[{sizeX:F1}, {sizeZ:F1}, {heightCount}]",
            centerXYZ = centerStr,
            action = action,
            status = status,
            pivotXYZ = pivotStr
        };

        currentAttempt.Actions.Add(newLog);
        SaveRawLog();

        if (action == "Key_G" || status == "Build_Complete")
        {
            tempLogData.Current_Blocks.Add(newLog);
            SaveTempLog();
            Debug.Log($"[현장보고] 타설 완료! (타설중앙: {centerStr} / 바닥코어: {pivotStr})");
        }
    }

    public void OnPressRKey()
    {
        string nowTime = DateTime.Now.ToString("HH:mm:ss");
        currentAttempt.End_Time = nowTime;
        currentAttempt.Status = "Reset";
        rawDailyLog.Reset_Timestamps.Add($"Attempt {currentAttempt.Attempt}: {nowTime} (Reset)");
        SaveRawLog();
        StartNewAttempt();

        tempLogData.Current_Blocks.Clear();
        SaveTempLog();
    }

    public void SaveToMaster()
    {
        if (tempLogData.Current_Blocks.Count == 0) return;
        string nowTime = DateTime.Now.ToString("HH:mm:ss");
        masterLogData.Total_Masterpieces++;
        Blueprint newBlueprint = new Blueprint { Blueprint_ID = masterLogData.Total_Masterpieces, Saved_Time = nowTime, Blocks = new List<ActionLog>(tempLogData.Current_Blocks) };
        masterLogData.Blueprints.Add(newBlueprint);
        SaveMasterLog();

        currentAttempt.End_Time = nowTime;
        currentAttempt.Status = "Success";
        rawDailyLog.Reset_Timestamps.Add($"Attempt {currentAttempt.Attempt}: {nowTime} (Success)");
        SaveRawLog();
        StartNewAttempt();

        Debug.Log("🎉 마스터 도면집에 십장님 특허 장부 도장 쾅! (Temp 장부 기록은 계속 유지됩니다)");
    }

    private void SaveRawLog() { try { File.WriteAllText(rawFilePath, JsonUtility.ToJson(rawDailyLog, true)); } catch (Exception e) { Debug.LogWarning("Raw Log 저장 실패: " + e.Message); } }
    private void SaveTempLog() { try { File.WriteAllText(tempFilePath, JsonUtility.ToJson(tempLogData, true)); } catch (Exception e) { Debug.LogWarning("Temp Log 저장 실패: " + e.Message); } }
    private void SaveMasterLog() { try { File.WriteAllText(masterFilePath, JsonUtility.ToJson(masterLogData, true)); } catch (Exception e) { Debug.LogWarning("Master Log 저장 실패: " + e.Message); } }
}
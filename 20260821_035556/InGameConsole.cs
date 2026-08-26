// [파일명: InGameConsole.cs] - TMP 적용 버전

using UnityEngine;
using UnityEngine.UI; 
using TMPro; // ⭐ TextMeshPro 사용을 위해 추가
using System.Collections.Generic;

public class InGameConsole : MonoBehaviour
{
    [Header("콘솔 설정")]
    public KeyCode toggleKey = KeyCode.F3;
    public int maxLogs = 50;

    [Header("UGUI 연결 (에디터에서 드래그 앤 드롭)")]
    public GameObject consolePanel; 
    public TextMeshProUGUI logText; // ⭐ 일반 Text에서 TextMeshProUGUI로 변경!
    public ScrollRect scrollRect; 

    private bool showConsole = false;
    private List<string> logList = new List<string>();
    private bool isDirty = false; 

    void OnEnable()
    {
        Application.logMessageReceivedThreaded += HandleLog;
        if (consolePanel != null) consolePanel.SetActive(showConsole);
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= HandleLog;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            showConsole = !showConsole;
            if (consolePanel != null) consolePanel.SetActive(showConsole);
        }

        if (showConsole && isDirty)
        {
            UpdateLogText();
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logString.Contains("메모리 체크")) return;

        string color = "white";
        if (type == LogType.Warning) color = "yellow";
        else if (type == LogType.Error || type == LogType.Exception) color = "red";

        lock (logList)
        {
            logList.Add($"<color={color}>{logString}</color>");
            
            if (logList.Count > maxLogs)
            {
                logList.RemoveAt(0);
            }
            
            isDirty = true; 
        }
    }

    private void UpdateLogText()
    {
        lock (logList)
        {
            if (logText != null)
            {
                logText.text = string.Join("\n", logList);
            }
            isDirty = false;
            
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}
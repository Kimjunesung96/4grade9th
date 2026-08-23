using UnityEngine;
using System.Collections.Generic;

public class InGameConsole : MonoBehaviour
{
    [Header("콘솔 설정")]
    public KeyCode toggleKey = KeyCode.F3; // 콘솔창을 켜고 끄는 단축키
    public int maxLogs = 50; // 화면에 유지할 최대 로그 개수

    private bool showConsole = false;
    private Vector2 scrollPosition;
    private List<string> logList = new List<string>();

    void OnEnable()
    {
        // ⭐ DOTS(멀티스레드) 환경에서 안전하게 로그를 가로채기 위해 Threaded 사용
        Application.logMessageReceivedThreaded += HandleLog;
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
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        // 🛑 [핵심] '메모리 체크'가 포함된 로그는 무시 (스팸 방지)
        if (logString.Contains("메모리 체크")) return;

        // 로그 타입에 따라 색상 지정
        string color = "white";
        if (type == LogType.Warning) color = "yellow";
        else if (type == LogType.Error || type == LogType.Exception) color = "red";

        // 멀티스레드 환경 충돌 방지를 위한 lock
        lock (logList)
        {
            logList.Add($"<color={color}>{logString}</color>");
            
            // 지정된 개수를 넘어가면 가장 오래된 로그 삭제
            if (logList.Count > maxLogs)
            {
                logList.RemoveAt(0);
            }
            
            // 새 로그가 들어오면 스크롤을 맨 아래로 자동 이동
            scrollPosition.y = float.MaxValue;
        }
    }

    void OnGUI()
    {
        if (!showConsole) return;

        // 콘솔창 크기 및 위치 설정 (화면 우측 상단)
        float width = 450f;
        float height = 350f;
        float x = Screen.width - width - 20f;
        float y = 20f;

        GUILayout.BeginArea(new Rect(x, y, width, height), $"인게임 현장 무전기 ({toggleKey}로 닫기)", GUI.skin.window);
        
        // 스크롤 뷰 시작
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        
        // 폰트 스타일 (RichText 활성화로 색상 적용)
        GUIStyle logStyle = new GUIStyle(GUI.skin.label);
        logStyle.richText = true;
        logStyle.wordWrap = true;

        // 저장된 로그들 출력
        lock (logList)
        {
            foreach (string log in logList)
            {
                GUILayout.Label(log, logStyle);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
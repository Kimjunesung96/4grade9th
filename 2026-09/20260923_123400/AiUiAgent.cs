using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 🤖 유니티 상단 메뉴에 나만의 AI 툴 창을 만듭니다.
public class AiUiAgent : EditorWindow
{
    // ⚠️ 보안: API 키는 하드코딩하지 않고 아래 텍스트필드에 직접 입력하세요.
  private string apiKey = "api긴한데니가뭘할수있는데"; 
    private string prompt = "화면 중앙에 가로 600, 세로 400 크기의 'MainPanel' 패널을 만들고, 그 안에 'StartButton'이라는 가로 200짜리 버튼을 만들어줘.";
    private string statusMsg = "대기 중...";

    // 🗨️ 모드 전환: true면 UI를 실제로 생성, false면 자유 대화(하이어라키 인지 + 인스펙터 열람 가능)
    private bool uiGenMode = true;
    private string chatResponse = "";
    private Vector2 scrollPos;

    // 💬 대화 기록: 매 전송마다 새로 만들지 않고 계속 이어감 (null이면 아직 대화 시작 전)
    private List<string> conversationMessages = null;

    private const string MODEL = "openai/gpt-oss-120b";
    private const int MAX_TOOL_ROUNDS = 4; // AI가 도구 호출을 무한 반복하지 않도록 제한
    private const int MAX_HISTORY_MESSAGES = 10; // system 제외, 최근 N개 메시지만 유지 (토큰 절약)
    private const int MAX_COMPONENTS_PER_LINE = 5; // 하이어라키 한 줄에 나열할 컴포넌트 수 제한

    [MenuItem("Window/🤖 AI UI 에이전트")]
    public static void ShowWindow()
    {
        GetWindow<AiUiAgent>("AI UI 에이전트");
    }

    void OnGUI()
    {
        GUILayout.Label("나만의 UI 자동 생성 에이전트", EditorStyles.boldLabel);

        apiKey = EditorGUILayout.TextField("Groq API Key", apiKey);

        GUILayout.Space(6);
        uiGenMode = GUILayout.Toolbar(uiGenMode ? 0 : 1, new[] { "🏗️ UI 생성 모드", "💬 자유 대화 모드" }) == 0;

        GUILayout.Space(10);
        GUILayout.Label(uiGenMode
            ? "AI에게 UI 생성을 명령하세요:"
            : "AI랑 대화하세요. (하이어라키 구조가 자동으로 전달되고, 필요하면 AI가 직접 특정 오브젝트의 인스펙터를 열람합니다)");
        prompt = EditorGUILayout.TextArea(prompt, GUILayout.Height(60));

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(uiGenMode ? "🚀 AI에게 UI 생성 시키기" : "💬 보내기", GUILayout.Height(40)))
        {
            if (uiGenMode) SendUiGenRequest();
            else SendChatRequest();
        }
        if (!uiGenMode && GUILayout.Button("🔄 대화 초기화", GUILayout.Height(40), GUILayout.Width(100)))
        {
            conversationMessages = null;
            chatResponse = "";
            statusMsg = "대화가 초기화되었습니다.";
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label($"상태: {statusMsg}");

        if (!uiGenMode && !string.IsNullOrEmpty(chatResponse))
        {
            GUILayout.Space(10);
            GUILayout.Label("AI 응답:", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(260));
            EditorGUILayout.TextArea(chatResponse, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    // =========================================================
    // 🏗️ UI 생성 모드 (기존 기능 그대로)
    // =========================================================

    private void SendUiGenRequest()
    {
        if (string.IsNullOrEmpty(apiKey)) { statusMsg = "API Key를 입력해주세요!"; return; }

        statusMsg = "AI가 도면을 설계 중입니다... ⏳";

        string systemPrompt = "너는 유니티 UI 에이전트야. 사용자의 요구를 분석해서 오직 아래 JSON 형식으로만 대답해. 마크다운이나 다른 설명은 절대 금지. " +
                               "{\"elements\":[{\"type\":\"Panel\",\"name\":\"패널이름\",\"w\":600,\"h\":400}, {\"type\":\"Button\",\"name\":\"버튼이름\",\"w\":200,\"h\":50}]}";

        string messagesJson = "[" +
            BuildMessageJson("system", systemPrompt) + "," +
            BuildMessageJson("user", prompt) +
            "]";

        string jsonPayload = $"{{\"model\":\"{MODEL}\",\"messages\":{messagesJson}}}";

        PostToGroq(jsonPayload, responseJson => ParseAndBuildUI(responseJson));
    }

    // 🏗️ AI가 준 JSON을 해독해서 실제로 유니티 화면에 짓기!
    private void ParseAndBuildUI(string responseJson)
    {
        try
        {
            GroqResponse groqResponse = JsonUtility.FromJson<GroqResponse>(responseJson);

            if (groqResponse == null || groqResponse.choices == null || groqResponse.choices.Length == 0)
            {
                statusMsg = "AI 응답 구조가 이상합니다. (choices 없음)";
                Debug.LogError("원본 응답: " + responseJson);
                return;
            }

            string content = groqResponse.choices[0].message.content ?? "";
            content = content.Replace("```json", "").Replace("```", "").Trim();

            AiUIResult uiResult = JsonUtility.FromJson<AiUIResult>(content);
            if (uiResult == null || uiResult.elements == null)
            {
                statusMsg = "AI가 이상한 도면을 줬습니다. 다시 시도하세요.";
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            foreach (var el in uiResult.elements)
            {
                GameObject newObj = new GameObject(el.name);
                newObj.transform.SetParent(canvas.transform, false);

                RectTransform rect = newObj.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(el.w, el.h);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;

                if (el.type == "Panel")
                {
                    Image img = newObj.AddComponent<Image>();
                    img.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                }
                else if (el.type == "Button")
                {
                    Image img = newObj.AddComponent<Image>();
                    img.color = Color.white;
                    newObj.AddComponent<Button>();
                }
            }

            statusMsg = $"성공! {uiResult.elements.Length}개의 UI가 생성되었습니다.";
            Debug.Log("🤖 AI가 UI 생성을 완료했습니다!");
        }
        catch (System.Exception e)
        {
            statusMsg = "도면 해독 실패!";
            Debug.LogError("JSON 파싱 에러: " + e.Message + "\n원본 응답: " + responseJson);
        }
    }

    // =========================================================
    // 💬 자유 대화 모드 (하이어라키 자동 첨부 + Tool Use로 인스펙터 열람)
    // =========================================================

    private void SendChatRequest()
    {
        if (string.IsNullOrEmpty(apiKey)) { statusMsg = "API Key를 입력해주세요!"; return; }

        statusMsg = "AI가 씬을 살펴보는 중입니다... ⏳";

        string systemPrompt =
            "너는 유니티 에디터 안에서 작동하는 개발 비서야. 사용자 메시지 앞에는 현재 씬의 Hierarchy 구조(오브젝트 이름만 나열된 트리)가 함께 제공돼. " +
            "이 목록만으로 부족하면, get_inspector 도구를 호출해서 특정 오브젝트의 Transform과 컴포넌트 상세 값을 직접 조회할 수 있어. " +
            "필요할 때 주저하지 말고 도구를 사용하고, 확인한 내용을 바탕으로 자연스럽게 한국어로 답변해줘.";

        string hierarchySummary = BuildHierarchySummary();
        // 씬이 바뀔 수 있으니 매 턴마다 최신 Hierarchy 스냅샷을 첨부
        string userContent = $"[현재 Hierarchy 스냅샷]\n{hierarchySummary}\n\n[사용자 메시지]\n{prompt}";

        if (conversationMessages == null)
        {
            // 대화 시작: system 메시지는 한 번만 넣음
            conversationMessages = new List<string> { BuildMessageJson("system", systemPrompt) };
        }
        conversationMessages.Add(BuildMessageJson("user", userContent));
        TrimConversationHistory();

        RunChatRound(conversationMessages, MAX_TOOL_ROUNDS);
    }

    // system 메시지(0번)는 남기고, 나머지는 최근 MAX_HISTORY_MESSAGES개만 유지해서 토큰 사용량을 억제
    private void TrimConversationHistory()
    {
        if (conversationMessages == null || conversationMessages.Count <= MAX_HISTORY_MESSAGES + 1) return;

        string systemMsg = conversationMessages[0];
        int removeCount = conversationMessages.Count - 1 - MAX_HISTORY_MESSAGES;
        conversationMessages.RemoveRange(1, removeCount);
        // system 메시지가 혹시라도 같이 잘렸을 경우를 대비해 안전하게 유지
        if (conversationMessages.Count == 0 || conversationMessages[0] != systemMsg)
            conversationMessages.Insert(0, systemMsg);
    }

    // 도구 호출이 끝날 때까지(또는 라운드 제한까지) 반복해서 Groq에 요청을 보내는 재귀 함수
    private void RunChatRound(List<string> messages, int roundsLeft)
    {
        string toolsJson =
            "[{\"type\":\"function\",\"function\":{" +
            "\"name\":\"get_inspector\"," +
            "\"description\":\"지정한 이름의 게임 오브젝트의 인스펙터 정보(Transform, 주요 컴포넌트 값)를 가져옵니다.\"," +
            "\"parameters\":{\"type\":\"object\",\"properties\":{" +
            "\"objectName\":{\"type\":\"string\",\"description\":\"조회할 게임 오브젝트 이름\"}}," +
            "\"required\":[\"objectName\"]}}}]";

        string messagesJson = "[" + string.Join(",", messages) + "]";
        string jsonPayload = $"{{\"model\":\"{MODEL}\",\"messages\":{messagesJson},\"tools\":{toolsJson}}}";

        PostToGroq(jsonPayload, responseJson =>
        {
            GroqResponse groqResponse;
            try
            {
                groqResponse = JsonUtility.FromJson<GroqResponse>(responseJson);
            }
            catch (System.Exception e)
            {
                statusMsg = "응답 해석 실패!";
                Debug.LogError("파싱 에러: " + e.Message + "\n원본 응답: " + responseJson);
                return;
            }

            if (groqResponse == null || groqResponse.choices == null || groqResponse.choices.Length == 0)
            {
                statusMsg = "AI 응답 구조가 이상합니다.";
                Debug.LogError("원본 응답: " + responseJson);
                return;
            }

            GroqMessage msg = groqResponse.choices[0].message;

            // AI가 도구 호출을 요청했고, 아직 라운드가 남아있다면 도구를 실행하고 다시 요청
            if (msg.tool_calls != null && msg.tool_calls.Length > 0 && roundsLeft > 0)
            {
                statusMsg = "AI가 인스펙터를 확인하는 중... ⏳";

                // 방금 받은 assistant 메시지(도구 호출 포함)를 대화 기록에 그대로 추가
                string assistantMsgJson = ExtractBalancedJsonAfterKey(responseJson, "\"message\"");
                messages.Add(assistantMsgJson);

                // 각 도구 호출을 실제로 실행하고, 그 결과를 tool 메시지로 추가
                foreach (var call in msg.tool_calls)
                {
                    string objectName = "";
                    try
                    {
                        ToolArgs args = JsonUtility.FromJson<ToolArgs>(call.function.arguments);
                        objectName = args != null ? args.objectName : "";
                    }
                    catch { /* 인자 파싱 실패 시 빈 이름으로 처리 */ }

                    string resultText = GetInspectorDetails(objectName);
                    messages.Add(BuildToolResultJson(call.id, resultText));
                }

                RunChatRound(messages, roundsLeft - 1);
                return;
            }

            // 더 이상 도구 호출이 없으면 최종 답변으로 표시하고, 대화 기록에도 남겨서 다음 턴에 이어지게 함
            chatResponse = msg.content ?? "(빈 응답)";
            messages.Add(BuildMessageJson("assistant", chatResponse));
            statusMsg = "응답 받음!";
            Repaint();
        });
    }

    // =========================================================
    // 🔍 씬 정보 수집 유틸
    // =========================================================

    // 하이어라키 창에 보이는 모든 것(카메라, 라이트, 일반 오브젝트, UI 등)을 이름+컴포넌트 요약으로 나열
    private string BuildHierarchySummary()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        if (roots.Length == 0)
        {
            return "(현재 씬에 오브젝트가 하나도 없습니다.)";
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("씬 이름: ").Append(scene.name).Append('\n');
        foreach (var root in roots)
        {
            AppendHierarchyLine(sb, root.transform, 0);
        }
        return sb.ToString();
    }

    private void AppendHierarchyLine(StringBuilder sb, Transform t, int depth)
    {
        sb.Append(' ', depth * 2).Append("- ").Append(t.name).Append(GetTypeHint(t)).Append('\n');

        for (int i = 0; i < t.childCount; i++)
        {
            AppendHierarchyLine(sb, t.GetChild(i), depth + 1);
        }
    }

    // 오브젝트에 붙어있는 컴포넌트 이름들을 괄호로 요약 (Transform/RectTransform은 당연히 있으니 생략)
    private string GetTypeHint(Transform t)
    {
        List<string> names = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;
            names.Add(c.GetType().Name);
        }
        if (names.Count == 0) return "";
        if (names.Count > MAX_COMPONENTS_PER_LINE)
        {
            int totalCount = names.Count;
            names = names.GetRange(0, MAX_COMPONENTS_PER_LINE);
            names.Add($"...+{totalCount - MAX_COMPONENTS_PER_LINE}개 더");
        }
        return " (" + string.Join(", ", names) + ")";
    }

    // 특정 오브젝트를 이름으로 찾아 인스펙터 값을 텍스트로 나열 (AI가 도구 호출로 요청했을 때만 실행)
    private string GetInspectorDetails(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return "오브젝트 이름이 비어있습니다.";

        Transform found = FindInScene(objectName);
        if (found == null)
            return $"'{objectName}' 이름의 오브젝트를 씬에서 찾을 수 없습니다.";

        StringBuilder sb = new StringBuilder();
        sb.Append("이름: ").Append(found.name).Append('\n');
        sb.Append("활성화: ").Append(found.gameObject.activeSelf).Append('\n');

        RectTransform rt = found as RectTransform;
        if (rt != null)
        {
            sb.Append("RectTransform: anchoredPosition=").Append(rt.anchoredPosition)
              .Append(", sizeDelta=").Append(rt.sizeDelta)
              .Append(", anchorMin=").Append(rt.anchorMin)
              .Append(", anchorMax=").Append(rt.anchorMax)
              .Append(", pivot=").Append(rt.pivot).Append('\n');
        }

        foreach (var comp in found.GetComponents<Component>())
        {
            if (comp == null || comp is Transform) continue;

            Image img = comp as Image;
            Button btn = comp as Button;
            Text txt = comp as Text;
            CanvasGroup cg = comp as CanvasGroup;
            Camera cam = comp as Camera;
            Light lightComp = comp as Light;

            if (img != null)
                sb.Append("Image: color=").Append(img.color).Append(", sprite=").Append(img.sprite ? img.sprite.name : "없음").Append('\n');
            else if (btn != null)
                sb.Append("Button: interactable=").Append(btn.interactable).Append(", onClick 리스너 수=").Append(btn.onClick.GetPersistentEventCount()).Append('\n');
            else if (txt != null)
                sb.Append("Text: text=\"").Append(txt.text).Append("\", fontSize=").Append(txt.fontSize).Append(", color=").Append(txt.color).Append('\n');
            else if (cg != null)
                sb.Append("CanvasGroup: alpha=").Append(cg.alpha).Append(", interactable=").Append(cg.interactable).Append('\n');
            else if (cam != null)
                sb.Append("Camera: fieldOfView=").Append(cam.fieldOfView).Append(", orthographic=").Append(cam.orthographic).Append('\n');
            else if (lightComp != null)
                sb.Append("Light: type=").Append(lightComp.type).Append(", intensity=").Append(lightComp.intensity).Append(", color=").Append(lightComp.color).Append('\n');
            else
                sb.Append(comp.GetType().Name).Append(" (컴포넌트 부착됨)\n");
        }

        // Transform 자체의 world 좌표도 (RectTransform이 아닌 일반 오브젝트용으로) 추가
        if (rt == null)
        {
            sb.Append("Transform: position=").Append(found.position)
              .Append(", rotation=").Append(found.rotation.eulerAngles)
              .Append(", localScale=").Append(found.localScale).Append('\n');
        }

        return sb.ToString();
    }

    // 씬의 모든 루트 오브젝트부터 시작해서 이름으로 재귀 검색 (UI, 카메라, 라이트 등 전부 포함)
    private Transform FindInScene(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            Transform hit = FindRecursive(root.transform, objectName);
            if (hit != null) return hit;
        }
        return null;
    }

    private Transform FindRecursive(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform hit = FindRecursive(t.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // =========================================================
    // 🌐 네트워크 / JSON 유틸
    // =========================================================

    private void PostToGroq(string jsonPayload, System.Action<string> onSuccess)
    {
        string url = "https://api.groq.com/openai/v1/chat/completions";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        var operation = request.SendWebRequest();
        operation.completed += (op) =>
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess(request.downloadHandler.text);
            }
            else
            {
                string errorBody = request.downloadHandler.text;
                float waitSeconds = TryParseRateLimitWait(errorBody);

                if (waitSeconds > 0f)
                {
                    statusMsg = $"토큰 한도 초과. {waitSeconds:F1}초 후 자동 재시도합니다... ⏳";
                    Debug.LogWarning("Rate limit, 재시도 예약: " + errorBody);
                    Repaint();

                    ScheduleDelayedCall(waitSeconds + 0.5, () => PostToGroq(jsonPayload, onSuccess));
                }
                else
                {
                    statusMsg = "통신 에러: " + request.error;
                    Debug.LogError("API 응답 에러: " + errorBody);
                }
            }
            request.Dispose();
            Repaint();
        };
    }

    // 지정한 초 후에 callback을 한 번 실행 (local function 없이 구식 델리게이트 자기참조 패턴으로 구현)
    private void ScheduleDelayedCall(double delaySeconds, System.Action callback)
    {
        double targetTime = EditorApplication.timeSinceStartup + delaySeconds;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (EditorApplication.timeSinceStartup >= targetTime)
            {
                EditorApplication.update -= tick;
                callback();
            }
        };
        EditorApplication.update += tick;
    }

    // Groq의 "Please try again in 15.24s" 형태 메시지에서 대기 시간(초)을 추출. 못 찾으면 0 반환.
    private float TryParseRateLimitWait(string errorBody)
    {
        if (string.IsNullOrEmpty(errorBody) || !errorBody.Contains("rate_limit_exceeded")) return 0f;

        var match = System.Text.RegularExpressions.Regex.Match(errorBody, @"try again in ([0-9]+(\.[0-9]+)?)s");
        if (match.Success && float.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float seconds))
        {
            return seconds;
        }
        return 5f; // 시간 파싱 실패해도 rate limit이면 안전하게 5초 대기 후 재시도
    }

    // role/content 메시지 하나를 안전하게 이스케이프된 JSON 조각으로 생성
    private string BuildMessageJson(string role, string content)
    {
        return $"{{\"role\":\"{role}\",\"content\":\"{EscapeForJson(content)}\"}}";
    }

    // 도구 실행 결과를 tool 메시지로 감싸기 (OpenAI/Groq 호환 형식)
    private string BuildToolResultJson(string toolCallId, string content)
    {
        return $"{{\"role\":\"tool\",\"tool_call_id\":\"{toolCallId}\",\"content\":\"{EscapeForJson(content)}\"}}";
    }

    private string EscapeForJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                 .Replace("\"", "\\\"")
                 .Replace("\n", "\\n")
                 .Replace("\r", "");
    }

    // 원본 응답 문자열에서 "key" 뒤에 오는 중괄호 블록을 괄호 개수 세어 그대로 잘라오는 유틸.
    // (assistant의 tool_calls 포함 메시지를 다음 요청에 그대로 재사용하기 위함 - 재직렬화 없이 원문 그대로 전달)
    private string ExtractBalancedJsonAfterKey(string json, string key)
    {
        int keyIndex = json.IndexOf(key);
        if (keyIndex < 0) return "{}";

        int braceStart = json.IndexOf('{', keyIndex);
        if (braceStart < 0) return "{}";

        int depth = 0;
        for (int i = braceStart; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return json.Substring(braceStart, i - braceStart + 1);
            }
        }
        return "{}";
    }

    // =========================================================
    // 📦 JSON 응답을 받아줄 그릇들
    // =========================================================

    [System.Serializable]
    public class AiUIResult { public AiUIElement[] elements; }

    [System.Serializable]
    public class AiUIElement { public string type; public string name; public float w; public float h; }

    [System.Serializable]
    public class GroqResponse { public GroqChoice[] choices; }

    [System.Serializable]
    public class GroqChoice { public GroqMessage message; }

    [System.Serializable]
    public class GroqMessage
    {
        public string role;
        public string content;
        public GroqToolCall[] tool_calls;
    }

    [System.Serializable]
    public class GroqToolCall
    {
        public string id;
        public string type;
        public GroqFunctionCall function;
    }

    [System.Serializable]
    public class GroqFunctionCall
    {
        public string name;
        public string arguments; // JSON 문자열 (예: {"objectName":"StartButton"})
    }

    [System.Serializable]
    public class ToolArgs { public string objectName; }
}
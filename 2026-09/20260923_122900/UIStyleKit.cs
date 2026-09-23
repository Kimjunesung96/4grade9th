using UnityEngine;

/// <summary>
/// 3단계(UI 다듬기)용 공용 스타일 킷.
///
/// 왜 "OnGUI 그대로 두고 스타일만" 인가:
///   BudgetUIManager의 OnGUI()는 700줄 넘게 탭/조건분기/버튼 콜백이 얽혀있는 실제 동작 로직이라,
///   이걸 지금 UGUI Canvas로 통째로 옮기면 발표 직전에 상호작용 버그가 새로 생길 위험이 큼
///   (에디터에서 직접 눌러보며 테스트할 수 없는 상태로 대규모 리라이트를 하는 거라 위험 대비 이득이 작음).
///   그래서 "동작 로직은 절대 건드리지 않고, 보이는 모양만" 통일해서 안전하게 다듬는 쪽을 택함.
///   → GUI.skin 하나만 교체하는 방식이라 기존 버튼 클릭/콜백 코드는 한 줄도 안 바뀜.
///
/// 여유 생기면 다음 단계로 진짜 UGUI Canvas 전환(특히 BudgetUIManager)을 권장하고,
/// 그때는 Unity 에디터에서 직접 눌러보면서 하나씩 옮기는 걸 추천함.
/// </summary>
public static class UIStyleKit
{
    // 팀/발표 톤에 맞춰 여기 색만 바꾸면 전체 UI 톤이 한번에 바뀜
    public static readonly Color PanelBg = new Color(0.10f, 0.11f, 0.14f, 0.92f);
    public static readonly Color HeaderBg = new Color(0.16f, 0.45f, 0.85f, 1f);
    public static readonly Color ButtonBg = new Color(0.22f, 0.23f, 0.28f, 1f);
    public static readonly Color ButtonActive = new Color(0.20f, 0.55f, 0.95f, 1f);
    public static readonly Color TextColor = new Color(0.92f, 0.94f, 0.98f, 1f);
    public static readonly Color AccentGood = new Color(0.45f, 0.85f, 0.55f, 1f);
    public static readonly Color AccentWarn = new Color(0.95f, 0.55f, 0.35f, 1f);

    private static GUISkin _skin;
    private static Texture2D _panelTex, _headerTex, _buttonTex, _activeTex, _dimTex;

    /// <summary>
    /// 이 프레임에 쓸 통일된 GUISkin. OnGUI() 맨 위에서 `GUI.skin = UIStyleKit.Skin;` 한 줄만 추가하면 적용됨.
    /// </summary>
    public static GUISkin Skin
    {
        get
        {
            if (_skin != null) return _skin;

            _panelTex = MakeTex(PanelBg);
            _headerTex = MakeTex(HeaderBg);
            _buttonTex = MakeTex(ButtonBg);
            _activeTex = MakeTex(ButtonActive);
            _dimTex = MakeTex(new Color(0f, 0f, 0f, 0.55f));

            _skin = ScriptableObject.CreateInstance<GUISkin>();
            _skin.font = GUI.skin.font;

            _skin.window = new GUIStyle(GUI.skin.window);
            _skin.window.normal.background = _panelTex;
            _skin.window.onNormal.background = _panelTex;
            _skin.window.normal.textColor = TextColor;
            _skin.window.padding = new RectOffset(10, 10, 26, 10);
            _skin.window.font = _skin.font;
            _skin.window.fontStyle = FontStyle.Bold;

            _skin.box = new GUIStyle(GUI.skin.box);
            _skin.box.normal.background = _panelTex;
            _skin.box.normal.textColor = TextColor;
            _skin.box.padding = new RectOffset(14, 14, 14, 14);

            _skin.label = new GUIStyle(GUI.skin.label);
            _skin.label.normal.textColor = TextColor;
            _skin.label.richText = true;
            _skin.label.fontSize = 14;

            _skin.button = new GUIStyle(GUI.skin.button);
            _skin.button.normal.background = _buttonTex;
            _skin.button.normal.textColor = TextColor;
            _skin.button.hover.background = _activeTex;
            _skin.button.hover.textColor = Color.white;
            _skin.button.fontSize = 14;
            _skin.button.fontStyle = FontStyle.Bold;

            _skin.textField = new GUIStyle(GUI.skin.textField);
            _skin.textField.normal.textColor = TextColor;
            _skin.textField.fontSize = 14;

            return _skin;
        }
    }

    /// <summary>모달 패널 뒤에 살짝 어둡게 깔아서 포커스를 주는 반투명 배경. showPanel일 때만 그려주면 됨.</summary>
    public static void DrawDim()
    {
        if (_dimTex == null) _dimTex = MakeTex(new Color(0f, 0f, 0f, 0.55f));
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _dimTex);
    }

    private static Texture2D MakeTex(Color c)
    {
        var tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { c, c, c, c });
        tex.Apply();
        return tex;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시뮬레이션 씬(perfectbackup0315)에서 ESC 누르면 뜨는 간단 팝업: Home / Exit.
/// 씬 안 빈 GameObject에 붙이면 됨. F1(도움말), F5(설정)와 겹치지 않게 ESC 전용.
/// </summary>
public class EscPauseMenu : MonoBehaviour
{
    [Tooltip("Home 눌렀을 때 돌아갈 메인메뉴 씬 이름")]
    public string mainMenuSceneName = "Main";

    private bool _showMenu = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // 1순위: 지금 마우스에 뭔가 잡혀있으면(AI 홀로그램 배치 중 / 드래그 중) 그것부터 취소
            bool cancelledSomething = false;

            if (AI_Builder.Instance != null && AI_Builder.Instance.isAiHologramActive)
            {
                AI_Builder.Instance.CancelHolograms();
                cancelledSomething = true;
            }

            var dragController = FindFirstObjectByType<SimulationDragController>();
            if (dragController != null && dragController.IsActive)
            {
                dragController.CancelDrag();
                cancelledSomething = true;
            }

            // 2순위: 취소할 게 없었을 때만 메뉴 토글
            if (!cancelledSomething)
            {
                _showMenu = !_showMenu;
            }
        }
    }

    void OnGUI()
    {
        if (!_showMenu) return;

        GUI.skin = UIStyleKit.Skin;

        // 7버튼: Settings(F5) / 키 오버레이(F2) / 콘솔(F3) / 도면 UI(O) / 보강계획 생성(Y) / Home / Exit
        float w = 280f, h = 56f, gap = 12f;
        int count = 7;
        float totalH = h * count + gap * (count - 1);
        float cx = Screen.width / 2f - w / 2f;
        float cy = Screen.height / 2f - totalH / 2f;

        // 반투명 배경으로 뒤쪽 시뮬레이션 살짝 가려서 팝업 느낌 주기
        GUI.Box(new Rect(cx - 30, cy - 50, w + 60, totalH + 90), "일시정지");

        float y = cy;

        if (GUI.Button(new Rect(cx, y, w, h), "Settings (F5)"))
        {
            if (SimulationSettingsUIManager.Instance != null)
                SimulationSettingsUIManager.Instance.TogglePanel();
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "키 오버레이 (F2)"))
        {
            if (KeyboardOverlayUI.Instance != null)
                KeyboardOverlayUI.Instance.CycleOverlay();
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "콘솔 (F3)"))
        {
            if (InGameConsole.Instance != null)
                InGameConsole.Instance.ToggleConsole();
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "도면 UI (O)"))
        {
            if (BlueprintTargetGenerator.Instance != null)
                BlueprintTargetGenerator.Instance.ToggleBlueprintUI();
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "보강계획 생성 (Y)"))
        {
            if (ReinforcementManager.Instance != null)
                ReinforcementManager.Instance.CreatePlanExcel();
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "Home"))
        {
            _showMenu = false;
            SceneManager.LoadScene(mainMenuSceneName);
        }
        y += h + gap;

        if (GUI.Button(new Rect(cx, y, w, h), "Exit"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
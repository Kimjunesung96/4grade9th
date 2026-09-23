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
            _showMenu = !_showMenu;
        }
    }

    void OnGUI()
    {
        if (!_showMenu) return;

        GUI.skin = UIStyleKit.Skin;

        float w = 280f, h = 60f, gap = 16f;
        float cx = Screen.width / 2f - w / 2f;
        float cy = Screen.height / 2f - (h * 2 + gap) / 2f;

        // 반투명 배경으로 뒤쪽 시뮬레이션 살짝 가려서 팝업 느낌 주기
        GUI.Box(new Rect(cx - 30, cy - 50, w + 60, h * 2 + gap + 90), "일시정지");

        if (GUI.Button(new Rect(cx, cy, w, h), "Home"))
        {
            _showMenu = false;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        if (GUI.Button(new Rect(cx, cy + h + gap, w, h), "Exit"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
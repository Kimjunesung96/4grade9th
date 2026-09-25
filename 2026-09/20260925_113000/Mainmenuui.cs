using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 최소 메인 메뉴: Start / Settings / Exit
/// UIStyleKit 스킨을 그대로 재사용해서 시뮬레이션 UI와 톤을 맞춤.
/// 새로 만드는 MainMenu 씬의 빈 오브젝트에 붙이면 됨.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Tooltip("Start 눌렀을 때 로드할 씬 이름 (빌드 설정에 등록되어 있어야 함)")]
    public string gameSceneName = "perfectbackup0315";

    [Tooltip("웹사이트 버튼 눌렀을 때 열릴 URL")]
    public string websiteUrl = "https://4grade9th-web-cxpi.vercel.app/";

    private bool _showSettings = false;

    void OnGUI()
    {
        GUI.skin = UIStyleKit.Skin;

        float w = 320f, h = 60f, gap = 20f;
        float cx = Screen.width / 2f - w / 2f;
        float cy = Screen.height / 2f - (h * 4 + gap * 3) / 2f;

        GUI.Label(new Rect(cx, cy - 100, w, 60), "프로젝트 이름", UIStyleKit.Skin.customStyles.Length > 0 ? GUI.skin.label : GUI.skin.label);

        if (!_showSettings)
        {
            if (GUI.Button(new Rect(cx, cy, w, h), "Start"))
            {
                SceneManager.LoadScene(gameSceneName);
            }

            if (GUI.Button(new Rect(cx, cy + h + gap, w, h), "Homepage"))
            {
                Application.OpenURL(websiteUrl);
            }

            if (GUI.Button(new Rect(cx, cy + (h + gap) * 2, w, h), "Settings"))
            {
                _showSettings = true;
            }

            if (GUI.Button(new Rect(cx, cy + (h + gap) * 3, w, h), "Exit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
        else
        {
            // 실제 설정은 SimulationSettingsUIManager가 시뮬레이션 씬 안에서 F5로 처리함.
            // 메뉴는 그 안내만 보여줌 (씬이 달라서 여기선 실제 설정값에 접근 불가).
            GUI.Label(new Rect(cx, cy, w, h), "게임 시작 후 F5 키로\n설정을 열 수 있습니다.");

            if (GUI.Button(new Rect(cx, cy + h + gap, w, h), "Back"))
            {
                _showSettings = false;
            }
        }
    }
}
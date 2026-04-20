using UnityEngine;

public class F1HelpManual : MonoBehaviour
{
    [Header("여기에 아까 만든 HelpPanel을 끌어다 넣으세요")]
    public GameObject helpUI;

    void Update()
    {
        // UI가 연결되어 있지 않으면 무시
        if (helpUI == null) return;

        // F1 키를 '누르고 있는 동안만' UI를 켜고, 떼면 끕니다! (오버워치 방식)
        if (Input.GetKeyDown(KeyCode.F1))
        {
            helpUI.SetActive(true);
        }
        else if (Input.GetKeyUp(KeyCode.F1))
        {
            helpUI.SetActive(false);
        }
    }
}
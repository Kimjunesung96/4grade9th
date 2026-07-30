using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

// ⭐ [신규 파일] 기존 시스템은 전혀 건드리지 않고, 밖에서 상태만 읽어서
//    화면에 키보드 레이아웃 + 지금 쓸 수 있는 키를 진하게 표시하는 오버레이.
//
//    - isBMode(진동)/isNMode(폭발): VibrationTestSystem.IsBModeActive / ShockwaveTestSystem.IsNModeActive
//      가 이미 public static이라 그대로 읽음 (수정 없음)
//    - CurrentMode(0~6): BuilderStateData ECS 싱글톤을 쿼리해서 읽음 (수정 없음)
//    - 건설 여부: BlockTag 붙은 엔티티가 있는지 세어봄 (수정 없음)
//    - isOMode/isLMode/isUMode/isYMode: SpawnerSystem.cs 안에 private라서
//      외부에서 읽을 방법이 없음 → 일단 수동 토글 버튼으로 대체.
//      나중에 SpawnerSystem.cs 쪽 필드를 public으로만 바꿔주면 여기 자동 감지로 교체 가능.
public class KeyboardOverlayUI : MonoBehaviour
{
    public int overlayState = 1; // 0=꺼짐, 1=오버레이만, 2=오버레이+키 설명 텍스트
    public KeyCode toggleKey = KeyCode.F2; // F1/F5는 이미 다른 용도라 F2로 둠. 원하는 키로 바꿔도 됨.

    // 수동 토글 (SpawnerSystem 내부 상태를 외부에서 못 읽어서 대체용)
    private bool manualBlueprintMode = false; // O/L/U 모드 흉내
    private bool manualYMode = false;         // Y모드(보강 CSV 로딩) 흉내

    private struct KeyBox
    {
        public string id;
        public string label;
        public Rect rect; // 0~1330 x 0~720 기준 좌표(원본 배치 이미지 기준)
        public KeyBox(string id, string label, float x, float y, float w, float h)
        { this.id = id; this.label = label; this.rect = new Rect(x, y, w, h); }
    }

    private readonly List<KeyBox> keys = new List<KeyBox>
    {
        new KeyBox("F1", "F1", 40, 20, 80, 70),
        new KeyBox("F5", "f5", 135, 20, 80, 70),
        new KeyBox("Y", "y", 475, 20, 60, 70),
        new KeyBox("O", "o", 735, 20, 60, 70),
        new KeyBox("U", "u", 810, 20, 60, 70),
        new KeyBox("R", "r", 885, 20, 60, 70),
        new KeyBox("Alpha1", "1", 910, 100, 60, 70),
        new KeyBox("V", "v", 20, 130, 60, 70),
        new KeyBox("Q", "q", 280, 130, 60, 70),
        new KeyBox("E", "e", 590, 130, 60, 70),
        new KeyBox("Wheel", "휠", 700, 130, 60, 250),
        new KeyBox("Alpha2", "2", 910, 180, 60, 70),
        new KeyBox("B", "b", 20, 210, 60, 70),
        new KeyBox("G", "g", 780, 250, 60, 70),
        new KeyBox("Alpha3", "3", 910, 260, 60, 70),
        new KeyBox("N", "n", 20, 290, 60, 70),
        new KeyBox("Alpha4", "4", 910, 340, 60, 70),
        new KeyBox("Alpha5", "5", 910, 420, 60, 70),
        new KeyBox("Space", "Space", 280, 450, 400, 60),
        new KeyBox("F", "f", 715, 450, 60, 60),
        new KeyBox("Alpha6", "6", 910, 500, 60, 70),
        new KeyBox("H", "h", 20, 370, 60, 70),
        new KeyBox("Delete", "del", 20, 450, 60, 70),
        new KeyBox("Enter", "enter", 1000, 580, 100, 60),
    };

    private readonly HashSet<string> alwaysActive = new HashSet<string> { "F1", "F5", "Y", "O", "U", "Q", "E", "Space", "G" };

    private readonly Dictionary<string, string> keyDescriptions = new Dictionary<string, string>
    {
        { "F1", "도움말" },
        { "F5", "설정창" },
        { "Y", "예산/보강 UI (연타시 순환)" },
        { "O", "블루프린트 UI 토글" },
        { "U", "마지막 건물 불러오기" },
        { "R", "블록 청소/취소" },
        { "Q", "카메라·설계도 회전(좌)" },
        { "E", "카메라·설계도 회전(우)" },
        { "Wheel", "층수/가이드 높이/진도 조절" },
        { "V", "응력 스캔" },
        { "B", "진동 테스트" },
        { "N", "폭발 테스트" },
        { "G", "고스트 확정/빌드" },
        { "F", "미리보기" },
        { "Space", "젯팩(공중 이동)" },
        { "H", "블록 보호 토글" },
        { "Delete", "블록 즉시 삭제" },
        { "Enter", "가이드 확정" },
        { "Alpha1", "벽(통짜) 건설" },
        { "Alpha2", "빈 프레임(윤곽만) 건설" },
        { "Alpha3", "원형 패턴 건설" },
        { "Alpha4", "피라미드 건설" },
        { "Alpha5", "원뿔 건설" },
        { "Alpha6", "미사용(예약)" },
    };

    private readonly Dictionary<string, string> blueprintGridDescriptions = new Dictionary<string, string>
    {
        { "Alpha1", "그리드 크기 10" },
        { "Alpha2", "그리드 크기 20" },
        { "Alpha3", "그리드 크기 30" },
        { "Alpha4", "그리드 크기 40" },
        { "Alpha5", "그리드 크기 50" },
    };

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) overlayState = (overlayState + 1) % 3;
    }

    private HashSet<string> ComputeActiveKeys()
    {
        var active = new HashSet<string>(alwaysActive);

        bool isBMode = VibrationTestSystem.IsBModeActive;
        bool isNMode = ShockwaveTestSystem.IsNModeActive;

        int currentMode = 1;
        bool hasBuilderState = false;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<BuilderStateData>());
            if (!query.IsEmpty)
            {
                currentMode = query.GetSingleton<BuilderStateData>().CurrentMode;
                hasBuilderState = true;
            }
        }

        bool built = false;
        if (world != null && world.IsCreated)
        {
            var em = world.EntityManager;
            var blockQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BlockTag>());
            built = !blockQuery.IsEmpty;
        }

        bool isAnySpecialMode = manualBlueprintMode || manualYMode;

        // 테스트 모드가 최우선 (실제 코드에서도 !isBMode && !isNMode 가드가 대부분이라 상호 배타적으로 취급)
        // G는 실제 코드상 진동/폭발 테스트 진행중엔 꺼짐 (isBuildGEnabled && !isBMode && !isNMode 가드)
        if (isBMode) { active.Add("Wheel"); active.Remove("G"); return active; }
        if (isNMode) { active.Remove("G"); return active; }

        // 블루프린트/Y모드 (수동 토글) — F/G, 그리고 블루프린트일 땐 휠도
        if (manualBlueprintMode) { active.Add("F"); active.Add("G"); active.Add("Wheel"); }
        if (manualYMode) { active.Add("F"); active.Add("G"); }

        if (!isAnySpecialMode)
        {
            if (hasBuilderState && currentMode == 0)
            {
                active.Add("H"); active.Add("Delete"); active.Add("R");
                active.Add("Alpha1"); active.Add("Alpha2"); active.Add("Alpha3");
                active.Add("Alpha4"); active.Add("Alpha5"); active.Add("Alpha6");
            }
            else
            {
                active.Add("Alpha1"); active.Add("Alpha2"); active.Add("Alpha3");
                active.Add("Alpha4"); active.Add("Alpha5"); active.Add("Alpha6");
                active.Add("Wheel"); active.Add("Enter");
            }

            if (built) { active.Add("R"); active.Add("V"); active.Add("B"); active.Add("N"); }
        }

        return active;
    }

    void OnGUI()
    {
        if (overlayState == 0) return;

        bool showLabels = overlayState == 2;
        float panelW = showLabels ? 1180f : 780f, panelH = showLabels ? 620f : 460f;
        float ox = 20f, oy = Screen.height - panelH - 20f;

        GUI.Box(new Rect(ox, oy, panelW, panelH), showLabels ? "키보드 (F2: 끄기)" : "키보드 (F2: 설명 보기)");

        var active = ComputeActiveKeys();
        bool isAnySpecial = manualBlueprintMode || manualYMode;

        var keyStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold };
        var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false, normal = { textColor = Color.white } };

        Color prevColor = GUI.color;
        foreach (var k in keys)
        {
            float sx = ox + (k.rect.x / 1330f) * panelW;
            float sy = oy + 32f + (k.rect.y / 720f) * (panelH - 56f);
            float sw = (k.rect.width / 1330f) * panelW;
            float sh = (k.rect.height / 720f) * (panelH - 56f);

            bool isActive = active.Contains(k.id);
            GUI.color = isActive ? new Color(0.35f, 0.65f, 1f, 1f) : new Color(1f, 1f, 1f, 0.25f);
            GUI.Box(new Rect(sx, sy, sw, sh), k.label, keyStyle);

            // ⭐ [F2 3단계] 설명 모드일 때, 활성화된 키 옆(오른쪽)에 기능 설명을 짧게 표시.
            //    1~5번 키는 상황에 따라 뜻이 달라짐: 블루프린트/Y모드 중엔 "그리드 크기", 아니면 "건설 모양"
            if (showLabels && isActive)
            {
                string desc = null;
                if (isAnySpecial && blueprintGridDescriptions.ContainsKey(k.id)) desc = blueprintGridDescriptions[k.id];
                else keyDescriptions.TryGetValue(k.id, out desc);

                if (!string.IsNullOrEmpty(desc))
                {
                    Color prevColor2 = GUI.color;
                    GUI.color = Color.white;
                    GUI.Label(new Rect(sx + sw + 8f, sy, 220f, sh), desc, labelStyle);
                    GUI.color = prevColor2;
                }
            }
        }
        GUI.color = prevColor;

        GUI.Box(new Rect(ox, oy + panelH + 4f, panelW, 60f), "");
        manualBlueprintMode = GUI.Toggle(new Rect(ox + 8f, oy + panelH + 10f, 240f, 24f), manualBlueprintMode, " 블루프린트 모드 (O/L/U) 흉내");
        manualYMode = GUI.Toggle(new Rect(ox + 8f, oy + panelH + 34f, 240f, 24f), manualYMode, " Y모드(보강 로딩) 흉내");
    }
}
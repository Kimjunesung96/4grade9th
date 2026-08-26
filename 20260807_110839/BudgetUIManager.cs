using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

public class BudgetUIManager : MonoBehaviour
{
    public static BudgetUIManager Instance;

    public static int yKeyState = 0;
    private bool showPanel = false;

    private int selectedTab = 0;

    private bool wallIsWood = false;
    private string selectedWallMaterial = "";

    private bool floorIsWood = false;
    private string selectedFloorMaterial = "";

    private string budgetInput = "500000";
    private string floorCountInput = "1";
    public bool wantsReinforcement = false;
    
    private bool pendingRemoveReinforcements = false;
    
    private bool isCheapON = false;
    private bool isExpensiveON = false;

    private List<string> woodMaterials = new List<string>();
    private List<string> nonWoodMaterials = new List<string>();

    private Vector2 wallScrollPos = Vector2.zero;
    private Vector2 floorScrollPos = Vector2.zero;

    private string csvPath;
    private string metaPath;

    public int reinforcementMode = 1; 

    public string reinforcementMaterial = "H_Beam";
    private static readonly string[] reinforcementMaterialOptions = new string[] { "H_Beam" };

    private class BlockData
    {
        public string[] Cols;
        public float Stress;
        public string Type;
        public string MatName;
        public float Price;
        public float Tensile;
        public float Compressive;
        public string Tool; 
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        csvPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        metaPath = Path.Combine(Application.dataPath, "StressBlock", "Budget_Meta.csv");
        LoadMaterialLists();
    }

    void Update()
    {
        if (pendingRemoveReinforcements)
        {
            pendingRemoveReinforcements = false;
            ExecuteRemoveReinforcements();
        }
    }

    void LoadMaterialLists()
    {
        woodMaterials.Clear();
        nonWoodMaterials.Clear();
        if (MaterialDataManager.Instance == null) return;

        foreach (var kv in MaterialDataManager.Instance.MaterialDict)
        {
            string name = kv.Key.ToLower();
            if (name.Contains("wood") || name.Contains("timber")) woodMaterials.Add(kv.Key);
            else nonWoodMaterials.Add(kv.Key);
        }

        if (string.IsNullOrEmpty(selectedWallMaterial) && nonWoodMaterials.Count > 0)
            selectedWallMaterial = nonWoodMaterials.First();
        if (string.IsNullOrEmpty(selectedFloorMaterial) && nonWoodMaterials.Count > 0)
            selectedFloorMaterial = nonWoodMaterials.First();
    }

    public void OnYKeyPressed()
    {
        yKeyState = (yKeyState + 1) % 3;
        if (yKeyState == 0)
        {
            showPanel = false;
            SpawnerSystem.isUMode = false;
            SpawnerSystem.isOMode = false;
            SpawnerSystem.isLMode = false;
        }
        else if (yKeyState == 1)
        {
            showPanel = false;
            SpawnerSystem.isUMode = true;
            var gen = FindFirstObjectByType<BlueprintTargetGenerator>();
            if (gen != null) gen.LoadLastBuildingForUMode();
        }
        else if (yKeyState == 2)
        {
            SpawnerSystem.isUMode = false;
            showPanel = true;
            isCheapON = false;
            isExpensiveON = false;
            LoadMaterialLists();
        }
    }

    public void SetReinforcementModeMany()
    {
        reinforcementMode = 1;
        UnityEngine.Debug.Log("보강 모드: 많이 (바둑판 물량 공세)");
    }

    public void SetReinforcementModeFew()
    {
        reinforcementMode = 2;
        UnityEngine.Debug.Log("보강 모드: 적게 (가성비 핀포인트)");
    }

    void OnGUI()
    {
        if (!showPanel) return;

        float panelW = 860f;
        float panelH = 580f;
        float panelX = (Screen.width - panelW) / 2f;
        float panelY = (Screen.height - panelH) / 2f;

        GUILayout.BeginArea(new Rect(panelX, panelY, panelW, panelH), GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = selectedTab == 0 ? Color.white : Color.gray;
        if (GUILayout.Button("벽 + 지붕", GUILayout.Height(40))) selectedTab = 0;
        GUI.backgroundColor = selectedTab == 1 ? Color.white : Color.gray;
        if (GUILayout.Button("바닥", GUILayout.Height(40))) selectedTab = 1;
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        if (selectedTab == 0) DrawMaterialTab(ref wallIsWood, ref selectedWallMaterial, ref wallScrollPos, "벽+지붕");
        else DrawMaterialTab(ref floorIsWood, ref selectedFloorMaterial, ref floorScrollPos, "바닥");

        GUILayout.Space(8);
        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        GUILayout.Space(4);

        GUILayout.BeginHorizontal();
        GUILayout.Label("층수", GUILayout.Width(50));
        floorCountInput = GUILayout.TextField(floorCountInput, GUILayout.Width(60));
        GUILayout.Label("(스크롤 복사)", GUILayout.Width(100));
        GUILayout.Space(20);
        GUILayout.Label("보강재", GUILayout.Width(60));

        GUI.backgroundColor = wantsReinforcement ? Color.cyan : Color.gray;
        if (GUILayout.Button("YES", GUILayout.Width(70))) wantsReinforcement = true;

        GUI.backgroundColor = !wantsReinforcement ? Color.cyan : Color.gray;
        if (GUILayout.Button("NO", GUILayout.Width(70))) wantsReinforcement = false;

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        if (wantsReinforcement)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(230); 
            GUILayout.Label("보강 모드", GUILayout.Width(65));
            
            GUI.backgroundColor = (reinforcementMode == 1) ? new Color(1f, 0.6f, 0.6f) : Color.gray;
            if (GUILayout.Button("많이 [격자]", GUILayout.Width(80))) reinforcementMode = 1;
            
            GUI.backgroundColor = (reinforcementMode == 2) ? new Color(0.6f, 0.8f, 1f) : Color.gray;
            if (GUILayout.Button("적게 [우산]", GUILayout.Width(80))) reinforcementMode = 2;
            
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(230);
            GUILayout.Label("보강재질", GUILayout.Width(65));

            foreach (string matOption in reinforcementMaterialOptions)
            {
                GUI.backgroundColor = (reinforcementMaterial == matOption) ? new Color(0.6f, 1f, 0.6f) : Color.gray;
                if (GUILayout.Button(matOption, GUILayout.Width(80))) reinforcementMaterial = matOption;
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }

        GUILayout.FlexibleSpace(); 

        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical();
        GUILayout.Space(25);
        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("just보강", GUILayout.Height(55), GUILayout.Width(130))) OnJustReinforce();
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label("자동 측정 예산 (원):", GUILayout.Width(130));
        budgetInput = GUILayout.TextField(budgetInput, GUILayout.Width(170));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = isCheapON ? new Color(0.6f, 1f, 0.6f) : Color.gray;
        if (GUILayout.Button(isCheapON ? "싸게 [자동 ON]\n최소 안전 확보" : "싸게 [OFF]", GUILayout.Height(50), GUILayout.Width(150)))
        {
            isCheapON = !isCheapON;
            if (isCheapON) isExpensiveON = false;
        }
        GUI.backgroundColor = isExpensiveON ? new Color(1f, 0.85f, 0.4f) : Color.gray;
        if (GUILayout.Button(isExpensiveON ? "비싸게 [자동 ON]\n예산 내 최고 강도" : "비싸게 [OFF]", GUILayout.Height(50), GUILayout.Width(150)))
        {
            isExpensiveON = !isExpensiveON;
            if (isExpensiveON) isCheapON = false;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        GUILayout.BeginVertical();
        GUILayout.Space(25);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("보강재 초기화\n(철거)", GUILayout.Height(55), GUILayout.Width(130))) 
        {
            pendingRemoveReinforcements = true; 
        }
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();

        GUILayout.BeginVertical();
        GUILayout.Space(25);
        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
        if (GUILayout.Button("확인\n(타설 준비)", GUILayout.Height(55), GUILayout.Width(150))) OnConfirm();
        GUILayout.EndVertical();

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.EndArea();
    }

    private void DrawMaterialTab(ref bool isWood, ref string selected, ref Vector2 scroll, string label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("(" + label + ") 재질 (수동)", GUILayout.Width(130));
        GUI.backgroundColor = !isWood ? Color.white : Color.gray;
        if (GUILayout.Button("비목재", GUILayout.Width(100))) isWood = false;
        GUI.backgroundColor = isWood ? Color.white : Color.gray;
        if (GUILayout.Button("목재", GUILayout.Width(100))) isWood = true;
        GUI.backgroundColor = Color.white;
        GUILayout.Label("  선택됨: " + selected, GUILayout.Width(220));
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        List<string> list = isWood ? woodMaterials : nonWoodMaterials;
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(160));
        foreach (var mat in list)
        {
            GUI.backgroundColor = selected == mat ? Color.cyan : Color.white;
            if (GUILayout.Button(mat, GUILayout.Height(32)))
                selected = mat;
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndScrollView();
    }

    // =========================================================================================
    // 🚀 [십장님 알고리즘 완벽 적용] 유니티 스포너 대신 CSV 단계에서 먼저 중복을 숙청합니다!
    // =========================================================================================
    private void MergeToAfterReinforce()
    {
        string basePath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        string planPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        string afterPath = Path.Combine(Application.dataPath, "StressBlock", "after_reinforce.csv");

        string header = "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type";
        List<string> allRawLines = new List<string>();

        // 1. "두 파일을 에프러리인포스에서 그대로 합쳐" -> 둘 다 무식하게 읽어들입니다.
        if (File.Exists(basePath))
        {
            var lines = File.ReadAllLines(basePath);
            for (int i = 1; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i])) allRawLines.Add(lines[i]);
        }
        if (File.Exists(planPath))
        {
            var lines = File.ReadAllLines(planPath);
            for (int i = 1; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i])) allRawLines.Add(lines[i]);
        }

        // ID를 기준으로 그룹화
        Dictionary<string, List<string>> groupedById = new Dictionary<string, List<string>>();
        foreach (var line in allRawLines)
        {
            var cols = line.Split(',');
            if (cols.Length < 12) continue;
            string rawId = cols[0];

            // ⭐ 원본 블록의 미세한 소수점 오차를 무시하고 3m 격자(Grid)에 맞춘 완벽한 ID(cleanId) 생성
            float px = float.Parse(rawId.Split('_')[0]) / 10f;
            float pz = float.Parse(rawId.Split('_')[1]) / 10f;
            float py = float.Parse(rawId.Split('_')[2]) / 10f;

            // ⭐ [구멍 버그 수정] SpawnerSystem.SaveLastBuildingSnapshot()과 반올림 방식이 달라서
            //    (여기는 Mathf.Round, 저기는 floor+0.5) 경계값에서 서로 다른 두 블록이 같은 cleanId로
            //    잘못 충돌 → 아래 dedup 로직이 "겹친 것"으로 착각해 둘 중 하나를 영구 삭제 → 건물에 구멍.
            //    SaveLastBuildingSnapshot과 완전히 동일한 floor+0.5 공식으로 통일해서 충돌 자체를 방지.
            float snapX = Mathf.Floor((px - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            float snapZ = Mathf.Floor((pz - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            float snapY = Mathf.Floor((py - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            
            float ix = Mathf.Round((snapX + 0.001f) * 10f);
            float iz = Mathf.Round((snapZ + 0.001f) * 10f);
            float iy = Mathf.Round((snapY + 0.001f) * 10f);
            string cleanId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

            if (!groupedById.ContainsKey(cleanId)) groupedById[cleanId] = new List<string>();
            groupedById[cleanId].Add(line);
        }

        List<string> finalList = new List<string> { header };
        foreach (var kvp in groupedById)
        {
            var linesForId = kvp.Value;
            if (linesForId.Count > 1) 
            {
                // 2. "그다음에 id가 같은 놈들 중에서 reinforce가 들어가 있는 줄을 지워버려!"
                // 즉, "Reinforcement"가 아닌 원본 줄을 찾아서 그것만 살립니다.
                var originals = linesForId.Where(l => l.Split(',')[10].Trim() != "Reinforcement").ToList();
                if (originals.Count > 1)
                {
                    // ⭐ [안전장치] 원본끼리 충돌하는 건 정상 상황이 아님(스냅 공식 통일로 이제 거의 안 나야 함).
                    //    조용히 버리면 다음에 또 구멍 버그가 나도 못 알아채니, 무조건 로그로 남긴다.
                    Debug.LogWarning($"⚠ [Merge] 원본 블록끼리 같은 격자 ID({kvp.Key})에서 충돌! {originals.Count}개 중 1개만 살리고 나머지는 버립니다. 좌표 스냅 로직을 다시 확인하세요.");
                }
                string originalLine = originals.FirstOrDefault();
                if (originalLine != null) 
                    finalList.Add(originalLine); // 원본 승리! (보강재 줄은 영원히 삭제됨)
                else 
                    finalList.Add(linesForId[0]);
            }
            else
            {
                // 안 겹친 정상 블록들은 그대로 통과!
                finalList.Add(linesForId[0]);
            }
        }

        // 3. 완벽하게 필터링된 완성본을 저장합니다.
        File.WriteAllLines(afterPath, finalList);
        File.WriteAllLines(csvPath, finalList);
        
        // ⭐ Last_Building.csv 덮어쓰기 코드 삭제 완료! 원본은 더이상 오염되지 않습니다.
        Debug.Log("🏭 [데이터 전처리 완료] 겹치는 보강재를 CSV단에서 완벽히 삭제하고 after_reinforce.csv를 완성했습니다!");
    }

    void OnConfirm()
    {
        float budget = float.TryParse(budgetInput, out float b) ? b : float.MaxValue;
        int floors = int.TryParse(floorCountInput, out int f) ? f : 1;

        string currentMode = "Manual";
        if (isCheapON) currentMode = "Cheap";
        if (isExpensiveON) currentMode = "Expensive";

        // ⭐ [신규] 싸게/비싸게가 켜지면 화면에 남아있는 수동 옵션(보강 YES/NO, 보강 모드, 보강재질,
        //    벽/바닥 수동 재질 탭)은 전부 무시하고 이 두 모드의 자동 로직만 그대로 적용한다.
        //    - 싸게: 스트레스 계산해서 가장 싼 재질/가장 싼 보강재로, "적게(가성비 핀포인트)" 방식만 최소 적용
        //    - 비싸게: 예산(가격)을 무시하고 가장 강한 재질/가장 강한 보강재로, "많이(격자)" 방식으로 최대 안정성 확보
        //    ⚠️ 벽/바닥 수동 재질(selectedWallMaterial/selectedFloorMaterial)은 ApplyMaterialsToScene의
        //       Cheap/Expensive 분기가 애초에 참조하지 않으므로 이미 자동 무시됨 (Manual 모드에서만 사용).
        if ((isCheapON || isExpensiveON) && MaterialDataManager.Instance != null)
        {
            var dict = MaterialDataManager.Instance.MaterialDict;
            wantsReinforcement = true;

            if (isCheapON)
            {
                reinforcementMaterial = dict.OrderBy(kv => kv.Value.Density * kv.Value.PricePerKg).First().Key;
                reinforcementMode = 2; // 적게 [우산] — 가성비 핀포인트
            }
            else // isExpensiveON
            {
                reinforcementMaterial = dict.OrderByDescending(kv => math.min(kv.Value.Tensile, kv.Value.Compressive)).First().Key;
                reinforcementMode = 1; // 많이 [격자] — 가격 무시하고 최대 안정성
            }
        }

        SaveBudgetMeta(budget, currentMode, floors);
        ApplyMaterialsToScene(currentMode, budget);

        // ⭐ [신규] 싸게/비싸게 공통: 재질 배정 끝난 뒤, 위에서 강제로 켠 wantsReinforcement +
        //    자동 선택된 reinforcementMode/reinforcementMaterial 그대로 보강 계획을 자동 생성.
        //    (CurrentStress.csv의 RiskLevel은 이미 계산되어 있는 값을 그대로 재사용 —
        //     ReinforcementManager.CreatePlanExcel()이 Danger/Quake_Danger 포인트만 골라 보강탑을 설계함)
        if (currentMode == "Cheap" || currentMode == "Expensive")
        {
            var reinforcer = UnityEngine.Object.FindFirstObjectByType<ReinforcementManager>();
            if (reinforcer != null)
            {
                reinforcer.CreatePlanExcel();
                string label = currentMode == "Cheap" ? "💰 [싸게 모드] 최저가 재질 + 최소(적게) 보강" : "💎 [비싸게 모드] 가격 무시 최강 재질 + 최대(많이) 보강";
                Debug.Log($"{label} 자동 적용 완료 (Y로 다시 열람 가능)");
            }
            else
            {
                Debug.LogWarning($"⚠ [{currentMode} 모드] ReinforcementManager를 씬에서 찾지 못해 자동 보강 계획을 생성하지 못했습니다.");
            }
        }

        // ⭐ [증발 버그 수정] 씬을 지우기 전에, 방금 G로 지은 것까지 포함해서
        // Last_Building.csv를 무조건 먼저 확정 저장한다. (backupIDToQuery 예약을 그냥
        // -1f로 취소해버리면 마지막 건설분이 원본 장부에 한 번도 안 남고 사라짐)
        SpawnerSystem.SaveLastBuildingSnapshot(Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager);

        var rm = FindFirstObjectByType<ReinforcementManager>();
        if (rm != null) rm.CreatePlanExcel();

        // 1. CSV 데이터 전처리 실행
        MergeToAfterReinforce();

        // 2. 씬 완전 철거 (전체 건물을 다시 예쁘게 깔아야 하므로 기존 찌꺼기 폭파)
        SpawnerSystem.backupIDToQuery = -1f;
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));

        showPanel = false;
        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        
        // 3. 스포너에게 복잡한 논리 시키지 않고, 오직 '완성본(after_reinforce.csv)'만 띄우라고 지시!
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "after_reinforce.csv"; // ⭐ 핵심: 완성본 장전!
        SpawnerSystem.loadDelayTimer = 5f;

        Debug.Log("✅ [장전 완료] 보강재가 결합된 '완전체 건물' 홀로그램을 화면에 띄웁니다! (G키로 타설하세요)");
    }

    void OnJustReinforce()
    {
        // ⭐ [증발 버그 수정] 씬을 지우기 전에 마지막 G까지 반드시 Last_Building.csv에 반영
        SpawnerSystem.SaveLastBuildingSnapshot(Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager);

        // 1. CSV 데이터 전처리 실행
        MergeToAfterReinforce();

        // 2. 씬 완전 철거
        SpawnerSystem.backupIDToQuery = -1f;
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));

        yKeyState = 0;
        showPanel = false;
        SpawnerSystem.isUMode = false;
        
        // 3. 스포너에게 복잡한 논리 시키지 않고, 오직 '완성본(after_reinforce.csv)'만 띄우라고 지시!
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "after_reinforce.csv"; // ⭐ 핵심: 완성본 장전!
        SpawnerSystem.loadDelayTimer = 5f;
        
        Debug.Log("(just보강) 보강재가 결합된 완전체 건물 장전 완료!");
    }

    void SaveBudgetMeta(float budget, string mode, int floors)
    {
        string header = "Budget,Mode,Floors,WallMaterial,FloorMaterial,Reinforce";
        string row = budget + "," + mode + "," + floors + "," + selectedWallMaterial + "," + selectedFloorMaterial + "," + (wantsReinforcement ? "YES" : "NO");
        File.WriteAllText(metaPath, header + "\n" + row);
    }

    void ApplyMaterialsToScene(string mode, float budget)
    {
        if (!File.Exists(csvPath)) return;
        var lines = File.ReadAllLines(csvPath);
        if (MaterialDataManager.Instance == null) return;
        var dict = MaterialDataManager.Instance.MaterialDict;

        var cheapMats = dict.OrderBy(kv => kv.Value.Density * kv.Value.PricePerKg).ToList(); 
        var strongestMats = dict.OrderByDescending(kv => math.min(kv.Value.Tensile, kv.Value.Compressive)).ToList();
        var strongestMat = strongestMats.First();

        var output = new List<string> { lines.FirstOrDefault() };
        float totalCost = 0f;
        List<BlockData> blocks = new List<BlockData>();

        for (int i = 1; i < lines.Length; i++)
        {
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',');
            if (cols.Length < 12) { output.Add(currentLine); continue; }

            float stress = 0f;
            float.TryParse(cols[4], out stress);
            string type = cols[11];
            string tool = cols[10].Trim(); 

            BlockData b = new BlockData { Cols = cols, Stress = stress, Type = type, Tool = tool, MatName = cols[7] };
            
            if (dict.TryGetValue(b.MatName, out var spec))
            {
                b.Tensile = spec.Tensile;
                b.Compressive = spec.Compressive;
                b.Price = spec.Density * spec.PricePerKg * 3.375f;
            }
            blocks.Add(b);
        }

        foreach (var b in blocks.Where(b => b.Tool == "Reinforcement"))
        {
            totalCost += b.Price;
        }

        var originalBlocks = blocks.Where(b => b.Tool != "Reinforcement").ToList();

        if (mode == "Manual")
        {
            foreach (var b in originalBlocks)
            {
                string matName = b.Type == "Floor" ? selectedFloorMaterial : selectedWallMaterial;
                b.MatName = matName;
                if (dict.TryGetValue(matName, out var spec))
                {
                    b.Tensile = spec.Tensile;
                    b.Compressive = spec.Compressive;
                    b.Price = spec.Density * spec.PricePerKg * 3.375f; 
                }
                totalCost += b.Price;
            }
        }
        else if (mode == "Cheap")
        {
            foreach (var b in originalBlocks)
            {
                var target = cheapMats.FirstOrDefault(m => math.min(m.Value.Tensile, m.Value.Compressive) >= b.Stress * 1.2f);
                if (string.IsNullOrEmpty(target.Key)) target = strongestMat;

                b.MatName = target.Key;
                b.Tensile = target.Value.Tensile;
                b.Compressive = target.Value.Compressive;
                b.Price = target.Value.Density * target.Value.PricePerKg * 3.375f;
                totalCost += b.Price;
            }
        }
        else if (mode == "Expensive")
        {
            foreach (var b in originalBlocks)
            {
                var target = cheapMats.FirstOrDefault(m => math.min(m.Value.Tensile, m.Value.Compressive) >= b.Stress * 1.2f);
                if (string.IsNullOrEmpty(target.Key)) target = strongestMat;

                b.MatName = target.Key;
                b.Tensile = target.Value.Tensile;
                b.Compressive = target.Value.Compressive;
                b.Price = target.Value.Density * target.Value.PricePerKg * 3.375f;
                totalCost += b.Price;
            }

            var sortedBlocks = originalBlocks.OrderByDescending(b => b.Stress).ToList();
            
            foreach (var b in sortedBlocks)
            {
                foreach (var strongMat in strongestMats)
                {
                    float newPrice = strongMat.Value.Density * strongMat.Value.PricePerKg * 3.375f;
                    
                    if (math.min(strongMat.Value.Tensile, strongMat.Value.Compressive) > math.min(b.Tensile, b.Compressive) 
                        && (totalCost - b.Price + newPrice) <= budget)
                    {
                        totalCost = totalCost - b.Price + newPrice;
                        b.MatName = strongMat.Key;
                        b.Tensile = strongMat.Value.Tensile;
                        b.Compressive = strongMat.Value.Compressive;
                        b.Price = newPrice;
                        break;
                    }
                }
            }
        }

        foreach (var b in blocks)
        {
            b.Cols[7] = b.MatName;
            b.Cols[8] = b.Tensile.ToString("F1");
            b.Cols[9] = b.Compressive.ToString("F1");
            output.Add(string.Join(",", b.Cols));
        }

        File.WriteAllLines(csvPath, output);
    }

    void ExecuteRemoveReinforcements()
    {
        string planPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        string afterPath = Path.Combine(Application.dataPath, "StressBlock", "after_reinforce.csv");
        if (File.Exists(planPath)) File.Delete(planPath);
        if (File.Exists(afterPath)) File.Delete(afterPath);

        // ⭐ [증발 버그 근본 수정] 예전엔 CurrentStress.csv(텍스트)를 그대로 베껴서 Last_Building.csv를
        // 만들었는데, CurrentStress.csv는 BlockDisplacement 등 특정 컴포넌트가 갖춰진 엔티티만
        // 매 프레임 다시 써지는 "일시적" 파일이라, 그 순간 컴포넌트가 아직 안 붙은 블록은
        // CurrentStress.csv에서 통째로 누락될 수 있음. 그 상태로 철거(전체 씬 삭제 후 재건축)하면
        // 그 블록은 영원히 사라짐. 대신 SaveLastBuildingSnapshot()처럼 "지금 살아있는 엔티티"를
        // 직접 조회해서 Last_Building.csv를 만들면, 살아있는 한 절대 누락되지 않음.
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        SpawnerSystem.SaveLastBuildingSnapshot(em);

        string stressBlockLastBuildPath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        if (!File.Exists(stressBlockLastBuildPath)) return;
        var cleanLines = File.ReadAllLines(stressBlockLastBuildPath).ToList();
        if (cleanLines.Count <= 1) return;

        File.WriteAllLines(csvPath, cleanLines);

        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string genPath = Path.Combine(projectPath, "BuildingLogs", "Last_Building.csv");
        if (!Directory.Exists(Path.GetDirectoryName(genPath))) Directory.CreateDirectory(Path.GetDirectoryName(genPath));
        File.WriteAllLines(genPath, cleanLines);

        var bpManagerForClear = FindFirstObjectByType<BlueprintManager>();
        if (bpManagerForClear != null) bpManagerForClear.ClearRuntimeCache();

        SpawnerSystem.backupIDToQuery = -1f;
        
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));
        
        if (LogManager.Instance != null) LogManager.Instance.OnPressRKey();

        showPanel = false;
        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "Last_Building.csv"; // 철거 시에는 순수 원본으로 복구
        SpawnerSystem.loadDelayTimer = 5f;

        var dragController = FindFirstObjectByType<SimulationDragController>();
        if (dragController != null) dragController.CancelDrag();

        Debug.Log("🗑️ [완벽 철거] 융합 파일 삭제 후, 순수한 원본 데이터로 건물을 재구축 대기 중!");
    }
}
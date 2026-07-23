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
    
    // ⭐ 철거 명령 예약용 깃발
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
        // ⭐ 예약된 철거 명령을 안전한 타이밍에 실행
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
        if (GUILayout.Button("보강재 초기화\n(테스트용)", GUILayout.Height(55), GUILayout.Width(130))) 
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

    void OnConfirm()
    {
        float budget = float.TryParse(budgetInput, out float b) ? b : float.MaxValue;
        int floors = int.TryParse(floorCountInput, out int f) ? f : 1;

        string currentMode = "Manual";
        if (isCheapON) currentMode = "Cheap";
        if (isExpensiveON) currentMode = "Expensive";

        SaveBudgetMeta(budget, currentMode, floors);
        ApplyMaterialsToScene(currentMode, budget);

        var rm = FindFirstObjectByType<ReinforcementManager>();
        if (rm != null) rm.CreatePlanExcel();

        showPanel = false;

        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        SpawnerSystem.loadDelayTimer = 5f;

        Debug.Log("✅ 설정 완료 적용 모드: " + currentMode + " / 보강 도면 장전 준비 완료!");
    }

    void OnJustReinforce()
    {
        yKeyState = 0;
        showPanel = false;
        SpawnerSystem.isUMode = false;
        SpawnerSystem.loadDelayTimer = 5f;
        Debug.Log("(just보강) 보강 도면 로드!");
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
        UnityEngine.Debug.Log("저장 완료 CurrentStress.csv 파일에 재질 변경 내역 저장 완료");

        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(BlockMaterial), typeof(OriginalPosition));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

        foreach (var ent in entities)
        {
            var pos = em.GetComponentData<OriginalPosition>(ent).Value;

            float ix = math.round(pos.x * 10f);
            float iy = math.round(pos.y * 10f);
            float iz = math.round(pos.z * 10f);

            string strX = (ix < 0f ? "-" : "0") + math.abs(ix).ToString("000");
            string strZ = (iz < 0f ? "-" : "0") + math.abs(iz).ToString("000");
            string strY = (iy < 0f ? "-" : "0") + math.abs(iy).ToString("000");
            string id = strX + "_" + strZ + "_" + strY;

            var targetBlock = blocks.FirstOrDefault(b => b.Cols[0] == id);
            if (targetBlock != null)
            {
                var newMat = new BlockMaterial
                {
                    MaterialName = targetBlock.MatName,
                    TensileStiffness = targetBlock.Tensile,
                    CompressiveStiffness = targetBlock.Compressive
                };
                em.SetComponentData(ent, newMat);
            }
        }
        entities.Dispose();
    }

    // ⭐ [이름 정상 변경됨] 안전한 타이밍에 찌꺼기 없이 실행됩니다!
void ExecuteRemoveReinforcements()
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogWarning("⚠️ CurrentStress.csv 파일이 없습니다.");
            return;
        }

        // ⭐ 제1원칙, 제3원칙: CurrentStress.csv와 Reinforcement_Plan.csv 양쪽 다 보강재 찌꺼기를 박멸합니다!
        string planPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        if (File.Exists(planPath))
        {
            File.Delete(planPath);
            Debug.Log("🗑️ [제1원칙] 보강 도면 파일(Reinforcement_Plan.csv) 완전 삭제 완료!");
        }

        var lines = File.ReadAllLines(csvPath).ToList();
        if (lines.Count <= 1) return;

        // ⭐ 제2원칙: ID를 유일한 키로 삼아 블록 중첩 생성 원천 차단
        HashSet<string> seenIDs = new HashSet<string>();
        List<string> cleanLines = new List<string> { lines.First() }; // 헤더 추가
        int restoredDestroyedCount = 0;
        int skippedParseFailCount = 0;

        for (int i = 1; i < lines.Count; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length >= 12)
            {
                string toolAttr = cols[10].Trim();
                string id = cols[0];

                // 제3원칙: 보강물(Reinforcement)이 아닌 원본(Existing 등)만 대상에 포함
                // ⭐ [명시화] RiskLevel/좌표가 "DESTROYED"였던 원본 블록도 여기서 좌표를 ID로부터
                // 재계산해서 복구 대상에 반드시 포함시킨다. (파괴 여부와 무관하게 원본이면 무조건 복구)
                if (toolAttr != "Reinforcement")
                {
                    bool wasDestroyed = cols[1].Trim() == "DESTROYED" || cols[5].Trim().Contains("Destroyed");

                    // 제2원칙: 이미 처리된 ID라면 중복 추가 무조건 방지
                    if (seenIDs.Contains(id)) continue;
                    seenIDs.Add(id);

                    // 제2원칙: ID에서 좌표를 정확히 파내어 완벽 복구
                    string[] idParts = id.Split('_');
                    if (idParts.Length >= 3 &&
                        float.TryParse(idParts[0], out float px) &&
                        float.TryParse(idParts[1], out float pz) &&
                        float.TryParse(idParts[2], out float py))
                    {
                        px /= 10f; pz /= 10f; py /= 10f;

                        string fixedLine = $"{id},{px:F2},{py:F2},{pz:F2},0.00,Safe,N,{cols[7]},{cols[8]},{cols[9]},{toolAttr},{cols[11]}";
                        cleanLines.Add(fixedLine);
                        if (wasDestroyed) restoredDestroyedCount++;
                    }
                    else
                    {
                        skippedParseFailCount++;
                        Debug.LogWarning($"⚠️ [복구 실패] ID 파싱 실패로 복구 대상에서 누락됨: {id}");
                    }
                }
            }
        }

        Debug.Log($"🧱 [원본 복구] 파괴됐던 원본 블록 {restoredDestroyedCount}개 포함 복구 완료! (파싱 실패로 누락된 블록: {skippedParseFailCount}개)");

        File.WriteAllLines(csvPath, cleanLines);
        
        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string genPath = Path.Combine(projectPath, "BuildingLogs", "Last_Building.csv");
        if (!Directory.Exists(Path.GetDirectoryName(genPath))) Directory.CreateDirectory(Path.GetDirectoryName(genPath));
        File.WriteAllLines(genPath, cleanLines);

        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));
        
        if (LogManager.Instance != null) LogManager.Instance.OnPressRKey();

        showPanel = false;
        yKeyState = 0;
        
        SpawnerSystem.isUMode = true;
        SpawnerSystem.isOMode = false;
        SpawnerSystem.isLMode = false;
        
        var gen = FindFirstObjectByType<BlueprintTargetGenerator>();
        if (gen != null) gen.LoadLastBuildingForUMode();

        Debug.Log("🗑️ [완벽 초기화 완료] 보강 도면 삭제 + ID 중복 필터링 + 순정 원본 복구 완료!");
    }
}
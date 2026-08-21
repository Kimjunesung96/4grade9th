using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;

public class BudgetUIManager : MonoBehaviour
{
    public static BudgetUIManager Instance;

    public static int yKeyState = 0;

    [Header("UGUI 연결 (에디터에서 드래그 앤 드롭)")]
    public GameObject mainPanel; // 메인 설정 창
    public GameObject receiptPanel; // 영수증 창
    public TextMeshProUGUI receiptText; // 영수증 텍스트

    [Header("입력 및 선택 UI")]
    public TMP_InputField budgetInput;
    public TMP_InputField floorCountInput;
    public TMP_Dropdown wallMaterialDropdown;
    public TMP_Dropdown floorMaterialDropdown;
    
    [Header("보강 설정 UI")]
    public Toggle wantsReinforcementToggle;
    public TMP_Dropdown reinforcementModeDropdown; // 0: 많이(격자), 1: 적게(우산)
    public TMP_Dropdown reinforcementMaterialDropdown;

    [Header("버튼 UI")]
    public Button btnJustReinforce;
    public Button btnConfirm;
    public Button btnCheap;
    public Button btnExpensive;
    public Button btnUndo;
    public Button btnClearAll;

    private bool pendingRemoveReinforcements = false;
    private bool isCheapON = false;
    private bool isExpensiveON = false;

    // ⭐ 이 줄을 꼭 추가해 주세요!
    public bool wantsReinforcement = false;

    private string csvPath;
    private string metaPath;

    public int reinforcementMode = 1; 
    public string reinforcementMaterial = "H_Beam";
    private List<string> allMaterials = new List<string>();
    private static readonly string[] reinforcementMaterialOptions = new string[] { "H_Beam" };

    private Dictionary<int, float> phaseCosts = new Dictionary<int, float>();
    private float totalCost = 0f;
    private float originalCost = 0f;
    private int currentMaxPhase = 0;

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
        Debug.Log($"💰 Start 실행됨! mainPanel={mainPanel}");
        csvPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        metaPath = Path.Combine(Application.dataPath, "StressBlock", "Budget_Meta.csv");
        
        LoadMaterialLists();
        CalculateCurrentCosts();
    if (wallMaterialDropdown != null)
        wallMaterialDropdown.onValueChanged.AddListener((idx) => {
            Debug.LogError($"🎯 드롭다운 값 변경됨! index={idx}, text={wallMaterialDropdown.options[idx].text}");
        });


        SetupUIEvents();

        if (mainPanel) mainPanel.SetActive(false);
        UpdateReceiptUI();
    }

    private void SetupUIEvents()
    {
        if (btnJustReinforce) btnJustReinforce.onClick.AddListener(OnJustReinforce);
        if (btnConfirm) btnConfirm.onClick.AddListener(OnConfirm);
        if (btnUndo) btnUndo.onClick.AddListener(UndoLastReinforcement);
        if (btnClearAll) btnClearAll.onClick.AddListener(() => pendingRemoveReinforcements = true);
        
        if (btnCheap) btnCheap.onClick.AddListener(() => {
            isCheapON = !isCheapON;
            if (isCheapON) isExpensiveON = false;
            UpdateToggleButtonColors();
        });

        if (btnExpensive) btnExpensive.onClick.AddListener(() => {
            isExpensiveON = !isExpensiveON;
            if (isExpensiveON) isCheapON = false;
            UpdateToggleButtonColors();
        });

        if (wantsReinforcementToggle) wantsReinforcementToggle.onValueChanged.AddListener((val) => {
            reinforcementModeDropdown.interactable = val;
            reinforcementMaterialDropdown.interactable = val;
        });

        UpdateToggleButtonColors();
    }

    private void UpdateToggleButtonColors()
    {
        if (btnCheap) btnCheap.GetComponent<Image>().color = isCheapON ? new Color(0.6f, 1f, 0.6f) : Color.white;
        if (btnExpensive) btnExpensive.GetComponent<Image>().color = isExpensiveON ? new Color(1f, 0.85f, 0.4f) : Color.white;
    }

void Update()
    {
        // ⭐ 이 세 줄을 추가합니다! DOTS 상태와 무관하게 UI 매니저가 직접 Y키를 감지합니다.
        if (Input.GetKeyDown(KeyCode.Y))
        {
            OnYKeyPressed();
        }

        if (pendingRemoveReinforcements)
        {
            pendingRemoveReinforcements = false;
            ExecuteRemoveReinforcements();
        }

        if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.R) || 
            Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.N))
        {
            Invoke(nameof(CalculateCurrentCosts), 0.1f);
        }
    }

    void CalculateCurrentCosts()
    {
        string targetCsv = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        if (!File.Exists(targetCsv)) targetCsv = csvPath; 
        if (!File.Exists(targetCsv)) return;
        
        try 
        {
            var lines = File.ReadAllLines(targetCsv);
            if (MaterialDataManager.Instance == null) return;
            var dict = MaterialDataManager.Instance.MaterialDict;

            float tempOriginal = 0f;
            float tempTotal = 0f;
            int tempMaxPhase = 0;
            Dictionary<int, float> tempPhaseCosts = new Dictionary<int, float>();

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length < 8) continue;

                string matName = cols[7].Trim();
                int phase = 0;
                if (cols.Length >= 13) int.TryParse(cols[12].Trim(), out phase);

                if (phase > tempMaxPhase) tempMaxPhase = phase;

                float price = 0f;
                if (dict.TryGetValue(matName, out var spec))
                {
                    price = spec.Density * spec.PricePerKg * 3.375f;
                }

                if (phase == 0) tempOriginal += price;
                else 
                {
                    if (!tempPhaseCosts.ContainsKey(phase)) tempPhaseCosts[phase] = 0f;
                    tempPhaseCosts[phase] += price;
                }
                tempTotal += price;
            }

            originalCost = tempOriginal;
            phaseCosts = tempPhaseCosts;
            totalCost = tempTotal;
            currentMaxPhase = tempMaxPhase;

            UpdateReceiptUI();
        } 
        catch (Exception) { }
    }

    void UpdateReceiptUI()
    {
        if (receiptPanel == null || receiptText == null) return;

        if (totalCost > 0)
        {
            receiptPanel.SetActive(true);
            string text = $"뼈대(원본): <color=#AADDFF>{originalCost:N0} 원</color>\n";
            for (int i = 1; i <= currentMaxPhase; i++)
            {
                if (phaseCosts.ContainsKey(i))
                {
                    text += $"{i}차 보강: <color=#FFAAAA>{phaseCosts[i]:N0} 원</color>\n";
                }
            }
            text += $"\n<color=yellow><b>총 누적 예산: {totalCost:N0} 원</b></color>";
            receiptText.text = text;
        }
        else
        {
            receiptPanel.SetActive(false);
        }
    }

    void LoadMaterialLists()
    {
        allMaterials.Clear();
        if (MaterialDataManager.Instance == null) return;

        foreach (var kv in MaterialDataManager.Instance.MaterialDict)
        {
            allMaterials.Add(kv.Key);
        }

        if (wallMaterialDropdown)
        {
            wallMaterialDropdown.ClearOptions();
            wallMaterialDropdown.AddOptions(allMaterials);
        }
        if (floorMaterialDropdown)
        {
            floorMaterialDropdown.ClearOptions();
            floorMaterialDropdown.AddOptions(allMaterials);
        }
        if (reinforcementMaterialDropdown)
        {
            reinforcementMaterialDropdown.ClearOptions();
            reinforcementMaterialDropdown.AddOptions(reinforcementMaterialOptions.ToList());
        }
    }

    public void OnYKeyPressed()
    {
        yKeyState = (yKeyState + 1) % 3;
        Debug.Log($"🔑 Y키! state={yKeyState}");
        if (yKeyState == 0)
        {
            if (mainPanel) mainPanel.SetActive(false);
            SpawnerSystem.isUMode = false;
            SpawnerSystem.isOMode = false;
            SpawnerSystem.isLMode = false;
        }
        else if (yKeyState == 1)
        {
            if (mainPanel) mainPanel.SetActive(false);
            SpawnerSystem.isUMode = true;
            var gen = FindFirstObjectByType<BlueprintTargetGenerator>();
            if (gen != null) gen.LoadLastBuildingForUMode();
        }
        else if (yKeyState == 2)
        {
            SpawnerSystem.isUMode = false;
            if (mainPanel) mainPanel.SetActive(true);
            isCheapON = false;
            isExpensiveON = false;
            UpdateToggleButtonColors();
            LoadMaterialLists();
        }
    }

    private void MergeToAfterReinforce()
    {
        string basePath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        string planPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        string afterPath = Path.Combine(Application.dataPath, "StressBlock", "after_reinforce.csv");

        List<string> allLines = new List<string>();
        if (File.Exists(basePath)) allLines.AddRange(File.ReadAllLines(basePath).Skip(1));
        if (File.Exists(planPath)) allLines.AddRange(File.ReadAllLines(planPath).Skip(1));

        Dictionary<string, string> bestBlocks = new Dictionary<string, string>();
        Dictionary<string, int> bestPhases = new Dictionary<string, int>();

        foreach (var line in allLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length < 12) continue;
            
            string rawId = cols[0];
            var idParts = rawId.Split('_');
            float px = float.Parse(idParts[0]) / 10f;
            float pz = float.Parse(idParts[1]) / 10f;
            float py = float.Parse(idParts[2]) / 10f;

            string cleanId = GridUtility.ToBlockID(px, py, pz);

            int phase = 0;
            if (cols.Length >= 13 && int.TryParse(cols[12].Trim(), out int p)) phase = p;
            else if (cols[10].Trim() == "Reinforcement") phase = 1;

            if (!bestBlocks.ContainsKey(cleanId) || phase > bestPhases[cleanId])
            {
                bestBlocks[cleanId] = line;
                bestPhases[cleanId] = phase;
            }
        }

        List<string> finalList = new List<string> { "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type,Phase" };
        finalList.AddRange(bestBlocks.Values);

        File.WriteAllLines(afterPath, finalList);
        File.WriteAllLines(csvPath, finalList);
        
        Debug.Log("🏭 [데이터 전처리 완료] 겹치는 블록 중 최신 보강재만 살려서 대폭발 버그를 차단했습니다!");
    }

    void OnConfirm()
    {
        float budget = float.MaxValue;
        if (budgetInput && !string.IsNullOrEmpty(budgetInput.text)) float.TryParse(budgetInput.text, out budget);
        
        int floors = 1;
        if (floorCountInput && !string.IsNullOrEmpty(floorCountInput.text)) int.TryParse(floorCountInput.text, out floors);

        string currentMode = "Manual";
        if (isCheapON) currentMode = "Cheap";
        if (isExpensiveON) currentMode = "Expensive";

        // ⭐ 지역 변수(bool) 선언을 제거하고, 방금 위에서 만든 전역 변수에 값을 저장합니다.
        wantsReinforcement = wantsReinforcementToggle != null && wantsReinforcementToggle.isOn;
        this.reinforcementMode = (reinforcementModeDropdown != null && reinforcementModeDropdown.value == 0) ? 1 : 2;
        this.reinforcementMaterial = reinforcementMaterialDropdown != null ? reinforcementMaterialDropdown.options[reinforcementMaterialDropdown.value].text : "H_Beam";

        if ((isCheapON || isExpensiveON) && MaterialDataManager.Instance != null)
        {
            var dict = MaterialDataManager.Instance.MaterialDict;
            wantsReinforcement = true;

            if (isCheapON)
            {
                reinforcementMaterial = dict.OrderBy(kv => kv.Value.Density * kv.Value.PricePerKg).First().Key;
                reinforcementMode = 2;
            }
            else 
            {
                reinforcementMaterial = dict.OrderByDescending(kv => math.min(kv.Value.Tensile, kv.Value.Compressive)).First().Key;
                reinforcementMode = 1; 
            }
        }

        SaveBudgetMeta(budget, currentMode, floors, wantsReinforcement);
        ApplyMaterialsToScene(currentMode, budget);

        if (currentMode == "Cheap" || currentMode == "Expensive" || wantsReinforcement)
        {
            var reinforcer = UnityEngine.Object.FindFirstObjectByType<ReinforcementManager>();
            if (reinforcer != null) reinforcer.CreatePlanExcel();
        }

        SpawnerSystem.SaveLastBuildingSnapshot(Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager);

        var rm = FindFirstObjectByType<ReinforcementManager>();
        if (rm != null) rm.CreatePlanExcel();

        MergeToAfterReinforce();

        SpawnerSystem.backupIDToQuery = -1f;
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));

        if (mainPanel) mainPanel.SetActive(false);
        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "after_reinforce.csv"; 
        SpawnerSystem.loadDelayTimer = 5f;

        Invoke(nameof(CalculateCurrentCosts), 0.5f); 
        Debug.Log("✅ [장전 완료] 보강재가 결합된 '완전체 건물' 홀로그램을 화면에 띄웁니다! (G키로 타설하세요)");
    }

    void OnJustReinforce()
    {
        SpawnerSystem.SaveLastBuildingSnapshot(Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager);
        MergeToAfterReinforce();

        SpawnerSystem.backupIDToQuery = -1f;
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));

        yKeyState = 0;
        if (mainPanel) mainPanel.SetActive(false);
        SpawnerSystem.isUMode = false;
        
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "after_reinforce.csv"; 
        SpawnerSystem.loadDelayTimer = 5f;
        
        Invoke(nameof(CalculateCurrentCosts), 0.5f);
        Debug.Log("(just보강) 보강재가 결합된 완전체 건물 장전 완료!");
    }

    void SaveBudgetMeta(float budget, string mode, int floors, bool wReinforce)
    {
        string selWall = wallMaterialDropdown != null ? wallMaterialDropdown.options[wallMaterialDropdown.value].text : "";
        string selFloor = floorMaterialDropdown != null ? floorMaterialDropdown.options[floorMaterialDropdown.value].text : "";
        
        string header = "Budget,Mode,Floors,WallMaterial,FloorMaterial,Reinforce";
        string row = budget + "," + mode + "," + floors + "," + selWall + "," + selFloor + "," + (wReinforce ? "YES" : "NO");
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

        foreach (var b in blocks.Where(b => b.Tool == "Reinforcement")) totalCost += b.Price;
        var originalBlocks = blocks.Where(b => b.Tool != "Reinforcement").ToList();

        string selWall = wallMaterialDropdown != null ? wallMaterialDropdown.options[wallMaterialDropdown.value].text : "";
        string selFloor = floorMaterialDropdown != null ? floorMaterialDropdown.options[floorMaterialDropdown.value].text : "";

        if (mode == "Manual")
        {
            foreach (var b in originalBlocks)
            {
                string matName = b.Type == "Floor" ? selFloor : selWall;
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

    void UndoLastReinforcement()
    {
        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        string stressBlockLastBuildPath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        if (!File.Exists(stressBlockLastBuildPath)) return;

        var allLines = File.ReadAllLines(stressBlockLastBuildPath);
        int maxPhase = 0;
        
        for (int i = 1; i < allLines.Length; i++)
        {
            var c = allLines[i].Split(',');
            if (c.Length >= 13 && int.TryParse(c[12].Trim(), out int p))
            {
                if (p > maxPhase) maxPhase = p;
            }
        }

        if (maxPhase == 0) return;

        var cleanLines = allLines.Where(line => 
        {
            var c = line.Split(',');
            if (c.Length < 13) return true;
            if (c[0] == "BlockID") return true; 
            int.TryParse(c[12].Trim(), out int p);
            return p != maxPhase; 
        }).ToList();

        File.WriteAllLines(csvPath, cleanLines);
        File.WriteAllLines(stressBlockLastBuildPath, cleanLines);
        
        var bpManagerForClear = FindFirstObjectByType<BlueprintManager>();
        if (bpManagerForClear != null) bpManagerForClear.ClearRuntimeCache();

        SpawnerSystem.backupIDToQuery = -1f;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));
        
        if (mainPanel) mainPanel.SetActive(false);
        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "Last_Building.csv"; 
        SpawnerSystem.loadDelayTimer = 5f;

        var dragController = FindFirstObjectByType<SimulationDragController>();
        if (dragController != null) dragController.CancelDrag();

        Invoke(nameof(CalculateCurrentCosts), 0.5f);
    }

    void ExecuteRemoveReinforcements()
    {
        string planPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        string afterPath = Path.Combine(Application.dataPath, "StressBlock", "after_reinforce.csv");
        if (File.Exists(planPath)) File.Delete(planPath);
        if (File.Exists(afterPath)) File.Delete(afterPath);

        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        string stressBlockLastBuildPath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
        if (!File.Exists(stressBlockLastBuildPath)) return;

        var cleanLines = File.ReadAllLines(stressBlockLastBuildPath).Where(line => 
        {
            var c = line.Split(',');
            if (c.Length < 11) return true;
            if (c[0] == "BlockID") return true; 
            return c[10].Trim() != "Reinforcement"; 
        }).ToList();

        if (cleanLines.Count <= 1) return;

        File.WriteAllLines(csvPath, cleanLines);
        File.WriteAllLines(stressBlockLastBuildPath, cleanLines);

        var bpManagerForClear = FindFirstObjectByType<BlueprintManager>();
        if (bpManagerForClear != null) bpManagerForClear.ClearRuntimeCache();

        SpawnerSystem.backupIDToQuery = -1f;
        em.DestroyEntity(em.CreateEntityQuery(typeof(BlockTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(JointTag)));
        em.DestroyEntity(em.CreateEntityQuery(typeof(GhostBlockTag)));
        
        if (LogManager.Instance != null) LogManager.Instance.OnPressRKey();

        if (mainPanel) mainPanel.SetActive(false);
        yKeyState = 0;
        SpawnerSystem.isUMode = false;
        
        SpawnerSystem.isAbsolutePositionMode = true;
        SpawnerSystem.targetLoadFile = "Last_Building.csv"; 
        SpawnerSystem.loadDelayTimer = 5f;

        var dragController = FindFirstObjectByType<SimulationDragController>();
        if (dragController != null) dragController.CancelDrag();

        Invoke(nameof(CalculateCurrentCosts), 0.5f);
    }
}
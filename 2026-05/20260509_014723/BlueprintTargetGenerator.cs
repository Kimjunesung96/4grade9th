using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class BlueprintTargetGenerator : MonoBehaviour
{
    private List<float3> currentScannedData = new List<float3>();
    private Color32[] currentPixels;
    private float texWidth = 0f;
    private float texHeight = 0f;

    // ⭐ 복원 및 업그레이드된 마우스 휠 복사 횟수 변수
    private float currentFloorCount = 1f;

    private bool showBlueprintUI = false;
    private List<string> availableCsvFiles = new List<string>();
    private Vector2 leftScrollPosition = Vector2.zero;
    private Vector2 rightScrollPosition = Vector2.zero;

    private bool isWaitingForLimit = false;
    private string pendingFileName = "";

    private string stressBlockFolder;
    private string savedBlueprintsFolder;

    // ⭐ 복층 장바구니 리스트! (누른 순서대로 1층, 2층, 3층이 됩니다)
    private List<string> selectedFloors = new List<string>();

    void Start()
    {
        stressBlockFolder = Path.Combine(Application.dataPath, "StressBlock");
        if (!Directory.Exists(stressBlockFolder))
        {
            Directory.CreateDirectory(stressBlockFolder);
        }

        savedBlueprintsFolder = Path.Combine(stressBlockFolder, "SavedBlueprints");
        if (!Directory.Exists(savedBlueprintsFolder))
        {
            Directory.CreateDirectory(savedBlueprintsFolder);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            showBlueprintUI = !showBlueprintUI;
            if (showBlueprintUI)
            {
                RefreshCsvList();
                isWaitingForLimit = false;
            }
        }

        if (Input.GetKeyDown(KeyCode.U))
        {
            SpawnerSystem.isUMode = true;
            SpawnerSystem.isOMode = false;
            SpawnerSystem.isLMode = false;
            LoadLastBuildingForUMode();
        }

        if (isWaitingForLimit)
        {
            HandleNumericLimitInput();
        }

        if ((SpawnerSystem.isOMode || SpawnerSystem.isUMode) && currentScannedData.Count > 0f)
        {
            if (Input.GetKeyDown(KeyCode.Q)) RotateBlueprint(true);
            if (Input.GetKeyDown(KeyCode.E)) RotateBlueprint(false);

            // ⭐ 십장님 특명! 마우스 휠(복사) 즉각 복구 (장바구니 구성물 통째로 무한 복사 가능)
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0.01f)
            {
                currentFloorCount += 1f;
                ApplyOffsetAndLoad(currentScannedData);
                Debug.Log($"🏢 [휠 UP] 건물이 통째로 복사되었습니다! 현재 총 [{currentFloorCount}세트] 장전 완료.");
            }
            else if (scroll < -0.01f)
            {
                currentFloorCount = math.max(1f, currentFloorCount - 1f);
                ApplyOffsetAndLoad(currentScannedData);
                Debug.Log($"🏢 [휠 DOWN] 제일 윗 단을 지웠습니다! 현재 총 [{currentFloorCount}세트] 장전 완료.");
            }
        }
    }

    void OnGUI()
    {
        if (!showBlueprintUI) return;

        float boxWidth = 700f;
        float boxHeight = 550f;
        GUI.Box(new Rect(Screen.width / 2f - boxWidth / 2f, Screen.height / 2f - boxHeight / 2f, boxWidth, boxHeight), "📂 현장 도면 관리소 (O키로 닫기)");

        // ==========================================
        // ◀◀ 왼쪽: 도면 보관함 (Library)
        // ==========================================
        GUILayout.BeginArea(new Rect(Screen.width / 2f - 330f, Screen.height / 2f - 230f, 310f, 500f));

        GUI.color = Color.green;
        if (GUILayout.Button("➕ [새로 만들기] 새 이미지 스캔", GUILayout.Height(40f)))
        {
            showBlueprintUI = false;
            OpenAndCacheImage();
        }
        GUI.color = Color.white;
        GUILayout.Space(10f);

        GUILayout.Label("📑 저장된 도면 (클릭 시 장바구니에 담김)");
        leftScrollPosition = GUILayout.BeginScrollView(leftScrollPosition, "box");
        for (int i = 0; i < availableCsvFiles.Count; i++)
        {
            string fileName = Path.GetFileNameWithoutExtension(availableCsvFiles[i]);
            if (GUILayout.Button($"{i + 1}. {fileName}", GUILayout.Height(30f)))
            {
                selectedFloors.Add(availableCsvFiles[i]);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // ==========================================
        // ▶▶ 오른쪽: 복층 구성 장바구니 (Cart)
        // ==========================================
        GUILayout.BeginArea(new Rect(Screen.width / 2f + 20f, Screen.height / 2f - 230f, 310f, 500f));

        GUI.color = new Color(0.8f, 0.9f, 1f);
        GUILayout.BeginVertical("box");
        GUILayout.Label($"🛒 복층 설계 장바구니: 총 {selectedFloors.Count}층");

        rightScrollPosition = GUILayout.BeginScrollView(rightScrollPosition);
        for (int i = 0; i < selectedFloors.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($" [{i + 1}층] {Path.GetFileNameWithoutExtension(selectedFloors[i])}");
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("❌", GUILayout.Width(30f))) { selectedFloors.RemoveAt(i); }
            GUI.color = new Color(0.8f, 0.9f, 1f);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(10f);
        if (selectedFloors.Count > 0)
        {
            GUI.color = Color.yellow;
            if (GUILayout.Button("✅ [타설 완료] 이 구성으로 장전!", GUILayout.Height(50f)))
            {
                showBlueprintUI = false;
                LoadStackedFloors();
            }
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("🗑️ 장바구니 비우기", GUILayout.Height(30f)))
            {
                selectedFloors.Clear();
            }
        }
        else
        {
            GUILayout.Label("왼쪽 보관함에서 도면을 담아주세요.");
        }
        GUILayout.EndVertical();
        GUI.color = Color.white;

        GUILayout.EndArea();
    }

    private void RefreshCsvList()
    {
        availableCsvFiles.Clear();
        string[] files = Directory.GetFiles(savedBlueprintsFolder, "*.csv");
        foreach (var file in files)
        {
            availableCsvFiles.Add(file);
        }
    }

    private void OpenAndCacheImage()
    {
#if UNITY_EDITOR
        string imagePath = EditorUtility.OpenFilePanel("도면 이미지 선택", "", "png,jpg,jpeg");
        
        if (!string.IsNullOrEmpty(imagePath))
        {
            pendingFileName = Path.GetFileNameWithoutExtension(imagePath);
            
            byte[] bytes = File.ReadAllBytes(imagePath);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            
            currentPixels = tex.GetPixels32();
            texWidth = (float)tex.width;
            texHeight = (float)tex.height;
            Destroy(tex);

            isWaitingForLimit = true;
            Debug.Log($"⚙️ [{pendingFileName}] 이미지 스캔 대기! 숫자키[1~0]를 눌러 타설 사이즈(밀도)를 확정하세요.");
        }
#else
        Debug.LogWarning("파일 다이얼로그는 에디터 전용입니다.");
#endif
    }

    private void HandleNumericLimitInput()
    {
        float targetLimit = -1f;

        if (Input.GetKeyDown(KeyCode.Alpha1)) targetLimit = 1000f;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) targetLimit = 2000f;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) targetLimit = 3000f;
        else if (Input.GetKeyDown(KeyCode.Alpha4)) targetLimit = 4000f;
        else if (Input.GetKeyDown(KeyCode.Alpha5)) targetLimit = 5000f;
        else if (Input.GetKeyDown(KeyCode.Alpha6)) targetLimit = 6000f;
        else if (Input.GetKeyDown(KeyCode.Alpha7)) targetLimit = 7000f;
        else if (Input.GetKeyDown(KeyCode.Alpha8)) targetLimit = 8000f;
        else if (Input.GetKeyDown(KeyCode.Alpha9)) targetLimit = 9000f;
        else if (Input.GetKeyDown(KeyCode.Alpha0)) targetLimit = 10000f;

        if (targetLimit > 0f)
        {
            isWaitingForLimit = false;
            Debug.Log($"⚙️ 사이즈 [{targetLimit}개] 추출 및 저장 중...");

            var scanResult = SearchUnderLimit(currentPixels, texWidth, texHeight, targetLimit);
            float dupCount = 0f;
            List<float3> finalBlocks = RemoveDuplicates(scanResult, out dupCount);

            string newFileName = $"{pendingFileName}_사이즈{targetLimit}";
            string newCsvPath = Path.Combine(savedBlueprintsFolder, newFileName + ".csv");

            SaveListToCSV(finalBlocks, newCsvPath);

            Debug.Log($"💾 [{newFileName}.csv] 저장 완료! 장바구니에 자동으로 담깁니다.");

            selectedFloors.Add(newCsvPath);
            RefreshCsvList();
            showBlueprintUI = true;
        }
    }

    private void LoadStackedFloors()
    {
        SpawnerSystem.isOMode = true;
        SpawnerSystem.isLMode = false;
        SpawnerSystem.isUMode = false;

        List<float3> finalStackedData = new List<float3>();

        for (int i = 0; i < selectedFloors.Count; i++)
        {
            string path = selectedFloors[i];
            if (File.Exists(path))
            {
                List<float3> rawList = new List<float3>();
                string[] lines = File.ReadAllLines(path);

                for (int j = 1; j < lines.Length; j++)
                {
                    string[] cols = lines[j].Split(',');
                    if (cols.Length >= 4 && cols[0] != "ID")
                    {
                        // ⭐ X, Z 스냅 정밀 교정 적용
                        float x = math.round((float.Parse(cols[1]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        float y = math.round((float.Parse(cols[2]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        float z = math.round((float.Parse(cols[3]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        rawList.Add(new float3(x, y, z));
                    }
                }

                List<float3> centeredList = CenterDataToOffset(rawList);

                // ⭐ 층수 15.0f 오프셋 적용
                float floorYOffset = i * 15.0f;

                foreach (var p in centeredList)
                {
                    finalStackedData.Add(new float3(p.x, p.y + floorYOffset, p.z));
                }
            }
        }

        currentScannedData = finalStackedData;
        currentFloorCount = 1f; // ⭐ 장바구니를 새로 장전할 때는 휠 복사 횟수를 1배로 초기화!
        SpawnerSystem.ExternalBlueprintData = finalStackedData;

        Debug.Log($"✅ [복층 타설 준비 완료] 총 {selectedFloors.Count}층 건물, {finalStackedData.Count}개 블록 장전! 우클릭(에임), Q/E(회전), F/G(타설) 조작 지원.");

        selectedFloors.Clear();
    }

    private void SaveListToCSV(List<float3> blocks, string path)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("ID,X,Y,Z,Type");
        foreach (var p in blocks)
        {
            string typeStr = p.y > 1.5f ? "Wall" : "Floor";
            // ⭐ 기존 ID 규격(X*10_Z*10_Y*10) 완벽 복구
            string id = $"{(int)math.round(p.x * 10f)}_{(int)math.round(p.z * 10f)}_{(int)math.round(p.y * 10f)}";
            sb.AppendLine($"{id},{p.x},{p.y},{p.z},{typeStr}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void LoadLastBuildingForUMode()
    {
        string path = Path.Combine(stressBlockFolder, "Last_Building.csv");
        if (File.Exists(path))
        {
            List<float3> rawList = new List<float3>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split(',');
                if (cols.Length >= 4 && cols[0] != "ID")
                {
                    float x = math.round((float.Parse(cols[1]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    float y = math.round((float.Parse(cols[2]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    float z = math.round((float.Parse(cols[3]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    rawList.Add(new float3(x, y, z));
                }
            }

            if (rawList.Count > 0)
            {
                currentScannedData = CenterDataToOffset(rawList);
                currentFloorCount = 1f;
                SpawnerSystem.ExternalBlueprintData = currentScannedData;
                Debug.Log($"💾 [U 모드 발동] 방금 지은 쌍둥이 건물 [{rawList.Count}개] 장전 완료!");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ Last_Building.csv 파일을 찾을 수 없습니다. 건물을 먼저 지어주세요.");
        }
    }

    private List<float3> CenterDataToOffset(List<float3> rawData)
    {
        if (rawData.Count == 0) return rawData;

        float minX = 99999f, minZ = 99999f, maxX = -99999f, maxZ = -99999f;
        foreach (var p in rawData)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float centerX = math.round(((minX + maxX) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;
        float centerZ = math.round(((minZ + maxZ) / 2f - 1.5f) / 3.0f) * 3.0f + 1.5f;

        List<float3> centeredData = new List<float3>();
        foreach (var p in rawData)
        {
            centeredData.Add(new float3(p.x - centerX, p.y, p.z - centerZ));
        }
        return centeredData;
    }

    private void RotateBlueprint(bool isLeft)
    {
        List<float3> rotatedData = new List<float3>();

        foreach (var p in currentScannedData)
        {
            float newX = isLeft ? -p.z : p.z;
            float newZ = isLeft ? p.x : -p.x;
            rotatedData.Add(new float3(newX, p.y, newZ));
        }

        currentScannedData = rotatedData;
        ApplyOffsetAndLoad(currentScannedData);

        Debug.Log($"🔄 도면이 {(isLeft ? "[왼쪽:Q]" : "[오른쪽:E]")}으로 90도 회전했습니다!");
    }

    private void ApplyOffsetAndLoad(List<float3> centeredRawData)
    {
        List<float3> stackedData = new List<float3>();

        // ⭐ 장바구니로 만든 복층 도면도 완벽하게 위로 복사되도록 '가장 높은 지붕'을 찾아서 계산!
        float maxY = 0f;
        foreach (var p in centeredRawData) { if (p.y > maxY) maxY = p.y; }
        float heightStep = math.floor(maxY / 15.0f) * 15.0f + 15.0f;
        if (heightStep < 15.0f) heightStep = 15.0f;

        for (float floor = 0f; floor < currentFloorCount; floor += 1f)
        {
            float floorYOffset = floor * heightStep;

            foreach (var pos in centeredRawData)
            {
                stackedData.Add(new float3(pos.x, pos.y + floorYOffset, pos.z));
            }
        }

        SpawnerSystem.ExternalBlueprintData = stackedData;
    }

    List<float3> RemoveDuplicates(List<float3> rawData, out float duplicateCount)
    {
        HashSet<string> uniqueCheck = new HashSet<string>();
        List<float3> cleanList = new List<float3>();
        duplicateCount = 0f;

        foreach (var pos in rawData)
        {
            // ⭐ X, Z 스냅 정밀 교정
            float snapX = math.round((pos.x - 1.5f) / 3.0f) * 3.0f + 1.5f;
            float snapY = math.round((pos.y - 1.5f) / 3.0f) * 3.0f + 1.5f;
            float snapZ = math.round((pos.z - 1.5f) / 3.0f) * 3.0f + 1.5f;

            string id = $"{snapX}_{snapY}_{snapZ}";

            if (!uniqueCheck.Contains(id))
            {
                uniqueCheck.Add(id);
                cleanList.Add(new float3(snapX, snapY, snapZ));
            }
            else
            {
                duplicateCount += 1f;
            }
        }
        return cleanList;
    }

    List<float3> SearchUnderLimit(Color32[] pixels, float w, float h, float limit)
    {
        for (float size = 2f; size < 500f; size += 1f)
        {
            List<float3> res = Scan(pixels, w, h, size);
            if ((float)res.Count <= limit) return res;
        }
        return new List<float3>();
    }

    List<float3> Scan(Color32[] pixels, float w, float h, float size)
    {
        List<float3> list = new List<float3>();
        for (float x = 0f; x <= w - size; x += size)
        {
            for (float z = 0f; z <= h - size; z += size)
            {
                float redCount = 0f;
                float blueCount = 0f;
                float blackCount = 0f;

                for (float i = 0f; i < size; i += 1f)
                {
                    for (float j = 0f; j < size; j += 1f)
                    {
                        if (x + i < w && z + j < h)
                        {
                            Color32 p = pixels[(int)((z + j) * w + (x + i))];
                            if (p.a > 128f)
                            {
                                if (p.r > 150f && p.g < 100f && p.b < 100f) redCount += 1f;
                                else if (p.b > 150f && p.r < 100f && p.g < 150f) blueCount += 1f;
                                else if ((p.r + p.g + p.b) / 3f < 128f) blackCount += 1f;
                            }
                        }
                    }
                }

                float area = size * size;

                // ⭐ 애초에 스캔할 때부터 좌표에 1.5f를 박아넣어 오차 원천 차단!
                if (redCount / area >= 0.1f)
                {
                    for (float y = 0f; y < 5f; y += 1f) list.Add(new float3(((int)(x / size)) * 3f + 1.5f, y * 3f + 1.5f, ((int)(z / size)) * 3f + 1.5f));
                }
                else if (blueCount > 0f)
                {
                    list.Add(new float3(((int)(x / size)) * 3f + 1.5f, 1.5f, ((int)(z / size)) * 3f + 1.5f));
                }
                else if (blackCount / area >= 0.3f)
                {
                    for (float y = 0f; y < 5f; y += 1f) list.Add(new float3(((int)(x / size)) * 3f + 1.5f, y * 3f + 1.5f, ((int)(z / size)) * 3f + 1.5f));
                }
                else if (blackCount > 0f)
                {
                    list.Add(new float3(((int)(x / size)) * 3f + 1.5f, 1.5f, ((int)(z / size)) * 3f + 1.5f));
                }
            }
        }
        return list;
    }
}
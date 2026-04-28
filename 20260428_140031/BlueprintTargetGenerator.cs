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
    private List<float3> data10k = new List<float3>();
    private List<float3> data5k = new List<float3>();

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            OpenAndSearch();
            SpawnerSystem.isOMode = true;
            SpawnerSystem.isLMode = false;
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            SpawnerSystem.isLMode = true;
            SpawnerSystem.isOMode = false;
            Debug.Log("📂 [L 모드 활성화] 탐색기 없이 로드합니다. 숫자 1(High) 또는 2(Low)를 누르세요.");
        }

        if (SpawnerSystem.isLMode && !SpawnerSystem.isOMode)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) LoadDirect("House_Plan_High.csv", "1단계(High) 도면");
            if (Input.GetKeyDown(KeyCode.Alpha2)) LoadDirect("House_Plan_Low.csv", "2단계(Low) 도면");
        }

        if (SpawnerSystem.isOMode && SpawnerSystem.ExternalBlueprintData != null && SpawnerSystem.ExternalBlueprintData.Count > 0)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0)
            {
                if (SpawnerSystem.ExternalBlueprintData.Count == data10k.Count)
                {
                    SpawnerSystem.ExternalBlueprintData = new List<float3>(data5k);
                    Debug.Log($"🧱 [마우스 휠 딸깍!] ➔ 2단계 쾌적 시공 (블록 {data5k.Count}개) 장전 완료!");
                }
                else
                {
                    SpawnerSystem.ExternalBlueprintData = new List<float3>(data10k);
                    Debug.Log($"🏗️ [마우스 휠 딸깍!] ➔ 1단계 정밀 시공 (블록 {data10k.Count}개) 장전 완료!");
                }
            }
        }
    }

    void OpenAndSearch()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("도면 이미지 선택", "", "png,jpg,jpeg");
        
        if (!string.IsNullOrEmpty(path))
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            Color32[] pixels = tex.GetPixels32();

            Debug.Log("🔍 도면 분석 및 오토 스케일링 중... 잠시만 기다려주세요!");

            var raw10k = SearchUnderLimit(pixels, tex.width, tex.height, 10000);
            data10k = RemoveDuplicates(raw10k, out int dup10k);
            SaveToCSV(data10k, "House_Plan_High.csv"); 

            var raw5k = SearchOverLimit(pixels, tex.width, tex.height, 5000);
            data5k = RemoveDuplicates(raw5k, out int dup5k);
            SaveToCSV(data5k, "House_Plan_Low.csv");   

            Debug.Log($"✅ [분석 완료] 정밀: {data10k.Count}개 / 쾌적: {data5k.Count}개");
            
            SpawnerSystem.ExternalBlueprintData = new List<float3>(data10k);
            Debug.Log($"⭐ 기본값 [1단계 정밀 시공] 장전 완료! 변경하려면 마우스 휠을 굴리세요.");
        }
#else
        Debug.LogWarning("파일 다이얼로그는 에디터 전용입니다.");
#endif
    }

    void LoadDirect(string fileName, string msg)
    {
        string path = Path.Combine(Application.dataPath, "StressBlock", fileName);
        if (File.Exists(path))
        {
            List<float3> tempList = new List<float3>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split(',');
                if (cols.Length >= 4)
                {
                    // ⭐ 폭발 억제! 1.5f 오차 보정해서 안전 착륙
                    float x = math.round(float.Parse(cols[1]) / 3.0f) * 3.0f;
                    float y = math.round((float.Parse(cols[2]) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                    float z = math.round(float.Parse(cols[3]) / 3.0f) * 3.0f;

                    tempList.Add(new float3(x, y, z));
                }
            }

            if (SpawnerSystem.ExternalBlueprintData != null)
            {
                SpawnerSystem.ExternalBlueprintData.Clear();
            }
            SpawnerSystem.ExternalBlueprintData = tempList;

            Debug.Log($"✅ [로컬 로드] {msg} ({tempList.Count}개) 장전 완료! 우클릭 후 G를 누르세요.");
        }
        else
        {
            Debug.LogError($"❌ 파일을 찾을 수 없습니다: {path}");
        }
    }

    List<float3> RemoveDuplicates(List<float3> rawData, out int duplicateCount)
    {
        HashSet<string> uniqueCheck = new HashSet<string>();
        List<float3> cleanList = new List<float3>();
        duplicateCount = 0;

        foreach (var pos in rawData)
        {
            float snapX = math.round(pos.x / 3.0f) * 3.0f;
            float snapY = math.round((pos.y - 1.5f) / 3.0f) * 3.0f + 1.5f;
            float snapZ = math.round(pos.z / 3.0f) * 3.0f;

            string id = $"{snapX}_{snapY}_{snapZ}";

            if (!uniqueCheck.Contains(id))
            {
                uniqueCheck.Add(id);
                cleanList.Add(new float3(snapX, snapY, snapZ));
            }
            else
            {
                duplicateCount++;
            }
        }
        return cleanList;
    }

    List<float3> SearchUnderLimit(Color32[] pixels, int w, int h, int limit)
    {
        for (int size = 2; size < 500; size++)
        {
            List<float3> res = Scan(pixels, w, h, size);
            if (res.Count <= limit) return res;
        }
        return new List<float3>();
    }

    List<float3> SearchOverLimit(Color32[] pixels, int w, int h, int limit)
    {
        for (int size = 300; size >= 2; size--)
        {
            List<float3> res = Scan(pixels, w, h, size);
            if (res.Count >= limit) return res;
        }
        return new List<float3>();
    }

    List<float3> Scan(Color32[] pixels, int w, int h, int size)
    {
        List<float3> list = new List<float3>();
        for (int x = 0; x <= w - size; x += size)
        {
            for (int z = 0; z <= h - size; z += size)
            {
                int black = 0;
                for (int i = 0; i < size; i++)
                {
                    for (int j = 0; j < size; j++)
                    {
                        if (x + i < w && z + j < h)
                        {
                            Color32 p = pixels[(z + j) * w + (x + i)];
                            if ((p.r + p.g + p.b) / 3f < 128) black++;
                        }
                    }
                }
                float ratio = (float)black / (size * size);

                if (ratio >= 0.3f)
                {
                    // ⭐ 폭발 억제! 스캔할 때부터 높이를 1.5, 4.5, 7.5... 로 잡아줍니다!
                    for (int y = 0; y < 5; y++) list.Add(new float3((x / size) * 3f, y * 3f + 1.5f, (z / size) * 3f));
                }
                else if (black > 0)
                {
                    list.Add(new float3((x / size) * 3f, 1.5f, (z / size) * 3f));
                }
            }
        }
        return list;
    }

    void SaveToCSV(List<float3> data, string fileName)
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("ID,X,Y,Z,Type");
        for (int i = 0; i < data.Count; i++)
        {
            float3 p = data[i];

            float x = math.round(p.x / 3.0f) * 3.0f;
            float y = math.round((p.y - 1.5f) / 3.0f) * 3.0f + 1.5f;
            float z = math.round(p.z / 3.0f) * 3.0f;

            string id = $"{x}_{y}_{z}";
            string type = y > 1.5f ? "Wall" : "Floor"; // 1.5초과는 전부 벽
            csv.AppendLine($"{id},{x},{y},{z},{type}");
        }
        string path = Path.Combine(Application.dataPath, "StressBlock", fileName);
        if (!Directory.Exists(Path.GetDirectoryName(path)))
            Directory.CreateDirectory(Path.GetDirectoryName(path));

        File.WriteAllText(path, csv.ToString());
    }
}
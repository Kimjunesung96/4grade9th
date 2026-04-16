using UnityEngine;
using System.Collections.Generic;
using System.IO;

[System.Serializable]
public class ArchitectBlockData
{
    public string id;
    public string toolName;
    public Vector3 position;
}

[System.Serializable]
public class ArchitectBlueprint
{
    public List<ArchitectBlockData> blocks = new List<ArchitectBlockData>();
}

public class BlueprintManager : MonoBehaviour
{
    [Header("도면 데이터 설정")]
    public string blueprintFileName = "Temp_Current_Building.json";

    private string FilePath => Path.Combine(Application.dataPath, "../BuildingLogs/temp", blueprintFileName);

    private Dictionary<string, HashSet<int>> blueprintCabinet = new Dictionary<string, HashSet<int>>();
    private ArchitectBlueprint currentBlueprint = new ArchitectBlueprint();

    void Awake()
    {
        LoadBlueprint();
    }

    public void LoadBlueprint()
    {
        blueprintCabinet.Clear();
        currentBlueprint.blocks.Clear();

        if (File.Exists(FilePath))
        {
            string json = File.ReadAllText(FilePath);
            currentBlueprint = JsonUtility.FromJson<ArchitectBlueprint>(json) ?? new ArchitectBlueprint();

            foreach (var block in currentBlueprint.blocks)
            {
                AddBlockToCabinet(block.id);
            }
            Debug.Log($"📂 [BlueprintManager] 도면 로드 완료! 총 {currentBlueprint.blocks.Count}개의 블록 정리됨.");
        }
        else
        {
            Debug.LogWarning($"⚠️ [BlueprintManager] 도면이 없습니다. 빈 현장에서 시작: {FilePath}");
        }
    }

    private void AddBlockToCabinet(string fullId)
    {
        int firstUnder = fullId.IndexOf('_');
        if (firstUnder == -1) return;
        int secondUnder = fullId.IndexOf('_', firstUnder + 1);
        if (secondUnder == -1) return;

        string colId = fullId.Substring(0, secondUnder);
        int yPos = int.Parse(fullId.Substring(secondUnder + 1));

        if (!blueprintCabinet.ContainsKey(colId))
        {
            blueprintCabinet[colId] = new HashSet<int>();
        }
        blueprintCabinet[colId].Add(yPos);
    }

    public bool IsBlockExist(string colId, int yPos)
    {
        if (blueprintCabinet.ContainsKey(colId))
        {
            return blueprintCabinet[colId].Contains(yPos);
        }
        return false;
    }

    public bool IsBlockExist(string fullId)
    {
        int firstUnder = fullId.IndexOf('_');
        if (firstUnder == -1) return false;
        int secondUnder = fullId.IndexOf('_', firstUnder + 1);
        if (secondUnder == -1) return false;

        string colId = fullId.Substring(0, secondUnder);
        int yPos = int.Parse(fullId.Substring(secondUnder + 1));
        return IsBlockExist(colId, yPos);
    }

    public void AddReinforcementBlock(string fullId, string toolType, Vector3 pos)
    {
        AddBlockToCabinet(fullId);

        ArchitectBlockData newBlock = new ArchitectBlockData
        {
            id = fullId,
            toolName = toolType,
            position = pos
        };
        currentBlueprint.blocks.Add(newBlock);

        // 🚨 십장님 특명! 렉 방지 위해 개별 저장과 콘솔 스팸을 삭제했습니다! 스포너가 다 짓고 한 번에 저장합니다.
    }

    public void SaveBlueprint()
    {
        string json = JsonUtility.ToJson(currentBlueprint, true);
        File.WriteAllText(FilePath, json);
        Debug.Log("💾 [BlueprintManager] JSON 도면 최종 기록 완료!");
    }

    public string VectorToID(Vector3 pos)
    {
        int snappedX = Mathf.RoundToInt((pos.x - 1.5f) / 3.0f) * 30 + 15;
        int snappedY = Mathf.RoundToInt((pos.y - 1.5f) / 3.0f) * 30 + 15;
        int snappedZ = Mathf.RoundToInt((pos.z - 1.5f) / 3.0f) * 30 + 15;
        snappedY = Mathf.Max(15, snappedY);

        return $"{snappedX:0000}_{snappedZ:0000}_{snappedY:0000}";
    }

    public void SplitID(string fullId, out string colId, out int y)
    {
        int firstUnder = fullId.IndexOf('_');
        int secondUnder = fullId.IndexOf('_', firstUnder + 1);
        colId = fullId.Substring(0, secondUnder);
        y = int.Parse(fullId.Substring(secondUnder + 1));
    }
}
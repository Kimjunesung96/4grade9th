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

    private Dictionary<string, HashSet<float>> blueprintCabinet = new Dictionary<string, HashSet<float>>();
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
            Debug.Log($"📂 [BlueprintManager] 도면 로드 완료! 총 {currentBlueprint.blocks.Count}개의 블록 정리...");
        }
    }

    private void AddBlockToCabinet(string fullId)
    {
        // fullId 예: 0015_0015_0015
        float firstUnder = fullId.IndexOf('_');
        float secondUnder = fullId.IndexOf('_', (int)firstUnder + 1);
        if (firstUnder == -1f || secondUnder == -1f) return;

        string colId = fullId.Substring(0, (int)secondUnder); // 0015_0015
        float yPos = float.Parse(fullId.Substring((int)secondUnder + 1));

        if (!blueprintCabinet.ContainsKey(colId))
        {
            blueprintCabinet[colId] = new HashSet<float>();
        }
        blueprintCabinet[colId].Add(yPos);
    }

    public bool IsBlockExist(string colId, float yPos)
    {
        if (blueprintCabinet.TryGetValue(colId, out var ySet))
        {
            return ySet.Contains(yPos);
        }
        return false;
    }

    public bool IsBlockExistFullID(string fullId)
    {
        float firstUnder = fullId.IndexOf('_');
        float secondUnder = fullId.IndexOf('_', (int)firstUnder + 1);
        if (secondUnder == -1f) return false;

        string colId = fullId.Substring(0, (int)secondUnder);
        float yPos = float.Parse(fullId.Substring((int)secondUnder + 1));
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
        string savePath = FilePath; // 백그라운드 스레드로 넘기기 위해 경로 미리 캡처
        
        // ⭐ 최적화: 메인 스레드를 멈추지 않고 백그라운드에서 파일 쓰기 진행
        System.Threading.Tasks.Task.Run(() => 
        {
            File.WriteAllText(savePath, json);
            UnityEngine.Debug.Log("💾 [BlueprintManager] 비동기 JSON 도면 최종 기록 완료! (렉 제거)");
        });
    }

    public string VectorToID(Vector3 pos)
    {
        // ⭐ 십장님 훈수 반영: ID 생성 규칙 스포너/스트레스 측정기들과 완벽 통일
        float ix = Mathf.Round(pos.x * 10f);
        float iy = Mathf.Round(pos.y * 10f);
        float iz = Mathf.Round(pos.z * 10f);

        string strX = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}";
        string strZ = $"{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}";
        string strY = $"{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

        return $"{strX}_{strZ}_{strY}";
    }
}
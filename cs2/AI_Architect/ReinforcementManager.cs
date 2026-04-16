using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class ReinforcementManager : MonoBehaviour
{
    private string stressCsvPath;
    private string planCsvPath;

    void Start()
    {
        stressCsvPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        planCsvPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        Debug.Log("👷‍♂️ [현장 반장] Y(도면 갱신) 대기 중! 실제 타설은 Spawner가 전담합니다!");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
        {
            CreatePlanExcel();
        }
    }

    public void CreatePlanExcel()
    {
        if (!File.Exists(stressCsvPath)) return;
        string[] lines = File.ReadAllLines(stressCsvPath);
        HashSet<string> existingBlocks = new HashSet<string>();
        List<string> planLines = new List<string> { "BlockID,PosX,PosY,PosZ,Tool" };

        // 1패스: 기존 블록 전부 Existing으로 등록
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;

            string id = cols[0];
            existingBlocks.Add(id);
            planLines.Add($"{id},{cols[1]},{cols[2]},{cols[3]},Existing");
        }

        // 2패스: Prescription=Y인 블록 기준으로 아래로 내려가며 보강 블록 생성
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;
            if (cols[6] != "Y") continue; // Prescription이 Y인 것만 보강

            string id = cols[0];
            string[] parts = id.Split('_');
            if (parts.Length != 3) continue;

            string colX = parts[0];
            string colZ = parts[1];
            int currentY = int.Parse(parts[2]); // BlockID 세번째 = 실제 Y(높이)

            float cleanX = float.Parse(cols[1]); // PosX
            float cleanY = float.Parse(cols[2]); // PosY (실제 높이값)
            float cleanZ = float.Parse(cols[3]); // PosZ

            while (currentY >= 45)
            {
                currentY -= 30;
                string targetId = $"{colX}_{colZ}_{currentY:0000}";
                if (existingBlocks.Contains(targetId)) break;
                existingBlocks.Add(targetId);

                // 보강 블록의 Y좌표: currentY / 10f (BlockID Y값 → Unity 좌표)
                float targetY = currentY / 10f;
                planLines.Add($"{targetId},{cleanX:F2},{targetY:F2},{cleanZ:F2},Reinforcement");
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [Y키 작동] 마스터 엑셀 도면 생성 완료! Spawner가 도면을 로드했습니다!");
    }
}
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
        Debug.Log("👷‍♂️ Y(도면 갱신) 대기 중!");
    }

    void Update() { if (Input.GetKeyDown(KeyCode.Y)) { CreatePlanExcel(); } }

    public void CreatePlanExcel()
    {
        if (!File.Exists(stressCsvPath)) return;
        string[] lines = File.ReadAllLines(stressCsvPath);
        if (lines.Length <= 1) return;

        HashSet<string> existingBlocks = new HashSet<string>();
        List<string> planLines = new List<string> { "BlockID,PosX,PosY,PosZ,Tool" };

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;
            string id = cols[0];
            existingBlocks.Add(id);
            planLines.Add($"{id},{cols[1]},{cols[2]},{cols[3]},Existing");
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;
            if (cols[6] != "Y") continue;

            string id = cols[0];
            string[] parts = id.Split('_');
            if (parts.Length != 3) continue;

            float cleanX = float.Parse(cols[1]);
            float cleanZ = float.Parse(cols[3]);
            int currentY = int.Parse(parts[2]);

            while (currentY >= 45)
            {
                currentY -= 30;
                float ix = Mathf.Round(cleanX * 10f);
                float iz = Mathf.Round(cleanZ * 10f);
                float iy = currentY;

                // ⭐ ID 생성 규칙 통일
                string strX = $"{(ix < 0 ? "-" : "0")}{Mathf.Abs(ix):000}";
                string strZ = $"{(iz < 0 ? "-" : "0")}{Mathf.Abs(iz):000}";
                string strY = $"{(iy < 0 ? "-" : "0")}{Mathf.Abs(iy):000}";

                string targetId = $"{strX}_{strZ}_{strY}";
                if (existingBlocks.Contains(targetId)) break;

                existingBlocks.Add(targetId);
                float targetY = currentY / 10f;
                planLines.Add($"{targetId},{cleanX:F2},{targetY:F2},{cleanZ:F2},Reinforcement");
            }
        }
        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 마스터 엑셀 도면 생성 완료!");
    }
}
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq; // ⭐ 이 줄이 빠져서 생긴 에러들을 해결합니다!

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
        var lines = File.ReadAllLines(stressCsvPath).ToList();
        if (lines.Count <= 1) return;

        HashSet<string> existingBlocks = new HashSet<string>();

        // 12칸 표준 규격으로 헤더 교체
        List<string> planLines = new List<string> { "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type" };

        for (int i = 1; i < lines.Count; i++)
        {
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',').ToList();
            if (cols.Count < 7) continue;

            string id = cols.ElementAt(0);
            existingBlocks.Add(id);

            float posY = float.Parse(cols.ElementAt(2));
            string typeStr = posY > 1.5f ? "Wall" : "Floor";

            // 12칸에 맞춰서 Existing(기존 블록) 저장
            string lineData = id + "," +
                              cols.ElementAt(1) + "," +
                              cols.ElementAt(2) + "," +
                              cols.ElementAt(3) + "," +
                              "0.00" + "," +
                              "Safe" + "," +
                              "N" + "," +
                              "Concrete" + "," +
                              "0.0" + "," +
                              "0.0" + "," +
                              "Existing" + "," +
                              typeStr;

            planLines.Add(lineData);
        }

        for (int i = 1; i < lines.Count; i++)
        {
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',').ToList();
            if (cols.Count < 7) continue;

            // 처방전(Prescription)이 Y인 블록만 보강
            if (cols.ElementAt(6) != "Y") continue;

            string id = cols.ElementAt(0);
            var parts = id.Split('_').ToList();
            if (parts.Count != 3) continue;

            float cleanX = float.Parse(cols.ElementAt(1));
            float cleanZ = float.Parse(cols.ElementAt(3));
            float currentY = float.Parse(parts.ElementAt(2));

            while (currentY >= 45f)
            {
                currentY -= 30f;
                float ix = Mathf.Round(cleanX * 10f);
                float iz = Mathf.Round(cleanZ * 10f);
                float iy = currentY;

                string strX = (ix < 0f ? "-" : "0") + Mathf.Abs(ix).ToString("000");
                string strZ = (iz < 0f ? "-" : "0") + Mathf.Abs(iz).ToString("000");
                string strY = (iy < 0f ? "-" : "0") + Mathf.Abs(iy).ToString("000");
                string targetId = strX + "_" + strZ + "_" + strY;

                if (!existingBlocks.Contains(targetId))
                {
                    float exactY = currentY / 10f;
                    string typeStr = exactY > 1.5f ? "Wall" : "Floor";

                    // 12칸에 맞춰서 Reinforcement(보강 철근) 저장
                    string newLineData = targetId + "," +
                                         cleanX.ToString("F2") + "," +
                                         exactY.ToString("F2") + "," +
                                         cleanZ.ToString("F2") + "," +
                                         "0.00" + "," +
                                         "Safe" + "," +
                                         "N" + "," +
                                         "Steel" + "," +
                                         "0.0" + "," +
                                         "0.0" + "," +
                                         "Reinforcement" + "," +
                                         typeStr;

                    planLines.Add(newLineData);
                    existingBlocks.Add(targetId);
                }
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [ReinforcementManager] 12칸 표준 도면 작성 완료! " + (planLines.Count - 1) + "개의 블록이 등록되었습니다.");
    }
}
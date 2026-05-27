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

            // ⭐ 12칸 데이터를 모두 읽어오도록 안전망 수정
            if (cols.Count < 12) continue;

            string id = cols.ElementAt(0);
            existingBlocks.Add(id);

            float posY = float.Parse(cols.ElementAt(2));
            string typeStr = posY > 1.5f ? "Wall" : "Floor";

            // ⭐ 하드코딩된 Concrete 제거! CurrentStress.csv의 재질과 강도를 그대로 읽어서 보존
            string lineData = id + "," +
                              cols.ElementAt(1) + "," +
                              cols.ElementAt(2) + "," +
                              cols.ElementAt(3) + "," +
                              "0.00" + "," +
                              "Safe" + "," +
                              "N" + "," +
                              cols.ElementAt(7) + "," +  // 재질 이름 유지
                              cols.ElementAt(8) + "," +  // 인장 강도 유지
                              cols.ElementAt(9) + "," +  // 압축 강도 유지
                              "Existing" + "," +
                              typeStr;

            planLines.Add(lineData);
        }

        for (int i = 1; i < lines.Count; i++)
        {
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',').ToList();
            if (cols.Count < 12) continue;

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
                float ix = Mathf.Round((cleanX + 0.001f) * 10f);
                float iz = Mathf.Round((cleanZ + 0.001f) * 10f);
                float iy = currentY;

                string strX = (ix < 0f ? "-" : "0") + Mathf.Abs(ix).ToString("000");
                string strZ = (iz < 0f ? "-" : "0") + Mathf.Abs(iz).ToString("000");
                string strY = (iy < 0f ? "-" : "0") + Mathf.Abs(iy).ToString("000");
                string targetId = strX + "_" + strZ + "_" + strY;

                if (!existingBlocks.Contains(targetId))
                {
                    float exactY = currentY / 10f;
                    string typeStr = exactY > 1.5f ? "Wall" : "Floor";

                    // 보강 철근은 무조건 Steel
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
        Debug.Log("📄 [ReinforcementManager] 12칸 표준 도면 작성 완료 (기존 재질 완벽 보존)!");
    }
}
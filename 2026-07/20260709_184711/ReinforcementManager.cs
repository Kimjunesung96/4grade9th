using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

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

            if (cols.Count < 12) continue;

            string id = cols.ElementAt(0);
            
            // ⭐ [버그 예방추가] 블록이 완전히 파괴(DESTROYED)된 데이터가 아닐 때만 기존 블록 장부에 등록!
            if (cols.ElementAt(1) != "DESTROYED")
            {
                existingBlocks.Add(id);
            }

            // 💥 [수정됨] 엑셀 칸이 "DESTROYED"일 수 있으므로...
            // 💥 [수정됨] 엑셀 칸이 "DESTROYED"일 수 있으므로, 무조건 안전한 ID에서 Y좌표를 추출!
            float posY = float.Parse(id.Split('_')[2]) / 10f;
            string typeStr = posY > 1.5f ? "Wall" : "Floor";

            // 💥 [수정됨] 새 도면에 "DESTROYED"라는 글자가 옮겨가는 것을 막고, ID에서 원래 좌표를 복구
            string safeX = cols.ElementAt(1) == "DESTROYED" ? (float.Parse(id.Split('_')[0]) / 10f).ToString("F2") : cols.ElementAt(1);
            string safeY = cols.ElementAt(2) == "DESTROYED" ? (float.Parse(id.Split('_')[2]) / 10f).ToString("F2") : cols.ElementAt(2);
            string safeZ = cols.ElementAt(3) == "DESTROYED" ? (float.Parse(id.Split('_')[1]) / 10f).ToString("F2") : cols.ElementAt(3);

            string lineData = id + "," +
                              safeX + "," +
                              safeY + "," +
                              safeZ + "," +
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

        // ⭐ [여기 추가!] UI에서 보강 옵션을 켰는지 확인
        bool shouldReinforce = true;
        if (BudgetUIManager.Instance != null)
        {
            shouldReinforce = BudgetUIManager.Instance.wantsReinforcement;
        }

        // ⭐ UI에서 체크했을 때만 아래의 보강 루프를 실행!
        if (shouldReinforce)
        {
            for (int i = 1; i < lines.Count; i++)
            {
                string currentLine = lines.ElementAt(i);
                var cols = currentLine.Split(',').ToList();
                if (cols.Count < 12) continue;

                // 처방전(Prescription)이 Y인 블록만 보강 (파괴된 블록도 Y로 기록되어 있음)
                if (cols.ElementAt(6) != "Y") continue;
                string id = cols.ElementAt(0);
                var parts = id.Split('_').ToList();
                if (parts.Count != 3) continue;

                // 엑셀 칸 무시하고 무조건 ID에서 안전하게 좌표 추출
                float cleanX = float.Parse(parts.ElementAt(0)) / 10f;
                float cleanZ = float.Parse(parts.ElementAt(1)) / 10f;
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
        } // ⭐ if (shouldReinforce) 끝나는 괄호

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [ReinforcementManager] 12칸 표준 도면 작성 완료 (UI 보강 옵션 연동 완벽 적용)!");
    }
}
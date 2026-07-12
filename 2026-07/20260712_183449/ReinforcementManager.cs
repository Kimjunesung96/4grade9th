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
        List<string> planLines = new List<string> { "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type" };

        for (int i = 1; i < lines.Count; i++)
        {
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',').ToList();

            if (cols.Count < 12) continue;

            string id = cols.ElementAt(0);
            
            if (cols.ElementAt(1) != "DESTROYED")
            {
                existingBlocks.Add(id);
            }

            float posY = float.Parse(id.Split('_')[2]) / 10f;
            string typeStr = posY > 1.5f ? "Wall" : "Floor";

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
                              cols.ElementAt(7) + "," +
                              cols.ElementAt(8) + "," +
                              cols.ElementAt(9) + "," +
                              "Existing" + "," +
                              typeStr;

           planLines.Add(lineData);
        }

        bool shouldReinforce = true;
        if (BudgetUIManager.Instance != null)
        {
            shouldReinforce = BudgetUIManager.Instance.wantsReinforcement;
        }

        if (shouldReinforce)
        {
            List<(Vector3 pos, bool isDanger)> flaggedPoints = new List<(Vector3 pos, bool isDanger)>();

            for (int i = 1; i < lines.Count; i++)
            {
                var cols = lines[i].Split(',').ToList();
                if (cols.Count < 12 || cols[5] != "Danger") continue; 

                string id = cols[0];
                var parts = id.Split('_').ToList();
                if (parts.Count != 3) continue;

                float cleanX = float.Parse(parts[0]) / 10f;
                float cleanZ = float.Parse(parts[1]) / 10f;
                float currentY = float.Parse(parts[2]); 

                flaggedPoints.Add((new Vector3(cleanX, currentY, cleanZ), true));
            }

            if (flaggedPoints.Count > 0)
            {
                // ⭐ 바둑판 교차점(절대 격자)을 기록할 딕셔너리
                Dictionary<Vector2, float> gridColumns = new Dictionary<Vector2, float>();

                foreach (var p in flaggedPoints)
                {
                    // ⭐ 핵심 로직: 위험 구역의 좌표를 12.0f 단위의 바둑판 교차점으로 강제 스냅(Snap)!
                    float gridX = Mathf.Round(p.pos.x / 12.0f) * 12.0f;
                    float gridZ = Mathf.Round(p.pos.z / 12.0f) * 12.0f;
                    Vector2 gPos = new Vector2(gridX, gridZ);

                    // 해당 바둑판 교차점에서 가장 높은 위치를 기록
                    if (!gridColumns.ContainsKey(gPos) || p.pos.y > gridColumns[gPos])
                    {
                        gridColumns[gPos] = p.pos.y;
                    }
                }

                List<Vector2> builtColumnsXZ = gridColumns.Keys.ToList();

                // 1. 바둑판 교차점에 기둥(보강중심 + 팔) 세우기
                foreach (var col in builtColumnsXZ)
                {
                    float cleanX = col.x;
                    float cleanZ = col.y;
                    float currentY = gridColumns[col];

                    while (currentY >= 45f)
                    {
                        currentY -= 30f;
                        float exactY = currentY / 10f;
                        string typeStr = "Reinforcement";

                        List<Vector3> crossOffsets = new List<Vector3>()
                        {
                            new Vector3(0, 0, 0),       
                            new Vector3(3.0f, 0, 0),    
                            new Vector3(-3.0f, 0, 0),   
                            new Vector3(0, 0, 3.0f),    
                            new Vector3(0, 0, -3.0f)    
                        };

                        foreach (var offset in crossOffsets)
                        {
                            float targetX = cleanX + offset.x;
                            float targetZ = cleanZ + offset.z;

                            float ix = Mathf.Round((targetX + 0.001f) * 10f);
                            float iz = Mathf.Round((targetZ + 0.001f) * 10f);
                            float iy = currentY;

                            string strX = (ix < 0f ? "-" : "0") + Mathf.Abs(ix).ToString("000");
                            string strZ = (iz < 0f ? "-" : "0") + Mathf.Abs(iz).ToString("000");
                            string strY = (iy < 0f ? "-" : "0") + Mathf.Abs(iy).ToString("000");
                            string targetId = strX + "_" + strZ + "_" + strY;

                            if (!existingBlocks.Contains(targetId))
                            {
                                string newLineData = targetId + "," +
                                                     targetX.ToString("F2") + "," +
                                                     exactY.ToString("F2") + "," +
                                                     targetZ.ToString("F2") + "," +
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
                }

                // 2. 바둑판 선(수평 보) 연결하기
                for (int i = 0; i < builtColumnsXZ.Count; i++)
                {
                    for (int j = i + 1; j < builtColumnsXZ.Count; j++)
                    {
                        Vector2 colA = builtColumnsXZ[i];
                        Vector2 colB = builtColumnsXZ[j];

                        float dx = Mathf.Abs(colA.x - colB.x);
                        float dz = Mathf.Abs(colA.y - colB.y);

                        // 바둑판의 직선으로 인접한(12.0f 거리) 교차점들만 연결
                        bool isHorizontalNeighbor = (dx > 11.9f && dx < 12.1f) && dz < 0.1f;
                        bool isVerticalNeighbor   = (dz > 11.9f && dz < 12.1f) && dx < 0.1f;

                        if (isHorizontalNeighbor || isVerticalNeighbor)
                        {
                            float meshY = Mathf.Max(gridColumns[colA], gridColumns[colB]);
                            
                            while (meshY >= 45f)
                            {
                                float exactY = meshY / 10f;
                                string typeStr = "Reinforcement";

                                // 4스텝으로 나누어 정중앙(s=2)에만 징검다리 놓기
                                int steps = 4; 
                                for (int s = 1; s < steps; s++) 
                                {
                                    float t = (float)s / steps;
                                    Vector2 interpXZ = Vector2.Lerp(colA, colB, t);

                                    float ix = Mathf.Round((interpXZ.x + 0.001f) * 10f);
                                    float iz = Mathf.Round((interpXZ.y + 0.001f) * 10f); 
                                    float iy = meshY;

                                    string strX = (ix < 0f ? "-" : "0") + Mathf.Abs(ix).ToString("000");
                                    string strZ = (iz < 0f ? "-" : "0") + Mathf.Abs(iz).ToString("000");
                                    string strY = (iy < 0f ? "-" : "0") + Mathf.Abs(iy).ToString("000");
                                    string targetId = strX + "_" + strZ + "_" + strY;

                                    if (!existingBlocks.Contains(targetId))
                                    {
                                        string newLineData = targetId + "," +
                                                             interpXZ.x.ToString("F2") + "," +
                                                             exactY.ToString("F2") + "," +
                                                             interpXZ.y.ToString("F2") + "," +
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
                                // 3층마다 수평 보 연결
                                meshY -= 90f; 
                            }
                        }
                    }
                }
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [ReinforcementManager] 절대 격자(바둑판) 스냅 및 3층 간격 징검다리 보강 완료!");
    }
}
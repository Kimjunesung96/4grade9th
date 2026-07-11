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
                flaggedPoints = flaggedPoints.OrderBy(p => Vector2.Distance(new Vector2(p.pos.x, p.pos.z), Vector2.zero)).ToList();

                List<Vector2> builtColumnsXZ = new List<Vector2>();
                Dictionary<Vector2, float> columnStartHeight = new Dictionary<Vector2, float>();

                foreach (var p in flaggedPoints)
                {
                    Vector2 currentXZ = new Vector2(p.pos.x, p.pos.z);
                    bool inForbiddenZone = false;

                    // ⭐ 12.0f(4칸) 간격 격자. 팔(arm) 사이에 딱 1칸이 남게 됨.
                    foreach (var built in builtColumnsXZ)
                    {
                        float dx = Mathf.Abs(currentXZ.x - built.x);
                        float dz = Mathf.Abs(currentXZ.y - built.y);
                        
                        if (dx < 11.9f && dz < 11.9f)
                        {
                            inForbiddenZone = true;
                            break;
                        }
                    }

                    if (inForbiddenZone) continue; 

                    builtColumnsXZ.Add(currentXZ);
                    columnStartHeight[currentXZ] = p.pos.y;

                    float cleanX = p.pos.x;
                    float cleanZ = p.pos.z;
                    float currentY = p.pos.y;

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

                for (int i = 0; i < builtColumnsXZ.Count; i++)
                {
                    for (int j = i + 1; j < builtColumnsXZ.Count; j++)
                    {
                        Vector2 colA = builtColumnsXZ[i];
                        Vector2 colB = builtColumnsXZ[j];

                        float dx = Mathf.Abs(colA.x - colB.x);
                        float dz = Mathf.Abs(colA.y - colB.y);

                        // ⭐ 12.0f(4칸) 거리에 있는 이웃 기둥 찾기
                        bool isHorizontalNeighbor = (dx > 11.9f && dx < 12.1f) && dz < 0.1f;
                        bool isVerticalNeighbor   = (dz > 11.9f && dz < 12.1f) && dx < 0.1f;

                        if (isHorizontalNeighbor || isVerticalNeighbor)
                        {
                            float meshY = Mathf.Max(columnStartHeight[colA], columnStartHeight[colB]);
                            
                            while (meshY >= 45f)
                            {
                                float exactY = meshY / 10f;
                                string typeStr = "Reinforcement";

                                // ⭐ 12.0 / 3.0 = 4스텝! (징검다리는 1~3번째 블록 위치를 계산)
                                // s=1 지점(3.0f)은 기둥 팔과 겹치고, s=3 지점(9.0f)도 상대 기둥 팔과 겹쳐서 건너뜀.
                                // s=2 지점(6.0f)만 비어있으므로 딱 이어지는 블록 1개가 생성됨.
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
                                // ⭐ 3층(90.0f) 간격으로 수평 보 연결
                                meshY -= 90f; 
                            }
                        }
                    }
                }
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [ReinforcementManager] 4칸(12.0f) 격자, 1칸 빈틈 3층 간격 보강 적용 완료!");
    }
}
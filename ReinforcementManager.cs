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

            // ⭐ [구멍 버그 수정] 이미 무너진(Destroyed) 블록은 existingBlocks에서 제외.
            //    안 그러면 "그 자리엔 이미 블록이 있다"고 착각해서 보강재를 안 놓게 되고,
            //    원본도 없고(이미 무너짐) 보강재도 안 생기니(existingBlocks에 있다고 착각) 영구 구멍이 됨.
            //    하필 응력이 제일 몰려 파괴된, 보강이 제일 필요한 자리에서 이 문제가 집중적으로 발생함.
            string riskLevel = cols.Count > 5 ? cols[5].Trim() : "";
            bool isDestroyed = riskLevel.EndsWith("_Destroyed");
            if (isDestroyed) continue;

            string id = cols.ElementAt(0);
            float px = float.Parse(id.Split('_')[0]) / 10f;
            float pz = float.Parse(id.Split('_')[1]) / 10f;
            float py = float.Parse(id.Split('_')[2]) / 10f;

            // ⭐ 스포너와 완벽히 동일한 사사오입(Floor + 0.5f) 방식으로 통일
            float snapX = Mathf.Floor((px - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            float snapZ = Mathf.Floor((pz - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            float snapY = Mathf.Floor((py - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;
            
            float ix = Mathf.Round((snapX + 0.001f) * 10f);
            float iz = Mathf.Round((snapZ + 0.001f) * 10f);
            float iy = Mathf.Round((snapY + 0.001f) * 10f);
            string cleanId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

            existingBlocks.Add(cleanId);
        }

        bool shouldReinforce = true;
        int currentMode = 1; 

        if (BudgetUIManager.Instance != null)
        {
            shouldReinforce = BudgetUIManager.Instance.wantsReinforcement;
            currentMode = BudgetUIManager.Instance.reinforcementMode;
        }

        string reinforceMat = "Steel";
        if (BudgetUIManager.Instance != null) reinforceMat = BudgetUIManager.Instance.reinforcementMaterial;

        if (shouldReinforce)
        {
            // ⭐ 마의 구간 탈출: 은행원 반올림(Round) 대신 진짜 반올림(Floor + 0.5f) 적용!
            float Snap(float val) => Mathf.Floor((val - 1.5f) / 3.0f + 0.5f) * 3.0f + 1.5f;

            List<(Vector3 pos, bool isDanger)> flaggedPoints = new List<(Vector3 pos, bool isDanger)>();
            List<Vector3> quakePoints = new List<Vector3>();

            for (int i = 1; i < lines.Count; i++)
            {
                var cols = lines[i].Split(',').ToList();
                if (cols.Count < 12) continue; 

                string id = cols[0];
                var parts = id.Split('_').ToList();
                if (parts.Count != 3) continue;

                float cleanX = float.Parse(parts[0]) / 10f;
                float cleanZ = float.Parse(parts[1]) / 10f;
                float rawY = float.Parse(parts[2]);

                if (cols[5] == "Danger" || cols[5] == "Danger_Destroyed")
                    flaggedPoints.Add((new Vector3(cleanX, rawY, cleanZ), true));
                else if (cols[5] == "Quake_Danger" || cols[5] == "Quake_Destroyed" || cols[5] == "Explosion_Danger" || cols[5] == "Explosion_Destroyed")
                    quakePoints.Add(new Vector3(cleanX, rawY, cleanZ));
            }

            if (quakePoints.Count > 0)
            {
                float minX = 9999f, minZ = 9999f, maxX = -9999f, maxZ = -9999f;
                foreach (var id in existingBlocks)
                {
                    var parts = id.Split('_');
                    if (parts.Length < 3) continue;
                    float py = float.Parse(parts[2]) / 10f;
                    if (py <= 1.5f) continue; 
                    float px = float.Parse(parts[0]) / 10f;
                    float pz = float.Parse(parts[1]) / 10f;
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (pz < minZ) minZ = pz; if (pz > maxZ) maxZ = pz;
                }
                Vector2 buildingCenter = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);
                List<Vector2> builtTowers = new List<Vector2>();
                var sortedQuake = quakePoints.OrderByDescending(p => p.y).ToList();

                foreach (var p in sortedQuake)
                {
                    Vector2 targetXZ = new Vector2(p.x, p.z);
                    if (builtTowers.Any(t => Vector2.Distance(targetXZ, t) < 14.9f)) continue;
                    builtTowers.Add(targetXZ);

                    Vector2 dir = (targetXZ - buildingCenter).normalized;
                    if (dir == Vector2.zero) dir = new Vector2(1, 0);

                    if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)) dir = new Vector2(Mathf.Sign(dir.x), 0f);
                    else dir = new Vector2(0f, Mathf.Sign(dir.y));

                    float towerX = Snap(p.x + dir.x * 12.0f);
                    float towerZ = Snap(p.z + dir.y * 12.0f);
                    float targetY = p.y / 10f; 

                    float wallX = dir.x != 0f ? p.x : towerX;
                    float wallZ = dir.y != 0f ? p.z : towerZ;

                    float dx = towerX - wallX; float dz = towerZ - wallZ;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz) / 3.0f));

                    for (float ty = 1.5f; ty <= targetY; ty += 3.0f)
                    {
                        float ix = Mathf.Round((towerX + 0.001f) * 10f); float iz = Mathf.Round((towerZ + 0.001f) * 10f); float iy = Mathf.Round((ty + 0.001f) * 10f);
                        string tId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";
                        if (!existingBlocks.Contains(tId))
                        {
                            planLines.Add($"{tId},{towerX:F2},{ty:F2},{towerZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                            existingBlocks.Add(tId);
                        }

                        if (currentMode == 1)
                        {
                            Vector2[] crossOffsetsB = new Vector2[] { new Vector2(3f, 0f), new Vector2(-3f, 0f), new Vector2(0f, 3f), new Vector2(0f, -3f) };
                            foreach (var off in crossOffsetsB)
                            {
                                float armX = Snap(towerX + off.x); float armZ = Snap(towerZ + off.y);
                                float aix = Mathf.Round((armX + 0.001f) * 10f); float aiz = Mathf.Round((armZ + 0.001f) * 10f);
                                string aId = $"{(aix < 0f ? "-" : "0")}{Mathf.Abs(aix):000}_{(aiz < 0f ? "-" : "0")}{Mathf.Abs(aiz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";
                                if (!existingBlocks.Contains(aId))
                                {
                                    planLines.Add($"{aId},{armX:F2},{ty:F2},{armZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                    existingBlocks.Add(aId);
                                }
                            }
                        }

                        int floorIndex = Mathf.RoundToInt((ty - 1.5f) / 3.0f);
                        if (floorIndex % 2 == 0)
                        {
                            for (int s = 1; s < steps; s++)
                            {
                                float bridgeX = Snap(wallX + (dx * s / steps));
                                float bridgeZ = Snap(wallZ + (dz * s / steps));
                                float bix = Mathf.Round((bridgeX + 0.001f) * 10f); float biz = Mathf.Round((bridgeZ + 0.001f) * 10f); float biy = Mathf.Round((ty + 0.001f) * 10f);
                                string bId = $"{(bix < 0f ? "-" : "0")}{Mathf.Abs(bix):000}_{(biz < 0f ? "-" : "0")}{Mathf.Abs(biz):000}_{(biy < 0f ? "-" : "0")}{Mathf.Abs(biy):000}";
                                if (!existingBlocks.Contains(bId))
                                {
                                    planLines.Add($"{bId},{bridgeX:F2},{ty:F2},{bridgeZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                    existingBlocks.Add(bId);
                                }
                            }
                        }
                    }
                }
            }

            if (flaggedPoints.Count > 0 && currentMode == 1)
            {
                Dictionary<Vector2, float> gridColumns = new Dictionary<Vector2, float>();
                foreach (var p in flaggedPoints)
                {
                    float gridX = Mathf.Round(p.pos.x / 12.0f) * 12.0f;
                    float gridZ = Mathf.Round(p.pos.z / 12.0f) * 12.0f;
                    Vector2 gPos = new Vector2(gridX, gridZ);
                    if (!gridColumns.ContainsKey(gPos) || p.pos.y > gridColumns[gPos]) gridColumns[gPos] = p.pos.y;
                }

                List<Vector2> builtColumnsXZ = gridColumns.Keys.ToList();

                foreach (var col in builtColumnsXZ)
                {
                    float cleanX = col.x; float cleanZ = col.y; float currentY = gridColumns[col];
                    while (currentY >= 45f)
                    {
                        currentY -= 30f; float exactY = currentY / 10f;
                        List<Vector3> crossOffsets = new List<Vector3>() { new Vector3(0,0,0), new Vector3(3f,0,0), new Vector3(-3f,0,0), new Vector3(0,0,3f), new Vector3(0,0,-3f) };
                        
                        foreach (var offset in crossOffsets)
                        {
                            float targetX = Snap(cleanX + offset.x); float targetZ = Snap(cleanZ + offset.z);
                            float ix = Mathf.Round((targetX + 0.001f) * 10f); float iz = Mathf.Round((targetZ + 0.001f) * 10f); float iy = currentY;
                            string targetId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

                            if (!existingBlocks.Contains(targetId))
                            {
                                planLines.Add($"{targetId},{targetX:F2},{exactY:F2},{targetZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                existingBlocks.Add(targetId);
                            }
                        }
                    }
                }

                for (int i = 0; i < builtColumnsXZ.Count; i++)
                {
                    for (int j = i + 1; j < builtColumnsXZ.Count; j++)
                    {
                        Vector2 colA = builtColumnsXZ[i]; Vector2 colB = builtColumnsXZ[j];
                        float dx = Mathf.Abs(colA.x - colB.x); float dz = Mathf.Abs(colA.y - colB.y);

                        if (((dx > 11.9f && dx < 12.1f) && dz < 0.1f) || ((dz > 11.9f && dz < 12.1f) && dx < 0.1f))
                        {
                            float meshY = Mathf.Max(gridColumns[colA], gridColumns[colB]);
                            while (meshY >= 45f)
                            {
                                float exactY = meshY / 10f;
                                for (int s = 1; s < 4; s++) 
                                {
                                    Vector2 interpXZ = Vector2.Lerp(colA, colB, (float)s / 4);
                                    float interpX = Snap(interpXZ.x); float interpZ = Snap(interpXZ.y);
                                    float ix = Mathf.Round((interpX + 0.001f) * 10f); float iz = Mathf.Round((interpZ + 0.001f) * 10f); float iy = meshY;
                                    string targetId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

                                    if (!existingBlocks.Contains(targetId))
                                    {
                                        planLines.Add($"{targetId},{interpX:F2},{exactY:F2},{interpZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                        existingBlocks.Add(targetId);
                                    }
                                }
                                meshY -= 90f; 
                            }
                        }
                    }
                }
            }

            if (flaggedPoints.Count > 0 && currentMode == 2)
            {
                List<Vector2> umbrellaCenters = new List<Vector2>();
                var sortedPoints = flaggedPoints.OrderByDescending(p => p.pos.y).ToList();

                foreach (var p in sortedPoints)
                {
                    Vector2 currentXZ = new Vector2(p.pos.x, p.pos.z);
                    if (umbrellaCenters.Any(u => Vector2.Distance(currentXZ, u) < 14.9f)) continue;

                    umbrellaCenters.Add(currentXZ);
                    float currentY = p.pos.y;
                    bool isFirstTopLayer = true;

                    while (currentY >= 45f)
                    {
                        currentY -= 30f; float exactY = currentY / 10f;
                        List<Vector3> offsets = new List<Vector3>() { new Vector3(0,0,0) };
                        if (isFirstTopLayer)
                        {
                            offsets.Add(new Vector3(3f,0,0)); offsets.Add(new Vector3(-3f,0,0));
                            offsets.Add(new Vector3(0,0,3f)); offsets.Add(new Vector3(0,0,-3f));
                            isFirstTopLayer = false;
                        }

                        foreach (var offset in offsets)
                        {
                            float targetX = Snap(currentXZ.x + offset.x); float targetZ = Snap(currentXZ.y + offset.z);
                            float ix = Mathf.Round((targetX + 0.001f) * 10f); float iz = Mathf.Round((targetZ + 0.001f) * 10f); float iy = currentY;
                            string targetId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

                            if (!existingBlocks.Contains(targetId))
                            {
                                planLines.Add($"{targetId},{targetX:F2},{exactY:F2},{targetZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                existingBlocks.Add(targetId);
                            }
                        }
                    }
                }
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log($"📄 [장부 완성] 새로 추가될 보강재 {planLines.Count - 1}개 기록 완료 (원본 블록은 건드리지 않습니다).");
    }
}
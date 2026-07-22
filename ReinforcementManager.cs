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
            // (기존 블록 데이터 읽어오는 부분 - 생략 없이 그대로 유지)
            string currentLine = lines.ElementAt(i);
            var cols = currentLine.Split(',').ToList();
            if (cols.Count < 12) continue;

            string id = cols.ElementAt(0);
            if (cols.ElementAt(1) != "DESTROYED") existingBlocks.Add(id);

            float posY = float.Parse(id.Split('_')[2]) / 10f;
            string typeStr = posY > 1.5f ? "Wall" : "Floor";

            string safeX = cols.ElementAt(1) == "DESTROYED" ? (float.Parse(id.Split('_')[0]) / 10f).ToString("F2") : cols.ElementAt(1);
string safeY = cols.ElementAt(2) == "DESTROYED" ? (float.Parse(id.Split('_')[2]) / 10f).ToString("F2") : cols.ElementAt(2);
            string safeZ = cols.ElementAt(3) == "DESTROYED" ? (float.Parse(id.Split('_')[1]) / 10f).ToString("F2") : cols.ElementAt(3);

            // ⭐ [해결!] 과거의 자신이 보강재(Reinforcement)였는지 기억을 유지하도록 currentTool 변수를 부활시킵니다!
            string currentTool = cols.Count > 10 ? cols.ElementAt(10).Trim() : "Existing";

            // ⭐ 무조건 "Existing"이라고 박아버리던 멍청한 코드를 수정하여 원래 신분을 유지하게 만듭니다.
            string lineData = id + "," + safeX + "," + safeY + "," + safeZ + "," +
                              "0.00,Safe,N," + cols.ElementAt(7) + "," + cols.ElementAt(8) + "," + cols.ElementAt(9) + "," + currentTool + "," + typeStr;
           planLines.Add(lineData);
        }

        // ⭐ UI 연동 부분!
        bool shouldReinforce = true;
        int currentMode = 1; // 1: 물량 바둑판, 2: 가성비 우산

        if (BudgetUIManager.Instance != null)
        {
            shouldReinforce = BudgetUIManager.Instance.wantsReinforcement;
            currentMode = BudgetUIManager.Instance.reinforcementMode;
        }

        // ⭐ [신규] 보강 블록 재질을 UI에서 고른 값으로 (기본값 Steel, UI 있으면 선택된 걸로 덮어씀 - 지금은 H_Beam)
        string reinforceMat = "Steel";
        if (BudgetUIManager.Instance != null) reinforceMat = BudgetUIManager.Instance.reinforcementMaterial;

       if (shouldReinforce)
            {
                // Danger(V) → UI토글로 그리드/우산 선택, Quake_Danger(B) → 아웃리거 타워, Explosion_Danger(N) → 미정
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
                    float currentY = float.Parse(parts[2]); 

                    if (cols[5] == "Danger" || cols[5] == "Danger_Destroyed")
                    {
                        flaggedPoints.Add((new Vector3(cleanX, currentY, cleanZ), true));
                    }
                    else if (cols[5] == "Quake_Danger" || cols[5] == "Quake_Destroyed" || cols[5] == "Explosion_Danger" || cols[5] == "Explosion_Destroyed")
                    {
                        quakePoints.Add(new Vector3(cleanX, currentY, cleanZ));
                    }
                }

                // ==========================================================
                // 🚀 [신규 모드] 로켓 도킹 타워 (Outrigger) 보강 로직 (Quake/Explosion_Danger 전용)
                // ==========================================================
                if (quakePoints.Count > 0)
                {
                    float minX = 9999f, minZ = 9999f, maxX = -9999f, maxZ = -9999f;
                    foreach (var id in existingBlocks)
                    {
                        var parts = id.Split('_');
                        if (parts.Length < 3) continue;
                        
                        // ⭐ 거대한 바닥(Floor) 블록은 건물 중심 계산에서 제외하여 정확한 외벽 방향 도출!
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
                        
                        // 타워 간격 제한: 반경 15m(5칸) 이내 중복 건설 방지
                        if (builtTowers.Any(t => Vector2.Distance(targetXZ, t) < 14.9f)) continue;
                        builtTowers.Add(targetXZ);

                        // 바깥 방향으로 4칸(12m) 이격된 타워 중심 계산
                        Vector2 dir = (targetXZ - buildingCenter).normalized;
                        if (dir == Vector2.zero) dir = new Vector2(1, 0); // 중심일 경우 예외처리

                        // ⭐ [대각선 방지] 방향을 가장 가까운 축(X 또는 Z) 하나로 스냅 → 연결 다리가 항상 직선이 되게
                        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)) dir = new Vector2(Mathf.Sign(dir.x), 0f);
                        else dir = new Vector2(0f, Mathf.Sign(dir.y));

                        float towerX = Mathf.Round((p.x + dir.x * 12.0f - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        float towerZ = Mathf.Round((p.z + dir.y * 12.0f - 1.5f) / 3.0f) * 3.0f + 1.5f;
                        float targetY = p.y / 10f; // ⭐ 루트 코즈 수정: p.y는 raw(×10) 값이라 실좌표로 나눠줘야 함 (안 그러면 기둥이 건물 10배 높이로 솟음)

                        // dir이 축 정렬됐으니 벽 쪽 좌표도 타워와 같은 축으로 맞춰줌 (진짜 직선 보장)
                        float wallX = dir.x != 0f ? p.x : towerX;
                        float wallZ = dir.y != 0f ? p.z : towerZ;

                        // 수직 기둥(타워) + 매 층마다 수평 도킹 암 동시 타설
                        float dx = towerX - wallX; float dz = towerZ - wallZ;
                        int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz) / 3.0f));

                        for (float ty = 1.5f; ty <= targetY; ty += 3.0f)
                        {
                            float ix = Mathf.Round(towerX * 10f); float iz = Mathf.Round(towerZ * 10f); float iy = Mathf.Round(ty * 10f);
                            string tId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";
                            if (!existingBlocks.Contains(tId))
                            {
                                planLines.Add($"{tId},{towerX:F2},{ty:F2},{towerZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                existingBlocks.Add(tId);
                            }

                            // ⭐ [B 많이 추가] "많이[격자]"(currentMode==1)일 때만 아웃리거 타워도 십자(+) 단면으로 두껍게
                            if (currentMode == 1)
                            {
                                Vector2[] crossOffsetsB = new Vector2[] { new Vector2(3f, 0f), new Vector2(-3f, 0f), new Vector2(0f, 3f), new Vector2(0f, -3f) };
                                foreach (var off in crossOffsetsB)
                                {
                                    float armX = towerX + off.x; float armZ = towerZ + off.y;
                                    float aix = Mathf.Round(armX * 10f); float aiz = Mathf.Round(armZ * 10f);
                                    string aId = $"{(aix < 0f ? "-" : "0")}{Mathf.Abs(aix):000}_{(aiz < 0f ? "-" : "0")}{Mathf.Abs(aiz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";
                                    if (!existingBlocks.Contains(aId))
                                    {
                                        planLines.Add($"{aId},{armX:F2},{ty:F2},{armZ:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                        existingBlocks.Add(aId);
                                    }
                                }
                            }

                            // ⭐ [연결-쉬고-연결 반복] 층 인덱스가 짝수일 때만 도킹암 연결, 홀수 층은 스킵
                            int floorIndex = Mathf.RoundToInt((ty - 1.5f) / 3.0f);
                            if (floorIndex % 2 == 0)
                            {
                                for (int s = 2; s < steps; s++)
                                {
                                    float bridgeX = Mathf.Round((wallX + (dx * s / steps) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                                    float bridgeZ = Mathf.Round((wallZ + (dz * s / steps) - 1.5f) / 3.0f) * 3.0f + 1.5f;
                                    float bix = Mathf.Round(bridgeX * 10f); float biz = Mathf.Round(bridgeZ * 10f); float biy = Mathf.Round(ty * 10f);
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
                    UnityEngine.Debug.Log("🚀 [로켓 도킹 타워] 지진 대비 외부 아웃리거 보강 도면 생성 완료!");
                }

                // ==========================================================
                // 🏗️ [Danger, UI모드1] 물량 공세 바둑판(Grid) 보강 로직
                // ==========================================================
                if (flaggedPoints.Count > 0 && currentMode == 1)
                {
                    Dictionary<Vector2, float> gridColumns = new Dictionary<Vector2, float>();

                    foreach (var p in flaggedPoints)
                    {
                        float gridX = Mathf.Round(p.pos.x / 12.0f) * 12.0f;
                        float gridZ = Mathf.Round(p.pos.z / 12.0f) * 12.0f;
                        Vector2 gPos = new Vector2(gridX, gridZ);

                        if (!gridColumns.ContainsKey(gPos) || p.pos.y > gridColumns[gPos])
                            gridColumns[gPos] = p.pos.y;
                    }

                    List<Vector2> builtColumnsXZ = gridColumns.Keys.ToList();

                    // 1-1. 바둑판 기둥 세우기 (십자 모양)
                    foreach (var col in builtColumnsXZ)
                    {
                        float cleanX = col.x; float cleanZ = col.y; float currentY = gridColumns[col];
                        while (currentY >= 45f)
                        {
                            currentY -= 30f;
                            float exactY = currentY / 10f;
                            List<Vector3> crossOffsets = new List<Vector3>() { new Vector3(0,0,0), new Vector3(3f,0,0), new Vector3(-3f,0,0), new Vector3(0,0,3f), new Vector3(0,0,-3f) };
                            
                            foreach (var offset in crossOffsets)
                            {
                                float targetX = cleanX + offset.x; float targetZ = cleanZ + offset.z;
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

                    // 1-2. 3층마다 바둑판 수평 보 연결
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
                                        float ix = Mathf.Round((interpXZ.x + 0.001f) * 10f); float iz = Mathf.Round((interpXZ.y + 0.001f) * 10f); float iy = meshY;
                                        string targetId = $"{(ix < 0f ? "-" : "0")}{Mathf.Abs(ix):000}_{(iz < 0f ? "-" : "0")}{Mathf.Abs(iz):000}_{(iy < 0f ? "-" : "0")}{Mathf.Abs(iy):000}";

                                        if (!existingBlocks.Contains(targetId))
                                        {
                                            planLines.Add($"{targetId},{interpXZ.x:F2},{exactY:F2},{interpXZ.y:F2},0.00,Safe,N,{reinforceMat},0.0,0.0,Reinforcement,Reinforcement");
                                            existingBlocks.Add(targetId);
                                        }
                                    }
                                    meshY -= 90f; 
                                }
                            }
                        }
                    }
                    Debug.Log("📄 [모드 1] 절대 격자(바둑판) 스냅 및 3층 간격 징검다리 보강 완료!");
                }

                // ==========================================================
                // ☂️ [Danger, UI모드2] 가성비 핀포인트 우산(Mushroom) 보강 로직
                // ==========================================================
                if (flaggedPoints.Count > 0 && currentMode == 2)
                {
                    List<Vector2> umbrellaCenters = new List<Vector2>();

                    // 빨간 구역들 중 가장 높은 곳부터 처리
                    var sortedPoints = flaggedPoints.OrderByDescending(p => p.pos.y).ToList();

                    foreach (var p in sortedPoints)
                    {
                        Vector2 currentXZ = new Vector2(p.pos.x, p.pos.z);
                        
                        // ⭐ 강제 거리 제한: 15.0f(5칸) 안에는 다른 우산을 절대 못 폄! (블록 극한 절약)
                        if (umbrellaCenters.Any(u => Vector2.Distance(currentXZ, u) < 14.9f)) continue;

                        umbrellaCenters.Add(currentXZ);
                        float currentY = p.pos.y;
                        bool isFirstTopLayer = true;

                        while (currentY >= 45f)
                        {
                            currentY -= 30f;
                            float exactY = currentY / 10f;
                            
                            // ⭐ 천장에 닿는 맨 위층(1겹)만 십자(+) 모양으로 하중을 받음 (우산 머리)
                            // 그 아래층들은 오직 중앙(1x1) 얇은 뼈대 하나만 내려감 (우산 손잡이)
                            List<Vector3> offsets = new List<Vector3>() { new Vector3(0,0,0) };
                            if (isFirstTopLayer)
                            {
                                offsets.Add(new Vector3(3f,0,0)); offsets.Add(new Vector3(-3f,0,0));
                                offsets.Add(new Vector3(0,0,3f)); offsets.Add(new Vector3(0,0,-3f));
                                isFirstTopLayer = false;
                            }

                            foreach (var offset in offsets)
                            {
                                float targetX = currentXZ.x + offset.x; float targetZ = currentXZ.y + offset.z;
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
                    Debug.Log("📄 [모드 2] 최소 블록 가성비 우산(Mushroom) 보강 완료!");
                }
            }

        File.WriteAllLines(planCsvPath, planLines);
    }
}
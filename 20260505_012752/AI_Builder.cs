using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
// 🚨 철거된 AI_Architect에서 구출해온 장부 양식
// 🚨 규격 수정 완료! (ActionLog 대신 AiBlock 사용)
[System.Serializable]
public class SplitTempData
{
    public List<AiBlock> Current_Blocks = new List<AiBlock>();
    public List<AiBlock> Blocks = new List<AiBlock>();
}
[Serializable]
public class AiBlock { public string tool; public string sizeXZH; public string centerXYZ; public string pivotXYZ; }

[Serializable]
public class TempLogWrapper { public string Notice; public List<AiBlock> Current_Blocks; }



public class AI_Builder : MonoBehaviour
{
    [Header("현장 자재")]
    public GameObject hologramPrefab;

    public bool isAiHologramActive = false;
    public static bool isAiWorking = false;

    public struct AiBuildCommand
    {
        public int mode;
        public float3 startPos;
        public float3 endPos;
        public float height;
        public float exactY;
    }
    public static Queue<AiBuildCommand> buildQueue = new Queue<AiBuildCommand>();

    private List<GameObject> activeHolograms = new List<GameObject>();
    private Vector3 currentBlueprintCenter;

    private List<float> ParseFloatList(string r)
    {
        try { return r.Replace("[", "").Replace("]", "").Split(',').Select(s => float.Parse(s.Trim())).ToList(); }
        catch { return null; }
    }

    public class BuildTask
    {
        public bool isTemp;
        public float bottomY;
        public float topY;
        public float h;
        public AiBlock block;
    }

    void Update()
    {
        isAiWorking = isAiHologramActive;

        //if (Input.GetKeyDown(KeyCode.Y)) { CancelManualDrag(); LoadAndSpawnHolograms("Option_A"); }
      //  if (Input.GetKeyDown(KeyCode.U)) { CancelManualDrag(); LoadAndSpawnHolograms("Option_B"); }
       // if (Input.GetKeyDown(KeyCode.I)) { CancelManualDrag(); LoadAndSpawnHolograms("Option_C"); }
      //  if (Input.GetKeyDown(KeyCode.L)) { CancelManualDrag(); LoadCurrentTempLog(); }
        if (Input.GetKeyDown(KeyCode.R)) { CancelManualDrag(); CancelHolograms(); }

        if (isAiHologramActive)
        {
            if (Input.GetMouseButtonDown(1))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit)) MoveHologramsTo(hit.point);
            }
            if (Input.GetKeyDown(KeyCode.G)) ConfirmAiBuild();
            if (Input.GetKeyDown(KeyCode.Escape)) CancelHolograms();
        }
    }

    private void LoadCurrentTempLog()
    {
        CancelHolograms();
        string tempPath = Path.Combine(Application.dataPath, "../BuildingLogs/temp/Temp_Current_Building.json");
        if (!File.Exists(tempPath)) return;
        try
        {
            string jsonRaw = File.ReadAllText(tempPath);
            TempLogWrapper tempData = JsonUtility.FromJson<TempLogWrapper>(jsonRaw);
            if (tempData != null && tempData.Current_Blocks != null) SpawnHologramsFromAiBlocks(tempData.Current_Blocks);
        }
        catch (Exception e) { Debug.LogError(e.Message); }
    }

    private void LoadAndSpawnHolograms(string optionName)
    {
        CancelHolograms();
        List<BuildTask> allTasks = new List<BuildTask>();

        string tempPath = Path.Combine(Application.dataPath, "temp", "Temp_Current_Building.json");
        if (File.Exists(tempPath))
        {
            SplitTempData tempData = JsonUtility.FromJson<SplitTempData>(File.ReadAllText(tempPath));
            if (tempData != null && tempData.Current_Blocks != null)
            {
                foreach (var b in tempData.Current_Blocks)
                {
                    var task = CreateTask(b, true);
                    if (task != null) allTasks.Add(task);
                }
            }
        }

        string optionFileName = optionName.Replace("Option", "option") + ".json";
        string targetPath = Path.Combine(Application.dataPath, "ai_designs", optionFileName);
        if (File.Exists(targetPath))
        {
            SplitTempData optData = JsonUtility.FromJson<SplitTempData>(File.ReadAllText(targetPath));
            if (optData != null && optData.Current_Blocks != null)
            {
                foreach (var b in optData.Current_Blocks)
                {
                    var task = CreateTask(b, false);
                    if (task != null) allTasks.Add(task);
                }
            }
        }

        var sortedTasks = allTasks.OrderBy(t => t.bottomY).ThenByDescending(t => t.isTemp).ToList();

        Dictionary<Vector2Int, float> skylineGrid = new Dictionary<Vector2Int, float>();
        float currentSkylineY = 0f;
        List<AiBlock> finalBlocksToSpawn = new List<AiBlock>();

        foreach (var task in sortedTasks)
        {
            List<float> sz = ParseFloatList(task.block.sizeXZH);
            List<float> cp = ParseFloatList(task.block.centerXYZ);
            if (sz == null || cp == null) continue;

            float sizeX = sz.ElementAt(0);
            float sizeZ = sz.ElementAt(1);
            float centerX = cp.ElementAt(0);
            float centerZ = cp.ElementAt(2);

            int startX = Mathf.RoundToInt((centerX - sizeX / 2f) / 3.0f);
            int endX = Mathf.RoundToInt((centerX + sizeX / 2f) / 3.0f);
            int startZ = Mathf.RoundToInt((centerZ - sizeZ / 2f) / 3.0f);
            int endZ = Mathf.RoundToInt((centerZ + sizeZ / 2f) / 3.0f);

            List<Vector2Int> footprint = new List<Vector2Int>();
            for (int x = startX; x < endX; x++)
            {
                for (int z = startZ; z < endZ; z++)
                {
                    footprint.Add(new Vector2Int(x, z));
                }
            }

            if (task.isTemp)
            {
                if (task.topY > currentSkylineY) currentSkylineY = task.topY;

                foreach (var cell in footprint)
                {
                    if (!skylineGrid.ContainsKey(cell)) skylineGrid[cell] = task.topY;
                    else skylineGrid[cell] = Mathf.Max(skylineGrid[cell], task.topY);
                }

                // ✨ [십장님 호통 반영] 원본 건물 당당하게 홀로그램 리스트에 복구!!
                finalBlocksToSpawn.Add(task.block);
            }
            else
            {
                bool hasTempUnderneath = false;
                float localSkyline = 0f;

                foreach (var cell in footprint)
                {
                    if (skylineGrid.ContainsKey(cell))
                    {
                        hasTempUnderneath = true;
                        localSkyline = Mathf.Max(localSkyline, skylineGrid[cell]);
                    }
                }

                if (!hasTempUnderneath)
                {
                    // temp가 없는 XZ 위치 → 맨땅에서 공중부양이면 스킵
                    if (task.bottomY > 1.6f)
                    {
                        Debug.Log("🛡️ [방어] 맨땅에 공중부양하는 보강벽 생성 취소!");
                        continue;
                    }

                    // 맨땅 보강재가 글로벌 스카이라인 초과 → 커팅
                    float globalSkyline = currentSkylineY > 0 ? currentSkylineY : 4.5f;
                    if (task.topY > globalSkyline + 0.1f)
                    {
                        float newH = Mathf.Round((globalSkyline - task.bottomY) / 3.0f);
                        if (newH < 1.0f) newH = 1.0f;
                        task.h = newH;
                        task.block.sizeXZH = $"[{sz.ElementAt(0)}, {sz.ElementAt(1)}, {newH}]";
                        Debug.Log($"✂️ [맨땅 커팅] H→{newH}층");
                    }
                }
                else
                {
                    // ★ 핵심 수정: 보강재가 localSkyline 안에 완전히 파묻혀 있으면 스킵
                    if (task.topY <= localSkyline + 0.1f)
                    {
                        Debug.Log($"🛡️ [방어] temp 안에 완전히 파묻힌 보강벽 스킵! (topY={task.topY:F1} <= sky={localSkyline:F1})");
                        continue;
                    }

                    // 보강재 바닥이 localSkyline 위 → 공중부양 스킵
                    if (task.bottomY >= localSkyline - 0.1f)
                    {
                        Debug.Log($"🛡️ [방어] 공중부양 보강벽 스킵! (bottomY={task.bottomY:F1} >= sky={localSkyline:F1})");
                        continue;
                    }

                    // 보강재 꼭대기가 localSkyline 초과 → 커팅
                    if (task.topY > localSkyline + 0.1f)
                    {
                        float newH = Mathf.Round((localSkyline - task.bottomY) / 3.0f);
                        if (newH < 1.0f)
                        {
                            Debug.Log("🛡️ [방어] 깎아내니 남는 게 없어 스킵!");
                            continue;
                        }
                        task.h = newH;
                        task.block.sizeXZH = $"[{sz.ElementAt(0)}, {sz.ElementAt(1)}, {newH}]";
                        Debug.Log($"✂️ [커팅] H→{newH}층 (sky={localSkyline:F1})");
                    }
                }

                finalBlocksToSpawn.Add(task.block);
            }
        }

        SpawnHologramsFromAiBlocks(finalBlocksToSpawn);
    }

    private BuildTask CreateTask(AiBlock block, bool isTemp)
    {
        string finalCenter = block.centerXYZ;
        if (string.IsNullOrEmpty(finalCenter) && !string.IsNullOrEmpty(block.pivotXYZ))
        {
            List<float> sz = ParseFloatList(block.sizeXZH);
            List<float> pv = ParseFloatList(block.pivotXYZ);
            if (sz != null && pv != null)
            {
                float cX = pv.ElementAt(0) + (sz.ElementAt(0) / 2f);
                float cY = pv.ElementAt(1);
                float cZ = pv.ElementAt(2) + (sz.ElementAt(1) / 2f);
                finalCenter = $"[{cX:F2}, {cY:F2}, {cZ:F2}]";
            }
        }

        List<float> cArr = ParseFloatList(finalCenter);
        List<float> sArr = ParseFloatList(block.sizeXZH);
        if (cArr == null || sArr == null) return null;

        float bottom = cArr.ElementAt(1) - 1.5f;
        float h = sArr.ElementAt(2);
        float top = bottom + (h * 3.0f);

        block.centerXYZ = finalCenter;
        return new BuildTask { isTemp = isTemp, bottomY = bottom, topY = top, h = h, block = block };
    }

    private void SpawnHologramsFromAiBlocks(List<AiBlock> blocks)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (AiBlock b in blocks)
        {
            List<float> szArr = ParseFloatList(b.sizeXZH);
            List<float> cpArr = ParseFloatList(b.centerXYZ);
            if (szArr == null || cpArr == null) continue;

            if (szArr.ElementAt(0) <= 3.1f && szArr.ElementAt(1) <= 3.1f) continue;

            float pX = cpArr.ElementAt(0);
            float pY = (cpArr.ElementAt(1) - 1.5f) + (szArr.ElementAt(2) * 3.0f / 2f);
            float pZ = cpArr.ElementAt(2);

            GameObject holo = Instantiate(hologramPrefab, new Vector3(pX, pY, pZ), Quaternion.identity);
            float scaleX = (float)Math.Round(szArr.ElementAt(0) / 3.0f) * 3.0f;
            float scaleZ = (float)Math.Round(szArr.ElementAt(1) / 3.0f) * 3.0f;
            float scaleY = szArr.ElementAt(2) * 3.0f;

            holo.transform.localScale = new Vector3(scaleX < 3.0f ? 3.0f : scaleX, scaleY, scaleZ < 3.0f ? 3.0f : scaleZ);
            holo.name = b.tool;
            activeHolograms.Add(holo);
            minX = Math.Min(minX, pX); maxX = Math.Max(maxX, pX);
            minZ = Math.Min(minZ, pZ); maxZ = Math.Max(maxZ, pZ);
        }
        if (activeHolograms.Count > 0)
        {
            currentBlueprintCenter = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
            isAiHologramActive = true;
        }
    }

    private void ConfirmAiBuild()
    {
        float blockSize = 3.0f;
        float shiftY = 0f;

        var sortedHolograms = activeHolograms.OrderBy(h => h.transform.position.y - (h.transform.localScale.y / 2f)).ToList();

        foreach (GameObject holo in sortedHolograms)
        {
            Vector3 size = holo.transform.localScale;
            Vector3 pos = holo.transform.position;
            Vector3 startCorner = pos - (size / 2f);

            int mode = 1;
            string toolName = holo.name;
            if (toolName.Contains("1_Solid")) mode = 1;
            else if (toolName.Contains("2_Empty")) mode = 2;
            else if (toolName.Contains("3_Circ")) mode = 3;
            else if (toolName.Contains("4_Pyra")) mode = 4;
            else if (toolName.Contains("5_Cone")) mode = 5;

            float3 sPos = new float3(Mathf.Round(startCorner.x / blockSize) * blockSize, 0, Mathf.Round(startCorner.z / blockSize) * blockSize);
            float3 ePos = new float3(sPos.x + size.x - blockSize, 0, sPos.z + size.z - blockSize);
            float h = Mathf.Round(size.y / blockSize);

            // 공중부양 강제 스냅 로직 (어긋남 완벽 방지)
            float snappedY = Mathf.Round((startCorner.y - 1.5f) / 3.0f) * 3.0f + 1.5f;

            buildQueue.Enqueue(new AiBuildCommand
            {
                mode = mode,
                startPos = sPos,
                endPos = ePos,
                height = h,
                exactY = snappedY
            });
        }
        CancelHolograms();
    }

    private void MoveHologramsTo(Vector3 targetPos)
    {
        float snapX = (float)Math.Round(targetPos.x / 3.0f) * 3.0f;
        float snapZ = (float)Math.Round(targetPos.z / 3.0f) * 3.0f;
        Vector3 newCenter = new Vector3(snapX, 0f, snapZ);
        Vector3 offset = newCenter - currentBlueprintCenter;
        foreach (GameObject holo in activeHolograms) holo.transform.position += new Vector3(offset.x, 0f, offset.z);
        currentBlueprintCenter = newCenter;
    }

    private void CancelHolograms() { foreach (var h in activeHolograms) if (h) Destroy(h); activeHolograms.Clear(); isAiHologramActive = false; isAiWorking = false; }
    private void CancelManualDrag() { var dc = FindFirstObjectByType<SimulationDragController>(); if (dc) dc.CancelDrag(); }
}
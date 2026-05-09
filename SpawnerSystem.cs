// SpawnerSystem 내부 백업 로직
if (backupIDToQuery > -1f)
{
    string path = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");
    string header = "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tool";

    Dictionary<string, string> masterData = new Dictionary<string, string>();
    if (File.Exists(path))
    {
        string[] existingLines = File.ReadAllLines(path);
        for (int i = 1; i < existingLines.Length; i++)
        {
            string[] cols = existingLines[i].Split(',');
            if (cols.Length > 0) masterData[cols[0]] = existingLines[i];
        }
    }

    bool found = false;
    foreach (var (transform, mat, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<BlockMaterial>>().WithAll<BlockTag>().WithEntityAccess())
    {
        float3 p = transform.ValueRO.Position;
        string realId = ((int)math.round(p.x * 10f)).ToString() + "_" + ((int)math.round(p.z * 10f)).ToString() + "_" + ((int)math.round(p.y * 10f)).ToString();
        string matName = mat.ValueRO.MaterialName.ToString();
        string toolType = p.y > 1.5f ? "Wall" : "Floor";

        string row = realId + "," + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2") + ",0.00,SAFE,N," + matName + "," + toolType;
        masterData[realId] = row;
        found = true;
    }

    if (found)
    {
        if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(header);
        foreach (var line in masterData.Values) sb.AppendLine(line);

        File.WriteAllText(path, sb.ToString());
        Debug.Log("한글 주석: 건축 스냅샷 백업 완료");
    }
    backupIDToQuery = -1f;
}
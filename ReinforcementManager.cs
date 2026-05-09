using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Unity.Mathematics;

public class ReinforcementManager : MonoBehaviour
{
    private string stressCsvPath;
    private string planCsvPath;

    void Start()
    {
        stressCsvPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        planCsvPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        Debug.Log("한글 주석: Y키로 도면 갱신 대기 중");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
        {
            CreatePlanExcel();
        }
    }

    public void CreatePlanExcel()
    {
        if (!File.Exists(stressCsvPath)) return;

        string header = "BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tool";
        string[] lines = File.ReadAllLines(stressCsvPath);

        Dictionary<string, string> planData = new Dictionary<string, string>();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length >= 9)
            {
                planData[cols[0]] = lines[i];
            }
        }

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length < 7 || cols[6] != "Y") continue;

            string id = cols[0];
            string[] parts = id.Split('_');
            if (parts.Length != 3) continue;

            float cleanX = float.Parse(cols[1]);
            float cleanZ = float.Parse(cols[3]);
            float currentY = float.Parse(parts[2]);

            while (currentY >= 45f)
            {
                currentY -= 30f;

                float ix = (float)math.round(cleanX * 10f);
                float iz = (float)math.round(cleanZ * 10f);

                string strX = (ix < 0f ? "-" : "0") + math.abs(ix).ToString("000");
                string strZ = (iz < 0f ? "-" : "0") + math.abs(iz).ToString("000");
                string strY = (currentY < 0f ? "-" : "0") + math.abs(currentY).ToString("000");
                string targetId = strX + "_" + strZ + "_" + strY;

                if (!planData.ContainsKey(targetId))
                {
                    float exactY = currentY / 10f;
                    planData[targetId] = targetId + "," + cleanX.ToString("F2") + "," + exactY.ToString("F2") + "," + cleanZ.ToString("F2") + ",0.00,SAFE,N,Steel,Reinforcement";
                }
            }
        }

        List<string> output = new List<string>();
        output.Add(header);
        foreach (var val in planData.Values) output.Add(val);

        string dir = Path.GetDirectoryName(planCsvPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        File.WriteAllLines(planCsvPath, output);
        Debug.Log("한글 주석: 보강 계획서 업데이트 완료");
    }
}
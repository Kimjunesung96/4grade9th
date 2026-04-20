using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class ReinforcementManager : MonoBehaviour
{
    private string stressCsvPath;
    private string planCsvPath;

    void Start()
    {
        stressCsvPath = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        planCsvPath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        Debug.Log("👷‍♂️ [현장 반장] Y(도면 갱신) 대기 중! 실제 타설은 Spawner가 전담합니다!");
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
        // 1. 스트레스 파일 자체가 물리적으로 없는 경우
        if (!File.Exists(stressCsvPath))
        {
            if (File.Exists(planCsvPath)) Debug.Log("🏗️ [현장 반장] 스트레스 검사 파일이 없습니다. 기존 도면을 유지합니다.");
            return;
        }

        // 일단 파일을 읽어옵니다.
        string[] lines = File.ReadAllLines(stressCsvPath);

        // ⭐ 2. [완벽한 방어 로직] 스트레스 파일은 있지만 데이터가 텅 빈 경우
        if (lines.Length <= 1)
        {
            if (File.Exists(planCsvPath))
            {
                Debug.Log("🏗️ [현장 반장] CurrentStress에 새로운 부하 데이터(블록)가 없습니다! 기존 Reinforcement 도면을 덮어쓰지 않고 재사용합니다.");
            }
            else
            {
                Debug.LogWarning("🚨 [현장 반장] 부하 데이터도 없고 기존 도면도 없습니다!");
            }
            return;
        }

        // ---------------------------------------------------------
        // ⭐ 3. 블록 데이터가 정상적으로 꽉꽉 들어있을 경우!
        // ---------------------------------------------------------
        HashSet<string> existingBlocks = new HashSet<string>();
        List<string> planLines = new List<string> { "BlockID,PosX,PosY,PosZ,Tool" };

        // 🛠️ 1패스: 기존에 있던 블록들을 엑셀에 'Existing'으로 먼저 쫙 적어줍니다!
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;

            // 한글 봉인 완료!
            string id = cols[0];
            existingBlocks.Add(id);
            planLines.Add($"{id},{cols[1]},{cols[2]},{cols[3]},Existing");
        }

        // 🛠️ 2패스: 'Y' 판정받은 위험 블록 밑으로 파고들어가며 보강 철근을 세웁니다!
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 7) continue;

            // 한글 봉인 완료!
            if (cols[6] != "Y") continue;

            // 한글 봉인 완료!
            string id = cols[0];
            string[] parts = id.Split('_');
            if (parts.Length != 3) continue;

            // 한글 봉인 완료!
            string colX = parts[0];
            string colZ = parts[1];
            int currentY = int.Parse(parts[2]);

            // 한글 봉인 완료!
            float cleanX = float.Parse(cols[1]);
            float cleanZ = float.Parse(cols[3]);

            // 밑으로 30씩 내려가면서 철근 박기
            while (currentY >= 45)
            {
                currentY -= 30;
                string targetId = $"{colX}_{colZ}_{currentY:0000}";

                // 이미 거기 블록이 있다면 멈춤 (바닥이나 다른 구조물에 닿음)
                if (existingBlocks.Contains(targetId)) break;

                existingBlocks.Add(targetId);

                // 보강 블록의 Y좌표: currentY / 10f (BlockID Y값 → Unity 좌표)
                float targetY = currentY / 10f;
                planLines.Add($"{targetId},{cleanX:F2},{targetY:F2},{cleanZ:F2},Reinforcement");
            }
        }

        File.WriteAllLines(planCsvPath, planLines);
        Debug.Log("📄 [Y키 작동] 마스터 엑셀 도면 생성 완료! Spawner가 도면을 로드했습니다!");
    }
}
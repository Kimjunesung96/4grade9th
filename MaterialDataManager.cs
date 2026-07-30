using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class MaterialDataManager : MonoBehaviour
{
    public static MaterialDataManager Instance;

    public struct MaterialSpec
    {
        public string Name;
        public float Density;
        public float BaseHP;
        public float PricePerKg; // ⭐ 누락되었던 단가 변수 선언 추가!
        public bool IsBrittle; // TRUE면 콘크리트(찢어짐 약함), FALSE면 철골 // TRUE면 콘크리트(찢어짐 약함), FALSE면 철골
        public float Tensile; // 인장강도
        public float Compressive; // 압축강도
        public float Shear; // 전단강도
        public float Bending; // 휨강도
        public float Torsion; // 비틀림강도
        public Color Color;
    }

    public Dictionary<string, MaterialSpec> MaterialDict = new Dictionary<string, MaterialSpec>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        LoadMaterialSpecs();
    }

    public void LoadMaterialSpecs()
    {
        string path = Path.Combine(Application.dataPath, "StressBlock", "Material_Specs.csv");
        string header = "MaterialName,Density,BaseHP,IsBrittle,Tensile,Compressive,Shear,Bending,Torsion,R,G,B,PricePerKg";

        // ⭐ [코드 기준 최신 스펙] 이 목록에 있는 재질명은 파일에 있든 없든 항상 이 값으로 갱신됨
        string[] defaultSpecs = new string[]
        {
            "Default,0.37,999999,FALSE,400,400,300,300,300,1.0,1.0,1.0,100",
            "Concrete,2.4,1000,TRUE,50,400,150,200,100,0.7,0.7,0.7,120", // ⭐ 레미콘 kg당 시세 반영 (콘크리트류 중 가장 저렴)
            "Reinforced_Concrete,2.5,1800,FALSE,350,550,250,480,220,0.5,0.5,0.5,200", // ⭐ 콘크리트+철근 배합이라 순수 콘크리트보다 비쌈, 휨강도 대폭 상향(RC 본연의 장점 반영)
            "Steel,7.8,2000,FALSE,600,600,300,400,300,0.3,0.3,0.4,1300", // ⭐ 구조용 철강 kg당 시세 반영
            "Wood_Default,0.55,320,TRUE,180,120,90,150,60,0.6,0.4,0.2,900", // ⭐ [수정] 일반 목재(SPF 각재급) 시세로 낮춤. 인장/압축 관계는 현실에 맞게 뒤집힌 채 유지(결방향 나무는 인장이 압축보다 강함)
            "Wood_Oak,0.75,450,TRUE,300,220,140,250,100,0.45,0.28,0.15,3500", // ⭐ [신규] 오크: 활엽수 원목이라 가공/수급 단가 높음, 밀도/강도 전반적으로 높음
            "Wood_Rubber,0.62,350,TRUE,220,150,110,200,80,0.5,0.32,0.18,2500", // ⭐ [신규] 고무나무 집성목: 일반 목재보다 비싸지만 오크보다 저렴, 휨(탄성) 특성 우수

            // ⭐ [추가] 유명 목재 4종 (실제 상대적 강도/가격 감안)
            "Wood_Pine,0.5,280,TRUE,150,110,70,130,55,0.85,0.7,0.45,600", // 소나무: 흔한 구조용 연목, 가장 저렴한 목재군
            "Wood_Cedar,0.42,300,TRUE,170,115,75,140,60,0.9,0.75,0.55,2800", // 편백나무: 국내서 인테리어/사우나용으로 인기 많아 연목 치고 단가 높음
            "Wood_Walnut,0.62,470,TRUE,330,240,150,260,110,0.35,0.22,0.12,6000", // 월넛: 가구재로 유명한 고급 활엽수, 오크보다도 비쌈
            "Wood_Teak,0.65,520,FALSE,350,260,160,280,120,0.55,0.35,0.15,8000", // 티크: 방수/내구성 최고급 원목, 목재 중 최고가. 다른 원목과 달리 질김(취성 아님)

            // ⭐ [추가] 합성목/공학목재 4종 (가공목이라 원목보다 취성이 낮고, 가격은 가공 방식에 따라 천차만별)
            "Wood_CLT,0.48,500,FALSE,260,200,160,300,140,0.8,0.65,0.4,2200", // CLT(직교집성판): 층을 교차로 붙인 구조용 목재. 실제로 목조 고층빌딩에 쓰이는 재료. 원목보다 강하고 덜 부서짐
            "Wood_Bamboo,0.7,380,TRUE,280,180,130,220,90,0.75,0.7,0.4,1800", // 대나무 집성재: 무게 대비 인장강도가 특히 좋음(친환경 소재로 각광)
            "Wood_Plywood,0.55,250,FALSE,140,100,90,160,70,0.8,0.68,0.45,500", // 합판: 얇은 판을 겹쳐 붙여 원목보다 안 부서짐(연성↑), 대신 강도는 낮음. 가장 저렴한 합성목
            "Wood_MDF,0.75,150,TRUE,60,50,40,70,30,0.75,0.6,0.4,350", // MDF: 밀도는 높지만 섬유판이라 강도는 목재 중 최약체. 인테리어용, 구조재로는 부적합
            "Glass,2.5,50,TRUE,10,50,50,50,50,0.8,0.9,1.0,2000",  // ⭐ 판유리 kg당 시세로 상향 (가공 유리류 중 고가)
            "H_Beam,4.5,2500,FALSE,900,700,400,600,400,0.55,0.55,0.6,1600" // ⭐ H형강: 압연 가공이 더 들어가 Steel(1300)보다 kg당 단가 높음. Steel보다 가볍고(밀도↓) 인장/휨강도 크게 높임(단면 형상 이점 반영)
        };

        // ⭐ [업데이트 방식] 기존 파일이 있으면 읽어와서 재질명 기준으로 담아두고,
        // 없으면 빈 상태에서 시작 → 아래에서 코드 기준 defaultSpecs로 있으면 갱신, 없으면 추가
        Dictionary<string, string> mergedRows = new Dictionary<string, string>();

        if (File.Exists(path))
        {
            string[] existingLines = File.ReadAllLines(path);
            for (int i = 1; i < existingLines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(existingLines[i])) continue;
                string name = existingLines[i].Split(',')[0];
                mergedRows[name] = existingLines[i]; // 기존 사용자 커스텀 재질도 일단 보존
            }
        }

        foreach (string specLine in defaultSpecs)
        {
            string name = specLine.Split(',')[0];
            mergedRows[name] = specLine; // 있으면 최신값으로 덮어쓰기(업데이트), 없으면 신규 추가
        }

        List<string> finalLines = new List<string> { header };
        finalLines.AddRange(mergedRows.Values);

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllLines(path, finalLines);
        Debug.Log($"📄 [자재 관리소] 단가표 갱신 완료! (코드 기준 스펙 {defaultSpecs.Length}종 반영, 총 {finalLines.Count - 1}종 보유)");

        MaterialDict.Clear();
        string[] lines = File.ReadAllLines(path);

       for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            // ⭐ 13에서 12로 롤백! 예전 12칸짜리 파일도 일단 읽을 수 있게 허용합니다.
            if (cols.Length >= 12) 
            {
                MaterialSpec spec = new MaterialSpec();
                spec.Name = cols[0];
                spec.Density = float.Parse(cols[1]);
                spec.BaseHP = float.Parse(cols[2]);
                spec.IsBrittle = cols[3].Trim().ToUpper() == "TRUE";
                spec.Tensile = float.Parse(cols[4]);
                spec.Compressive = float.Parse(cols[5]);
                spec.Shear = float.Parse(cols[6]);
                spec.Bending = float.Parse(cols[7]);
                spec.Torsion = float.Parse(cols[8]);
                spec.Color = new Color(float.Parse(cols[9]), float.Parse(cols[10]), float.Parse(cols[11]));

                // ⭐ 추가: 13번째 칸(단가)이 파일에 있으면 그걸 읽고, 없으면 기본 단가(100)를 적용!
                if (cols.Length >= 13) 
                {
                    spec.PricePerKg = float.Parse(cols[12]);
                }
                else 
                {
                    spec.PricePerKg = 100f; 
                }

                MaterialDict[spec.Name] = spec;
            }
        }
        Debug.Log($"📊 [자재 관리소] 총 {MaterialDict.Count}개의 단가표 로드 완료!");
    }
}
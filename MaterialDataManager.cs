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

        if (!File.Exists(path))
        {
            // ⭐ 대괄호 에러 원천 차단: new string[] 과 중괄호 { } 로 확실히 묶습니다.
            string[] defaultSpecs = new string[]
            {
                "MaterialName,Density,BaseHP,IsBrittle,Tensile,Compressive,Shear,Bending,Torsion,R,G,B,PricePerKg",
                "Default,0.37,999999,FALSE,400,400,300,300,300,1.0,1.0,1.0,100",
                "Concrete,2.4,1000,TRUE,50,400,150,200,100,0.7,0.7,0.7,50",
                "Reinforced_Concrete,2.5,1500,FALSE,350,500,200,250,150,0.5,0.5,0.5,120",
                "Steel,7.8,2000,FALSE,600,600,300,400,300,0.3,0.3,0.4,800",
                "Wood,0.6,300,TRUE,50,200,100,150,50,0.6,0.4,0.2,800", // ⭐ 목재 가격 800원으로 현실화
                "Glass,2.5,50,TRUE,10,50,50,50,50,0.8,0.9,1.0,1500"   // ⭐ 유리 가격 1500원으로 현실화
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, defaultSpecs);
            Debug.Log("📄 [자재 관리소] 15cm 축척 스펙이 적용된 신규 단가표가 생성되었습니다.");
        }

        MaterialDict.Clear();
        string[] lines = File.ReadAllLines(path);
        // [이상한태그]spec.Density = float.Parse(cols[1]);
        
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
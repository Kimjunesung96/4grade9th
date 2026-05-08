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
        public bool IsBrittle; // ⭐ TRUE면 콘크리트(찢어짐 약함, 랭킨), FALSE면 철골(폰미제스)
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
            // ⭐ 십장님 지시: 무게, 체력, 인장/압축/전단 강도를 엑셀에서 완벽 통제!
            string[] defaultSpecs = {
                "MaterialName,Density,BaseHP,IsBrittle,Tensile,Compressive,Shear,Bending,Torsion,R,G,B",
                "Default,0.37,999999,FALSE,10,10,10,10,10,1.0,1.0,1.0",
                "Concrete,2.4,1000,TRUE,50,10,15,20,10,0.7,0.7,0.7",
                "Reinforced_Concrete,2.5,1500,FALSE,30,15,20,25,15,0.5,0.5,0.5", // 철근이 들어가서 안 끊어짐! (FALSE)
                "Steel,7.8,2000,FALSE,15,15,30,40,30,0.3,0.3,0.4",
                "Wood,0.6,300,TRUE,5,20,10,15,5,0.6,0.4,0.2",
                "Glass,2.5,50,TRUE,5,5,5,5,5,0.8,0.9,1.0"
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, defaultSpecs);
            Debug.Log("📄 [자재 관리소] 물리 스펙(투트랙)이 적용된 신규 단가표가 생성되었습니다.");
        }

        MaterialDict.Clear();
        string[] lines = File.ReadAllLines(path);

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
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

                MaterialDict[spec.Name] = spec;
            }
        }
        Debug.Log($"📊 [자재 관리소] 총 {MaterialDict.Count}개의 단가표 로드 완료!");
    }
}
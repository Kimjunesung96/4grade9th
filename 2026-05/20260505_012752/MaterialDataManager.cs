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
        public float Defense;
        public float BaseHP;
        public Color Color;
    }

    // 엑셀에서 읽어온 단가표를 기억하는 딕셔너리
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

        // 엑셀 단가표가 없으면 현장 디폴트 값으로 하나 찍어냅니다.
        if (!File.Exists(path))
        {
            string[] defaultSpecs = {
                "MaterialName,Density,Defense,BaseHP,R,G,B",
                "Default,2.4,300,1000,1.0,1.0,1.0",
                "Concrete,2.4,400,1000,0.7,0.7,0.7",
                "Steel,7.8,600,2000,0.3,0.3,0.4",
                "Wood,0.6,80,300,0.6,0.4,0.2",
                "Glass,2.5,200,50,0.8,0.9,1.0"
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, defaultSpecs);
            Debug.Log("📄 [자재 관리소] Material_Specs.csv 파일이 없어서 기본 단가표를 생성했습니다.");
        }

        MaterialDict.Clear();
        string[] lines = File.ReadAllLines(path);

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length >= 7)
            {
                MaterialSpec spec = new MaterialSpec();
                spec.Name = cols[0];
                spec.Density = float.Parse(cols[1]);
                spec.Defense = float.Parse(cols[2]);
                spec.BaseHP = float.Parse(cols[3]);
                spec.Color = new Color(float.Parse(cols[4]), float.Parse(cols[5]), float.Parse(cols[6]));

                MaterialDict[spec.Name] = spec;
            }
        }
        Debug.Log($"📊 [자재 관리소] 총 {MaterialDict.Count}개의 단가표 로드 완료!");
    }
}
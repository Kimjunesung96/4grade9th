using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using UnityEngine.SceneManagement;

public class HierarchyExporter : EditorWindow
{
    // 유니티 상단 메뉴에 버튼을 생성합니다.
    [MenuItem("Window/📝 하이어라키 TXT 추출기")]
    public static void ExportHierarchyToText()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== 유니티 씬 하이어라키 및 인스펙터 추출 데이터 ===");
        sb.AppendLine($"추출 시간: {System.DateTime.Now}");
        sb.AppendLine("================================================\n");

        // 현재 활성화된 씬의 가장 최상단 오브젝트들을 모두 가져옵니다.
        GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject go in rootObjects)
        {
            TraverseHierarchy(go, sb, 0);
        }

        // 파일 저장 창을 띄웁니다.
        string path = EditorUtility.SaveFilePanel("텍스트로 저장", "", "HierarchyData.txt", "txt");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"💾 추출 완료! 파일 위치: {path}");
        }
    }

    // 자식 오브젝트까지 파고들며 데이터를 수집하는 재귀 함수입니다.
    static void TraverseHierarchy(GameObject go, StringBuilder sb, int depth)
    {
        string indent = new string(' ', depth * 4);
        sb.AppendLine($"{indent}■ [{go.name}] (활성화: {go.activeSelf})");

        // 오브젝트에 붙어있는 모든 컴포넌트를 가져옵니다.
        Component[] components = go.GetComponents<Component>();
        foreach (Component comp in components)
        {
            if (comp == null) continue;
            sb.AppendLine($"{indent}    └ 컴포넌트: {comp.GetType().Name}");

            // 인스펙터에 보이는 변수(Property)들을 추적합니다.
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty sp = so.GetIterator();
            
            bool enterChildren = true;
            while (sp.NextVisible(enterChildren))
            {
                enterChildren = false; 
                if (sp.name == "m_Script") continue; // 기본 스크립트 참조 필드는 제외

                sb.AppendLine($"{indent}        - {sp.displayName}: {GetPropertyValue(sp)}");
            }
        }
        sb.AppendLine();

        // 자식 오브젝트가 있다면 동일하게 반복합니다.
        foreach (Transform child in go.transform)
        {
            TraverseHierarchy(child.gameObject, sb, depth + 1);
        }
    }

    // 인스펙터 변수의 타입에 맞춰 값을 문자열로 변환합니다.
    static string GetPropertyValue(SerializedProperty sp)
    {
        switch (sp.propertyType)
        {
            case SerializedPropertyType.Integer: return sp.intValue.ToString();
            case SerializedPropertyType.Boolean: return sp.boolValue.ToString();
            case SerializedPropertyType.Float: return sp.floatValue.ToString("F3");
            case SerializedPropertyType.String: return sp.stringValue;
            case SerializedPropertyType.Vector3: return sp.vector3Value.ToString();
            case SerializedPropertyType.Color: return sp.colorValue.ToString();
            case SerializedPropertyType.ObjectReference: return sp.objectReferenceValue != null ? sp.objectReferenceValue.name : "Null";
            case SerializedPropertyType.Enum: return sp.enumDisplayNames.Length > 0 && sp.enumValueIndex >= 0 && sp.enumValueIndex < sp.enumDisplayNames.Length ? sp.enumDisplayNames[sp.enumValueIndex] : sp.enumValueIndex.ToString();
            default: return "[복합 데이터 타입]";
        }
    }
}
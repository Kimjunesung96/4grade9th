using UnityEngine;

// ⭐ [브릿지] SimulationSettings는 ScriptableObject라서 DOTS ISystem(구조체)들이
//    직접 들고 있을 수 없음. MaterialDataManager.Instance / BudgetUIManager.Instance와
//    동일한 패턴으로 static Instance를 제공해서, 기존 시스템들이 바로 참조할 수 있게 함.
//
//    씬에 빈 GameObject 하나 만들고 이 컴포넌트 붙인 뒤, settingsAsset 슬롯에
//    SimulationSettings 에셋을 드래그해서 연결하면 됨.
public class SimulationSettingsProvider : MonoBehaviour
{
    public static SimulationSettings Instance;

    [SerializeField] private SimulationSettings settingsAsset;

    void Awake()
    {
        if (settingsAsset == null)
        {
            Debug.LogWarning("[SimulationSettingsProvider] settingsAsset이 연결되지 않았습니다! 원본 하드코딩값(fallback)으로 동작합니다.");
            return;
        }
        Instance = settingsAsset;
    }
}
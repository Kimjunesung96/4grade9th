using UnityEngine;
using UnityEngine.EventSystems;

public class ClickDebugger : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            var go = EventSystem.current.currentSelectedGameObject;
            Debug.LogError($"🖱️ 클릭! EventSystem.current={EventSystem.current}, currentSelected={(go != null ? go.name : "NULL")}");

            var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            Debug.LogError($"🎯 RaycastAll 결과 {results.Count}개:");
            foreach (var r in results)
                Debug.LogError($"   - {r.gameObject.name} (module: {r.module})");
        }
    }
}
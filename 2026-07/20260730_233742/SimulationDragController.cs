using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;

public class SimulationDragController : MonoBehaviour
{
    public float blockSize = 3.0f;
    public LayerMask groundLayer = ~0;
    public Material coreMaterial;
    public Material previewMaterial;

    private Transform simulationPivot;
    private bool isSimulationActive = false;
    private float3 definedStart;
    private float3 definedEnd;

    void Start()
    {
        simulationPivot = new GameObject("SimulationPivot").transform;
        simulationPivot.gameObject.SetActive(false);
        // ... ( Start 내부 GameObject 생성 로직 기존과 동일하게 유지 ) ...
    }

    void Update()
    {
        // ===== [실험용 디버그 로그 START] =====
        if (Camera.main == null)
        {
            Debug.Log("[DragDebug] Camera.main이 NULL입니다! MainCamera 태그 확인 필요.");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer);

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0))
        {
            Debug.Log($"[DragDebug] 레이캐스트 결과: {hitSomething} | groundLayer mask: {groundLayer.value} | hit.collider: {(hitSomething ? hit.collider.name : "없음")}");
        }
        // ===== [실험용 디버그 로그 END] =====

        if (!hitSomething) return;
        float3 hitPoint = hit.point;

        if (Input.GetMouseButtonDown(0)) { definedStart = SnapToGrid(hitPoint); definedEnd = definedStart; isSimulationActive = true; simulationPivot.gameObject.SetActive(true); SyncWithDOTS(); }
        if (Input.GetMouseButton(0) && isSimulationActive) { definedEnd = SnapToGrid(hitPoint); SyncWithDOTS(); }
        if (Input.GetMouseButtonUp(0) && math.distance(definedStart, definedEnd) < 0.1f) { CancelDrag(); }
    }

    float3 SnapToGrid(float3 pos) { return math.floor(pos / blockSize) * blockSize; }

    void SyncWithDOTS()
    {
        // 🚀 [추가된 코드] DOTS 월드가 아직 안 만들어졌으면(Null) 동기화를 건너뜁니다!
        if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null)
        {
            Debug.Log("[DragDebug] DefaultGameObjectInjectionWorld가 NULL입니다!");
            return;
        }

        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(BuilderStateData));
        if (query.HasSingleton<BuilderStateData>())
        {
            var data = query.GetSingleton<BuilderStateData>();
            data.GuideStartPos = definedStart; data.GuideEndPos = definedEnd;
            query.SetSingleton(data);
        }
        else
        {
            Debug.Log("[DragDebug] BuilderStateData 싱글턴을 찾을 수 없습니다!");
        }
    }

    // 📡 [최종 보강] R, L 키 눌렀을 때 초록색 선까지 싹 지워버리는 유배 무전기
    public void CancelDrag()
    {
        isSimulationActive = false;
        if (simulationPivot) simulationPivot.gameObject.SetActive(false);

        // 좌표를 -9999로 멀리 보내서 초록색 가이드를 화면에서 치워버립니다.
        definedStart = new float3(-9999f, -9999f, -9999f);
        definedEnd = new float3(-9999f, -9999f, -9999f);
        SyncWithDOTS();
    }
}
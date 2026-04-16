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
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer)) return;
        float3 hitPoint = hit.point;

        if (Input.GetMouseButtonDown(0)) { definedStart = SnapToGrid(hitPoint); definedEnd = definedStart; isSimulationActive = true; simulationPivot.gameObject.SetActive(true); SyncWithDOTS(); }
        if (Input.GetMouseButton(0) && isSimulationActive) { definedEnd = SnapToGrid(hitPoint); SyncWithDOTS(); }
        if (Input.GetMouseButtonUp(0) && math.distance(definedStart, definedEnd) < 0.1f) { CancelDrag(); }
    }

    float3 SnapToGrid(float3 pos) { return math.floor(pos / blockSize) * blockSize; }

    void SyncWithDOTS()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(BuilderStateData));
        if (query.HasSingleton<BuilderStateData>())
        {
            var data = query.GetSingleton<BuilderStateData>();
            data.GuideStartPos = definedStart; data.GuideEndPos = definedEnd;
            query.SetSingleton(data);
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
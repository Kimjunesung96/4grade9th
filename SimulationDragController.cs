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
        if (Camera.main == null) return;
        
        // ⭐ 1. DOTS 월드가 생성되지 않았다면 무시 (에러 방지)
        if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null) return;

        var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var physicsQuery = em.CreateEntityQuery(typeof(Unity.Physics.PhysicsWorldSingleton));
        if (!physicsQuery.HasSingleton<Unity.Physics.PhysicsWorldSingleton>()) return;
        
        // ⭐ 2. 유니티 기본 물리가 아닌 DOTS 물리 월드 가져오기
        var physicsWorld = physicsQuery.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;

        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Unity.Physics.RaycastInput rayInput = new Unity.Physics.RaycastInput 
        { 
            Start = ray.origin, 
            End = ray.origin + ray.direction * 1000f, 
            Filter = Unity.Physics.CollisionFilter.Default 
        };

        // ⭐ 3. DOTS 전용 광선 쏘기! 이제 빌드에서도 바닥을 완벽히 인식합니다.
        bool hitSomething = physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit);

        if (!hitSomething) return;
        
        // hit.point 대신 hit.Position을 사용합니다.
        float3 hitPoint = hit.Position; 

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
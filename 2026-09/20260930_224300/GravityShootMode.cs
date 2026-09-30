using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

// ⭐ [신규 파일] M모드(시간제한 없는 중력 모드) 전용 발사 기능.
//    - M모드 on/off 자체는 StressVisualizationSystem.cs에 이미 구현됨 (isGravityMode / IsGravityModeActive)
//    - M모드에서 마우스 입력 전부가 발사로 동작함:
//        좌클릭 홀드 = 연사(fireRate), 우클릭 = 점사, 휠클릭 = 점사, 휠 굴리기 = 연사(fireRate)
//    - 매번 카메라 → 마우스 위치로 레이캐스트 쏘고, 그 방향으로 표준 3x3x3 블록 프리팹을 "발사체"로 인스턴스화함.
//    - 발사체는 BlockTag를 안 붙여서 건물 구조(응력/조인트) 시스템에 편입되지 않고,
//      순수 DOTS 물리 다이나믹 바디로만 날아가서 부딪힘.
//
//    ⚠️ 주의: SimulationDragController.cs도 좌클릭으로 "테스트 영역 지정 드래그"를 시작함.
//    두 스크립트가 같은 씬에서 동시에 활성 상태면 M모드 좌클릭 시 발사와 드래그 영역 지정이
//    같이 발생할 수 있음. 실제로 충돌 나면 알려주면 좌클릭은 발사에서 빼줄게.
public class GravityShootMode : MonoBehaviour
{
    [Header("발사 설정")]
    public float launchForce = 25f;      // 발사 속도
    public float playerRadius = 1.0f;    // ⭐ 플레이어 캡슐 반지름 (발사체가 이 밖에서 생성됨. 그래도 맞으면 키워줘)
    public float muzzleHeight = 1.0f;    // ⭐ 플레이어 위치에서 위로 얼마 띄운 지점에서 발사할지
    public float spawnGap = 0.3f;        // ⭐ 캡슐/발사체 사이 여유 간격
    public float blockMargin = 0.05f;   // 기존 블록과 동일한 여백 (한 변 = 크기 - margin)
    public float fireRate = 8f;          // 연사(좌클릭 홀드, 휠 굴리기) 시 초당 발사 수

    public static int SizeLevel = 1;    // 휠로 조절: 1~9 → 한 변 3~11 (기본 3). 휠 올리면 +1, 내리면 -1. x/y/z 똑같이 커짐. HUD에서도 읽음
    public static int SpeedTier = 1; /* HUD에서 읽을 수 있게 public static */ // 숫자키 1~9로 조절되는 속도 배율 (1누르면 1배, 2누르면 2배... 9누르면 9배)
    private float _nextFireTime;

    private EntityQuery _spawnerQuery, _physicsQuery, _jointQuery, _playerQuery;
    private bool _queriesReady;

    void EnsureQueries()
    {
        if (_queriesReady) return;
        if (World.DefaultGameObjectInjectionWorld == null) return;
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        _spawnerQuery = em.CreateEntityQuery(typeof(SpawnerData));
        _physicsQuery = em.CreateEntityQuery(typeof(Unity.Physics.PhysicsWorldSingleton));
        _playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerData>(), ComponentType.ReadOnly<LocalTransform>());
        _jointQuery = em.CreateEntityQuery(ComponentType.ReadOnly<JointTag>(), ComponentType.ReadOnly<Unity.Physics.PhysicsConstrainedBodyPair>());
        _queriesReady = true;
    }

    void Update()
    {
        // M모드가 아니면 아무것도 안 함
        if (!StressVisualizationSystem.IsGravityModeActive) return;
        if (Camera.main == null) return;
        if (World.DefaultGameObjectInjectionWorld == null) return;

        // 숫자키 1~9로 속도 배율 조절 (1=1배 ... 9=9배)
        for (int k = 1; k <= 9; k++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + k) || Input.GetKeyDown(KeyCode.Keypad0 + k))
            {
                SpeedTier = k;
                Debug.Log($"[GravityShootMode] 발사 속도 {SpeedTier}배로 설정");
            }
        }

        // 휠로 발사체 크기 조절 (휠 한 칸 = 한 변 1씩, 1~9단계 = 3~11)
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f)
        {
            SizeLevel = Mathf.Clamp(SizeLevel + (wheel > 0f ? 1 : -1), 1, 9);
            Debug.Log($"[GravityShootMode] 발사체 크기 {SizeLevel + 2}로 설정 (단계 {SizeLevel}/9)");
        }

        EnsureQueries();
        if (!_queriesReady) return;
        if (_spawnerQuery.IsEmpty) return; // 씬에 SpawnerAuthoring이 없으면 쏠 프리팹이 없음
        if (!_physicsQuery.HasSingleton<Unity.Physics.PhysicsWorldSingleton>()) return;
        if (_playerQuery.IsEmpty) return; // ⭐ 발사 원점이 될 플레이어가 없으면 발사 안 함

        // 마우스 입력을 발사로: 좌클릭(연사)/우클릭(점사)/휠클릭(점사). 휠 굴리기는 이제 크기 조절이라 발사 안 함.
        bool singleShot = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool autoFireHeld = Input.GetMouseButton(0);

        bool wantsFire = singleShot || autoFireHeld;
        if (!wantsFire) return;

        // 연사(좌클릭 홀드)는 fireRate로 속도 제한, 점사(우/휠클릭)는 누른 순간 즉시 발사
        if (!singleShot && Time.time < _nextFireTime) return;
        _nextFireTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var spawnerData = _spawnerQuery.GetSingleton<SpawnerData>();
        if (spawnerData.Prefab == Entity.Null) return;

        var physicsWorld = _physicsQuery.GetSingleton<Unity.Physics.PhysicsWorldSingleton>().PhysicsWorld;

        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        var rayInput = new Unity.Physics.RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * 1000f,
            Filter = Unity.Physics.CollisionFilter.Default
        };

        // ⭐ 카메라 레이는 "조준점" 찾는 용도로만 사용. 플레이어 자신에 맞은 건 무시.
        bool hitSomething = physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit);
        if (hitSomething && em.HasComponent<PlayerData>(hit.Entity))
        {
            hitSomething = false;
        }

        float3 aimPoint = hitSomething ? hit.Position : (float3)ray.origin + (float3)ray.direction * 200f;

        // ⭐ 발사 원점 = 카메라가 아니라 플레이어 몸 쪽. 조준점 방향으로 캡슐 밖에서 생성돼서 자기 몸을 관통/충돌하지 않음.
        float3 playerPos = _playerQuery.GetSingleton<LocalTransform>().Position + new float3(0f, muzzleHeight, 0f);
        float3 toAim = aimPoint - playerPos;
        float3 direction = math.lengthsq(toAim) > 0.0001f ? math.normalize(toAim) : (float3)ray.direction;

        float blockSize = (SizeLevel + 2) - blockMargin;          // 3~11 에서 여백 뺀 실제 스케일
        float3 spawnPos = playerPos + direction * (playerRadius + blockSize * 0.5f + spawnGap);

        var instance = em.Instantiate(spawnerData.Prefab);
        em.AddComponent<ShotProjectileTag>(instance); // M모드 끝날 때 이거 붙은 것만 정리

        // 위치/크기 세팅 (기존 블록과 동일한 스케일)
        em.SetComponentData(instance, LocalTransform.FromPositionRotationScale(spawnPos, quaternion.identity, blockSize));

        // 건물 구조 시스템(BlockTag/BlockStress/조인트)에는 절대 편입 안 시킴 — 순수 발사체로만 취급
        // 프리팹에 이미 PhysicsCollider/PhysicsMass/PhysicsVelocity/PhysicsGravityFactor가 있다고 가정
        // (기존 SpawnerSystem들이 같은 프리팹으로 블록을 만들 때도 그렇게 씀)
        if (em.HasComponent<Unity.Physics.PhysicsGravityFactor>(instance))
        {
            em.SetComponentData(instance, new Unity.Physics.PhysicsGravityFactor { Value = 1.0f });
        }

        if (em.HasComponent<Unity.Physics.PhysicsMass>(instance))
        {
            var mass = em.GetComponentData<Unity.Physics.PhysicsMass>(instance);
            // 혹시 프리팹이 기본적으로 Kinematic(InverseMass=0)이면 발사체는 무조건 다이나믹으로 풀어줌
            if (mass.InverseMass <= 0f)
            {
                mass.InverseMass = 1.0f;
                mass.InverseInertia = new float3(1f, 1f, 1f);
            }

            // ⭐ 크기가 커지면 질량도 커짐: 질량은 부피(한 변^3)에 비례, 관성은 질량 x 한 변^2 = 한 변^5에 비례.
            //    기준(3x3x3)의 배율로 계산. 발사 속도는 그대로라 큰 블록일수록 운동량이 커서 더 세게 부딪힘.
            float sizeRatio = (SizeLevel + 2) / 3f;
            float massScale = sizeRatio * sizeRatio * sizeRatio;
            float inertiaScale = massScale * sizeRatio * sizeRatio;
            mass.InverseMass /= massScale;
            mass.InverseInertia /= inertiaScale;
            em.SetComponentData(instance, mass);
        }

        if (em.HasComponent<Unity.Physics.PhysicsVelocity>(instance))
        {
            em.SetComponentData(instance, new Unity.Physics.PhysicsVelocity
            {
                Linear = direction * launchForce * SpeedTier,
                Angular = float3.zero
            });
        }

        // ⭐ 맞은 블록이 건물의 일부(BlockTag)라면 "일시파괴": 조인트만 끊고 물리를 풀어줌.
        //    영구 삭제 아님 — M을 다시 눌러 StopPhysics()가 실행되면 원위치+조인트 복구됨.
        if (hitSomething && em.HasComponent<BlockTag>(hit.Entity))
        {
            BreakBlockLoose(em, hit.Entity);
        }
    }

    /// <summary>
    /// 맞은 블록에 연결된 조인트를 끊고, 그 블록(과 같이 끊긴 이웃)의 물리를 다이나믹으로 풀어줌.
    /// StressVisualizationSystem.ApplyFailureCheck의 "일시적 파괴" 로직과 동일한 규칙:
    /// - 엔티티/조인트 자체는 절대 삭제하지 않음 (StopPhysics()가 복구할 대상이 남아있어야 함)
    /// - 바닥에 가까운 블록(y &lt;= 3.1)은 기초라 그대로 둠
    /// </summary>
    private void BreakBlockLoose(EntityManager em, Entity target)
    {
        if (!_jointQuery.IsEmpty)
        {
            var jointEntities = _jointQuery.ToEntityArray(Allocator.Temp);
            var pairs = _jointQuery.ToComponentDataArray<Unity.Physics.PhysicsConstrainedBodyPair>(Allocator.Temp);

            for (int i = 0; i < jointEntities.Length; i++)
            {
                Entity eA = pairs[i].EntityA;
                Entity eB = pairs[i].EntityB;
                if (eA != target && eB != target) continue;

                em.DestroyEntity(jointEntities[i]); // 조인트만 끊음 (블록 엔티티는 그대로)
                ReleaseOne(em, eA);
                ReleaseOne(em, eB);
            }

            jointEntities.Dispose();
            pairs.Dispose();
        }

        ReleaseOne(em, target);
    }

    private void ReleaseOne(EntityManager em, Entity e)
    {
        if (e == Entity.Null || !em.Exists(e)) return;
        if (!em.HasComponent<LocalTransform>(e)) return;

        float3 pos = em.GetComponentData<LocalTransform>(e).Position;
        if (pos.y <= 3.1f) return; // 바닥층은 기초라 그대로 둠 (기존 ReleaseBlockPhysics와 동일 규칙)

        if (em.HasComponent<Unity.Physics.PhysicsGravityFactor>(e))
        {
            em.SetComponentData(e, new Unity.Physics.PhysicsGravityFactor { Value = 1.0f });
        }
        if (em.HasComponent<Unity.Physics.PhysicsMass>(e))
        {
            var m = em.GetComponentData<Unity.Physics.PhysicsMass>(e);
            m.InverseMass = 0.1f;
            m.InverseInertia = new float3(0.1f, 0.1f, 0.1f);
            em.SetComponentData(e, m);
        }
    }
}
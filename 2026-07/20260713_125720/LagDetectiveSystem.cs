using Unity.Entities;
using Unity.Physics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class LagDetectiveSystem : SystemBase
{
    private EntityQuery blockQuery;
    private EntityQuery jointQuery;
    private float checkTimer;

    protected override void OnCreate()
    {
        // 감시할 대상(블록과 조인트)을 찾는 쿼리 생성
        blockQuery = GetEntityQuery(ComponentType.ReadOnly<BlockTag>());
        jointQuery = GetEntityQuery(ComponentType.ReadOnly<JointTag>());
        checkTimer = 0f;
    }

    protected override void OnUpdate()
    {
        // 1. 순간적인 프리징(화면 멈춤) 감지
        // 한 프레임을 처리하는 데 0.1초(100ms) 이상 걸리면 치명적인 병목으로 간주합니다.
        if (SystemAPI.Time.DeltaTime > 0.1f)
        {
            UnityEngine.Debug.LogWarning($"🚨 [렉 감지] 프레임 처리 지연! ({SystemAPI.Time.DeltaTime * 1000f:F1}ms 소요)\n원인 의심: 파일 저장(CSV I/O) 병목 또는 O(N^2) 이중 반복문");
        }

        // 2. 조인트 폭탄 감지 (매 프레임 세면 낭비이므로 1초마다 한 번씩만 검사)
        checkTimer += SystemAPI.Time.DeltaTime;
        if (checkTimer >= 1.0f)
        {
            checkTimer = 0f;

            int blockCount = blockQuery.CalculateEntityCount();
            int jointCount = jointQuery.CalculateEntityCount();

            // 블록당 연결될 수 있는 정상적인 조인트 개수(상하좌우전후 = 최대 6개)를 아득히 초과했는지 검사
            if (jointCount > 10000 && jointCount > blockCount * 5)
            {
                UnityEngine.Debug.LogError($"🧨 [조인트 폭탄 경고] 블록({blockCount}개) 대비 물리 조인트({jointCount}개)가 비정상적으로 많습니다!\n물리 엔진이 마비될 수 있습니다. 고정(Static) 블록 간의 무의미한 용접을 멈추세요!");
            }
        }
    }
}
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

// 화면을 그리기 직전에 카메라 위치를 업데이트합니다.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    private float3 offset;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerData>();
        offset = new float3(0, 15f, -15f); // 연줄의 길이
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Camera.main == null) return;

        // ⭐ 핵심: .WithNone<Prefab>() 을 추가해서 정중앙에 숨어있는 투명 프리팹 유령을 무시합니다!
        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerData>().WithNone<Prefab>())
        {
            float3 playerPos = transform.ValueRO.Position;

            // 1. 카메라 목표 위치 계산
            Vector3 targetPos = playerPos + offset;

            // 2. 부드럽게 고무줄처럼 따라가기
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, targetPos, SystemAPI.Time.DeltaTime * 5f);

            // 3. 고개 까딱임 방지 (항상 45도 땅을 내려다봄)
            Camera.main.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            // ⭐ 1명만 찾았으면 바로 반복문을 종료해서 화면 떨림(지진)을 완벽 차단!
            break;
        }
    }
}
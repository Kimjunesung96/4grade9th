using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

// 화면을 그리기 직전에 카메라 위치를 업데이트합니다.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    private const float Pitch = 30f;              // 내려다보는 각도 (기존 45도 -> 30도)
    private const float Distance = 21.2132f;       // 기존 offset(0,15,-15)와 동일한 거리 유지
    private const float MaxYaw = 90f;            // ⭐ 커서가 화면 좌/우 끝에 있을 때의 최대 회전 각도
    private const float DeadZone = 0.2f;         // ⭐ 화면 중앙 이 비율(0~1) 안에서는 회전 안 함 (조준 중 카메라 안 흔들리게)
    private const float YawSmooth = 8f;          // ⭐ 목표 각도로 따라가는 속도 (클수록 빠름)

    private float yaw; // 현재 좌우 회전 각도 (커서 위치를 부드럽게 따라감)

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerData>();
        yaw = 0f;
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Camera.main == null) return;

        // 1. ⭐ 커서의 화면 X 위치 → 목표 yaw. 중앙 = 0도, 커서를 되돌리면 원래 시점으로 복귀.
        float nx = (Input.mousePosition.x / Mathf.Max(1f, Screen.width) - 0.5f) * 2f; // -1(왼쪽 끝) ~ 1(오른쪽 끝)
        nx = Mathf.Clamp(nx, -1f, 1f);
        float mag = Mathf.Max(0f, Mathf.Abs(nx) - DeadZone) / (1f - DeadZone);        // 데드존 밖만 0~1로 다시 펴줌
        float targetYaw = -Mathf.Sign(nx) * mag * MaxYaw;                              // 부호는 기존 회전 방향과 동일
        yaw = Mathf.Lerp(yaw, targetYaw, SystemAPI.Time.DeltaTime * YawSmooth);

        // 2. 30도 피치 기준으로 높이/수평거리 재계산 (거리(Distance)는 고정)
        float pitchRad = Pitch * Mathf.Deg2Rad;
        float height = Distance * Mathf.Sin(pitchRad);
        float horizontal = Distance * Mathf.Cos(pitchRad);

        // 3. yaw 각도만큼 회전시킨 offset 계산
        float yawRad = yaw * Mathf.Deg2Rad;
        float3 offset = new float3(
            Mathf.Sin(yawRad) * horizontal,
            height,
            -Mathf.Cos(yawRad) * horizontal
        );

        // ⭐ 핵심: .WithNone<Prefab>() 을 추가해서 정중앙에 숨어있는 투명 프리팹 유령을 무시합니다!
        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerData>().WithNone<Prefab>())
        {
            float3 playerPos = transform.ValueRO.Position;

            // 4. 카메라 목표 위치 계산
            Vector3 targetPos = playerPos + offset;

            // 5. 부드럽게 고무줄처럼 따라가기
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, targetPos, SystemAPI.Time.DeltaTime * 5f);

            // 6. 항상 플레이어(중심)를 바라보도록 회전
            Camera.main.transform.LookAt(playerPos);

            // ⭐ 1명만 찾았으면 바로 반복문을 종료해서 화면 떨림(지진)을 완벽 차단!
            break;
        }
    }
}
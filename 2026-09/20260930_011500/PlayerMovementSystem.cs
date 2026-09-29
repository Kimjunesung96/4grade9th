using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Burst;
using UnityEngine;
 
// ⭐ UpdateInGroup을 FixedStepSimulationSystemGroup으로 지정하면 물리 연산 주기와 동기화되어 캐릭터가 덜덜거리는 현상이 사라집니다.
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct PlayerMovementSystem : ISystem
{
    private const float DoubleTapWindow = 0.3f; // ⭐ 이 시간(초) 안에 스페이스를 두 번 누르면 "그 높이 고정" 토글

    private float lastSpaceTapTime;
    private bool isHovering;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerData>();
        lastSpaceTapTime = -999f;
        isHovering = false;
    }
 
    public void OnDestroy(ref SystemState state) { }
 
    public void OnUpdate(ref SystemState state)
    {
        // 1. 키보드 입력 받기 (Input 클래스는 유니티 메인 스레드에서만 접근 가능하므로 밖에서 처리)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        float3 moveInput = new float3(moveX, 0, moveZ);
        bool isJetpackActive = Input.GetKey(KeyCode.Space);
        float deltaTime = SystemAPI.Time.DeltaTime;

        // ⭐ 스페이스바 더블탭 감지 → 현재 높이에서 고정(호버) 토글
        if (Input.GetKeyDown(KeyCode.Space))
        {
            float now = Time.time;
            if (now - lastSpaceTapTime <= DoubleTapWindow)
            {
                isHovering = !isHovering; // 더블탭 성공: 호버 on/off 전환
                lastSpaceTapTime = -999f; // 연속 트리플탭으로 다시 토글되는 것 방지
            }
            else
            {
                lastSpaceTapTime = now; // 첫 번째 탭 기록, 다음 탭이 window 안에 와야 인정됨
            }
        }
        bool isHoveringNow = isHovering;

        // ⭐ 카메라가 CameraFollowSystem에서 계속 회전하니까, 이동도 카메라가 보는 방향 기준으로 바꿔줌
        //    (안 하면 카메라를 돌려도 W/S/A/D가 항상 월드 고정 축으로만 움직여서 "이동이 그대로"인 문제 발생)
        float3 camForward = new float3(0, 0, 1);
        float3 camRight = new float3(1, 0, 0);
        if (Camera.main != null)
        {
            Vector3 f = Camera.main.transform.forward; f.y = 0f;
            Vector3 r = Camera.main.transform.right; r.y = 0f;
            if (f.sqrMagnitude > 0.0001f) f.Normalize();
            if (r.sqrMagnitude > 0.0001f) r.Normalize();
            camForward = new float3(f.x, f.y, f.z);
            camRight = new float3(r.x, r.y, r.z);
        }
 
        // 2. 실제 엔티티 순회와 이동 물리 연산은 C# Job System을 통해 멀티코어로 던집니다 (Burst Compile 적용)
        new PlayerMoveJob
        {
            MoveInput = moveInput,
            CamForward = camForward,
            CamRight = camRight,
            IsJetpackActive = isJetpackActive,
            IsHovering = isHoveringNow,
            DeltaTime = deltaTime
        }.ScheduleParallel();
    }
}
 
// ⭐ BurstCompile을 적용하여 C++에 준하는 속도로 최적화된 기계어로 변환됩니다.
[BurstCompile]
public partial struct PlayerMoveJob : IJobEntity
{
    public float3 MoveInput;
    public float3 CamForward;
    public float3 CamRight;
    public bool IsJetpackActive;
    public bool IsHovering; // ⭐ 스페이스 더블탭으로 켠 "높이 고정" 상태
    public float DeltaTime;
 
    public void Execute(ref PhysicsVelocity velocity, ref PhysicsMass mass, in PlayerData player)
    {
        // 회전 잠금 (오뚝이 유지)
        mass.InverseInertia = float3.zero;
        velocity.Angular = float3.zero;

        // ⭐ 카메라 기준 방향으로 입력을 변환 (W = 카메라가 보는 방향, A/D = 카메라 기준 좌우)
        float3 worldMove = CamRight * MoveInput.x + CamForward * MoveInput.z;
 
        // 기존 y축 속도(중력/제트팩)는 그대로 두고, x와 z축(수평) 이동만 덮어씌웁니다.
        float currentY = velocity.Linear.y;
        velocity.Linear = new float3(worldMove.x * player.MoveSpeed, currentY, worldMove.z * player.MoveSpeed);

        if (IsHovering)
        {
            // ⭐ 호버 모드: 중력/제트팩 다 무시하고 y축 속도를 0으로 고정 → 지금 높이에서 그대로 뜬 채 유지
            //    (좌우/전후 이동은 그대로 가능, 높이만 고정)
            velocity.Linear.y = 0f;
        }
        else
        {
            // 제트팩 상승 (스페이스바)
            if (IsJetpackActive)
            {
                velocity.Linear.y += player.JetpackForce * DeltaTime;
            }
 
            // 추락 속도 제한
            if (velocity.Linear.y < player.MaxFallSpeed)
            {
                velocity.Linear.y = player.MaxFallSpeed;
            }
        }
    }
}
// -----------------
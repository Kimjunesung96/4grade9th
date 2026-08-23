
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
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerData>();
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
 
        // 2. 실제 엔티티 순회와 이동 물리 연산은 C# Job System을 통해 멀티코어로 던집니다 (Burst Compile 적용)
        new PlayerMoveJob
        {
            MoveInput = moveInput,
            IsJetpackActive = isJetpackActive,
            DeltaTime = deltaTime
        }.ScheduleParallel();
    }
}
 
// ⭐ BurstCompile을 적용하여 C++에 준하는 속도로 최적화된 기계어로 변환됩니다.
[BurstCompile]
public partial struct PlayerMoveJob : IJobEntity
{
    public float3 MoveInput;
    public bool IsJetpackActive;
    public float DeltaTime;
 
    public void Execute(ref PhysicsVelocity velocity, ref PhysicsMass mass, in PlayerData player)
    {
        // 회전 잠금 (오뚝이 유지)
        mass.InverseInertia = float3.zero;
        velocity.Angular = float3.zero;
 
        // 기존 y축 속도(중력/제트팩)는 그대로 두고, x와 z축(수평) 이동만 덮어씌웁니다.
        float currentY = velocity.Linear.y;
        velocity.Linear = new float3(MoveInput.x * player.MoveSpeed, currentY, MoveInput.z * player.MoveSpeed);
 
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
// -----------------
 
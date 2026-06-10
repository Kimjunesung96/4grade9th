using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

// ISystem(구조체) 대신 SystemBase(클래스)를 사용하면
// 키보드 입력(Input)이나 Debug.Log 같은 기존 유니티 기능을 훨씬 안정적으로 쓸 수 있습니다.
public partial class PlayerMovementSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. 키보드 입력 받기
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        float3 moveInput = new float3(moveX, 0, moveZ);
        bool isJetpackActive = Input.GetKey(KeyCode.Space);
        float deltaTime = SystemAPI.Time.DeltaTime;

        bool foundPlayer = false; // 플레이어를 찾았는지 확인하는 스위치

        // 2. 물리 속도(PhysicsVelocity)와 플레이어 데이터(PlayerData)가 있는 놈을 찾아서 움직입니다.
        foreach (var (velocity, mass, player) in
                 SystemAPI.Query<RefRW<PhysicsVelocity>, RefRW<PhysicsMass>, RefRO<PlayerData>>())
        {
            foundPlayer = true; // 오! 찾았다!

            // 회전 잠금 (오뚝이 유지)
            mass.ValueRW.InverseInertia = float3.zero;
            velocity.ValueRW.Angular = float3.zero;

            // 기존 y축 속도(중력/제트팩)는 그대로 두고, x와 z축(수평) 이동만 덮어씌웁니다.
            float currentY = velocity.ValueRO.Linear.y;
            velocity.ValueRW.Linear = new float3(moveInput.x * player.ValueRO.MoveSpeed, currentY, moveInput.z * player.ValueRO.MoveSpeed);

            // 제트팩 상승 (스페이스바)
            if (isJetpackActive)
            {
                velocity.ValueRW.Linear.y += player.ValueRO.JetpackForce * deltaTime;
            }

            // 추락 속도 제한
            if (velocity.ValueRW.Linear.y < player.ValueRO.MaxFallSpeed)
            {
                velocity.ValueRW.Linear.y = player.ValueRO.MaxFallSpeed;
            }
        }

        // 3. 디버그: 만약 씬에 캐릭터가 없다면 콘솔창에 빨간불을 띄워줍니다.
        if (!foundPlayer)
        {
            // 이 경고가 뜬다면, 유니티가 캡슐을 엔티티로 제대로 변환(Baking)하지 못한 겁니다!
            // Debug.LogWarning("플레이어 엔티티를 찾지 못했습니다! 서브 씬을 껐다 켜보세요.");
        }
    }
}
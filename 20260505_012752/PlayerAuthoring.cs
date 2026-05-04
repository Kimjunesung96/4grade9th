using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    public float MoveSpeed = 10f;
    public float JetpackForce = 25f; // 제트팩 상승 기류의 세기
    public float MaxFallSpeed = -20f; // 너무 빨리 추락하지 않게 제한

    class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerData
            {
                MoveSpeed = authoring.MoveSpeed,
                JetpackForce = authoring.JetpackForce,
                MaxFallSpeed = authoring.MaxFallSpeed
            });
        }
    }
}

public struct PlayerData : IComponentData
{
    public float MoveSpeed;
    public float JetpackForce;
    public float MaxFallSpeed;
}
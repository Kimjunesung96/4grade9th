using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

// 이 클래스 이름이 파일명과 반드시 같아야 합니다!
public class ObjectColorAuthoring : MonoBehaviour
{
    public Color initialColor = Color.white;

    // Baker 클래스는 Authoring 클래스 안에 포함되어 있어야 합니다.
    public class ObjectColorBaker : Baker<ObjectColorAuthoring>
    {
        public override void Bake(ObjectColorAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ObjectColorData
            {
                Value = new float4(
                    authoring.initialColor.r,
                    authoring.initialColor.g,
                    authoring.initialColor.b,
                    authoring.initialColor.a
                )
            });
        }
    }
}
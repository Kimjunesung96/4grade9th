using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class SpawnerAuthoring : MonoBehaviour
{
    public GameObject Prefab;
    public int2 Count;

    public class SpawnerBaker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SpawnerData
            {
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic),
                Count = authoring.Count
            });
        }
    }
}
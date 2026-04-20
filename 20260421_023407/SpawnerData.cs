using Unity.Entities;
using Unity.Mathematics;

public struct SpawnerData : IComponentData
{
    public Entity Prefab;
    public int2 Count;
}
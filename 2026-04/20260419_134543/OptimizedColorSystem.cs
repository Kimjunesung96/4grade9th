using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct OptimizedColorSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var time = (float)SystemAPI.Time.ElapsedTime;

        // 모든 ObjectColorData를 가진 엔티티를 병렬로 업데이트
        new ColorUpdateJob { Time = time }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct ColorUpdateJob : IJobEntity
{
    public float Time;

    public void Execute(ref ObjectColorData colorData, in LocalTransform transform)
    {
        // 위치(position) 값에 따라 색상이 다르게 변하도록 오프셋 추가
        colorData.Value = new float4(
            math.sin(Time + transform.Position.x) * 0.5f + 0.5f,
            math.cos(Time + transform.Position.y) * 0.5f + 0.5f,
            math.sin(Time + transform.Position.z) * 0.5f + 0.5f,
            1.0f
        );
    }
}
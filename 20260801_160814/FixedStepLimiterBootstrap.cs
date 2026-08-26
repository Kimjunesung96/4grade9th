using Unity.Entities;
using UnityEngine;

// ⭐ 커스텀 IRateManager: FixedStepSimulationSystemGroup이 한 프레임에
// 밀린 물리 스텝을 무한정 몰아서 처리("따라잡기 폭주")하지 못하도록
// 프레임당 최대 실행 횟수를 강제로 제한합니다.
public class CappedFixedRateManager : IRateManager
{
    private readonly float timestep;
    private readonly int maxStepsPerFrame;
    private float accumulatedTime;
    private int stepsThisFrame;

    public CappedFixedRateManager(float timestep, int maxStepsPerFrame)
    {
        this.timestep = timestep;
        this.maxStepsPerFrame = maxStepsPerFrame;
        this.accumulatedTime = 0f;
        this.stepsThisFrame = 0;
    }

    public float Timestep
    {
        get => timestep;
        set { /* 고정값 사용, 런타임 변경 무시 */ }
    }

    public bool ShouldGroupUpdate(ComponentSystemGroup group)
    {
        // 새 실제 프레임이 시작될 때(누적시간이 리셋 직후) 딱 한 번만 deltaTime을 더합니다.
        if (stepsThisFrame == 0)
        {
            accumulatedTime += group.World.Time.DeltaTime;
        }

        if (accumulatedTime >= timestep && stepsThisFrame < maxStepsPerFrame)
        {
            accumulatedTime -= timestep;
            stepsThisFrame++;

            // 그룹 내부 시간(World Time)을 고정 스텝 시간으로 세팅
            group.World.SetTime(new Unity.Core.TimeData(
                elapsedTime: group.World.Time.ElapsedTime,
                deltaTime: timestep));

            return true;
        }

        // ⭐ 폭주 방지 핵심: 밀린 시간이 남아있어도 max에 도달하면
        // 남은 시간을 버리고(드롭) 다음 실제 프레임으로 넘어갑니다.
        if (stepsThisFrame >= maxStepsPerFrame)
        {
            accumulatedTime = 0f;
        }

        stepsThisFrame = 0; // 다음 실제 프레임을 위해 리셋
        return false;
    }
}

public class FixedStepLimiterBootstrap : MonoBehaviour
{
    [Tooltip("한 프레임당 최대 몇 번까지 물리 스텝을 몰아서 처리할지 제한합니다. 렉이 나도 이 값 이상은 절대 몰아서 처리하지 않습니다.")]
    public int maxStepsPerFrame = 2;

    void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogWarning("[FixedStepLimiter] DOTS 월드를 찾을 수 없습니다.");
            return;
        }

        var fixedGroup = world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
        if (fixedGroup != null)
        {
            fixedGroup.RateManager = new CappedFixedRateManager(1f / 60f, maxStepsPerFrame);
            Debug.Log($"[FixedStepLimiter] 적용 완료! 프레임당 최대 {maxStepsPerFrame}회로 물리 스텝 제한.");
        }
        else
        {
            Debug.LogWarning("[FixedStepLimiter] FixedStepSimulationSystemGroup을 찾을 수 없습니다.");
        }
    }
}
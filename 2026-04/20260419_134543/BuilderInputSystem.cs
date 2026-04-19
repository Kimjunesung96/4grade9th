using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BuilderInputSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // 싱글톤 초기화 (SystemAPI 대신 EntityManager를 직접 사용)
        var entity = state.EntityManager.CreateEntity(typeof(BuilderStateData));
        state.EntityManager.SetComponentData(entity, new BuilderStateData
        {
            CurrentMode = 1,
            GuideHeight = 5f
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        // SystemAPI.GetSingletonRW는 state 없이 호출합니다.
        if (!SystemAPI.HasSingleton<BuilderStateData>()) return;

        var builderState = SystemAPI.GetSingletonRW<BuilderStateData>();

        // 1. 모드 전환
        if (Input.GetKeyDown(KeyCode.Alpha0)) builderState.ValueRW.CurrentMode = 0;
        if (Input.GetKeyDown(KeyCode.Alpha1)) builderState.ValueRW.CurrentMode = 1;
        if (Input.GetKeyDown(KeyCode.Alpha2)) builderState.ValueRW.CurrentMode = 2;
        if (Input.GetKeyDown(KeyCode.Alpha3)) builderState.ValueRW.CurrentMode = 3;
        if (Input.GetKeyDown(KeyCode.Alpha4)) builderState.ValueRW.CurrentMode = 4;
        if (Input.GetKeyDown(KeyCode.Alpha5)) builderState.ValueRW.CurrentMode = 5;
        if (Input.GetKeyDown(KeyCode.Alpha6)) builderState.ValueRW.CurrentMode = 6;

        // 2. 9번 편집 모드 토글
        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            builderState.ValueRW.IsGuideEditMode = !builderState.ValueRO.IsGuideEditMode;
            Debug.Log($"9번 가이드/편집 모드: {(builderState.ValueRO.IsGuideEditMode ? "ON" : "OFF")}");
        }

        // 3. 마우스 조작 (가이드라인 처리)
        HandleMouseInput(ref builderState);
    }

    private void HandleMouseInput(ref RefRW<BuilderStateData> state)
    {
        if (Input.GetMouseButtonDown(1))
        {
            state.ValueRW.IsDragging = true;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            state.ValueRW.IsDragging = false;
        }

        if (state.ValueRO.IsGuideEditMode && Input.GetMouseButton(0))
        {
            float scroll = Input.GetAxis("Mouse Y");
            if (math.abs(scroll) > 0.01f)
            {
                state.ValueRW.GuideHeight = math.max(1.0f, state.ValueRO.GuideHeight + (scroll * 10f)); // 속도 조절
            }
        }
    }
}
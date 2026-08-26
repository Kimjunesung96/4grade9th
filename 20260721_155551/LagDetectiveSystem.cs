using Unity.Entities;
using UnityEngine;
using Unity.Profiling;
using System.Collections.Generic;
using System.Linq;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial class LagDetectiveSystem : SystemBase
{
    // 감시 대상 시스템 이름들 (프로젝트에 있는 시스템 이름 그대로 적어주세요)
    private static readonly string[] WatchedSystems = new string[]
    {
        "SpawnerSystem",
        "SpawnerSystem_Cone",
        "SpawnerSystem_Cylinder",
        "SpawnerSystem_Frame",
        "SpawnerSystem_Motor",
        "SpawnerSystem_Pyramid",
        "SpawnerSystem_Solid",
        "StressVisualizationSystem",
        "VibrationTestSystem",
        "ShockwaveTestSystem",
        "BuilderActionSystem",
        "BuilderInputSystem",
        "MaterialPropertyInitSystem",
        "DefaultMaterialInitSystem",
        "CameraFollowSystem",
        "OptimizedColorSystem",
        "PlayerMovementSystem",
    };

    private Dictionary<string, ProfilerRecorder> recorders = new Dictionary<string, ProfilerRecorder>();
    private float checkTimer;

    protected override void OnCreate()
    {
        // 각 시스템 이름으로 프로파일러 레코더를 만들어 둡니다.
        // Unity가 내부적으로 시스템 OnUpdate를 이 이름으로 프로파일링하고 있어서 그대로 후킹 가능합니다.
        foreach (var name in WatchedSystems)
        {
            var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, name, 1);
            recorders[name] = recorder;
        }
    }

    protected override void OnDestroy()
    {
        foreach (var kv in recorders)
        {
            kv.Value.Dispose();
        }
        recorders.Clear();
    }

    protected override void OnUpdate()
    {
        float dt = SystemAPI.Time.DeltaTime;

        // 0.1초(100ms) 이상 걸린 프레임만 상세 분석
        if (dt > 0.1f)
        {
            var results = new List<(string name, double ms)>();

            foreach (var kv in recorders)
            {
                if (kv.Value.Valid && kv.Value.Count > 0)
                {
                    // 나노초 -> 밀리초 변환
                    double ms = kv.Value.LastValue / 1000000.0;
                    results.Add((kv.Key, ms));
                }
            }

            // 오래 걸린 순서로 정렬
            var sorted = results.OrderByDescending(r => r.ms).ToList();

            string report = $"🚨 [렉 감지 상세] 프레임 {dt * 1000f:F1}ms 지연 발생! 범인 랭킹:\n";
            for (int i = 0; i < sorted.Count && i < 5; i++)
            {
                if (sorted[i].ms > 1.0) // 1ms 이상만 표시 (의미없는 항목 제거)
                {
                    report += $"  {i + 1}위: {sorted[i].name} - {sorted[i].ms:F2}ms\n";
                }
            }

            // 어떤 시스템도 크게 잡히지 않았다면 -> Unity 내부(물리 솔버, GC 등) 병목 가능성
            if (sorted.Count == 0 || sorted[0].ms < 1.0)
            {
                report += "  ⚠️ 감시 대상 시스템 중엔 범인이 없습니다. Unity.Physics 솔버, GC(가비지 컬렉션), 또는 렌더링 스레드 병목일 가능성이 높습니다.";
            }

            UnityEngine.Debug.LogWarning(report);
        }

        // GC 발생 감지 (매 프레임 체크는 가볍습니다)
        checkTimer += dt;
        if (checkTimer >= 1.0f)
        {
            checkTimer = 0f;
            long gcMemory = System.GC.GetTotalMemory(false);
            UnityEngine.Debug.Log($"📈 [메모리 체크] 현재 관리 힙: {gcMemory / 1024 / 1024}MB");
        }
    }
}
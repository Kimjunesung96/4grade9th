using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

// T→5 바람 모드.
// 중력 모드(M)의 물리 해제·판정 루프를 그대로 쓰기 때문에, StressVisualizationSystem 의 private 필드/메서드
// (isGravityMode, StartScan, StopPhysics ...)에 접근해야 한다. StressVisualizationSystem 이 partial 이라서
// 바람 모드 관련 코드를 이 파일에 모아 두었다. (StressVisualizationSystem.cs 쪽에는 호출 몇 줄만 남아 있다.)

// 블록 하나가 받는 바람 힘(N). 바람 모드가 켜지거나 풍속/방향이 바뀔 때 갱신된다.
public struct WindForce : IComponentData
{
    public float3 Force;
}

// 매 프레임 바람 힘을 속도에 반영한다 (a = F / m).
// 고정된 바닥 블록(InverseMass = 0)은 움직이지 않고, 조인트가 끊겨 풀려난 블록은 바람에 날린다.
[BurstCompile]
public partial struct ApplyWindJob : IJobEntity
{
    public float DeltaTime;

    public void Execute(in PhysicsMass mass, in WindForce wind, ref PhysicsVelocity velocity)
    {
        if (mass.InverseMass <= 0f) return;
        velocity.Linear += wind.Force * mass.InverseMass * DeltaTime;
    }
}

public partial struct StressVisualizationSystem
{
    // ---- T→5 바람 모드 (중력 모드 기계를 재사용: 물리 해제 + 1초 판정 + 바람 힘) ----
    public static bool IsWindModeActive;
    public static int WindTier = 3;            // 1~9 → 풍속 10~90 m/s
    public static int WindClock = 0;           // 0 = 12시, 1 = 1시 ... 11 = 11시 (시계 방향)
    public static float WindRefDeg;            // 12시 방향의 월드 각도 (활성화 순간 카메라가 바라보던 방향, 0=+X 90=+Z)
    public static string WindClockLabel { get { return (WindClock == 0 ? 12 : WindClock) + "시 방향"; } }

    // ===== T→5 바람 모드 =====
    //  · 중력 모드(M)와 같은 "무한" 모드: T→5 로 켜고 다시 T→5 로 끈다. 끌 때 구조물이 원위치로 복구된다.
    //  · 숫자 1~9: 풍속 (10 m/s 단위).  우클릭: 바람 방향을 시계 방향으로 30° (12시→1시→…→12시), 좌클릭: 반시계 방향.
    //  · 12시 방향 = 켜는 순간 카메라가 바라보던 방향(XZ 평면). 바람은 그 시계 방향 "쪽으로" 분다.
    //  · 블록에 바람 힘(WindForce)을 매 프레임 가하고, 1초마다 StructuralLoadSolver 가 풍하중까지 넣어 파괴를 판정한다.
    private void HandleWindMode(ref SystemState state)
    {
        if (TestMode.Selected(5))
        {
            if (IsWindModeActive) { StopWindMode(ref state); return; }
            if (isGravityMode) { UnityEngine.Debug.LogWarning("[바람 모드] 중력 모드(T→4)가 켜져 있습니다. 먼저 끄세요."); return; }
            if (!JointStressCalc.UseForceBased) { UnityEngine.Debug.LogWarning("[바람 모드] 힘 기반 판정 모드가 꺼져 있어 켤 수 없습니다."); return; }
            StartWindMode(ref state);
            return;
        }
        if (!IsWindModeActive) return;

        if (TestMode.ChordBusy()) return;   // T 를 누른 직후의 숫자는 시험 선택용
        bool changed = false;
        for (int k = 1; k <= 9; k++)
            if (Input.GetKeyDown(KeyCode.Alpha0 + k) || Input.GetKeyDown(KeyCode.Keypad0 + k)) { WindTier = k; changed = true; }
        if (Input.GetMouseButtonDown(1)) { WindClock = (WindClock + 1) % 12; changed = true; }
        if (Input.GetMouseButtonDown(0)) { WindClock = (WindClock + 11) % 12; changed = true; }
        if (!changed) return;

        ApplyWindSettings();
        RefreshWindForces(ref state);
        UnityEngine.Debug.Log($"[바람 모드] 풍속 {StructuralLoadSolver.WindSpeedMs:0}m/s / {WindClockLabel}");
    }

    private void StartWindMode(ref SystemState state)
    {
        IsWindModeActive = true;
        isGravityMode = true;          // 물리 해제/판정 루프는 중력 모드 것을 그대로 쓴다. (IsGravityModeActive 는 false 로 둬서 사격·드래그는 꺼진 상태)
        isWeightScanMode = true;
        StartScan(ref state);
        isScanning = false;            // 스캔 타이머 없이 무한 유지
        TestMode.CaptureNumbers = true; // 숫자 키가 건설 모드로 새지 않게

        float3 fwd = new float3(1, 0, 0);
        if (Camera.main != null) fwd = (float3)Camera.main.transform.forward;
        fwd.y = 0f;
        if (math.lengthsq(fwd) < 1e-6f) fwd = new float3(1, 0, 0);
        WindRefDeg = math.degrees(math.atan2(fwd.z, fwd.x));
        WindClock = 0;

        ApplyWindSettings();
        RefreshWindForces(ref state);
        UnityEngine.Debug.Log($"[바람 모드] 켜짐 — 풍속 {StructuralLoadSolver.WindSpeedMs:0}m/s / {WindClockLabel}. 숫자 1~9 풍속, 우클릭 시계 방향, 좌클릭 반시계 방향, T→5 종료");
    }

    private void StopWindMode(ref SystemState state)
    {
        IsWindModeActive = false;
        TestMode.CaptureNumbers = false;
        isGravityMode = false;
        needsColorUpdate = true;
        foreach (var wf in SystemAPI.Query<RefRW<WindForce>>()) wf.ValueRW.Force = float3.zero;
        StopPhysics(ref state);
        UnityEngine.Debug.Log("[바람 모드] 꺼짐 — 구조물 원위치");
    }

    private static void ApplyWindSettings()
    {
        StructuralLoadSolver.WindSpeedMs = WindTier * 10f;
        StructuralLoadSolver.WindDirDeg = WindRefDeg - WindClock * 30f;   // 시계 방향 = 각도 감소 (위에서 볼 때 +Z 가 위, +X 가 오른쪽)
    }

    // 원래 격자 위치 기준으로 노출 면을 계산해 블록마다 바람 힘(N)을 갱신한다.
    private void RefreshWindForces(ref SystemState state)
    {
        var ents = new System.Collections.Generic.List<Entity>();
        var grids = new System.Collections.Generic.List<int3>();
        foreach (var (pos, entity) in SystemAPI.Query<RefRO<OriginalPosition>>().WithAll<BlockTag>().WithEntityAccess())
        {
            float3 op = pos.ValueRO.Value;
            ents.Add(entity);
            grids.Add(new int3((int)math.floor(op.x / 3.0f + 0.5f), (int)math.floor(op.y / 3.0f + 0.5f), (int)math.floor(op.z / 3.0f + 0.5f)));
        }
        int n = ents.Count;
        if (n == 0) return;

        var fx = new float[n];
        var fz = new float[n];
        StructuralLoadSolver.ComputeWind(grids.ToArray(), StructuralLoadSolver.WindSpeedMs, StructuralLoadSolver.WindDirDeg, fx, fz, out int exposed);
        StructuralLoadSolver.LastWindExposedFaces = exposed;

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        for (int i = 0; i < n; i++)
        {
            var f = new WindForce { Force = new float3(fx[i], 0f, fz[i]) };
            if (SystemAPI.HasComponent<WindForce>(ents[i])) SystemAPI.SetComponent(ents[i], f);
            else ecb.AddComponent(ents[i], f);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
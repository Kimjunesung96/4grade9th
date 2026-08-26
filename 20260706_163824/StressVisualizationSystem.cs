using Unity.Entities;
using Unity.Transforms;
using Unity.Physics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using System.IO;
using System;
using Unity.Burst;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;

public struct OriginalPosition : IComponentData { public float3 Value; }

[BurstCompile]
public partial struct ResetStressJob : IJobEntity
{
    public void Execute(ref BlockStress stress) { stress.TargetStress = 0.0f; }
}

// ⭐ 자체하중/진동 → 블록이 직접 받는 압축성 스트레스 (조인트 당김과는 별개로 관리)
[BurstCompile]
public partial struct ApplyExternalLoadJob : IJobEntity
{
    public bool IsWeightScanMode;
    public float BaseWeight;
    public float DynamicSensitivity;
    public float DeltaTime;
    public float QuakeX;
    public float QuakeZ;

    public void Execute(in PhysicsMass mass, ref BlockStress stress, ref PhysicsVelocity velRW)
    {
        float additionalStress = 0.0f;
        if (IsWeightScanMode)
        {
            float realMass = mass.InverseMass > 0.0f ? (1.0f / mass.InverseMass) : 1.0f;
            additionalStress = BaseWeight * realMass * 0.01f;
        }
        else
        {
            velRW.Linear += new float3(QuakeX, 0.0f, QuakeZ) * DeltaTime;
            additionalStress = math.lengthsq(velRW.Linear) * DynamicSensitivity;
        }
        stress.TargetStress += additionalStress;
    }
}

[BurstCompile]
public partial struct SmoothStressJob : IJobEntity
{
    public float DeltaTime;
    public float SmoothSpeed;
    public void Execute(ref BlockStress stress)
    {
        float currentSmoothed = math.lerp(stress.SmoothedStress, stress.TargetStress, DeltaTime * SmoothSpeed);
        stress.SmoothedStress = math.max(stress.SmoothedStress, currentSmoothed);
    }
}

// ⭐ 매 프레임 "원래 위치 대비 가장 멀리 밀려난 지점"을 갱신. StopPhysics가 리셋하기 전에 값이 확보되어야 함.
[BurstCompile]
public partial struct TrackMaxDisplacementJob : IJobEntity
{
    public void Execute(in LocalTransform transform, in OriginalPosition origin, ref BlockDisplacement disp)
    {
        float dist = math.distance(transform.Position, origin.Value);
        if (dist > disp.MaxDist)
        {
            disp.MaxDist = dist;
            disp.MaxPos = transform.Position;
        }
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
public partial struct StressVisualizationSystem : ISystem
{
    private float scanTimer;
    private bool isScanning;
    private bool needsColorUpdate;
    private bool isWeightScanMode;
    private EntityQuery jointQuery;

    // ⭐ 1/3초(초당 3회)마다 인장/압축 파괴 판정
    private float failureTickTimer;
    private const float FailureTickInterval = 1.0f / 3.0f;

    // ⭐ 조인트가 늘어난 거리를 "인장 스트레스" 숫자로 바꾸는 배율 (튜닝 가능한 값)
    private const float TensionStressScale = 100.0f;

    // 스캔 도중 압축 파괴된 블록들을 최종 CSV에 기록하기 위한 스냅샷
    private NativeList<FixedString512Bytes> destroyedLines;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BlockStress>();
        jointQuery = state.GetEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>(), ComponentType.ReadOnly<PhysicsJoint>());
        destroyedLines = new NativeList<FixedString512Bytes>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (destroyedLines.IsCreated) destroyedLines.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (Input.GetKeyDown(KeyCode.V)) { isWeightScanMode = true; StartScan(ref state); }
        else if (Input.GetKeyDown(KeyCode.B)) { isWeightScanMode = false; StartScan(ref state); }

        if (isScanning)
        {
            scanTimer -= SystemAPI.Time.DeltaTime;
            if (scanTimer <= 0.0f) { isScanning = false; needsColorUpdate = true; StopPhysics(ref state); return; }

            state.Dependency = new ResetStressJob().ScheduleParallel(state.Dependency);

            float time = (float)SystemAPI.Time.ElapsedTime;
            state.Dependency = new ApplyExternalLoadJob
            {
                IsWeightScanMode = isWeightScanMode,
                BaseWeight = 1.0f,
                DynamicSensitivity = 0.5f,
                DeltaTime = SystemAPI.Time.DeltaTime,
                QuakeX = !isWeightScanMode ? math.sin(time * 35.0f) * 5.0f : 0.0f,
                QuakeZ = !isWeightScanMode ? math.cos(time * 28.0f) * 5.0f : 0.0f
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new SmoothStressJob { DeltaTime = SystemAPI.Time.DeltaTime, SmoothSpeed = 3.0f }.ScheduleParallel(state.Dependency);

            // ⭐ 리셋 전에 최대 변위 지점을 계속 갱신
            state.Dependency = new TrackMaxDisplacementJob().ScheduleParallel(state.Dependency);

            // ⭐ 1/3초마다 인장(조인트 절단)/압축(블록 파괴) 즉시 판정
            failureTickTimer -= SystemAPI.Time.DeltaTime;
            if (failureTickTimer <= 0.0f)
            {
                failureTickTimer += FailureTickInterval;
                state.Dependency.Complete();
                ApplyFailureCheck(ref state);
            }
        }

        if (needsColorUpdate) { UpdateResults(ref state); }
    }

    private void StartScan(ref SystemState state)
    {
        scanTimer = 5.0f; isScanning = true; needsColorUpdate = false;
        failureTickTimer = FailureTickInterval;
        destroyedLines.Clear();
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        NativeList<Entity> allEntities = new NativeList<Entity>(Allocator.Temp);
        NativeList<float3> allPositions = new NativeList<float3>(Allocator.Temp);

        // 1. 씬에 존재하는 모든 블록 수집
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BlockTag>().WithEntityAccess())
        {
            allEntities.Add(entity);
            allPositions.Add(transform.ValueRO.Position);
        }

        // 🔥 빠른 검색과 안전한 중복 방지를 위해 NativeHashSet 사용
        NativeHashSet<Entity> badEntities = new NativeHashSet<Entity>(allEntities.Length, Allocator.Temp);
        int badCount = 0;

        // 2. 불량 블록(겹침, 파편) 판별
        for (int i = 0; i < allEntities.Length; i++)
        {
            float3 myPos = allPositions[i];
            float minDiff = float.MaxValue;

            for (int j = 0; j < allEntities.Length; j++)
            {
                if (i == j) continue;
                float3 otherPos = allPositions[j];
                float diff = math.abs(myPos.x - otherPos.x) + math.abs(myPos.y - otherPos.y) + math.abs(myPos.z - otherPos.z);
                if (diff < minDiff) minDiff = diff;
            }

            float diffSum = minDiff * 10.0f;

            // 🚫 조건: 파고든 겹침 블록이거나, 건물 밖 허공의 파편인 경우
            if (diffSum <= 29.5f || diffSum >= 69.5f)
            {
                if (badEntities.Add(allEntities[i]))
                {
                    badCount++;
                    ecb.DestroyEntity(allEntities[i]); // 블록 파괴 예약
                }
            }
        }

        // 3. ⭐ [핵심 추가] 파괴될 블록과 연결된 '조인트(Joint)'도 함께 찾아서 파괴! (물리엔진 에러 원천 차단)
        foreach (var (jointPair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
        {
            // 이 조인트가 연결한 두 블록(EntityA, EntityB) 중 하나라도 삭제 예정이라면 이 조인트도 쓸모없으므로 파괴
            if (badEntities.Contains(jointPair.ValueRO.EntityA) || badEntities.Contains(jointPair.ValueRO.EntityB))
            {
                ecb.DestroyEntity(entity);
            }
        }

        if (badCount > 0)
        {
            UnityEngine.Debug.LogWarning($"[안전 스폰] 진단 시작: 폭발 유발 블록 및 고립된 파편 {badCount}개 (및 관련 조인트) 자동 삭제 완료!");
        }

        // 3.5. ⭐ 살아있는 조인트 중 "원래 길이(자연 길이)"가 아직 없는 것에만 최초 1회 부여
        //      (이후 스캔에서는 절대 덮어쓰지 않음 — 자연 길이는 구조물이 변형되기 전 상태여야 하므로)
        foreach (var (jointPair, joint, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>, RefRO<PhysicsJoint>>().WithAll<JointTag>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<JointRestLength>(entity)) continue;

            Entity eA = jointPair.ValueRO.EntityA; Entity eB = jointPair.ValueRO.EntityB;
            if (badEntities.Contains(eA) || badEntities.Contains(eB)) continue;
            if (!SystemAPI.HasComponent<LocalTransform>(eA) || !SystemAPI.HasComponent<LocalTransform>(eB)) continue;

            var transA = SystemAPI.GetComponent<LocalTransform>(eA);
            var transB = SystemAPI.GetComponent<LocalTransform>(eB);
            float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.ValueRO.BodyAFromJoint.Position);
            float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.ValueRO.BodyBFromJoint.Position);
            float restDist = math.distance(pivotA, pivotB);

            ecb.AddComponent(entity, new JointRestLength { Value = restDist });
        }

        // 4. 살아남은 정상 블록에만 안전하게 컴포넌트 추가 및 중력 활성화
        foreach (var (color, stress, transform, entity) in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>, RefRW<BlockStress>, RefRO<LocalTransform>>().WithAll<BlockTag>().WithEntityAccess())
        {
            // 삭제 예정인 블록은 건너뛰어 AddComponent 오류를 방지합니다.
            if (badEntities.Contains(entity)) continue;

            color.ValueRW.Value = new float4(1.0f, 1.0f, 1.0f, 1.0f);
            stress.ValueRW.SmoothedStress = 0.0f; stress.ValueRW.TargetStress = 0.0f;

            if (!SystemAPI.HasComponent<OriginalPosition>(entity))
            {
                ecb.AddComponent(entity, new OriginalPosition { Value = transform.ValueRO.Position });
            }

            // ⭐ 이번 스캔의 최대 변위 기록을 원래 위치로 초기화 (매 스캔마다 리셋)
            float3 originForDisp = SystemAPI.HasComponent<OriginalPosition>(entity)
                ? SystemAPI.GetComponent<OriginalPosition>(entity).Value
                : transform.ValueRO.Position;

            if (!SystemAPI.HasComponent<BlockDisplacement>(entity))
            {
                ecb.AddComponent(entity, new BlockDisplacement { MaxPos = originForDisp, MaxDist = 0.0f });
            }
            else
            {
                ecb.SetComponent(entity, new BlockDisplacement { MaxPos = originForDisp, MaxDist = 0.0f });
            }
        }

        foreach (var (gravity, velocity, entity) in SystemAPI.Query<RefRW<PhysicsGravityFactor>, RefRW<PhysicsVelocity>>().WithAll<BlockTag>().WithEntityAccess())
        {
            if (badEntities.Contains(entity)) continue;

            gravity.ValueRW.Value = 1.0f;
            velocity.ValueRW.Linear.y -= 0.01f;
        }

        ecb.Playback(state.EntityManager);

        // 메모리 정리
        ecb.Dispose();
        allEntities.Dispose();
        allPositions.Dispose();
        badEntities.Dispose();
    }

    private void StopPhysics(ref SystemState state)
    {
        foreach (var (transform, velocity, gravity, originalPos) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<PhysicsGravityFactor>, RefRO<OriginalPosition>>())
        {
            gravity.ValueRW.Value = 0.0f; velocity.ValueRW.Linear = float3.zero; velocity.ValueRW.Angular = float3.zero;
            transform.ValueRW.Position = originalPos.ValueRO.Value;
            transform.ValueRW.Rotation = quaternion.identity;
        }
    }

    // ⭐ 1/3초마다 호출: 인장 초과 → 조인트만 절단 / 압축 초과 → 블록 통째로 파괴
    private void ApplyFailureCheck(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 1) 인장 파괴: 조인트가 늘어난 만큼(스트레스)이 두 블록 평균 Tensile(방어력)을 넘으면 그 조인트만 끊음
        foreach (var (jointPair, joint, rest, jointEntity) in SystemAPI.Query<
          RefRO<PhysicsConstrainedBodyPair>, RefRO<PhysicsJoint>, RefRO<JointRestLength>>().WithAll<JointTag>().WithEntityAccess())
        {
            Entity eA = jointPair.ValueRO.EntityA; Entity eB = jointPair.ValueRO.EntityB;
            if (!SystemAPI.HasComponent<LocalTransform>(eA) || !SystemAPI.HasComponent<LocalTransform>(eB)) continue;
            if (!SystemAPI.HasComponent<BlockMaterial>(eA) || !SystemAPI.HasComponent<BlockMaterial>(eB)) continue;

            var transA = SystemAPI.GetComponent<LocalTransform>(eA);
            var transB = SystemAPI.GetComponent<LocalTransform>(eB);
            float3 pivotA = math.transform(new RigidTransform(transA.Rotation, transA.Position), joint.ValueRO.BodyAFromJoint.Position);
            float3 pivotB = math.transform(new RigidTransform(transB.Rotation, transB.Position), joint.ValueRO.BodyBFromJoint.Position);
            float dist = math.distance(pivotA, pivotB);
            float stretch = math.max(0.0f, dist - rest.ValueRO.Value);
            if (stretch <= 0.0f) continue;

            var matA = SystemAPI.GetComponent<BlockMaterial>(eA);
            var matB = SystemAPI.GetComponent<BlockMaterial>(eB);
            float tensileStress = stretch * TensionStressScale;
            float tensileDefense = (matA.TensileStiffness + matB.TensileStiffness) * 0.5f;

            if (tensileStress > tensileDefense)
            {
                // ⭐ 블록은 그대로 두고 연결만 끊음 → 중력으로 떨어지거나 기울기 시작
                ecb.DestroyEntity(jointEntity);
            }
        }

        // 2) 압축 파괴: 블록 자체 스트레스(자체하중/진동)가 Compressive(방어력)를 넘으면 블록 통째로 파괴
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (stress, mat, pos, entity) in SystemAPI.Query<
          RefRO<BlockStress>, RefRO<BlockMaterial>, RefRO<OriginalPosition>>().WithEntityAccess())
        {
            float compStress = stress.ValueRO.SmoothedStress;
            if (compStress <= mat.ValueRO.CompressiveStiffness) continue;

            float3 originPos = pos.ValueRO.Value;
            float ix = math.round(originPos.x * 10f); float iy = math.round(originPos.y * 10f); float iz = math.round(originPos.z * 10f);
            string strX = (ix < 0f ? "-" : "0") + math.abs(ix).ToString("000");
            string strZ = (iz < 0f ? "-" : "0") + math.abs(iz).ToString("000");
            string strY = (iy < 0f ? "-" : "0") + math.abs(iy).ToString("000");
            string id = strX + "_" + strZ + "_" + strY;
            string mName = mat.ValueRO.MaterialName.ToString().Replace("\0", "").Trim();
            string type = originPos.y > 1.5f ? "Wall" : "Floor";

            string destroyedLineStr = id + ",DESTROYED,DESTROYED,DESTROYED," +
                                compStress.ToString("F2") + ",Destroyed,Y," +
                                mName + "," + mat.ValueRO.TensileStiffness.ToString("F1") + "," +
                                mat.ValueRO.CompressiveStiffness.ToString("F1") + "," + "Existing" + "," + type;
            destroyedLines.Add(new FixedString512Bytes(destroyedLineStr));

            toDestroy.Add(entity);
        }

        // 압축 파괴된 블록에 연결된 조인트도 정리 (물리엔진이 죽은 참조 밟지 않도록)
        foreach (var (jointPair, jointEntity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
        {
            for (int i = 0; i < toDestroy.Length; i++)
            {
                if (jointPair.ValueRO.EntityA == toDestroy[i] || jointPair.ValueRO.EntityB == toDestroy[i])
                {
                    ecb.DestroyEntity(jointEntity);
                    break;
                }
            }
        }

        for (int i = 0; i < toDestroy.Length; i++)
        {
            ecb.DestroyEntity(toDestroy[i]);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        toDestroy.Dispose();
    }

    private void UpdateResults(ref SystemState state)
    {
        state.Dependency.Complete();
        string path = Path.Combine(Application.dataPath, "StressBlock", "CurrentStress.csv");
        string reinforcePath = Path.Combine(Application.dataPath, "StressBlock", "Reinforcement_Plan.csv");
        string lastBuildPath = Path.Combine(Application.dataPath, "StressBlock", "Last_Building.csv");

        var toolMap = new Dictionary<string, string>();
        var typeMap = new Dictionary<string, string>();
        var matMap = new Dictionary<string, string>();
        var tensileMap = new Dictionary<string, string>();
        var compMap = new Dictionary<string, string>();

        // ① 기존 CurrentStress 읽기 (Y키에서 수정한 재질 데이터 완벽 보존)
        if (File.Exists(path))
        {
            var oldLines = File.ReadAllLines(path).ToList();
            for (int i = 1; i < oldLines.Count; i++)
            {
                var c = oldLines.ElementAt(i).Split(',').ToList();
                if (c.Count >= 12)
                {
                    string k = c.ElementAt(0);
                    toolMap[k] = c.ElementAt(10);
                    typeMap[k] = c.ElementAt(11);
                    matMap[k] = c.ElementAt(7);      // 재질 이름 보존
                    tensileMap[k] = c.ElementAt(8);  // 인장 강도 보존
                    compMap[k] = c.ElementAt(9);     // 압축 강도 보존
                }
            }
        }

        // ② Reinforcement_Plan 읽기 (최신 보강 철근 데이터 덮어쓰기)
        if (File.Exists(reinforcePath))
        {
            var rLines = File.ReadAllLines(reinforcePath).ToList();
            for (int i = 1; i < rLines.Count; i++)
            {
                var c = rLines.ElementAt(i).Split(',').ToList();
                if (c.Count >= 12)
                {
                    string k = c.ElementAt(0);
                    toolMap[k] = c.ElementAt(10);
                    matMap[k] = c.ElementAt(7);
                    tensileMap[k] = c.ElementAt(8);
                    compMap[k] = c.ElementAt(9);
                }
            }
        }

        // ③ Last_Building 읽기
        if (File.Exists(lastBuildPath))
        {
            var lLines = File.ReadAllLines(lastBuildPath).ToList();
            for (int i = 1; i < lLines.Count; i++)
            {
                var c = lLines.ElementAt(i).Split(',').ToList();
                if (c.Count >= 12)
                {
                    string k = c.ElementAt(0);
                    if (typeMap.ContainsKey(k)) typeMap.Remove(k);
                    typeMap.Add(k, c.ElementAt(11));
                }
            }
        }

        // ④ 보존된 데이터를 바탕으로 스트레스만 업데이트하여 CSV 작성
        using (StreamWriter writer = new StreamWriter(path, false))
        {
            writer.WriteLine("BlockID,PosX,PosY,PosZ,Stress,RiskLevel,Prescription,Material,Tensile,Compressive,Tool,Type");

            foreach (var (stress, color, mat, pos, disp) in SystemAPI.Query<
              RefRO<BlockStress>,
              RefRW<URPMaterialPropertyBaseColor>,
              RefRO<BlockMaterial>,
              RefRO<OriginalPosition>,
              RefRO<BlockDisplacement>>())
            {
                // ⭐ ID는 반드시 "원래 위치" 기준으로 생성 (자재 lookup 키/보강 로직과의 연결고리 유지)
                float3 originPos = pos.ValueRO.Value;
                float ix = math.round(originPos.x * 10f); float iy = math.round(originPos.y * 10f); float iz = math.round(originPos.z * 10f);

                string strX = (ix < 0f ? "-" : "0") + math.abs(ix).ToString("000");
                string strZ = (iz < 0f ? "-" : "0") + math.abs(iz).ToString("000");
                string strY = (iy < 0f ? "-" : "0") + math.abs(iy).ToString("000");
                string id = strX + "_" + strZ + "_" + strY;

                // ⭐ PosX/Y/Z에는 이번 스캔에서 가장 멀리 밀려난 지점을 기록
                float3 p = disp.ValueRO.MaxDist > 0.0f ? disp.ValueRO.MaxPos : originPos;

                // ⭐ 기존 CSV의 재료 데이터가 있으면 그것을 최우선으로 가져옴! (V키가 재질을 엎어버리는 현상 해결)
                string mName = matMap.ContainsKey(id) ? matMap[id] : mat.ValueRO.MaterialName.ToString().Replace("\0", "").Trim();
                string tStr = tensileMap.ContainsKey(id) ? tensileMap[id] : mat.ValueRO.TensileStiffness.ToString("F1");
                string cStr = compMap.ContainsKey(id) ? compMap[id] : mat.ValueRO.CompressiveStiffness.ToString("F1");

                // 강도 문자열을 숫자로 안전하게 변환
                float tensile = 100f; float compressive = 100f;
                float.TryParse(tStr, out tensile);
                float.TryParse(cStr, out compressive);
                if (tensile <= 0.1f) tensile = mat.ValueRO.TensileStiffness;
                if (compressive <= 0.1f) compressive = mat.ValueRO.CompressiveStiffness;

                float curStress = stress.ValueRO.SmoothedStress; // 자체하중/진동에 의한 압축성 스트레스
                float t = math.clamp(curStress / math.max(1.0f, compressive), 0.0f, 1.0f);

                // ⭐ 재질별 고유 색상 매핑
                float4 baseCol = new float4(0.7f, 0.7f, 0.7f, 1.0f); // 콘크리트 회색
                if (mName.Contains("Steel")) baseCol = new float4(0.2f, 0.5f, 1.0f, 1.0f); // 파란색
                else if (mName.Contains("Wood") || mName.Contains("Timber")) baseCol = new float4(0.6f, 0.4f, 0.2f, 1.0f); // 갈색
                else if (mName.Contains("Brick")) baseCol = new float4(0.8f, 0.3f, 0.2f, 1.0f); // 진홍색

                string risk = "Safe";
                string pres = "N";

                if (t >= 0.8f)
                {
                    color.ValueRW.Value = new float4(1.0f, 0.0f, 0.0f, 1.0f); // 위험(빨강)
                    risk = "Danger";
                    pres = "Y";
                }
                else if (t >= 0.5f)
                {
                    color.ValueRW.Value = new float4(1.0f, 1.0f, 0.0f, 1.0f); // 경고(노랑)
                    risk = "Warning";
                }
                else
                {
                    color.ValueRW.Value = baseCol; // 안전(재질 본연의 색상)
                }

                string tool = toolMap.ContainsKey(id) ? toolMap[id] : "Existing";
                string type = typeMap.ContainsKey(id) ? typeMap[id] : (originPos.y > 1.5f ? "Wall" : "Floor");

                // 최종적으로 업데이트된 스트레스와 보존된 재질 정보를 엑셀로 저장
                string lineData = id + "," +
                                  p.x.ToString("F2") + "," +
                                  p.y.ToString("F2") + "," +
                                  p.z.ToString("F2") + "," +
                                  curStress.ToString("F2") + "," +
                                  risk + "," +
                                  pres + "," +
                                  mName + "," +
                                  tStr + "," +
                                  cStr + "," +
                                  tool + "," +
                                  type;

                writer.WriteLine(lineData);
            }

            // ⭐ 스캔 도중(1/3초 틱) 압축 파괴로 이미 사라진 블록들도 마저 기록
            foreach (var line in destroyedLines)
            {
                writer.WriteLine(line.ToString());
            }
        }

        needsColorUpdate = false;
        UnityEngine.Debug.Log("📊 [통합 CSV] 스트레스 업데이트 완료! (Y키의 재질 데이터 완벽 보존)");
    }
}
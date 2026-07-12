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

    private float failureTickTimer;
    private const float FailureTickInterval = 1.0f / 3.0f;

    private const float TensionStressScale = 5000.0f;

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
            state.Dependency = new TrackMaxDisplacementJob().ScheduleParallel(state.Dependency);

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

        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<BlockTag>().WithEntityAccess())
        {
            allEntities.Add(entity);
            allPositions.Add(transform.ValueRO.Position);
        }

        NativeHashSet<Entity> badEntities = new NativeHashSet<Entity>(allEntities.Length, Allocator.Temp);
        int badCount = 0;

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

            if (diffSum <= 29.5f || diffSum >= 69.5f)
            {
                if (badEntities.Add(allEntities[i]))
                {
                    badCount++;
                    ecb.DestroyEntity(allEntities[i]);
                }
            }
        }

        foreach (var (jointPair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
        {
            if (badEntities.Contains(jointPair.ValueRO.EntityA) || badEntities.Contains(jointPair.ValueRO.EntityB))
            {
                ecb.DestroyEntity(entity);
            }
        }

        if (badCount > 0)
        {
            UnityEngine.Debug.LogWarning($"[안전 스폰] 진단 시작: 폭발 유발 블록 및 고립된 파편 {badCount}개 (및 관련 조인트) 자동 삭제 완료!");
        }

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

        foreach (var (color, stress, transform, entity) in SystemAPI.Query<RefRW<URPMaterialPropertyBaseColor>, RefRW<BlockStress>, RefRO<LocalTransform>>().WithAll<BlockTag>().WithEntityAccess())
        {
            if (badEntities.Contains(entity)) continue;

            color.ValueRW.Value = new float4(1.0f, 1.0f, 1.0f, 1.0f);
            stress.ValueRW.SmoothedStress = 0.0f; stress.ValueRW.TargetStress = 0.0f;
            stress.ValueRW.MaxTensileRatio = 0.0f; 
            
            if (!SystemAPI.HasComponent<OriginalPosition>(entity))
            {
                ecb.AddComponent(entity, new OriginalPosition { Value = transform.ValueRO.Position });
            }

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

    private void ApplyFailureCheck(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

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

            float ratio = tensileStress / math.max(1.0f, tensileDefense);

            if (SystemAPI.HasComponent<BlockStress>(eA)) 
            {
                var stressA = SystemAPI.GetComponentRW<BlockStress>(eA);
                stressA.ValueRW.MaxTensileRatio = math.max(stressA.ValueRO.MaxTensileRatio, ratio);
            }
            if (SystemAPI.HasComponent<BlockStress>(eB)) 
            {
                var stressB = SystemAPI.GetComponentRW<BlockStress>(eB);
                stressB.ValueRW.MaxTensileRatio = math.max(stressB.ValueRO.MaxTensileRatio, ratio);
            }

            if (tensileStress > tensileDefense)
            {
                ecb.DestroyEntity(jointEntity);
            }
        }

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
            
            // ⭐ [핵심 수정] 무작정 Wall로 덮어씌우지 않기 위해, 데이터를 조각내어 '|' 기호로 포장해둡니다.
            // 나중에 UpdateResults에서 딕셔너리와 결합하여 정확한 꼬리표를 찾아줄 거예요.
            string destroyedLineStr = id + "|" +
                                      compStress.ToString("F2") + "|" +
                                      mName + "|" +
                                      mat.ValueRO.TensileStiffness.ToString("F1") + "|" +
                                      mat.ValueRO.CompressiveStiffness.ToString("F1") + "|" +
                                      originPos.y.ToString("F2");
                                      
            destroyedLines.Add(new FixedString512Bytes(destroyedLineStr));

            toDestroy.Add(entity);
        }

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
                    matMap[k] = c.ElementAt(7);      
                    tensileMap[k] = c.ElementAt(8);  
                    compMap[k] = c.ElementAt(9);     
                }
            }
        }

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
                    typeMap[k] = c.ElementAt(11); // ⭐ [핵심 수정] 여기서 Type(Reinforcement) 정보도 확실히 읽어옵니다!
                    matMap[k] = c.ElementAt(7);
                    tensileMap[k] = c.ElementAt(8);
                    compMap[k] = c.ElementAt(9);
                }
            }
        }

        if (File.Exists(lastBuildPath))
        {
            var lLines = File.ReadAllLines(lastBuildPath).ToList();
            for (int i = 1; i < lLines.Count; i++)
            {
                var c = lLines.ElementAt(i).Split(',').ToList();
                if (c.Count >= 12)
                {
                    string k = c.ElementAt(0);
                    typeMap[k] = c.ElementAt(11); 
                    toolMap[k] = c.ElementAt(10); // ⭐ 도구 이름도 확실히 갱신!
                    matMap[k] = c.ElementAt(7);   // ⭐ 재질도 최신으로 갱신!
                }
            }
        }

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
                float3 originPos = pos.ValueRO.Value;
                float ix = math.round(originPos.x * 10f); float iy = math.round(originPos.y * 10f); float iz = math.round(originPos.z * 10f);

                string strX = (ix < 0f ? "-" : "0") + math.abs(ix).ToString("000");
                string strZ = (iz < 0f ? "-" : "0") + math.abs(iz).ToString("000");
                string strY = (iy < 0f ? "-" : "0") + math.abs(iy).ToString("000");
                string id = strX + "_" + strZ + "_" + strY;

                float3 p = disp.ValueRO.MaxDist > 0.0f ? disp.ValueRO.MaxPos : originPos;

                string mName = matMap.ContainsKey(id) ? matMap[id] : mat.ValueRO.MaterialName.ToString().Replace("\0", "").Trim();
                string tStr = tensileMap.ContainsKey(id) ? tensileMap[id] : mat.ValueRO.TensileStiffness.ToString("F1");
                string cStr = compMap.ContainsKey(id) ? compMap[id] : mat.ValueRO.CompressiveStiffness.ToString("F1");

                float tensile = 100f; float compressive = 100f;
                float.TryParse(tStr, out tensile);
                float.TryParse(cStr, out compressive);
                if (tensile <= 0.1f) tensile = mat.ValueRO.TensileStiffness;
                if (compressive <= 0.1f) compressive = mat.ValueRO.CompressiveStiffness;

                float curStress = stress.ValueRO.SmoothedStress;
                float compRatio = math.clamp(curStress / math.max(1.0f, compressive), 0.0f, 1.0f);
                float tensRatio = math.clamp(stress.ValueRO.MaxTensileRatio, 0.0f, 1.0f);
                float t = math.max(compRatio, tensRatio);
                float finalStressRecord = math.max(curStress, stress.ValueRO.MaxTensileRatio * tensile);

                float4 baseCol = new float4(0.7f, 0.7f, 0.7f, 1.0f);
                if (mName.Contains("Steel")) baseCol = new float4(0.2f, 0.5f, 1.0f, 1.0f);
                else if (mName.Contains("Wood") || mName.Contains("Timber")) baseCol = new float4(0.6f, 0.4f, 0.2f, 1.0f);
                else if (mName.Contains("Brick")) baseCol = new float4(0.8f, 0.3f, 0.2f, 1.0f);

                string risk = "Safe";
                string pres = "N";

                color.ValueRW.Value = math.lerp(baseCol, new float4(1.0f, 0.0f, 0.0f, 1.0f), t);

                if (t >= 0.99f) { risk = "Danger"; pres = "Y"; }
                else if (t >= 0.66f) { risk = "Danger"; pres = "Y"; }
                else if (t >= 0.33f) { risk = "Warning"; pres = "N"; }
                else { risk = "Safe"; pres = "N"; }

                string tool = toolMap.ContainsKey(id) ? toolMap[id] : "Existing";
                string type = typeMap.ContainsKey(id) ? typeMap[id] : (originPos.y > 1.5f ? "Wall" : "Floor");

                string lineData = id + "," +
                                  p.x.ToString("F2") + "," +
                                  p.y.ToString("F2") + "," +
                                  p.z.ToString("F2") + "," +
                                  finalStressRecord.ToString("F2") + "," + 
                                  risk + "," +
                                  pres + "," +
                                  mName + "," +
                                  tStr + "," +
                                  cStr + "," +
                                  tool + "," +
                                  type;

                writer.WriteLine(lineData);
            }

            // ⭐ [핵심 수정] 무너진 블록들도 마지막에 딕셔너리 검사를 거쳐 영원히 원래 꼬리표를 유지하도록 출력합니다!
            foreach (var line in destroyedLines)
            {
                var parts = line.ToString().Split('|');
                if (parts.Length < 6) continue;
                
                string dId = parts[0];
                string dCompStress = parts[1];
                string dMatName = parts[2];
                string dTensile = parts[3];
                string dComp = parts[4];
                float dPosY = float.Parse(parts[5]);
                
                // 파괴된 녀석들도 족보(map)를 뒤져서 본래 자기 도구와 타입을 찾아냄!
                string dTool = toolMap.ContainsKey(dId) ? toolMap[dId] : "Existing";
                string dType = typeMap.ContainsKey(dId) ? typeMap[dId] : (dPosY > 1.5f ? "Wall" : "Floor");
                
                string finalDestroyedLine = dId + ",DESTROYED,DESTROYED,DESTROYED," +
                                            dCompStress + ",Destroyed,Y," +
                                            dMatName + "," + dTensile + "," + dComp + "," +
                                            dTool + "," + dType;
                                            
                writer.WriteLine(finalDestroyedLine);
            }
        }

        needsColorUpdate = false;
        UnityEngine.Debug.Log("📊 [통합 CSV] 스트레스 업데이트 완료! (Y키의 재질 및 보강재 꼬리표 완벽 보존)");
    }
}
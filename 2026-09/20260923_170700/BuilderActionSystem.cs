using Unity.Entities;
using Unity.Physics; // ⭐ 조인트(철근) 데이터를 읽어오기 위한 물리 장비 장착!
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct BuilderActionSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BuilderStateData>()) return;
        var builderState = SystemAPI.GetSingleton<BuilderStateData>();
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // [Enter 키]: 가이드 확정 및 건설 실행
        if (Input.GetKeyDown(KeyCode.Return) && builderState.CurrentMode >= 1 && builderState.CurrentMode <= 6)
        {
            Debug.Log($"[건설 확정] {builderState.CurrentMode}번 구조물 생성!");
            // 여기에 각 모드별 블록 Instantiate 및 Physics Joint 연결 로직 호출
        }

        // [H 키]: 0번 모드에서 선택된 블록 보호(Lock) 토글
        if (builderState.CurrentMode == 0 && Input.GetKeyDown(KeyCode.H))
        {
            foreach (var (selected, entity) in SystemAPI.Query<RefRO<SelectedTag>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<ProtectedTag>(entity))
                {
                    ecb.RemoveComponent<ProtectedTag>(entity);
                    Debug.Log("보호 해제됨");
                }
                else
                {
                    ecb.AddComponent<ProtectedTag>(entity);
                    Debug.Log("보호 설정됨 (R키 면역)");
                }
            }
        }

        // [Delete 키]: 0번 모드에서 선택된 블록 즉시 삭제
        if (builderState.CurrentMode == 0 && Input.GetKeyDown(KeyCode.Delete))
        {
            // 1. 선택된 벽돌 파괴
            foreach (var (selected, entity) in SystemAPI.Query<RefRO<SelectedTag>>().WithEntityAccess())
            {
                ecb.DestroyEntity(entity);
            }

            // ⭐ 2. 유령 철근 철거: 조인트 양끝 중 하나라도 '삭제될 운명(Selected)'이라면 조인트도 파괴!
            foreach (var (pair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
            {
                bool aSelected = SystemAPI.HasComponent<SelectedTag>(pair.ValueRO.EntityA);
                bool bSelected = SystemAPI.HasComponent<SelectedTag>(pair.ValueRO.EntityB);

                if (aSelected || bSelected)
                {
                    ecb.DestroyEntity(entity);
                }
            }
            Debug.Log("선택된 객체 및 연결된 조인트 삭제 완료");
        }

        // [R 키]: 대청소 (ProtectedTag 없는 모든 블록 삭제)
        if (Input.GetKeyDown(KeyCode.R))
        {
            // 1. 보호받지 못하는 벽돌 파괴
            foreach (var (block, entity) in SystemAPI.Query<RefRO<BlockTag>>().WithEntityAccess())
            {
                if (!SystemAPI.HasComponent<ProtectedTag>(entity))
                {
                    ecb.DestroyEntity(entity);
                }
            }

            // ⭐ 2. 유령 철근 철거: 조인트 양끝 블록이 '둘 다 보호 상태'가 아니라면 조인트도 끊어버림!
            foreach (var (pair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
            {
                bool aProtected = SystemAPI.HasComponent<ProtectedTag>(pair.ValueRO.EntityA);
                bool bProtected = SystemAPI.HasComponent<ProtectedTag>(pair.ValueRO.EntityB);

                // 둘 중 하나라도 보호 태그가 없으면(즉, 삭제되었다면) 이 조인트는 쓸모없는 유령입니다.
                if (!aProtected || !bProtected)
                {
                    ecb.DestroyEntity(entity);
                }
            }
            Debug.Log("🧹 대청소 완료! (허공에 남은 유령 조인트까지 완벽 철거)");
        }
// ⏪ [Ctrl + Z 키]: 가장 최근에 수동으로 타설한 블록 덩어리 물리기 (Undo)
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.Z))
        {
            // ⭐ int 대신 float로 선언하고, 기본값도 -1f로 변경했습니다!
            float maxID = -1f; 
            
            // 1. 씬에 있는 블록들 중에서 가장 번호가 높은(최신) StructureID를 찾습니다.
            foreach (var id in SystemAPI.Query<RefRO<StructureID>>().WithAll<BlockTag>())
            {
                if (id.ValueRO.Value > maxID) maxID = id.ValueRO.Value;
            }

            if (maxID >= 0)
            {
                // 2. 가장 최신 ID를 가진 블록들을 찾아 모조리 파괴합니다.
                foreach (var (id, entity) in SystemAPI.Query<RefRO<StructureID>>().WithAll<BlockTag>().WithEntityAccess())
                {
                    if (id.ValueRO.Value == maxID)
                    {
                        ecb.DestroyEntity(entity);
                    }
                }

                // 3. 지워진 블록에 물려있던 억울한 '철근(조인트)'들도 같이 끊어버립니다.
                foreach (var (pair, entity) in SystemAPI.Query<RefRO<PhysicsConstrainedBodyPair>>().WithAll<JointTag>().WithEntityAccess())
                {
                    bool aHasID = SystemAPI.HasComponent<StructureID>(pair.ValueRO.EntityA) && SystemAPI.GetComponent<StructureID>(pair.ValueRO.EntityA).Value == maxID;
                    bool bHasID = SystemAPI.HasComponent<StructureID>(pair.ValueRO.EntityB) && SystemAPI.GetComponent<StructureID>(pair.ValueRO.EntityB).Value == maxID;

                    // 양쪽 블록 중 하나라도 방금 지워진 최신 블록이었다면 이 조인트도 철거!
                    if (aHasID || bHasID)
                    {
                        ecb.DestroyEntity(entity);
                    }
                }

                Debug.Log($"⏪ [Undo] {maxID}번 작업물의 타설을 취소했습니다!");
            }
            else
            {
                Debug.LogWarning("⏪ [Undo] 더 이상 취소할 작업물이 씬에 없습니다.");
            }
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
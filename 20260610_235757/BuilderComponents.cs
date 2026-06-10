using Unity.Entities;
using Unity.Mathematics;

// 시스템 전체의 상태를 관리하는 싱글톤 데이터
public struct BuilderStateData : IComponentData
{
    public int CurrentMode;         // 0~6번 모드
    public bool IsGuideEditMode;    // 9번 키 토글 상태
    public bool IsDragging;         // 우클릭 드래그 중인지 여부

    // 가이드(설계도) 정보
    public float3 GuideStartPos;
    public float3 GuideEndPos;
    public float GuideRadius;
    public float GuideHeight;

    // 0번 선택 모드용 드래그 영역
    public float2 SelectionStartScreen;
    public float2 SelectionEndScreen;
}

// 블록 식별용 태그
//public struct BlockTag : IComponentData { }

// 6번 모터판 태그
public struct MotorTag : IComponentData
{
    public float Speed;
    public float Torque;
    public float3 Normal;
}

// 0번 모드로 선택된 객체 표시
public struct SelectedTag : IComponentData { }

// H키로 보호된 객체 (R키 청소 면역)
public struct ProtectedTag : IComponentData { }
using Unity.Entities;

// 1. 이 엔티티가 '블록'임을 알려주는 꼬리표
public struct BlockTag : IComponentData { }

// 2. 이 엔티티가 '조인트'임을 알려주는 꼬리표
public struct JointTag : IComponentData { }

// 3. (보너스) 나중에 파괴 시스템 만들 때 쓸 꼬리표
public struct DestroyTag : IComponentData { }
public struct StructureID : IComponentData
{
    public int Value; // 같은 값 = 같은 덩어리(하나의 객체)
}
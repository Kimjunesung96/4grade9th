using Unity.Entities;
using Unity.Collections;

// 기존: 물리 스트레스 수치 저장
public struct BlockStress : IComponentData
{
    public float TargetStress;
    public float SmoothedStress;
}

// ⭐ 신규: 십장님 특명! 재료별 체력과 3대 방어력 추가!
public struct BlockHealth : IComponentData
{
    public float MaxHP;
    public float CurrentHP;
    public float Defense; // 압축/인장/전단 종합 방어력 (이걸 못 넘으면 안 부서짐!)
}

// ⭐ 신규: 엑셀에서 받아온 재질 이름표 (예: "Concrete_Wall")
public struct BlockMaterial : IComponentData
{
    public FixedString32Bytes MaterialName;
}
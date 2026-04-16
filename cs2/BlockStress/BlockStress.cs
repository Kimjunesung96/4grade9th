using Unity.Entities;

// 블록이 받는 물리적 부하(스트레스)를 저장할 컴포넌트
public struct BlockStress : IComponentData
{
    public float TargetStress;   // 물리 엔진이 계산한 이번 프레임의 진짜 부하 (널뛰기 함)
    public float SmoothedStress; // 서서히 변하는 시각 효과용 부하 (부드러움)
}
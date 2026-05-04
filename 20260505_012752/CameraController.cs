using UnityEngine;
using Unity.Mathematics;

public class CameraController : MonoBehaviour
{
    [Header("📷 카메라 회전 (Orbit) 설정")]
    public Transform target; // 🎯 바라볼 중심점 (공사 현장 중앙)
    public float rotationSpeed = 10f; // Q/E 키 누를 때 회전하는 속도
    public float angleStep = 45f; // 한 번에 돌아가는 각도 (정팔각형 = 45도)

    [Header("🔍 카메라 줌 (Zoom) 설정")]
    public float scrollSpeed = 5f; // 휠 스크롤 속도
    public float minDistance = 10f; // 최대한 가까이 갈 수 있는 거리
    public float maxDistance = 100f; // 최대한 멀리 갈 수 있는 거리

    private float currentDistance; // 현재 중심점과의 거리
    private float targetAngle; // 목표 회전 각도
    private float currentAngle; // 현재 회전 각도

    void Start()
    {
        // 1. 시작할 때 타겟(중심점)이 없으면, 카메라 앞쪽 바닥을 임시 타겟으로 잡습니다.
        if (target == null)
        {
            GameObject tempTarget = new GameObject("CameraCenterPoint");
            // 카메라가 바라보는 방향으로 땅바닥(y=0)과 만나는 지점을 찾습니다.
            float distanceToGround = transform.position.y / Mathf.Cos(Vector3.Angle(Vector3.down, transform.forward) * Mathf.Deg2Rad);
            tempTarget.transform.position = transform.position + transform.forward * distanceToGround;
            tempTarget.transform.position = new Vector3(tempTarget.transform.position.x, 0, tempTarget.transform.position.z);
            target = tempTarget.transform;
            Debug.Log("🎯 [카메라] 중심점(Target)이 없어서 임시 중심점을 생성했습니다.");
        }

        // 2. 시작할 때 현재 카메라와 타겟 사이의 거리와 각도를 계산하여 저장합니다.
        Vector3 offset = transform.position - target.position;
        offset.y = 0; // 수평 각도만 계산
        currentAngle = Vector3.SignedAngle(Vector3.forward, offset, Vector3.up);
        targetAngle = currentAngle;
        currentDistance = Vector3.Distance(transform.position, target.position);
    }

    void Update()
    {
        HandleRotation();
        HandleZoom();
        UpdateCameraPosition();
    }

    // 🔄 Q / E 키로 정팔각형(45도씩) 목표 각도 설정
    private void HandleRotation()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            targetAngle += angleStep; // 왼쪽으로 45도
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            targetAngle -= angleStep; // 오른쪽으로 45도
        }

        // 부드럽게 목표 각도로 회전 (스무딩 효과)
        currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * rotationSpeed);
    }

    // 🔍 마우스 휠로 거리감(Zoom) 조절
    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            // 휠을 굴리면 거리가 변하지만, 설정한 최소/최대 거리를 넘지 못하게 막습니다.
            currentDistance -= scroll * scrollSpeed;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }
    }

    // 🎥 계산된 각도와 거리를 바탕으로 카메라의 실제 위치와 방향 업데이트
    private void UpdateCameraPosition()
    {
        // 삼각함수를 써서 타겟 주변을 도는 새로운 X, Z 위치를 계산합니다.
        float rad = currentAngle * Mathf.Deg2Rad;

        // 현재 카메라의 높이(Y)와 내려다보는 각도(Pitch)는 그대로 유지!
        float heightOffset = transform.position.y - target.position.y;

        // 수평 거리(바닥 기준)를 피타고라스 정리로 역계산
        float horizontalDistance = Mathf.Sqrt(currentDistance * currentDistance - heightOffset * heightOffset);
        // 만약 너무 가까이 줌인해서 에러가 나면 보정해줍니다.
        if (float.IsNaN(horizontalDistance)) horizontalDistance = 1f;

        Vector3 newPos = target.position + new Vector3(Mathf.Sin(rad) * horizontalDistance, heightOffset, Mathf.Cos(rad) * horizontalDistance);

        transform.position = newPos;

        // 항상 타겟(중심점)을 정면으로 바라보게 각도를 돌려줍니다.
        transform.LookAt(target);
    }
}
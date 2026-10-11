using UnityEngine;

// 시험 모드 선택기: T 를 누른 뒤 숫자 키로 시험을 고른다.
//   T → 1  무게 스캔        (예전 V)
//   T → 2  지진/진동        (예전 B, 한 번 더 누르면 격발)
//   T → 3  충격파           (예전 N, 한 번 더 누르면 격발)
//   T → 4  중력/사격        (예전 M, 다시 누르면 종료)
//   T → 5  바람             (물리 바람 모드, 다시 하면 종료)
//   T → 6  정적 구조 분석    (사용률 색칠 + 블록별 원인/안전율, 다시 하면 종료)
//
// 동작 규칙
//  - T 를 누르면 "선택 대기" 상태가 되고, 다음 숫자 하나가 시험 선택으로 소비된다. 3초 안에 숫자가 없으면 취소된다.
//  - T 를 다시 누르면 선택 대기를 취소한다. (T 를 누른 채 숫자를 눌러도 같은 결과)
//  - 선택 대기 중이거나 선택이 소비된 프레임에는 NumbersBlocked() 가 true 라서, 건설 모드(1~6)·청사진 격자(1~5)·
//    중력 사격 속도(1~9) 같은 기존 숫자 키 기능이 같이 반응하지 않는다.
//  - 시험이 시작된 뒤에는 대기 상태가 아니므로 숫자 키는 원래 기능(예: M 모드의 속도 단계)으로 돌아간다.
//
// 어느 시스템이 먼저 실행되든 같은 결과가 나오도록, 프레임당 한 번만 입력을 읽고 결과를 공유한다.
public static class TestMode
{
    const float WindowSec = 3f;
    const int TestCount = 6;

    static int lastFrame = -1;
    static bool waiting;
    static float openedAt;
    static int selected;
    static bool blockThisFrame;

    // 선택 대기 중인지 (HUD 표시용)
    public static bool Waiting { get { Poll(); return waiting; } }

    // 이번 프레임에 n번 시험이 선택되었는가 (1~6)
    public static bool Selected(int n) { Poll(); return selected == n; }

    // 이번 프레임에 어떤 시험이든 선택되었는가
    public static bool AnySelected { get { Poll(); return selected != 0; } }

    // 숫자 키를 기존 기능(건설 모드 등)이 읽어도 되는가. true 면 읽지 말 것.
    public static bool NumbersBlocked() { Poll(); return blockThisFrame || CaptureNumbers; }

    // 바람 모드처럼 숫자 키를 직접 쓰는 모드가 켜져 있는 동안 true (건설 모드 숫자 키를 막는다)
    public static bool CaptureNumbers;

    // T 를 눌렀거나 선택 대기 중이거나 이번 프레임에 시험이 선택된 경우 true (이때의 숫자는 시험 선택용)
    public static bool ChordBusy() { Poll(); return blockThisFrame; }

    static void Poll()
    {
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        selected = 0;
        blockThisFrame = false;

        if (waiting && Time.unscaledTime - openedAt > WindowSec)
        {
            waiting = false;
            Debug.Log("[시험 모드] 시간 초과로 취소");
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            waiting = !waiting;
            openedAt = Time.unscaledTime;
            blockThisFrame = true;
            if (waiting) Debug.Log("[시험 모드] 1 무게 스캔 / 2 지진 / 3 충격파 / 4 중력 사격 / 5 바람 / 6 정적 분석 — 숫자를 누르세요");
            else Debug.Log("[시험 모드] 취소");
            return;
        }

        if (!waiting) return;
        blockThisFrame = true;

        for (int n = 1; n <= TestCount; n++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + n) || Input.GetKeyDown(KeyCode.Keypad0 + n))
            {
                selected = n;
                waiting = false;
                break;
            }
        }
    }
}
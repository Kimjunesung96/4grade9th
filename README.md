# 블록 기반 구조물 시뮬레이션 (Unity DOTS)

3m 격자 블록으로 구조물을 짓고, 중력·진동·충격파·바람 등의 시험으로 **위험 영역을 찾아 자동으로 보강 계획과 공사비를 만들어 주는** Unity 시뮬레이션입니다.
웹 CAD/3D 추출 도구(React + Three.js, FastAPI)에서 만든 블록 CSV를 불러와 같은 환경에서 시험할 수 있습니다.

**시연 영상**: https://youtu.be/K8w-Bco6pRg

- 엔진: Unity DOTS (Entities, Unity Physics, Burst), URP, Input System
- 언어: C#
- 단위: 블록 1개 = 3유닛 = 0.15m 정육면체 (1유닛 = 5cm)

## 주요 기능

- **블록 건설**: 벽, 원기둥, 원뿔, 피라미드, 프레임, 솔리드 등 5가지 모양을 격자에 맞춰 타설
- **도면 불러오기**: PNG/JPG 도면 또는 웹 도구에서 내보낸 CSV를 블록 구조물로 변환
- **구조 시험 6종**: 무게 스캔, 지진/진동, 충격파, 중력/사격, 바람, 정적 구조 분석
- **자동 보강 설계**: 시험 결과에서 위험 블록을 찾아 보강 계획(`Reinforcement_Plan.csv`)을 만들고 공사비를 계산
- **누적 보강**: 보강을 여러 차수(Phase)로 쌓아 가며 재시험하고, 차수별 비용을 따로 표시
- **재질별 물성**: 콘크리트, 철, 유리, 목재류, H빔 등 재질표 기반 (`Material_Specs.csv`)
- **인게임 설정창**: F5에서 스프링 강성, 감쇠, 지진, 바람, 파단 기준 등을 실시간 조절하고 값을 클립보드로 복사

## 전체 흐름

```
웹 CAD/3D 추출 도구 ──CSV──▶ 도면 UI(O) ──▶ 블록 건설(G)
                                              │
                          T → 1~6 시험 ◀──────┘
                                │
                  위험 블록 분석 → 보강 계획(Y) → 보강 타설 → 재시험
                                │
                     공사비 영수증 / CSV 기록
```

## 조작법

### 공통

| 키 | 기능 |
|---|---|
| `T` → `1`~`6` | 시험 모드 선택 (3초 안에 숫자를 누르지 않으면 취소) |
| `1`~`5` | 건설 모양 선택 |
| 휠 | 높이(층수) 조절 |
| `Enter` | 가이드 확정 |
| `G` | 타설(건설 확정) |
| `R` | 블록 청소 |
| `H` / `Del` | 블록 보호 토글 / 블록 즉시 삭제 |
| `Q` / `E` | 카메라·설계도 회전 |
| `Space` | 젯팩 |
| `Y` | 예산/보강 UI (한 번 더 누르면 보강 설정창) |
| `O` | 블루프린트(도면) UI |
| `F1` / `F2` / `F3` / `F5` | 도움말 / 키보드 오버레이 / 콘솔 / 설정창 |
| `Esc` | 일시정지 메뉴 |

### 시험 모드 (`T` → 숫자)

| 선택 | 시험 | 조작 |
|---|---|---|
| `T→1` | 무게 스캔 (자중 응력) | 자동으로 5초 판정 |
| `T→2` | 지진/진동 | 휠로 진도 조절(1~8), `B`로 격발 |
| `T→3` | 충격파 | `N`으로 폭발 발사 |
| `T→4` | 중력/사격 | `1`~`9` 발사 속도, 좌클릭 연사, 우·휠클릭 점사, `T→4`로 종료 |
| `T→5` | 바람 | `1`~`9` 풍속(10m/s 단위), 좌/우클릭으로 바람 방향 30° 회전 |
| `T→6` | 정적 구조 분석 | 사용률을 색으로 표시, `J` 이전 저장, `K` 하중 종류 전환, `X` 이전 지우기 |

정적 분석(`T→6`)은 물리를 돌리지 않고 구조물 전체를 한 번에 풀어 블록을 초록(0%) → 노랑(50%) → 빨강(100%) → 어두운 빨강(초과)으로 칠합니다. 마우스를 블록에 올리면 압축·인장·전단·휨·좌굴 사용률과 지배 원인, 안전율이 나오고, 보강 전후를 비교할 수 있습니다.

## 파단(부서짐) 판정 방식

조인트마다 매 틱 상태를 보고 판정하는 방식입니다. 블록에 체력을 깎는 방식이 아닙니다.

1. **조인트 판정** (`JointStressCalc.ShouldBreak`): 시험 시스템들이 공통으로 호출합니다.
   - 일반 모드: `늘어난 길이 × 응력 배율 > 재질 Tensile` 이고 최소 늘어남(0.05유닛)을 넘으면 끊김
   - MPa 모드(현실 재료값): 재질표의 E, 인장·압축강도(MPa)를 사용. 재질 종류별 파단 늘어남 상한을 넘으면 끊김
     (기본값: 취성 0.10, 연성 0.30, 유리 0.02유닛. 실험으로 맞춘 임시값이며 F5 슬라이더로 조절)
2. **정적 하중 솔버** (`StructuralLoadSolver`, MPa 모드): 위층부터 자중·지진·풍하중을 누적해 단면의 축력, 전단, 휨, 좌굴을 계산하고 사용률이 100%를 넘으면 그 블록의 물리를 풀어 떨어뜨립니다.

## 재질

`Material_Specs.csv`(없으면 코드의 기본값으로 생성)에서 읽습니다.
`Default`, `Concrete`, `Reinforced_Concrete`, `Steel`, `Glass`, `H_Beam`, 목재류(`Wood_Default`, `Wood_Oak`, `Wood_Pine`, `Wood_Cedar`, `Wood_Walnut`, `Wood_Teak`, `Wood_CLT`, `Wood_Bamboo`, `Wood_Plywood`, `Wood_MDF`, `Wood_Rubber`)

열: 밀도, 취성 여부, Tensile / Compressive / Shear / Bending / Torsion(일반 모드용), 색, kg당 단가, E(GPa), 인장·압축강도(MPa, MPa 모드용)

조인트 스프링 강성은 `Bending / 10`(Hz, 설정의 최소~최대 범위로 제한), 감쇠는 취성/비취성 설정값을 씁니다.
(`BaseHP` 열은 현재 판정에 쓰이지 않습니다.)

## F5 설정창

| 구분 | 항목 |
|---|---|
| 물리 | 솔버 반복, 스프링 강성(최소/최대), 감쇠(취성/일반), 응력 배율 단계 |
| 시험 시간 | 중력 스캔, 진동, 충격파 |
| 하중 | 현실 재료값(MPa) 모드, 지진 가속도·방향·주파수, 풍속·방향, 전단/좌굴 포함 |
| 파단 | 취성/연성/유리 파단 늘어남 상한 |
| 기타 | 카메라 감도·피치, 보강 간격 |

**설정값 복사** 버튼을 누르면 모든 설정과 마지막 판정 결과가 텍스트로 클립보드에 복사됩니다. (`Ctrl+V`로 붙여넣기)
`SimulationSettings` 에셋 값은 에디터에서 바꿔야 저장되고, 빌드 실행 중에 바꾼 값과 코드의 static 값은 종료 시 초기화됩니다.

## 데이터 파일

실행 파일(빌드) 또는 프로젝트 루트(에디터) 옆에 생성됩니다. (`Assets` 안에 쓰면 Unity 임포트와 충돌하므로 바깥에 둡니다.)

```
StressBlock/
  Last_Building.csv        마지막으로 건설한 건물
  CurrentStress.csv        최근 시험의 블록별 응력
  Reinforcement_Plan.csv   보강 계획 (BlockID, PosX/Y/Z, Stress, RiskLevel, Prescription, Material, ..., Phase)
  after_reinforce.csv      보강 후 건물
  Budget_Meta.csv          공사비 기록
  Material_Specs.csv       재질표
  vibration/ shockwave/    시험별 기록
BuildingLogs/
  Import/                  도면 이미지(PNG/JPG) 대기소
  ProcessedBlueprints/     변환된 도면
  SavedBlueprints/         저장된 도면 CSV
  temp/                    임시 건물 JSON
```

웹 도구의 CSV(`PosX/PosY/PosZ/Material/Color`)는 다운로드 폴더 동기화 버튼으로 가져옵니다.

## 스크립트 구성

| 영역 | 파일 |
|---|---|
| 건설 | `SpawnerSystem*.cs`(모양별), `BuilderInputSystem.cs`, `BuilderActionSystem.cs`, `GridUtility.cs`, `BlueprintTargetGenerator.cs`, `BlueprintManager.cs`, `AI_Builder.cs` |
| 시험 | `Testmode.cs`, `StressVisualizationSystem.cs`(무게·중력), `VibrationTestSystem.cs`, `ShockwaveTestSystem.cs`, `GravityShootMode.cs`, `Windtestsystem.cs`, `Staticanalysissystem.cs` |
| 구조 계산 | `StructuralLoadSolver.cs`, `Jointstresscalc.cs`, `BlockStress.cs`, `MaterialDataManager.cs` |
| 보강/예산 | `ReinforcementManager.cs`, `BudgetUIManager.cs` |
| 설정 | `Simulationsettings .cs`, `Simulationsettingsprovider .cs`, `Simulationsettingsuimanager .cs` |
| UI | `Modehudui.cs`, `Keyboardoverlayui.cs`, `EscPauseMenu.cs`, `Mainmenuui.cs`, `InGameConsole.cs`, `UIStyleKit.cs`, `F1HelpManual.cs` |
| 기타 | `CameraController.cs`, `CameraFollowSystem.cs`, `PlayerMovementSystem.cs`, `LogManager.cs`, `StressBlockPaths.cs`, `FixedStepLimiterBootstrap.cs`, `LagDetectiveSystem.cs` |

## 실행 방법

빌드가 끝난 실행 파일이 이 저장소의 **`build` 브랜치**에 올라가 있습니다. Unity 설치 없이 받아서 바로 실행할 수 있습니다. (Windows)

1. 저장소의 [`build` 브랜치](https://github.com/Kimjunesung96/4grade9th/tree/build)로 이동합니다. (브랜치 드롭다운에서 `build` 선택)
2. **Code → Download ZIP**으로 받거나 직접 링크를 사용합니다: https://github.com/Kimjunesung96/4grade9th/archive/refs/heads/build.zip
3. 압축을 풀고 `prototypejolup.exe`를 실행합니다.
4. Windows SmartScreen 경고가 뜨면 **추가 정보 → 실행**을 누릅니다.

git으로 받을 경우:

```
git clone -b build --single-branch --depth 1 https://github.com/Kimjunesung96/4grade9th.git
```

실행하면 `prototypejolup.exe` 옆에 `StressBlock/`, `BuildingLogs/` 폴더가 만들어져 건물, 시험 결과, 도면이 저장됩니다. exe와 `prototypejolup_Data`, `MonoBleedingEdge`, DLL들은 같은 폴더에 함께 있어야 하니 exe만 따로 옮기지 마세요.

## 알려진 한계와 튜닝 포인트

- 파단 늘어남 상한(취성/연성/유리)은 재료 강도에서 계산한 값이 아니라 실험으로 맞춘 임시값입니다.
- 발사체의 속도·질량에서 충격력을 계산해 재료 강도와 비교하는 방식은 아직 없고, 사격은 조준선에 직접 맞은 블록만 강제로 끊습니다.
- 조인트가 스프링이라 단단한 재질도 상한 전까지는 늘어나며 출렁일 수 있습니다. F5의 스프링 강성·감쇠·솔버 반복을 올려 완화합니다.
- 블록이 2만 개 이상이면 솔버 반복을 높이는 만큼 프레임이 떨어질 수 있습니다.

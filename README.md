# 🏗️ Virtual Construct (버츄얼 컨스트럭트)

> **"스케치하는 순간, 건축 구조의 취약점이 바로 보인다."**
> React Web 에디터 + Supabase 실시간 클라우드 DB + Unity 6 DOTS 고성능 물리 엔진이 융합된 실시간 3D 건축 구조 시뮬레이션 및 하중 취약점 시각화 플랫폼

---

## 📺 프로젝트 시연 동영상
> **교수님 채점용 시연 영상입니다. 아래 이미지 또는 링크를 클릭하면 시연 페이지(YouTube)로 이동합니다.**

[![Virtual Construct 시연 영상](https://img.youtube.com/vi/aH3odFUrHu4/0.jpg)](https://youtu.be/aH3odFUrHu4)

🔗 **[유튜브 링크: Virtual Construct 핵심 기능 시연 보러가기](https://youtu.be/aH3odFUrHu4)**

---

## 📊 프로젝트 보고서 및 발표 자료 (PPT)
> **교수님 채점용 발표 자료 리스트입니다. 링크를 클릭하면 해당 발표 파일로 이동합니다.**

* 📂 **[프로젝트 제안서 및 중간발표 PPT 자료 바로가기](https://github.com/Kimjunesung96/4grade9th/blob/main/20263600김준성.pptx)**
* 📂 **[프로젝트 최종 보고서 및 기말발표 PPT 자료 바로가기](https://github.com/Kimjunesung96/4grade9th/blob/main/버츄얼건축.pptx)**

---

## 💡 개발 동기 및 배경
기존 건축 구조 설계 생태계는 극단적인 양극화 문제를 겪고 있습니다.
1. **전문 CAD 툴 (AutoCAD, Revit 등):** 구조 분석이 정밀하지만, 무겁고 진입 장벽이 너무 높아 일반인의 접근이 불가능합니다.
2. **샌드박스 게임 (마인크래프트 등):** 접근성은 뛰어나나 공학적 데이터가 전무하여 실제 버틸 수 있는 구조인지 검증할 수 없습니다.

**Virtual Construct**는 이 둘 사이의 공백을 메우기 위해 탄생했습니다. 일반 사용자도 브라우저에서 스케치하듯 가볍게 도면을 그리면, 그 즉시 3D 건물로 변환됨과 동시에 고성능 물리 엔진이 하중 흐름을 시뮬레이션하여 **"짓기 전에 무너질 취약점"**을 실시간으로 시각화하는 웹-엔진 통합 플랫폼을 지향합니다.

---

## 🛠️ 핵심 기술 아키텍처 (Core Architecture)
본 시스템은 세 개의 레이어가 단일 데이터 파이프라인으로 묶여 실시간 가동됩니다.

1. **WEB FRONTEND (React 2D Drawing Grid):**
   * 별도의 설치 없이 웹 브라우저에서 즉시 구동되는 격자 기반 드로잉 에디터입니다.
   * 마우스 클릭 및 드래그 동작만으로 바닥을 채우고, 벽을 올리고, 방을 나누는 평면도 스케치가 가능합니다.
2. **CLOUD DATABASE (Supabase Real-time Cloud Link):**
   * 웹에서 설계된 평면 도면 데이터를 **12열 표준 CSV 포맷 구조물 장부**로 변환하여 클라우드에 실시간 동기화합니다.
   * 도면의 미세한 수정 사항이 발생하는 즉시 감지하여 실시간 파이프라인으로 유니티 엔진에 토스합니다.
3. **ENGINE & PHYSICS (Unity 6 DOTS & Burst Compiler):**
   * 수신된 12열 구조 데이터를 해독하여 실시간으로 3D 건축물을 자동 조립합니다.
   * **Entity Component System (ECS)** 구조와 **Burst Compiler**를 극한으로 활용하여 수만 개의 구조 블록에 가해지는 역학적 하중(Stress)과 컴프레션 장력을 실시간 연산하고 시각화합니다.

---

## ✨ 핵심 기능 (Key Features)

### 1. 웹 실시간 도면 스케치 및 자동 3D 변환
* 명령어 없는 직관적 그리드 에디터를 통해 평면도를 작성합니다.
* 도면 업로드 시 유니티 엔진이 축을 실시간으로 세우고 자동 매핑하여 별도의 3D 모델링 공정 없이 실시간 완공을 처리합니다.
* 클릭 몇 번으로 경사도 조정이 가능한 가변 지붕 자동 생성 및 기둥/벽체 추가 조립(레고 공법) 시스템을 지원합니다.

### 2. 고성능 구조 하중 시뮬레이션 및 취약점 시각화 (`StressVisualizationSystem`)
* **Burst Compile** 기반의 고성능 Physics Job 시스템을 통해 각 블록에 가해지는 하중 분산과 역학적 리스크 레벨을 실시간 분석합니다.
* 하중 테스트 가동 시, 안전 구역은 물론 붕괴 위험도가 높은 취약 구역을 블록 렌더러 색상 변환(회색, 빨강, 주황, 노랑, 검은색)을 통해 실시간으로 시각적 경고를 제공합니다.

### 3. 지능형 자재 스펙 및 실시간 공사 단가 산정 (`MaterialDataManager` & `BudgetUIManager`)
* 각 건축 블록마다 고유의 자재 스펙(밀도, 베이스 HP, 부서짐 임계치 등)을 데이터 구조화하여 관리합니다.
* 목재(Wood), 콘크리트(Concrete), 철골(Steel) 등 선택한 자재 및 레이어 층수(공법 모드)에 따라 **소요되는 자재 단가 및 총공사 예산을 UI를 통해 실시간으로 산정**하여 경제적 설계 검토를 지원합니다.
* 오버워치 스타일의 홀드형 도움말 메뉴(`F1HelpManual`)를 탑재하여 유저 편의성을 극대화했습니다.

---

## 💻 기술 스택 (Tech Stack)

| 레이어 | 사용 기술 |
| :--- | :--- |
| **Frontend** | React, HTML5 Canvas, CSS3 |
| **Backend & Cloud** | Supabase, Real-time Database, REST API |
| **Engine & Physics** | Unity 6, Unity DOTS (Entities, Unity Physics, Unity Mathematics), Burst Compiler |
| **Language & Tool** | C#, JavaScript, Python (Data Processing), Git/GitHub |

---

## 📅 개발 마일스톤 (Milestones)

* **Phase 1: 2D 웹 에디터 및 격자 UI 설계** ── `100% 완료`
* **Phase 2: Supabase 실시간 클라우드 데이터 파이프라인 연동** ── `100% 완료`
* **Phase 3: Unity 6 DOTS 엔진 자동 빌드 및 시뮬레이션 연동** ── `100% 완료`
* **Phase 4: 실시간 스트레스 시각화 및 자재·예산 UI 연동** ── `100% 완료`
* **Phase 5 (Future Vision):** AI 생성형 설계 모듈 (건축물 사진 분석을 통한 자동 구조 도면 생성 및 하중 테스트 연계 아키텍처 설계 완료)

---

## 👤 개발자 정보 (Developer)

* **이름:** 김준성 (Kim Jun-seong)
* **소속:** 동양미래대학교 컴퓨터소프트웨어공학과 (Dongmidae Computer Software Engineering)
* **담당 파트:**
  * **Frontend & Cloud DB Layer:** React 2D 에디터 구현, Supabase 실시간 데이터 동기화 파이프라인 구축 및 12열 표준 포맷 설계
  * **Simulation & Physics Layer:** Unity 6 DOTS 기반의 고성능 실시간 3D 건축 자동 스포너 및 물리 하중 취약점 시각화 시스템 개발

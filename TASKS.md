# Pop — 진행 상황 (새 세션은 여기부터 읽는다)

3D 뚫어뻥 클라이밍 게임. 탐험가 다람쥐가 뚫어뻥을 붙이고 흔들고 떼며 산 정상의 도토리 나무까지 오른다. 난이도 아주 어려움.

- 기획서(Notion): https://app.notion.com/p/3D-41674e10a7648088ae2bcfe8a9f88471
- Unity 6000.3.25f1, URP, Input System. Unity 프로젝트 폴더는 `Pop/`
- 프로젝트 메모(결정 · 규칙 요약): `Pop/ClaudeHand/NOTES.md`

## 최신 (2026-11-23) 다람쥐 Mixamo 리깅 · 대기 / 걷기 · 달리기 / 점프 애니메이션
- **모델**: `Pop/Assets/_Game/Art/Characters/Squirrel/Mixamo/Squirrel_Explorer_Mixamo.fbx` (4.2만 삼각형, Mixamo 뼈대 25개 · 손가락 없음, Humanoid). VARCO 리그 FBX(45 MB, 24.6만 삼각형)는 참조가 없어져 지웠다. 재질 · 텍스처는 그대로 `M_Squirrel_Explorer_Rig.mat`
  - 만든 순서: 예전 FBX에서 뼈대를 떼고 Blender로 17%까지 줄임 → Mixamo 자동 리깅(OBJ, 100배 크기로 올려야 함) → Blender에서 스킨 고침 → FBX
  - Mixamo 리깅 문제와 고친 것: 꼬리 위쪽이 왼팔에 붙어 휘날림 → 꼬리 전체를 Hips에 고정(꼬리뼈 없음, 흔들림 없음). 볼 · 턱이 몸통/어깨에 붙음 → Head로. 팔 가중치가 몸통에 번짐 → Spine2로
- **클립** (Mixamo, FBX for Unity, 30 fps, 같은 폴더): Happy Idle → `Squirrel_Idle` / Happy Walk → `Squirrel_Walk` / Run Forward(Happy Run Forward) → `Squirrel_Run` / Jump(Jump In Place, 79프레임 중 공중 구간 24~42만) → `Squirrel_JumpAir` / Falling Idle → `Squirrel_Fall`
- **Animator** `Squirrel.controller`: Locomotion(속도 블렌드: Idle 0 / Walk 0.9 m/s / Run 6 m/s 3배속) · Air(점프 공중 자세, 위쪽 속도로 진행 → 체공 1.02초에 자동으로 맞음, 약 0.59배속) · Fall(위쪽 속도 -12 m/s 아래로 오래 떨어질 때). Root Motion 안 씀
- **코드**: `Scripts/Player/PlayerAnimationVisual.cs`(Player에 붙음, PlayerController 상태 · 속도만 읽음. 이동 · 튜닝 · 뚫어뻥 코드는 안 바꿈), `Editor/SquirrelAnimationSetup.cs`(메뉴 Pop/Dev/Squirrel/1 Setup Mixamo Assets · 2 Replace Player Visual), `Scripts/Dev/AnimCheck.cs`(Play 중 자동 확인: `new GameObject("AnimCheck").AddComponent<Pop.Dev.AnimCheck>().Run(폴더)`)
- **씬**: Mountain · Prototype 모두 Player/Visual/Squirrel만 교체(키 1.2 m, 발 = 캡슐 바닥, 엉덩이 = 캡슐 축). Player 밖은 그대로
- **Play 확인(Mountain, AnimCheck)**: 대기 · 이동 · 방향 전환 · 점프(체공 1.03초, 높이 1.96 m) · 기절 색 0.6 · 긴 낙하 → Fall → 착지. T자 자세 없음. 확인 그림 `Pop/Screenshots/animcheck_*.png`
- **남은 것 / 아쉬운 점**
  - 발 미끄러짐: 이동 6 m/s가 다리 길이에 비해 빨라서 달리기 3배속으로도 약 4.7 m/s만 덮는다(약 20% 미끄러짐, Blender에서 잰 값). AnimCheck 로그의 foot slide 숫자는 믿을 수 없음(디딤 판정이 틀림)
  - 점프 정점 자세가 몸을 앞으로 숙인 다이빙처럼 보임. Fall 자세는 머리부터 떨어지는 모양. 왼쪽을 볼 때 꼬리 밑동(가는 줄기)이 길게 보임(모델 모양)
  - 줄에 매달렸을 때 · 기절 전용 동작 없음(지금은 공중 자세), 꼬리 흔들림(꼬리뼈) 없음, 착지 동작 없음
  - 사용자 눈으로 본 확인은 아직(자동 확인 그림만 봄)

## 최신 (2026-11-23) 시작 화면 · 골인 · 클리어 시간
- **Build Settings**: 0 `Title` · 1 `Mountain` · 2 `Prototype` · 3 `SampleScene`. 게임은 `Pop/Assets/_Game/Scenes/Title.unity`에서 시작한다
- **시작 화면** (`Title.unity`, uGUI + 레거시 Text, 한글은 실행 시 OS 글꼴 맑은 고딕을 입힘 `KoreanUIFont`): 제목 Pop, 부제 "다람쥐, 뚫어뻥 하나로 도토리 나무까지", 최고 기록(없으면 "—:—"), [시작] → Mountain, [종료] → Application.Quit(에디터에서는 Play 멈춤), 조작 안내. EventSystem은 InputSystemUIInputModule. 코드 `Scripts/Game/TitleScreen.cs`
- **타이머**: Mountain이 시작될 때(씬 Start) 돌기 시작, 골인할 때까지 멈추지 않음(떨어져도 리셋·일시정지 없음). `Time.deltaTime` 누적이라 Time.timeScale 0(디버그 순간이동 얼리기)일 때만 멈춘다. 왼쪽 위 HUD `mm:ss.ff` + 최고 기록. 코드 `Scripts/Game/RunManager.cs`(씬의 `GameUI` 오브젝트)
- **골인**: `AcornTree_Goal/GoalTrigger`(BoxCollider 트리거 + `GoalTrigger.cs`). 월드 범위 x 54.99~68.39, y 213.25~216.75, z ±2 = 정상 발판(Summit_Plateau, 윗면 213.25, x 50.89~68.39) 윗면 위 3.5 m 층, 나무(x 62) 왼쪽 7 m부터 발판 오른쪽 끝까지. 발판은 10 m 두께라 아래에서 지나갈 수 없고, 사용자가 놓은 T7_Ledge_23 (5)(6)(7)(x 20~48, 꼭대기 213.4/220.3/224.9)에서는 상자에 닿으려면 정상 발판 위 3.5 m 안까지 내려와야 한다(= 정상에 도착). Play에서 (5)(6)(7) 꼭대기 순간이동 → 골인 안 됨 확인
- 골인하면: 타이머 정지 → `PlayerController.SetControlLocked(true)`(새로 더한 작은 공개 메서드: A/D·Space·발사 입력 무시, 매달려 있으면 놓음. 이동·튜닝은 그대로) → 클리어 화면("클리어!", 이번 기록, 최고 기록, 새 기록이면 "새 기록!") · [다시 하기](Mountain 다시 불러오기) · [타이틀로]. 잠금 중 PlungerController는 발사·조준 커서를 끄고 시스템 커서를 보인다(버튼 클릭이 뚫어뻥을 쏘지 않음)
- **최고 기록 저장**: PlayerPrefs 키 `Pop.BestClearTime`(float 초, 짧을수록 좋음). 코드 `Scripts/Game/ClearRecord.cs`(`ClearRecord.ClearBest()`로 지움)
- **만드는 메뉴**: Pop/Dev/Build Title Scene(Title.unity 새로 만들고 Build Settings 순서 맞춤), Pop/Dev/Add Mountain Game UI(열린 Mountain에 GameUI · EventSystem · GoalTrigger를 더함, 이미 있으면 건너뜀, 레벨은 안 건드림). 코드 `Editor/GameUIBuilder.cs`. `MountainBuilder.Summit()`도 이제 GoalTrigger를 만든다(Rebuild 메뉴는 이번에 실행하지 않음 — 손 편집 보존)
- **시험 방법**: Title.unity에서 Play → [시작] → HUD 타이머 확인 → 메뉴 Pop/Debug/Teleport의 "시작 · 정상"에서 Summit_Plateau 또는 AcornTree_Goal (옆) → 클리어 화면. 정상 바로 아래(T7_Ledge_23 (7) 등)로 순간이동하면 골인되지 않아야 한다. 기록 지우기: Play 중이 아니어도 `PlayerPrefs.DeleteKey("Pop.BestClearTime")`
- Esc(일시정지/타이틀)는 아직 없음

## 최신 (2026-11-13)
기획서 🔴 핵심 항목이 모두 들어갔다. 시험 씬은 `Pop/Assets/_Game/Scenes/Prototype.unity`(2026-11-23부터 Build Settings 2번).
다음: 사용자 플레이 확인 → 🟡 체크포인트 · 골인 판정 · 기본 UI(높이 표시).

## 최신 (2026-11-20)
- **작업 환경을 옮기려고 지금 상태 그대로 `main`에 커밋 · 푸시함.** 플레이 확인 전. 다음 순서: 사용자 플레이(Mountain) → 맵 구간 다양화(굴뚝 · 천장 · 큰 진자 · 공중 재부착 · 미끄럼 경사 등, 지금은 층마다 "앵커 몇 개 → 쉬는 발판" 반복이라 단조로움) → 골인 판정 → 다람쥐 동작(대기 · 걷기 · 점프 · 매달리기 · 기절)
- Mountain.unity: 사용자 손 편집 포함해 저장 · 커밋(2026-11-20) — Crag_T1_A00 옮김(11.2, 9.1) · 복제 2개(3.7, 9.2 / -4.0, 6.0), T2_Ledge_06 복제 3개(씬 루트, 9.1, 11.4 / 1.6, 10.2 / 16.5, 12.5). 경로 데이터(MountainRoute)와 어긋나 봇이 첫 앵커에 못 붙음 — 봇 검증하려면 경로 데이터를 맞추거나 봇을 다른 단계부터 돌린다. Rebuild Mountain Level 메뉴를 실행하면 이 손 편집이 사라진다
- 사용자 결정: 사거리 15 → 7.5m, 흔들기 힘 20 → 4
- **본 맵 `Pop/Assets/_Game/Scenes/Mountain.unity`** (Build Settings 1번, 난이도 최상): 좌우 벽으로 둘러싼 탑(폭 89m, 정상 발판 213m). 경로는 9층 지그재그(층마다 방향 바꿈, 앵커 28개), 4구간: 기슭(1~2층) · 중턱 사슬(3~5층) · 고지 작은 패치/기절 바위(6~8층) · 정상 질주(9층) → 도토리 나무
- **떨어지는 방식 (사용자 결정 2026-11-20: 리스폰 아님)**: 실패하면 순간이동 없이 몸이 그대로 아래로 떨어진다. 앵커 바위는 지붕이 55°라 설 수 없어 튕겨 미끄러지고, 설 수 있는 곳은 경로의 발판(층 끝 돌아서는 계단, 3·4·7·8층 가운데 쉬는 발판)과 맨 아래 시작 분지(5° 바닥, 걸어서 시작점으로)뿐이다. 층 가장자리에서 미끄러지면 몇 m, 가운데에서 크게 떨어지면 수십~백여 m 아래 발판이나 시작 분지까지 떨어진다. KillZone은 벽 바깥·아주 아래 안전망(SafetyNet_*)만 남겼고 경로 위에는 없다
- 맵 고치기: `Editor/MountainBuilder.cs`의 Design() 수정 → 메뉴 Pop/Dev/Rebuild Mountain Level → Play에서 `AddComponent<Pop.Dev.RouteBot>().Run(0,-1,false)`로 등반 재검증(봇 완주 확인), 낙하 시험은 `Pop.Dev.DropTester`
- 남은 것: ~~골인 판정 없음~~(2026-11-23 GoalTrigger 추가), 사람 손 타이밍 여유 미확인(앵커를 붙일 수 있는 시간이 고지 약 0.4초), 기절 바위 실제 접촉 미시험, 가운데 실수는 손실이 큼(쉬는 발판이 바로 아래에 있을 때만 적게 잃음)
- 플레이어 = VARCO 탐험가 다람쥐(2026-11-23부터 Mixamo 리그 + 애니메이션, 맨 위 항목), 정상 = VARCO 도토리 나무
- **디버그 순간이동 창** (사용자 "에디터 상에서"): 메뉴 **Pop/Debug/Teleport** (에디터 창, Play 중에만). 목록 버튼(시작·정상 / 구간 / 발판 / 앵커 / 경로 데이터) · ±10 m · X·Y 이동 · Scene 뷰 클릭 순간이동 · 순간이동 뒤 얼리기([계속]으로 풂). 게임 키는 안 씀(입력 규칙 유지). 코드: `Editor/DebugTeleportWindow.cs`, `PlayerController.DebugTeleport(Vector3)`(줄 해제 · 기절 해제 · 속도 0). Play 시험은 아직

## 한 일
- [x] **이동 · 점프 · 카메라** (2026-11-12) — 사용자 "딱 좋다". 이 수치와 공중 관성 규칙은 함부로 바꾸지 않는다
  - Rigidbody 캡슐(키 1.2m), Z 위치 · 회전 고정, A/D 이동 · Space 점프(점프 버퍼 0.12초 · 코요테 0.08초), 중력 ×1.6, 마찰 0 재질
  - 공중에서 A/D를 안 누르면 감속하지 않고, 목표 속도보다 빠를 때 깎지 않음(관성 유지)
  - 카메라: 측면 고정 추적, 회전 없음, W/S 누르는 동안 시점만 위/아래 최대 6m, 떼면 복귀
- [x] **뚫어뻥 발사 · 부착 · 흔들기 · 해제** (2026-11-12)
  - 캐릭터 → 마우스 커서 방향 Raycast, 사거리 7.5m(2026-11-20 사용자, 15 → 절반), 실패 시 쿨타임 0.3초. 부착 가능 = `PlungerAttachable` 컴포넌트(밝은 연두베이지 `M_Attachable`), 불가 = 회색 `M_Level`
  - 줄 = 늘어나지 않는 로프(거리 ≤ 길이, 속도 기반 제약), 길이 = 발사 순간 거리. 바닥에서 부착하면 0.4m 당겨 떠오름(0.2초 동안 부드럽게)
  - A/D = 줄에 수직 방향 힘(swingForce 4 — 2026-11-20 사용자 "너무 세다", 20 → 1/5). 클릭 = 해제(속도 유지), Space = 해제 + 점프 힘
  - 조준 커서(OnGUI): 초록 = 부착 가능, 빨강 = 불가, 회색 = 사거리 밖
- [x] **접촉 해제 · 기절 · 낙사** (2026-11-13)
  - 부착 중 다른 면 접촉: 바닥(법선 기준) → 해제만, 벽 · 천장 → 해제 + 기절(Stunned)
  - **공중 충돌 기절 (2026-11-20 사용자 "줄에 매달려 있을 때 말고도")**: 점프 · 해제 후 비행 · 낙하 · 발사 실패 중 공중에서 벽 · 천장(같은 법선 분류: 45°보다 가파른 면)에 부딪히면 기절해서 떨어진다. 닿기 직전 면 안쪽 속도가 `PlayerTuning.airStunMinImpactSpeed`(1 m/s)를 넘을 때만(스치기 · 벽에 붙어 A/D 누르기는 무시). 바닥에 서서 벽으로 걸어 들어가기, 바닥에서 점프한 그 스텝, 바닥과 벽에 같이 닿은 스텝(착지 · 모서리)은 기절 아님. 바닥으로 치는 경사 착지는 기절 아님. 앵커 바위 지붕(55°)은 벽으로 분류되므로 떨어지며 세게 부딪히면 기절
  - 결정 (2026-11-13, 사용자): **부착한 그 벽이어도 닿으면 무조건 기절** — "기획서가 잘못된 것". 기획서 4장 표 첫 줄과 다름
  - 바닥에서 부착할 때만 0.2초 유예 + 바닥에서 떨어지기 전 바닥 접촉 무시. 공중 부착은 유예 없음
  - 기절(부착 중 접촉 · 공중 충돌 공통): 조작 전부 불가, 바닥에 착지해야만 풀림. 머리 위 노란 별 + 어둡게, 풀릴 때 번쩍. 벽 틈 끼임 방지 밀어내기(실제 재현은 못 함)
  - 낙사: `KillZone` 트리거 또는 `killY`(-10) → 즉시 `RespawnPoint`로 리스폰. 체크포인트는 `PlayerController.SetRespawnPoint(Vector3)`를 부르면 됨
- [x] **발사 연출** (2026-11-13, 사용자 "붙지 않더라도 날아가는 연출")
  - 실패: 머리가 줄을 달고 날아가 부착 불가 면에서 튕겨 떨어지거나, 사거리 끝에서 멈칫 후 되감김
  - 성공: 부착은 즉시, 보이는 머리만 2배 속도로 날아가 꽂힘. 튜닝은 PlungerController 인스펙터(shotSpeed 45 등)

## 확인 못 한 것 (사용자 플레이로 확인)
- 실제 마우스 · 키 입력 전반(코드로 부착 · 해제 · 접촉 · 낙사는 Play에서 확인함)
- A/D 흔들기 감각, 머리 위 앵커 4개를 진자로 올라갈 수 있는지(기획서 "위로 올라가는 원리" 검증)
- 기절 표시 · 발사 연출의 느낌, 벽 바로 옆에서 그 벽에 쏘면 유예 직후 곧바로 기절하는 게 너무 가혹한지

## 다음 (기획서 15장)
- [ ] 🟡 체크포인트(`SetRespawnPoint` 사용) · ~~골인 판정(도토리 나무)~~ 2026-11-23 완료 · 기본 UI(높이 표시, 조준 커서 정리; 시작 화면 · 타이머 HUD · 클리어 화면은 2026-11-23 완료) · 튜닝 값 정리
- [ ] 🟢 ~~애니메이션~~(2026-11-23 대기 · 이동 · 점프 · 낙하 완료, 매달리기 · 기절 · 착지는 남음) · 사운드(휙/착!/뽁!/퍽!) · 충돌/기절 이펙트 · 카메라 연출 · 타임어택
- 이번 주 제외: 전투, 적 AI, 인벤토리, 스킬, NPC, 스토리, 온라인, 줄 길이 조절

## 코드 구조
- `Pop/Assets/_Game/` 아래에 게임 코드 · 에셋 전부
- 입력은 `Pop.Input.GameInput` 한 곳에서만 읽는다
- 튜닝 값: `Tuning/PlayerTuning.asset`, `Tuning/CameraTuning.asset`(ScriptableObject)
- `Scripts/Player/PlayerController.cs`: 이동 · 점프 · 상태(Normal/Attached/Flying/Stunned/Falling/Dead) · 줄 물리 · 접촉 판정 · 낙사
- `Scripts/Plunger/PlungerController.cs`: 조준 · 발사 · 머리/줄 표시 · 발사 연출 · 커서
- `Scripts/Player/PlayerStunVisual.cs`, `Scripts/Player/KillZone.cs`, `Scripts/Camera/CameraFollow.cs`

## 다른 PC에서 이어 할 때
- 커밋하지 않은 로컬 파일: `Pop/Packages/manifest.json` · `packages-lock.json`(MCP for Unity, Claude Hand Tools 로컬 경로 줄), `manifest.json.claude-hand.bak`, `ProjectSettings/ProjectSettings.asset`(runInBackground 등 도구용 설정). 다른 PC에서는 그 PC의 Claude Hand 설정을 다시 한다
- 그 밖에 커밋 안 함: `Pop/Screenshots/`(작업 확인용 화면), `Pop/Assets/Editor/HubForceResolve.cs` 삭제(이 PC에서 지워져 있었으나 의도 확인 전이라 커밋에 안 넣음 — 저장소에는 아직 있음), 저장소 루트의 `bgm20054.github.io/`(이 프로젝트와 무관)
- 다람쥐 FBX가 45 MB라 클론이 조금 느릴 수 있음
- 테스트는 아직 없다(작성 · 실행 모두 안 함)

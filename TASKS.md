# Pop — 진행 상황 (새 세션은 여기부터 읽는다)

3D 뚫어뻥 클라이밍 게임. 탐험가 다람쥐가 뚫어뻥을 붙이고 흔들고 떼며 산 정상의 도토리 나무까지 오른다. 난이도 아주 어려움.

- 기획서(Notion): https://app.notion.com/p/3D-41674e10a7648088ae2bcfe8a9f88471
- Unity 6000.3.25f1, URP, Input System. Unity 프로젝트 폴더는 `Pop/`
- 프로젝트 메모(결정 · 규칙 요약): `Pop/ClaudeHand/NOTES.md`

## 최신 (2026-11-13)
기획서 🔴 핵심 항목이 모두 들어갔다. 시험 씬은 `Pop/Assets/_Game/Scenes/Prototype.unity`(Build Settings 0번).
다음: 사용자 플레이 확인 → 🟡 체크포인트 · 골인 판정 · 기본 UI(높이 표시).

## 한 일
- [x] **이동 · 점프 · 카메라** (2026-11-12) — 사용자 "딱 좋다". 이 수치와 공중 관성 규칙은 함부로 바꾸지 않는다
  - Rigidbody 캡슐(키 1.2m), Z 위치 · 회전 고정, A/D 이동 · Space 점프(점프 버퍼 0.12초 · 코요테 0.08초), 중력 ×1.6, 마찰 0 재질
  - 공중에서 A/D를 안 누르면 감속하지 않고, 목표 속도보다 빠를 때 깎지 않음(관성 유지)
  - 카메라: 측면 고정 추적, 회전 없음, W/S 누르는 동안 시점만 위/아래 최대 6m, 떼면 복귀
- [x] **뚫어뻥 발사 · 부착 · 흔들기 · 해제** (2026-11-12)
  - 캐릭터 → 마우스 커서 방향 Raycast, 사거리 15m, 실패 시 쿨타임 0.3초. 부착 가능 = `PlungerAttachable` 컴포넌트(밝은 연두베이지 `M_Attachable`), 불가 = 회색 `M_Level`
  - 줄 = 늘어나지 않는 로프(거리 ≤ 길이, 속도 기반 제약), 길이 = 발사 순간 거리. 바닥에서 부착하면 0.4m 당겨 떠오름(0.2초 동안 부드럽게)
  - A/D = 줄에 수직 방향 힘(swingForce 20). 클릭 = 해제(속도 유지), Space = 해제 + 점프 힘
  - 조준 커서(OnGUI): 초록 = 부착 가능, 빨강 = 불가, 회색 = 사거리 밖
- [x] **접촉 해제 · 기절 · 낙사** (2026-11-13)
  - 부착 중 다른 면 접촉: 바닥(법선 기준) → 해제만, 벽 · 천장 → 해제 + 기절(Stunned)
  - 결정 (2026-11-13, 사용자): **부착한 그 벽이어도 닿으면 무조건 기절** — "기획서가 잘못된 것". 기획서 4장 표 첫 줄과 다름
  - 바닥에서 부착할 때만 0.2초 유예 + 바닥에서 떨어지기 전 바닥 접촉 무시. 공중 부착은 유예 없음
  - 기절: 조작 전부 불가, 바닥에 착지해야만 풀림. 머리 위 노란 별 + 어둡게, 풀릴 때 번쩍. 벽 틈 끼임 방지 밀어내기(실제 재현은 못 함)
  - 낙사: `KillZone` 트리거 또는 `killY`(-10) → 즉시 `RespawnPoint`로 리스폰. 체크포인트는 `PlayerController.SetRespawnPoint(Vector3)`를 부르면 됨
- [x] **발사 연출** (2026-11-13, 사용자 "붙지 않더라도 날아가는 연출")
  - 실패: 머리가 줄을 달고 날아가 부착 불가 면에서 튕겨 떨어지거나, 사거리 끝에서 멈칫 후 되감김
  - 성공: 부착은 즉시, 보이는 머리만 2배 속도로 날아가 꽂힘. 튜닝은 PlungerController 인스펙터(shotSpeed 45 등)

## 확인 못 한 것 (사용자 플레이로 확인)
- 실제 마우스 · 키 입력 전반(코드로 부착 · 해제 · 접촉 · 낙사는 Play에서 확인함)
- A/D 흔들기 감각, 머리 위 앵커 4개를 진자로 올라갈 수 있는지(기획서 "위로 올라가는 원리" 검증)
- 기절 표시 · 발사 연출의 느낌, 벽 바로 옆에서 그 벽에 쏘면 유예 직후 곧바로 기절하는 게 너무 가혹한지

## 다음 (기획서 15장)
- [ ] 🟡 체크포인트(`SetRespawnPoint` 사용) · 골인 판정(도토리 나무) · 기본 UI(높이 표시, 조준 커서 정리) · 튜닝 값 정리
- [ ] 🟢 애니메이션 · 사운드(휙/착!/뽁!/퍽!) · 충돌/기절 이펙트 · 카메라 연출 · 타임어택
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
- 테스트는 아직 없다(작성 · 실행 모두 안 함)

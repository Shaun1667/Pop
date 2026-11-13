# Pop 메모

- 2026-11-12 기획서: https://app.notion.com/p/3D-41674e10a7648088ae2bcfe8a9f88471 (3D 뚫어뻥 클라이밍 게임, 주인공 탐험가 다람쥐, 목표 산 정상 도토리 나무)
- 2026-11-12 플레이는 측면 시점 한 평면(A/D 좌우 이동만). 카메라는 캐릭터 고정 추적, 마우스는 조준 커서 전용(캐릭터→커서 방향 Raycast). W/S는 시점만 위/아래 이동.
- 2026-11-12 플레이어 상태: Normal / Attached / Flying / Stunned / Falling / Dead.
- 2026-11-12 Attached 중 부착 면 외 접촉 → 즉시 해제. 벽·천장·장애물이면 기절(바닥 착지까지), 바닥이면 해제만. Space = 해제 + 점프 힘.
- 2026-11-12 줄 길이 고정(발사 지점~부착 지점 거리). 예외: 바닥에서 천장/벽 부착 시 0.3~0.5m 당겨 떠오름 + 0.2초 유예.
- 2026-11-12 부착 가능 표면은 `PlungerAttachable` 컴포넌트 또는 Layer로 관리. 튜닝 값은 인스펙터에 노출.
- 2026-11-12 렌더 파이프라인 URP, 입력은 Input System 패키지.
- 2026-11-12 게임 코드·에셋은 Assets/_Game/ 아래. 키 입력은 Pop.Input.GameInput 한 곳에서만 읽는다. 튜닝 값은 Assets/_Game/Tuning/의 ScriptableObject(PlayerTuning, CameraTuning).
- 2026-11-12 이동·점프·카메라 1차 완료(Prototype 씬). 사용자 "딱 좋다" — 이 수치·공중 관성 규칙은 함부로 바꾸지 않는다.
- 2026-11-12 뚫어뻥 1차 완료. 부착 가능 면 = PlungerAttachable + M_Attachable(밝은 연두베이지), 불가 면 = 회색 M_Level. 줄 물리(늘어나지 않는 로프, 속도 기반 거리 제약)는 PlayerController, 조준·발사·표시는 PlungerController. 다음: 부착 중 다른 면 접촉 시 해제·기절(Stunned), 낙사, 체크포인트.
- 2026-11-13 접촉 해제·기절·낙사 1차 완료. 부착 중 다른 콜라이더 접촉 → 바닥이면 해제만, 벽·천장이면 Stunned(착지해야만 풀림). 낙사는 KillZone 트리거 또는 PlayerTuning.killY(-10) → 즉시 리스폰. 리스폰 지점은 PlayerController.SetRespawnPoint(Vector3)로 바꾼다(체크포인트가 이걸 부른다). 기절 시험 구역은 Ground_East(x 33~50)와 x 30~33 구덩이. 다음: 체크포인트 · 골인 · 기본 UI(🟡).
- 결정 (2026-11-13, 사용자): 부착 중 벽·천장에 닿으면 **부착한 그 면이어도 무조건 기절**. "기획서가 잘못된 것" — 기획서 4장 표 첫 줄(붙은 면 접촉은 기절 아님)은 따르지 않는다. 바닥 접촉 → 해제만은 그대로.

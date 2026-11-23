# Pop — 진행 상황 (새 세션은 여기부터 읽는다)

3D 뚫어뻥 클라이밍 게임. 탐험가 날다람쥐(2026-11-23부터, 활공은 없음)가 뚫어뻥을 붙이고 흔들고 떼며 산 정상의 도토리 나무까지 오른다. 난이도 아주 어려움.

- 기획서(Notion): https://app.notion.com/p/3D-41674e10a7648088ae2bcfe8a9f88471
- Unity 6000.3.25f1, URP, Input System. Unity 프로젝트 폴더는 `Pop/`
- 프로젝트 메모(결정 · 규칙 요약): `Pop/ClaudeHand/NOTES.md`

## 최신 (2026-11-23) 기절 풀림 번쩍임 제거 (사용자 요청)
- `PlayerStunVisual`: 착지로 풀릴 때 흰색 섞기를 지움. 풀리면 색이 바로 원래대로 돌아오고, 별이 튀며 사라지는 연출만 남음. 기절 중 어둡게(0.6) · 별은 그대로. 셰이더 `_FlashAmount` 시도는 커밋하지 않고 되돌림

## 최신 (2026-11-23) 플레이어 캐릭터에 툰 셰이더 (사용자가 털 대신 툰을 고름. 캐릭터만, 환경 머티리얼은 그대로)
- **셰이더** `Pop/Assets/_Game/Art/Shaders/PopToonCharacter.shader`("Pop/Toon Character", URP 17.3, Forward · Forward+ 모두, 렌더러 피처 없음) + `PopToonDepthPasses.hlsl`
  - ToonForward: 부드러운 2단 램프(half-lambert, 경계만 흐림) + SH 환경광 + 크림색 림(빛 쪽이 조금 더 셈). **그림자는 받지 않음**(얼룩 방지), 드리우기만. 추가 광원은 안 씀
  - Outline: `LightMode = SRPDefaultUnlit`(URP가 불투명 단계에서 그대로 그림), Cull Front, **화면 고정 두께**(1080p 기준 px, 해상도에 비례), 정점을 **카메라 반대로 `outlineDepthPush` m 밀어** 얇은 비막 · 챙에서 안 뚫고 나옴. 색 = outlineColor와 어둡게 한 알베도를 섞음
  - ShadowCaster · DepthOnly · DepthNormals(SSAO용, Cull Off일 때 뒷면 법선 뒤집음)
  - `_BaseColor`가 알베도 · 림 · 아웃라인에 모두 곱해져 **PlayerStunVisual(MPB _BaseColor 0.6) 그대로 동작**(코드 안 바꿈)
- **설정 에셋** `Pop/Assets/_Game/Art/Shaders/ToonSettings_FlyingSquirrel.asset`(클래스 `Scripts/Rendering/ToonCharacterSettings.cs`, `Pop.Rendering`). 값은 여기서만 고치고 메뉴 **Pop/Dev/Squirrel/Apply Toon Material**로 머티리얼에 넣음. `useToon` 끄고 메뉴 → URP Lit으로 되돌아감(텍스처 · Lit 키워드 그대로 남아 있음)
  - 최종값: normalStrength 0.25 · cull Back · shadeEdge 0.56 · shadeSoftness 0.10 · shadeColor (0.66,0.60,0.68) · lightStrength 0.85 · ambientStrength 0.5 · rim (1,0.92,0.76) 0.40 / edge 0.72 / softness 0.08 / litSide 0.35 · outline (0.16,0.11,0.09) albedoMix 0.45 · darken 0.35 · **1.5 px** · push **0.04 m**
- **머티리얼**: `M_FlyingSquirrel.mat`의 셰이더만 바꿈(GUID 71ae3406… 그대로 → FBX remap · 씬 참조 유지, 씬 수정 없음). `Editor/SquirrelAnimationSetup.cs`: `Character.toonSettingsPath`(Flying만), `ApplyToon()`, Setup(1 Setup Mixamo Assets)이 EnsureMaterial 뒤에 ApplyToon을 불러 **재실행해도 툰 유지**(Setup 자체 재실행은 이번에 안 해 봄 — 그때 Wings 작업이 진행 중이었음)
- **확인(Mountain Play, PC_Renderer Forward+, D3D12, 1920x1080)**: 셰이더 오류 · 경고 0, C# 오류 없음. 대기 · 활공 달리기 · 점프 · 낙하 · 기절, 시작 지점(회색 절벽 앞)과 T2_Ledge_04 통나무 위에서 전후 비교. 캐릭터 약 83px. 비막 · 모자 챙 · 스카프에서 아웃라인 뚫림 · 검은 삼각형 없음, 좌우 양쪽에서 비막 안 사라짐(cull Back 유지). 기절 어두워짐(0.6) 정상, 착지 뒤 원래대로. 평균 밝기는 Lit보다 약 +10%(기절은 같음)
- **성능**: 캐릭터 몫 드로우 +5~6 / SetPass +6 / 삼각형 +270k(Lit: +4 / +1 / +220k). Outline 패스 1회 더, 전용 셰이더라 SetPass를 환경 Lit과 나눠 쓰지 못함
- 그림: `Pop/Screenshots/toon_before_*.png` · `toon_after_*.png`(idle · run · jump · fall · stun · log_idle · log_run), `toon_after_close_glide_{left,right}_{game,above}.png`. `toon_after_close_runwings_*` · `toon_after_close_fall_membrane_*` · `probe_*.png`는 튜닝 전 낡은 그림
- **남은 것 / 아쉬운 점**: 기절이 풀릴 때 "흰색 번쩍임"은 Lit · 툰 모두 실제로 안 보임(원래 _BaseColor가 이미 흰색이라 흰색으로 Lerp해도 그대로 — PlayerStunVisual 구조 문제). 보이게 하려면 셰이더에 `_FlashAmount`를 두고 PlayerStunVisual이 넣게 하면 됨. 림을 노멀맵 N 대신 기하 법선으로 계산하면 normalStrength를 다시 올릴 수 있음(지금은 털 노멀 때문에 생기는 림 얼룩을 0.25로 피함). 법선 이음매 아웃라인 틈은 게임 배율에서 안 보여 매끈한 법선 굽기는 안 함. Mobile_Renderer(Forward)에서는 Play 확인 안 함(같은 패스라 동작할 것). 사람 눈 확인은 아직

## 최신 (2026-11-23) 달릴 때 활공 자세: 다리 정지 · 머리 고정 · 바닥에서 살짝 뜸 · 비막 펴고 가끔 퍼덕임 (애니메이션만)
- **사용자 요청**: ① "달릴 때 날개를 펴고 간간히 퍼덕이면서 날게, 이동 모션만, 이동 높이 동일" → ② 수정 "달릴 때 다리는 움직이지 않고 머리도 흔들리지 않아야, 다리가 공중에 떠야 하고 앞으로 살짝 기울어지게". (①의 첫 구현 = 달리기 다리 그대로 + 팔만 덮는 레이어는 ②로 대체됨)
- **방식**: Animator에 온몸을 덮는 **`Wings` 레이어**(Override, 기본 무게 0, 상태 WingsFlap)를 두고 `PlayerAnimationVisual`이 무게를 넣는다(바닥에서 Speed 1.5→4 m/s SmoothStep, 공중 · 매달림 0, `wingsBlendTime` 0.25초). 무게 1이면 달리기 클립이 완전히 덮인다. 이동 · 물리 · 캡슐 · Rigidbody · 튜닝 · 씬은 안 바꿈(컨트롤러 GUID 유지)
- **에셋**(Setup이 코드로 만든다, `Art/Characters/FlyingSquirrel/Mixamo/`): `FlyingSquirrel_WingsFlap.anim`(2.4초 루프. Idle 첫 프레임의 Humanoid 커브 130개를 고정값으로 깔고 → RootQ = 정면 · RootT.x = 0(좌우 대칭) → 몸통 · 목 · 머리 근육 0(`GlideBody`) → 다리 고정 자세(`GlideLeg`) → 팔: 편 채 미끄러지다 1.6~2.1초에 두 번 퍼덕임(+28 / -36도) → 퍼덕일 때 엉덩이가 안 움직이게 RootT.y 보정) · `FlyingSquirrel_Wings.mask`(온몸) · `FlyingSquirrel.controller`(레이어 2개)
- **코드**: `Editor/SquirrelAnimationSetup.cs`(`Character.wings` = Flying만 true, `EnsureWingsClip` · `EnsureWingsMask`, 상수 `WingsLoop · WingLevel 0.40 · WingFront 0.42 · WingTwist · WingStretch · WingElevation · GlideBody · GlideLeg · WingBodyCompUp/Down`), `Scripts/Player/PlayerAnimationVisual.cs`(`glideHover` 0.2 m = 보이는 모델(animator.transform)만 무게만큼 위로, `glideLean` 25 (2026-11-23 사용자 "25도로", 13에서 올림 · 옛 메모리 값이 남지 않게 필드 이름도 바꿈) = LateUpdate에서 엉덩이 뼈를 앞으로 돌림, `glideHeadCounter` 0.6. Wings 레이어가 없는 컨트롤러(예전 다람쥐)는 아무것도 안 함)
- **알아둘 것**: 이 아바타의 근육 부호 — Arm Front-Back 0.30 = 정확히 옆 · 클수록 뒤, Upper Leg In-Out + = 벌림, Upper Leg Front-Back 1.0 = 뒤로 최대(약 30도), Foot Up-Down - = 발끝 폄. Humanoid RootT는 무게중심이라 팔 · 다리 자세를 바꾸면 엉덩이 높이가 따라 변한다. Player · Visual 스케일 0.6(바닥 = Player y - 0.6)
- **검증**(Mountain Play, 달리는 중): 엉덩이 기준 발 위치 변화 0.000002 m 이하(다리 정지), 머리 y 범위 0.0011 m(퍼덕임 포함), 가장 낮은 발 뼈 = 지면 위 0.195 m(hover 0이면 -0.005 m), 기울임 엉덩이 13.0도 / 머리 5.2도. Player transform · Rigidbody y 0.60000 고정(hover 0.2와 0에서 같음), 캡슐 center (0,0,0) · height 2 · radius 0.5 불변. 가속 · 감속 때 무게 · hover 약 0.25초에 고르게, 멈춘 뒤 모델 localPosition 기준값과 차이 0, 점프 중 무게 0 · 오프셋 0 · Air 자세, 착지 후 다시 활공, 좌우 전환 정상. Setup 재실행으로 같은 결과(GUID 불변)
- **남은 것 / 사용자 판단**
  - 모습은 "엎드려 나는" 것이 아니라 선 채로 떠서 25도 숙인 자세다(다리가 짧아 뒤로 길게 안 뻗음). 게임 화면에서는 캐릭터가 약 70px이라 뜬 것 · 다리 정지는 보이지만 비막 · 퍼덕임은 작다. 꼬리는 Humanoid 뼈가 아니라 움직임 없음
  - 안 한 시험: 줄에 매달린 상태(코드상 무게 0), 걷기 속도(0.9~1.5 m/s) 유지, 근접 확대 메시 검사

## (2026-11-23) 뚫어뻥 줄이 몸통(가슴)에서 나오게 (사용자 "캐릭터를 바꾸면서 뚫어뻥 줄이 몸통 밖으로 나오게 됐어")
- **원인**: 보이는 줄 시작점이 `transform.position`(캡슐 중심)이었는데 날다람쥐 몸은 캡슐 아래쪽에 있어서 매달린 자세에서 줄이 머리 위 허공에서 시작했다(예: 플레이어 y 7.29 / 가슴 6.85 / 머리 7.07)
- **고침**: `PlungerController.RopeStart()`(새, public) = 다람쥐 Animator의 Humanoid 뼈 Chest(없으면 Spine → Hips → 플레이어 중심) + 바라보는 쪽으로 `ropeChestForward` 0.06 m, z는 플레이 평면. 뼈는 코드로 찾음(Awake, 없어지면 다시 찾음 — 씬 수정 없음), LateUpdate에서 읽어 애니메이션과 같은 프레임. 쓰는 곳: 줄 선 시작점 · 발사된 머리가 날아가기 시작하는 점 · 되감길 때 돌아오는 점. 인스펙터 `ropeFromChest`(끄면 예전처럼 중심) · `ropeChestForward`
- **그대로**: 줄 물리(Rigidbody 기준 거리 제약) · 조준 레이 원점(`_rb.position`, 보이는 선이 없어 안 바꿈) · 사거리 · 튜닝 값
- **Play 확인(Mountain, 패치 (12.5, 11.4) 아래)**: 매달림 · 흔들기 · 왼쪽 보기 · 낙하 중 부착(시작점 y 8.10 = 가슴, 예전 값 8.37) · 바닥에서 발사(원점 = 가슴 + 바라보는 쪽 0.06) 모두 가슴에서 시작. 오류 없음, 씬 안 바뀜, `Pop.BestClearTime` 안 건드림. 그림 `Pop/Screenshots/rope_chest_hang_gameview.png` · `rope_chest_hang_close.png` · `rope_chest_swing_left.png`
- 남은 것: 줄은 z = 0 평면에 그려서 몸 앞이 아니라 몸 안(등 · 어깨 쪽)으로 들어가 보인다. 사람 눈 확인은 아직

## 최신 (2026-11-23) 플레이어 캐릭터를 날다람쥐로 교체 (겉모습만)
- **캐릭터는 이제 탐험가 날다람쥐**(Mixamo 리그, 팔다리 사이 비막 있음). **활공 기능은 일부러 넣지 않았다 (사용자 결정 2026-11-23)**: 이동 · 점프 · 뚫어뻥 · 튜닝 값은 그대로, 겉모습과 Animator만 바꿈
- **새 에셋** `Pop/Assets/_Game/Art/Characters/FlyingSquirrel/`
  - `Mixamo/FlyingSquirrel_Explorer_Mixamo.fbx`(스킨 모델, m 단위, 4.5만 삼각형, 뼈 25개, Humanoid, 메시 FlyingSquirrel · 슬롯 M_FlyingSquirrel) + `FlyingSquirrel@HappyIdle · @HappyWalk · @RunForward · @Jump · @FallingIdle.fbx`(Mixamo 원본, cm 단위. Humanoid라 단위 차이는 아바타 크기로 맞춰짐 — 엉덩이 높이 모델 0.19 m / 클립 0.18 m 확인)
  - 클립: `FlyingSquirrel_Idle`(2.0 s, loop) · `_Walk`(1.1 s, loop) · `_Run`(0.9 s, loop) · `_JumpAir`(Jump 24~42프레임, 0.6 s, loop 아님) · `_Fall`(0.7 s, loop) — 예전 다람쥐 세트와 길이 · 루프 같음
  - `Mixamo/FlyingSquirrel.controller`(새 Animator: Locomotion[Idle 0 / Walk 0.9 / Run 6 m/s 3배속] · Air[JumpT] · Fall, 파라미터 Speed · Grounded · JumpT · VSpeed — 예전 `Squirrel.controller`와 같은 구조)
  - `Textures/base_color.png`(4096, 오른쪽 어깨 스카프 고친 판) · `normal.png`(Normal map) · `orm.png`(linear) · `metallic_smoothness.png`(orm에서 만듦: R = ORM.B, A = 1 - ORM.G, linear). 가져오기 최대 2048
  - `Materials/M_FlyingSquirrel.mat`(URP Lit, base + normal + metallic_smoothness, Smoothness 0.6 = 예전 다람쥐와 같음). FBX의 M_FlyingSquirrel 슬롯을 이 머티리얼로 remap
- **코드** `Editor/SquirrelAnimationSetup.cs`를 캐릭터 설정(`Character`: 폴더 · 모델 · 클립 접두어 · 컨트롤러 · 머티리얼 · 텍스처 폴더 · RunTimeScale) 두 벌로 일반화: `Flying`(기본) · `Classic`(예전 다람쥐)
  - 메뉴 **Pop/Dev/Squirrel/1 Setup Mixamo Assets** · **2 Replace Player Visual (open scene)** = 날다람쥐. **Pop/Dev/Squirrel/Old Squirrel/1 ... · 2 ...** = 예전 다람쥐로 되돌리기(같은 일을 예전 에셋으로)
  - Setup: 텍스처 가져오기 설정 + metallic_smoothness 생성 + 머티리얼(있으면 고쳐 씀) + FBX Humanoid · 클립 설정 + 컨트롤러. 컨트롤러가 이미 있으면 파일 내용만 바꿔 GUID 유지(Setup을 다시 해도 씬 참조가 안 끊김)
  - Replace: Player/Visual 아래의 예전 다람쥐 · 날다람쥐 인스턴스를 모두 지우고 새로 넣음(멱등, 두 번 실행해도 자식 1개). 키 1.2 m · 발 = 캡슐 바닥 · 엉덩이 = 캡슐 축, PlayerAnimationVisual.animator 연결, Root Motion 끔. `ReplaceInActiveScene(ch, save: false)`로 저장 없이도 가능
  - `Scripts/Dev/AnimCheck.cs`: `TPoseHandSpan`을 public 필드로(모델마다 다름). 날다람쥐는 바인드 자세가 팔을 내린 자세라 이 손 거리 검사는 공중 자세에서 의미가 없다(0으로 두면 됨)
- **씬**: Mountain · Prototype 모두 `Player/Visual/FlyingSquirrel`로 교체해 저장(예전 `Squirrel` 인스턴스 삭제). 로컬 스케일 2.165, 구운 메시 높이 1.200 m, 발 y = 캡슐 바닥(차이 0.000), PlayerFacingVisual.visual · PlayerStunVisual · PlayerAnimationVisual.animator 연결 확인. Player 밖은 안 건드림
- **예전 다람쥐 에셋은 그대로 둠**: `Art/Characters/Squirrel/`(Mixamo FBX · Squirrel.controller · 텍스처 · M_Squirrel_Explorer_Rig.mat). 씬에서는 더 안 씀
- **Play 확인(Mountain)**: AnimCheck — 대기 · 이동 6 m/s · 방향 전환(Visual yaw 110 / 250) · 점프(체공 1.02 s, 높이 1.96 m, Air 자세가 JumpT로 진행) · 기절 색 0.6 → 착지 뒤 1.0 · 긴 낙하(vy -20 → Fall) → 착지 Locomotion. 씬을 다시 불러 첫 프레임부터 3300프레임 동안 바인드 자세(T자) 0프레임(첫 프레임부터 Idle). 자세별 그림(대기 · 걷기 · 달리기 · 점프 · 낙하 × 옆 · 앞 · 뒤)에서 비막이 튀거나 찢어진 곳 없음. Prototype은 Play로 서 있는 것까지만 확인. 내 변경으로 생긴 경고 · 오류 없음
- **발 미끄러짐**: Run 1배속 디딤 발 속도 약 1.55 m/s(예전 다람쥐 1.57 m/s와 같음, 같은 방법으로 잼) → 3배속 약 4.65 m/s, 6 m/s에서 약 22% 미끄러짐. 예전과 같아서 RunTimeScale 3.0 그대로(딱 맞추려면 약 3.9배속인데 발이 너무 빨라짐)
- **남은 것 / 아쉬운 점**: 등 쪽(조끼 아래 · 비막 뿌리) 텍스처가 얼룩지고 엉덩이 옆에 밝은 틈처럼 보이는 곳이 있음(게임 카메라에서는 등이 안 보임). 팔 옆 비막에 텍스처 늘어진 줄무늬. Fall 자세는 예전처럼 머리부터 떨어지는 모양, 점프 정점은 앞으로 숙인 자세. 매달리기 · 기절 · 착지 전용 동작 없음(예전과 같음). 사용자 눈 확인은 아직

## 최신 (2026-11-23) Esc 일시정지 (사용자 "게임중 ESC를 누르면 게임이 일시정지되고 돌아가기 게임종료 버튼 두개")
- **동작**: 한 판 중(RunManager가 있는 씬 = 지금은 Mountain만. Prototype · Title에는 GameUI가 없어 일시정지 없음) Esc → 어두운 배경 + "일시정지" + [돌아가기] · [게임 종료] (+ 작은 "Esc 계속하기"). Esc 다시 · [돌아가기] → 계속. [게임 종료] → Application.Quit(에디터에서는 Play 멈춤)
- **일시정지 중**: Time.timeScale 0(들어가기 전 값을 기억했다가 그대로 되돌림, 1로 강제 안 함) · 타이머 멈춤(RunManager가 `PauseMenu.IsPaused`면 안 셈) · 게임 조작 입력 무시(`GameInput.GameplayBlocked` → MoveX · Jump · Fire · LookY 0/false, PlayerController · PlungerController Update는 바로 return이라 이동 입력 · 점프 버퍼가 그대로 남아 계속할 때 끊김 없음) · 조준 커서 숨김 · 시스템 커서 보임(계속하면 이전 Cursor.visible · lockState로 되돌림)
- **소리**: `AudioListener.pause`로 효과음 · 기절 반복음 · 징글이 멈췄다가 그 자리에서 이어짐. **BGM은 50%로 줄여 계속 틂**(BGM 소스 ignoreListenerPause, 설정 `pauseBgmVolumeMultiplier` 0.5 · `pauseBgmFadeTime` 0.2 s). UI 클릭음은 이제 전용 AudioSource `UI`(ignoreListenerPause, PlayOneShot)로 남 → 일시정지 중에도 들림. 새 `GameAudio.HookButton(Button)`: PauseMenu가 Start에서 자기 버튼 둘을 한 번 더 걸어 둠(중복은 거름)
- **Esc가 안 먹는 때**: 골인 뒤(RunManager.Cleared · 조작 잠금), 디버그 순간이동 얼리기 중(`PauseMenu.DebugFreeze`, DebugTeleportWindow가 켜고 끔), timeScale이 이미 0일 때. 일시정지 중에 얼리기를 켜면 계속해도 0으로 남고, 얼림을 풀 때 일시정지 전 timeScale로 돌아감(얼림을 일시정지 중에 풀면 일시정지가 되돌림)
- **입력**: `GameInput.PausePressed`(Keyboard Esc, 다른 키와 같은 방식) + 입력 액션 에셋 `Pop/Assets/InputSystem_Actions.inputactions`의 Player 맵에 `Pause`(Button, <Keyboard>/escape)를 더함(게임 코드는 지금처럼 GameInput만 읽음)
- **코드**: 새 `Pop/Assets/_Game/Scripts/Game/PauseMenu.cs`(GameUI 오브젝트, RunManager 옆, 실행 순서 -60). 바꾼 것: `GameInput.cs` · `RunManager.cs`(타이머 한 줄) · `PlayerController.cs` · `PlungerController.cs`(일시정지면 return 한 줄씩, 이동 · 튜닝 값 그대로) · `GameAudio.cs` · `GameAudioSettings.cs`(필드 2개) · `Editor/DebugTeleportWindow.cs`(Freeze/Unfreeze) · `Editor/GameUIBuilder.cs`(메뉴 **Pop/Dev/Add Pause Menu**: 열린 씬 GameUI에 PausePanel + PauseMenu, 있으면 건너뜀. Add Mountain Game UI도 부름)
- **씬**: Mountain.unity의 GameUI 아래에 `PausePanel`(꺼진 채, 마지막 자식)과 PauseMenu 컴포넌트만 더해 저장. 레벨 · EnvVisual은 안 건드림
- **Play 확인(Mountain, 키 · 마우스는 Input System 이벤트로 흉내)**: Esc → 패널 · timeScale 0 · 타이머 8.134에서 멈춤 · BGM 0.35 → 0.175 · 시스템 커서 보임 / Esc 다시 → 계속(커서 숨김, 타이머 이어서). 공중(Falling)에서 일시정지 → 10초 동안 위치 · 타이머(15.6719) 그대로 → [돌아가기]를 마우스로 누름: 누르는 프레임에 발사 없음(Shot None, Fire 소리 없음), 떼면 계속 · 클릭음 · 속도 그대로 · 타이머 15.67에서 이어짐. timeScale 0.5에서 일시정지 → 계속하면 0.5. 얼리기 흉내(DebugFreeze + 0) 중 일시정지 안 됨. 골인(RunManager.Clear) 뒤 Esc → 아무것도 안 함. [게임 종료] 클릭 → Play 멈춤. 경고 · 오류 없음. `Pop.BestClearTime`은 시험 전에 없었고 골인 시험 뒤 지움(지금 없음)
- 그림: `Pop/Screenshots/pause_menu_mountain.png`
- 남은 것: 사람 손으로 Esc 확인(실제 키 · 마우스), 기절 반복음 멈춤은 AudioListener.pause로 처리(기절 중 일시정지는 따로 안 해 봄), DebugTeleportWindow 얼리기와 겹치는 경우는 코드로만 확인, Prototype에는 HUD가 없어 일시정지도 없음(필요하면 Prototype에서 Pop/Dev/Add Mountain Game UI → 골인 트리거는 없으면 건너뜀)

## 최신 (2026-11-23) 소리 (BGM · 효과음 · UI · 징글)
- **구조**: `Pop/Assets/_Game/Scripts/Audio/GameAudio.cs`(싱글톤, 씬에 안 둠) + 설정 `Pop/Assets/_Game/Scripts/Audio/GameAudioSettings.cs` → 에셋 `Pop/Assets/_Game/Tuning/Resources/GameAudioSettings.asset`
  - 시작: `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`가 Resources에서 설정을 불러 `GameAudio` 오브젝트를 만들고 DontDestroyOnLoad. 어느 씬에서 Play해도(Title · Mountain · Prototype) 생긴다. **씬 파일은 하나도 안 바꿈**
  - 씬이 불릴 때마다: 그 씬의 PlayerController · PlungerController · RunManager 이벤트를 듣고, 모든 uGUI `Button`(꺼진 것 포함)의 onClick에 클릭음을 단다. 켜진 AudioListener가 없으면(Title) 자기 AudioListener를 켠다(소리는 전부 2D)
  - AudioSource: 효과음 풀 10개(2D), 기절 반복 1개(loop), BGM 2개(크로스페이드), 징글 1개. 페이드는 unscaled 시간
  - 무작위: 이벤트마다 변형 3개 중 하나, 바로 전과 같은 것은 안 고름. 효과음 · UI 피치 1 ± 0.05
- **이벤트 → 클립** (클립은 전부 `Audio/` 아래 `_cut` 판, BGM만 원본 mp3)
  - 발사(성공 · 실패) → `SFX_Plunger_Fire_0x_cut` — `PlungerController.Fired`(새 이벤트, Fire() 시작)
  - 부착 → `SFX_Plunger_Attach_0x_cut` — `PlungerController.HeadPlanted`(새 이벤트, 보이는 머리가 꽂히는 순간. 날아가는 0.1초 안에 놓으면 안 남)
  - 해제(클릭 · Space) → `SFX_Plunger_Release_0x_cut` — `PlayerController.Released` 중 상태가 Flying인 것(DoRelease)만. 접촉 해제 · 낙사 · 순간이동 해제는 소리 없음
  - 점프 → `SFX_Jump_0x_cut` — `PlayerController.Jumped`(새 이벤트, 일반 점프만)
  - 착지 → `SFX_Land_0x_cut`, 아래 속도로 볼륨: GameAudio.FixedUpdate(실행 순서 100, PlayerController 다음)가 공중일 때 마지막 속도를 기억했다가 IsGrounded가 켜지는 스텝에 씀. 잰 값: 0.2 m 턱 ≈ 2.4 m/s(소리 없음), 0.3 m 2.9, 0.5 m 3.9, 평지 점프 7.8
  - 기절 시작(부착 중 접촉 기절 · 공중 충돌 기절 모두) → `SFX_Hit_Wall_0x_cut` 한 번 + `SFX_Stun_Loop_0x_cut` 반복 — `PlayerController.StateChanged`(→ Stunned). Stunned에서 나가면 0.15초 페이드 아웃. 리스폰(Respawned) · 씬 바뀜 · 플레이어 없어짐에도 멈춤
  - UI 버튼 전부 → `UI_Button_Click_0x_cut` (2026-11-23부터 전용 소스 `UI`, 일시정지 중에도 남)
  - BGM: Title → `BGM_Title_01/02`, Mountain · Prototype → `BGM_Climb_01/02` (무작위, loop, 1.5초 크로스페이드). 같은 종류 씬끼리는 이어서 틀고, 떨어짐 · 리스폰에는 다시 시작 안 함
  - 골인 → BGM 0.5초에 0으로 + `Jingle_Clear_0x_cut` — `RunManager.RunCleared`(새 이벤트, Clear() 끝). 다시 하기 · 타이틀로 가면 새 BGM이 크로스페이드로 들어온다(징글은 0.3초 페이드)
- **튜닝**: `Tuning/Resources/GameAudioSettings.asset` 인스펙터 — 클립 목록, 볼륨(BGM 0.35 · SFX 0.8 · UI 0.6 · 징글 0.8), 이벤트별 배율(fire · attach · release · jump 0.8 · land · hitWall · stunLoop 0.6 · uiClick), pitchVariation 0.05, bgmCrossfadeTime 1.5, goalBgmVolumeMultiplier 0 · goalBgmFadeTime 0.5, stunLoopFadeOut 0.15, landMinImpactSpeed 2.8 · landFullImpactSpeed 9 · landMinVolume 0.3, titleScenes · climbScenes(씬 이름), sfxPoolSize 10, **debugLog**(켜면 `[GameAudio] <이벤트> -> <클립>` 로그)
- **가져오기 설정**: `_cut` 효과음 · UI · 징글 = Decompress On Load, Force To Mono, Preload 켬, Load In Background 끔, Vorbis 100%(기절 반복 3개만 PCM — 반복 이음 안전). BGM mp3 4개 = Streaming, Vorbis 70%, Preload 끔. 원본 wav(`_cut` 아닌 것)는 안 씀
- 바꾼 게임 코드(이벤트만 더함, 동작 · 수치 그대로): `PlayerController.Jumped`, `PlungerController.Fired` · `HeadPlanted`, `RunManager.RunCleared`
- **Play 확인**: Title BGM → [시작] 클릭음 → Mountain 크로스페이드 · 점프 · 착지(세기별, 0.2 m 턱은 안 남) · 발사 · 부착 · 클릭 해제 · Space 해제 · 공중 벽 기절(Hit_Wall + 반복, 착지하면 멈춤) · 부착 중 천장 기절 · 기절 중 낙사 → 반복 멈춤, BGM 계속 · 골인 이벤트(RunCleared만 불러 기록 저장 안 함) → BGM 0 + 징글 · [다시 하기] → 새 Climb BGM · [타이틀로] → Title BGM · Prototype → Climb BGM. 경고 · 오류 없음. `Pop.BestClearTime`은 시험 전후 모두 없음(안 건드림)
- 남은 것: AudioMixer 없음(볼륨은 설정 에셋으로), 실제 손 플레이로 볼륨 균형 확인, 착지 동작 · 줄 흔들림 소리 · 환경음(`Audio/AMB/` 비어 있음) 없음

## 최신 (2026-11-23) Mountain 겉모습 VARCO 환경 모델로 교체 (Phase B)
- **콜라이더 · 게임 동작은 그대로**: 바꾼 오브젝트마다 자식 `EnvVisual`(콜라이더 없음, static = Batching · Occluder · Occludee · ReflectionProbe, GI 기여는 끔)을 더하고 원래 MeshRenderer만 껐다. 위치 · 부모 · 콜라이더 · PlungerAttachable · Player · GameUI · GoalTrigger는 손대지 않음. Rebuild Mountain Level 실행 안 함(손 편집 보존). Prototype은 안 바뀜
- **메뉴** `Pop/Dev/Apply Env Visuals`: 열린 씬에 적용. 다시 실행하면 이전 `EnvVisual`을 지우고 새로 만든다(멱등). 끝에 맞춤 검사 로그. 그 밖에 `Pop/Dev/Env Visuals/Setup Assets`(FBX 가져오기 설정 · 텍스처 · 머티리얼), `.../Fit Check`(검사만), `.../Remove`(EnvVisual 지우고 원래 렌더러 다시 켬). 코드 `Pop/Assets/_Game/Editor/EnvVisualsApplier.cs`
- **에셋** `Pop/Assets/_Game/Art/Environment/Mountain/`: `Models/`(FBX 6개, Bake Axis Conversion 켬 · Generate Colliders 끔 · Read/Write 끔), `Textures/<모델>/`(base_color · normal · orm, + metallic_smoothness = M_AcornTree와 같은 방법: R = ORM.B, A = 1 - ORM.G), `Materials/M_*.mat`(URP Lit, Smoothness 0.6), `Concepts/`(컨셉 그림). 안 쓴 것: WedgeRock_Mossy, MossPad, unused 폴더
- **대응**
  - `Crag_*/Rock` 29개 → Env_WedgeRock_Grey: Rock 메시 범위(폭 · 높이 · 깊이 4)에 맞추고 바닥 맞춤, 모델 지붕이 볼록해서 Rock 콜라이더와 레이로 비교해 높이만 줄임
  - `Crag_*/Patch` 29개 → **그대로 (사용자 결정: 연두베이지 M_Attachable 상자가 보이게 둔다)**
  - 보통 발판 `T*_Ledge_*` 24개(4.5~7.8 × 2.5 × 4) → Env_LogLedge(Y 90° 회전). 폭 6 m 넘으면 2개를 끝과 끝으로. 평평한 윗면 = 상자 윗면. 가지 그루터기 끝은 1개면 가까운 벽 쪽, 2개면 양 바깥 끝
  - `T7_Ledge_23 (1)~(7)`, `T2_Ledge_06 (1)~(3)` → Env_TrunkPillar(밑동이 벌어진 모양이라 몸통 폭을 상자 폭 - 0.1 m에 맞춤)
  - `T5_Ledge_15`, `T5_Ledge_16` → Env_RockPillar 3개 쌓기(윗면이 울퉁불퉁해서 위 두 조각은 뒤집어 평평한 면을 위로, 이음은 겹쳐 묻음)
  - `Summit_Plateau`, `Ledge_Start` → Env_MesaPlateau / `Wall_Left`, `Wall_Right` → Env_CliffWall 24장 세로 타일(번갈아 뒤집음)
  - **2차 (2026-11-23, "아직 회색 박스 부분이 있어")**: `Floor_Basin` → Ground, `Backdrop_Mountain/Backdrop_00~22` → Backdrop 규칙을 applier에 더했다(같은 메뉴 한 번으로 전부, 멱등). 모델 대신 **만든 메시**(씬에 저장)를 EnvVisual 아래에 둔다
    - Ground(`GrassGround`): Floor_Basin 모양(윗면 꺾인선 + 평평한 바닥, z ±2)을 그대로 다시 만든다 → 윗면 = 콜라이더 면(맞춤 검사 0.00). 윗면 + 옆면 위쪽 울퉁불퉁한 띠(0.16~0.40 m) = 풀 `M_Ground_Grass`, 나머지 옆 · 앞 · 바닥 = 돌 `M_Ground_Rock`, 월드 좌표 UV
    - Backdrop(`BackdropCliff`): 판마다 앞면(z 13)만 있는 절벽 면. 판 가운데 높이를 잇는 부드러운 산 능선 + 울퉁불퉁한 윤곽, z ±1.6 m 굴곡(법선은 해석적으로 → 판 경계 이음선 없음). 열 1 m × 행 60, 판 23개 합계 약 3만 삼각형. `M_Backdrop_Cliff`(절벽 돌, 어둡고 푸르스름한 tint, 30 m 타일)
    - 텍스처: VARCO 텍스처는 UV 아틀라스라 메사 윗면 풀 · 절벽 돌 영역을 잘라 4장 섞기로 이음선 없는 반복 텍스처를 만든다 `Art/Environment/Mountain/Textures/Env_Tile/`(base · normal · metallic_smoothness, Wrap Repeat, TextureImporter.userData = 버전). 머티리얼 `Materials/M_Ground_Grass · M_Ground_Rock · M_Backdrop_Cliff`(URP Lit)
    - 새 메뉴 `Pop/Dev/Env Visuals/Audit`: 켜진 회색 상자 렌더러 목록(EnvVisual · Crag Patch · Player · GameUI · 도토리 나무 제외) → **0개**. SafetyNet은 렌더러 없음
    - Play: Floor_Basin x 20 · 45 · 70에 세움 → Normal · 접지, 콜라이더 바닥 - 표면 = +0.001 m. 패치 아래 · 비스듬히 아래 레이 87개 중 85개가 패치에 맞음(2개는 전처럼 사용자 기둥 T2_Ledge_06 (1)), EnvVisual 콜라이더 0개. 골인 근처는 안 감, `Pop.BestClearTime`(가짜 110.34 s) 그대로
    - 남은 것: 돌 · 배경 텍스처가 반복되는 줄무늬가 보인다(배경 절벽의 가로 금 · 바닥 앞면 7~11 m 반복). 풀 윗면은 거의 단색. 배경 판 맨 바깥 두 장(x -82 · +148) 바깥 끝은 세로로 잘림
    - 그림: `Pop/Screenshots/env2_start_basin_final.png`(시작 구역) · `env2_overview.png` · `env2_backdrop.png` · Game 뷰 `env2_game_basin_x20.png` · `env2_game_basin_x70.png`(전: `env2_before_start.png`)
  - 그대로: 도토리 나무, 골인, Crag Patch. 규칙에 안 맞아 남은 것 없음
  - 모델 조각 123개, 약 123만 삼각형. Play GPU 약 0.9 ms라 그림자는 그대로 둠
- **맞춤 검사** (겉모습 메시 vs 콜라이더 레이 비교, z = 0, m)
  - 서는 윗면 중앙값: 통나무 -0.02, 나무 기둥 -0.03, 바위 기둥 0.00, 메사 +0.03. 옆면 끝에서 설 수 있는 높이(-0.15 안)까지: 통나무 ≤ 0.05, 바위 기둥 ≤ 0.07, 나무 기둥 ≤ 0.10, 메사 ≤ 0.15
  - 통나무 한쪽 끝 그루터기(길이의 약 15%)는 윗면보다 최대 0.30 m 높다(모델 모양). 그 끝에 서면 발이 묻혀 보인다
  - 메사는 윗면 가장자리가 둥글어 양옆으로 0.55 m 넓혔다 → 옆으로 최대 0.31 m 튀어나옴(규칙 0.15 넘음, 서는 면 쪽을 택함)
  - 쐐기 바위 지붕(서는 면 아님): 작은 바위 중앙값 0.00~+0.03, 큰 바위 T7_A18~20(폭 4.6 m)은 중앙값 +0.11, 울퉁불퉁해서 ±0.46. 옆으로 최대 0.30
  - 나무 기둥 옆면 안쪽 최대 0.14 · 바깥 0.04, 바위 기둥 안쪽 0.20, 절벽 벽 안쪽 면 안쪽 최대 0.34(울퉁불퉁)
- **Play 확인**
  - RouteBot 0단계부터: 1단계(T1_A00) 발사 실패 — 사용자가 옮긴 Crag_T1_A00(예전과 같음). 5단계부터(순간이동): 5~41 통과, 42(T5_A13)에서 T5_Ledge_15 기둥에 부딪혀 기절. 58부터: 59(T7_A18) 스윙 중 Crag_T6_A16에 부딪혀 기절. 65부터: 65~81 통과, 82(T9_A25)에서 사용자 기둥 T7_Ledge_23 (3)에 부딪혀 기절. 실패는 모두 경로 데이터(MountainRoute)와 지금 레벨이 어긋난 곳(T6_Ledge_18/19 없음, 기둥 · 바위 위치 등). EnvVisual을 Play 중에만 끄고 같은 구간(40~, 58~)을 다시 돌려도 같은 단계 · 같은 위치에서 실패 → 겉모습은 게임에 영향 없음
  - DropTester 5곳(T1_Ledge_02 · T7_Ledge_23 (1) · Crag_T1_A01 Rock · Summit_Plateau · T2_Ledge_06 (1)): 겉모습 켜고 / 끄고 결과 같음
  - 뚫어뻥 조준 레이: 패치 29개 × 5방향 = 145개 중 142개가 패치에 맞음. 3개는 사용자 기둥 T2_Ledge_06 (1)(2)의 콜라이더에 막힘(겉모습과 무관). EnvVisual 아래 콜라이더 0개
  - **주의**: DropTester의 정상 지점 낙하가 골인 판정에 들어가 가짜 최고 기록 01:50.33이 PlayerPrefs `Pop.BestClearTime`에 저장됨. 지우기: `PlayerPrefs.DeleteKey("Pop.BestClearTime")` 또는 `ClearRecord.ClearBest()`
- 확인 그림: `Pop/Screenshots/env_overview.png`, `env_section1_foothills.png` ~ `env_section4_summit.png`, Game 뷰 `env_play_bot_t8.png`(통나무 위) · `env_game_player_wedge-1.png`(쐐기 바위 옆)
- 남은 것: 사용자 눈 확인, 통나무 그루터기 끝 · 메사 가장자리 절충이 괜찮은지, 경로 데이터를 지금 레벨에 맞추면 봇 완주 검증 가능

## 최신 (2026-11-23) 다람쥐 Mixamo 리깅 · 대기 / 걷기 · 달리기 / 점프 애니메이션 (예전 다람쥐 — 지금 캐릭터는 맨 위 날다람쥐 항목)
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
- ~~Esc(일시정지/타이틀)는 아직 없음~~ → 2026-11-23 Esc 일시정지 추가(맨 위 항목)

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
- 플레이어 = 탐험가 날다람쥐(2026-11-23 교체, 맨 위 항목. 그 전은 VARCO 탐험가 다람쥐 Mixamo 리그), 정상 = VARCO 도토리 나무
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

using UnityEngine;
using UnityEngine.InputSystem;

namespace Pop.Input
{
    /// <summary>
    /// 키보드·마우스 입력을 읽는 유일한 곳. 다른 스크립트는 여기 값만 본다.
    /// 마우스 왼쪽 클릭 = 뚫어뻥 발사/해제, 마우스 위치 = 조준 커서.
    /// A/D = 좌우 이동, Space = 점프, W/S = 시점 위/아래(캐릭터 조작과 무관), Esc = 일시정지.
    /// GameplayBlocked(일시정지 중)이면 게임 조작 값(MoveX · Jump · Fire · LookY)은 전부 0/false. PausePressed와 포인터 위치는 그대로.
    /// </summary>
    public static class GameInput
    {
        /// <summary>일시정지 등으로 게임 조작을 막는가 (PauseMenu가 켜고 끈다)</summary>
        public static bool GameplayBlocked { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => GameplayBlocked = false;

        /// <summary>A/D: -1 ~ 1</summary>
        public static float MoveX
        {
            get
            {
                var kb = Keyboard.current;
                if (kb == null || GameplayBlocked) return 0f;
                float x = 0f;
                if (kb.aKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed) x += 1f;
                return x;
            }
        }

        /// <summary>이번 프레임에 Space를 눌렀는가</summary>
        public static bool JumpPressed
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && !GameplayBlocked && kb.spaceKey.wasPressedThisFrame;
            }
        }

        /// <summary>Space를 누르고 있는가</summary>
        public static bool JumpHeld
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && !GameplayBlocked && kb.spaceKey.isPressed;
            }
        }

        /// <summary>이번 프레임에 마우스 왼쪽 버튼을 눌렀는가 (뚫어뻥 발사 / 해제)</summary>
        public static bool FirePressed
        {
            get
            {
                var m = Mouse.current;
                return m != null && !GameplayBlocked && m.leftButton.wasPressedThisFrame;
            }
        }

        /// <summary>이번 프레임에 Esc를 눌렀는가 (일시정지 열기 / 닫기). 입력 액션 에셋의 Player/Pause와 같은 키.</summary>
        public static bool PausePressed
        {
            get
            {
                var kb = Keyboard.current;
                return kb != null && kb.escapeKey.wasPressedThisFrame;
            }
        }

        /// <summary>마우스가 연결돼 있는가</summary>
        public static bool HasPointer => Mouse.current != null;

        /// <summary>마우스 화면 위치 (픽셀, 왼쪽 아래 원점). 마우스가 없으면 화면 가운데.</summary>
        public static Vector2 PointerScreenPosition
        {
            get
            {
                var m = Mouse.current;
                return m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }

        /// <summary>W/S: 시점 위(+1)/아래(-1)</summary>
        public static float LookY
        {
            get
            {
                var kb = Keyboard.current;
                if (kb == null || GameplayBlocked) return 0f;
                float y = 0f;
                if (kb.wKey.isPressed) y += 1f;
                if (kb.sKey.isPressed) y -= 1f;
                return y;
            }
        }
    }
}

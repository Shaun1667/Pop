using System;
using Pop.Input;
using Pop.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pop.Game
{
    /// <summary>
    /// 한 판 중 Esc 일시정지 (GameUI 오브젝트, RunManager 옆).
    /// Esc → 일시정지 화면(어두운 배경, "일시정지", [돌아가기] [게임 종료]). Esc 다시 · [돌아가기] → 계속.
    /// 일시정지 중: Time.timeScale 0(들어가기 전 값을 기억했다가 그대로 되돌림, 1로 강제하지 않음),
    /// 타이머 멈춤(RunManager), 게임 조작 입력 무시(GameInput.GameplayBlocked: 이동 · 점프 · 발사 · 시점),
    /// 시스템 커서 보임 · 조준 커서 숨김, 소리는 GameAudio가 PausedChanged를 듣고 처리한다.
    /// 골인(클리어 화면) 뒤 · 조작 잠금 중 · 디버그 순간이동 얼리기(DebugFreeze) 중 · timeScale이 이미 0이면 Esc는 아무것도 안 한다.
    /// </summary>
    [DefaultExecutionOrder(-60)] // PlayerController · PlungerController보다 먼저: 같은 프레임의 입력부터 막는다
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }

        /// <summary>지금 일시정지 중인가</summary>
        public static bool IsPaused { get; private set; }
        /// <summary>일시정지 바뀜 (true = 멈춤). GameAudio가 듣는다.</summary>
        public static event Action<bool> PausedChanged;

        /// <summary>
        /// 디버그 순간이동 창의 "얼리기"가 켜져 있는가 (창이 켜고 끈다). 켜져 있으면 Esc 무시,
        /// 일시정지 중에 켜졌으면 계속할 때 timeScale을 0으로 둔다(얼림을 풀 때 창이 되돌린다).
        /// </summary>
        public static bool DebugFreeze { get; set; }

        /// <summary>일시정지 들어가기 직전 Time.timeScale (일시정지 중에만 뜻 있음)</summary>
        public static float TimeScaleBeforePause { get; private set; } = 1f;

        [Header("일시정지 화면")]
        public GameObject pausePanel;
        public Button resumeButton;
        public Button quitButton;

        RunManager _run;
        PlayerController _player;
        bool _prevCursorVisible;
        CursorLockMode _prevCursorLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsPaused = false;
            DebugFreeze = false;
            TimeScaleBeforePause = 1f;
            PausedChanged = null;
            Instance = null;
        }

        void OnEnable() => Instance = this;

        void Awake()
        {
            Instance = this;
            _run = GetComponent<RunManager>();
            if (_run == null) _run = FindFirstObjectByType<RunManager>();
            if (pausePanel != null) pausePanel.SetActive(false);
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
        }

        void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            // 클릭음: GameAudio는 씬을 불러올 때 버튼에 달지만, 늦게 생긴 버튼도 놓치지 않게 한 번 더 부탁한다(중복은 GameAudio가 거른다)
            var ga = Pop.Audio.GameAudio.Instance;
            if (ga != null)
            {
                ga.HookButton(resumeButton);
                ga.HookButton(quitButton);
            }
        }

        void OnDisable()
        {
            if (IsPaused) Resume();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!GameInput.PausePressed) return;
            if (IsPaused) Resume();
            else if (CanPause) Pause();
        }

        /// <summary>지금 일시정지할 수 있는가: 한 판 진행 중, 골인 전, 조작 잠금 아님, 얼리기 아님, 시간이 흐르는 중</summary>
        public bool CanPause
        {
            get
            {
                if (IsPaused || DebugFreeze) return false;
                if (Time.timeScale <= 0f) return false;
                if (_run != null && (_run.Cleared || !_run.Running)) return false;
                if (_player == null) _player = FindFirstObjectByType<PlayerController>();
                if (_player != null && _player.ControlLocked) return false;
                return true;
            }
        }

        public void Pause()
        {
            if (!CanPause) return;
            TimeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            IsPaused = true;
            GameInput.GameplayBlocked = true;

            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (pausePanel != null) pausePanel.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            PausedChanged?.Invoke(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            GameInput.GameplayBlocked = false;
            // 일시정지 중에 디버그 얼리기가 켜졌으면 얼린 채로 둔다(얼림을 풀 때 디버그 창이 TimeScaleBeforePause로 되돌린다)
            Time.timeScale = DebugFreeze ? 0f : TimeScaleBeforePause;

            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;

            if (pausePanel != null) pausePanel.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            PausedChanged?.Invoke(false);
        }

        /// <summary>[게임 종료]: 게임을 끝낸다(에디터에서는 Play 멈춤)</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

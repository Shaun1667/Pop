using Pop.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pop.Game
{
    /// <summary>
    /// 한 판(Mountain)의 타이머 · 골인 · 클리어 화면.
    /// 타이머는 씬이 시작될 때(Start) 돌기 시작해서 골인할 때까지 멈추지 않는다(떨어져도 리셋 · 일시정지 없음).
    /// Time.deltaTime으로 세므로 Time.timeScale 0(디버그 순간이동 얼리기)일 때만 멈춘다.
    /// 골인(GoalTrigger) → 타이머 정지 · 플레이어 조작 잠금 · 기록 저장(PlayerPrefs, ClearRecord) · 클리어 화면.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        [Header("씬 이름")]
        public string titleSceneName = "Title";

        [Header("HUD")]
        public Text timerText;
        public Text hudBestText;

        [Header("클리어 화면")]
        public GameObject clearPanel;
        public Text clearTimeText;
        public Text clearBestText;
        public GameObject newRecordLabel;
        public Button retryButton;
        public Button titleButton;

        float _elapsed;
        bool _running;
        bool _cleared;

        /// <summary>이번 판 경과 시간(초)</summary>
        public float Elapsed => _elapsed;
        public bool Running => _running;
        public bool Cleared => _cleared;
        /// <summary>이번 판이 최고 기록이었는가 (골인 뒤)</summary>
        public bool LastWasNewRecord { get; private set; }
        /// <summary>골인했을 때 (Clear 끝, 한 판에 한 번). 소리 등 표시용.</summary>
        public event System.Action RunCleared;

        void OnEnable()
        {
            Instance = this; // OnEnable에서도: Play 중 스크립트를 다시 불러오면 static이 비기 때문
        }

        void Awake()
        {
            Instance = this;
            if (clearPanel != null) clearPanel.SetActive(false);
            if (retryButton != null) retryButton.onClick.AddListener(Retry);
            if (titleButton != null) titleButton.onClick.AddListener(GoTitle);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _elapsed = 0f;
            _running = true;
            _cleared = false;
            if (hudBestText != null) hudBestText.text = "최고 " + ClearRecord.BestText;
            RefreshTimer();
        }

        void Update()
        {
            if (_running) _elapsed += Time.deltaTime;
            RefreshTimer();
        }

        void RefreshTimer()
        {
            if (timerText != null) timerText.text = ClearRecord.Format(_elapsed);
        }

        /// <summary>골인. GoalTrigger가 부른다. 한 판에 한 번만.</summary>
        public void Clear(PlayerController player)
        {
            if (_cleared) return;
            _cleared = true;
            _running = false;
            RefreshTimer();

            if (player != null) player.SetControlLocked(true);

            LastWasNewRecord = ClearRecord.Submit(_elapsed);
            if (clearTimeText != null) clearTimeText.text = "이번 기록  " + ClearRecord.Format(_elapsed);
            if (clearBestText != null) clearBestText.text = "최고 기록  " + ClearRecord.BestText;
            if (newRecordLabel != null) newRecordLabel.SetActive(LastWasNewRecord);
            if (hudBestText != null) hudBestText.text = "최고 " + ClearRecord.BestText;
            if (clearPanel != null) clearPanel.SetActive(true);
            RunCleared?.Invoke();

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Debug.Log($"[Pop] 클리어 {ClearRecord.Format(_elapsed)} (최고 {ClearRecord.BestText}{(LastWasNewRecord ? ", 새 기록" : "")})");
        }

        /// <summary>다시 하기: 지금 씬을 다시 불러온다</summary>
        public void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>타이틀로</summary>
        public void GoTitle()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(titleSceneName);
        }
    }
}

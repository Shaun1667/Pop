using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pop.Game
{
    /// <summary>
    /// 시작 화면: [시작] → Mountain, [종료] → 게임 끝(에디터에서는 Play 멈춤), 최고 기록 표시.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        public string gameSceneName = "Mountain";
        public Button startButton;
        public Button quitButton;
        public Text bestText;

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
        }

        void Start()
        {
            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (bestText != null) bestText.text = "최고 기록  " + ClearRecord.BestText;
        }

        public void StartGame() => SceneManager.LoadScene(gameSceneName);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

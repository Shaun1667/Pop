using UnityEngine;

namespace Pop.Audio
{
    /// <summary>
    /// 게임 소리 설정: 이벤트마다 클립 목록(무작위로 고름) · 볼륨 · 피치 흔들림 · BGM 크로스페이드 · 착지 세기 기준.
    /// 실행 시 GameAudio가 Resources에서 불러온다 (Assets/_Game/Tuning/Resources/GameAudioSettings.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Pop/Game Audio Settings", fileName = "GameAudioSettings")]
    public class GameAudioSettings : ScriptableObject
    {
        [Header("클립 (이벤트마다 무작위 1개, 같은 것 연달아 안 나옴)")]
        public AudioClip[] plungerFire;
        public AudioClip[] plungerAttach;
        public AudioClip[] plungerRelease;
        public AudioClip[] jump;
        public AudioClip[] land;
        public AudioClip[] hitWall;
        [Tooltip("기절 중 반복 (loop)")]
        public AudioClip[] stunLoop;
        public AudioClip[] uiClick;
        [Tooltip("골인 징글")]
        public AudioClip[] jingleClear;
        public AudioClip[] bgmTitle;
        public AudioClip[] bgmClimb;

        [Header("씬 → BGM (이름이 같으면)")]
        public string[] titleScenes = { "Title" };
        public string[] climbScenes = { "Mountain", "Prototype" };

        [Header("볼륨 (0~1)")]
        [Range(0f, 1f)] public float bgmVolume = 0.35f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        [Range(0f, 1f)] public float uiVolume = 0.6f;
        [Range(0f, 1f)] public float jingleVolume = 0.8f;

        [Header("이벤트별 배율 (위 볼륨 × 이 값)")]
        [Range(0f, 2f)] public float fireVolume = 1f;
        [Range(0f, 2f)] public float attachVolume = 1f;
        [Range(0f, 2f)] public float releaseVolume = 1f;
        [Range(0f, 2f)] public float jumpVolume = 0.8f;
        [Range(0f, 2f)] public float landVolume = 1f;
        [Range(0f, 2f)] public float hitWallVolume = 1f;
        [Range(0f, 2f)] public float stunLoopVolume = 0.6f;
        [Range(0f, 2f)] public float uiClickVolume = 1f;

        [Header("피치")]
        [Tooltip("효과음 피치 흔들림 (1 ± 이 값). BGM · 징글 · 기절 반복음에는 안 씀")]
        [Range(0f, 0.3f)] public float pitchVariation = 0.05f;

        [Header("BGM")]
        [Tooltip("씬이 바뀔 때 BGM 크로스페이드 (s)")]
        public float bgmCrossfadeTime = 1.5f;
        [Tooltip("골인 때 BGM을 이 배율까지 줄인다 (0 = 끔)")]
        [Range(0f, 1f)] public float goalBgmVolumeMultiplier = 0f;
        [Tooltip("골인 때 BGM 줄이는 시간 (s)")]
        public float goalBgmFadeTime = 0.5f;

        [Header("기절 반복음")]
        [Tooltip("기절이 풀릴 때 반복음 페이드 아웃 (s)")]
        public float stunLoopFadeOut = 0.15f;

        [Header("착지 (아래로 떨어지던 속도 m/s 기준)")]
        [Tooltip("이 속도보다 느리게 내려앉으면 소리 없음. 0.2 m 턱에서 걸어 내려가기 ≈ 2.4 m/s (중력 ×1.6)")]
        public float landMinImpactSpeed = 2.8f;
        [Tooltip("이 속도 이상이면 최대 볼륨. 평지 점프 착지 ≈ 8 m/s")]
        public float landFullImpactSpeed = 9f;
        [Tooltip("최소 기준 속도에서의 볼륨 배율 (최대까지 선형)")]
        [Range(0f, 1f)] public float landMinVolume = 0.3f;

        [Header("기타")]
        [Tooltip("효과음 AudioSource 수 (동시에 겹쳐 나는 최대)")]
        [Range(4, 16)] public int sfxPoolSize = 10;
        [Tooltip("소리가 날 때마다 콘솔에 [GameAudio] <이벤트> -> <클립> 로그")]
        public bool debugLog = false;
    }
}

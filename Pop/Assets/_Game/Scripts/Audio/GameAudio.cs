using System.Collections.Generic;
using Pop.Game;
using Pop.Player;
using Pop.Plunger;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pop.Audio
{
    /// <summary>
    /// 게임 소리 전부 (BGM · 효과음 · UI · 징글). 씬에 두지 않는다:
    /// Play가 시작될 때(첫 씬을 불러오기 전) Resources/GameAudioSettings를 불러 스스로 만들고 DontDestroyOnLoad로 씬을 넘어 산다.
    /// 씬이 불릴 때마다 그 씬의 PlayerController · PlungerController · RunManager 이벤트를 듣고, 모든 uGUI Button의 onClick에 클릭음을 단다.
    /// 게임 코드는 이벤트만 내고 소리는 모른다. 이동 · 판정 · 튜닝은 건드리지 않는다.
    /// 착지 세기는 FixedUpdate에서 PlayerController 다음에(실행 순서 100) 공중일 때 마지막 아래 속도를 기억해 바닥에 닿는 스텝에 쓴다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        public const string SettingsResourcePath = "GameAudioSettings";
        public static GameAudio Instance { get; private set; }

        public GameAudioSettings settings;

        enum BgmKind { None, Title, Climb }

        class Fader
        {
            public AudioSource src;
            public float target;
            public float speed;      // 볼륨/초. 0이면 바로
            public bool stopAtZero;
        }

        AudioSource[] _sfx;
        int _sfxNext;
        readonly AudioSource[] _bgm = new AudioSource[2];
        readonly Fader[] _bgmFade = new Fader[2];
        int _bgmActive;
        BgmKind _bgmKind = BgmKind.None;
        bool _bgmDucked;
        AudioSource _stun;
        Fader _stunFade;
        AudioSource _jingle;
        Fader _jingleFade;
        readonly List<Fader> _faders = new List<Fader>();

        readonly Dictionary<AudioClip[], int> _lastPick = new Dictionary<AudioClip[], int>();
        readonly HashSet<Button> _hookedButtons = new HashSet<Button>();
        int _handledSceneHandle = -1;

        PlayerController _player;
        PlungerController _plunger;
        RunManager _run;
        bool _wasGrounded = true;
        bool _trackAir;
        float _lastAirVy;

        /// <summary>마지막으로 난 소리 (이벤트, 클립 이름). 확인용</summary>
        public string LastEvent { get; private set; }
        public string LastClipName { get; private set; }
        public AudioClip CurrentBgm => _bgm[_bgmActive] != null && _bgm[_bgmActive].isPlaying ? _bgm[_bgmActive].clip : null;
        public AudioSource ActiveBgmSource => _bgm[_bgmActive];
        public AudioSource StunSource => _stun;
        public AudioSource JingleSource => _jingle;
        public bool StunLoopPlaying => _stun != null && _stun.isPlaying && _stunFade.target > 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var s = Resources.Load<GameAudioSettings>(SettingsResourcePath);
            if (s == null)
            {
                Debug.LogWarning("[GameAudio] Resources/" + SettingsResourcePath + " 없음 — 소리 없이 진행");
                return;
            }
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            var ga = go.AddComponent<GameAudio>();
            ga.Init(s);
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnhookPlayer();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Init(GameAudioSettings s)
        {
            settings = s;
            int n = Mathf.Clamp(s.sfxPoolSize, 4, 16);
            _sfx = new AudioSource[n];
            for (int i = 0; i < n; i++) _sfx[i] = MakeSource("SFX_" + i, false);
            for (int i = 0; i < 2; i++)
            {
                _bgm[i] = MakeSource("BGM_" + i, true);
                _bgmFade[i] = AddFader(_bgm[i]);
            }
            _stun = MakeSource("StunLoop", true);
            _stunFade = AddFader(_stun);
            _jingle = MakeSource("Jingle", false);
            _jingleFade = AddFader(_jingle);
        }

        void Start()
        {
            // 첫 씬: sceneLoaded가 먼저 왔으면 같은 씬이라 건너뛴다
            var scene = SceneManager.GetActiveScene();
            if (scene.isLoaded) HandleScene(scene);
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f; // 2D
            a.dopplerLevel = 0f;
            a.volume = 0f;
            return a;
        }

        Fader AddFader(AudioSource src)
        {
            var f = new Fader { src = src };
            _faders.Add(f);
            return f;
        }

        static void SetFade(Fader f, float target, float time, bool stopAtZero)
        {
            f.target = target;
            f.stopAtZero = stopAtZero;
            float delta = Mathf.Abs(f.src.volume - target);
            f.speed = time > 0.001f ? Mathf.Max(delta, 0.0001f) / time : 0f;
            if (f.speed <= 0f)
            {
                f.src.volume = target;
                if (stopAtZero && target <= 0f) f.src.Stop();
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _faders.Count; i++)
            {
                var f = _faders[i];
                if (f.src == null) continue;
                if (!Mathf.Approximately(f.src.volume, f.target) && f.speed > 0f)
                    f.src.volume = Mathf.MoveTowards(f.src.volume, f.target, f.speed * dt);
                if (f.stopAtZero && f.target <= 0f && f.src.volume <= 0.0001f && f.src.isPlaying)
                    f.src.Stop();
            }

            // 플레이어가 사라졌는데(씬 정리 등) 기절 반복음이 남아 있으면 끈다
            if (_player == null && _stun != null && _stun.isPlaying && _stunFade.target > 0f)
                StopStunLoop(false);
        }

        void FixedUpdate()
        {
            if (_player == null) return;
            bool g = _player.IsGrounded;
            if (g && !_wasGrounded && _trackAir)
                OnLanded(-_lastAirVy);
            if (!g)
            {
                _lastAirVy = _player.Velocity.y; // PlayerController가 이번 스텝에 넣은 속도 = 충돌 직전 속도
                _trackAir = true;
            }
            else _trackAir = false;
            _wasGrounded = g;
        }

        // ---- 씬 ----

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single || scene == SceneManager.GetActiveScene())
                HandleScene(scene);
            else
                HookButtons(scene);
        }

        void HandleScene(Scene scene)
        {
            if (scene.handle == _handledSceneHandle) return;
            _handledSceneHandle = scene.handle;

            StopStunLoop(true);
            if (_jingle.isPlaying) SetFade(_jingleFade, 0f, 0.3f, true);

            EnsureListener();
            UnhookPlayer();
            HookPlayer();
            HookButtons(scene);

            var kind = KindFor(scene.name);
            if (kind != BgmKind.None && (kind != _bgmKind || _bgmDucked || CurrentBgm == null))
                PlayBgm(kind);
        }

        AudioListener _ownListener;

        /// <summary>씬에 켜진 AudioListener가 없으면(Title은 카메라에 없음) 자기 것을 켠다. 있으면 꺼 둔다. 소리는 전부 2D라 위치는 상관없다.</summary>
        void EnsureListener()
        {
            if (_ownListener == null)
            {
                _ownListener = gameObject.AddComponent<AudioListener>();
                _ownListener.enabled = false;
            }
            bool other = false;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l != _ownListener && l.enabled && l.gameObject.activeInHierarchy) { other = true; break; }
            _ownListener.enabled = !other;
        }

        BgmKind KindFor(string sceneName)
        {
            if (settings.titleScenes != null && System.Array.IndexOf(settings.titleScenes, sceneName) >= 0) return BgmKind.Title;
            if (settings.climbScenes != null && System.Array.IndexOf(settings.climbScenes, sceneName) >= 0) return BgmKind.Climb;
            return BgmKind.None;
        }

        void HookPlayer()
        {
            _player = FindFirstObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.Jumped += OnJumped;
                _player.Released += OnReleased;
                _player.StateChanged += OnStateChanged;
                _player.Respawned += OnRespawned;
                _plunger = _player.GetComponent<PlungerController>();
                if (_plunger != null)
                {
                    _plunger.Fired += OnFired;
                    _plunger.HeadPlanted += OnHeadPlanted;
                }
            }
            _wasGrounded = true;
            _trackAir = false;
            _lastAirVy = 0f;

            _run = FindFirstObjectByType<RunManager>();
            if (_run != null) _run.RunCleared += OnRunCleared;
        }

        void UnhookPlayer()
        {
            if (_player != null)
            {
                _player.Jumped -= OnJumped;
                _player.Released -= OnReleased;
                _player.StateChanged -= OnStateChanged;
                _player.Respawned -= OnRespawned;
            }
            if (_plunger != null)
            {
                _plunger.Fired -= OnFired;
                _plunger.HeadPlanted -= OnHeadPlanted;
            }
            if (_run != null) _run.RunCleared -= OnRunCleared;
            _player = null;
            _plunger = null;
            _run = null;
        }

        void HookButtons(Scene scene)
        {
            _hookedButtons.RemoveWhere(b => b == null);
            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in buttons)
            {
                if (b == null || b.gameObject.scene != scene || _hookedButtons.Contains(b)) continue;
                b.onClick.AddListener(PlayUIClick);
                _hookedButtons.Add(b);
            }
        }

        // ---- 이벤트 → 소리 ----

        void OnFired() => PlaySfx("Fire", settings.plungerFire, settings.sfxVolume * settings.fireVolume);
        void OnHeadPlanted() => PlaySfx("Attach", settings.plungerAttach, settings.sfxVolume * settings.attachVolume);
        void OnJumped() => PlaySfx("Jump", settings.jump, settings.sfxVolume * settings.jumpVolume);

        void OnReleased(bool withJump)
        {
            // 클릭 · Space 해제(DoRelease)만: 그때만 상태가 Flying. 접촉 해제 · 낙사 · 순간이동 해제는 소리 없음(접촉 기절은 Hit_Wall)
            if (_player != null && _player.State == PlayerState.Flying)
                PlaySfx(withJump ? "Release(Space)" : "Release", settings.plungerRelease, settings.sfxVolume * settings.releaseVolume);
        }

        void OnStateChanged(PlayerState prev, PlayerState next)
        {
            if (next == PlayerState.Stunned)
            {
                PlaySfx("HitWall", settings.hitWall, settings.sfxVolume * settings.hitWallVolume);
                StartStunLoop();
            }
            else if (prev == PlayerState.Stunned)
            {
                StopStunLoop(false);
            }
        }

        void OnRespawned(Vector3 _)
        {
            StopStunLoop(false);
            _wasGrounded = false;
            _trackAir = false;
            _lastAirVy = 0f;
        }

        void OnLanded(float impactSpeed)
        {
            if (impactSpeed < settings.landMinImpactSpeed) return;
            float range = Mathf.Max(0.01f, settings.landFullImpactSpeed - settings.landMinImpactSpeed);
            float t = Mathf.Clamp01((impactSpeed - settings.landMinImpactSpeed) / range);
            float scale = Mathf.Lerp(settings.landMinVolume, 1f, t);
            PlaySfx("Land(" + impactSpeed.ToString("F1") + " m/s, x" + scale.ToString("F2") + ")",
                settings.land, settings.sfxVolume * settings.landVolume * scale);
        }

        void OnRunCleared()
        {
            if (_bgm[_bgmActive].isPlaying)
                SetFade(_bgmFade[_bgmActive], settings.bgmVolume * settings.goalBgmVolumeMultiplier, settings.goalBgmFadeTime, settings.goalBgmVolumeMultiplier <= 0f);
            _bgmDucked = true;
            var clip = Pick(settings.jingleClear);
            if (clip == null) return;
            _jingle.Stop();
            _jingle.clip = clip;
            _jingle.pitch = 1f;
            SetFade(_jingleFade, settings.jingleVolume, 0f, true);
            _jingle.Play();
            Log("Jingle", clip);
        }

        /// <summary>UI 버튼 클릭음. 씬의 모든 Button onClick에 자동으로 달린다.</summary>
        public void PlayUIClick() => PlaySfx("UIClick", settings.uiClick, settings.uiVolume * settings.uiClickVolume);

        // ---- 재생 ----

        void PlaySfx(string evt, AudioClip[] clips, float volume)
        {
            var clip = Pick(clips);
            if (clip == null || _sfx == null) return;
            var src = NextSfxSource();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            float pv = settings.pitchVariation;
            src.pitch = 1f + Random.Range(-pv, pv);
            src.Play();
            Log(evt, clip);
        }

        AudioSource NextSfxSource()
        {
            for (int i = 0; i < _sfx.Length; i++)
            {
                int k = (_sfxNext + i) % _sfx.Length;
                if (!_sfx[k].isPlaying) { _sfxNext = (k + 1) % _sfx.Length; return _sfx[k]; }
            }
            var s = _sfx[_sfxNext]; // 전부 재생 중이면 가장 오래된 차례의 것을 빼앗는다
            _sfxNext = (_sfxNext + 1) % _sfx.Length;
            return s;
        }

        void StartStunLoop()
        {
            var clip = Pick(settings.stunLoop);
            if (clip == null) return;
            _stun.Stop();
            _stun.clip = clip;
            _stun.loop = true;
            _stun.pitch = 1f;
            SetFade(_stunFade, settings.sfxVolume * settings.stunLoopVolume, 0f, true);
            _stun.Play();
            Log("StunLoop start", clip);
        }

        void StopStunLoop(bool immediate)
        {
            if (_stun == null) return;
            bool was = _stun.isPlaying && _stunFade.target > 0f;
            SetFade(_stunFade, 0f, immediate ? 0f : settings.stunLoopFadeOut, true);
            if (was && settings.debugLog) Debug.Log("[GameAudio] StunLoop stop");
        }

        void PlayBgm(BgmKind kind)
        {
            var clips = kind == BgmKind.Title ? settings.bgmTitle : settings.bgmClimb;
            var clip = Pick(clips);
            if (clip == null) return;
            int next = 1 - _bgmActive;
            var src = _bgm[next];
            src.Stop();
            src.clip = clip;
            src.loop = true;
            src.pitch = 1f;
            src.volume = 0f;
            src.Play();
            SetFade(_bgmFade[next], settings.bgmVolume, settings.bgmCrossfadeTime, true);
            SetFade(_bgmFade[_bgmActive], 0f, settings.bgmCrossfadeTime, true);
            _bgmActive = next;
            _bgmKind = kind;
            _bgmDucked = false;
            Log("BGM " + kind, clip);
        }

        /// <summary>무작위 1개. 2개 이상이면 바로 전과 같은 것은 고르지 않는다.</summary>
        AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            int count = clips.Length;
            int idx;
            if (count == 1) idx = 0;
            else
            {
                int last = _lastPick.TryGetValue(clips, out var l) ? l : -1;
                idx = Random.Range(0, last >= 0 ? count - 1 : count);
                if (last >= 0 && idx >= last) idx++;
            }
            _lastPick[clips] = idx;
            return clips[idx];
        }

        void Log(string evt, AudioClip clip)
        {
            LastEvent = evt;
            LastClipName = clip != null ? clip.name : null;
            if (settings != null && settings.debugLog)
                Debug.Log("[GameAudio] " + evt + " -> " + LastClipName);
        }
    }
}

using UnityEngine;

namespace Pop.Player
{
    /// <summary>
    /// 기절 표시(임시). Stunned 동안 머리 위에서 도는 노란 별(구, 콜라이더 없음) + 플레이어 색을 어둡게.
    /// 착지로 풀리는 순간 별이 바깥으로 튀며 사라진다 (번쩍임 없음, 색은 바로 원래대로).
    /// 낙사 등 다른 이유로 풀리면 바로 지운다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerStunVisual : MonoBehaviour
    {
        public Color starColor = new Color(1f, 0.88f, 0.1f, 1f);
        [Range(1, 8)] public int starCount = 3;
        public float starSize = 0.16f;
        public float orbitRadius = 0.38f;
        [Tooltip("머리 꼭대기에서 별 고리까지 높이 (m)")]
        public float heightAboveHead = 0.25f;
        [Tooltip("별 회전 속도 (도/초)")]
        public float spinSpeed = 300f;
        [Tooltip("기절 중 플레이어 색 배율 (1 = 그대로)")]
        [Range(0f, 1f)] public float darken = 0.6f;
        [Tooltip("착지로 풀릴 때 별이 튀는 시간 (s). 0이면 효과 없음")]
        public float recoverPunchTime = 0.25f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        PlayerController _player;
        Collider _col;
        Renderer[] _renderers;
        Color[] _baseColors;
        MaterialPropertyBlock _block;

        GameObject _root;
        Transform[] _stars;
        Material _starMat;

        bool _stunned;
        float _punch;
        float _angle;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _col = GetComponent<Collider>();
            _block = new MaterialPropertyBlock();

            // 플레이어 자신의 렌더러만 (자식이 있으면 함께)
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i].sharedMaterial;
                _baseColors[i] = m == null ? Color.white
                    : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                    : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }

            BuildStars();
        }

        void OnEnable()
        {
            if (_player != null) _player.StateChanged += OnStateChanged;
            if (_player != null && _player.State == PlayerState.Stunned) SetStunned(true);
        }

        void OnDisable()
        {
            if (_player != null) _player.StateChanged -= OnStateChanged;
            _punch = 0f;
            SetStunned(false);
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root);
            if (_starMat != null) Destroy(_starMat);
        }

        void OnStateChanged(PlayerState prev, PlayerState next)
        {
            if (next == PlayerState.Stunned)
            {
                _punch = 0f;
                SetStunned(true);
            }
            else if (prev == PlayerState.Stunned)
            {
                SetStunned(false);
                // 착지로 풀렸을 때만 짧게 티 낸다 (낙사 등은 바로 지움)
                _punch = next == PlayerState.Normal ? recoverPunchTime : 0f;
                if (_punch > 0f) _root.SetActive(true);
            }
        }

        void SetStunned(bool on)
        {
            _stunned = on;
            if (_root != null) _root.SetActive(on);
            ApplyTint(on ? darken : 1f, 0f);
        }

        void LateUpdate()
        {
            if (!_stunned && _punch <= 0f) return;

            float dt = Time.deltaTime;
            _angle += spinSpeed * dt;

            float radius = orbitRadius;
            float size = starSize;
            if (!_stunned)
            {
                // 풀림 효과: 별이 바깥으로 퍼지며 작아진다. 플레이어 색은 SetStunned(false)에서 이미 원래대로.
                _punch -= dt;
                float k = recoverPunchTime > 0f ? Mathf.Clamp01(1f - _punch / recoverPunchTime) : 1f;
                radius = orbitRadius * (1f + 1.8f * k);
                size = starSize * (1f - k);
                if (_punch <= 0f)
                {
                    _punch = 0f;
                    _root.SetActive(false);
                    ApplyTint(1f, 0f);
                    return;
                }
            }

            float top = _col != null ? _col.bounds.max.y : transform.position.y + 0.6f;
            _root.transform.position = new Vector3(transform.position.x, top + heightAboveHead, transform.position.z);
            for (int i = 0; i < _stars.Length; i++)
            {
                float a = (_angle + 360f * i / _stars.Length) * Mathf.Deg2Rad;
                // 측면 시점이라 고리를 살짝 기울여(앞뒤 → 위아래) 도는 게 보이게 한다
                _stars[i].localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.35f, Mathf.Sin(a) * radius);
                _stars[i].localScale = Vector3.one * Mathf.Max(0.001f, size);
            }
        }

        void ApplyTint(float mul, float flash)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                if (mul == 1f && flash <= 0f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                Color c = _baseColors[i];
                Color t = new Color(c.r * mul, c.g * mul, c.b * mul, c.a);
                t = Color.Lerp(t, new Color(1f, 1f, 1f, c.a), flash);
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, t);
                _block.SetColor(ColorId, t);
                r.SetPropertyBlock(_block);
            }
        }

        void BuildStars()
        {
            _root = new GameObject("StunStars (runtime)");
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _starMat = new Material(shader) { name = "StunStar (runtime)" };
            if (_starMat.HasProperty(BaseColorId)) _starMat.SetColor(BaseColorId, starColor);
            _starMat.color = starColor;

            _stars = new Transform[starCount];
            for (int i = 0; i < starCount; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "Star" + i;
                var col = s.GetComponent<Collider>();
                col.enabled = false;
                Destroy(col);
                s.transform.SetParent(_root.transform, false);
                s.transform.localScale = Vector3.one * starSize;
                var mr = s.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _starMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _stars[i] = s.transform;
            }
            _root.SetActive(false);
        }
    }
}

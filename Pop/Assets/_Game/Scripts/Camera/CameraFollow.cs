using Pop.Input;
using Pop.Player;
using UnityEngine;

namespace Pop.CameraSystem
{
    /// <summary>
    /// 측면 고정 시점 카메라. 회전 없이 -Z 쪽에서 +Z를 바라보며 대상을 SmoothDamp로 따라간다.
    /// W/S를 누르는 동안 시점이 위/아래로 lookDistance까지 움직이고, 떼면 부드럽게 원위치.
    /// 대상이 PlayerController면 리스폰(순간이동) 때 끌려가지 않고 바로 붙는다(SnapToTarget).
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public CameraTuning tuning;

        Vector3 _followVelocity;
        float _lookOffset;
        float _lookReturnVelocity;
        PlayerController _player;
        int _snapFrames;

        /// <summary>현재 W/S 시점 오프셋 (m)</summary>
        public float LookOffset => _lookOffset;

        void Start()
        {
            transform.rotation = Quaternion.identity;
            SnapToTarget();
            HookPlayer();
        }

        void OnDestroy()
        {
            if (_player != null) _player.Respawned -= OnPlayerRespawned;
        }

        void HookPlayer()
        {
            if (_player != null) _player.Respawned -= OnPlayerRespawned;
            _player = target != null ? target.GetComponent<PlayerController>() : null;
            if (_player != null) _player.Respawned += OnPlayerRespawned;
        }

        void OnPlayerRespawned(Vector3 _)
        {
            // 이번 프레임과 다음 프레임(보간이 자리 잡을 때까지) 바로 붙인다.
            _snapFrames = 2;
            SnapToTarget();
        }

        /// <summary>보간 없이 대상 위치로 바로 이동 (순간이동·리스폰용).</summary>
        public void SnapToTarget()
        {
            if (target == null || tuning == null) return;
            _followVelocity = Vector3.zero;
            _appliedLook = _lookOffset;
            transform.position = DesiredPosition();
        }

        void LateUpdate()
        {
            if (target == null || tuning == null) return;

            float dt = Time.deltaTime;
            float look = GameInput.LookY;
            if (Mathf.Abs(look) > 0.01f)
            {
                _lookReturnVelocity = 0f;
                _lookOffset = Mathf.MoveTowards(_lookOffset, look * tuning.lookDistance, tuning.lookSpeed * dt);
            }
            else
            {
                _lookOffset = Mathf.SmoothDamp(_lookOffset, 0f, ref _lookReturnVelocity, tuning.lookReturnSmoothTime);
            }

            if (_snapFrames > 0)
            {
                _snapFrames--;
                SnapToTarget();
                return;
            }

            // 추적은 SmoothDamp, W/S 오프셋은 그 위에 바로 더해 입력 반응을 유지한다.
            Vector3 followPos = target.position + new Vector3(0f, tuning.offsetY, -tuning.distance);
            Vector3 current = transform.position - new Vector3(0f, _appliedLook, 0f);
            Vector3 smoothed = Vector3.SmoothDamp(current, followPos, ref _followVelocity, tuning.followSmoothTime);
            _appliedLook = _lookOffset;
            transform.SetPositionAndRotation(smoothed + new Vector3(0f, _appliedLook, 0f), Quaternion.identity);
        }

        float _appliedLook;

        Vector3 DesiredPosition()
        {
            return target.position + new Vector3(0f, tuning.offsetY + _lookOffset, -tuning.distance);
        }
    }
}

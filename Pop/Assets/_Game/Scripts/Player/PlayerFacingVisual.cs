using UnityEngine;

namespace Pop.Player
{
    /// <summary>
    /// 보이는 모델만 좌우로 돌린다 (이동 · 물리에는 손대지 않는다).
    /// A/D 입력(PlayerController.MoveInput) 쪽을 보고, 입력이 없으면 마지막 방향을 유지한다.
    /// 기절 중에는 입력이 막히므로 속도 X 쪽을 본다.
    /// 모델은 +Z가 정면이라고 가정하고, 측면 시점에서 얼굴이 보이게 카메라 쪽(-Z)으로 cameraTurn만큼 더 돌린다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerFacingVisual : MonoBehaviour
    {
        [Tooltip("돌릴 시각용 자식 (예: Visual)")]
        public Transform visual;
        [Tooltip("시작 방향: +1 = 오른쪽(+X), -1 = 왼쪽")]
        public int startFacing = 1;
        [Tooltip("옆모습에서 카메라 쪽으로 더 돌리는 각도 (도)")]
        [Range(0f, 60f)] public float cameraTurn = 20f;
        [Tooltip("돌아서는 속도 (도/초)")]
        public float turnSpeed = 900f;
        [Tooltip("입력이 없을 때 이 속도(m/s)보다 빠르면 속도 방향을 본다 (기절 · 사망 중에만)")]
        public float velocityThreshold = 0.5f;

        PlayerController _player;
        int _facing;

        public int Facing => _facing;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _facing = startFacing >= 0 ? 1 : -1;
            if (visual != null) visual.localRotation = Target();
        }

        void LateUpdate()
        {
            if (visual == null || _player == null) return;

            var st = _player.State;
            if (st == PlayerState.Stunned || st == PlayerState.Dead)
            {
                float vx = _player.Velocity.x;
                if (Mathf.Abs(vx) > velocityThreshold) _facing = vx > 0f ? 1 : -1;
            }
            else
            {
                float inp = _player.MoveInput;
                if (Mathf.Abs(inp) > 0.1f) _facing = inp > 0f ? 1 : -1;
            }

            visual.localRotation = Quaternion.RotateTowards(visual.localRotation, Target(), turnSpeed * Time.deltaTime);
        }

        Quaternion Target() => Quaternion.Euler(0f, _facing > 0 ? 90f + cameraTurn : -90f - cameraTurn, 0f);
    }
}

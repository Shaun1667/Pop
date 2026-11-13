using UnityEngine;

namespace Pop.Player
{
    /// <summary>
    /// 낙사 구역. 트리거 콜라이더에 플레이어가 들어오면 PlayerController.Kill() → 즉시 리스폰.
    /// 콜라이더는 isTrigger로 강제한다(뚫어뻥 레이·접촉 해제 판정에 걸리지 않게).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class KillZone : MonoBehaviour
    {
        void Reset() => MakeTrigger();
        void Awake() => MakeTrigger();

        void MakeTrigger()
        {
            foreach (var c in GetComponents<Collider>()) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other) => TryKill(other);
        void OnTriggerStay(Collider other) => TryKill(other);

        static void TryKill(Collider other)
        {
            var rb = other.attachedRigidbody;
            var player = rb != null ? rb.GetComponent<PlayerController>() : other.GetComponentInParent<PlayerController>();
            if (player != null) player.Kill();
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}

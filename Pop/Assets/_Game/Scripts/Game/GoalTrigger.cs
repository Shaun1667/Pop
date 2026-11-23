using Pop.Player;
using UnityEngine;

namespace Pop.Game
{
    /// <summary>
    /// 정상 도토리 나무 앞의 골인 트리거. 플레이어가 들어오면 RunManager.Clear()를 부른다(한 번만).
    /// 상자는 정상 발판 윗면 바로 위 얇은 층이라, 정상 발판에 실제로 서거나 내려앉아야 닿는다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalTrigger : MonoBehaviour
    {
        bool _done;

        void Reset()
        {
            var b = GetComponent<BoxCollider>();
            b.isTrigger = true;
        }

        void OnTriggerEnter(Collider other) => Check(other);
        void OnTriggerStay(Collider other) => Check(other);

        void Check(Collider other)
        {
            if (_done) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            var run = RunManager.Instance != null ? RunManager.Instance : FindFirstObjectByType<RunManager>();
            if (run == null) return;
            _done = true;
            run.Clear(p);
        }

        void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider>();
            if (b == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.25f);
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}

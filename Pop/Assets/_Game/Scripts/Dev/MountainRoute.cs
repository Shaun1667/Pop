using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pop.Dev
{
    public enum RouteStepKind
    {
        /// <summary>바닥에 선 채 dir 쪽으로 달리다 jumpX에서 점프</summary>
        RunJump,
        /// <summary>공중에서 올라가다 꼭짓점(vy ≤ 0)에서 point로 발사·부착 → releaseAngle에서 Space 해제</summary>
        Attach,
        /// <summary>공중에서 dir을 누른 채 착지할 때까지</summary>
        Land
    }

    [Serializable]
    public class RouteStep
    {
        public RouteStepKind kind;
        public string label;
        [Tooltip("누르고 있는 방향 (+1 = D, -1 = A)")]
        public int dir = 1;
        [Tooltip("Attach: 조준 지점(부착 면 위)")]
        public Vector3 point;
        [Tooltip("Attach: 줄이 수직에서 dir 쪽으로 이 각도(도)를 넘고 올라가는 중이면 Space 해제")]
        public float releaseAngle;
        [Tooltip("Attach: 줄에 매달린 동안 dir 키를 누르는 정도 (1 = 누름, 0 = 안 누름)")]
        public float swingInput = 1f;
        [Tooltip("RunJump: 이 x를 지나면 점프")]
        public float jumpX;
        [Tooltip("RunJump: 이 단계부터 시험할 때 세워 둘 위치 (발판 위)")]
        public Vector3 standPos;
        [Tooltip("설계 시뮬레이션에서 부착 순간 거리 (m)")]
        public float simAttachDistance;
        [Tooltip("설계 시뮬레이션에서 부착 가능(≤ 사거리) 시간 창 (s)")]
        public float simAttachWindow;
    }

    /// <summary>
    /// 개발용: Mountain 맵을 만들 때 설계 시뮬레이션이 남긴 정답 경로. RouteBot이 이대로 플레이해 맵을 검증한다.
    /// 게임 진행에는 쓰지 않는다.
    /// </summary>
    public class MountainRoute : MonoBehaviour
    {
        public List<RouteStep> steps = new List<RouteStep>();
    }
}

using UnityEngine;

namespace Pop.Plunger
{
    /// <summary>
    /// 뚫어뻥이 붙을 수 있는 면 표시. 맞은 콜라이더 또는 그 부모에 있으면 부착 가능.
    /// 컴포넌트가 없거나 꺼져 있으면 부착 불가 면.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlungerAttachable : MonoBehaviour
    {
    }
}

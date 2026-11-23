using UnityEngine;
using UnityEngine.UI;

namespace Pop.Game
{
    /// <summary>
    /// 이 오브젝트 아래 모든 UI Text에 한글이 나오는 OS 글꼴(맑은 고딕 등)을 실행 시 입힌다.
    /// 글꼴 파일을 프로젝트에 넣지 않으려고 OS 글꼴을 쓴다. 못 찾으면 원래 글꼴 그대로.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class KoreanUIFont : MonoBehaviour
    {
        public string[] osFontNames = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial" };

        static Font _font;

        void Awake()
        {
            if (_font == null)
            {
                try { _font = Font.CreateDynamicFontFromOSFont(osFontNames, 32); }
                catch { _font = null; }
            }
            if (_font == null) return;
            foreach (var t in GetComponentsInChildren<Text>(true)) t.font = _font;
        }
    }
}

using UnityEngine;

namespace Pop.Rendering
{
    /// <summary>
    /// Pop/Toon Character 셰이더 값 한 벌. 머티리얼 값은 여기서만 고치고
    /// Pop/Dev/Squirrel/Apply Toon Material(또는 1 Setup Mixamo Assets)로 머티리얼에 넣는다.
    /// useToon을 끄고 적용하면 머티리얼이 URP Lit으로 돌아간다(텍스처는 그대로).
    /// </summary>
    [CreateAssetMenu(menuName = "Pop/Toon Character Settings", fileName = "ToonCharacterSettings")]
    public class ToonCharacterSettings : ScriptableObject
    {
        public const string ShaderName = "Pop/Toon Character";

        [Tooltip("끄면 URP Lit으로 되돌린다")]
        public bool useToon = true;

        [Header("기본")]
        [Range(0f, 2f)] public float normalStrength = 0.4f;
        [Tooltip("Back = 보통. 비막이 한쪽 면뿐이라 뒤에서 안 보이면 Off")]
        public UnityEngine.Rendering.CullMode cull = UnityEngine.Rendering.CullMode.Back;

        [Header("램프 (부드러운 2단)")]
        [Tooltip("half-lambert(0~1) 기준 밝은 면 / 그늘 면 경계")]
        [Range(0f, 1f)] public float shadeEdge = 0.5f;
        [Tooltip("경계 흐림 폭. 작을수록 딱딱한 셀")]
        [Range(0.005f, 0.5f)] public float shadeSoftness = 0.08f;
        [Tooltip("그늘 면 색 배율")]
        public Color shadeColor = new Color(0.7f, 0.64f, 0.72f, 1f);
        [Range(0f, 2f)] public float lightStrength = 1f;
        [Range(0f, 2f)] public float ambientStrength = 0.6f;

        [Header("림 라이트")]
        public Color rimColor = new Color(1f, 0.92f, 0.76f, 1f);
        [Range(0f, 2f)] public float rimStrength = 0.55f;
        [Tooltip("1 - N·V 가 이 값을 넘으면 림")]
        [Range(0f, 1f)] public float rimEdge = 0.62f;
        [Range(0.005f, 0.5f)] public float rimSoftness = 0.12f;
        [Tooltip("0 = 고르게, 1 = 빛 쪽만")]
        [Range(0f, 1f)] public float rimLitSide = 0.35f;

        [Header("아웃라인 (화면 고정 두께)")]
        public Color outlineColor = new Color(0.16f, 0.11f, 0.09f, 1f);
        [Tooltip("0 = outlineColor, 1 = 알베도를 어둡게 한 색")]
        [Range(0f, 1f)] public float outlineAlbedoMix = 0.45f;
        [Range(0f, 1f)] public float outlineAlbedoDarken = 0.35f;
        [Tooltip("1080p 기준 픽셀. 해상도에 비례해 커진다")]
        [Range(0f, 8f)] public float outlineWidthPx = 1.6f;
        [Tooltip("아웃라인 정점을 카메라 반대쪽으로 미는 거리 (m). 얇은 비막 · 챙에서 뚫고 나오는 것을 막는다")]
        [Range(0f, 0.3f)] public float outlineDepthPush = 0.04f;

        /// <summary>머티리얼에 값을 넣는다. 셰이더는 바꾸지 않는다(에디터 코드가 바꿈).</summary>
        public void ApplyTo(Material m)
        {
            if (m == null) return;
            m.SetFloat("_BumpScale", normalStrength);
            m.SetFloat("_Cull", (float)cull);
            m.SetFloat("_ShadeEdge", shadeEdge);
            m.SetFloat("_ShadeSoftness", shadeSoftness);
            m.SetColor("_ShadeColor", shadeColor);
            m.SetFloat("_LightStrength", lightStrength);
            m.SetFloat("_AmbientStrength", ambientStrength);
            m.SetColor("_RimColor", rimColor);
            m.SetFloat("_RimStrength", rimStrength);
            m.SetFloat("_RimEdge", rimEdge);
            m.SetFloat("_RimSoftness", rimSoftness);
            m.SetFloat("_RimLitSide", rimLitSide);
            m.SetColor("_OutlineColor", outlineColor);
            m.SetFloat("_OutlineAlbedoMix", outlineAlbedoMix);
            m.SetFloat("_OutlineAlbedoDarken", outlineAlbedoDarken);
            m.SetFloat("_OutlineWidth", outlineWidthPx);
            m.SetFloat("_OutlineDepthPush", outlineDepthPush);
        }
    }
}

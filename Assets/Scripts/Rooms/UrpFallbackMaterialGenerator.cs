using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using UMaterial = UnityEngine.Material;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// Custom GLTFast material generator using Universal Render Pipeline/Lit.
    ///
    /// GLTFast's Shader Graphs ("Shader Graphs/glTF-pbrMetallicRoughness") are stripped
    /// from Android release builds because Unity can't track their dynamic usage.
    /// URP/Lit is always compiled into URP builds, so this generator always produces
    /// visible PBR materials with correct colors and textures.
    /// </summary>
    public sealed class UrpFallbackMaterialGenerator : IMaterialGenerator
    {
        static readonly int BaseMap     = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor   = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic    = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness  = Shader.PropertyToID("_Smoothness");
        static readonly int Surface     = Shader.PropertyToID("_Surface");
        static readonly int AlphaClipProp = Shader.PropertyToID("_AlphaClip");
        static readonly int Cutoff      = Shader.PropertyToID("_Cutoff");

        static Shader s_Shader;
        static Shader UrpLit =>
            s_Shader != null ? s_Shader : (s_Shader = Shader.Find("Universal Render Pipeline/Lit"));

        public void SetLogger(ICodeLogger logger) { }

        public UMaterial GetDefaultMaterial(bool pointsSupport = false)
            => new UMaterial(UrpLit) { name = "GLTFast_Default" };

        public UMaterial GenerateMaterial(
            MaterialBase gltfMaterial,
            IGltfReadable gltf,
            bool pointsSupport = false)
        {
            var mat = new UMaterial(UrpLit);
            if (gltfMaterial == null) return mat;

            mat.name = string.IsNullOrWhiteSpace(gltfMaterial.name)
                ? "GLTFast_Material"
                : gltfMaterial.name;

            var pbr = gltfMaterial.PbrMetallicRoughness as PbrMetallicRoughnessBase;

            if (pbr != null)
            {
                // Base color (linear RGBA)
                mat.SetColor(BaseColor, pbr.BaseColor);

                // Base color texture
                var baseTexInfo = pbr.BaseColorTexture;
                if (baseTexInfo != null)
                {
                    var tex = gltf.GetTexture(baseTexInfo.index);
                    if (tex != null) mat.SetTexture(BaseMap, tex);
                }

                // Metallic / roughness (roughness → 1-smoothness)
                mat.SetFloat(Metallic,   pbr.metallicFactor);
                mat.SetFloat(Smoothness, 1f - pbr.roughnessFactor);
            }

            // Alpha mode
            switch (gltfMaterial.GetAlphaMode())
            {
                case MaterialBase.AlphaMode.Blend:
                    mat.SetFloat(Surface, 1f);
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    mat.SetOverrideTag("RenderType", "Transparent");
                    break;
                case MaterialBase.AlphaMode.Mask:
                    mat.SetFloat(AlphaClipProp, 1f);
                    mat.SetFloat(Cutoff, gltfMaterial.alphaCutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    break;
            }

            return mat;
        }
    }
}

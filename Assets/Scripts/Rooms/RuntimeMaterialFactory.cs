using UnityEngine;
using UnityEngine.Rendering;

namespace Room2Scan.Rooms
{
    internal static class RuntimeMaterialFactory
    {
        private const string SolidColorShaderName = "Room2Scan/SolidColor";
        private const string TexturedShaderName = "Room2Scan/TexturedUnlit";

        private static Shader cachedSolidColorShader;
        private static Shader cachedTexturedShader;
        private static bool loggedMissingShader;
        private static bool loggedMissingTexturedShader;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int GltfBaseColorTexture = Shader.PropertyToID("baseColorTexture");
        private static readonly int GltfBaseColorFactor = Shader.PropertyToID("baseColorFactor");
        private static readonly int GltfDiffuseTexture = Shader.PropertyToID("diffuseTexture");
        private static readonly int GltfDiffuseFactor = Shader.PropertyToID("diffuseFactor");

        public readonly struct MaterialNormalizationResult
        {
            public MaterialNormalizationResult(int convertedTexturedMaterials, int replacedUnsupportedMaterials)
            {
                ConvertedTexturedMaterials = convertedTexturedMaterials;
                ReplacedUnsupportedMaterials = replacedUnsupportedMaterials;
            }

            public int ConvertedTexturedMaterials { get; }
            public int ReplacedUnsupportedMaterials { get; }
            public int ChangedMaterials => ConvertedTexturedMaterials + ReplacedUnsupportedMaterials;
        }

        public static Material CreateSolidColorMaterial(string name, Color color)
        {
            var shader = ResolveSolidColorShader();
            if (shader == null)
            {
                if (!loggedMissingShader)
                {
                    Debug.LogError("Room2Scan runtime material shader was not found. Check GraphicsSettings always-included shaders.");
                    loggedMissingShader = true;
                }
                return null;
            }

            var material = new Material(shader) { name = name };
            SetColor(material, color);
            return material;
        }

        public static void SetColor(Material material, Color color)
        {
            if (material == null) return;

            if (material.HasProperty(BaseColor))
                material.SetColor(BaseColor, color);
            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, color);
        }

        public static int ReplaceUnsupportedMaterials(GameObject root, Color fallbackColor)
        {
            return NormalizeLoadedMaterials(root, fallbackColor).ReplacedUnsupportedMaterials;
        }

        public static MaterialNormalizationResult NormalizeLoadedMaterials(GameObject root, Color fallbackColor)
        {
            return NormalizeLoadedMaterials(root, fallbackColor, default);
        }

        public static MaterialNormalizationResult NormalizeLoadedMaterials(
            GameObject root,
            Color fallbackColor,
            GltfBaseColorTextureExtractor.MaterialTextureData extractedTexture)
        {
            if (root == null) return new MaterialNormalizationResult(0, 0);

            var convertedTextured = 0;
            var replacedUnsupported = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (TryCreateTexturedMaterial(material, out var texturedMaterial))
                    {
                        materials[i] = texturedMaterial;
                        convertedTextured++;
                        changed = true;
                        continue;
                    }

                    if (!ShouldReplace(material)) continue;

                    if (extractedTexture.HasTexture
                        && TryCreateTexturedMaterial(material, extractedTexture, out texturedMaterial))
                    {
                        materials[i] = texturedMaterial;
                        convertedTextured++;
                        changed = true;
                        continue;
                    }

                    materials[i] = CreateSolidColorMaterial($"{renderer.name}_Fallback", fallbackColor);
                    replacedUnsupported++;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            return new MaterialNormalizationResult(convertedTextured, replacedUnsupported);
        }

        private static Shader ResolveSolidColorShader()
        {
            if (cachedSolidColorShader != null) return cachedSolidColorShader;

            cachedSolidColorShader =
                Shader.Find(SolidColorShaderName)
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? ResolveBuiltInShaderFallback();

            return cachedSolidColorShader;
        }

        private static Shader ResolveTexturedShader()
        {
            if (cachedTexturedShader != null) return cachedTexturedShader;

            cachedTexturedShader =
                Shader.Find(TexturedShaderName)
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture");

            if (cachedTexturedShader == null && !loggedMissingTexturedShader)
            {
                Debug.LogError("Room2Scan textured runtime material shader was not found. Check GraphicsSettings always-included shaders.");
                loggedMissingTexturedShader = true;
            }

            return cachedTexturedShader;
        }

        private static Shader ResolveBuiltInShaderFallback()
        {
            return GraphicsSettings.currentRenderPipeline == null
                ? Shader.Find("Standard")
                : null;
        }

        private static bool TryCreateTexturedMaterial(Material source, out Material converted)
        {
            converted = null;
            if (source == null || source.shader == null) return false;
            if (source.shader.name == TexturedShaderName) return false;
            if (!TryGetBaseTexture(source, out var texture, out var textureProperty)) return false;

            var shader = ResolveTexturedShader();
            if (shader == null) return false;

            converted = new Material(shader)
            {
                name = string.IsNullOrWhiteSpace(source.name)
                    ? "Room2Scan_TexturedMaterial"
                    : $"{source.name}_Room2ScanTextured",
                renderQueue = source.renderQueue
            };

            if (converted.HasProperty(BaseMap))
            {
                converted.SetTexture(BaseMap, texture);
                converted.SetTextureScale(BaseMap, ResolveTextureScale(source, textureProperty));
                converted.SetTextureOffset(BaseMap, ResolveTextureOffset(source, textureProperty));
            }

            if (converted.HasProperty(MainTex))
            {
                converted.SetTexture(MainTex, texture);
                converted.SetTextureScale(MainTex, ResolveTextureScale(source, textureProperty));
                converted.SetTextureOffset(MainTex, ResolveTextureOffset(source, textureProperty));
            }

            var baseColor = TryGetColor(source, GltfBaseColorFactor, out var gltfBaseColor)
                ? gltfBaseColor
                : TryGetColor(source, GltfDiffuseFactor, out var diffuseColor)
                    ? diffuseColor
                    : TryGetColor(source, BaseColor, out var urpBaseColor)
                        ? urpBaseColor
                        : TryGetColor(source, ColorProperty, out var color)
                            ? color
                            : Color.white;

            SetColor(converted, baseColor);
            return true;
        }

        private static bool TryCreateTexturedMaterial(
            Material source,
            GltfBaseColorTextureExtractor.MaterialTextureData extractedTexture,
            out Material converted)
        {
            converted = null;
            if (!extractedTexture.HasTexture) return false;

            var shader = ResolveTexturedShader();
            if (shader == null) return false;

            var materialName = source != null && !string.IsNullOrWhiteSpace(source.name)
                ? $"{source.name}_Room2ScanExtractedTexture"
                : "Room2Scan_ExtractedTexture";

            converted = new Material(shader)
            {
                name = materialName,
                renderQueue = source != null ? source.renderQueue : -1
            };

            if (converted.HasProperty(BaseMap))
                converted.SetTexture(BaseMap, extractedTexture.Texture);
            if (converted.HasProperty(MainTex))
                converted.SetTexture(MainTex, extractedTexture.Texture);

            SetColor(converted, extractedTexture.BaseColor);
            return true;
        }

        private static bool TryGetBaseTexture(Material material, out Texture texture, out int textureProperty)
        {
            if (TryGetTexture(material, GltfBaseColorTexture, out texture))
            {
                textureProperty = GltfBaseColorTexture;
                return true;
            }

            if (TryGetTexture(material, GltfDiffuseTexture, out texture))
            {
                textureProperty = GltfDiffuseTexture;
                return true;
            }

            if (TryGetTexture(material, BaseMap, out texture))
            {
                textureProperty = BaseMap;
                return true;
            }

            if (TryGetTexture(material, MainTex, out texture))
            {
                textureProperty = MainTex;
                return true;
            }

            try
            {
                texture = material.mainTexture;
                if (texture != null)
                {
                    textureProperty = MainTex;
                    return true;
                }
            }
            catch
            {
                // Some shader graphs do not expose a conventional main texture.
            }

            textureProperty = 0;
            texture = null;
            return false;
        }

        private static bool TryGetTexture(Material material, int propertyId, out Texture texture)
        {
            texture = null;
            if (!material.HasProperty(propertyId)) return false;
            texture = material.GetTexture(propertyId);
            return texture != null;
        }

        private static bool TryGetColor(Material material, int propertyId, out Color color)
        {
            color = Color.white;
            if (!material.HasProperty(propertyId)) return false;
            color = material.GetColor(propertyId);
            return true;
        }

        private static Vector2 ResolveTextureScale(Material material, int textureProperty)
        {
            try
            {
                return material.HasProperty(textureProperty)
                    ? material.GetTextureScale(textureProperty)
                    : material.mainTextureScale;
            }
            catch
            {
                return Vector2.one;
            }
        }

        private static Vector2 ResolveTextureOffset(Material material, int textureProperty)
        {
            try
            {
                return material.HasProperty(textureProperty)
                    ? material.GetTextureOffset(textureProperty)
                    : material.mainTextureOffset;
            }
            catch
            {
                return Vector2.zero;
            }
        }

        private static bool ShouldReplace(Material material)
        {
            if (material == null || material.shader == null) return true;

            var shaderName = material.shader.name;

            // Always replace broken/error shaders
            if (shaderName.Contains("Error") || shaderName.Contains("FallbackError"))
                return true;

            // GLTFast-generated shaders (glTF/*, Shader Graphs/glTF-*) are valid — do NOT replace.
            // Replacing them strips the PBR textures and produces flat solid colors.
            if (shaderName.StartsWith("glTF/") || shaderName.StartsWith("Shader Graphs/glTF"))
                return false;

            // Built-in pipeline: "Standard" works fine — nothing to replace.
            if (GraphicsSettings.currentRenderPipeline == null)
                return false;

            // URP: only replace the legacy "Standard" shader (not supported under URP).
            return shaderName == "Standard";
        }
    }
}

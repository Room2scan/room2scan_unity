using UnityEngine;
using UnityEngine.Rendering;

namespace Room2Scan.Rooms
{
    internal static class RuntimeMaterialFactory
    {
        private const string SolidColorShaderName = "Room2Scan/SolidColor";

        private static Shader cachedSolidColorShader;
        private static bool loggedMissingShader;

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

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        public static int ReplaceUnsupportedMaterials(GameObject root, Color fallbackColor)
        {
            if (root == null) return 0;

            var replaced = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (!ShouldReplace(materials[i])) continue;

                    materials[i] = CreateSolidColorMaterial($"{renderer.name}_Fallback", fallbackColor);
                    replaced++;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            return replaced;
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

        private static Shader ResolveBuiltInShaderFallback()
        {
            return GraphicsSettings.currentRenderPipeline == null
                ? Shader.Find("Standard")
                : null;
        }

        private static bool ShouldReplace(Material material)
        {
            if (material == null || material.shader == null) return true;

            var shaderName = material.shader.name;
            if (shaderName.Contains("Error") || shaderName.Contains("FallbackError"))
                return true;

            if (GraphicsSettings.currentRenderPipeline == null)
                return false;

            return shaderName == "Standard"
                   || shaderName.StartsWith("glTF/");
        }
    }
}

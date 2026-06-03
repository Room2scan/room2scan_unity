using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Room2Scan.Rooms
{
    internal static class GltfBaseColorTextureExtractor
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunkType = 0x4E4F534A;
        private const uint BinaryChunkType = 0x004E4942;

        public readonly struct MaterialTextureData
        {
            public MaterialTextureData(Texture2D texture, Color baseColor)
            {
                Texture = texture;
                BaseColor = baseColor;
            }

            public Texture2D Texture { get; }
            public Color BaseColor { get; }
            public bool HasTexture => Texture != null;
        }

        public static bool TryExtractFirstBaseColorTexture(string uriOrPath, out MaterialTextureData textureData)
        {
            textureData = default;

            try
            {
                var path = ResolveLocalPath(uriOrPath);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                var bytes = File.ReadAllBytes(path);
                if (!TryReadGlb(bytes, out var json, out var binary))
                    return false;

                var gltf = JsonUtility.FromJson<GltfJson>(json);
                if (gltf?.materials == null || gltf.materials.Length == 0)
                    return false;

                foreach (var material in gltf.materials)
                {
                    var pbr = material?.pbrMetallicRoughness;
                    if (pbr == null) continue;

                    var baseColor = ResolveBaseColor(pbr.baseColorFactor);
                    var textureIndex = pbr.baseColorTexture?.index ?? -1;
                    if (TryCreateTexture(path, gltf, binary, textureIndex, out var texture))
                    {
                        texture.name = $"{Path.GetFileNameWithoutExtension(path)}_BaseColor";
                        textureData = new MaterialTextureData(texture, baseColor);
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Room2Scan GLB texture extraction failed: {exception.Message}");
            }

            return false;
        }

        private static string ResolveLocalPath(string uriOrPath)
        {
            if (string.IsNullOrWhiteSpace(uriOrPath)) return null;

            if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;

            const string filePrefix = "file://";
            return uriOrPath.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)
                ? uriOrPath.Substring(filePrefix.Length)
                : uriOrPath;
        }

        private static bool TryReadGlb(byte[] bytes, out string json, out byte[] binary)
        {
            json = null;
            binary = null;

            if (bytes == null || bytes.Length < 20)
                return false;

            var magic = BitConverter.ToUInt32(bytes, 0);
            var version = BitConverter.ToUInt32(bytes, 4);
            if (magic != GlbMagic || version != 2)
                return false;

            var offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                var chunkLength = checked((int)BitConverter.ToUInt32(bytes, offset));
                var chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                offset += 8;

                if (chunkLength < 0 || offset + chunkLength > bytes.Length)
                    return false;

                if (chunkType == JsonChunkType)
                {
                    json = Encoding.UTF8.GetString(bytes, offset, chunkLength).TrimEnd('\0', ' ', '\n', '\r', '\t');
                }
                else if (chunkType == BinaryChunkType)
                {
                    binary = new byte[chunkLength];
                    Buffer.BlockCopy(bytes, offset, binary, 0, chunkLength);
                }

                offset += AlignToFourBytes(chunkLength);
            }

            return !string.IsNullOrWhiteSpace(json) && binary != null;
        }

        private static int AlignToFourBytes(int value)
        {
            return (value + 3) & ~3;
        }

        private static bool TryCreateTexture(
            string glbPath,
            GltfJson gltf,
            byte[] binary,
            int textureIndex,
            out Texture2D texture)
        {
            texture = null;
            if (textureIndex < 0 || gltf.textures == null || textureIndex >= gltf.textures.Length)
                return false;

            var imageIndex = gltf.textures[textureIndex]?.source ?? -1;
            if (imageIndex < 0 || gltf.images == null || imageIndex >= gltf.images.Length)
                return false;

            var image = gltf.images[imageIndex];
            var imageBytes = ResolveImageBytes(glbPath, gltf, binary, image);
            if (imageBytes == null || imageBytes.Length == 0)
                return false;

            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!loaded.LoadImage(imageBytes, true))
            {
                UnityEngine.Object.Destroy(loaded);
                return false;
            }

            loaded.wrapMode = TextureWrapMode.Repeat;
            loaded.filterMode = FilterMode.Bilinear;
            texture = loaded;
            return true;
        }

        private static byte[] ResolveImageBytes(string glbPath, GltfJson gltf, byte[] binary, GltfImage image)
        {
            if (image == null) return null;

            if (image.bufferView >= 0 && gltf.bufferViews != null && image.bufferView < gltf.bufferViews.Length)
            {
                var view = gltf.bufferViews[image.bufferView];
                if (view == null || view.byteLength <= 0 || view.byteOffset < 0)
                    return null;
                if (view.byteOffset + view.byteLength > binary.Length)
                    return null;

                var imageBytes = new byte[view.byteLength];
                Buffer.BlockCopy(binary, view.byteOffset, imageBytes, 0, view.byteLength);
                return imageBytes;
            }

            if (string.IsNullOrWhiteSpace(image.uri))
                return null;

            const string base64Marker = ";base64,";
            var markerIndex = image.uri.IndexOf(base64Marker, StringComparison.OrdinalIgnoreCase);
            if (image.uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && markerIndex >= 0)
                return Convert.FromBase64String(image.uri.Substring(markerIndex + base64Marker.Length));

            var imagePath = Path.Combine(
                Path.GetDirectoryName(glbPath) ?? string.Empty,
                Uri.UnescapeDataString(image.uri));
            return File.Exists(imagePath) ? File.ReadAllBytes(imagePath) : null;
        }

        private static Color ResolveBaseColor(float[] factor)
        {
            return factor != null && factor.Length >= 4
                ? new Color(factor[0], factor[1], factor[2], factor[3])
                : Color.white;
        }

        [Serializable]
        private sealed class GltfJson
        {
            public GltfMaterial[] materials;
            public GltfTexture[] textures;
            public GltfImage[] images;
            public GltfBufferView[] bufferViews;
        }

        [Serializable]
        private sealed class GltfMaterial
        {
            public GltfPbrMetallicRoughness pbrMetallicRoughness;
        }

        [Serializable]
        private sealed class GltfPbrMetallicRoughness
        {
            public GltfTextureInfo baseColorTexture;
            public float[] baseColorFactor;
        }

        [Serializable]
        private sealed class GltfTextureInfo
        {
            public int index = -1;
        }

        [Serializable]
        private sealed class GltfTexture
        {
            public int source = -1;
        }

        [Serializable]
        private sealed class GltfImage
        {
            public int bufferView = -1;
            public string uri;
        }

        [Serializable]
        private sealed class GltfBufferView
        {
            public int byteOffset;
            public int byteLength;
        }
    }
}

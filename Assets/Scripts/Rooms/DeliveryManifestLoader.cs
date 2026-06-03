using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Room2Scan.Rooms
{
    public static class DeliveryManifestLoader
    {
        public static bool TryReadStaticWallBounds(string manifestPath, out List<Bounds> wallBounds, out string errorMessage)
        {
            wallBounds = new List<Bounds>();
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                errorMessage = $"Manifest not found: {manifestPath}";
                return false;
            }

            DeliveryManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<DeliveryManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                errorMessage = $"Manifest parse failed: {ex.Message}";
                return false;
            }

            wallBounds = BuildWallBounds(manifest?.wall_boxes);
            return true;
        }

        public static async Task<DeliveryManifestLoadResult> LoadAsync(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return DeliveryManifestLoadResult.Failure($"Manifest not found: {manifestPath}");
            }

            DeliveryManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<DeliveryManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                return DeliveryManifestLoadResult.Failure($"Manifest parse failed: {ex.Message}");
            }

            if (manifest?.furniture == null || manifest.furniture.Length == 0)
            {
                return DeliveryManifestLoadResult.Failure("Manifest has no furniture items.");
            }

            var rootDir = ResolveDeliveryRoot(manifestPath);
            var staticBounds = BuildWallBounds(manifest.wall_boxes);
            var fm = FurnitureManager.GetOrCreateInstance();
            FurnitureManager.SetStaticCollisionBounds(staticBounds);

            var loaded = 0;
            var skipped = 0;

            foreach (var item in manifest.furniture)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                {
                    skipped++;
                    continue;
                }

                var glbPath = ResolvePath(rootDir, item.movable_asset_local_pivot_glb);
                var position = ToVector3(item.initial_transform?.position, Vector3.zero);
                var rotationEuler = ToVector3(item.initial_transform?.rotation_euler_degrees, Vector3.zero);
                var rotation = Quaternion.Euler(rotationEuler);
                var colliderCenter = ToVector3(item.box_collider?.center_local, Vector3.zero);
                var colliderSize = ToVector3(item.box_collider?.size, Vector3.zero);

                var result = await fm.AddFurnitureFromGlbAsync(
                    item.id,
                    string.IsNullOrWhiteSpace(item.category) ? item.id : item.category,
                    glbPath,
                    position,
                    rotation,
                    colliderCenter,
                    colliderSize);

                if (result.Success)
                {
                    loaded++;
                }
                else
                {
                    skipped++;
                    Debug.LogWarning($"Room2Scan DeliveryManifestLoader: skipped '{item.id}': {result.ErrorMessage}");
                }
            }

            Debug.Log($"Room2Scan DeliveryManifestLoader: placed {loaded}/{manifest.furniture.Length} furniture items from '{manifestPath}'.");
            return DeliveryManifestLoadResult.Ok(loaded, skipped, staticBounds.Count);
        }

        private static string ResolveDeliveryRoot(string manifestPath)
        {
            var metadataDir = Directory.GetParent(Path.GetFullPath(manifestPath));
            return metadataDir?.Parent?.FullName ?? Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty;
        }

        private static string ResolvePath(string rootDir, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(relativePath) || LooksLikeWindowsAbsolutePath(relativePath))
            {
                return relativePath;
            }

            return Path.GetFullPath(Path.Combine(rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static List<Bounds> BuildWallBounds(WallBox[] wallBoxes)
        {
            var result = new List<Bounds>();
            if (wallBoxes == null) return result;

            foreach (var wall in wallBoxes)
            {
                var center = ToVector3(wall?.box_collider?.center, Vector3.zero);
                var size = ToVector3(wall?.box_collider?.size, Vector3.zero);
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f) continue;
                result.Add(new Bounds(center, size));
            }

            return result;
        }

        private static Vector3 ToVector3(float[] values, Vector3 fallback)
        {
            return values != null && values.Length >= 3
                ? new Vector3(values[0], values[1], values[2])
                : fallback;
        }

        private static bool LooksLikeWindowsAbsolutePath(string value)
        {
            return value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
        }

        [Serializable]
        private sealed class DeliveryManifest
        {
            public WallBox[] wall_boxes;
            public FurnitureItem[] furniture;
        }

        [Serializable]
        private sealed class WallBox
        {
            public string id;
            public BoxColliderSpec box_collider;
        }

        [Serializable]
        private sealed class FurnitureItem
        {
            public string id;
            public string category;
            public string movable_asset_local_pivot_glb;
            public TransformSpec initial_transform;
            public FurnitureColliderSpec box_collider;
        }

        [Serializable]
        private sealed class TransformSpec
        {
            public float[] position;
            public float[] rotation_euler_degrees;
            public float[] scale;
        }

        [Serializable]
        private sealed class BoxColliderSpec
        {
            public float[] center;
            public float[] size;
        }

        [Serializable]
        private sealed class FurnitureColliderSpec
        {
            public float[] center_local;
            public float[] size;
        }
    }

    public sealed class DeliveryManifestLoadResult
    {
        private DeliveryManifestLoadResult(bool success, int furnitureCount, int skippedCount, int staticColliderCount, string errorMessage)
        {
            Success = success;
            FurnitureCount = furnitureCount;
            SkippedCount = skippedCount;
            StaticColliderCount = staticColliderCount;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public int FurnitureCount { get; }
        public int SkippedCount { get; }
        public int StaticColliderCount { get; }
        public string ErrorMessage { get; }

        public static DeliveryManifestLoadResult Ok(int furnitureCount, int skippedCount, int staticColliderCount) =>
            new DeliveryManifestLoadResult(true, furnitureCount, skippedCount, staticColliderCount, null);

        public static DeliveryManifestLoadResult Failure(string errorMessage) =>
            new DeliveryManifestLoadResult(false, 0, 0, 0, errorMessage);
    }
}

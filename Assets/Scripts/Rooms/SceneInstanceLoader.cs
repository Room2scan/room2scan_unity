using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// Parses a ReplicaCAD scene_instance.json and instantiates all object_instances
    /// into FurnitureManager using the downloaded object GLBs.
    /// </summary>
    public static class SceneInstanceLoader
    {
        // ── JSON-serialisable types ───────────────────────────────────────────────────

        [Serializable]
        private sealed class SceneRoot
        {
            public StageInstance stage_instance;
            public ObjectInstance[] object_instances;
        }

        [Serializable]
        private sealed class StageInstance
        {
            public string template_name;
        }

        [Serializable]
        private sealed class ObjectInstance
        {
            public string  template_name;   // e.g. "objects/frl_apartment_sofa"
            public float[] translation;      // [x, y, z]  – right-handed GLTF space
            public float[] rotation;         // [qw, qx, qy, qz] – Habitat convention
        }

        // ── Public API ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads a ReplicaCAD scene_instance JSON, then for each object_instance
        /// asynchronously loads the corresponding GLB and places it via FurnitureManager.
        /// Returns the number of objects successfully loaded.
        /// </summary>
        public static async Task<int> LoadAsync(string jsonPath, string objectsBaseDir)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"Room2Scan SceneInstanceLoader: JSON not found: {jsonPath}");
                return 0;
            }

            SceneRoot scene;
            try
            {
                scene = JsonUtility.FromJson<SceneRoot>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Room2Scan SceneInstanceLoader: failed to parse '{jsonPath}': {ex.Message}");
                return 0;
            }

            if (scene?.object_instances == null || scene.object_instances.Length == 0)
            {
                Debug.Log("Room2Scan SceneInstanceLoader: no object_instances in scene.");
                return 0;
            }

            var fm     = FurnitureManager.GetOrCreateInstance();
            var loaded = 0;

            for (var i = 0; i < scene.object_instances.Length; i++)
            {
                var obj = scene.object_instances[i];
                if (obj == null || string.IsNullOrWhiteSpace(obj.template_name))
                    continue;

                // "objects/frl_apartment_basket" → catalogId = "frl_apartment_basket"
                var catalogId  = ExtractLastSegment(obj.template_name);
                var instanceId = $"scene_{catalogId}_{i:D3}";
                var glbPath    = Path.Combine(objectsBaseDir, catalogId + ".glb");

                // Convert GLTF right-handed → Unity left-handed (mirror X axis)
                var pos = ToUnityPosition(obj.translation);
                var rot = ToUnityRotation(obj.rotation);

                var result = await fm.AddFurnitureFromGlbAsync(instanceId, catalogId, glbPath, pos, rot);

                if (result.Success)
                    loaded++;
                else
                    Debug.LogWarning($"Room2Scan SceneInstanceLoader: skip '{catalogId}': {result.ErrorMessage}");
            }

            Debug.Log($"Room2Scan SceneInstanceLoader: placed {loaded}/{scene.object_instances.Length} objects.");
            return loaded;
        }

        // ── Coordinate-system conversion ──────────────────────────────────────────────

        /// <summary>
        /// GLTF right-handed position → Unity left-handed.
        /// GLTFast mirrors the X axis, so Unity.x = -GLTF.x
        /// </summary>
        private static Vector3 ToUnityPosition(float[] t) =>
            t != null && t.Length >= 3
                ? new Vector3(-t[0], t[1], t[2])
                : Vector3.zero;

        /// <summary>
        /// Habitat/ReplicaCAD quaternion [qw, qx, qy, qz] (right-handed)
        /// → Unity Quaternion(x, y, z, w) (left-handed, X mirrored).
        /// Matches GLTFast's own conversion: Unity = new Quaternion(-qx, qy, qz, -qw).
        /// </summary>
        private static Quaternion ToUnityRotation(float[] r)
        {
            if (r == null || r.Length < 4) return Quaternion.identity;
            // r[0]=qw  r[1]=qx  r[2]=qy  r[3]=qz
            return new Quaternion(-r[1], r[2], r[3], -r[0]);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────

        private static string ExtractLastSegment(string path)
        {
            var idx = path.LastIndexOf('/');
            return idx >= 0 ? path.Substring(idx + 1) : path;
        }
    }
}

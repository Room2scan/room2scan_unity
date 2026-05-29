using System;
using System.Collections.Generic;
using UnityEngine;

namespace Room2Scan.Rooms
{
    public sealed class FurnitureManager : MonoBehaviour
    {
        private static readonly Color DefaultColor = new Color(0.4f, 0.6f, 1f, 1f);
        private static readonly Color SelectedColor = new Color(1f, 0.8f, 0.2f, 1f);

        private static FurnitureManager instance;

        private readonly Dictionary<string, FurnitureInstance> items = new Dictionary<string, FurnitureInstance>();
        private string selectedInstanceId;

        public static FurnitureManager Instance => instance;

        public static FurnitureManager GetOrCreateInstance()
        {
            if (instance != null) return instance;

            var existing = FindFirstObjectByType<FurnitureManager>();
            if (existing != null)
            {
                instance = existing;
                return instance;
            }

            var go = new GameObject("FurnitureManager");
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            instance = go.AddComponent<FurnitureManager>();
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            gameObject.name = "FurnitureManager";
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        public AddFurnitureResult AddFurniture(string instanceId, string catalogId, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return AddFurnitureResult.Failure("missing_instance_id", "instanceId is required.");

            if (items.ContainsKey(instanceId))
                return AddFurnitureResult.Failure("duplicate_instance_id", $"Instance '{instanceId}' already exists.");

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Furniture_{SanitizeName(catalogId)}_{instanceId}";
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            var mat = CreateMaterial(DefaultColor);
            go.GetComponent<Renderer>().material = mat;

            if (Application.isPlaying) DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.DontSave;

            items[instanceId] = new FurnitureInstance(instanceId, catalogId, go, mat);
            Debug.Log($"Room2Scan FurnitureManager: added '{catalogId}' ({instanceId}) at {position}");
            return AddFurnitureResult.Success(instanceId, position);
        }

        public bool SelectFurniture(string instanceId)
        {
            if (selectedInstanceId != null && items.TryGetValue(selectedInstanceId, out var prev))
                prev.Material.color = DefaultColor;

            selectedInstanceId = null;

            if (!items.TryGetValue(instanceId, out var item))
                return false;

            item.Material.color = SelectedColor;
            selectedInstanceId = instanceId;
            return true;
        }

        public bool RotateSelected(float deltaDeg)
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item))
                return false;

            item.GameObject.transform.Rotate(Vector3.up, deltaDeg, Space.World);
            return true;
        }

        public bool DeleteSelected()
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item))
                return false;

            items.Remove(selectedInstanceId);
            selectedInstanceId = null;
            DestroyFurnitureInstance(item);
            return true;
        }

        public void ClearAll()
        {
            foreach (var item in items.Values)
                DestroyFurnitureInstance(item);
            items.Clear();
            selectedInstanceId = null;
        }

        public IReadOnlyList<LayoutItem> GetLayoutItems()
        {
            var result = new List<LayoutItem>(items.Count);
            foreach (var item in items.Values)
            {
                var t = item.GameObject.transform;
                result.Add(new LayoutItem(item.InstanceId, item.CatalogId, t.position, t.eulerAngles.y, t.localScale.x));
            }
            return result;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            return mat;
        }

        private static void DestroyFurnitureInstance(FurnitureInstance item)
        {
            if (Application.isPlaying)
            {
                Destroy(item.Material);
                Destroy(item.GameObject);
            }
            else
            {
                DestroyImmediate(item.Material);
                DestroyImmediate(item.GameObject);
            }
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            return System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_-]", "_");
        }

        private sealed class FurnitureInstance
        {
            public string InstanceId { get; }
            public string CatalogId { get; }
            public GameObject GameObject { get; }
            public Material Material { get; }

            public FurnitureInstance(string instanceId, string catalogId, GameObject go, Material mat)
            {
                InstanceId = instanceId;
                CatalogId = catalogId;
                GameObject = go;
                Material = mat;
            }
        }

        public sealed class LayoutItem
        {
            public string InstanceId { get; }
            public string CatalogId { get; }
            public Vector3 Position { get; }
            public float RotationYDeg { get; }
            public float Scale { get; }

            public LayoutItem(string instanceId, string catalogId, Vector3 position, float rotationYDeg, float scale)
            {
                InstanceId = instanceId;
                CatalogId = catalogId;
                Position = position;
                RotationYDeg = rotationYDeg;
                Scale = scale;
            }
        }

        public sealed class AddFurnitureResult
        {
            public bool Success { get; }
            public string InstanceId { get; }
            public Vector3 Position { get; }
            public string ErrorCode { get; }
            public string ErrorMessage { get; }

            private AddFurnitureResult(bool success, string instanceId, Vector3 position, string errorCode, string errorMessage)
            {
                Success = success;
                InstanceId = instanceId;
                Position = position;
                ErrorCode = errorCode;
                ErrorMessage = errorMessage;
            }

            public static AddFurnitureResult Success(string instanceId, Vector3 position) =>
                new AddFurnitureResult(true, instanceId, position, null, null);

            public static AddFurnitureResult Failure(string errorCode, string errorMessage) =>
                new AddFurnitureResult(false, null, default, errorCode, errorMessage);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace Room2Scan.Rooms
{
    public sealed class FurnitureManager : MonoBehaviour
    {
        private static readonly Color DefaultColor     = new Color(0.4f, 0.6f, 1.0f, 1f);
        private static readonly Color SelectedColor   = new Color(1.0f, 0.8f, 0.2f, 1f);
        private static readonly Color HiddenColor     = new Color(0.4f, 0.6f, 1.0f, 0.25f);
        private static readonly Color CollisionColor  = new Color(1.0f, 0.22f, 0.22f, 1f);  // red  — overlap / wall breach
        private static readonly Color PlacementOkColor= new Color(0.25f, 0.85f, 0.35f, 1f); // green — OK while dragging

        private static FurnitureManager instance;

        private readonly Dictionary<string, FurnitureInstance> items =
            new Dictionary<string, FurnitureInstance>();

        private string selectedInstanceId;

        /// <summary>
        /// Room bounding box set by UnityBridge when a room is built or loaded.
        /// Used by FurnitureDragController to clamp furniture inside the room.
        /// </summary>
        public static Bounds RoomBounds;

        // ── Public API ───────────────────────────────────────────────────────────────

        public static FurnitureManager Instance        => instance;
        public string SelectedInstanceId               => selectedInstanceId;

        /// <summary>Update the room bounds used for wall-collision clamping.</summary>
        public static void SetRoomBounds(Bounds b) { RoomBounds = b; }

        public static FurnitureManager GetOrCreateInstance()
        {
            if (instance != null) return instance;
            var existing = FindFirstObjectByType<FurnitureManager>();
            if (existing != null) { instance = existing; return instance; }
            var go = new GameObject("FurnitureManager");
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            instance = go.AddComponent<FurnitureManager>();
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return instance;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            gameObject.name = "FurnitureManager";
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        // ── Core furniture operations ─────────────────────────────────────────────────

        /// <summary>
        /// Synchronous add — creates a fallback cube.
        /// Used for user-triggered AddFurniture from the catalog.
        /// </summary>
        public AddFurnitureResult AddFurniture(string instanceId, string catalogId, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return AddFurnitureResult.Failure("missing_instance_id", "instanceId is required.");
            if (items.ContainsKey(instanceId))
                return AddFurnitureResult.Failure("duplicate_instance_id", $"Instance '{instanceId}' already exists.");

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Furniture_{SanitizeName(catalogId)}_{instanceId}";
            go.transform.position   = position;
            go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            var mat = CreateMaterial(DefaultColor);
            go.GetComponent<Renderer>().material = mat;

            // Identifier component — used by FurnitureDragController for tap-to-select
            var identifier = go.AddComponent<FurnitureIdentifier>();
            identifier.InstanceId = instanceId;

            if (Application.isPlaying) DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.DontSave;

            items[instanceId] = new FurnitureInstance(instanceId, catalogId, go, mat);
            Debug.Log($"Room2Scan FurnitureManager: added '{catalogId}' ({instanceId}) at {position}");
            return AddFurnitureResult.Ok(instanceId, position);
        }

        /// <summary>
        /// Async add — loads a real GLB via GLTFast.
        /// Falls back to a cube if the GLB is missing or fails to load.
        /// Used by SceneInstanceLoader when auto-placing objects from a scene JSON.
        /// </summary>
        public async Task<AddFurnitureResult> AddFurnitureFromGlbAsync(
            string instanceId, string catalogId, string glbPath,
            Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return AddFurnitureResult.Failure("missing_instance_id", "instanceId is required.");
            if (items.ContainsKey(instanceId))
                return AddFurnitureResult.Failure("duplicate_instance_id", $"Instance '{instanceId}' already exists.");

            // Parent object carries position / rotation; GLB mesh is a child.
            var parent = new GameObject($"Furniture_{SanitizeName(catalogId)}_{instanceId}");
            parent.transform.position = position;
            parent.transform.rotation = rotation;
            if (Application.isPlaying) DontDestroyOnLoad(parent);
            else parent.hideFlags = HideFlags.DontSave;

            // Identifier for tap-to-select raycasting
            var identifier = parent.AddComponent<FurnitureIdentifier>();
            identifier.InstanceId = instanceId;

            var glbLoaded = false;

            if (!string.IsNullOrWhiteSpace(glbPath) && File.Exists(glbPath))
            {
                var uri = NormalizePath(glbPath);
                try
                {
                    var gltf = new GltfImport();
                    if (await gltf.Load(uri))
                        glbLoaded = await gltf.InstantiateMainSceneAsync(parent.transform);
                    else
                        gltf.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"Room2Scan FurnitureManager: GLB load failed for '{catalogId}': {ex.Message}");
                }
            }

            if (!glbLoaded)
            {
                // Fallback: small coloured cube so the item is still visible.
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(parent.transform, false);
                cube.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                cube.GetComponent<Renderer>().material = CreateMaterial(DefaultColor);
            }

            // GLB-loaded instances own their materials internally; no separate Material ref.
            items[instanceId] = new FurnitureInstance(instanceId, catalogId, parent, null);
            Debug.Log($"Room2Scan FurnitureManager: scene-placed '{catalogId}' ({instanceId}) at {position}");
            return AddFurnitureResult.Ok(instanceId, position);
        }

        public bool SelectFurniture(string instanceId)
        {
            // Deselect current
            if (selectedInstanceId != null && items.TryGetValue(selectedInstanceId, out var prev))
            {
                if (prev.Material != null)
                    prev.Material.color = prev.Visible ? DefaultColor : HiddenColor;
            }

            selectedInstanceId = null;

            if (!items.TryGetValue(instanceId, out var item)) return false;

            if (item.Material != null) item.Material.color = SelectedColor;
            selectedInstanceId = instanceId;
            return true;
        }

        public DuplicateFurnitureResult DuplicateSelected()
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var original))
                return DuplicateFurnitureResult.Failure("no_selection", "No furniture is selected.");

            var newId  = $"dup_{SanitizeName(original.CatalogId)}_{Guid.NewGuid():N}";
            var newPos = original.GameObject.transform.position + new Vector3(0.6f, 0f, 0.6f);
            var result = AddFurniture(newId, original.CatalogId, newPos);
            if (!result.Success)
                return DuplicateFurnitureResult.Failure(result.ErrorCode, result.ErrorMessage);

            // copy rotation & scale from original
            if (items.TryGetValue(newId, out var newItem))
            {
                newItem.GameObject.transform.rotation   = original.GameObject.transform.rotation;
                newItem.GameObject.transform.localScale = original.GameObject.transform.localScale;
            }

            return DuplicateFurnitureResult.Ok(newId, selectedInstanceId, newPos);
        }

        public bool RotateSelected(float deltaDeg)
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item)) return false;
            item.GameObject.transform.Rotate(Vector3.up, deltaDeg, Space.World);
            return true;
        }

        public bool DeleteSelected()
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item)) return false;
            var deletedId = selectedInstanceId;
            items.Remove(deletedId);
            selectedInstanceId = null;
            DestroyFurnitureInstance(item);
            Debug.Log($"Room2Scan FurnitureManager: deleted '{deletedId}'");
            return true;
        }

        public bool SetVisibility(string instanceId, bool visible)
        {
            if (!items.TryGetValue(instanceId, out var item)) return false;
            item.Visible = visible;

            var r = item.GameObject.GetComponentInChildren<Renderer>(true);
            if (r != null) r.enabled = visible;

            if (item.Material != null)
                item.Material.color = instanceId == selectedInstanceId
                    ? SelectedColor
                    : (visible ? DefaultColor : HiddenColor);

            return true;
        }

        public bool SetLocked(string instanceId, bool locked)
        {
            if (!items.TryGetValue(instanceId, out var item)) return false;
            item.Locked = locked;
            return true;
        }

        public void ClearAll()
        {
            foreach (var item in items.Values) DestroyFurnitureInstance(item);
            items.Clear();
            selectedInstanceId = null;
        }

        // ── P5: Move / collision / query ─────────────────────────────────────────────

        /// <summary>Moves the currently selected furniture to the given world position.</summary>
        public bool MoveSelected(float x, float y, float z)
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item)) return false;
            item.GameObject.transform.position = new Vector3(x, y, z);
            return true;
        }

        /// <summary>Returns the root GameObject of the currently selected furniture, or null.</summary>
        public GameObject GetSelectedGameObject()
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item)) return null;
            return item.GameObject;
        }

        /// <summary>
        /// Finds the instanceId whose root GameObject equals <paramref name="go"/> or is an ancestor of it.
        /// Used by FurnitureDragController after a Physics.Raycast hit.
        /// </summary>
        public string GetInstanceIdFromGameObject(GameObject go)
        {
            // Direct component check (cube furniture has FurnitureIdentifier on itself)
            var id = go.GetComponent<FurnitureIdentifier>()
                  ?? go.GetComponentInParent<FurnitureIdentifier>();
            return id?.InstanceId;
        }

        /// <summary>
        /// Returns true when <paramref name="instanceId"/> overlaps any other furniture
        /// or breaches the room walls.
        /// </summary>
        public bool CheckCollision(string instanceId)
        {
            if (!items.TryGetValue(instanceId, out var target)) return false;
            if (target.Locked) return false;

            var tb = GetWorldBounds(target.GameObject);

            // ── Wall/boundary check ───────────────────────────────────────────────
            if (RoomBounds.size.sqrMagnitude > 0f)
            {
                if (tb.min.x < RoomBounds.min.x || tb.max.x > RoomBounds.max.x ||
                    tb.min.z < RoomBounds.min.z || tb.max.z > RoomBounds.max.z)
                    return true;
            }

            // ── Furniture-to-furniture AABB overlap ───────────────────────────────
            foreach (var kvp in items)
            {
                if (kvp.Key == instanceId)   continue;
                if (!kvp.Value.Visible)       continue;
                if (kvp.Value.Locked)         continue;

                var ob = GetWorldBounds(kvp.Value.GameObject);
                if (tb.Intersects(ob)) return true;
            }

            return false;
        }

        /// <summary>
        /// Sets the drag-feedback color of <paramref name="instanceId"/>:
        /// red = collision, green = placement OK.
        /// Call with <paramref name="hasCollision"/> = false when drag ends
        /// to restore the normal selected-yellow tint.
        /// </summary>
        public void SetCollisionColor(string instanceId, bool hasCollision)
        {
            if (!items.TryGetValue(instanceId, out var item)) return;
            if (item.Material == null) return;
            item.Material.color = hasCollision ? CollisionColor : PlacementOkColor;
        }

        /// <summary>Encapsulated AABB of all renderers on a furniture GameObject.</summary>
        private static Bounds GetWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, go.transform.localScale);

            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // ── Query helpers ────────────────────────────────────────────────────────────

        public IReadOnlyList<LayoutItem> GetLayoutItems()
        {
            var result = new List<LayoutItem>(items.Count);
            foreach (var item in items.Values)
            {
                var t = item.GameObject.transform;
                result.Add(new LayoutItem(
                    item.InstanceId, item.CatalogId,
                    t.position, t.eulerAngles.y, t.localScale.x,
                    item.Visible, item.Locked));
            }
            return result;
        }

        /// <summary>Returns the transform of the currently selected item, or null.</summary>
        public TransformData? GetSelectedTransform()
        {
            if (selectedInstanceId == null || !items.TryGetValue(selectedInstanceId, out var item))
                return null;
            var t = item.GameObject.transform;
            return new TransformData(t.position, t.eulerAngles.y, t.localScale.x);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = color };
        }

        private static void DestroyFurnitureInstance(FurnitureInstance item)
        {
            if (Application.isPlaying)
            {
                if (item.Material != null) Destroy(item.Material);
                Destroy(item.GameObject);
            }
            else
            {
                if (item.Material != null) DestroyImmediate(item.Material);
                DestroyImmediate(item.GameObject);
            }
        }

        /// <summary>Converts a local file path to a file:// URI suitable for GLTFast.</summary>
        private static string NormalizePath(string path)
        {
            if (LooksLikeWindowsAbsolutePath(path) || Path.IsPathRooted(path))
                return new Uri(path).AbsoluteUri;
            if (Uri.TryCreate(path, UriKind.Absolute, out _))
                return path;
            return path;
        }

        private static bool LooksLikeWindowsAbsolutePath(string v) =>
            v.Length >= 3 && char.IsLetter(v[0]) && v[1] == ':' && (v[2] == '\\' || v[2] == '/');

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            return System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_-]", "_");
        }

        // ── Inner types ──────────────────────────────────────────────────────────────

        private sealed class FurnitureInstance
        {
            public string     InstanceId  { get; }
            public string     CatalogId   { get; }
            public GameObject GameObject  { get; }
            /// <summary>Null for GLB-loaded instances (they own their materials internally).</summary>
            public Material   Material    { get; }
            public bool       Visible     { get; set; } = true;
            public bool       Locked      { get; set; } = false;

            public FurnitureInstance(string instanceId, string catalogId, GameObject go, Material mat)
            {
                InstanceId = instanceId;
                CatalogId  = catalogId;
                GameObject = go;
                Material   = mat;
            }
        }

        // ── Public data types ─────────────────────────────────────────────────────────

        public readonly struct TransformData
        {
            public Vector3 Position     { get; }
            public float   RotationYDeg { get; }
            public float   Scale        { get; }

            public TransformData(Vector3 position, float rotationYDeg, float scale)
            {
                Position     = position;
                RotationYDeg = rotationYDeg;
                Scale        = scale;
            }
        }

        public sealed class LayoutItem
        {
            public string  InstanceId   { get; }
            public string  CatalogId    { get; }
            public Vector3 Position     { get; }
            public float   RotationYDeg { get; }
            public float   Scale        { get; }
            public bool    Visible      { get; }
            public bool    Locked       { get; }

            public LayoutItem(
                string instanceId, string catalogId,
                Vector3 position, float rotationYDeg, float scale,
                bool visible, bool locked)
            {
                InstanceId   = instanceId;
                CatalogId    = catalogId;
                Position     = position;
                RotationYDeg = rotationYDeg;
                Scale        = scale;
                Visible      = visible;
                Locked       = locked;
            }
        }

        public sealed class AddFurnitureResult
        {
            public bool    Success      { get; }
            public string  InstanceId   { get; }
            public Vector3 Position     { get; }
            public string  ErrorCode    { get; }
            public string  ErrorMessage { get; }

            private AddFurnitureResult(bool success, string instanceId, Vector3 position, string errorCode, string errorMessage)
            {
                Success      = success;
                InstanceId   = instanceId;
                Position     = position;
                ErrorCode    = errorCode;
                ErrorMessage = errorMessage;
            }

            public static AddFurnitureResult Ok(string instanceId, Vector3 position) =>
                new AddFurnitureResult(true, instanceId, position, null, null);
            public static AddFurnitureResult Failure(string code, string message) =>
                new AddFurnitureResult(false, null, default, code, message);
        }

        public sealed class DuplicateFurnitureResult
        {
            public bool    Success            { get; }
            public string  NewInstanceId      { get; }
            public string  OriginalInstanceId { get; }
            public Vector3 Position           { get; }
            public string  ErrorCode          { get; }
            public string  ErrorMessage       { get; }

            private DuplicateFurnitureResult(bool success, string newId, string origId, Vector3 pos, string code, string msg)
            {
                Success            = success;
                NewInstanceId      = newId;
                OriginalInstanceId = origId;
                Position           = pos;
                ErrorCode          = code;
                ErrorMessage       = msg;
            }

            public static DuplicateFurnitureResult Ok(string newId, string origId, Vector3 pos) =>
                new DuplicateFurnitureResult(true, newId, origId, pos, null, null);
            public static DuplicateFurnitureResult Failure(string code, string msg) =>
                new DuplicateFurnitureResult(false, null, null, default, code, msg);
        }
    }
}

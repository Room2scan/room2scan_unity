using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace Room2Scan.Rooms
{
    public sealed class RoomManager : MonoBehaviour
    {
        private const string RoomSchemaVersion = "room-json/v1";
        private const string GlbFormat = "glb";
        private const string PlyFormat = "ply";

        private static RoomManager instance;

        private GameObject roomRoot;
        private GltfImport currentImport;
        private int loadGeneration;
        private bool currentRoomUsesGeneratedPlyAssets;

        public static RoomManager Instance => instance;
        public string CurrentRoomId { get; private set; }
        public RoomLoadResult CurrentRoomResult { get; private set; }

        public static RoomManager GetOrCreateInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            var existing = FindFirstObjectByType<RoomManager>();
            if (existing != null)
            {
                instance = existing;
                return instance;
            }

            var managerObject = new GameObject("RoomManager");
            if (!Application.isPlaying)
            {
                managerObject.hideFlags = HideFlags.DontSave;
            }

            instance = managerObject.AddComponent<RoomManager>();
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(managerObject);
            }

            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                DestroyUnityObject(gameObject);
                return;
            }

            instance = this;
            gameObject.name = "RoomManager";
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        public void LoadRoomFromBridgeEnvelope(string envelopeJson, Action<RoomLoadResult> onCompleted)
        {
            _ = LoadRoomFromBridgeEnvelopeAsync(envelopeJson, onCompleted);
        }

        public void ClearRoom()
        {
            loadGeneration++;
            ClearLoadedRoom();
            CurrentRoomId = null;
            CurrentRoomResult = null;
        }

        private async Task LoadRoomFromBridgeEnvelopeAsync(string envelopeJson, Action<RoomLoadResult> onCompleted)
        {
            var generation = ++loadGeneration;

            if (!TryParseRoom(envelopeJson, out var room, out var errorCode, out var errorMessage))
            {
                Complete(onCompleted, RoomLoadResult.Failure(null, null, errorCode, errorMessage));
                return;
            }

            var roomId = room.roomId;
            var originalMeshUri = room.mesh.uri;
            var loadableMeshUri = NormalizeMeshUri(originalMeshUri);
            var isDevPlyMesh = IsDevPlyMesh(room.mesh);

            try
            {
                if (isDevPlyMesh)
                {
                    LoadLocalPlyRoom(roomId, originalMeshUri, onCompleted);
                    return;
                }

                Debug.Log($"Room2Scan RoomManager: loading GLB room '{roomId}' from {loadableMeshUri}");

                var gltf = new GltfImport();
                var loadSucceeded = await gltf.Load(loadableMeshUri);
                if (generation != loadGeneration)
                {
                    gltf.Dispose();
                    return;
                }

                if (!loadSucceeded)
                {
                    gltf.Dispose();
                    Complete(onCompleted, RoomLoadResult.Failure(roomId, originalMeshUri, "glb_load_failed", $"Could not load GLB: {originalMeshUri}"));
                    return;
                }

                var nextRoomRoot = new GameObject($"RoomRoot_{SanitizeName(roomId)}");
                PrepareRoomRoot(nextRoomRoot);

                var instantiateSucceeded = await gltf.InstantiateMainSceneAsync(nextRoomRoot.transform);
                if (generation != loadGeneration)
                {
                    DestroyUnityObject(nextRoomRoot);
                    gltf.Dispose();
                    return;
                }

                if (!instantiateSucceeded)
                {
                    DestroyUnityObject(nextRoomRoot);
                    gltf.Dispose();
                    Complete(onCompleted, RoomLoadResult.Failure(roomId, originalMeshUri, "glb_instantiate_failed", $"Could not instantiate GLB scene: {originalMeshUri}"));
                    return;
                }

                ClearLoadedRoom();

                roomRoot = nextRoomRoot;
                currentImport = gltf;
                currentRoomUsesGeneratedPlyAssets = false;
                CurrentRoomId = roomId;

                var colliderCount = AddMeshColliders(roomRoot);
                var bounds = ResolveBounds(room, roomRoot);
                FrameCamera(bounds);

                var result = RoomLoadResult.Success(roomId, originalMeshUri, loadableMeshUri, colliderCount, bounds);
                CurrentRoomResult = result;

                Debug.Log($"Room2Scan RoomManager: loaded room '{roomId}' with {colliderCount} mesh colliders.");
                Complete(onCompleted, result);
            }
            catch (Exception exception)
            {
                if (generation == loadGeneration)
                {
                    Complete(onCompleted, RoomLoadResult.Failure(roomId, originalMeshUri, "room_load_exception", exception.Message));
                }
            }
        }

        private void LoadLocalPlyRoom(string roomId, string originalMeshUri, Action<RoomLoadResult> onCompleted)
        {
            Debug.Log($"Room2Scan RoomManager: loading local PLY test room '{roomId}' from {originalMeshUri}");

            if (!PlyMeshLoader.TryLoad(originalMeshUri, roomId, out var nextRoomRoot, out var plyBounds, out var error))
            {
                Complete(onCompleted, RoomLoadResult.Failure(roomId, originalMeshUri, "ply_load_failed", error));
                return;
            }

            PrepareRoomRoot(nextRoomRoot);
            ClearLoadedRoom();

            roomRoot = nextRoomRoot;
            currentImport = null;
            currentRoomUsesGeneratedPlyAssets = true;
            CurrentRoomId = roomId;

            var colliderCount = AddMeshColliders(roomRoot);
            var bounds = plyBounds.size == Vector3.zero ? ResolveBounds(null, roomRoot) : plyBounds;
            FrameCamera(bounds);

            var result = RoomLoadResult.Success(roomId, originalMeshUri, originalMeshUri, colliderCount, bounds);
            CurrentRoomResult = result;

            Debug.Log($"Room2Scan RoomManager: loaded local PLY test room '{roomId}' with {colliderCount} mesh colliders.");
            Complete(onCompleted, result);
        }

        private static void Complete(Action<RoomLoadResult> onCompleted, RoomLoadResult result)
        {
            onCompleted?.Invoke(result);
        }

        private void ClearLoadedRoom()
        {
            if (roomRoot != null)
            {
                if (currentRoomUsesGeneratedPlyAssets)
                {
                    DestroyGeneratedMeshesAndMaterials(roomRoot);
                }

                DestroyUnityObject(roomRoot);
                roomRoot = null;
            }

            if (currentImport != null)
            {
                currentImport.Dispose();
                currentImport = null;
            }

            currentRoomUsesGeneratedPlyAssets = false;
        }

        private static bool TryParseRoom(string json, out RoomJson room, out string errorCode, out string errorMessage)
        {
            room = null;
            errorCode = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                errorCode = "invalid_room_json";
                errorMessage = "LoadRoom payload is empty.";
                return false;
            }

            try
            {
                var envelope = JsonUtility.FromJson<BridgeRoomEnvelope>(json);
                room = envelope?.payload?.room;

                if (room == null || string.IsNullOrWhiteSpace(room.roomId))
                {
                    room = JsonUtility.FromJson<RoomJson>(json);
                }
            }
            catch (Exception exception)
            {
                errorCode = "invalid_room_json";
                errorMessage = exception.Message;
                return false;
            }

            return ValidateRoom(room, out errorCode, out errorMessage);
        }

        private static bool ValidateRoom(RoomJson room, out string errorCode, out string errorMessage)
        {
            errorCode = null;
            errorMessage = null;

            if (room == null || string.IsNullOrWhiteSpace(room.roomId))
            {
                errorCode = "invalid_room_json";
                errorMessage = "LoadRoom command must include payload.room with a roomId.";
                return false;
            }

            if (room.schemaVersion != RoomSchemaVersion)
            {
                errorCode = "unsupported_room_schema";
                errorMessage = $"Unsupported room schema version: {room.schemaVersion}";
                return false;
            }

            if (room.mesh == null || string.IsNullOrWhiteSpace(room.mesh.uri))
            {
                errorCode = "missing_mesh_uri";
                errorMessage = "room-json/v1 requires mesh.uri.";
                return false;
            }

            if (!string.Equals(room.mesh.format, GlbFormat, StringComparison.OrdinalIgnoreCase) && !IsDevPlyMesh(room.mesh))
            {
                errorCode = "unsupported_mesh_format";
                errorMessage = $"room-json/v1 runtime loading supports GLB. Local PLY is allowed only in Unity Editor/development builds for testing. Received: {room.mesh.format}";
                return false;
            }

            if (!HasUnityRoomLocalCoordinates(room.coordinateSystem))
            {
                errorCode = "unsupported_coordinate_system";
                errorMessage = "room-json/v1 must be normalized to Unity room-local coordinates: meter, left-handed, +Y up, +Z forward, identity toUnity transform.";
                return false;
            }

            if (room.bounds == null || !room.bounds.IsValid)
            {
                errorCode = "invalid_bounds";
                errorMessage = "room-json/v1 requires valid bounds.min and bounds.max.";
                return false;
            }

            return true;
        }

        private static bool IsDevPlyMesh(RoomMeshJson mesh)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return mesh != null
                   && (string.Equals(mesh.format, PlyFormat, StringComparison.OrdinalIgnoreCase)
                       || mesh.uri.EndsWith(".ply", StringComparison.OrdinalIgnoreCase));
#else
            return false;
#endif
        }

        private static bool HasUnityRoomLocalCoordinates(RoomCoordinateSystemJson coordinateSystem)
        {
            if (coordinateSystem == null || coordinateSystem.toUnity == null)
            {
                return false;
            }

            return coordinateSystem.toUnity.positionOffset != null
                   && coordinateSystem.toUnity.rotationEulerDeg != null
                   && coordinateSystem.unit == "meter"
                   && coordinateSystem.handedness == "left"
                   && coordinateSystem.upAxis == "+Y"
                   && coordinateSystem.forwardAxis == "+Z"
                   && coordinateSystem.toUnity.positionOffset.IsZero
                   && coordinateSystem.toUnity.rotationEulerDeg.IsZero
                   && Mathf.Approximately(coordinateSystem.toUnity.scaleMultiplier, 1f);
        }

        private static string NormalizeMeshUri(string meshUri)
        {
            if (LooksLikeWindowsAbsolutePath(meshUri) || Path.IsPathRooted(meshUri))
            {
                return new Uri(meshUri).AbsoluteUri;
            }

            if (Uri.TryCreate(meshUri, UriKind.Absolute, out var absoluteUri) && !string.IsNullOrWhiteSpace(absoluteUri.Scheme))
            {
                return meshUri;
            }

            return BuildStreamingAssetsUri(meshUri);
        }

        private static string BuildStreamingAssetsUri(string relativeMeshUri)
        {
            var normalizedRelativeUri = relativeMeshUri.Replace("\\", "/").TrimStart('/');
            var streamingAssetsPath = Application.streamingAssetsPath.Replace("\\", "/").TrimEnd('/');

            if (streamingAssetsPath.Contains("://", StringComparison.Ordinal))
            {
                return $"{streamingAssetsPath}/{normalizedRelativeUri}";
            }

            var localPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, normalizedRelativeUri));
            return new Uri(localPath).AbsoluteUri;
        }

        private static bool LooksLikeWindowsAbsolutePath(string value)
        {
            return value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
        }

        private static int AddMeshColliders(GameObject root)
        {
            var colliderCount = 0;
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null || meshFilter.GetComponent<Collider>() != null)
                {
                    continue;
                }

                try
                {
                    var meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                    if (CountTriangles(meshFilter.sharedMesh) > 1_500_000)
                    {
                        meshCollider.cookingOptions &= ~MeshColliderCookingOptions.UseFastMidphase;
                    }

                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                    colliderCount++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Room2Scan RoomManager: could not add MeshCollider to {meshFilter.name}: {exception.Message}");
                }
            }

            return colliderCount;
        }

        private static long CountTriangles(Mesh mesh)
        {
            long triangleCount = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                triangleCount += (long)mesh.GetIndexCount(i) / 3;
            }

            return triangleCount;
        }

        private static Bounds ResolveBounds(RoomJson room, GameObject root)
        {
            if (room?.bounds != null && room.bounds.IsValid)
            {
                return room.bounds.ToBounds();
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void FrameCamera(Bounds bounds)
        {
            var camera = Camera.main ?? FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            var largestHorizontalSize = Mathf.Max(bounds.size.x, bounds.size.z, 1f);
            var largestSize = Mathf.Max(largestHorizontalSize, bounds.size.y, 1f);
            var distance = Mathf.Max(largestHorizontalSize * 1.4f, 5f);
            var target = bounds.center + Vector3.up * Mathf.Min(bounds.extents.y * 0.25f, 1.2f);

            camera.transform.position = bounds.center + new Vector3(-distance, Mathf.Max(bounds.size.y, 2f) + distance * 0.5f, -distance);
            camera.transform.LookAt(target);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(100f, distance * 5f);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(largestSize * 0.65f, 2.5f);
        }

        private static void PrepareRoomRoot(GameObject root)
        {
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(root);
            }
            else
            {
                root.hideFlags = HideFlags.DontSave;
            }
        }

        private static void DestroyGeneratedMeshesAndMaterials(GameObject root)
        {
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                DestroyUnityObject(meshFilter.sharedMesh);
                meshFilter.sharedMesh = null;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    DestroyUnityObject(material);
                }
            }
        }

        private static string SanitizeName(string value)
        {
            return Regex.Replace(value, "[^A-Za-z0-9_-]", "_");
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        [Serializable]
        private sealed class BridgeRoomEnvelope
        {
            public BridgeRoomPayload payload;
        }

        [Serializable]
        private sealed class BridgeRoomPayload
        {
            public RoomJson room;
        }

        [Serializable]
        private sealed class RoomJson
        {
            public string schemaVersion;
            public string roomId;
            public RoomCoordinateSystemJson coordinateSystem;
            public RoomMeshJson mesh;
            public RoomBoundsJson bounds;
        }

        [Serializable]
        private sealed class RoomCoordinateSystemJson
        {
            public string unit;
            public string handedness;
            public string upAxis;
            public string forwardAxis;
            public RoomToUnityTransformJson toUnity;
        }

        [Serializable]
        private sealed class RoomToUnityTransformJson
        {
            public Vector3Json positionOffset;
            public Vector3Json rotationEulerDeg;
            public float scaleMultiplier;
        }

        [Serializable]
        private sealed class RoomMeshJson
        {
            public string uri;
            public string format;
        }

        [Serializable]
        private sealed class RoomBoundsJson
        {
            public Vector3Json min;
            public Vector3Json max;

            public bool IsValid => min != null
                                   && max != null
                                   && max.x > min.x
                                   && max.y >= min.y
                                   && max.z > min.z;

            public Bounds ToBounds()
            {
                var minVector = min.ToVector3();
                var maxVector = max.ToVector3();
                return new Bounds((minVector + maxVector) * 0.5f, maxVector - minVector);
            }
        }

        [Serializable]
        private sealed class Vector3Json
        {
            public float x;
            public float y;
            public float z;

            public bool IsZero => Mathf.Approximately(x, 0f) && Mathf.Approximately(y, 0f) && Mathf.Approximately(z, 0f);

            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }
        }
    }

    public sealed class RoomLoadResult
    {
        private RoomLoadResult(bool success, string roomId, string meshUri, string normalizedMeshUri, int colliderCount, Bounds bounds, string errorCode, string errorMessage)
        {
            SuccessFlag = success;
            RoomId = roomId;
            MeshUri = meshUri;
            NormalizedMeshUri = normalizedMeshUri;
            ColliderCount = colliderCount;
            Bounds = bounds;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool SuccessFlag { get; }
        public string RoomId { get; }
        public string MeshUri { get; }
        public string NormalizedMeshUri { get; }
        public int ColliderCount { get; }
        public Bounds Bounds { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        public static RoomLoadResult Success(string roomId, string meshUri, string normalizedMeshUri, int colliderCount, Bounds bounds)
        {
            return new RoomLoadResult(true, roomId, meshUri, normalizedMeshUri, colliderCount, bounds, null, null);
        }

        public static RoomLoadResult Failure(string roomId, string meshUri, string errorCode, string errorMessage)
        {
            return new RoomLoadResult(false, roomId, meshUri, null, 0, default, errorCode, errorMessage);
        }
    }
}

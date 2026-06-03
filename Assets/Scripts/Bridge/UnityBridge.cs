using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Room2Scan.Rooms;
using UnityEngine;

namespace Room2Scan.Bridge
{
    public sealed class UnityBridge : MonoBehaviour
    {
        private const string SchemaVersion  = "unity-bridge/v1";
        private const string DefaultRoomId  = "mock_room";

        private static UnityBridge instance;

        private string currentRoomId  = DefaultRoomId;
        private string activeTool     = "select";   // mirrors RN toolbar state
        private bool   snapEnabled    = false;
        private bool   loadRoomInProgress;
        private string loadingRoomId;

        public static UnityBridge Instance => instance;

        // ── Public properties (read by FurnitureDragController) ───────────────────
        public string ActiveTool  => activeTool;
        public bool   SnapEnabled => snapEnabled;

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => GetOrCreateInstance();

        public static UnityBridge GetOrCreateInstance()
        {
            if (instance != null) return instance;
            var existing = FindFirstObjectByType<UnityBridge>();
            if (existing != null) { instance = existing; return instance; }
            var bridgeObject = new GameObject("UnityBridge");
            if (!Application.isPlaying) bridgeObject.hideFlags = HideFlags.DontSave;
            instance = bridgeObject.AddComponent<UnityBridge>();
            if (Application.isPlaying) DontDestroyOnLoad(bridgeObject);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            gameObject.name = "UnityBridge";
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            SendToRN(BuildEnvelope("BridgeReady", "event", null, "{\"ready\":true}"));
        }

        // ── Entry point from RN ───────────────────────────────────────────────────────

        public void ReceiveFromRN(string envelopeJson)
        {
            if (string.IsNullOrWhiteSpace(envelopeJson))
            {
                SendEditorError(null, null, "invalid_json", "Received an empty bridge message.");
                return;
            }

            BridgeEnvelopeHeader envelope;
            try   { envelope = JsonUtility.FromJson<BridgeEnvelopeHeader>(envelopeJson); }
            catch (Exception ex)
            {
                SendEditorError(null, null, "invalid_json", ex.Message);
                return;
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.name))
            {
                SendEditorError(null, null, "invalid_json", "Bridge message is missing a command name.");
                return;
            }

            if (envelope.schemaVersion != SchemaVersion)
            {
                SendEditorError(envelope.requestId, envelope.name, "unsupported_schema",
                    $"Unsupported schema version: {envelope.schemaVersion}");
                return;
            }

            Debug.Log($"Room2Scan RN->Unity: {envelope.name}");

            switch (envelope.name)
            {
                // ── Room ──────────────────────────────────────────────────────
                case "LoadRoom":
                    var sceneJsonPath        = ExtractString(envelopeJson, "sceneInstancePath", null);
                    var objectsBaseDir       = ExtractString(envelopeJson, "objectsBasePath",   null);
                    var deliveryManifestPath = ExtractString(envelopeJson, "deliveryManifestPath", null);
                    var requestedRoomId      = ExtractString(envelopeJson, "roomId", DefaultRoomId);

                    if (loadRoomInProgress && string.Equals(loadingRoomId, requestedRoomId, StringComparison.Ordinal))
                    {
                        Debug.Log($"Room2Scan UnityBridge: ignoring duplicate LoadRoom for '{requestedRoomId}' while it is still loading.");
                        break;
                    }

                    loadRoomInProgress = true;
                    loadingRoomId = requestedRoomId;
                    var roomManager = RoomManager.GetOrCreateInstance();
                    Action<RoomLoadResult> onRoomLoaded = result =>
                    {
                        try
                        {
                            if (result.SuccessFlag)
                            {
                                currentRoomId = result.RoomId;
                                FurnitureManager.GetOrCreateInstance().ClearAll();
                                AttachOrbitCamera(result.Bounds);
                                FurnitureManager.SetRoomBounds(result.Bounds);
                                EnsureFurnitureDragController();
                                if (!string.IsNullOrWhiteSpace(deliveryManifestPath))
                                    LoadDeliveryManifestAndNotify(deliveryManifestPath);
                                else if (!string.IsNullOrWhiteSpace(sceneJsonPath))
                                    LoadSceneInstanceAndNotify(sceneJsonPath, objectsBaseDir);
                            }
                            SendRoomLoaded(envelope.requestId, result);
                        }
                        finally
                        {
                            loadRoomInProgress = false;
                            loadingRoomId = null;
                        }
                    };

                    if (!string.IsNullOrWhiteSpace(deliveryManifestPath))
                        roomManager.LoadDeliveryRoomShellFromBridgeEnvelope(envelopeJson, deliveryManifestPath, onRoomLoaded);
                    else
                        roomManager.LoadRoomFromBridgeEnvelope(envelopeJson, onRoomLoaded);
                    break;

                case "CreateProceduralRoom":
                    HandleCreateProceduralRoom(envelopeJson, envelope.requestId);
                    break;

                case "ResetEditor":
                    RoomManager.GetOrCreateInstance().ClearRoom();
                    FurnitureManager.GetOrCreateInstance().ClearAll();
                    currentRoomId = DefaultRoomId;
                    SendToRN(BuildEnvelope("EditorReset", "event", envelope.requestId, "{\"success\":true}"));
                    break;

                // ── Catalog ───────────────────────────────────────────────────
                case "LoadFurnitureCatalog":
                    SendFurnitureCatalogLoaded(envelope.requestId,
                        ExtractString(envelopeJson, "catalogId", "mock_catalog"), 0);
                    break;

                // ── Furniture CRUD ────────────────────────────────────────────
                case "AddFurniture":
                    HandleAddFurniture(envelopeJson, envelope.requestId);
                    break;

                case "SelectFurniture":
                    HandleSelectFurniture(envelopeJson, envelope.requestId);
                    break;

                case "DuplicateSelected":
                    HandleDuplicateSelected(envelope.requestId);
                    break;

                case "RotateSelected":
                    HandleRotateSelected(envelopeJson, envelope.requestId);
                    break;

                case "DeleteSelected":
                    HandleDeleteSelected(envelope.requestId);
                    break;

                // ── Move (absolute position from RN) ─────────────────────────
                case "MoveFurniture":
                    HandleMoveFurniture(envelopeJson, envelope.requestId);
                    break;

                // ── Visibility / lock ─────────────────────────────────────────
                case "SetObjectVisibility":
                    HandleSetVisibility(envelopeJson, envelope.requestId);
                    break;

                case "SetObjectLocked":
                    HandleSetLocked(envelopeJson, envelope.requestId);
                    break;

                // ── Editor state ──────────────────────────────────────────────
                case "SetActiveTool":
                    activeTool = ExtractString(envelopeJson, "toolId", "select");
                    Debug.Log($"Room2Scan Bridge: active tool = {activeTool}");
                    SendToRN(BuildEnvelope("ToolChanged", "event", envelope.requestId,
                        $"{{\"toolId\":\"{EscapeJson(activeTool)}\"}}"));
                    break;

                case "SetViewMode":
                    var mode = ExtractString(envelopeJson, "mode", "3D");
                    Debug.Log($"Room2Scan Bridge: view mode = {mode}");
                    var viewCam = Camera.main ?? FindFirstObjectByType<Camera>();
                    if (viewCam != null)
                    {
                        var orbit = viewCam.GetComponent<OrbitCameraController>();
                        if (orbit != null) orbit.SetTopDown(mode == "2D");
                    }
                    SendToRN(BuildEnvelope("ViewModeChanged", "event", envelope.requestId,
                        $"{{\"mode\":\"{EscapeJson(mode)}\"}}"));
                    break;

                case "SetSnapEnabled":
                    snapEnabled = ExtractBool(envelopeJson, "enabled", false);
                    Debug.Log($"Room2Scan Bridge: snap = {snapEnabled}");
                    // TODO P2: wire into floor-snap placement logic
                    SendToRN(BuildEnvelope("SnapChanged", "event", envelope.requestId,
                        $"{{\"enabled\":{(snapEnabled ? "true" : "false")}}}"));
                    break;

                // ── Undo / Redo ───────────────────────────────────────────────
                case "UndoAction":
                    // TODO P2: implement undo stack
                    Debug.Log("Room2Scan Bridge: UndoAction (no-op in P1)");
                    SendToRN(BuildEnvelope("UndoResult", "event", envelope.requestId,
                        "{\"success\":false,\"reason\":\"undo_not_implemented\"}"));
                    break;

                case "RedoAction":
                    // TODO P2: implement undo stack
                    Debug.Log("Room2Scan Bridge: RedoAction (no-op in P1)");
                    SendToRN(BuildEnvelope("RedoResult", "event", envelope.requestId,
                        "{\"success\":false,\"reason\":\"redo_not_implemented\"}"));
                    break;

                // ── Layout ────────────────────────────────────────────────────
                case "SaveLayout":
                    SendLayoutSaved(envelope.requestId);
                    break;

                default:
                    SendEditorError(envelope.requestId, envelope.name, "unknown_command",
                        $"Unknown command: {envelope.name}");
                    break;
            }
        }

        // ── Scene-instance auto-load ──────────────────────────────────────────────────

        /// <summary>
        /// Fire-and-forget: loads all furniture from a ReplicaCAD scene_instance JSON,
        /// then broadcasts ObjectListUpdated so the RN editor list stays in sync.
        /// </summary>
        private static async void LoadSceneInstanceAndNotify(string jsonPath, string objectsDir)
        {
            Debug.Log($"Room2Scan Bridge: auto-loading scene instance from '{jsonPath}'");
            var count = await SceneInstanceLoader.LoadAsync(jsonPath, objectsDir ?? "objects");
            Debug.Log($"Room2Scan Bridge: scene instance placed {count} objects.");
            SendObjectListUpdated();
        }

        private static async void LoadDeliveryManifestAndNotify(string manifestPath)
        {
            Debug.Log($"Room2Scan Bridge: loading delivery manifest from '{manifestPath}'");
            var result = await DeliveryManifestLoader.LoadAsync(manifestPath);
            if (!result.Success)
            {
                Debug.LogWarning($"Room2Scan Bridge: delivery manifest load failed: {result.ErrorMessage}");
                SendToRN(BuildEnvelope("EditorError", "event", null,
                    "{\"failedCommand\":\"LoadRoom\"}",
                    $"\"error\":{{\"code\":\"delivery_manifest_load_failed\",\"message\":\"{EscapeJson(result.ErrorMessage)}\"}}"));
                return;
            }

            Debug.Log($"Room2Scan Bridge: delivery manifest placed {result.FurnitureCount} objects and {result.StaticColliderCount} static colliders.");
            SendObjectListUpdated();
        }

        // ── Command handlers ──────────────────────────────────────────────────────────

        private void HandleAddFurniture(string envelopeJson, string requestId)
        {
            var envelope = JsonUtility.FromJson<BridgeAddFurnitureEnvelope>(envelopeJson);
            var p = envelope?.payload;
            if (p == null || string.IsNullOrWhiteSpace(p.instanceId))
            {
                SendEditorError(requestId, "AddFurniture", "invalid_payload",
                    "AddFurniture requires payload.instanceId.");
                return;
            }

            var position = p.position != null
                ? new Vector3(p.position.x, p.position.y, p.position.z)
                : Vector3.zero;

            var fm     = FurnitureManager.GetOrCreateInstance();
            var result = fm.AddFurniture(p.instanceId, p.catalogId ?? "unknown", position);

            if (result.Success)
            {
                SendFurnitureAdded(requestId, result.InstanceId, result.Position,
                    p.catalogId ?? "unknown", false /* not a duplicate */);
                SendObjectListUpdated();
            }
            else
            {
                SendEditorError(requestId, "AddFurniture", result.ErrorCode, result.ErrorMessage);
            }
        }

        private void HandleSelectFurniture(string envelopeJson, string requestId)
        {
            var instanceId = ExtractString(envelopeJson, "instanceId", null);
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                SendEditorError(requestId, "SelectFurniture", "invalid_payload",
                    "SelectFurniture requires payload.instanceId.");
                return;
            }

            var fm      = FurnitureManager.GetOrCreateInstance();
            var success = fm.SelectFurniture(instanceId);

            // Include live transform so RN can show real values in ObjectDetail
            var transform = fm.GetSelectedTransform();
            var transformJson = transform.HasValue
                ? $",\"position\":{{\"x\":{FormatFloat(transform.Value.Position.x)}" +
                  $",\"y\":{FormatFloat(transform.Value.Position.y)}" +
                  $",\"z\":{FormatFloat(transform.Value.Position.z)}}}," +
                  $"\"rotationYDeg\":{FormatFloat(transform.Value.RotationYDeg)}," +
                  $"\"scale\":{FormatFloat(transform.Value.Scale)}"
                : string.Empty;

            SendToRN(BuildEnvelope("FurnitureSelected", "event", requestId,
                $"{{\"instanceId\":\"{EscapeJson(instanceId)}\",\"success\":{(success ? "true" : "false")}{transformJson}}}"));
        }

        private void HandleDuplicateSelected(string requestId)
        {
            var fm     = FurnitureManager.GetOrCreateInstance();
            var result = fm.DuplicateSelected();

            if (result.Success)
            {
                // Reuse FurnitureAdded so RN adds the new item to its list
                SendFurnitureAdded(requestId, result.NewInstanceId, result.Position,
                    null /* catalogId unknown here — RN infers from original */, true /* isDuplicate */);
                SendObjectListUpdated();
            }
            else
            {
                SendEditorError(requestId, "DuplicateSelected", result.ErrorCode, result.ErrorMessage);
            }
        }

        private void HandleRotateSelected(string envelopeJson, string requestId)
        {
            var envelope  = JsonUtility.FromJson<BridgeRotateEnvelope>(envelopeJson);
            var deltaDeg  = envelope?.payload?.deltaDeg ?? 45f;
            var fm        = FurnitureManager.GetOrCreateInstance();
            var success   = fm.RotateSelected(deltaDeg);

            // Return updated transform so RN display stays in sync
            var transform = fm.GetSelectedTransform();
            var transformJson = transform.HasValue
                ? $",\"position\":{{\"x\":{FormatFloat(transform.Value.Position.x)}" +
                  $",\"y\":{FormatFloat(transform.Value.Position.y)}" +
                  $",\"z\":{FormatFloat(transform.Value.Position.z)}}}," +
                  $"\"rotationYDeg\":{FormatFloat(transform.Value.RotationYDeg)}," +
                  $"\"scale\":{FormatFloat(transform.Value.Scale)}"
                : string.Empty;

            SendToRN(BuildEnvelope("FurnitureTransformed", "event", requestId,
                $"{{\"success\":{(success ? "true" : "false")}" +
                $",\"instanceId\":\"{EscapeJson(fm.SelectedInstanceId ?? string.Empty)}\"{transformJson}}}"));
        }

        private void HandleDeleteSelected(string requestId)
        {
            var fm        = FurnitureManager.GetOrCreateInstance();
            var deletedId = fm.SelectedInstanceId; // capture before delete clears it
            var success   = fm.DeleteSelected();

            SendToRN(BuildEnvelope("FurnitureDeleted", "event", requestId,
                $"{{\"success\":{(success ? "true" : "false")}" +
                $",\"instanceId\":\"{EscapeJson(deletedId ?? string.Empty)}\"}}"));

            if (success) SendObjectListUpdated();
        }

        private void HandleSetVisibility(string envelopeJson, string requestId)
        {
            var instanceId = ExtractString(envelopeJson, "instanceId", null);
            var visible    = ExtractBool(envelopeJson, "visible", true);

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                SendEditorError(requestId, "SetObjectVisibility", "invalid_payload",
                    "SetObjectVisibility requires payload.instanceId.");
                return;
            }

            var success = FurnitureManager.GetOrCreateInstance().SetVisibility(instanceId, visible);
            SendToRN(BuildEnvelope("VisibilityChanged", "event", requestId,
                $"{{\"instanceId\":\"{EscapeJson(instanceId)}\",\"visible\":{(visible ? "true" : "false")},\"success\":{(success ? "true" : "false")}}}"));
        }

        private void HandleSetLocked(string envelopeJson, string requestId)
        {
            var instanceId = ExtractString(envelopeJson, "instanceId", null);
            var locked     = ExtractBool(envelopeJson, "locked", false);

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                SendEditorError(requestId, "SetObjectLocked", "invalid_payload",
                    "SetObjectLocked requires payload.instanceId.");
                return;
            }

            var success = FurnitureManager.GetOrCreateInstance().SetLocked(instanceId, locked);
            SendToRN(BuildEnvelope("LockChanged", "event", requestId,
                $"{{\"instanceId\":\"{EscapeJson(instanceId)}\",\"locked\":{(locked ? "true" : "false")},\"success\":{(success ? "true" : "false")}}}"));
        }

        // ── Outbound event builders ──────────────────────────────────────────────────

        private static void SendFurnitureAdded(
            string requestId, string instanceId, Vector3 position,
            string catalogId, bool isDuplicate)
        {
            var catPart = string.IsNullOrWhiteSpace(catalogId)
                ? string.Empty
                : $",\"catalogId\":\"{EscapeJson(catalogId)}\"";

            var payload = "{" +
                $"\"instanceId\":\"{EscapeJson(instanceId)}\"," +
                $"\"success\":true," +
                $"\"isDuplicate\":{(isDuplicate ? "true" : "false")}" +
                catPart +
                $",\"position\":{{\"x\":{FormatFloat(position.x)}" +
                $",\"y\":{FormatFloat(position.y)}" +
                $",\"z\":{FormatFloat(position.z)}}}" +
                $",\"rotationYDeg\":0,\"scale\":0.8" +
                "}";
            SendToRN(BuildEnvelope("FurnitureAdded", "event", requestId, payload));
        }

        /// <summary>Sends the full current furniture list so RN can reconcile its object list.</summary>
        private static void SendObjectListUpdated()
        {
            var items  = FurnitureManager.GetOrCreateInstance().GetLayoutItems();
            var sb     = new StringBuilder("[");
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var t    = item;
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append($"\"instanceId\":\"{EscapeJson(item.InstanceId)}\",");
                sb.Append($"\"catalogId\":\"{EscapeJson(item.CatalogId)}\",");
                sb.Append($"\"position\":{{\"x\":{FormatFloat(item.Position.x)}," +
                          $"\"y\":{FormatFloat(item.Position.y)}," +
                          $"\"z\":{FormatFloat(item.Position.z)}}},");
                sb.Append($"\"rotationYDeg\":{FormatFloat(item.RotationYDeg)},");
                sb.Append($"\"scale\":{FormatFloat(item.Scale)},");
                sb.Append($"\"visible\":{(item.Visible ? "true" : "false")},");
                sb.Append($"\"locked\":{(item.Locked ? "true" : "false")}");
                sb.Append('}');
            }
            sb.Append(']');
            SendToRN(BuildEnvelope("ObjectListUpdated", "event", null,
                $"{{\"items\":{sb}}}"));
        }

        // ── P4: Procedural room ───────────────────────────────────────────────────────

        private void HandleCreateProceduralRoom(string envelopeJson, string requestId)
        {
            // Clear any existing room first
            RoomManager.GetOrCreateInstance().ClearRoom();
            FurnitureManager.GetOrCreateInstance().ClearAll();

            var spec = ProceduralRoomBuilder.ParseFromJson(envelopeJson);
            var result = ProceduralRoomBuilder.Build(spec);

            if (!result.Success)
            {
                SendEditorError(requestId, "CreateProceduralRoom", "build_failed", result.ErrorMessage);
                return;
            }

            currentRoomId = spec.RoomId;
            RoomManager.GetOrCreateInstance().AdoptGeneratedRoom(spec.RoomId, result.RoomRoot);

            // Attach orbit camera, set room bounds, attach drag controller
            AttachOrbitCamera(result.Bounds);
            FurnitureManager.SetRoomBounds(result.Bounds);
            EnsureFurnitureDragController();

            // Notify RN
            var b = result.Bounds;
            var payload =
                "{" +
                $"\"roomId\":\"{EscapeJson(spec.RoomId)}\"," +
                $"\"name\":\"{EscapeJson(spec.Name)}\"," +
                $"\"width\":{spec.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"length\":{spec.Length.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"height\":{spec.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}," +
                "\"success\":true," +
                $"\"bounds\":{{\"min\":{{\"x\":{FormatFloat(b.min.x)},\"y\":{FormatFloat(b.min.y)},\"z\":{FormatFloat(b.min.z)}}}," +
                $"\"max\":{{\"x\":{FormatFloat(b.max.x)},\"y\":{FormatFloat(b.max.y)},\"z\":{FormatFloat(b.max.z)}}}}}" +
                "}";

            // Also fire RoomLoaded so existing UnityEditorScreen code recognises the room
            var roomLoadedPayload =
                "{" +
                $"\"roomId\":\"{EscapeJson(spec.RoomId)}\"," +
                "\"success\":true," +
                $"\"bounds\":{{\"min\":{{\"x\":{FormatFloat(b.min.x)},\"y\":{FormatFloat(b.min.y)},\"z\":{FormatFloat(b.min.z)}}}," +
                $"\"max\":{{\"x\":{FormatFloat(b.max.x)},\"y\":{FormatFloat(b.max.y)},\"z\":{FormatFloat(b.max.z)}}}}}" +
                "}";

            SendToRN(BuildEnvelope("ProceduralRoomCreated", "event", requestId, payload));
            SendToRN(BuildEnvelope("RoomLoaded",            "event", requestId, roomLoadedPayload));

            Debug.Log($"[UnityBridge] Procedural room '{spec.Name}' built: {spec.Width}x{spec.Length}x{spec.Height}m");
        }

        // ── P5: Move + drag event helpers ─────────────────────────────────────────────

        private void HandleMoveFurniture(string envelopeJson, string requestId)
        {
            var x = ExtractFloat(envelopeJson, "x", 0f);
            var y = ExtractFloat(envelopeJson, "y", 0f);
            var z = ExtractFloat(envelopeJson, "z", 0f);

            var fm      = FurnitureManager.GetOrCreateInstance();
            var success = fm.MoveSelected(x, y, z);
            var t       = fm.GetSelectedTransform();
            var pos     = t.HasValue ? t.Value.Position : new Vector3(x, y, z);
            var ry      = t.HasValue ? t.Value.RotationYDeg : 0f;
            var scale   = t.HasValue ? t.Value.Scale : 1f;

            SendToRN(BuildEnvelope("FurnitureTransformed", "event", requestId,
                "{\"success\":" + (success ? "true" : "false") +
                ",\"instanceId\":\"" + EscapeJson(fm.SelectedInstanceId ?? string.Empty) + "\"" +
                ",\"position\":{\"x\":" + FormatFloat(pos.x) +
                ",\"y\":" + FormatFloat(pos.y) +
                ",\"z\":" + FormatFloat(pos.z) + "}" +
                ",\"rotationYDeg\":" + FormatFloat(ry) +
                ",\"scale\":" + FormatFloat(scale) + "}"));
        }

        /// <summary>
        /// Ensures a FurnitureDragController component is attached to the main camera.
        /// Safe to call multiple times — checks for existing component first.
        /// </summary>
        private static void EnsureFurnitureDragController()
        {
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;
            if (cam.GetComponent<FurnitureDragController>() == null)
                cam.gameObject.AddComponent<FurnitureDragController>();
        }

        // ── Public outbound events (called by FurnitureDragController) ───────────────

        /// <summary>Broadcasts FurnitureSelected to RN (e.g. after tap-to-select).</summary>
        public void SendFurnitureSelectedEvent(string instanceId, FurnitureManager.TransformData? t)
        {
            var tp = t.HasValue
                ? ",\"position\":{\"x\":" + FormatFloat(t.Value.Position.x) +
                  ",\"y\":" + FormatFloat(t.Value.Position.y) +
                  ",\"z\":" + FormatFloat(t.Value.Position.z) + "}" +
                  ",\"rotationYDeg\":" + FormatFloat(t.Value.RotationYDeg) +
                  ",\"scale\":" + FormatFloat(t.Value.Scale)
                : string.Empty;

            SendToRN(BuildEnvelope("FurnitureSelected", "event", null,
                "{\"instanceId\":\"" + EscapeJson(instanceId) + "\",\"success\":true" + tp + "}"));
        }

        /// <summary>Broadcasts FurnitureTransformed to RN after a drag completes.</summary>
        public void SendFurnitureTransformedEvent(string instanceId, FurnitureManager.TransformData t)
        {
            SendToRN(BuildEnvelope("FurnitureTransformed", "event", null,
                "{\"success\":true" +
                ",\"instanceId\":\"" + EscapeJson(instanceId) + "\"" +
                ",\"position\":{\"x\":" + FormatFloat(t.Position.x) +
                ",\"y\":" + FormatFloat(t.Position.y) +
                ",\"z\":" + FormatFloat(t.Position.z) + "}" +
                ",\"rotationYDeg\":" + FormatFloat(t.RotationYDeg) +
                ",\"scale\":" + FormatFloat(t.Scale) + "}"));
        }

        /// <summary>Broadcasts CollisionStatus to RN (throttled — only on state change).</summary>
        public void SendCollisionStatusEvent(string instanceId, bool hasCollision)
        {
            SendToRN(BuildEnvelope("CollisionStatus", "event", null,
                "{\"instanceId\":\"" + EscapeJson(instanceId) + "\"" +
                ",\"hasCollision\":" + (hasCollision ? "true" : "false") + "}"));
        }

        /// <summary>Position the main camera to see the whole room isometrically.</summary>
        private static void AttachOrbitCamera(Bounds bounds)
        {
            var cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) return;

            cam.orthographic = false;
            cam.fieldOfView  = 50f;

            // Remove existing controller
            var existing = cam.GetComponent<OrbitCameraController>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var controller = cam.gameObject.AddComponent<OrbitCameraController>();
            var pivot  = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.35f, bounds.center.z);
            var fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            var half   = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
            var dist   = Mathf.Clamp((half / Mathf.Tan(fovRad * 0.5f)) * 1.15f, 3f, 30f);
            controller.SetPivotAndDistance(pivot, dist, initialYaw: -135f, initialPitch: 45f);
        }

        private void SendRoomLoaded(string requestId, RoomLoadResult result)
        {
            var success = result.SuccessFlag ? "true" : "false";
            var roomId  = string.IsNullOrWhiteSpace(result.RoomId) ? DefaultRoomId : result.RoomId;
            var payload = new StringBuilder("{");
            payload.Append($"\"roomId\":\"{EscapeJson(roomId)}\",");
            payload.Append($"\"success\":{success},");
            payload.Append($"\"meshUri\":\"{EscapeJson(result.MeshUri)}\"");

            if (result.SuccessFlag)
            {
                var b = result.Bounds;
                payload.Append($",\"normalizedMeshUri\":\"{EscapeJson(result.NormalizedMeshUri)}\"");
                payload.Append($",\"colliderCount\":{result.ColliderCount}");
                payload.Append($",\"bounds\":{{\"min\":{{\"x\":{FormatFloat(b.min.x)},\"y\":{FormatFloat(b.min.y)},\"z\":{FormatFloat(b.min.z)}}}," +
                               $"\"max\":{{\"x\":{FormatFloat(b.max.x)},\"y\":{FormatFloat(b.max.y)},\"z\":{FormatFloat(b.max.z)}}}}}");
            }
            else
            {
                payload.Append($",\"error\":{{\"code\":\"{EscapeJson(result.ErrorCode)}\",\"message\":\"{EscapeJson(result.ErrorMessage)}\"}}");
            }

            payload.Append('}');
            SendToRN(BuildEnvelope("RoomLoaded", "event", requestId, payload.ToString()));
        }

        private static void SendFurnitureCatalogLoaded(string requestId, string catalogId, int itemCount)
        {
            SendToRN(BuildEnvelope("FurnitureCatalogLoaded", "event", requestId,
                $"{{\"catalogId\":\"{EscapeJson(catalogId)}\",\"success\":true,\"itemCount\":{itemCount}}}"));
        }

        private void SendLayoutSaved(string requestId)
        {
            var savedAt    = DateTime.UtcNow.ToString("o");
            var layoutItems = FurnitureManager.GetOrCreateInstance().GetLayoutItems();

            var itemsSb = new StringBuilder("[");
            for (var i = 0; i < layoutItems.Count; i++)
            {
                var item = layoutItems[i];
                if (i > 0) itemsSb.Append(',');
                itemsSb.Append('{');
                itemsSb.Append($"\"instanceId\":\"{EscapeJson(item.InstanceId)}\",");
                itemsSb.Append($"\"catalogId\":\"{EscapeJson(item.CatalogId)}\",");
                itemsSb.Append($"\"position\":{{\"x\":{FormatFloat(item.Position.x)}" +
                               $",\"y\":{FormatFloat(item.Position.y)}" +
                               $",\"z\":{FormatFloat(item.Position.z)}}},");
                itemsSb.Append($"\"rotationYDeg\":{FormatFloat(item.RotationYDeg)},");
                itemsSb.Append($"\"scale\":{FormatFloat(item.Scale)},");
                itemsSb.Append($"\"visible\":{(item.Visible ? "true" : "false")},");
                itemsSb.Append($"\"locked\":{(item.Locked ? "true" : "false")}");
                itemsSb.Append('}');
            }
            itemsSb.Append(']');

            var layoutJson =
                "{\"layout\":{" +
                "\"schemaVersion\":\"layout-json/v1\"," +
                $"\"layoutId\":\"layout_{Guid.NewGuid():N}\"," +
                $"\"roomId\":\"{EscapeJson(currentRoomId)}\"," +
                "\"roomSchemaVersion\":\"room-json/v1\"," +
                $"\"editorSessionId\":\"unity_session_{Guid.NewGuid():N}\"," +
                $"\"savedAt\":\"{savedAt}\"," +
                "\"coordinateSystem\":{\"unit\":\"meter\",\"handedness\":\"left\",\"upAxis\":\"+Y\",\"forwardAxis\":\"+Z\"}," +
                $"\"items\":{itemsSb}," +
                "\"validation\":{\"isValid\":true,\"invalidItemIds\":[],\"warnings\":[]}," +
                "\"extensions\":{\"source\":\"p1\"}" +
                "}}";

            SendToRN(BuildEnvelope("LayoutSaved", "event", requestId, layoutJson));
        }

        private void SendEditorError(string requestId, string failedCommand, string code, string message)
        {
            var payload = string.IsNullOrWhiteSpace(failedCommand)
                ? "{}"
                : $"{{\"failedCommand\":\"{EscapeJson(failedCommand)}\"}}";
            var error = $"\"error\":{{\"code\":\"{EscapeJson(code)}\",\"message\":\"{EscapeJson(message)}\"}}";
            SendToRN(BuildEnvelope("EditorError", "event", requestId, payload, error));
        }

        // ── Wire to @azesmway/react-native-unity ──────────────────────────────────────

        private static void SendToRN(string envelopeJson)
        {
            if (TrySendViaUnityMessageManager(envelopeJson)) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var vmr = new AndroidJavaClass("com.azesmwayreactnativeunity.ReactNativeUnityViewManager");
                vmr.CallStatic("sendMessageToMobileApp", envelopeJson);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Room2Scan UnityBridge could not send via ReactNativeUnityViewManager: {ex.Message}");
            }
#endif
            Debug.Log($"Room2Scan Unity->RN: {envelopeJson}");
        }

        private static bool TrySendViaUnityMessageManager(string envelopeJson)
        {
            try
            {
                var t = FindType("UnityMessageManager");
                if (t == null) return false;
                var instance = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return false;
                var method = t.GetMethod("SendMessageToRN", new[] { typeof(string) });
                if (method == null) return false;
                method.Invoke(instance, new object[] { envelopeJson });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Room2Scan UnityBridge: UnityMessageManager send failed: {ex.Message}");
                return false;
            }
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        // ── JSON helpers ──────────────────────────────────────────────────────────────

        private static string BuildEnvelope(string name, string kind, string requestId, string payloadJson, string extraField = null)
        {
            var msgId   = $"unity_{Guid.NewGuid():N}";
            var reqPart = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $"\"requestId\":\"{EscapeJson(requestId)}\",";
            var extra   = string.IsNullOrWhiteSpace(extraField) ? string.Empty : $",{extraField}";
            return "{" +
                   $"\"schemaVersion\":\"{SchemaVersion}\"," +
                   $"\"messageId\":\"{msgId}\"," +
                   reqPart +
                   $"\"kind\":\"{kind}\"," +
                   "\"direction\":\"unity_to_rn\"," +
                   $"\"name\":\"{name}\"," +
                   $"\"sentAt\":\"{DateTime.UtcNow:o}\"," +
                   $"\"payload\":{payloadJson}" +
                   extra +
                   "}";
        }

        private static string ExtractString(string json, string key, string fallback)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"])*)\"");
            return m.Success ? UnescapeJson(m.Groups["v"].Value) : fallback;
        }

        private static float ExtractFloat(string json, string key, float fallback)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*([\\d.\\-]+)");
            return m.Success
                ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : fallback;
        }

        private static bool ExtractBool(string json, string key, bool fallback)
        {
            var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(?<v>true|false)");
            if (!m.Success) return fallback;
            return m.Groups["v"].Value == "true";
        }

        private static string EscapeJson(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            return v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string UnescapeJson(string v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static string FormatFloat(float value) =>
            value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        // ── Deserialization classes ────────────────────────────────────────────────────

        [Serializable]
        private sealed class BridgeEnvelopeHeader
        {
            public string schemaVersion;
            public string messageId;
            public string requestId;
            public string kind;
            public string direction;
            public string name;
            public string sentAt;
        }

        [Serializable]
        private sealed class BridgeAddFurnitureEnvelope
        {
            public AddFurniturePayload payload;

            [Serializable]
            public sealed class AddFurniturePayload
            {
                public string     instanceId;
                public string     catalogId;
                public Vector3Json position;
            }
        }

        [Serializable]
        private sealed class BridgeRotateEnvelope
        {
            public RotatePayload payload;

            [Serializable]
            public sealed class RotatePayload { public float deltaDeg; }
        }

        [Serializable]
        private sealed class Vector3Json
        {
            public float x;
            public float y;
            public float z;
        }
    }
}

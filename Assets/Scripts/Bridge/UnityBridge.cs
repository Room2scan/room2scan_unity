using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Room2Scan.Rooms;
using UnityEngine;

namespace Room2Scan.Bridge
{
    public sealed class UnityBridge : MonoBehaviour
    {
        private const string SchemaVersion = "unity-bridge/v1";
        private const string DefaultRoomId = "mock_room";

        private static UnityBridge instance;
        private string currentRoomId = DefaultRoomId;

        public static UnityBridge Instance => instance;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreateInstance();
        }

        public static UnityBridge GetOrCreateInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            var existing = FindFirstObjectByType<UnityBridge>();
            if (existing != null)
            {
                instance = existing;
                return instance;
            }

            var bridgeObject = new GameObject("UnityBridge");
            if (!Application.isPlaying)
            {
                bridgeObject.hideFlags = HideFlags.DontSave;
            }

            instance = bridgeObject.AddComponent<UnityBridge>();
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(bridgeObject);
            }

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
            gameObject.name = "UnityBridge";
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        public void ReceiveFromRN(string envelopeJson)
        {
            if (string.IsNullOrWhiteSpace(envelopeJson))
            {
                SendEditorError(null, null, "invalid_json", "Received an empty bridge message.");
                return;
            }

            BridgeEnvelopeHeader envelope;
            try
            {
                envelope = JsonUtility.FromJson<BridgeEnvelopeHeader>(envelopeJson);
            }
            catch (Exception exception)
            {
                SendEditorError(null, null, "invalid_json", exception.Message);
                return;
            }

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.name))
            {
                SendEditorError(null, null, "invalid_json", "Bridge message is missing a command name.");
                return;
            }

            if (envelope.schemaVersion != SchemaVersion)
            {
                SendEditorError(envelope.requestId, envelope.name, "unsupported_schema", $"Unsupported schema version: {envelope.schemaVersion}");
                return;
            }

            Debug.Log($"Room2Scan RN->Unity: {envelope.name}");

            switch (envelope.name)
            {
                case "LoadRoom":
                    RoomManager.GetOrCreateInstance().LoadRoomFromBridgeEnvelope(
                        envelopeJson,
                        result =>
                        {
                            if (result.SuccessFlag)
                            {
                                currentRoomId = result.RoomId;
                            }

                            SendRoomLoaded(envelope.requestId, result);
                        });
                    break;
                case "LoadFurnitureCatalog":
                    SendFurnitureCatalogLoaded(envelope.requestId, ExtractString(envelopeJson, "catalogId", "mock_catalog"), 0);
                    break;
                case "AddFurniture":
                    HandleAddFurniture(envelopeJson, envelope.requestId);
                    break;
                case "SelectFurniture":
                    HandleSelectFurniture(envelopeJson, envelope.requestId);
                    break;
                case "RotateSelected":
                    HandleRotateSelected(envelopeJson, envelope.requestId);
                    break;
                case "DeleteSelected":
                    HandleDeleteSelected(envelope.requestId);
                    break;
                case "SaveLayout":
                    SendLayoutSaved(envelope.requestId);
                    break;
                case "ResetEditor":
                    RoomManager.GetOrCreateInstance().ClearRoom();
                    FurnitureManager.GetOrCreateInstance().ClearAll();
                    currentRoomId = DefaultRoomId;
                    SendEditorReset(envelope.requestId);
                    break;
                default:
                    SendEditorError(envelope.requestId, envelope.name, "unknown_command", $"Unknown command: {envelope.name}");
                    break;
            }
        }

        private void HandleAddFurniture(string envelopeJson, string requestId)
        {
            var envelope = JsonUtility.FromJson<BridgeAddFurnitureEnvelope>(envelopeJson);
            var p = envelope?.payload;
            if (p == null || string.IsNullOrWhiteSpace(p.instanceId))
            {
                SendEditorError(requestId, "AddFurniture", "invalid_payload", "AddFurniture requires payload.instanceId.");
                return;
            }

            var position = p.position != null ? new Vector3(p.position.x, p.position.y, p.position.z) : Vector3.zero;
            var result = FurnitureManager.GetOrCreateInstance().AddFurniture(p.instanceId, p.catalogId ?? "unknown", position);
            if (result.Success)
                SendFurnitureAdded(requestId, result.InstanceId, result.Position);
            else
                SendEditorError(requestId, "AddFurniture", result.ErrorCode, result.ErrorMessage);
        }

        private void HandleSelectFurniture(string envelopeJson, string requestId)
        {
            var instanceId = ExtractString(envelopeJson, "instanceId", null);
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                SendEditorError(requestId, "SelectFurniture", "invalid_payload", "SelectFurniture requires payload.instanceId.");
                return;
            }

            var success = FurnitureManager.GetOrCreateInstance().SelectFurniture(instanceId);
            SendToRN(BuildEnvelope("FurnitureSelected", "event", requestId,
                $"{{\"instanceId\":\"{EscapeJson(instanceId)}\",\"success\":{(success ? "true" : "false")}}}"));
        }

        private void HandleRotateSelected(string envelopeJson, string requestId)
        {
            var envelope = JsonUtility.FromJson<BridgeRotateEnvelope>(envelopeJson);
            var deltaDeg = envelope?.payload?.deltaDeg ?? 45f;
            var success = FurnitureManager.GetOrCreateInstance().RotateSelected(deltaDeg);
            SendToRN(BuildEnvelope("FurnitureTransformed", "event", requestId,
                $"{{\"success\":{(success ? "true" : "false")}}}"));
        }

        private void HandleDeleteSelected(string requestId)
        {
            var success = FurnitureManager.GetOrCreateInstance().DeleteSelected();
            SendToRN(BuildEnvelope("FurnitureDeleted", "event", requestId,
                $"{{\"success\":{(success ? "true" : "false")}}}"));
        }

        private static void SendFurnitureAdded(string requestId, string instanceId, Vector3 position)
        {
            var payload = "{" +
                $"\"instanceId\":\"{EscapeJson(instanceId)}\"," +
                "\"success\":true," +
                $"\"position\":{{\"x\":{FormatFloat(position.x)},\"y\":{FormatFloat(position.y)},\"z\":{FormatFloat(position.z)}}}" +
                "}";
            SendToRN(BuildEnvelope("FurnitureAdded", "event", requestId, payload));
        }

        private void SendRoomLoaded(string requestId, RoomLoadResult result)
        {
            var success = result.SuccessFlag ? "true" : "false";
            var roomId = string.IsNullOrWhiteSpace(result.RoomId) ? DefaultRoomId : result.RoomId;
            var payload =
                "{" +
                $"\"roomId\":\"{EscapeJson(roomId)}\"," +
                $"\"success\":{success}," +
                $"\"meshUri\":\"{EscapeJson(result.MeshUri)}\"";

            if (result.SuccessFlag)
            {
                var bounds = result.Bounds;
                payload +=
                    $",\"normalizedMeshUri\":\"{EscapeJson(result.NormalizedMeshUri)}\"" +
                    $",\"colliderCount\":{result.ColliderCount}" +
                    ",\"bounds\":{" +
                    $"\"min\":{{\"x\":{FormatFloat(bounds.min.x)},\"y\":{FormatFloat(bounds.min.y)},\"z\":{FormatFloat(bounds.min.z)}}}," +
                    $"\"max\":{{\"x\":{FormatFloat(bounds.max.x)},\"y\":{FormatFloat(bounds.max.y)},\"z\":{FormatFloat(bounds.max.z)}}}" +
                    "}";
            }
            else
            {
                payload +=
                    ",\"error\":{" +
                    $"\"code\":\"{EscapeJson(result.ErrorCode)}\"," +
                    $"\"message\":\"{EscapeJson(result.ErrorMessage)}\"" +
                    "}";
            }

            payload += "}";
            SendToRN(BuildEnvelope("RoomLoaded", "event", requestId, payload));
        }

        private void SendFurnitureCatalogLoaded(string requestId, string catalogId, int itemCount)
        {
            SendToRN(BuildEnvelope("FurnitureCatalogLoaded", "event", requestId, $"{{\"catalogId\":\"{EscapeJson(catalogId)}\",\"success\":true,\"itemCount\":{itemCount}}}"));
        }

        private void SendLayoutSaved(string requestId)
        {
            var savedAt = DateTime.UtcNow.ToString("o");
            var layoutItems = FurnitureManager.GetOrCreateInstance().GetLayoutItems();

            var itemsJson = new System.Text.StringBuilder("[");
            for (var i = 0; i < layoutItems.Count; i++)
            {
                var item = layoutItems[i];
                if (i > 0) itemsJson.Append(',');
                itemsJson.Append('{');
                itemsJson.Append($"\"instanceId\":\"{EscapeJson(item.InstanceId)}\",");
                itemsJson.Append($"\"catalogId\":\"{EscapeJson(item.CatalogId)}\",");
                itemsJson.Append($"\"position\":{{\"x\":{FormatFloat(item.Position.x)},\"y\":{FormatFloat(item.Position.y)},\"z\":{FormatFloat(item.Position.z)}}},");
                itemsJson.Append($"\"rotationYDeg\":{FormatFloat(item.RotationYDeg)},");
                itemsJson.Append($"\"scale\":{FormatFloat(item.Scale)}");
                itemsJson.Append('}');
            }
            itemsJson.Append(']');

            var layoutJson =
                "{" +
                "\"layout\":{" +
                "\"schemaVersion\":\"layout-json/v1\"," +
                $"\"layoutId\":\"layout_{Guid.NewGuid():N}\"," +
                $"\"roomId\":\"{EscapeJson(currentRoomId)}\"," +
                "\"roomSchemaVersion\":\"room-json/v1\"," +
                $"\"editorSessionId\":\"unity_session_{Guid.NewGuid():N}\"," +
                $"\"savedAt\":\"{savedAt}\"," +
                "\"coordinateSystem\":{\"unit\":\"meter\",\"handedness\":\"left\",\"upAxis\":\"+Y\",\"forwardAxis\":\"+Z\"}," +
                $"\"items\":{itemsJson}," +
                "\"validation\":{\"isValid\":true,\"invalidItemIds\":[],\"warnings\":[]}," +
                "\"extensions\":{\"source\":\"p1\"}" +
                "}" +
                "}";

            SendToRN(BuildEnvelope("LayoutSaved", "event", requestId, layoutJson));
        }

        private void SendEditorReset(string requestId)
        {
            SendToRN(BuildEnvelope("EditorReset", "event", requestId, "{\"success\":true}"));
        }

        private void SendEditorError(string requestId, string failedCommand, string code, string message)
        {
            var payload = string.IsNullOrWhiteSpace(failedCommand)
                ? "{}"
                : $"{{\"failedCommand\":\"{EscapeJson(failedCommand)}\"}}";
            var error = $"\"error\":{{\"code\":\"{EscapeJson(code)}\",\"message\":\"{EscapeJson(message)}\"}}";
            SendToRN(BuildEnvelope("EditorError", "event", requestId, payload, error));
        }

        private static string BuildEnvelope(string name, string kind, string requestId, string payloadJson, string extraFieldJson = null)
        {
            var messageId = $"unity_{Guid.NewGuid():N}";
            var requestIdField = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $"\"requestId\":\"{EscapeJson(requestId)}\",";
            var extra = string.IsNullOrWhiteSpace(extraFieldJson) ? string.Empty : $",{extraFieldJson}";

            return "{" +
                   $"\"schemaVersion\":\"{SchemaVersion}\"," +
                   $"\"messageId\":\"{messageId}\"," +
                   requestIdField +
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
            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
            return match.Success ? UnescapeJson(match.Groups["value"].Value) : fallback;
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string UnescapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static void SendToRN(string envelopeJson)
        {
            if (TrySendViaUnityMessageManager(envelopeJson))
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityViewManager = new AndroidJavaClass("com.azesmwayreactnativeunity.ReactNativeUnityViewManager");
                unityViewManager.CallStatic("sendMessageToMobileApp", envelopeJson);
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Room2Scan UnityBridge could not send via ReactNativeUnityViewManager: {exception.Message}");
            }
#endif

            Debug.Log($"Room2Scan Unity->RN: {envelopeJson}");
        }

        private static bool TrySendViaUnityMessageManager(string envelopeJson)
        {
            try
            {
                var managerType = FindType("UnityMessageManager");
                if (managerType == null)
                {
                    return false;
                }

                var instanceProperty = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var manager = instanceProperty?.GetValue(null);
                if (manager == null)
                {
                    return false;
                }

                var method = managerType.GetMethod("SendMessageToRN", new[] { typeof(string) });
                if (method == null)
                {
                    return false;
                }

                method.Invoke(manager, new object[] { envelopeJson });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Room2Scan UnityBridge could not send via UnityMessageManager: {exception.Message}");
                return false;
            }
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        [Serializable]
        private sealed class BridgeAddFurnitureEnvelope
        {
            public AddFurniturePayload payload;

            [Serializable]
            public sealed class AddFurniturePayload
            {
                public string instanceId;
                public string catalogId;
                public Vector3Json position;
            }
        }

        [Serializable]
        private sealed class BridgeRotateEnvelope
        {
            public RotatePayload payload;

            [Serializable]
            public sealed class RotatePayload
            {
                public float deltaDeg;
            }
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

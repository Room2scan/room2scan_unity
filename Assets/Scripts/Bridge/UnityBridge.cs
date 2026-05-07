using System;
using System.Reflection;
using System.Text.RegularExpressions;
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
                    currentRoomId = ExtractString(envelopeJson, "roomId", DefaultRoomId);
                    SendRoomLoaded(envelope.requestId, currentRoomId);
                    break;
                case "LoadFurnitureCatalog":
                    SendFurnitureCatalogLoaded(envelope.requestId, ExtractString(envelopeJson, "catalogId", "mock_catalog"), 0);
                    break;
                case "SaveLayout":
                    SendLayoutSaved(envelope.requestId);
                    break;
                case "ResetEditor":
                    currentRoomId = DefaultRoomId;
                    SendEditorReset(envelope.requestId);
                    break;
                default:
                    SendEditorError(envelope.requestId, envelope.name, "unknown_command", $"Unknown command: {envelope.name}");
                    break;
            }
        }

        private void SendRoomLoaded(string requestId, string roomId)
        {
            SendToRN(BuildEnvelope("RoomLoaded", "event", requestId, $"{{\"roomId\":\"{EscapeJson(roomId)}\",\"success\":true}}"));
        }

        private void SendFurnitureCatalogLoaded(string requestId, string catalogId, int itemCount)
        {
            SendToRN(BuildEnvelope("FurnitureCatalogLoaded", "event", requestId, $"{{\"catalogId\":\"{EscapeJson(catalogId)}\",\"success\":true,\"itemCount\":{itemCount}}}"));
        }

        private void SendLayoutSaved(string requestId)
        {
            var savedAt = DateTime.UtcNow.ToString("o");
            var layoutJson =
                "{" +
                "\"layout\":{" +
                "\"schemaVersion\":\"layout-json/v1\"," +
                "\"layoutId\":\"mock_layout_0001\"," +
                $"\"roomId\":\"{EscapeJson(currentRoomId)}\"," +
                "\"roomSchemaVersion\":\"room-json/v1\"," +
                "\"editorSessionId\":\"unity_p0_mock_session\"," +
                $"\"savedAt\":\"{savedAt}\"," +
                "\"coordinateSystem\":{\"unit\":\"meter\",\"handedness\":\"left\",\"upAxis\":\"+Y\",\"forwardAxis\":\"+Z\"}," +
                "\"items\":[]," +
                "\"validation\":{\"isValid\":true,\"invalidItemIds\":[],\"warnings\":[]}," +
                "\"extensions\":{\"source\":\"p0_mock\"}" +
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
    }
}

using System;
using System.IO;
using Room2Scan.Bridge;
using UnityEditor;
using UnityEngine;

namespace Room2Scan.Bridge.Editor
{
    public static class UnityBridgeEditorTest
    {
        private const string LoadRoomExamplePath = "docs/schemas/unity-bridge-v1.example.load-room.json";

        [MenuItem("Room2Scan/P0/Send Mock LoadRoom")]
        public static void SendMockLoadRoom()
        {
            var json = ReadProjectFile(LoadRoomExamplePath);
            UnityBridge.GetOrCreateInstance().ReceiveFromRN(json);
        }

        [MenuItem("Room2Scan/P0/Send Mock SaveLayout")]
        public static void SendMockSaveLayout()
        {
            var requestId = $"req_editor_save_{Guid.NewGuid():N}";
            var json =
                "{" +
                "\"schemaVersion\":\"unity-bridge/v1\"," +
                $"\"messageId\":\"rn_{Guid.NewGuid():N}\"," +
                $"\"requestId\":\"{requestId}\"," +
                "\"kind\":\"command\"," +
                "\"direction\":\"rn_to_unity\"," +
                "\"name\":\"SaveLayout\"," +
                $"\"sentAt\":\"{DateTime.UtcNow:o}\"," +
                "\"payload\":{}" +
                "}";

            UnityBridge.GetOrCreateInstance().ReceiveFromRN(json);
        }

        private static string ReadProjectFile(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Could not resolve Unity project root.");
            }

            var path = Path.Combine(projectRoot, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Mock bridge payload not found: {path}", path);
            }

            return File.ReadAllText(path);
        }
    }
}

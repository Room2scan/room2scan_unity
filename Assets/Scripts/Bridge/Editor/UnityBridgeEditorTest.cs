using System;
using System.IO;
using System.Text.RegularExpressions;
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

        [MenuItem("Room2Scan/P1/Send Local PLY LoadRoom")]
        public static void SendLocalPlyLoadRoom()
        {
            var projectRoot = ResolveProjectRoot();
            var plyPath = Path.Combine(projectRoot, "replica", "room0_mesh.ply");
            if (!File.Exists(plyPath))
            {
                throw new FileNotFoundException($"Local Replica PLY not found: {plyPath}", plyPath);
            }

            var json = ReadProjectFile(LoadRoomExamplePath);
            json = ReplaceJsonString(json, "uri", new Uri(plyPath).AbsoluteUri);
            json = ReplaceJsonString(json, "format", "ply");
            json = ReplaceJsonString(json, "sourceFormat", "ply");
            json = ReplaceJsonString(json, "requestId", $"req_load_local_ply_{Guid.NewGuid():N}");
            json = ReplaceJsonString(json, "messageId", $"rn_{Guid.NewGuid():N}");

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
            var path = Path.Combine(ResolveProjectRoot(), relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Mock bridge payload not found: {path}", path);
            }

            return File.ReadAllText(path);
        }

        private static string ResolveProjectRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new InvalidOperationException("Could not resolve Unity project root.");
            }

            return projectRoot;
        }

        private static string ReplaceJsonString(string json, string key, string value)
        {
            return Regex.Replace(
                json,
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"",
                $"\"{key}\": \"{EscapeJson(value)}\"",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

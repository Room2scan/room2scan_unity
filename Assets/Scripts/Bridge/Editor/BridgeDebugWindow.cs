using System;
using System.Collections.Generic;
using System.Globalization;
using Room2Scan.Rooms;
using UnityEditor;
using UnityEngine;

namespace Room2Scan.Bridge.Editor
{
    public sealed class BridgeDebugWindow : EditorWindow
    {
        private string catalogId = "mock_chair";
        private float rotateDelta = 45f;
        private int furnitureCounter;
        private bool ceilingHidden;
        private float ceilingCutY = 0.9f; // 전체 높이 대비 비율 (0~1)

        // 원본 메시 보존용
        private readonly Dictionary<MeshFilter, Mesh> originalMeshes = new Dictionary<MeshFilter, Mesh>();

        [MenuItem("Room2Scan/Bridge Debug Window")]
        public static void Open() => GetWindow<BridgeDebugWindow>("Bridge Debug");

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode에서만 동작합니다. ▶ 누르고 사용하세요.", MessageType.Info);
                return;
            }

            var bridge = UnityBridge.GetOrCreateInstance();

            // ── Room ──────────────────────────────────────────────────────────
            GUILayout.Label("Room", EditorStyles.boldLabel);
            if (GUILayout.Button("LoadRoom (mock)"))
            {
                ceilingHidden = false;
                originalMeshes.Clear();
                SendLoadRoom(bridge);
                // 0.5초 후 카메라 컨트롤러 부착 (비동기 로드 대기)
                EditorApplication.delayCall += AttachOrbitCameraDelayed;
            }
            if (GUILayout.Button("ResetEditor"))
            {
                ceilingHidden = false;
                originalMeshes.Clear();
                SendRaw(bridge, Command("ResetEditor", "{}"));
            }

            EditorGUILayout.Space(6);

            // ── Ceiling ───────────────────────────────────────────────────────
            GUILayout.Label("View", EditorStyles.boldLabel);
            ceilingCutY = EditorGUILayout.Slider("Ceiling cut (0=bottom, 1=top)", ceilingCutY, 0.1f, 1f);
            if (GUILayout.Button(ceilingHidden ? "Show Ceiling" : "Hide Ceiling"))
            {
                if (ceilingHidden) RestoreCeiling();
                else HideCeiling(ceilingCutY);
                ceilingHidden = !ceilingHidden;
            }

            EditorGUILayout.Space(8);

            // ── Furniture ─────────────────────────────────────────────────────
            GUILayout.Label("Furniture", EditorStyles.boldLabel);
            catalogId = EditorGUILayout.TextField("Catalog ID", catalogId);

            if (GUILayout.Button("AddFurniture"))
            {
                var instanceId = "dbg_" + (++furnitureCounter);
                var x = UnityEngine.Random.Range(-2f, 2f);
                var z = UnityEngine.Random.Range(-2f, 2f);
                var xs = x.ToString("G", CultureInfo.InvariantCulture);
                var zs = z.ToString("G", CultureInfo.InvariantCulture);
                var payload = "{\"instanceId\":\"" + instanceId + "\"," +
                              "\"catalogId\":\"" + catalogId + "\"," +
                              "\"position\":{\"x\":" + xs + ",\"y\":0,\"z\":" + zs + "}}";
                SendRaw(bridge, Command("AddFurniture", payload));
            }

            EditorGUILayout.Space(4);
            rotateDelta = EditorGUILayout.FloatField("Rotate deltaDeg", rotateDelta);

            if (GUILayout.Button("RotateSelected"))
            {
                var ds = rotateDelta.ToString("G", CultureInfo.InvariantCulture);
                SendRaw(bridge, Command("RotateSelected", "{\"deltaDeg\":" + ds + "}"));
            }

            if (GUILayout.Button("DeleteSelected"))
                SendRaw(bridge, Command("DeleteSelected", "{}"));

            EditorGUILayout.Space(8);

            // ── Layout ────────────────────────────────────────────────────────
            GUILayout.Label("Layout", EditorStyles.boldLabel);
            if (GUILayout.Button("SaveLayout"))
                SendRaw(bridge, Command("SaveLayout", "{}"));
        }

        // ── Ceiling helpers ───────────────────────────────────────────────────

        private void HideCeiling(float cutRatio)
        {
            GameObject roomRoot = null;
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go.name.StartsWith("RoomRoot_", System.StringComparison.Ordinal))
                {
                    roomRoot = go;
                    break;
                }
            }
            if (roomRoot == null)
            {
                Debug.LogWarning("[BridgeDebug] No RoomRoot found. Load a room first.");
                return;
            }

            // 전체 bounds Y 범위 계산
            var allFilters = roomRoot.GetComponentsInChildren<MeshFilter>(true);
            var globalYMax = float.MinValue;
            var globalYMin = float.MaxValue;
            foreach (var mf in allFilters)
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                var worldMax = mf.transform.TransformPoint(b.max).y;
                var worldMin = mf.transform.TransformPoint(b.min).y;
                if (worldMax > globalYMax) globalYMax = worldMax;
                if (worldMin < globalYMin) globalYMin = worldMin;
            }

            var cutWorldY = Mathf.Lerp(globalYMin, globalYMax, cutRatio);
            Debug.Log($"[BridgeDebug] HideCeiling: cutting above Y={cutWorldY:F2} (range {globalYMin:F2}~{globalYMax:F2})");

            foreach (var mf in allFilters)
            {
                if (mf.sharedMesh == null) continue;
                if (!originalMeshes.ContainsKey(mf))
                    originalMeshes[mf] = mf.sharedMesh;

                mf.mesh = CutMeshAboveY(mf.sharedMesh, mf.transform, cutWorldY);
            }
        }

        private void RestoreCeiling()
        {
            foreach (var kv in originalMeshes)
                if (kv.Key != null) kv.Key.mesh = kv.Value;
        }

        private static Mesh CutMeshAboveY(Mesh src, Transform t, float cutWorldY)
        {
            var verts = src.vertices;
            var tris  = src.triangles;

            var newTris = new List<int>(tris.Length);
            for (var i = 0; i < tris.Length; i += 3)
            {
                var a = t.TransformPoint(verts[tris[i]]).y;
                var b = t.TransformPoint(verts[tris[i + 1]]).y;
                var c = t.TransformPoint(verts[tris[i + 2]]).y;
                // 세 꼭짓점 모두 cutY 이상이면 제거
                if (a >= cutWorldY && b >= cutWorldY && c >= cutWorldY) continue;
                newTris.Add(tris[i]);
                newTris.Add(tris[i + 1]);
                newTris.Add(tris[i + 2]);
            }

            var m = new Mesh { name = src.name + "_noceiling" };
            m.indexFormat = src.indexFormat;
            m.vertices  = verts;
            m.normals   = src.normals;
            m.uv        = src.uv;
            m.colors    = src.colors;
            m.triangles = newTris.ToArray();
            m.RecalculateBounds();
            return m;
        }

        // ── Camera helpers ────────────────────────────────────────────────────

        private static void AttachOrbitCameraDelayed()
        {
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;

            // perspective + 적당한 FOV
            cam.orthographic = false;
            cam.fieldOfView  = 50f;

            // 기존 컨트롤러 제거
            var existing = cam.GetComponent<OrbitCameraController>();
            if (existing != null) DestroyImmediate(existing);

            var controller = cam.gameObject.AddComponent<OrbitCameraController>();

            var rm = RoomManager.Instance;
            if (rm != null && rm.CurrentRoomResult != null && rm.CurrentRoomResult.SuccessFlag)
            {
                var bounds  = rm.CurrentRoomResult.Bounds;
                // 방 바닥 기준 약간 위 지점을 피벗으로 (중앙 높이의 40%)
                var pivot   = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.4f, bounds.center.z);
                // FOV와 방 크기로 딱 맞는 거리 계산
                var fovRad  = cam.fieldOfView * Mathf.Deg2Rad;
                var halfSpan = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
                var dist    = Mathf.Clamp((halfSpan / Mathf.Tan(fovRad * 0.5f)) * 1.1f, 3f, 30f);
                // 45° isometric 앵글
                controller.SetPivotAndDistance(pivot, dist, initialYaw: -135f, initialPitch: 45f);
            }
            else
            {
                controller.SetPivotAndDistance(new Vector3(3f, 1.1f, 1.2f), 10f, -135f, 45f);
            }

            Debug.Log("[BridgeDebug] OrbitCameraController attached — isometric default view.");
        }

        // ── Bridge helpers ────────────────────────────────────────────────────

        private static void SendRaw(UnityBridge bridge, string json) => bridge.ReceiveFromRN(json);

        private static void SendLoadRoom(UnityBridge bridge, string roomId = "room0", string meshPath = null)
        {
            if (meshPath == null)
                meshPath = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "replica", roomId + "_mesh.ply"))
                    .Replace("\\", "/");

            var format = meshPath.EndsWith(".ply", System.StringComparison.OrdinalIgnoreCase) ? "ply" : "glb";
            var room = "{\"schemaVersion\":\"room-json/v1\"," +
                       "\"roomId\":\"" + roomId + "\"," +
                       "\"mesh\":{\"uri\":\"" + meshPath + "\",\"format\":\"" + format + "\"}," +
                       "\"coordinateSystem\":{" +
                           "\"unit\":\"meter\"," +
                           "\"handedness\":\"left\"," +
                           "\"upAxis\":\"+Y\"," +
                           "\"forwardAxis\":\"+Z\"," +
                           "\"toUnity\":{" +
                               "\"positionOffset\":{\"x\":0,\"y\":0,\"z\":0}," +
                               "\"rotationEulerDeg\":{\"x\":0,\"y\":0,\"z\":0}," +
                               "\"scaleMultiplier\":1" +
                           "}}," +
                       "\"bounds\":{" +
                           "\"min\":{\"x\":-0.8794,\"y\":0,\"z\":-1.186}," +
                           "\"max\":{\"x\":6.8852,\"y\":2.8078,\"z\":3.5123}" +
                       "}}";
            SendRaw(bridge, Command("LoadRoom", "{\"room\":" + room + "}"));
        }

        private static string Command(string name, string payload)
        {
            var msgId  = "dbg_" + Guid.NewGuid().ToString("N");
            var reqId  = "req_" + Guid.NewGuid().ToString("N");
            var sentAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            return "{" +
                   "\"schemaVersion\":\"unity-bridge/v1\"," +
                   "\"messageId\":\"" + msgId + "\"," +
                   "\"requestId\":\"" + reqId + "\"," +
                   "\"kind\":\"command\"," +
                   "\"direction\":\"rn_to_unity\"," +
                   "\"name\":\"" + name + "\"," +
                   "\"sentAt\":\"" + sentAt + "\"," +
                   "\"payload\":" + payload +
                   "}";
        }
    }
}

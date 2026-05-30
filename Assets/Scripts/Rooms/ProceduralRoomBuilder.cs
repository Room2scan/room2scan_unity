using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// P4 — ProceduralRoomBuilder
    /// Builds a box-shaped room (floor, ceiling, 4 walls) as Unity Mesh objects.
    /// Supports optional door and window cutouts.
    ///
    /// Called by UnityBridge when it receives a CreateProceduralRoom command.
    /// </summary>
    public static class ProceduralRoomBuilder
    {
        // ── Public API ────────────────────────────────────────────────────────────

        public struct RoomSpec
        {
            public string RoomId;
            public string Name;
            public float  Width;         // metres, X axis
            public float  Length;        // metres, Z axis
            public float  Height;        // metres, Y axis
            public float  WallThickness;
            public DoorSpec[]   Doors;
            public WindowSpec[] Windows;
        }

        public struct DoorSpec
        {
            /// <summary>0..1 offset along the wall</summary>
            public string Wall;
            public float  Offset;
            public float  Width;
            public float  Height;
        }

        public struct WindowSpec
        {
            public string Wall;
            public float  Offset;
            public float  Width;
            public float  Height;
            public float  SillHeight;
        }

        public struct BuildResult
        {
            public bool       Success;
            public string     ErrorMessage;
            public GameObject RoomRoot;
            public Bounds     Bounds;
        }

        /// <summary>
        /// Build a procedural room and return the root GameObject.
        /// Call this from the main thread.
        /// </summary>
        public static BuildResult Build(RoomSpec spec)
        {
            try
            {
                // Validate
                if (spec.Width  < 0.5f || spec.Width  > 50f) throw new ArgumentException("Width out of range");
                if (spec.Length < 0.5f || spec.Length > 50f) throw new ArgumentException("Length out of range");
                if (spec.Height < 1.0f || spec.Height > 20f) throw new ArgumentException("Height out of range");

                float t = Mathf.Clamp(spec.WallThickness, 0.05f, 0.5f);

                // Root object
                var root = new GameObject($"RoomRoot_{spec.RoomId}");

                // ── Floor ────────────────────────────────────────────────────────
                var floor = BuildQuad(
                    "Floor", root.transform,
                    new Vector3(0, 0, 0),
                    spec.Width, spec.Length,
                    false   // face up
                );
                SetRoomMaterial(floor, RoomSurface.Floor);

                // ── Ceiling ───────────────────────────────────────────────────────
                var ceiling = BuildQuad(
                    "Ceiling", root.transform,
                    new Vector3(0, spec.Height, 0),
                    spec.Width, spec.Length,
                    true    // face down
                );
                SetRoomMaterial(ceiling, RoomSurface.Ceiling);

                // ── Walls ─────────────────────────────────────────────────────────
                // North wall (Z = +length/2), South (Z = -length/2),
                // East  wall (X = +width/2),  West  (X = -width/2)
                BuildWall("Wall_North", root.transform, spec, "north", t);
                BuildWall("Wall_South", root.transform, spec, "south", t);
                BuildWall("Wall_East",  root.transform, spec, "east",  t);
                BuildWall("Wall_West",  root.transform, spec, "west",  t);

                // ── Bounds ────────────────────────────────────────────────────────
                var bounds = new Bounds(
                    new Vector3(0, spec.Height * 0.5f, 0),
                    new Vector3(spec.Width, spec.Height, spec.Length)
                );

                return new BuildResult { Success = true, RoomRoot = root, Bounds = bounds };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProceduralRoomBuilder] Build failed: {ex.Message}");
                return new BuildResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        private enum RoomSurface { Floor, Ceiling, Wall }

        /// <summary>Axis-aligned quad (floor or ceiling).</summary>
        private static GameObject BuildQuad(
            string name, Transform parent,
            Vector3 pos, float w, float l, bool facingDown)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            var verts = new Vector3[]
            {
                new Vector3(-w * 0.5f, 0, -l * 0.5f),
                new Vector3( w * 0.5f, 0, -l * 0.5f),
                new Vector3( w * 0.5f, 0,  l * 0.5f),
                new Vector3(-w * 0.5f, 0,  l * 0.5f),
            };

            int[] tris = facingDown
                ? new[] { 0, 2, 1,  0, 3, 2 }
                : new[] { 0, 1, 2,  0, 2, 3 };

            var uv = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1),
            };

            var mesh = new Mesh { name = name };
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.uv        = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            return go;
        }

        /// <summary>Build one wall with optional door/window cutouts.</summary>
        private static void BuildWall(
            string name, Transform parent,
            RoomSpec spec, string wallSide, float t)
        {
            // Determine wall dimensions and orientation
            float wallWidth, wallPosX, wallPosZ;
            Quaternion rot;

            switch (wallSide)
            {
                case "north":
                    wallWidth = spec.Width;
                    wallPosX  = 0;
                    wallPosZ  = spec.Length * 0.5f;
                    rot       = Quaternion.Euler(0, 0, 0);
                    break;
                case "south":
                    wallWidth = spec.Width;
                    wallPosX  = 0;
                    wallPosZ  = -spec.Length * 0.5f;
                    rot       = Quaternion.Euler(0, 180, 0);
                    break;
                case "east":
                    wallWidth = spec.Length;
                    wallPosX  = spec.Width * 0.5f;
                    wallPosZ  = 0;
                    rot       = Quaternion.Euler(0, -90, 0);
                    break;
                case "west":
                    wallWidth = spec.Length;
                    wallPosX  = -spec.Width * 0.5f;
                    wallPosZ  = 0;
                    rot       = Quaternion.Euler(0, 90, 0);
                    break;
                default:
                    return;
            }

            // Collect cutouts for this wall
            var cutouts = new List<(float center, float w, float yBot, float yTop)>();
            if (spec.Doors != null)
            {
                foreach (var d in spec.Doors)
                {
                    if (d.Wall != wallSide) continue;
                    var center = Mathf.Lerp(-wallWidth * 0.5f + d.Width * 0.5f,
                                            wallWidth * 0.5f  - d.Width * 0.5f, d.Offset);
                    cutouts.Add((center, d.Width, 0, d.Height));
                }
            }
            if (spec.Windows != null)
            {
                foreach (var win in spec.Windows)
                {
                    if (win.Wall != wallSide) continue;
                    var center = Mathf.Lerp(-wallWidth * 0.5f + win.Width * 0.5f,
                                            wallWidth * 0.5f  - win.Width * 0.5f, win.Offset);
                    cutouts.Add((center, win.Width, win.SillHeight, win.SillHeight + win.Height));
                }
            }

            var mesh = cutouts.Count == 0
                ? BuildSolidWallMesh(wallWidth, spec.Height)
                : BuildWallMeshWithCutouts(wallWidth, spec.Height, cutouts);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(wallPosX, 0, wallPosZ);
            go.transform.localRotation = rot;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            SetRoomMaterial(go, RoomSurface.Wall);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        /// <summary>Simple solid wall quad.</summary>
        private static Mesh BuildSolidWallMesh(float w, float h)
        {
            var verts = new[]
            {
                new Vector3(-w*0.5f, 0, 0), new Vector3(w*0.5f, 0, 0),
                new Vector3( w*0.5f, h, 0), new Vector3(-w*0.5f, h, 0),
            };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            var uv   = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
            var mesh = new Mesh { name = "WallMesh" };
            mesh.vertices = verts; mesh.triangles = tris; mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Wall with rectangular cutouts (doors/windows).
        /// Uses a simple strip-based polygon approach.
        /// </summary>
        private static Mesh BuildWallMeshWithCutouts(
            float w, float h,
            List<(float cx, float cw, float yBot, float yTop)> cutouts)
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            var uvs   = new List<Vector2>();

            // For each column region between/around cutouts, build quads
            // Collect X breakpoints
            var xs = new SortedSet<float> { -w * 0.5f, w * 0.5f };
            foreach (var (cx, cw, _, _) in cutouts)
            {
                xs.Add(cx - cw * 0.5f);
                xs.Add(cx + cw * 0.5f);
            }
            var xList = new List<float>(xs);

            // For each vertical strip between x breakpoints
            for (var xi = 0; xi < xList.Count - 1; xi++)
            {
                float x0 = xList[xi], x1 = xList[xi + 1];
                float midX = (x0 + x1) * 0.5f;

                // Find cutouts in this strip
                var stripCutouts = new List<(float yBot, float yTop)>();
                foreach (var (cx, cw, yBot, yTop) in cutouts)
                {
                    if (midX > cx - cw * 0.5f && midX < cx + cw * 0.5f)
                        stripCutouts.Add((yBot, yTop));
                }

                // Y breakpoints for this strip
                var ys = new SortedSet<float> { 0, h };
                foreach (var (yBot, yTop) in stripCutouts)
                {
                    ys.Add(Mathf.Clamp(yBot, 0, h));
                    ys.Add(Mathf.Clamp(yTop, 0, h));
                }
                var yList = new List<float>(ys);

                for (var yi = 0; yi < yList.Count - 1; yi++)
                {
                    float y0 = yList[yi], y1 = yList[yi + 1];
                    float midY = (y0 + y1) * 0.5f;

                    // Is this sub-quad inside a cutout?
                    bool isCutout = false;
                    foreach (var (yBot, yTop) in stripCutouts)
                    {
                        if (midY > yBot && midY < yTop) { isCutout = true; break; }
                    }
                    if (isCutout) continue;

                    // Add quad
                    int baseIdx = verts.Count;
                    verts.Add(new Vector3(x0, y0, 0));
                    verts.Add(new Vector3(x1, y0, 0));
                    verts.Add(new Vector3(x1, y1, 0));
                    verts.Add(new Vector3(x0, y1, 0));

                    float u0 = (x0 + w * 0.5f) / w, u1 = (x1 + w * 0.5f) / w;
                    float v0 = y0 / h, v1 = y1 / h;
                    uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u1, v0));
                    uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u0, v1));

                    tris.AddRange(new[] { baseIdx, baseIdx+1, baseIdx+2, baseIdx, baseIdx+2, baseIdx+3 });
                }
            }

            var mesh = new Mesh { name = "WallMeshCutout" };
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Apply a simple runtime material based on surface type.</summary>
        private static void SetRoomMaterial(GameObject go, RoomSurface surface)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;

            Color c;
            switch (surface)
            {
                case RoomSurface.Floor:   c = new Color(0.85f, 0.82f, 0.74f); break; // warm off-white
                case RoomSurface.Ceiling: c = new Color(0.96f, 0.96f, 0.96f); break; // near white
                default:                  c = new Color(0.92f, 0.90f, 0.88f); break; // light beige
            }

            // Use URP Lit if available, otherwise fall back to Standard
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = c;
                mr.sharedMaterial = mat;
            }
        }

        // ── JSON parsing helpers (no JsonUtility needed) ──────────────────────────

        public static RoomSpec ParseFromJson(string envelopeJson)
        {
            float GetFloat(string key, float def)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    envelopeJson, $"\"{key}\"\\s*:\\s*([\\d.\\-]+)");
                return m.Success
                    ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                    : def;
            }
            string GetStr(string key, string def)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    envelopeJson, $"\"{key}\"\\s*:\\s*\"(?<v>[^\"]*)\"");
                return m.Success ? m.Groups["v"].Value : def;
            }

            // Parse doors
            var doors    = new List<DoorSpec>();
            var windows  = new List<WindowSpec>();

            // Basic extraction — only first door/window for now
            // A full JSON parser would be preferable for many openings
            var doorMatch = System.Text.RegularExpressions.Regex.Match(
                envelopeJson, @"""doors""\s*:\s*\[([^\]]*)\]");
            if (doorMatch.Success)
            {
                var doorBlock = doorMatch.Groups[1].Value;
                ParseOpenings(doorBlock, out var dWalls, out var dOffsets, out var dWidths, out var dHeights);
                for (var i = 0; i < dWalls.Count; i++)
                {
                    doors.Add(new DoorSpec
                    {
                        Wall   = dWalls[i],
                        Offset = dOffsets[i],
                        Width  = dWidths[i],
                        Height = dHeights[i],
                    });
                }
            }

            var winMatch = System.Text.RegularExpressions.Regex.Match(
                envelopeJson, @"""windows""\s*:\s*\[([^\]]*)\]");
            if (winMatch.Success)
            {
                var winBlock = winMatch.Groups[1].Value;
                ParseOpenings(winBlock, out var wWalls, out var wOffsets, out var wWidths, out var wHeights);
                for (var i = 0; i < wWalls.Count; i++)
                {
                    // sillHeight
                    var sm = System.Text.RegularExpressions.Regex.Match(
                        winBlock, @"""sillHeight""\s*:\s*([\d.\-]+)");
                    var sill = sm.Success
                        ? float.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture)
                        : 0.9f;
                    windows.Add(new WindowSpec
                    {
                        Wall       = wWalls[i],
                        Offset     = wOffsets[i],
                        Width      = wWidths[i],
                        Height     = wHeights[i],
                        SillHeight = sill,
                    });
                }
            }

            return new RoomSpec
            {
                RoomId        = GetStr("roomId",  $"proc_{Guid.NewGuid():N}"),
                Name          = GetStr("name",    "새 방"),
                Width         = GetFloat("width",  4f),
                Length        = GetFloat("length", 4f),
                Height        = GetFloat("height", 2.7f),
                WallThickness = GetFloat("wallThickness", 0.15f),
                Doors         = doors.ToArray(),
                Windows       = windows.ToArray(),
            };
        }

        private static void ParseOpenings(
            string block,
            out List<string> walls,
            out List<float>  offsets,
            out List<float>  widths,
            out List<float>  heights)
        {
            walls   = new List<string>();
            offsets = new List<float>();
            widths  = new List<float>();
            heights = new List<float>();

            var wallMatches   = System.Text.RegularExpressions.Regex.Matches(block, @"""wall""\s*:\s*""([^""]*)""");
            var offsetMatches = System.Text.RegularExpressions.Regex.Matches(block, @"""offset""\s*:\s*([\d.\-]+)");
            var widthMatches  = System.Text.RegularExpressions.Regex.Matches(block, @"""width""\s*:\s*([\d.\-]+)");
            var heightMatches = System.Text.RegularExpressions.Regex.Matches(block, @"""height""\s*:\s*([\d.\-]+)");

            var count = wallMatches.Count;
            for (var i = 0; i < count; i++)
            {
                walls.Add(i < wallMatches.Count   ? wallMatches[i].Groups[1].Value   : "south");
                offsets.Add(i < offsetMatches.Count ? float.Parse(offsetMatches[i].Groups[1].Value, CultureInfo.InvariantCulture) : 0.5f);
                widths.Add(i < widthMatches.Count  ? float.Parse(widthMatches[i].Groups[1].Value,  CultureInfo.InvariantCulture) : 0.9f);
                heights.Add(i < heightMatches.Count ? float.Parse(heightMatches[i].Groups[1].Value, CultureInfo.InvariantCulture) : 2.1f);
            }
        }
    }
}

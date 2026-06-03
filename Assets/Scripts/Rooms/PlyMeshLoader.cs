using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Room2Scan.Rooms
{
    public static class PlyMeshLoader
    {
        private const string BinaryLittleEndian = "binary_little_endian";

        public static bool TryLoad(string meshUri, string roomId, out GameObject root, out Bounds bounds, out string error)
        {
            root = null;
            bounds = default;
            error = null;

            if (!TryResolveLocalPath(meshUri, out var localPath, out error))
            {
                return false;
            }

            try
            {
                using var stream = File.OpenRead(localPath);
                using var reader = new BinaryReader(stream);

                var header = ReadHeader(reader);
                if (header.format != BinaryLittleEndian)
                {
                    error = $"PLY test loader only supports {BinaryLittleEndian}. Received: {header.format}";
                    return false;
                }

                if (header.vertexCount <= 0 || header.faceCount <= 0)
                {
                    error = $"PLY file has invalid counts. vertices={header.vertexCount}, faces={header.faceCount}";
                    return false;
                }

                var vertices = new List<Vector3>(header.vertexCount);
                var normals = header.hasNormals ? new List<Vector3>(header.vertexCount) : null;
                var colors = header.hasColors ? new List<Color32>(header.vertexCount) : null;
                var minY = float.PositiveInfinity;

                for (var i = 0; i < header.vertexCount; i++)
                {
                    var vertex = ReadVertex(reader, header.vertexProperties);
                    var position = RemapReplicaPosition(vertex.position);
                    vertices.Add(position);
                    minY = Mathf.Min(minY, position.y);

                    if (normals != null)
                    {
                        normals.Add(RemapReplicaNormal(vertex.normal));
                    }

                    if (colors != null)
                    {
                        colors.Add(vertex.color);
                    }
                }

                if (!float.IsInfinity(minY) && !Mathf.Approximately(minY, 0f))
                {
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var vertex = vertices[i];
                        vertex.y -= minY;
                        vertices[i] = vertex;
                    }
                }

                var triangles = new List<int>(header.faceCount * 3);
                for (var i = 0; i < header.faceCount; i++)
                {
                    ReadFace(reader, triangles);
                }

                var mesh = new Mesh
                {
                    name = $"{roomId}_ply_mesh",
                    indexFormat = IndexFormat.UInt32
                };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, true);

                if (normals != null && normals.Count > 0)
                {
                    mesh.SetNormals(normals);
                }
                else
                {
                    mesh.RecalculateNormals();
                }

                if (colors != null && colors.Count > 0)
                {
                    mesh.SetColors(colors);
                }

                mesh.RecalculateBounds();
                bounds = mesh.bounds;

                root = new GameObject($"RoomRoot_{roomId}_PLY");
                var meshObject = new GameObject("ReplicaPlyMesh");
                meshObject.transform.SetParent(root.transform, false);

                var meshFilter = meshObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;

                var meshRenderer = meshObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = CreateDefaultMaterial();

                Debug.Log($"Room2Scan PLY loader: loaded {Path.GetFileName(localPath)} with {vertices.Count} vertices and {triangles.Count / 3} triangles.");
                return true;
            }
            catch (Exception exception)
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                error = exception.Message;
                root = null;
                bounds = default;
                return false;
            }
        }

        private static bool TryResolveLocalPath(string meshUri, out string localPath, out string error)
        {
            localPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(meshUri))
            {
                error = "PLY mesh URI is empty.";
                return false;
            }

            if (Uri.TryCreate(meshUri, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile)
                {
                    error = $"PLY test loader only supports local files. Received URI: {meshUri}";
                    return false;
                }

                localPath = uri.LocalPath;
            }
            else if (Path.IsPathRooted(meshUri))
            {
                localPath = meshUri;
            }
            else
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var projectRelativePath = Path.GetFullPath(Path.Combine(projectRoot, meshUri));
                localPath = File.Exists(projectRelativePath)
                    ? projectRelativePath
                    : Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, meshUri));
            }

            if (File.Exists(localPath))
            {
                return true;
            }

            error = $"PLY file not found: {localPath}";
            return false;
        }

        private static PlyHeader ReadHeader(BinaryReader reader)
        {
            var header = new PlyHeader();
            var inVertexElement = false;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var line = ReadAsciiLine(reader);
                if (line == "end_header")
                {
                    return header;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                if (parts[0] == "format" && parts.Length >= 2)
                {
                    header.format = parts[1];
                    continue;
                }

                if (parts[0] == "element" && parts.Length >= 3)
                {
                    inVertexElement = parts[1] == "vertex";
                    if (parts[1] == "vertex")
                    {
                        header.vertexCount = ParseInt(parts[2]);
                    }
                    else if (parts[1] == "face")
                    {
                        header.faceCount = ParseInt(parts[2]);
                    }

                    continue;
                }

                if (inVertexElement && parts[0] == "property" && parts.Length >= 3)
                {
                    var property = new PlyVertexProperty(parts[1], parts[2]);
                    header.vertexProperties.Add(property);

                    if (parts[2] is "nx" or "ny" or "nz")
                    {
                        header.hasNormals = true;
                    }

                    if (parts[2] is "red" or "green" or "blue" or "alpha")
                    {
                        header.hasColors = true;
                    }
                }
            }

            throw new InvalidDataException("PLY header is missing end_header.");
        }

        private static PlyVertex ReadVertex(BinaryReader reader, List<PlyVertexProperty> properties)
        {
            var vertex = new PlyVertex { color = new Color32(180, 180, 180, 255) };
            foreach (var property in properties)
            {
                switch (property.name)
                {
                    case "x":
                        vertex.position.x = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "y":
                        vertex.position.y = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "z":
                        vertex.position.z = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "nx":
                        vertex.normal.x = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "ny":
                        vertex.normal.y = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "nz":
                        vertex.normal.z = ReadPropertyAsFloat(reader, property.type);
                        break;
                    case "red":
                        vertex.color.r = ReadPropertyAsByte(reader, property.type);
                        break;
                    case "green":
                        vertex.color.g = ReadPropertyAsByte(reader, property.type);
                        break;
                    case "blue":
                        vertex.color.b = ReadPropertyAsByte(reader, property.type);
                        break;
                    case "alpha":
                        vertex.color.a = ReadPropertyAsByte(reader, property.type);
                        break;
                    default:
                        SkipProperty(reader, property.type);
                        break;
                }
            }

            return vertex;
        }

        private static void ReadFace(BinaryReader reader, List<int> triangles)
        {
            var count = reader.ReadByte();
            if (count < 3)
            {
                for (var i = 0; i < count; i++)
                {
                    reader.ReadInt32();
                }

                return;
            }

            var first = reader.ReadInt32();
            var previous = reader.ReadInt32();
            for (var i = 2; i < count; i++)
            {
                var current = reader.ReadInt32();
                triangles.Add(first);
                triangles.Add(previous);
                triangles.Add(current);
                previous = current;
            }
        }

        private static Vector3 RemapReplicaPosition(Vector3 source)
        {
            return new Vector3(source.x, source.z, source.y);
        }

        private static Vector3 RemapReplicaNormal(Vector3 source)
        {
            return new Vector3(source.x, source.z, source.y).normalized;
        }

        private static string ReadAsciiLine(BinaryReader reader)
        {
            var chars = new List<byte>(64);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var value = reader.ReadByte();
                if (value == '\n')
                {
                    break;
                }

                if (value != '\r')
                {
                    chars.Add(value);
                }
            }

            return System.Text.Encoding.ASCII.GetString(chars.ToArray());
        }

        private static int ParseInt(string value)
        {
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static float ReadPropertyAsFloat(BinaryReader reader, string type)
        {
            return type switch
            {
                "float" or "float32" => reader.ReadSingle(),
                "double" or "float64" => (float)reader.ReadDouble(),
                "uchar" or "uint8" => reader.ReadByte(),
                "char" or "int8" => reader.ReadSByte(),
                "ushort" or "uint16" => reader.ReadUInt16(),
                "short" or "int16" => reader.ReadInt16(),
                "uint" or "uint32" => reader.ReadUInt32(),
                "int" or "int32" => reader.ReadInt32(),
                _ => throw new NotSupportedException($"Unsupported PLY property type: {type}")
            };
        }

        private static byte ReadPropertyAsByte(BinaryReader reader, string type)
        {
            return type switch
            {
                "uchar" or "uint8" => reader.ReadByte(),
                "char" or "int8" => unchecked((byte)reader.ReadSByte()),
                "ushort" or "uint16" => (byte)Mathf.Clamp(reader.ReadUInt16(), 0, 255),
                "short" or "int16" => (byte)Mathf.Clamp(reader.ReadInt16(), 0, 255),
                "uint" or "uint32" => ClampByte(reader.ReadUInt32()),
                "int" or "int32" => (byte)Mathf.Clamp(reader.ReadInt32(), 0, 255),
                "float" or "float32" => (byte)Mathf.Clamp(Mathf.RoundToInt(reader.ReadSingle()), 0, 255),
                "double" or "float64" => (byte)Mathf.Clamp((int)Math.Round(reader.ReadDouble()), 0, 255),
                _ => throw new NotSupportedException($"Unsupported PLY color property type: {type}")
            };
        }

        private static byte ClampByte(uint value)
        {
            return value > 255 ? (byte)255 : (byte)value;
        }

        private static void SkipProperty(BinaryReader reader, string type)
        {
            var bytes = type switch
            {
                "char" or "uchar" or "int8" or "uint8" => 1,
                "short" or "ushort" or "int16" or "uint16" => 2,
                "int" or "uint" or "float" or "int32" or "uint32" or "float32" => 4,
                "double" or "float64" => 8,
                _ => throw new NotSupportedException($"Unsupported PLY property type: {type}")
            };

            reader.BaseStream.Seek(bytes, SeekOrigin.Current);
        }

        private static Material CreateDefaultMaterial()
        {
            // Room2Scan/VertexColor 셰이더로 PLY vertex color를 그대로 표시
            var shader = Shader.Find("Room2Scan/VertexColor");
            if (shader == null)
            {
                // 폴백: URP/Lit (vertex color 없이 단색)
                Debug.LogWarning("Room2Scan PLY: Room2Scan/VertexColor shader not found, falling back to solid color.");
                return RuntimeMaterialFactory.CreateSolidColorMaterial("Room2Scan_PLY_Material", new Color(0.82f, 0.84f, 0.86f, 1f));
            }

            return new Material(shader) { name = "Room2Scan_PLY_Material" };
        }

        private sealed class PlyHeader
        {
            public string format;
            public int vertexCount;
            public int faceCount;
            public bool hasNormals;
            public bool hasColors;
            public readonly List<PlyVertexProperty> vertexProperties = new();
        }

        private readonly struct PlyVertexProperty
        {
            public PlyVertexProperty(string type, string name)
            {
                this.type = type;
                this.name = name;
            }

            public readonly string type;
            public readonly string name;
        }

        private struct PlyVertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Color32 color;
        }
    }
}

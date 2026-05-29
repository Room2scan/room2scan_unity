using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Export;
using Room2Scan.Rooms;
using UnityEditor;
using UnityEngine;

namespace Room2Scan.Rooms.Editor
{
    public static class RoomMeshConversionEditor
    {
        private const string SourcePlyRelativePath = "replica/room0_mesh.ply";
        private const string OutputGlbAssetPath = "Assets/StreamingAssets/replica/room0_mesh.glb";

        [MenuItem("Room2Scan/P1/Convert room0 PLY to StreamingAssets GLB")]
        public static void ConvertRoom0PlyToStreamingAssetsGlb()
        {
            ConvertRoom0PlyToStreamingAssetsGlbAsync().GetAwaiter().GetResult();
        }

        public static void ConvertRoom0PlyToStreamingAssetsGlbFromCommandLine()
        {
            ConvertRoom0PlyToStreamingAssetsGlb();
        }

        public static void EnsureRoom0StreamingAssetsGlb()
        {
            if (File.Exists(GetAbsoluteOutputPath()))
            {
                return;
            }

            ConvertRoom0PlyToStreamingAssetsGlb();
        }

        private static async Task ConvertRoom0PlyToStreamingAssetsGlbAsync()
        {
            var sourcePath = GetAbsoluteSourcePath();
            var outputPath = GetAbsoluteOutputPath();

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Replica PLY source not found: {sourcePath}", sourcePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Could not resolve GLB output directory."));

            if (!PlyMeshLoader.TryLoad(sourcePath, "replica_room0", out var roomRoot, out _, out var error))
            {
                throw new InvalidOperationException($"Could not load source PLY for GLB conversion: {error}");
            }

            try
            {
                roomRoot.name = "replica_room0";
                var exportSettings = new ExportSettings
                {
                    Format = GltfFormat.Binary,
                    FileConflictResolution = FileConflictResolution.Overwrite,
                    ComponentMask = ComponentType.Mesh,
                    PreservedVertexAttributes = VertexAttributeUsage.Normal | VertexAttributeUsage.Color,
                    Deterministic = true
                };

                var gameObjectExportSettings = new GameObjectExportSettings
                {
                    OnlyActiveInHierarchy = false,
                    DisabledComponents = true
                };

                var exporter = new GameObjectExport(exportSettings, gameObjectExportSettings);
                exporter.AddScene(new[] { roomRoot }, roomRoot.transform.worldToLocalMatrix, "Replica room0");

                var success = await exporter.SaveToFileAndDispose(outputPath);
                if (!success)
                {
                    throw new InvalidOperationException($"glTFast failed to export GLB: {outputPath}");
                }

                AssetDatabase.ImportAsset(OutputGlbAssetPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"Room2Scan GLB conversion complete: {outputPath}");
            }
            finally
            {
                DestroyGeneratedMeshesAndMaterials(roomRoot);
                UnityEngine.Object.DestroyImmediate(roomRoot);
            }
        }

        private static string GetAbsoluteSourcePath()
        {
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), SourcePlyRelativePath));
        }

        private static string GetAbsoluteOutputPath()
        {
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), OutputGlbAssetPath));
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName
                   ?? throw new InvalidOperationException("Could not resolve Unity project root.");
        }

        private static void DestroyGeneratedMeshesAndMaterials(GameObject root)
        {
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(meshFilter.sharedMesh);
                    meshFilter.sharedMesh = null;
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        UnityEngine.Object.DestroyImmediate(material);
                    }
                }
            }
        }
    }
}

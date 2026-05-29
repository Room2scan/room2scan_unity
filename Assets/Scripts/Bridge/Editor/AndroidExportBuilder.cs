using System.IO;
using System.Linq;
using Room2Scan.Rooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Room2Scan.Bridge.Editor
{
    public static class AndroidExportBuilder
    {
        private const string DefaultOutputPath = "C:/Users/park/room2scan_app/unity/builds/android";
        private const string FallbackScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Room2Scan/Build/Export Android Unity Library")]
        public static void ExportAndroidUnityLibrary()
        {
            Export(DefaultOutputPath);
        }

        public static void ExportAndroidUnityLibraryFromCommandLine()
        {
            Export(GetArgumentValue("-room2scanOutput") ?? DefaultOutputPath);
        }

        private static void Export(string outputPath)
        {
            EnsureBuildScenes();

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }

            Directory.CreateDirectory(outputPath);
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.scan2room.unity");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.AcceptExternalModificationsToPlayer | BuildOptions.Development
            });

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new System.Exception($"Unity Android export failed: {report.summary.result}");
            }
        }

        private static void EnsureBuildScenes()
        {
            var scenes = GetEnabledScenePaths();
            if (scenes.Length > 0)
            {
                return;
            }

            if (!File.Exists(FallbackScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, FallbackScenePath);
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(FallbackScenePath, true) };
        }

        private static string[] GetEnabledScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static string GetArgumentValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}

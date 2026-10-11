using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Player builds: a Windows build (which also runs as a local server with -server), and the Linux
    /// dedicated server that Edgegap hosts. The Edgegap plugin (Tools > Edgegap Server Hosting) can
    /// also build, containerise and upload the server for you.
    /// </summary>
    public static class BuildMenu
    {
        public const string WindowsPath = "Builds/Windows/SpongykillOps.exe";
        public const string ServerPath = "Builds/EdgegapServer/ServerBuild";

        static string[] Scenes => EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();

        [MenuItem("Spongykill/Build/Windows (Client + Local Server)", priority = 100)]
        public static void BuildWindowsMenu() => BuildWindows(development: false);

        public static BuildReport BuildWindows(bool development)
        {
            Branding.Apply();
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = WindowsPath,
                target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            return Report(BuildPipeline.BuildPlayer(options), WindowsPath);
        }

        [MenuItem("Spongykill/Build/Linux Dedicated Server (Edgegap)", priority = 101)]
        public static void BuildLinuxServerMenu() => BuildLinuxServer();

        public static BuildReport BuildLinuxServer()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                EditorUtility.DisplayDialog("Linux server support missing",
                    "Install \"Linux Build Support (IL2CPP and Mono)\" and \"Linux Dedicated Server Build Support\" for this Unity version in Unity Hub (Installs > ... > Add modules), then restart the Editor.", "OK");
                return null;
            }
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = ServerPath,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            };
            return Report(BuildPipeline.BuildPlayer(options), ServerPath);
        }

        [MenuItem("Spongykill/Build/Run Local Server From Windows Build", priority = 120)]
        public static void RunLocalServer()
        {
            if (!File.Exists(WindowsPath))
            {
                EditorUtility.DisplayDialog("No Windows build", "Build Spongykill > Build > Windows first.", "OK");
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(WindowsPath),
                Arguments = "-batchmode -nographics -server -logFile \"" + Path.GetFullPath("Builds/local-server.log") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            Debug.Log("[Spongykill] Local server started on UDP 7777 (log: Builds/local-server.log). Join 127.0.0.1 from the main menu.");
        }

        static BuildReport Report(BuildReport report, string path)
        {
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[Spongykill] Build succeeded: {path} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s).");
            else
                Debug.LogError($"[Spongykill] Build {summary.result}: {path} with {summary.totalErrors} errors.");
            return report;
        }
    }
}

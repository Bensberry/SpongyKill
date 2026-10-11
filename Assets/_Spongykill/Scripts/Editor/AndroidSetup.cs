using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spongykill.EditorTools
{
    /// <summary>
    /// Android player settings (landscape, 64-bit ARM with IL2CPP, internet access for online play)
    /// and an APK build. Android clients join the same servers as PC, so both platforms play together
    /// as long as they're built from the same version.
    /// </summary>
    public static class AndroidSetup
    {
        public const string ApkPath = "Builds/Android/SpongykillOps.apk";
        const string PackageName = "com.spongykill.ops";

        [MenuItem("Spongykill/Build/Configure Android Settings", priority = 110)]
        public static void Configure()
        {
            PlayerSettings.productName = "Spongykill Ops";
            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            // Replace Unity's placeholder ids (new projects and templates); keep any id someone chose.
            if (string.IsNullOrEmpty(identifier) || identifier.Contains("DefaultCompany") || identifier.Contains("UnityTechnologies") || identifier.Contains("template"))
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);

            // Shooters are played sideways; either way up is fine.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            if ((int)PlayerSettings.Android.minSdkVersion < 26) PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorUserBuildSettings.buildAppBundle = false;
            AssetDatabase.SaveAssets();
            Debug.Log($"[Spongykill] Android settings configured ({PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}, ARM64 IL2CPP, landscape).");
        }

        [MenuItem("Spongykill/Build/Android APK", priority = 111)]
        public static void BuildApkMenu() => BuildApk(switchBack: true);

        /// <summary>Switches to Android (a one-off reimport), builds the APK, and optionally switches back to Windows.</summary>
        public static BuildReport BuildApk(bool switchBack)
        {
            Branding.Apply();
            Configure();
            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            if (previous != BuildTarget.Android) EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[Spongykill] Android build succeeded: {ApkPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s).");
            else
                Debug.LogError($"[Spongykill] Android build {summary.result} with {summary.totalErrors} errors.");

            if (switchBack && previous != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
            return report;
        }
    }
}

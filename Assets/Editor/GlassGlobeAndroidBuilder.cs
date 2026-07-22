using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class GlassGlobeAndroidBuilder
{
    private const string ScenePath = "Assets/GlassGlobe/Scenes/GlassGlobePreview.unity";
    private const string OutputPath = "Builds/Android/GlassGlobePreview.apk";
    private const string PackageName = "com.glassglobe.preview";

    [MenuItem("GlassGlobe/Build Android Preview APK")]
    public static void BuildPreviewApk()
    {
        Debug.Log("GlassGlobeAndroidBuilder: Android preview build starting.");

        GlassGlobeArCoreProjectSetup.EnsureConfigured();
        GlassGlobeProjectBuilder.BuildPreviewScene();
        if (!GlassGlobeBuildValidator.ValidateLoadedPreviewScene())
        {
            throw new BuildFailedException(
                "GlassGlobe Android build stopped because preview validation failed.");
        }

        string outputDirectory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        PlayerSettings.productName = "GlassGlobe Preview";
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android,
            PackageName);
        PlayerSettings.bundleVersion = "0.2.1";
        PlayerSettings.Android.bundleVersionCode = 12;
        PlayerSettings.Android.minSdkVersion =
            AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.SetScriptingBackend(
            NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // Weather needs network access. ARCore contributes camera permission and
        // its required manifest entries through the enabled XR loader.
        PlayerSettings.Android.forceInternetPermission = true;
        EditorUserBuildSettings.buildAppBundle = false;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                "GlassGlobe Android build failed: " + summary.result +
                " errors=" + summary.totalErrors +
                " warnings=" + summary.totalWarnings);
        }

        Debug.Log(
            "GlassGlobeAndroidBuilder: Android preview build succeeded. apk=" +
            OutputPath + " size=" + summary.totalSize + " bytes");
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class GlassGlobeAndroidBuilder
{
    private const string ScenePath = "Assets/GlassGlobe/Scenes/GlassGlobePreview.unity";
    private const string OutputPath = "Builds/Android/GlassGlobePreview.apk";
    private const string PreviewPackageName = "com.glassglobe.preview";
    private const string PlayPackageName = "com.GlassGlobe.myapp";
    private const string VersionName = "0.2.6";
    private const int VersionCode = 18;
    private const string PlayBundleOutputPath = "Builds/Android/GlassGlobe-0.2.6-18.aab";

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
            PreviewPackageName);
        PlayerSettings.bundleVersion = VersionName;
        PlayerSettings.Android.bundleVersionCode = VersionCode;
        PlayerSettings.Android.minSdkVersion =
            AndroidSdkVersions.AndroidApiLevel25;
        ConfigurePortraitOrientation();
        PlayerSettings.SetScriptingBackend(
            NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        // Weather needs network access. ARCore contributes camera permission and
        // its required manifest entries through the enabled XR loader.
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.Android.keystoreName = string.Empty;
        PlayerSettings.Android.keyaliasName = string.Empty;
        PlayerSettings.Android.keystorePass = string.Empty;
        PlayerSettings.Android.keyaliasPass = string.Empty;
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

    [MenuItem("GlassGlobe/Build Google Play App Bundle")]
    public static void BuildPlayBundle()
    {
        string keystorePath = Environment.GetEnvironmentVariable("GLASSGLOBE_KEYSTORE_PATH");
        string keystorePassword = Environment.GetEnvironmentVariable("GLASSGLOBE_KEYSTORE_PASSWORD");
        string keyAlias = Environment.GetEnvironmentVariable("GLASSGLOBE_KEY_ALIAS");
        string keyAliasPassword = Environment.GetEnvironmentVariable("GLASSGLOBE_KEY_ALIAS_PASSWORD");
        if (string.IsNullOrWhiteSpace(keystorePath) ||
            !File.Exists(keystorePath) ||
            string.IsNullOrWhiteSpace(keystorePassword) ||
            string.IsNullOrWhiteSpace(keyAlias) ||
            string.IsNullOrWhiteSpace(keyAliasPassword))
        {
            throw new BuildFailedException(
                "GlassGlobe Play build needs the GLASSGLOBE_KEYSTORE_PATH, " +
                "GLASSGLOBE_KEYSTORE_PASSWORD, GLASSGLOBE_KEY_ALIAS, and " +
                "GLASSGLOBE_KEY_ALIAS_PASSWORD environment variables.");
        }

        Debug.Log("GlassGlobeAndroidBuilder: Google Play app bundle build starting.");

        GlassGlobeArCoreProjectSetup.EnsureConfigured();
        GlassGlobeProjectBuilder.BuildPreviewScene();
        if (!GlassGlobeBuildValidator.ValidateLoadedPreviewScene())
        {
            throw new BuildFailedException(
                "GlassGlobe Play build stopped because preview validation failed.");
        }

        string outputDirectory = Path.GetDirectoryName(PlayBundleOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        try
        {
            PlayerSettings.productName = "Glass Globe";
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                PlayPackageName);
            PlayerSettings.bundleVersion = VersionName;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            PlayerSettings.Android.minSdkVersion =
                AndroidSdkVersions.AndroidApiLevel25;
            ConfigurePortraitOrientation();
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Path.GetFullPath(keystorePath);
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyAliasPassword;
            EditorUserBuildSettings.buildAppBundle = true;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayBundleOutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "GlassGlobe Play build failed: " + summary.result +
                    " errors=" + summary.totalErrors +
                    " warnings=" + summary.totalWarnings);
            }

            Debug.Log(
                "GlassGlobeAndroidBuilder: Google Play app bundle build succeeded. aab=" +
                PlayBundleOutputPath + " size=" + summary.totalSize + " bytes");
        }
        finally
        {
            PlayerSettings.productName = "GlassGlobe Preview";
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                PreviewPackageName);
            PlayerSettings.bundleVersion = VersionName;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            ConfigurePortraitOrientation();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = string.Empty;
            PlayerSettings.Android.keyaliasName = string.Empty;
            PlayerSettings.Android.keystorePass = string.Empty;
            PlayerSettings.Android.keyaliasPass = string.Empty;
        }
    }

    private static void ConfigurePortraitOrientation()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
    }
}

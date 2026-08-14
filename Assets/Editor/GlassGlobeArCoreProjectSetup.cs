using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

/// <summary>
    /// Keeps the generated project configured for the optional AR camera after
    /// package resolution and before Android builds. Runtime code starts ARCore
    /// only while the camera view is requested. This is idempotent and safe to
    /// run repeatedly.
/// </summary>
[InitializeOnLoad]
public static class GlassGlobeArCoreProjectSetup
{
    private const string ArCoreLoaderType =
        "UnityEngine.XR.ARCore.ARCoreLoader";

    static GlassGlobeArCoreProjectSetup()
    {
        EditorApplication.delayCall += EnsureConfigured;
    }

    [MenuItem("GlassGlobe/Configure Optional ARCore Camera")]
    public static void EnsureConfigured()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureConfigured;
            return;
        }

        XRGeneralSettingsPerBuildTarget perBuildTarget =
            GetOrCreateGeneralSettings();
        if (!perBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
        {
            perBuildTarget.CreateDefaultSettingsForBuildTarget(
                BuildTargetGroup.Android);
        }

        if (!perBuildTarget.HasManagerSettingsForBuildTarget(
                BuildTargetGroup.Android))
        {
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(
                BuildTargetGroup.Android);
        }

        XRGeneralSettings generalSettings =
            perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        XRManagerSettings managerSettings =
            perBuildTarget.ManagerSettingsForBuildTarget(
                BuildTargetGroup.Android);
        if (generalSettings == null || managerSettings == null)
        {
            Debug.LogError(
                "GlassGlobe ARCore setup: XR Management could not create Android settings.");
            return;
        }

        if (generalSettings.Manager != managerSettings)
        {
            generalSettings.Manager = managerSettings;
        }

        managerSettings.automaticLoading = true;
        managerSettings.automaticRunning = true;

        bool loaderAssigned = false;
        for (int index = 0; index < managerSettings.activeLoaders.Count; index++)
        {
            XRLoader loader = managerSettings.activeLoaders[index];
            if (loader != null && loader.GetType().FullName == ArCoreLoaderType)
            {
                loaderAssigned = true;
                break;
            }
        }

        if (!loaderAssigned)
        {
            loaderAssigned = XRPackageMetadataStore.AssignLoader(
                managerSettings,
                ArCoreLoaderType,
                BuildTargetGroup.Android);
        }

        PlayerSettings.Android.minSdkVersion =
            AndroidSdkVersions.AndroidApiLevel25;

        // This project renders through OpenGL ES on Android (AR Foundation /
        // ARCore 6.3.x on Unity 6000.3), so do not allow Unity's automatic
        // graphics selection to place Vulkan first.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.Android.optimizedFramePacing = false;

        EditorUtility.SetDirty(perBuildTarget);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
        AssetDatabase.SaveAssets();

        if (loaderAssigned)
        {
            Debug.Log(
                "GlassGlobe ARCore setup: Android loader enabled; OpenGL ES 3 selected; runtime camera tracking starts on demand.");
        }
        else
        {
            Debug.LogWarning(
                "GlassGlobe ARCore setup: loader metadata is not ready yet. Close XR Plug-in Management if it is open, then run GlassGlobe/Configure Optional ARCore Camera.");
        }
    }

    private static XRGeneralSettingsPerBuildTarget GetOrCreateGeneralSettings()
    {
        string[] settingsGuids =
            AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
        if (settingsGuids.Length > 0)
        {
            string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuids[0]);
            return AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                settingsPath);
        }

        const string xrFolder = "Assets/XR";
        const string settingsFolder = "Assets/XR/Settings";
        if (!AssetDatabase.IsValidFolder(xrFolder))
        {
            AssetDatabase.CreateFolder("Assets", "XR");
        }

        if (!AssetDatabase.IsValidFolder(settingsFolder))
        {
            AssetDatabase.CreateFolder(xrFolder, "Settings");
        }

        XRGeneralSettingsPerBuildTarget settings =
            ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(
            settings,
            settingsFolder + "/XRGeneralSettingsPerBuildTarget.asset");
        AssetDatabase.SaveAssets();
        return settings;
    }
}

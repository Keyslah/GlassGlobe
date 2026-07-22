using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Keeps the generated project configured for ARCore after package resolution
/// and before Android builds. This is idempotent and safe to run repeatedly.
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

    [MenuItem("GlassGlobe/Configure Invisible ARCore Tracking")]
    public static void EnsureConfigured()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureConfigured;
            return;
        }

        XRGeneralSettingsPerBuildTarget perBuildTarget =
            XRGeneralSettingsPerBuildTarget.GetOrCreate();
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
            AndroidSdkVersions.AndroidApiLevel24;

        EditorUtility.SetDirty(perBuildTarget);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
        AssetDatabase.SaveAssets();

        if (loaderAssigned)
        {
            Debug.Log(
                "GlassGlobe ARCore setup: Android loader enabled; camera tracking can run with its background hidden.");
        }
        else
        {
            Debug.LogWarning(
                "GlassGlobe ARCore setup: loader metadata is not ready yet. Close XR Plug-in Management if it is open, then run GlassGlobe/Configure Invisible ARCore Tracking.");
        }
    }
}

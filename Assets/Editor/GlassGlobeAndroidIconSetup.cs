using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Keeps every Android launcher-icon variant pointed at the approved
/// GlassGlobe artwork. Adaptive icons use the complete artwork as their
/// background and a transparent foreground, so launchers can apply their
/// preferred mask without rendering the globe twice.
/// </summary>
public static class GlassGlobeAndroidIconSetup
{
    private const string IconPath =
        "Assets/GlassGlobe/Art/GlassGlobeIconCartoonNaturalClouds.png";
    private const string TransparentForegroundPath =
        "Assets/GlassGlobe/Art/GlassGlobeIconTransparentForeground.asset";

    [MenuItem("GlassGlobe/Configure Android Launcher Icon")]
    public static void EnsureConfigured()
    {
        AssetDatabase.ImportAsset(
            IconPath,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);
        ConfigureIconImporter();

        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null)
        {
            throw new BuildFailedException(
                "GlassGlobe Android icon setup could not load " + IconPath);
        }

        Texture2D transparentForeground = GetOrCreateTransparentForeground();
        ConfigureSingleLayerIcons(AndroidPlatformIconKind.Legacy, icon);
        ConfigureSingleLayerIcons(AndroidPlatformIconKind.Round, icon);
        ConfigureAdaptiveIcons(icon, transparentForeground);

        AssetDatabase.SaveAssets();
        Debug.Log(
            "GlassGlobe Android icon setup: legacy, round, and adaptive " +
            "launcher icons use " + IconPath);
    }

    private static void ConfigureIconImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(IconPath) as
            TextureImporter;
        if (importer == null)
        {
            throw new BuildFailedException(
                "GlassGlobe Android icon setup could not inspect " + IconPath);
        }

        bool changed = false;
        changed |= SetIfDifferent(
            importer.textureType,
            TextureImporterType.Sprite,
            value => importer.textureType = value);
        changed |= SetIfDifferent(
            importer.spriteImportMode,
            SpriteImportMode.Single,
            value => importer.spriteImportMode = value);
        changed |= SetIfDifferent(
            importer.sRGBTexture,
            true,
            value => importer.sRGBTexture = value);
        changed |= SetIfDifferent(
            importer.mipmapEnabled,
            false,
            value => importer.mipmapEnabled = value);
        changed |= SetIfDifferent(
            importer.npotScale,
            TextureImporterNPOTScale.None,
            value => importer.npotScale = value);
        changed |= SetIfDifferent(
            importer.textureCompression,
            TextureImporterCompression.Uncompressed,
            value => importer.textureCompression = value);
        changed |= SetIfDifferent(
            importer.maxTextureSize,
            2048,
            value => importer.maxTextureSize = value);

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static bool SetIfDifferent<T>(
        T current,
        T desired,
        System.Action<T> assign)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(
            current,
            desired))
        {
            return false;
        }

        assign(desired);
        return true;
    }

    private static void ConfigureSingleLayerIcons(
        PlatformIconKind kind,
        Texture2D icon)
    {
        PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(
            NamedBuildTarget.Android,
            kind);
        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].maxLayerCount != 1)
            {
                throw new BuildFailedException(
                    "GlassGlobe Android icon setup expected one layer for " +
                    kind + " at " + slots[index].width + "x" +
                    slots[index].height + ".");
            }

            slots[index].SetTextures(new[] { icon });
        }

        PlayerSettings.SetPlatformIcons(
            NamedBuildTarget.Android,
            kind,
            slots);
    }

    private static void ConfigureAdaptiveIcons(
        Texture2D background,
        Texture2D foreground)
    {
        PlatformIconKind kind = AndroidPlatformIconKind.Adaptive;
        PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(
            NamedBuildTarget.Android,
            kind);
        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].maxLayerCount != 2)
            {
                throw new BuildFailedException(
                    "GlassGlobe Android icon setup expected background and " +
                    "foreground layers for the adaptive icon at " +
                    slots[index].width + "x" + slots[index].height + ".");
            }

            // Android adaptive layer order is background first, foreground second.
            slots[index].SetTextures(new[] { background, foreground });
        }

        PlayerSettings.SetPlatformIcons(
            NamedBuildTarget.Android,
            kind,
            slots);
    }

    private static Texture2D GetOrCreateTransparentForeground()
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            TransparentForegroundPath);
        if (texture != null)
        {
            return texture;
        }

        texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "GlassGlobeIconTransparentForeground",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, Color.clear);
        texture.Apply(false, false);
        AssetDatabase.CreateAsset(texture, TransparentForegroundPath);
        return texture;
    }
}

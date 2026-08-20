using UnityEditor;
using UnityEngine;

/// <summary>
/// Replaces Unity's startup branding with the approved GlassGlobe artwork.
/// </summary>
public static class GlassGlobeSplashScreenSetup
{
    private const string LogoPath =
        "Assets/GlassGlobe/Art/GlassGlobeIconCartoonNaturalClouds.png";
    private const float LogoDurationSeconds = 2f;

    [MenuItem("GlassGlobe/Configure Startup Splash")]
    public static void EnsureConfigured()
    {
        Sprite logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (logo == null)
        {
            throw new UnityEditor.Build.BuildFailedException(
                "GlassGlobe splash setup could not load a Sprite from " +
                LogoPath + ". Run the Android icon setup first.");
        }

        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.animationMode =
            PlayerSettings.SplashScreen.AnimationMode.Static;
        PlayerSettings.SplashScreen.drawMode =
            PlayerSettings.SplashScreen.DrawMode.AllSequential;
        PlayerSettings.SplashScreen.background = null;
        PlayerSettings.SplashScreen.backgroundPortrait = null;
        PlayerSettings.SplashScreen.blurBackgroundImage = false;

        // Match the artwork's outer edge so its square boundary disappears
        // into the portrait splash background.
        PlayerSettings.SplashScreen.backgroundColor =
            new Color32(1, 14, 44, 255);
        PlayerSettings.SplashScreen.logos = new[]
        {
            PlayerSettings.SplashScreenLogo.Create(
                LogoDurationSeconds,
                logo)
        };

        AssetDatabase.SaveAssets();
        Debug.Log(
            "GlassGlobe splash setup: Unity logo disabled; startup uses " +
            LogoPath);
    }
}

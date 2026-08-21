using System;
using System.Collections.Generic;
using System.IO;
using GlassGlobe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GlassGlobeBuildValidator
{
    private const string ScenePath = "Assets/GlassGlobe/Scenes/GlassGlobePreview.unity";
    private const string FullNightSourceRoot =
        "Assets/StreamingAssets/GlassGlobeNightFullRes";

    private static readonly string[] FullNightSourceIds =
        { "A1", "B1", "C1", "D1", "A2", "B2", "C2", "D2" };

    private static readonly long[] FullNightSourceSizes =
    {
        30825698L,
        29472401L,
        59097456L,
        39122860L,
        7830664L,
        21337236L,
        13553256L,
        14400257L
    };

    [MenuItem("GlassGlobe/Validate Preview Scene")]
    public static void ValidatePreviewScene()
    {
        if (!ValidatePreviewSceneInternal(true))
        {
            throw new Exception("GlassGlobe build validation failed. See Console errors above.");
        }
    }

    public static bool ValidateLoadedPreviewScene()
    {
        return ValidatePreviewSceneInternal(false);
    }

    private static bool ValidatePreviewSceneInternal(bool openScene)
    {
        Debug.Log("GlassGlobeBuildValidator: validating " + ScenePath);
        int errors = 0;

        string absoluteScenePath = Path.Combine(Directory.GetCurrentDirectory(), ScenePath);
        if (!File.Exists(absoluteScenePath))
        {
            LogError(ref errors, "GlassGlobeBuildValidator: scene does not exist at " + ScenePath);
            Debug.LogError("GlassGlobe build validation failed with " + errors + " issue(s).");
            return false;
        }

        if (openScene || SceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            LogError(ref errors, "GlassGlobeBuildValidator: preview scene is not loaded.");
            Debug.LogError("GlassGlobe build validation failed with " + errors + " issue(s).");
            return false;
        }

        Camera mainCamera = FindMainCamera(scene);
        LogRequired(ref errors, mainCamera != null, "GlassGlobeBuildValidator: missing MainCamera-tagged camera.");
        if (mainCamera != null)
        {
            LogRequired(ref errors, !mainCamera.orthographic, "GlassGlobeBuildValidator: Main Camera must use real perspective, not orthographic rendering.");
        }

        GlobeRenderer globe = FindInScene<GlobeRenderer>(scene);
        LogRequired(ref errors, globe != null, "GlassGlobeBuildValidator: missing Earth Globe with GlobeRenderer.");
        if (globe != null)
        {
            LogRequired(
                ref errors,
                globe.longitudeSegments == 240 && globe.latitudeSegments == 120,
                "GlassGlobeBuildValidator: globe mesh must use the 240x120 full-resolution tile grid.");
        }

        GlobeGridRenderer grid = FindInScene<GlobeGridRenderer>(scene);
        LogRequired(ref errors, grid != null, "GlassGlobeBuildValidator: missing low-poly GlobeGridRenderer.");
        if (grid != null)
        {
            LogRequired(ref errors, grid.GetComponentsInChildren<LineRenderer>(true).Length > 0, "GlassGlobeBuildValidator: low-poly globe grid lines were not generated.");
        }

        PhonePoseSimulator phonePose = FindInScene<PhonePoseSimulator>(scene);
        LogRequired(ref errors, phonePose != null, "GlassGlobeBuildValidator: missing PhonePoseSimulator.");
        if (phonePose != null)
        {
            LogRequired(ref errors, phonePose.targetCamera != null, "GlassGlobeBuildValidator: PhonePoseSimulator targetCamera is not assigned.");
            LogRequired(ref errors, phonePose.eyeToPhoneDistanceInches >= 12.5f && phonePose.eyeToPhoneDistanceInches <= 14.5f, "GlassGlobeBuildValidator: eye-to-phone baseline should be about 13.5 inches.");
            LogRequired(ref errors, phonePose.PhysicalViewportFovDegrees >= 23f && phonePose.PhysicalViewportFovDegrees <= 26f, "GlassGlobeBuildValidator: default viewport FOV should approximate handheld physical scale.");
        }

        FarSideRaycaster raycaster = FindInScene<FarSideRaycaster>(scene);
        LogRequired(ref errors, raycaster != null, "GlassGlobeBuildValidator: missing FarSideRaycaster.");
        if (raycaster != null)
        {
            LogRequired(ref errors, raycaster.phonePose != null, "GlassGlobeBuildValidator: FarSideRaycaster phonePose is not assigned.");
            LogRequired(ref errors, raycaster.globe != null, "GlassGlobeBuildValidator: FarSideRaycaster globe is not assigned.");
            LogRequired(ref errors, raycaster.farSideMarker != null, "GlassGlobeBuildValidator: FarSideRaycaster farSideMarker is not assigned.");
            raycaster.UpdateRaycast();
            LogRequired(ref errors, raycaster.HasIntersection, "GlassGlobeBuildValidator: center reticle ray does not intersect the globe.");
        }

        GlassGlobeHUD hud = FindInScene<GlassGlobeHUD>(scene);
        LogRequired(ref errors, hud != null, "GlassGlobeBuildValidator: missing GlassGlobeHUD UI controls.");
        if (hud != null)
        {
            LogRequired(ref errors, hud.phonePose != null, "GlassGlobeBuildValidator: GlassGlobeHUD phonePose is not assigned.");
            LogRequired(ref errors, hud.farSideRaycaster != null, "GlassGlobeBuildValidator: GlassGlobeHUD farSideRaycaster is not assigned.");
            LogRequired(ref errors, hud.showHud, "GlassGlobeBuildValidator: GlassGlobeHUD is disabled.");
        }

        LogRequired(ref errors, FindInScene<GlassGlobeReticle>(scene) != null, "GlassGlobeBuildValidator: missing center reticle object.");
        LogRequired(ref errors, FindInScene<CountryLabelController>(scene) != null, "GlassGlobeBuildValidator: missing CountryLabelController.");

        WeatherOverlay weather = FindInScene<WeatherOverlay>(scene);
        LogRequired(ref errors, weather != null, "GlassGlobeBuildValidator: missing WeatherOverlay.");
        if (weather != null)
        {
            LogRequired(ref errors, weather.cloudMaterial != null, "GlassGlobeBuildValidator: WeatherOverlay cloud material is not assigned, the weather shader would be stripped.");
            LogRequired(ref errors, weather.radarMaterial != null, "GlassGlobeBuildValidator: WeatherOverlay radar material is not assigned, the weather shader would be stripped.");
        }

        SatelliteOverlay satellites = FindInScene<SatelliteOverlay>(scene);
        LogRequired(ref errors, satellites != null, "GlassGlobeBuildValidator: missing SatelliteOverlay.");
        if (satellites != null)
        {
            LogRequired(ref errors, satellites.markerMaterial != null, "GlassGlobeBuildValidator: SatelliteOverlay marker material is not assigned, the sprite shader would be stripped.");
        }

        EarthquakeOverlay earthquakes = FindInScene<EarthquakeOverlay>(scene);
        LogRequired(ref errors, earthquakes != null, "GlassGlobeBuildValidator: missing EarthquakeOverlay.");
        if (earthquakes != null)
        {
            LogRequired(ref errors, earthquakes.markerMaterial != null, "GlassGlobeBuildValidator: EarthquakeOverlay marker material is not assigned, the sprite shader would be stripped.");
        }

        EarthStyleController earthAtNight = FindInScene<EarthStyleController>(scene);
        LogRequired(
            ref errors,
            earthAtNight != null,
            "GlassGlobeBuildValidator: missing EarthStyleController on the globe.");

        Texture2D nightTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/GlassGlobe/Resources/GlassGlobeNightLights.jpg");
        LogRequired(
            ref errors,
            nightTexture != null,
            "GlassGlobeBuildValidator: Earth at Night Resources texture is missing.");
        if (nightTexture != null)
        {
            LogRequired(
                ref errors,
                nightTexture.width == 3600 && nightTexture.height == 1800,
                "GlassGlobeBuildValidator: Earth at Night must import at its native 3600x1800 size.");
        }

        ValidateFullResolutionNightSources(ref errors);

        string nightLayoutError;
        LogRequired(
            ref errors,
            NightMapTileLayout.ValidateContract(out nightLayoutError),
            "GlassGlobeBuildValidator: full-resolution night layout is invalid: " +
            nightLayoutError);

        Shader nightTileShader = Shader.Find("GlassGlobe/Earth at Night Tile");
        LogRequired(
            ref errors,
            nightTileShader != null,
            "GlassGlobeBuildValidator: full-resolution Earth at Night tile shader is missing.");

        if (globe != null && globe.globeMaterial != null)
        {
            LogRequired(
                ref errors,
                globe.globeMaterial.HasProperty("_NightTex"),
                "GlassGlobeBuildValidator: globe shader is missing _NightTex.");
            LogRequired(
                ref errors,
                globe.globeMaterial.HasProperty("_NightOpacity"),
                "GlassGlobeBuildValidator: globe shader is missing _NightOpacity.");
            LogRequired(
                ref errors,
                globe.globeMaterial.HasProperty("_NightCoverageTex"),
                "GlassGlobeBuildValidator: globe shader is missing _NightCoverageTex.");
        }

        BlueMarbleSurface blueMarble = FindInScene<BlueMarbleSurface>(scene);
        LogRequired(ref errors, blueMarble != null, "GlassGlobeBuildValidator: missing BlueMarbleSurface on the globe.");
        LogRequired(ref errors, HasBlueMarbleResource("Spring", 4096, 2048), "GlassGlobeBuildValidator: Blue Marble spring texture must import at 4096x2048.");
        LogRequired(ref errors, HasBlueMarbleResource("Summer", 16384, 8192), "GlassGlobeBuildValidator: Blue Marble summer texture must import at 16384x8192.");
        LogRequired(ref errors, HasBlueMarbleResource("Fall", 4096, 2048), "GlassGlobeBuildValidator: Blue Marble fall texture must import at 4096x2048.");
        LogRequired(ref errors, HasBlueMarbleResource("Winter", 4096, 2048), "GlassGlobeBuildValidator: Blue Marble winter texture must import at 4096x2048.");

        SunMoonBackground sunMoon = FindInScene<SunMoonBackground>(scene);
        LogRequired(ref errors, sunMoon != null, "GlassGlobeBuildValidator: missing SunMoonBackground.");
        if (sunMoon != null)
        {
            LogRequired(ref errors, sunMoon.planetMaterial != null, "GlassGlobeBuildValidator: SunMoonBackground planet material is not assigned, planet dots would share the Moon phase texture.");
        }

        CountryBorderRenderer borders = FindInScene<CountryBorderRenderer>(scene);
        LogRequired(ref errors, borders != null, "GlassGlobeBuildValidator: missing CountryBorderRenderer.");
        if (borders != null)
        {
            LineRenderer[] lineRenderers = borders.GetComponentsInChildren<LineRenderer>(true);
            LogRequired(ref errors, lineRenderers.Length > 0, "GlassGlobeBuildValidator: no country/continent LineRenderers were generated.");
            LogRequired(ref errors, HasOutline(borders, "Australia"), "GlassGlobeBuildValidator: geography missing Australia.");
            LogRequired(ref errors, HasOutline(borders, "Antarctica"), "GlassGlobeBuildValidator: geography missing Antarctica.");
            LogRequired(ref errors, HasOutline(borders, "Brazil"), "GlassGlobeBuildValidator: geography missing Brazil.");
            LogRequired(ref errors, HasOutline(borders, "South Africa"), "GlassGlobeBuildValidator: geography missing South Africa.");
            LogRequired(ref errors, HasOutline(borders, "New Zealand") || HasOutline(borders, "Indonesia"), "GlassGlobeBuildValidator: geography missing New Zealand or Indonesia.");
        }

        LogRequired(ref errors, HasGameObjectNamed(scene, "Earth Globe"), "GlassGlobeBuildValidator: missing Earth Globe object.");
        LogRequired(ref errors, HasGameObjectNamed(scene, "Debug User Position"), "GlassGlobeBuildValidator: missing user position marker.");
        LogRequired(ref errors, HasGameObjectNamed(scene, "Debug Far-Side Intersection"), "GlassGlobeBuildValidator: missing far-side intersection marker.");
        LogRequired(ref errors, HasGameObjectNamed(scene, "Debug Center Ray"), "GlassGlobeBuildValidator: missing center-screen ray debug line.");
        LogRequired(ref errors, HasGameObjectNamed(scene, "Center Reticle"), "GlassGlobeBuildValidator: missing Center Reticle object.");

        ValidateMissingComponentsAndReferences(scene, ref errors);

        if (errors == 0)
        {
            Debug.Log("GlassGlobe build validation passed: scene exists, camera/globe/UI/debug objects are present, sample geography is generated as 3D lat/lon line geometry, and no missing references were found.");
            return true;
        }

        Debug.LogError("GlassGlobe build validation failed with " + errors + " issue(s). See errors above.");
        return false;
    }

    private static Camera FindMainCamera(Scene scene)
    {
        List<Camera> cameras = GetComponentsInScene<Camera>(scene);
        for (int index = 0; index < cameras.Count; index++)
        {
            Camera camera = cameras[index];
            if (camera != null && camera.CompareTag("MainCamera"))
            {
                return camera;
            }
        }

        return cameras.Count > 0 ? cameras[0] : null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        List<T> components = GetComponentsInScene<T>(scene);
        return components.Count > 0 ? components[0] : null;
    }

    private static List<T> GetComponentsInScene<T>(Scene scene) where T : Component
    {
        List<T> components = new List<T>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int index = 0; index < roots.Length; index++)
        {
            components.AddRange(roots[index].GetComponentsInChildren<T>(true));
        }

        return components;
    }

    private static bool HasGameObjectNamed(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int index = 0; index < roots.Length; index++)
        {
            if (FindChildByName(roots[index].transform, name) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindChildByName(root.GetChild(index), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ValidateMissingComponentsAndReferences(Scene scene, ref int errors)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject gameObject = transforms[transformIndex].gameObject;
                Component[] components = gameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                    {
                        LogError(ref errors, "GlassGlobeBuildValidator: missing script/component on " + GetPath(gameObject) + ".");
                        continue;
                    }

                    SerializedObject serializedObject = new SerializedObject(component);
                    SerializedProperty property = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        if (property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        {
                            LogError(ref errors, "GlassGlobeBuildValidator: missing object reference on " + GetPath(gameObject) + " component " + component.GetType().Name + " property " + property.propertyPath + ".");
                        }
                    }
                }
            }
        }
    }

    private static bool HasOutline(CountryBorderRenderer borders, string outlineName)
    {
        if (borders.Outlines == null)
        {
            return false;
        }

        for (int index = 0; index < borders.Outlines.Count; index++)
        {
            CountryBorderRenderer.GeoOutline outline = borders.Outlines[index];
            if (outline != null && outline.name == outlineName)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFullResolutionNightSources(ref int errors)
    {
        long totalBytes = 0L;
        for (int index = 0; index < FullNightSourceIds.Length; index++)
        {
            string path = FullNightSourceRoot + "/BlackMarble_2016_" +
                FullNightSourceIds[index] + ".resource";
            string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), path);
            FileInfo source = new FileInfo(absolutePath);
            LogRequired(
                ref errors,
                source.Exists,
                "GlassGlobeBuildValidator: missing full-resolution night source " + path + ".");
            if (!source.Exists)
            {
                continue;
            }

            LogRequired(
                ref errors,
                source.Length == FullNightSourceSizes[index],
                "GlassGlobeBuildValidator: full-resolution night source " +
                FullNightSourceIds[index] + " has the wrong byte length.");
            totalBytes += source.Length;
        }

        LogRequired(
            ref errors,
            totalBytes == 215639828L,
            "GlassGlobeBuildValidator: full-resolution night source set must total 215639828 bytes.");
    }

    private static bool HasBlueMarbleResource(
        string season,
        int expectedWidth,
        int expectedHeight)
    {
        string path = "Assets/GlassGlobe/Resources/GlassGlobeBlueMarble" + season + ".jpg";
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        return texture != null &&
            texture.width == expectedWidth &&
            texture.height == expectedHeight;
    }

    private static string GetPath(GameObject gameObject)
    {
        string path = gameObject.name;
        Transform parent = gameObject.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static void LogRequired(ref int errors, bool condition, string error)
    {
        if (!condition)
        {
            LogError(ref errors, error);
        }
    }

    private static void LogError(ref int errors, string error)
    {
        errors++;
        Debug.LogError(error);
    }
}

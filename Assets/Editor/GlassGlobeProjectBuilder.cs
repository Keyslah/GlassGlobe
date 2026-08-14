using GlassGlobe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GlassGlobeProjectBuilder
{
    private const string GlassGlobeRoot = "Assets/GlassGlobe";
    private const string ScenePath = "Assets/GlassGlobe/Scenes/GlassGlobePreview.unity";

    [MenuItem("GlassGlobe/Build Preview Scene")]
    public static void BuildPreviewScene()
    {
        Debug.Log("GlassGlobeProjectBuilder: BuildPreviewScene starting.");
        EnsureFolder("Assets", "GlassGlobe");
        EnsureFolder(GlassGlobeRoot, "Scenes");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Material globeMaterial = GlobeRenderer.CreateDefaultGlobeMaterial(new Color(0.01f, 0.08f, 0.10f, 0.06f));
        Material gridMaterial = CountryBorderRenderer.CreateDefaultBorderMaterial(new Color(0.15f, 0.88f, 1f, 0.95f));
        Material borderMaterial = CountryBorderRenderer.CreateDefaultBorderMaterial(new Color(1f, 0.9f, 0.35f, 1f));
        Material userMarkerMaterial = CreateUnlitMaterial("GlassGlobe User Marker", new Color(0.2f, 1f, 0.45f, 1f));
        Material nearMarkerMaterial = CreateUnlitMaterial("GlassGlobe Near Marker", new Color(1f, 0.65f, 0.15f, 1f));
        Material farMarkerMaterial = CreateUnlitMaterial("GlassGlobe Far Marker", new Color(1f, 0.2f, 0.25f, 1f));
        Material upLineMaterial = CreateUnlitMaterial("GlassGlobe Up Line", new Color(0.35f, 1f, 0.55f, 1f));
        Material downLineMaterial = CreateUnlitMaterial("GlassGlobe Down Line", new Color(0.55f, 0.75f, 1f, 1f));
        Material rayLineMaterial = CreateUnlitMaterial("GlassGlobe Center Ray", new Color(1f, 0.25f, 0.3f, 1f));

        GameObject root = new GameObject("GlassGlobe Preview Root");

        GameObject lightObject = new GameObject("Preview Directional Light");
        lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;

        GameObject globeObject = new GameObject("Earth Globe");
        globeObject.transform.SetParent(root.transform, false);
        GlobeRenderer globe = globeObject.AddComponent<GlobeRenderer>();
        globe.radiusUnits = EarthMath.DefaultEarthRadiusUnits;
        globe.center = Vector3.zero;
        globe.longitudeSegments = 128;
        globe.latitudeSegments = 64;
        globe.globeMaterial = globeMaterial;
        globe.globeColor = new Color(0.01f, 0.08f, 0.10f, 0.06f);
        globe.RebuildGlobe();

        EarthStyleController earthStyle = globeObject.AddComponent<EarthStyleController>();
        earthStyle.globe = globe;

        BlueMarbleSurface blueMarble = globeObject.AddComponent<BlueMarbleSurface>();
        blueMarble.globe = globe;
        ConfigureBlueMarbleTexture("Spring");
        ConfigureBlueMarbleTexture("Summer");
        ConfigureBlueMarbleTexture("Fall");
        ConfigureBlueMarbleTexture("Winter");

        GameObject gridRoot = new GameObject("Low Poly Earth Grid");
        gridRoot.transform.SetParent(root.transform, false);
        GlobeGridRenderer gridRenderer = gridRoot.AddComponent<GlobeGridRenderer>();
        gridRenderer.globe = globe;
        gridRenderer.gridMaterial = gridMaterial;
        gridRenderer.longitudeLineCount = 24;
        gridRenderer.latitudeLineCount = 13;
        gridRenderer.segmentsPerLine = 36;
        gridRenderer.surfaceOffset = 0.045f;
        gridRenderer.lineWidth = 0.018f;
        gridRenderer.RebuildGrid();

        GameObject viewportObject = new GameObject("Simulated Phone Viewport");
        viewportObject.transform.SetParent(root.transform, false);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.transform.SetParent(viewportObject.transform, false);
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.005f, 0.007f, 0.01f, 1f);
        camera.fieldOfView = PhonePoseSimulator.DefaultViewportFovDegrees;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 120f;
        cameraObject.AddComponent<AudioListener>();

        GameObject debugRoot = new GameObject("Debug Visualization");
        debugRoot.transform.SetParent(root.transform, false);
        GameObject userMarker = CreateSphereMarker("Debug User Position", 0.015f, userMarkerMaterial, debugRoot.transform);
        GameObject nearMarker = CreateSphereMarker("Debug Near-Side Entry", 0.012f, nearMarkerMaterial, debugRoot.transform);
        GameObject farMarker = CreateSphereMarker("Debug Far-Side Intersection", 0.10f, farMarkerMaterial, debugRoot.transform);
        LineRenderer localUpLine = CreateDebugLine("Debug Local Up", upLineMaterial, 0.008f, debugRoot.transform);
        LineRenderer localDownLine = CreateDebugLine("Debug Local Down", downLineMaterial, 0.006f, debugRoot.transform);
        LineRenderer centerRayLine = CreateDebugLine("Debug Center Ray", rayLineMaterial, 0.003f, debugRoot.transform);

        PhonePoseSimulator phonePose = viewportObject.AddComponent<PhonePoseSimulator>();
        phonePose.targetCamera = camera;
        phonePose.userCoordinate = new GeoCoordinate(37.7749f, -122.4194f);
        phonePose.earthCenter = globe.Center;
        phonePose.earthRadiusUnits = globe.RadiusUnits;
        phonePose.observerHeightUnits = 0.35f;
        phonePose.eyeToPhoneDistanceInches = PhonePoseSimulator.DefaultEyeToPhoneDistanceInches;
        phonePose.phoneViewportHeightInches = PhonePoseSimulator.DefaultPhoneViewportHeightInches;
        phonePose.headingDegrees = 120f;
        phonePose.tiltDegrees = 45f;
        phonePose.UsePhysicalViewportFov();
        phonePose.userPositionMarker = userMarker.transform;
        phonePose.localUpLine = localUpLine;
        phonePose.localDownLine = localDownLine;

        PhonePoseSensors poseSensors = viewportObject.AddComponent<PhonePoseSensors>();
        poseSensors.simulator = phonePose;
        poseSensors.globe = globe;
        poseSensors.targetCamera = camera;
        poseSensors.userPositionMarker = userMarker.transform;
        poseSensors.localUpLine = localUpLine;
        poseSensors.localDownLine = localDownLine;
        poseSensors.observerHeightUnits = 0.35f;
        poseSensors.cameraFovDegrees = PhonePoseSimulator.DefaultViewportFovDegrees;

        CameraFeedRenderer cameraFeed = cameraObject.AddComponent<CameraFeedRenderer>();
        cameraFeed.targetCamera = camera;
        cameraFeed.poseSensors = poseSensors;
        cameraFeed.feedVerticalFovDegrees = 70f;
        cameraFeed.windowFovDegrees = PhonePoseSimulator.DefaultViewportFovDegrees;
        cameraFeed.startEnabledOnDevice = true;

        Shader feedShader = Shader.Find("GlassGlobe/Camera Feed");
        if (feedShader != null)
        {
            Material feedMaterial = new Material(feedShader);
            feedMaterial.name = "GlassGlobe Camera Feed";
            cameraFeed.feedMaterial = feedMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: GlassGlobe/Camera Feed shader not found at scene build time.");
        }

        GameObject galaxyObject = new GameObject("Milky Way Background");
        galaxyObject.transform.SetParent(root.transform, false);
        MilkyWayBackground milkyWay = galaxyObject.AddComponent<MilkyWayBackground>();
        milkyWay.targetCamera = camera;
        milkyWay.poseSensors = poseSensors;
        milkyWay.simulator = phonePose;

        Shader galaxyShader = Shader.Find("GlassGlobe/Galaxy");
        if (galaxyShader != null)
        {
            Material galaxyMaterial = new Material(galaxyShader);
            galaxyMaterial.name = "GlassGlobe Galaxy";

            const string galaxyTexturePath = "Assets/GlassGlobe/Textures/MilkyWayPanorama.jpg";
            TextureImporter galaxyImporter = AssetImporter.GetAtPath(galaxyTexturePath) as TextureImporter;
            if (galaxyImporter != null &&
                (galaxyImporter.maxTextureSize < 8192 || galaxyImporter.npotScale != TextureImporterNPOTScale.None))
            {
                galaxyImporter.maxTextureSize = 8192;
                galaxyImporter.npotScale = TextureImporterNPOTScale.None;
                galaxyImporter.SaveAndReimport();
                Debug.Log("GlassGlobeProjectBuilder: preserved the Milky Way panorama at its native 6000x3000 resolution.");
            }

            Texture2D galaxyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(galaxyTexturePath);
            if (galaxyTexture != null)
            {
                galaxyMaterial.mainTexture = galaxyTexture;
            }
            else
            {
                Debug.LogWarning("GlassGlobeProjectBuilder: Milky Way panorama texture not found at Assets/GlassGlobe/Textures/MilkyWayPanorama.jpg.");
            }

            milkyWay.galaxyMaterial = galaxyMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: GlassGlobe/Galaxy shader not found at scene build time.");
        }

        GameObject sunMoonObject = new GameObject("Sun Moon Background");
        sunMoonObject.transform.SetParent(root.transform, false);
        SunMoonBackground sunMoon = sunMoonObject.AddComponent<SunMoonBackground>();
        sunMoon.targetCamera = camera;
        sunMoon.poseSensors = poseSensors;
        sunMoon.simulator = phonePose;

        Shader skySpriteShader = Shader.Find("GlassGlobe/Sky Sprite");
        if (skySpriteShader != null)
        {
            Material sunMaterial = new Material(skySpriteShader);
            sunMaterial.name = "GlassGlobe Sun Sprite";
            sunMoon.sunMaterial = sunMaterial;

            Material moonMaterial = new Material(skySpriteShader);
            moonMaterial.name = "GlassGlobe Moon Sprite";
            sunMoon.moonMaterial = moonMaterial;

            Material planetMaterial = new Material(skySpriteShader);
            planetMaterial.name = "GlassGlobe Planet Sprite";
            sunMoon.planetMaterial = planetMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: GlassGlobe/Sky Sprite shader not found at scene build time.");
        }

        GameObject weatherObject = new GameObject("Weather Overlay");
        weatherObject.transform.SetParent(root.transform, false);
        WeatherOverlay weather = weatherObject.AddComponent<WeatherOverlay>();
        weather.globe = globe;

        Shader weatherShader = Shader.Find("GlassGlobe/Weather");
        if (weatherShader != null)
        {
            Material cloudMaterial = new Material(weatherShader);
            cloudMaterial.name = "GlassGlobe Weather Clouds";
            weather.cloudMaterial = cloudMaterial;

            Material radarMaterial = new Material(weatherShader);
            radarMaterial.name = "GlassGlobe Weather Radar";
            weather.radarMaterial = radarMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: GlassGlobe/Weather shader not found at scene build time.");
        }

        if (!GlassGlobeLandMaskBaker.EnsureBaked())
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: land mask could not be baked; Earth art will report it missing.");
        }

        GameObject earthArtObject = new GameObject("Earth Art Overlay");
        earthArtObject.transform.SetParent(root.transform, false);
        EarthArtOverlay earthArt = earthArtObject.AddComponent<EarthArtOverlay>();
        earthArt.globe = globe;
        earthArt.weatherOverlay = weather;

        Shader earthArtShader = Shader.Find("GlassGlobe/Earth Art");
        Shader artCloudShader = Shader.Find("GlassGlobe/Art Clouds");
        if (earthArtShader != null && artCloudShader != null)
        {
            Material earthArtMaterial = new Material(earthArtShader);
            earthArtMaterial.name = "GlassGlobe Earth Art";
            earthArt.earthArtMaterial = earthArtMaterial;

            Material artCloudMaterial = new Material(artCloudShader);
            artCloudMaterial.name = "GlassGlobe Art Clouds";
            earthArt.artCloudMaterial = artCloudMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: Earth art shaders not found at scene build time.");
        }

        Shader oceanShader = Shader.Find("GlassGlobe/Stylized Ocean");
        if (oceanShader != null)
        {
            // Creating the material here keeps the stylized ocean shader from
            // being stripped from the player build. EarthArtOverlay.BuildLayers
            // assigns the land mask and ripple normal at runtime and sets the
            // cull/depth/render-queue state for the far-side shell.
            Material oceanMaterial = new Material(oceanShader);
            oceanMaterial.name = "GlassGlobe Stylized Ocean";
            earthArt.oceanMaterial = oceanMaterial;
        }
        else
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: GlassGlobe/Stylized Ocean shader not found at scene build time.");
        }

        GameObject satelliteObject = new GameObject("Satellite Overlay");
        satelliteObject.transform.SetParent(root.transform, false);
        SatelliteOverlay satelliteOverlay = satelliteObject.AddComponent<SatelliteOverlay>();
        satelliteOverlay.globe = globe;
        satelliteOverlay.targetCamera = camera;
        if (skySpriteShader != null)
        {
            Material satelliteMaterial = new Material(skySpriteShader);
            satelliteMaterial.name = "GlassGlobe Satellite Marker";
            satelliteOverlay.markerMaterial = satelliteMaterial;
        }

        GameObject earthquakeObject = new GameObject("Earthquake Overlay");
        earthquakeObject.transform.SetParent(root.transform, false);
        EarthquakeOverlay earthquakeOverlay = earthquakeObject.AddComponent<EarthquakeOverlay>();
        earthquakeOverlay.globe = globe;
        earthquakeOverlay.targetCamera = camera;
        if (skySpriteShader != null)
        {
            Material earthquakeMaterial = new Material(skySpriteShader);
            earthquakeMaterial.name = "GlassGlobe Earthquake Marker";
            earthquakeOverlay.markerMaterial = earthquakeMaterial;
        }

        GameObject borderRoot = new GameObject("Country Border Line Renderers");
        borderRoot.transform.SetParent(root.transform, false);
        CountryBorderRenderer borderRenderer = borderRoot.AddComponent<CountryBorderRenderer>();
        borderRenderer.globe = globe;
        borderRenderer.borderMaterial = borderMaterial;
        borderRenderer.surfaceOffset = 0.08f;
        borderRenderer.maxSegmentDegrees = 1.5f;
        borderRenderer.showCountryOutlines = true;
        borderRenderer.showContinentOutlines = true;
        if (!borderRenderer.LoadRealOutlines())
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: real country data missing from Resources, falling back to sample outlines.");
            borderRenderer.ResetToSampleData();
        }

        borderRenderer.RebuildBorders();

        GameObject labelRoot = new GameObject("Country Labels");
        labelRoot.transform.SetParent(root.transform, false);
        CountryLabelController labelController = labelRoot.AddComponent<CountryLabelController>();
        labelController.borderRenderer = borderRenderer;
        labelController.globe = globe;
        labelController.targetCamera = camera;
        labelController.showCountryLabels = false;
        labelController.showContinentLabels = false;
        labelController.surfaceOffset = 0.22f;
        labelController.characterSize = 0.08f;
        labelController.labelColor = new Color(0.92f, 0.98f, 1f, 0.95f);
        labelController.RebuildLabels();

        GameObject raycasterObject = new GameObject("Far Side Raycaster");
        raycasterObject.transform.SetParent(root.transform, false);
        FarSideRaycaster raycaster = raycasterObject.AddComponent<FarSideRaycaster>();
        raycaster.phonePose = phonePose;
        raycaster.globe = globe;
        raycaster.targetCamera = camera;
        raycaster.centerRayLine = centerRayLine;
        raycaster.nearSideMarker = nearMarker.transform;
        raycaster.farSideMarker = farMarker.transform;

        GameObject hudObject = new GameObject("GlassGlobe HUD");
        hudObject.transform.SetParent(root.transform, false);
        GlassGlobeHUD hud = hudObject.AddComponent<GlassGlobeHUD>();
        hud.phonePose = phonePose;
        hud.poseSensors = poseSensors;
        hud.cameraFeed = cameraFeed;
        hud.farSideRaycaster = raycaster;
        hud.borderRenderer = borderRenderer;
        hud.showHud = true;
        hud.panelRect = new Rect(20f, 20f, 460f, 330f);

        GameObject reticleObject = new GameObject("Center Reticle");
        reticleObject.transform.SetParent(root.transform, false);
        reticleObject.AddComponent<GlassGlobeReticle>();

        GameObject validatorObject = new GameObject("GlassGlobe Scene Validator");
        validatorObject.transform.SetParent(root.transform, false);
        GlassGlobeSceneValidator validator = validatorObject.AddComponent<GlassGlobeSceneValidator>();
        validator.validateOnStart = true;

        phonePose.ApplyPose();
        raycaster.UpdateRaycast();
        bool runtimeValid = validator.ValidateScene();

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!saved)
        {
            Debug.LogError("GlassGlobeProjectBuilder: failed to save preview scene to " + ScenePath);
            return;
        }

        bool editorValid = GlassGlobeBuildValidator.ValidateLoadedPreviewScene();
        if (!runtimeValid || !editorValid)
        {
            Debug.LogError("GlassGlobeProjectBuilder: BuildPreviewScene completed, but validation reported missing pieces. Open the Console for details.");
            return;
        }

        Debug.Log("GlassGlobeProjectBuilder: BuildPreviewScene completed successfully. Scene saved to " + ScenePath);
    }

    /// <summary>
    /// Configures one on-demand NASA Blue Marble seasonal map so it is
    /// imported the way the globe surface needs it: 4096x2048 with mipmaps,
    /// no CPU copy, longitude wrapping, and ASTC on Android.
    /// Reimport only happens when something actually differs, so repeated
    /// scene builds stay fast.
    /// </summary>
    private static void ConfigureBlueMarbleTexture(string season)
    {
        string path = GlassGlobeRoot + "/Resources/GlassGlobeBlueMarble" + season + ".jpg";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning("GlassGlobeProjectBuilder: Blue Marble " + season + " texture not found at " + path + ".");
            return;
        }

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        bool androidNeedsUpdate =
            !android.overridden ||
            android.maxTextureSize != 4096 ||
            android.format != TextureImporterFormat.ASTC_6x6;

        bool needsUpdate =
            androidNeedsUpdate ||
            !importer.mipmapEnabled ||
            importer.isReadable ||
            importer.streamingMipmaps ||
            importer.maxTextureSize != 4096 ||
            importer.npotScale != TextureImporterNPOTScale.None ||
            importer.wrapModeU != TextureWrapMode.Repeat ||
            importer.wrapModeV != TextureWrapMode.Clamp ||
            importer.anisoLevel != 4 ||
            !importer.sRGBTexture;

        if (needsUpdate)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            // Mipmap streaming stays OFF: every quality level in this project
            // has streamingMipmapsActive = 0, so the flag would claim a feature
            // the build never runs. Turn it on only together with that quality
            // setting.
            importer.streamingMipmaps = false;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            // Equirectangular: longitude wraps at the antimeridian, latitude
            // must clamp so the poles do not bleed across.
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Compressed;

            android.overridden = true;
            android.maxTextureSize = 4096;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = (int)TextureCompressionQuality.Normal;
            importer.SetPlatformTextureSettings(android);

            importer.SaveAndReimport();
            Debug.Log("GlassGlobeProjectBuilder: reimported the Blue Marble " + season + " map at 4096x2048 with Android ASTC 6x6.");
        }

    }

    private static GameObject CreateSphereMarker(string name, float diameter, Material material, Transform parent)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        marker.transform.SetParent(parent, false);
        marker.transform.localScale = Vector3.one * diameter;

        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        return marker;
    }

    private static LineRenderer CreateDebugLine(string name, Material material, float width, Transform parent)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(parent, false);
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.widthMultiplier = width;
        lineRenderer.numCapVertices = 3;
        lineRenderer.numCornerVertices = 3;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.sharedMaterial = material;
        return lineRenderer;
    }

    private static Material CreateUnlitMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.name = name;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}

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
        camera.fieldOfView = 32.4f;
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
        phonePose.eyeToPhoneDistanceInches = 10f;
        phonePose.phoneViewportHeightInches = 5.8f;
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
        poseSensors.cameraFovDegrees = 32.4f;

        CameraFeedRenderer cameraFeed = cameraObject.AddComponent<CameraFeedRenderer>();
        cameraFeed.targetCamera = camera;
        cameraFeed.poseSensors = poseSensors;
        cameraFeed.feedVerticalFovDegrees = 70f;
        cameraFeed.windowFovDegrees = 32.4f;
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

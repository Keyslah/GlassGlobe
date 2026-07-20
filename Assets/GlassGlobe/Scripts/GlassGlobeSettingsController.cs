using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Draws the expandable Settings flow and applies settings to the existing
    /// GlassGlobe systems. The controller is attached at runtime by GlassGlobeHUD,
    /// so the generated preview scene does not need a hand-maintained reference.
    /// </summary>
    public sealed class GlassGlobeSettingsController : MonoBehaviour
    {
        private const float MaxReliableSkyAlignmentAltitudeDegrees = 80f;

        public GlassGlobeHUD hud;
        public CameraFeedRenderer cameraFeed;
        public PhonePoseSensors poseSensors;
        public PhonePoseSimulator phonePose;
        public FarSideRaycaster farSideRaycaster;
        public CountryBorderRenderer borderRenderer;
        public CountryLabelController labelController;
        public MilkyWayBackground milkyWay;
        public SunMoonBackground sunMoon;
        public EarthStyleController earthStyle;
        public WeatherOverlay weather;
        public GlobeGridRenderer gridRenderer;

        [Min(0.5f)]
        public float settingsButtonVisibleSeconds = 3f;

        [Min(0.05f)]
        public float settingsButtonFadeSeconds = 0.35f;

        private enum SettingsPage
        {
            Closed,
            Settings,
            Camera,
            Viewpoint,
            Background,
            Display,
            EarthStyles,
            Weather,
            Orient,
            OrientCapture,
            Privacy
        }

        private enum AlignBody
        {
            Sun,
            Moon
        }

        private struct ViewpointChoice
        {
            public string Name;
            public string Kind;
            public GeoCoordinate Coordinate;

            public ViewpointChoice(string name, string kind, float latitude, float longitude)
            {
                Name = name;
                Kind = kind;
                Coordinate = new GeoCoordinate(latitude, longitude);
            }

            public ViewpointChoice(string name, string kind, GeoCoordinate coordinate)
            {
                Name = name;
                Kind = kind;
                Coordinate = coordinate;
            }
        }

        private struct TouchTarget
        {
            public Rect ScreenRect;
            public Action Action;

            public TouchTarget(Rect screenRect, Action action)
            {
                ScreenRect = screenRect;
                Action = action;
            }
        }

        private static readonly ViewpointChoice[] CityChoices =
        {
            new ViewpointChoice("Beijing, China", "City", 39.9042f, 116.4074f),
            new ViewpointChoice("Shanghai, China", "City", 31.2304f, 121.4737f),
            new ViewpointChoice("Tokyo, Japan", "City", 35.6762f, 139.6503f),
            new ViewpointChoice("Singapore", "City", 1.3521f, 103.8198f),
            new ViewpointChoice("Mumbai, India", "City", 19.0760f, 72.8777f),
            new ViewpointChoice("Delhi, India", "City", 28.6139f, 77.2090f),
            new ViewpointChoice("Dubai, United Arab Emirates", "City", 25.2048f, 55.2708f),
            new ViewpointChoice("Sydney, Australia", "City", -33.8688f, 151.2093f),
            new ViewpointChoice("Cairo, Egypt", "City", 30.0444f, 31.2357f),
            new ViewpointChoice("Nairobi, Kenya", "City", -1.2921f, 36.8219f),
            new ViewpointChoice("Johannesburg, South Africa", "City", -26.2041f, 28.0473f),
            new ViewpointChoice("London, United Kingdom", "City", 51.5074f, -0.1278f),
            new ViewpointChoice("Paris, France", "City", 48.8566f, 2.3522f),
            new ViewpointChoice("Moscow, Russia", "City", 55.7558f, 37.6173f),
            new ViewpointChoice("Istanbul, Turkey", "City", 41.0082f, 28.9784f),
            new ViewpointChoice("New York City, United States", "City", 40.7128f, -74.0060f),
            new ViewpointChoice("Chicago, United States", "City", 41.8781f, -87.6298f),
            new ViewpointChoice("San Francisco, United States", "City", 37.7749f, -122.4194f),
            new ViewpointChoice("Los Angeles, United States", "City", 34.0522f, -118.2437f),
            new ViewpointChoice("Mexico City, Mexico", "City", 19.4326f, -99.1332f),
            new ViewpointChoice("Toronto, Canada", "City", 43.6532f, -79.3832f),
            new ViewpointChoice("Vancouver, Canada", "City", 49.2827f, -123.1207f),
            new ViewpointChoice("Rio de Janeiro, Brazil", "City", -22.9068f, -43.1729f),
            new ViewpointChoice("Sao Paulo, Brazil", "City", -23.5505f, -46.6333f)
        };

        private readonly List<ViewpointChoice> viewpointChoices = new List<ViewpointChoice>();
        private readonly List<ViewpointChoice> filteredChoices = new List<ViewpointChoice>();
        private readonly List<TouchTarget> touchTargets = new List<TouchTarget>();

        private SettingsPage currentPage = SettingsPage.Closed;
        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private GUIStyle buttonStyle;
        private GUIStyle entryButtonStyle;
        private Rect activeAreaRect;
        private float activeUiScale = 1f;
        private float lastInteractionTime;
        private Vector3 lastMousePosition;
        private bool hasLastMousePosition;
        private bool hudWasVisible = true;
        private bool hasSimulatorDefault;
        private GeoCoordinate simulatorDefaultCoordinate;
        private bool cameraSettingApplied;
        private bool appliedCameraSetting;
        private bool labelsSettingApplied;
        private bool appliedLabelsSetting;
        private string appliedDisplaySignature;
        private bool milkyWaySettingApplied;
        private bool appliedMilkyWaySetting;
        private bool sunSettingApplied;
        private bool appliedSunSetting;
        private bool moonSettingApplied;
        private bool appliedMoonSetting;
        private EarthArtOverlay earthArt;
        private string appliedEarthArtSignature;
        private bool nightLightsSettingApplied;
        private bool appliedNightLightsSetting;
        private bool rimGlowSettingApplied;
        private bool appliedRimGlowSetting;
        private bool weatherCloudsSettingApplied;
        private bool appliedWeatherCloudsSetting;
        private bool weatherRadarSettingApplied;
        private bool appliedWeatherRadarSetting;
        private AlignBody alignTarget = AlignBody.Sun;
        private Texture2D alignRingTexture;
        private GUIStyle captureTextStyle;
        private string orientStatusMessage = string.Empty;
        private string appliedViewpointSignature;
        private bool choicesBuiltFromCountries;
        private string viewpointSearch = string.Empty;
        private string latitudeText = "0.0000";
        private string longitudeText = "0.0000";
        private string customViewpointName = string.Empty;
        private string statusMessage = string.Empty;
        private Vector2 settingsScrollPosition;
        private Rect activeTouchViewportRect;
        private int trackedTouchFingerId = -1;
        private Vector2 touchStartScreenPoint;
        private bool touchDragged;
        private bool scrollTouchActive;

        public static GlassGlobeSettingsController EnsureInstance(GlassGlobeHUD owner)
        {
            if (owner == null)
            {
                return null;
            }

            GlassGlobeSettingsController controller = owner.GetComponent<GlassGlobeSettingsController>();
            if (controller == null)
            {
                controller = owner.gameObject.AddComponent<GlassGlobeSettingsController>();
            }

            controller.hud = owner;
            return controller;
        }

        private void Awake()
        {
            if (hud == null)
            {
                hud = GetComponent<GlassGlobeHUD>();
            }

            GlassGlobeSettingsState.Load();
            ResolveReferences();
            CaptureSimulatorDefault();
            SeedCoordinateFields();
            BuildViewpointChoices();
            lastInteractionTime = Time.unscaledTime;
        }

        private void Start()
        {
            ResolveReferences();
            CaptureSimulatorDefault();
            ApplySavedSettings();
        }

        private void OnDisable()
        {
            if (currentPage != SettingsPage.Closed && hud != null)
            {
                hud.showHud = hudWasVisible;
            }

            if (phonePose != null)
            {
                phonePose.DragInputBlocked = false;
            }
        }

        private void Update()
        {
            ResolveReferences();
            CaptureSimulatorDefault();
            BuildViewpointChoices();
            TrackInteraction();
            HandleMobileTouch();
            ApplySettingsIfChanged();

            if (phonePose != null)
            {
                phonePose.DragInputBlocked = currentPage != SettingsPage.Closed;
            }

            if (currentPage != SettingsPage.Closed && hud != null)
            {
                hud.showHud = false;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (Application.isMobilePlatform && Event.current.type == EventType.Repaint)
            {
                touchTargets.Clear();
            }

            if (currentPage == SettingsPage.Closed)
            {
                DrawSettingsEntryButton();
                return;
            }

            DrawSettingsPage();
        }

        private void DrawSettingsEntryButton()
        {
            float elapsed = Time.unscaledTime - lastInteractionTime;
            float alpha = 1f;
            if (elapsed > settingsButtonVisibleSeconds)
            {
                alpha = 1f - (elapsed - settingsButtonVisibleSeconds) / Mathf.Max(0.05f, settingsButtonFadeSeconds);
            }

            alpha = Mathf.Clamp01(alpha);
            if (alpha <= 0.01f)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            float minimumTouchHeight = Application.isMobilePlatform && Screen.dpi > 0f
                ? Mathf.Clamp(Screen.dpi * 0.3f, 48f, 144f)
                : 48f;
            float height = Mathf.Max(
                Mathf.Clamp(Screen.height * 0.06f, 48f, 72f),
                minimumTouchHeight);
            float maximumWidth = Mathf.Max(120f, safeArea.width - 40f);
            float minimumWidth = Mathf.Min(maximumWidth, Mathf.Max(132f, height * 2f));
            float width = Mathf.Clamp(
                Screen.width * 0.26f,
                minimumWidth,
                Mathf.Min(280f, maximumWidth));
            float safeBottom = Screen.height - safeArea.yMin;
            Rect buttonRect = new Rect(
                safeArea.xMax - width - 20f,
                safeBottom - height - 24f,
                width,
                height);

            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * alpha);
            GUI.enabled = alpha > 0.15f;
            entryButtonStyle.fontSize = Application.isMobilePlatform
                ? Mathf.RoundToInt(buttonStyle.fontSize * GetMobileUiScale())
                : buttonStyle.fontSize;
            bool clicked = GUI.Button(buttonRect, "Settings", entryButtonStyle);
            GUI.enabled = previousEnabled;
            GUI.color = previousColor;

            if (alpha > 0.15f)
            {
                RegisterScreenTouch(buttonRect, OpenSettings);
            }

            if (clicked && !Application.isMobilePlatform)
            {
                OpenSettings();
            }
        }

        private void DrawSettingsPage()
        {
            if (currentPage == SettingsPage.OrientCapture)
            {
                DrawOrientCapturePage();
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
            GUI.color = previousColor;

            activeUiScale = GetMobileUiScale();

            Rect safeArea = Screen.safeArea;
            float safeX = safeArea.xMin / activeUiScale;
            float safeY = (Screen.height - safeArea.yMax) / activeUiScale;
            float logicalWidth = safeArea.width / activeUiScale;
            float logicalHeight = safeArea.height / activeUiScale;
            float panelWidth = Mathf.Min(540f, logicalWidth - 24f);
            float panelHeight = Mathf.Min(780f, logicalHeight - 24f);
            activeAreaRect = new Rect(
                safeX + (logicalWidth - panelWidth) * 0.5f,
                safeY + (logicalHeight - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(activeUiScale, activeUiScale, 1f));
            GUILayout.BeginArea(activeAreaRect, GUI.skin.box);
            activeTouchViewportRect = new Rect(
                activeAreaRect.x * activeUiScale,
                activeAreaRect.y * activeUiScale,
                activeAreaRect.width * activeUiScale,
                activeAreaRect.height * activeUiScale);
            settingsScrollPosition = GUILayout.BeginScrollView(
                settingsScrollPosition,
                false,
                false);

            switch (currentPage)
            {
                case SettingsPage.Settings:
                    DrawSettingsHome();
                    break;
                case SettingsPage.Camera:
                    DrawCameraPage();
                    break;
                case SettingsPage.Viewpoint:
                    DrawViewpointPage();
                    break;
                case SettingsPage.Background:
                    DrawBackgroundPage();
                    break;
                case SettingsPage.Display:
                    DrawDisplayPage();
                    break;
                case SettingsPage.EarthStyles:
                    DrawEarthStylesPage();
                    break;
                case SettingsPage.Weather:
                    DrawWeatherPage();
                    break;
                case SettingsPage.Orient:
                    DrawOrientPage();
                    break;
                case SettingsPage.Privacy:
                    DrawPrivacyPage();
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void DrawSettingsHome()
        {
            GUILayout.Label("Settings", titleStyle);
            GUILayout.Space(8f);
            DrawButton("Back to Viewpoint", BackToViewpoint, 46f);
            GUILayout.Space(18f);
            GUILayout.Label("Choose a settings category", bodyStyle);
            GUILayout.Space(10f);
            DrawButton("Camera", delegate { ShowPage(SettingsPage.Camera); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Viewpoint", delegate { ShowPage(SettingsPage.Viewpoint); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Background", delegate { ShowPage(SettingsPage.Background); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Display", delegate { ShowPage(SettingsPage.Display); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Earth Styles", delegate { ShowPage(SettingsPage.EarthStyles); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Weather", delegate { ShowPage(SettingsPage.Weather); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Orient", delegate { ShowPage(SettingsPage.Orient); }, 58f);
            GUILayout.Space(8f);
            DrawButton("Privacy", delegate { ShowPage(SettingsPage.Privacy); }, 58f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Additional settings categories can be added without changing this navigation pattern.", bodyStyle);
        }

        private void DrawCameraPage()
        {
            DrawCategoryHeader("Camera");
            GUILayout.Space(18f);
            GUILayout.Label("Camera feed", headingStyle);
            GUILayout.Label("Controls the rear-camera background behind the Earth overlay.", bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Camera enabled",
                GlassGlobeSettingsState.CameraFeedEnabled,
                delegate { SetCameraFeedEnabled(!GlassGlobeSettingsState.CameraFeedEnabled); });
            GUILayout.Space(12f);
            string feedStatus = cameraFeed != null ? cameraFeed.FeedStatus : "Camera component not found";
            GUILayout.Label("Status: " + feedStatus, statusStyle);
        }

        private void DrawBackgroundPage()
        {
            DrawCategoryHeader("Background");
            GUILayout.Space(18f);
            GUILayout.Label("Sky", headingStyle);
            GUILayout.Label(
                "Shows the Milky Way where it actually is right now for your viewpoint, seen through and around the Earth. Hidden while the camera feed covers the background.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Milky Way Galaxy",
                GlassGlobeSettingsState.MilkyWayEnabled,
                delegate { SetMilkyWayEnabled(!GlassGlobeSettingsState.MilkyWayEnabled); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Sun",
                GlassGlobeSettingsState.SunEnabled,
                delegate { SetSunEnabled(!GlassGlobeSettingsState.SunEnabled); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Moon",
                GlassGlobeSettingsState.MoonEnabled,
                delegate { SetMoonEnabled(!GlassGlobeSettingsState.MoonEnabled); });
            GUILayout.Space(12f);
            string milkyWayStatus = milkyWay != null ? milkyWay.StatusText : "Milky Way component not found";
            GUILayout.Label("Milky Way: " + milkyWayStatus, statusStyle);
            string sunStatus = sunMoon != null ? sunMoon.SunStatus : "Sun component not found";
            GUILayout.Label("Sun: " + sunStatus, statusStyle);
            string moonStatus = sunMoon != null ? sunMoon.MoonStatus : "Moon component not found";
            GUILayout.Label("Moon: " + moonStatus, statusStyle);
        }

        private void DrawDisplayPage()
        {
            DrawCategoryHeader("Display");
            GUILayout.Space(18f);
            GUILayout.Label("On-screen information", headingStyle);
            GUILayout.Space(8f);
            DrawCheckbox(
                "Main information and controls",
                GlassGlobeSettingsState.MainHudVisible,
                delegate { GlassGlobeSettingsState.SetMainHudVisible(!GlassGlobeSettingsState.MainHudVisible); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Country name",
                GlassGlobeSettingsState.CountryBannerVisible,
                delegate { GlassGlobeSettingsState.SetCountryBannerVisible(!GlassGlobeSettingsState.CountryBannerVisible); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Country name at top",
                GlassGlobeSettingsState.CountryBannerAtTop,
                delegate { GlassGlobeSettingsState.SetCountryBannerAtTop(!GlassGlobeSettingsState.CountryBannerAtTop); });
            GUILayout.Label("Uncheck to place the country name at the bottom.", bodyStyle);

            GUILayout.Space(18f);
            GUILayout.Label("Country outline color", headingStyle);
            DrawColorChoices(true);
            DrawThicknessRow(
                "Country line thickness",
                GlassGlobeSettingsState.CountryOutlineThickness,
                delegate (float value) { GlassGlobeSettingsState.SetCountryOutlineThickness(value); RefreshDisplaySettings(); });
            GUILayout.Space(18f);
            DrawCheckbox(
                "Grid",
                GlassGlobeSettingsState.GridVisible,
                delegate { GlassGlobeSettingsState.SetGridVisible(!GlassGlobeSettingsState.GridVisible); RefreshDisplaySettings(); });
            GUILayout.Space(8f);
            GUILayout.Label("Grid color", headingStyle);
            DrawColorChoices(false);
            DrawThicknessRow(
                "Grid thickness",
                GlassGlobeSettingsState.GridThickness,
                delegate (float value) { GlassGlobeSettingsState.SetGridThickness(value); RefreshDisplaySettings(); });
        }

        private void DrawThicknessRow(string label, float value, Action<float> setter)
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + Mathf.RoundToInt(value * 100f) + "%", bodyStyle, GUILayout.Width(260f));
            DrawButton("-", delegate { setter(value - 0.25f); }, 46f);
            GUILayout.Space(6f);
            DrawButton("+", delegate { setter(value + 0.25f); }, 46f);
            GUILayout.EndHorizontal();
        }

        private void RefreshDisplaySettings()
        {
            appliedDisplaySignature = string.Empty;
            ApplyDisplaySettings();
        }

        private void DrawColorChoices(bool countryOutlines)
        {
            GUILayout.BeginHorizontal();
            DrawColorChoice("Gold", new Color(1f, 0.82f, 0.22f, 1f), countryOutlines);
            GUILayout.Space(5f);
            DrawColorChoice("Cyan", new Color(0.15f, 0.88f, 1f, 0.95f), countryOutlines);
            GUILayout.Space(5f);
            DrawColorChoice("White", Color.white, countryOutlines);
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            DrawColorChoice("Red", new Color(1f, 0.25f, 0.2f, 1f), countryOutlines);
            GUILayout.Space(5f);
            DrawColorChoice("Green", new Color(0.25f, 1f, 0.4f, 1f), countryOutlines);
            GUILayout.Space(5f);
            DrawColorChoice("Violet", new Color(0.72f, 0.42f, 1f, 1f), countryOutlines);
            GUILayout.EndHorizontal();
        }

        private void DrawColorChoice(string name, Color color, bool countryOutlines)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = color;
            DrawButton(name, delegate
            {
                if (countryOutlines)
                {
                    GlassGlobeSettingsState.SetCountryOutlineColor(color);
                }
                else
                {
                    GlassGlobeSettingsState.SetGridColor(color);
                }

                appliedDisplaySignature = string.Empty;
                ApplyDisplaySettings();
            }, 46f);
            GUI.backgroundColor = previousBackground;
        }

        private void DrawEarthStylesPage()
        {
            DrawCategoryHeader("Earth Styles");
            GUILayout.Space(18f);
            GUILayout.Label("Globe appearance", headingStyle);
            GUILayout.Label(
                "Night Lights paints real city lights from NASA's Black Marble map onto the glass Earth. Rim Glow adds a soft halo around the Earth's edge so it reads as a glass sphere.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Night Lights (city lights)",
                GlassGlobeSettingsState.NightLightsEnabled,
                delegate { SetNightLightsEnabled(!GlassGlobeSettingsState.NightLightsEnabled); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Rim Glow (glass edge)",
                GlassGlobeSettingsState.RimGlowEnabled,
                delegate { SetRimGlowEnabled(!GlassGlobeSettingsState.RimGlowEnabled); });
            GUILayout.Space(12f);
            string nightStatus = earthStyle != null ? earthStyle.NightLightsStatus : "Earth style component not found";
            GUILayout.Label("Night Lights: " + nightStatus, statusStyle);
            string rimStatus = earthStyle != null ? earthStyle.RimGlowStatus : "Earth style component not found";
            GUILayout.Label("Rim Glow: " + rimStatus, statusStyle);

            GUILayout.Space(16f);
            GUILayout.Label("Dreamy Earth art", headingStyle);
            GUILayout.Label(
                "Stylized shimmering water and soft drifting land colors on the far side, plus art clouds shaped by the live satellite cloud field that creep along so slowly you only catch the motion by staring.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Shimmering water",
                GlassGlobeSettingsState.WaterArtEnabled,
                delegate { SetWaterArtEnabled(!GlassGlobeSettingsState.WaterArtEnabled); });
            DrawOpacityRow(
                "Water opacity",
                GlassGlobeSettingsState.WaterArtOpacity,
                delegate (float value) { GlassGlobeSettingsState.SetWaterArtOpacity(value); MarkEarthArtDirty(); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Dreamy land",
                GlassGlobeSettingsState.LandArtEnabled,
                delegate { SetLandArtEnabled(!GlassGlobeSettingsState.LandArtEnabled); });
            DrawOpacityRow(
                "Land opacity",
                GlassGlobeSettingsState.LandArtOpacity,
                delegate (float value) { GlassGlobeSettingsState.SetLandArtOpacity(value); MarkEarthArtDirty(); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Stylized ocean (glinting water)",
                GlassGlobeSettingsState.OceanArtEnabled,
                delegate { SetOceanArtEnabled(!GlassGlobeSettingsState.OceanArtEnabled); });
            DrawOpacityRow(
                "Ocean opacity",
                GlassGlobeSettingsState.OceanArtOpacity,
                delegate (float value) { GlassGlobeSettingsState.SetOceanArtOpacity(value); MarkEarthArtDirty(); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Art clouds (slow drift)",
                GlassGlobeSettingsState.ArtCloudsEnabled,
                delegate { SetArtCloudsEnabled(!GlassGlobeSettingsState.ArtCloudsEnabled); });
            DrawOpacityRow(
                "Cloud opacity",
                GlassGlobeSettingsState.ArtCloudsOpacity,
                delegate (float value) { GlassGlobeSettingsState.SetArtCloudsOpacity(value); MarkEarthArtDirty(); });
            GUILayout.Space(12f);
            string earthArtStatus = earthArt != null ? earthArt.EarthArtStatus : "Earth art component not found";
            GUILayout.Label("Earth art: " + earthArtStatus, statusStyle);
            string oceanStatus = earthArt != null ? earthArt.OceanStatus : "Earth art component not found";
            GUILayout.Label("Stylized ocean: " + oceanStatus, statusStyle);
            string artCloudsStatus = earthArt != null ? earthArt.ArtCloudsStatus : "Earth art component not found";
            GUILayout.Label("Art clouds: " + artCloudsStatus, statusStyle);
        }

        private void DrawOpacityRow(string label, float value, System.Action<float> setter)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                string.Format("{0}: {1:0}%", label, value * 100f),
                bodyStyle,
                GUILayout.Width(200f));
            float capturedValue = value;
            DrawButton("-", delegate { setter(capturedValue - 0.1f); }, 38f);
            GUILayout.Space(6f);
            DrawButton("+", delegate { setter(capturedValue + 0.1f); }, 38f);
            GUILayout.EndHorizontal();
        }

        private void DrawWeatherPage()
        {
            DrawCategoryHeader("Weather");
            GUILayout.Space(18f);
            GUILayout.Label("Live weather on the Earth", headingStyle);
            GUILayout.Label(
                "Draws current weather on the far side of the glass Earth. Clouds come from live satellite infrared imagery; rain and snow come from ground radar where networks exist. Both refresh about every 10 minutes over the network.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Clouds (satellite)",
                GlassGlobeSettingsState.WeatherCloudsEnabled,
                delegate { SetWeatherCloudsEnabled(!GlassGlobeSettingsState.WeatherCloudsEnabled); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Rain / snow radar",
                GlassGlobeSettingsState.WeatherRadarEnabled,
                delegate { SetWeatherRadarEnabled(!GlassGlobeSettingsState.WeatherRadarEnabled); });
            GUILayout.Space(12f);
            string cloudsStatus = weather != null ? weather.CloudsStatus : "Weather component not found";
            GUILayout.Label("Clouds: " + cloudsStatus, statusStyle);
            string radarStatus = weather != null ? weather.RadarStatus : "Weather component not found";
            GUILayout.Label("Radar: " + radarStatus, statusStyle);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Cloud imagery: NOAA GOES and Himawari via NASA GIBS, plus EUMETSAT Meteosat. Radar: RainViewer.com, personal use.",
                bodyStyle);
        }

        private void DrawOrientPage()
        {
            DrawCategoryHeader("Orient");
            GUILayout.Space(14f);
            GUILayout.Label("Align with the sky", headingStyle);
            GUILayout.Label(
                "You can set north directly, or use the real Sun or Moon for fine alignment.",
                bodyStyle);
            GUILayout.Space(10f);

            bool sensorsActive = poseSensors != null && poseSensors.SensorModeActive;
            if (!sensorsActive)
            {
                GUILayout.Label("Alignment needs the phone's live sensors, so it is available in the device build only.", statusStyle);
            }
            else
            {
                GUILayout.Label("Quick north alignment", headingStyle);
                GUILayout.Label(
                    "Hold the phone upright and aim the center dot toward true north, then tap the button. GlassGlobe will ignore the magnetic compass until you reset it or leave the app. Gyro north can slowly drift, so tap again when needed.",
                    bodyStyle);
                GUILayout.Space(6f);
                DrawButton("My Phone Is Facing North", AlignPhoneToNorth, 50f);
                if (!string.IsNullOrEmpty(orientStatusMessage))
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(orientStatusMessage, statusStyle);
                }
                GUILayout.Space(16f);
                GUILayout.Label("Fine alignment", headingStyle);
                GUILayout.Label(
                    "Point the center dot at the real Sun or Moon and capture. The camera turns on so you can see the sky. Never look directly at the Sun - watch the screen only.",
                    bodyStyle);
                GUILayout.Space(8f);

                float sunAzimuth;
                float sunAltitude;
                Vector3 sunWorld;
                ComputeBodyPosition(AlignBody.Sun, out sunAzimuth, out sunAltitude, out sunWorld);
                float moonAzimuth;
                float moonAltitude;
                Vector3 moonWorld;
                ComputeBodyPosition(AlignBody.Moon, out moonAzimuth, out moonAltitude, out moonWorld);

                DrawButton(
                    sunAltitude >= MaxReliableSkyAlignmentAltitudeDegrees
                        ? "Align to Sun (too close overhead)"
                        : sunAltitude > -1f
                            ? "Align to Sun"
                            : "Align to Sun (below horizon)",
                    delegate { StartAlignment(AlignBody.Sun); },
                    46f);
                GUILayout.Space(8f);
                DrawButton(
                    moonAltitude >= MaxReliableSkyAlignmentAltitudeDegrees
                        ? "Align to Moon (too close overhead)"
                        : moonAltitude > -1f
                            ? "Align to Moon"
                            : "Align to Moon (below horizon)",
                    delegate { StartAlignment(AlignBody.Moon); },
                    46f);
                GUILayout.Space(8f);
                DrawButton("Reset Heading Correction", ResetHeadingOffset, 42f);
                if (!string.IsNullOrEmpty(orientStatusMessage))
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(orientStatusMessage, statusStyle);
                }

                GUILayout.Space(10f);
                GUILayout.Label(
                    string.Format(
                        "{0}\nCurrent manual correction: {1:+0.0;-0.0;0.0} deg\nSun: azimuth {2:0} deg, altitude {3:0} deg\nMoon: azimuth {4:0} deg, altitude {5:0} deg",
                        poseSensors.GyroNorthLockActive
                            ? "Gyro north lock: ON (compass ignored)"
                            : "Gyro north lock: OFF (automatic compass)",
                        poseSensors.ActiveHeadingCorrectionDegrees,
                        sunAzimuth,
                        sunAltitude,
                        moonAzimuth,
                        moonAltitude),
                    bodyStyle);
            }

        }

        private void AlignPhoneToNorth()
        {
            if (poseSensors == null)
            {
                orientStatusMessage = "Sensors unavailable.";
                return;
            }

            float correctionDegrees;
            if (!poseSensors.TryAlignCurrentHeadingToNorth(out correctionDegrees))
            {
                orientStatusMessage = poseSensors.GameRotationAvailable
                    ? "Wait for the gyro-only orientation sensor, hold the phone upright toward north, then try again."
                    : "This phone's gyro-only orientation sensor is unavailable, so metal-resistant north lock cannot start.";
                return;
            }

            orientStatusMessage =
                "North locked with the magnetic compass ignored. Applied " +
                correctionDegrees.ToString("+0.0;-0.0;0.0") +
                " deg; set it again after leaving or reopening GlassGlobe.";
        }

        private void StartAlignment(AlignBody body)
        {
            if (GlassGlobeSettingsState.ViewpointOverrideEnabled)
            {
                orientStatusMessage =
                    "Sun and Moon alignment needs the sky at your real GPS location. Return to real GPS location first.";
                return;
            }

            if (poseSensors == null || !poseSensors.HasLocationFix)
            {
                orientStatusMessage =
                    "Wait for a real GPS fix before aligning to the Sun or Moon.";
                return;
            }

            float azimuth;
            float altitude;
            Vector3 world;
            ComputeBodyPosition(body, out azimuth, out altitude, out world);
            if (altitude <= -1f)
            {
                orientStatusMessage = "The " + BodyName(body) + " is below the horizon right now. Try the other body.";
                return;
            }

            if (altitude >= MaxReliableSkyAlignmentAltitudeDegrees)
            {
                orientStatusMessage =
                    "The " + BodyName(body) + " is too close to overhead for reliable heading alignment. Use Set North or try again later.";
                return;
            }

            if (!GlassGlobeSettingsState.CameraFeedEnabled)
            {
                SetCameraFeedEnabled(true);
            }

            alignTarget = body;
            orientStatusMessage = string.Empty;
            currentPage = SettingsPage.OrientCapture;
        }

        private void DrawOrientCapturePage()
        {
            float uiScale = GetMobileUiScale();
            Camera sceneCamera = ResolveSceneCamera();
            float azimuthTarget;
            float altitudeTarget;
            Vector3 targetWorld;
            ComputeBodyPosition(alignTarget, out azimuthTarget, out altitudeTarget, out targetWorld);

            bool hasGyroHeading = poseSensors != null && poseSensors.GyroNorthLockActive;
            if (hasGyroHeading && sceneCamera != null)
            {
                Vector3 screenPoint = sceneCamera.WorldToScreenPoint(targetWorld);
                if (screenPoint.z > 0f)
                {
                    float ringSize = Screen.height * 0.075f;
                    Rect ringRect = new Rect(
                        screenPoint.x - ringSize * 0.5f,
                        Screen.height - screenPoint.y - ringSize * 0.5f,
                        ringSize,
                        ringSize);
                    GUI.DrawTexture(ringRect, EnsureAlignRingTexture());
                }
            }

            float azimuthForward;
            float altitudeForward;
            ComputeCameraPointing(out azimuthForward, out altitudeForward);
            float azimuthDelta = Mathf.DeltaAngle(azimuthForward, azimuthTarget);
            float altitudeDelta = altitudeTarget - altitudeForward;

            string turnText = azimuthDelta >= 0f
                ? string.Format("turn right {0:0} deg", azimuthDelta)
                : string.Format("turn left {0:0} deg", -azimuthDelta);
            string tiltText = altitudeDelta >= 0f
                ? string.Format("tilt up {0:0} deg", altitudeDelta)
                : string.Format("tilt down {0:0} deg", -altitudeDelta);
            string guidanceText = hasGyroHeading
                ? string.Format("Guidance: {0}, {1}.", turnText, tiltText)
                : string.Format(
                    "{0}. Use the camera image; direction guidance stays off until Capture.",
                    tiltText);

            captureTextStyle.fontSize = Mathf.RoundToInt(15f * uiScale);
            Rect safeArea = Screen.safeArea;
            Color previousColor = GUI.color;
            float boxHeight = Screen.height * 0.16f;
            Rect instructionRect = new Rect(16f, Screen.height - safeArea.yMax + 16f, Screen.width - 32f, boxHeight);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Box(instructionRect, GUIContent.none);
            GUI.color = previousColor;

            string bodyName = BodyName(alignTarget);
            string warning = alignTarget == AlignBody.Sun
                ? "Never look directly at the Sun - watch the screen only.\n"
                : string.Empty;
            GUI.Label(
                new Rect(instructionRect.x + 12f, instructionRect.y + 10f, instructionRect.width - 24f, instructionRect.height - 20f),
                string.Format(
                    "Center the dot on the real {0}, then tap Capture.\n{1}{2}",
                    bodyName,
                    warning,
                    guidanceText),
                captureTextStyle);

            if (!string.IsNullOrEmpty(orientStatusMessage))
            {
                Rect statusRect = new Rect(16f, instructionRect.yMax + 8f, Screen.width - 32f, Screen.height * 0.08f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.Box(statusRect, GUIContent.none);
                GUI.color = previousColor;
                GUI.Label(
                    new Rect(statusRect.x + 12f, statusRect.y + 8f, statusRect.width - 24f, statusRect.height - 16f),
                    orientStatusMessage,
                    captureTextStyle);
            }

            float buttonHeight = Mathf.Clamp(Screen.height * 0.065f, 48f, 110f);
            float buttonWidth = Screen.width * 0.42f;
            float safeBottom = Screen.height - safeArea.yMin;
            float buttonY = safeBottom - buttonHeight - 24f;
            Rect captureRect = new Rect(Screen.width * 0.5f - buttonWidth - 8f, buttonY, buttonWidth, buttonHeight);
            Rect cancelRect = new Rect(Screen.width * 0.5f + 8f, buttonY, buttonWidth, buttonHeight);

            entryButtonStyle.fontSize = Mathf.RoundToInt(15f * uiScale);
            bool captureClicked = GUI.Button(captureRect, "Capture", entryButtonStyle);
            RegisterScreenTouch(captureRect, CaptureAlignment);
            if (captureClicked && !Application.isMobilePlatform)
            {
                CaptureAlignment();
            }

            bool cancelClicked = GUI.Button(cancelRect, "Cancel", entryButtonStyle);
            RegisterScreenTouch(cancelRect, CancelAlignment);
            if (cancelClicked && !Application.isMobilePlatform)
            {
                CancelAlignment();
            }
        }

        private void CaptureAlignment()
        {
            if (poseSensors == null)
            {
                orientStatusMessage = "Sensors unavailable.";
                return;
            }

            float azimuthTarget;
            float altitudeTarget;
            Vector3 targetWorld;
            ComputeBodyPosition(alignTarget, out azimuthTarget, out altitudeTarget, out targetWorld);
            if (altitudeTarget <= -1f)
            {
                orientStatusMessage = "The " + BodyName(alignTarget) + " is below the horizon; cannot align.";
                return;
            }

            if (altitudeTarget >= MaxReliableSkyAlignmentAltitudeDegrees)
            {
                orientStatusMessage =
                    "The " + BodyName(alignTarget) + " is too close to overhead for reliable heading alignment.";
                return;
            }

            float azimuthCorrection;
            float altitudeDelta;
            if (!poseSensors.TryAlignCurrentViewToSkyTarget(
                    azimuthTarget,
                    altitudeTarget,
                    6f,
                    out azimuthCorrection,
                    out altitudeDelta))
            {
                if (!float.IsNaN(altitudeDelta))
                {
                    orientStatusMessage = string.Format(
                        "That does not look like the {0}: tilt is off by {1:0} deg. Center the dot on the {0} and tap Capture again.",
                        BodyName(alignTarget),
                        Mathf.Abs(altitudeDelta));
                }
                else
                {
                    orientStatusMessage = poseSensors.GameRotationAvailable
                        ? "Wait for the gyro-only orientation sensor, center the dot on the " + BodyName(alignTarget) + ", then try again."
                        : "This phone's gyro-only orientation sensor is unavailable, so metal-resistant sky alignment cannot start.";
                }

                return;
            }

            orientStatusMessage = string.Format(
                "Aligned to the {0}. Gyro north locked with the compass ignored; corrected by {1:+0.0;-0.0;0.0} deg.",
                BodyName(alignTarget),
                azimuthCorrection);
            currentPage = SettingsPage.Orient;
        }

        private void CancelAlignment()
        {
            currentPage = SettingsPage.Orient;
        }

        private void ResetHeadingOffset()
        {
            if (poseSensors != null)
            {
                poseSensors.ResetHeadingCorrection();
                orientStatusMessage = "Manual heading correction reset; automatic compass alignment restored.";
            }
        }

        private static string BodyName(AlignBody body)
        {
            return body == AlignBody.Sun ? "Sun" : "Moon";
        }

        private void ComputeBodyPosition(AlignBody body, out float azimuthDegrees, out float altitudeDegrees, out Vector3 worldPosition)
        {
            GeoCoordinate coordinate = ResolveObserverCoordinate();
            EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(coordinate);
            float lst = SkyMath.ComputeLocalSiderealDegrees(coordinate.Longitude);
            Vector3 equatorial = body == AlignBody.Sun
                ? SkyMath.SunEquatorialDirection()
                : SkyMath.MoonTopocentricEquatorialDirection(lst, coordinate.Latitude);
            Vector3 enu = SkyMath.EquatorialToEnu(equatorial, lst, coordinate.Latitude);
            SkyMath.EnuToAzimuthAltitude(enu, out azimuthDegrees, out altitudeDegrees);

            Vector3 worldDirection = SkyMath.EquatorialToWorld(equatorial, lst, coordinate.Latitude, frame);
            Camera sceneCamera = ResolveSceneCamera();
            Vector3 origin = sceneCamera != null ? sceneCamera.transform.position : Vector3.zero;
            worldPosition = origin + worldDirection * 85f;
        }

        private void ComputeCameraPointing(out float azimuthDegrees, out float altitudeDegrees)
        {
            azimuthDegrees = 0f;
            altitudeDegrees = 0f;
            Camera sceneCamera = ResolveSceneCamera();
            if (sceneCamera == null)
            {
                return;
            }

            GeoCoordinate coordinate = ResolveObserverCoordinate();
            EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(coordinate);
            Vector3 forward = sceneCamera.transform.forward;
            Vector3 enu = new Vector3(
                Vector3.Dot(forward, frame.East),
                Vector3.Dot(forward, frame.North),
                Vector3.Dot(forward, frame.Up));
            SkyMath.EnuToAzimuthAltitude(enu.normalized, out azimuthDegrees, out altitudeDegrees);
        }

        private GeoCoordinate ResolveObserverCoordinate()
        {
            if (poseSensors != null && poseSensors.SensorModeActive)
            {
                return poseSensors.CurrentCoordinate;
            }

            if (phonePose != null)
            {
                return phonePose.userCoordinate;
            }

            return new GeoCoordinate(0f, 0f);
        }

        private Camera ResolveSceneCamera()
        {
            if (poseSensors != null && poseSensors.targetCamera != null)
            {
                return poseSensors.targetCamera;
            }

            return Camera.main;
        }

        private Texture2D EnsureAlignRingTexture()
        {
            if (alignRingTexture != null)
            {
                return alignRingTexture;
            }

            int size = 96;
            alignRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            alignRingTexture.wrapMode = TextureWrapMode.Clamp;
            float half = (size - 1) * 0.5f;
            Color ringColor = new Color(1f, 0.8f, 0.2f, 0.95f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    bool onRing = distance > 0.72f && distance < 0.95f;
                    alignRingTexture.SetPixel(x, y, onRing ? ringColor : Color.clear);
                }
            }

            alignRingTexture.Apply();
            return alignRingTexture;
        }

        private void DrawPrivacyPage()
        {
            DrawCategoryHeader("Privacy");
            GUILayout.Space(14f);
            GUILayout.Label("Sharing controls", headingStyle);
            GUILayout.Label("These settings hide information on screen. The coordinates can still be used internally to position the Earth.", bodyStyle);
            GUILayout.Space(10f);

            DrawCheckbox(
                "Privacy mode (hide all location readouts)",
                GlassGlobeSettingsState.PrivacyModeEnabled,
                delegate { GlassGlobeSettingsState.SetPrivacyMode(!GlassGlobeSettingsState.PrivacyModeEnabled); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Hide current coordinates",
                GlassGlobeSettingsState.HideUserCoordinates,
                delegate { GlassGlobeSettingsState.SetHideUserCoordinates(!GlassGlobeSettingsState.HideUserCoordinates); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Hide far-side coordinates",
                GlassGlobeSettingsState.HideFarSideCoordinates,
                delegate { GlassGlobeSettingsState.SetHideFarSideCoordinates(!GlassGlobeSettingsState.HideFarSideCoordinates); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Hide location accuracy",
                GlassGlobeSettingsState.HideLocationAccuracy,
                delegate { GlassGlobeSettingsState.SetHideLocationAccuracy(!GlassGlobeSettingsState.HideLocationAccuracy); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Hide country / region readout",
                GlassGlobeSettingsState.HideViewedRegion,
                delegate { GlassGlobeSettingsState.SetHideViewedRegion(!GlassGlobeSettingsState.HideViewedRegion); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Show viewed-from name",
                GlassGlobeSettingsState.ShowViewedFromName,
                delegate { GlassGlobeSettingsState.SetShowViewedFromName(!GlassGlobeSettingsState.ShowViewedFromName); });
        }

        private void DrawViewpointPage()
        {
            DrawCategoryHeader("Viewpoint");
            GUILayout.Space(10f);
            GUILayout.Label("Current: " + GlassGlobeSettingsState.ViewedFromLabel, headingStyle);
            GUILayout.Label(
                GlassGlobeSettingsState.ViewpointOverrideEnabled
                    ? "The selected viewpoint replaces GPS position while phone orientation continues to drive the view."
                    : "GlassGlobe is using the device GPS position.",
                bodyStyle);
            GUILayout.Space(8f);

            if (GlassGlobeSettingsState.ViewpointOverrideEnabled)
            {
                DrawButton("Use Real GPS Location", UseRealLocation, 42f);
                GUILayout.Space(8f);
            }

            DrawCheckbox(
                "Show country names on Earth",
                GlassGlobeSettingsState.CountryLabelsVisible,
                delegate { SetCountryLabelsVisible(!GlassGlobeSettingsState.CountryLabelsVisible); });

            GUILayout.Space(12f);
            GUILayout.Label("View from a city or country", headingStyle);
            GUILayout.Label("Search the included city presets and all countries in the bundled Natural Earth data.", bodyStyle);
            viewpointSearch = GUILayout.TextField(
                viewpointSearch ?? string.Empty,
                GUILayout.Height(GetInteractiveControlHeight(38f)));
            BuildFilteredChoices();

            int visibleChoiceCount = Mathf.Min(6, filteredChoices.Count);
            for (int index = 0; index < visibleChoiceCount; index++)
            {
                ViewpointChoice choice = filteredChoices[index];
                ViewpointChoice capturedChoice = choice;
                DrawButton(choice.Kind + ": " + choice.Name, delegate { SelectViewpoint(capturedChoice); }, 38f);
                GUILayout.Space(3f);
            }

            if (visibleChoiceCount == 0)
            {
                GUILayout.Label("No included city or country matches that search.", statusStyle);
            }

            GUILayout.Space(10f);
            GUILayout.Label("View from coordinates", headingStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Lat", bodyStyle, GUILayout.Width(32f));
            latitudeText = GUILayout.TextField(
                latitudeText ?? string.Empty,
                GUILayout.Height(GetInteractiveControlHeight(36f)));
            GUILayout.Space(8f);
            GUILayout.Label("Lon", bodyStyle, GUILayout.Width(32f));
            longitudeText = GUILayout.TextField(
                longitudeText ?? string.Empty,
                GUILayout.Height(GetInteractiveControlHeight(36f)));
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
            GUILayout.Label("Name shown in the HUD (optional)", bodyStyle);
            customViewpointName = GUILayout.TextField(
                customViewpointName ?? string.Empty,
                GUILayout.Height(GetInteractiveControlHeight(36f)));
            GUILayout.Space(5f);
            DrawButton("View From These Coordinates", ApplyManualViewpoint, 42f);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Space(8f);
                GUILayout.Label(statusMessage, statusStyle);
            }
        }

        private void DrawCategoryHeader(string title)
        {
            GUILayout.Label(title, titleStyle);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            DrawButton("Back to Viewpoint", BackToViewpoint, 44f);
            GUILayout.Space(8f);
            DrawButton("Back to Settings", delegate { ShowPage(SettingsPage.Settings); }, 44f);
            GUILayout.EndHorizontal();
        }

        private void DrawCheckbox(string label, bool value, Action action)
        {
            DrawButton((value ? "[x] " : "[ ] ") + label, action, 42f);
        }

        private void DrawButton(string text, Action action, float height)
        {
            bool clicked = GUILayout.Button(
                text,
                buttonStyle,
                GUILayout.Height(GetInteractiveControlHeight(height)));
            Rect localRect = GUILayoutUtility.GetLastRect();
            RegisterLocalTouch(localRect, action);
            if (clicked && !Application.isMobilePlatform)
            {
                action();
            }
        }

        private void RegisterLocalTouch(Rect localRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect screenRect = new Rect(
                (activeAreaRect.x + localRect.x - settingsScrollPosition.x) * activeUiScale,
                (activeAreaRect.y + localRect.y - settingsScrollPosition.y) * activeUiScale,
                localRect.width * activeUiScale,
                localRect.height * activeUiScale);
            Rect clippedRect = IntersectRects(screenRect, activeTouchViewportRect);
            if (clippedRect.width > 0f && clippedRect.height > 0f)
            {
                RegisterScreenTouch(clippedRect, action);
            }
        }

        private void RegisterScreenTouch(Rect screenRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            touchTargets.Add(new TouchTarget(screenRect, action));
        }

        private void HandleMobileTouch()
        {
            if (!Application.isMobilePlatform || Input.touchCount == 0)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);
            Vector2 screenPoint = new Vector2(touch.position.x, Screen.height - touch.position.y);
            if (touch.phase == TouchPhase.Began)
            {
                trackedTouchFingerId = touch.fingerId;
                touchStartScreenPoint = screenPoint;
                touchDragged = false;
                scrollTouchActive = currentPage != SettingsPage.Closed &&
                    activeTouchViewportRect.Contains(screenPoint);
                return;
            }

            if (touch.fingerId != trackedTouchFingerId)
            {
                return;
            }

            float dragThreshold = 18f * Mathf.Max(1f, activeUiScale);
            if ((screenPoint - touchStartScreenPoint).sqrMagnitude > dragThreshold * dragThreshold)
            {
                touchDragged = true;
            }

            if (touch.phase == TouchPhase.Moved && scrollTouchActive)
            {
                settingsScrollPosition.y = Mathf.Max(
                    0f,
                    settingsScrollPosition.y + touch.deltaPosition.y / Mathf.Max(1f, activeUiScale));
                lastInteractionTime = Time.unscaledTime;
                return;
            }

            if (touch.phase == TouchPhase.Canceled)
            {
                ResetTrackedTouch();
                return;
            }

            if (touch.phase != TouchPhase.Ended)
            {
                return;
            }

            if (touchDragged)
            {
                ResetTrackedTouch();
                return;
            }

            for (int index = touchTargets.Count - 1; index >= 0; index--)
            {
                TouchTarget target = touchTargets[index];
                if (!target.ScreenRect.Contains(touchStartScreenPoint) ||
                    !target.ScreenRect.Contains(screenPoint))
                {
                    continue;
                }

                target.Action();
                lastInteractionTime = Time.unscaledTime;
                ResetTrackedTouch();
                return;
            }

            ResetTrackedTouch();
        }

        private void TrackInteraction()
        {
            bool interacted = Input.touchCount > 0 || Input.anyKeyDown;
            Vector3 currentMousePosition = Input.mousePosition;
            if (!hasLastMousePosition)
            {
                hasLastMousePosition = true;
                lastMousePosition = currentMousePosition;
            }
            else if ((currentMousePosition - lastMousePosition).sqrMagnitude > 0.25f)
            {
                interacted = true;
                lastMousePosition = currentMousePosition;
            }

            if (interacted)
            {
                lastInteractionTime = Time.unscaledTime;
            }
        }

        private void OpenSettings()
        {
            ResolveReferences();
            if (hud != null)
            {
                hudWasVisible = hud.showHud;
                hud.showHud = false;
            }

            ShowPage(SettingsPage.Settings);
            statusMessage = string.Empty;
            lastInteractionTime = Time.unscaledTime;
            ApplySavedSettings();
            if (phonePose != null)
            {
                phonePose.DragInputBlocked = true;
            }
        }

        private void BackToViewpoint()
        {
            currentPage = SettingsPage.Closed;
            if (hud != null)
            {
                hud.showHud = hudWasVisible;
            }

            if (phonePose != null)
            {
                phonePose.DragInputBlocked = false;
            }

            lastInteractionTime = Time.unscaledTime;
        }

        private void SetCameraFeedEnabled(bool value)
        {
            GlassGlobeSettingsState.SetCameraFeedEnabled(value);
            cameraSettingApplied = false;
            ApplyCameraSetting();
        }

        private void SetCountryLabelsVisible(bool value)
        {
            GlassGlobeSettingsState.SetCountryLabelsVisible(value);
            labelsSettingApplied = false;
            ApplyCountryLabelSetting();
        }

        private void SetMilkyWayEnabled(bool value)
        {
            GlassGlobeSettingsState.SetMilkyWayEnabled(value);
            milkyWaySettingApplied = false;
            ApplyMilkyWaySetting();
        }

        private void SetSunEnabled(bool value)
        {
            GlassGlobeSettingsState.SetSunEnabled(value);
            sunSettingApplied = false;
            ApplySunMoonSettings();
        }

        private void SetMoonEnabled(bool value)
        {
            GlassGlobeSettingsState.SetMoonEnabled(value);
            moonSettingApplied = false;
            ApplySunMoonSettings();
        }

        private void SetNightLightsEnabled(bool value)
        {
            GlassGlobeSettingsState.SetNightLightsEnabled(value);
            nightLightsSettingApplied = false;
            ApplyEarthStyleSettings();
        }

        private void SetRimGlowEnabled(bool value)
        {
            GlassGlobeSettingsState.SetRimGlowEnabled(value);
            rimGlowSettingApplied = false;
            ApplyEarthStyleSettings();
        }

        private void SetWaterArtEnabled(bool value)
        {
            GlassGlobeSettingsState.SetWaterArtEnabled(value);
            MarkEarthArtDirty();
        }

        private void SetLandArtEnabled(bool value)
        {
            GlassGlobeSettingsState.SetLandArtEnabled(value);
            MarkEarthArtDirty();
        }

        private void SetOceanArtEnabled(bool value)
        {
            GlassGlobeSettingsState.SetOceanArtEnabled(value);
            MarkEarthArtDirty();
        }

        private void SetArtCloudsEnabled(bool value)
        {
            GlassGlobeSettingsState.SetArtCloudsEnabled(value);
            MarkEarthArtDirty();
        }

        private void MarkEarthArtDirty()
        {
            appliedEarthArtSignature = null;
            ApplyEarthArtSettings();
        }

        private void SetWeatherCloudsEnabled(bool value)
        {
            GlassGlobeSettingsState.SetWeatherCloudsEnabled(value);
            weatherCloudsSettingApplied = false;
            ApplyWeatherSettings();
        }

        private void SetWeatherRadarEnabled(bool value)
        {
            GlassGlobeSettingsState.SetWeatherRadarEnabled(value);
            weatherRadarSettingApplied = false;
            ApplyWeatherSettings();
        }

        private void SelectViewpoint(ViewpointChoice choice)
        {
            GlassGlobeSettingsState.SetViewpoint(choice.Coordinate, choice.Name);
            latitudeText = choice.Coordinate.Latitude.ToString("0.0000", CultureInfo.InvariantCulture);
            longitudeText = choice.Coordinate.Longitude.ToString("0.0000", CultureInfo.InvariantCulture);
            customViewpointName = choice.Name;
            statusMessage = "Viewpoint changed to " + choice.Name + ".";
            ApplyViewpointSetting(true);
        }

        private void ApplyManualViewpoint()
        {
            float latitude;
            float longitude;
            if (!TryParseFloat(latitudeText, out latitude) ||
                !TryParseFloat(longitudeText, out longitude) ||
                float.IsNaN(latitude) ||
                float.IsInfinity(latitude) ||
                float.IsNaN(longitude) ||
                float.IsInfinity(longitude))
            {
                statusMessage = "Enter valid latitude and longitude numbers.";
                return;
            }

            if (latitude < -90f || latitude > 90f || longitude < -180f || longitude > 180f)
            {
                statusMessage = "Latitude must be -90 to 90 and longitude must be -180 to 180.";
                return;
            }

            GeoCoordinate coordinate = new GeoCoordinate(latitude, longitude);
            string label = string.IsNullOrWhiteSpace(customViewpointName)
                ? "Custom viewpoint"
                : customViewpointName.Trim();
            GlassGlobeSettingsState.SetViewpoint(coordinate, label);
            statusMessage = "Viewpoint changed to " + label + ".";
            ApplyViewpointSetting(true);
        }

        private void UseRealLocation()
        {
            GlassGlobeSettingsState.UseRealLocation();
            statusMessage = "Using the device GPS location.";
            ApplyViewpointSetting(true);
        }

        private static bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ShowPage(SettingsPage page)
        {
            currentPage = page;
            settingsScrollPosition = Vector2.zero;
        }

        private void ResetTrackedTouch()
        {
            trackedTouchFingerId = -1;
            touchDragged = false;
            scrollTouchActive = false;
        }

        private static Rect IntersectRects(Rect left, Rect right)
        {
            float xMin = Mathf.Max(left.xMin, right.xMin);
            float yMin = Mathf.Max(left.yMin, right.yMin);
            float xMax = Mathf.Min(left.xMax, right.xMax);
            float yMax = Mathf.Min(left.yMax, right.yMax);
            return xMax > xMin && yMax > yMin
                ? Rect.MinMaxRect(xMin, yMin, xMax, yMax)
                : Rect.zero;
        }

        private static float GetMobileUiScale()
        {
            if (!Application.isMobilePlatform)
            {
                return 1f;
            }

            if (Screen.dpi > 0f)
            {
                return Mathf.Clamp(Screen.dpi / 160f, 1f, 4f);
            }

            float shortestSide = Mathf.Min(Screen.width, Screen.height);
            return Mathf.Clamp(shortestSide / 360f, 1f, 4f);
        }

        private static float GetInteractiveControlHeight(float requestedHeight)
        {
            return Application.isMobilePlatform
                ? Mathf.Max(48f, requestedHeight)
                : requestedHeight;
        }

        private void ApplySavedSettings()
        {
            GlassGlobeSettingsState.Load();
            ResolveReferences();
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
            ApplyDisplaySettings();
            ApplyMilkyWaySetting();
            ApplySunMoonSettings();
            ApplyEarthStyleSettings();
            ApplyEarthArtSettings();
            ApplyWeatherSettings();
            ApplyViewpointSetting(true);
        }

        private void ApplySettingsIfChanged()
        {
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
            ApplyDisplaySettings();
            ApplyMilkyWaySetting();
            ApplySunMoonSettings();
            ApplyEarthStyleSettings();
            ApplyEarthArtSettings();
            ApplyWeatherSettings();
            ApplyViewpointSetting(false);
        }

        private void ApplyCameraSetting()
        {
            if (cameraFeed == null)
            {
                return;
            }

            bool desired = GlassGlobeSettingsState.CameraFeedEnabled;
            if (cameraSettingApplied && appliedCameraSetting == desired)
            {
                return;
            }

            cameraFeed.SetFeedWanted(desired);
            appliedCameraSetting = desired;
            cameraSettingApplied = true;
        }

        private void ApplyCountryLabelSetting()
        {
            if (labelController == null)
            {
                return;
            }

            bool desired = GlassGlobeSettingsState.CountryLabelsVisible;
            if (labelsSettingApplied && appliedLabelsSetting == desired)
            {
                return;
            }

            labelController.showCountryLabels = desired;
            labelController.RebuildLabels();
            appliedLabelsSetting = desired;
            labelsSettingApplied = true;
        }

        private void ApplyDisplaySettings()
        {
            string signature = ColorUtility.ToHtmlStringRGBA(GlassGlobeSettingsState.CountryOutlineColor) + ":" +
                ColorUtility.ToHtmlStringRGBA(GlassGlobeSettingsState.GridColor) + ":" +
                GlassGlobeSettingsState.GridVisible + ":" +
                GlassGlobeSettingsState.CountryOutlineThickness.ToString("0.00", CultureInfo.InvariantCulture) + ":" +
                GlassGlobeSettingsState.GridThickness.ToString("0.00", CultureInfo.InvariantCulture);
            if (signature == appliedDisplaySignature)
            {
                return;
            }

            if (borderRenderer != null)
            {
                borderRenderer.SetCountryOutlineColor(GlassGlobeSettingsState.CountryOutlineColor);
                borderRenderer.SetCountryOutlineThickness(GlassGlobeSettingsState.CountryOutlineThickness);
            }

            if (gridRenderer != null)
            {
                gridRenderer.SetGridColor(GlassGlobeSettingsState.GridColor);
                gridRenderer.SetGridVisible(GlassGlobeSettingsState.GridVisible);
                gridRenderer.SetGridThickness(GlassGlobeSettingsState.GridThickness);
            }

            appliedDisplaySignature = signature;
        }

        private void ApplySunMoonSettings()
        {
            if (sunMoon == null)
            {
                return;
            }

            bool desiredSun = GlassGlobeSettingsState.SunEnabled;
            if (!sunSettingApplied || appliedSunSetting != desiredSun)
            {
                sunMoon.SetSunVisible(desiredSun);
                appliedSunSetting = desiredSun;
                sunSettingApplied = true;
            }

            bool desiredMoon = GlassGlobeSettingsState.MoonEnabled;
            if (!moonSettingApplied || appliedMoonSetting != desiredMoon)
            {
                sunMoon.SetMoonVisible(desiredMoon);
                appliedMoonSetting = desiredMoon;
                moonSettingApplied = true;
            }
        }

        private void ApplyWeatherSettings()
        {
            if (weather == null)
            {
                return;
            }

            bool desiredClouds = GlassGlobeSettingsState.WeatherCloudsEnabled;
            if (!weatherCloudsSettingApplied || appliedWeatherCloudsSetting != desiredClouds)
            {
                weather.SetCloudsVisible(desiredClouds);
                appliedWeatherCloudsSetting = desiredClouds;
                weatherCloudsSettingApplied = true;
            }

            bool desiredRadar = GlassGlobeSettingsState.WeatherRadarEnabled;
            if (!weatherRadarSettingApplied || appliedWeatherRadarSetting != desiredRadar)
            {
                weather.SetRadarVisible(desiredRadar);
                appliedWeatherRadarSetting = desiredRadar;
                weatherRadarSettingApplied = true;
            }
        }

        private void ApplyMilkyWaySetting()
        {
            if (milkyWay == null)
            {
                return;
            }

            bool desired = GlassGlobeSettingsState.MilkyWayEnabled;
            if (milkyWaySettingApplied && appliedMilkyWaySetting == desired)
            {
                return;
            }

            milkyWay.SetVisible(desired);
            appliedMilkyWaySetting = desired;
            milkyWaySettingApplied = true;
        }

        private void ApplyEarthArtSettings()
        {
            if (earthArt == null)
            {
                return;
            }

            string signature = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}:{1:0.00}:{2}:{3:0.00}:{4}:{5:0.00}:{6}:{7:0.00}",
                GlassGlobeSettingsState.WaterArtEnabled,
                GlassGlobeSettingsState.WaterArtOpacity,
                GlassGlobeSettingsState.LandArtEnabled,
                GlassGlobeSettingsState.LandArtOpacity,
                GlassGlobeSettingsState.OceanArtEnabled,
                GlassGlobeSettingsState.OceanArtOpacity,
                GlassGlobeSettingsState.ArtCloudsEnabled,
                GlassGlobeSettingsState.ArtCloudsOpacity);
            if (signature == appliedEarthArtSignature)
            {
                return;
            }

            earthArt.SetWaterVisible(GlassGlobeSettingsState.WaterArtEnabled);
            earthArt.SetLandVisible(GlassGlobeSettingsState.LandArtEnabled);
            earthArt.SetOceanVisible(GlassGlobeSettingsState.OceanArtEnabled);
            earthArt.SetArtCloudsVisible(GlassGlobeSettingsState.ArtCloudsEnabled);
            earthArt.ApplyOpacities();
            appliedEarthArtSignature = signature;
        }

        private void ApplyEarthStyleSettings()
        {
            if (earthStyle == null)
            {
                return;
            }

            bool desiredNight = GlassGlobeSettingsState.NightLightsEnabled;
            bool desiredRim = GlassGlobeSettingsState.RimGlowEnabled;
            if (nightLightsSettingApplied && appliedNightLightsSetting == desiredNight &&
                rimGlowSettingApplied && appliedRimGlowSetting == desiredRim)
            {
                return;
            }

            earthStyle.ApplySettings();
            appliedNightLightsSetting = desiredNight;
            nightLightsSettingApplied = true;
            appliedRimGlowSetting = desiredRim;
            rimGlowSettingApplied = true;
        }

        private void ApplyViewpointSetting(bool force)
        {
            string signature = GlassGlobeSettingsState.ViewpointOverrideEnabled
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "override:{0:0.000000}:{1:0.000000}:{2}",
                    GlassGlobeSettingsState.ViewpointLatitude,
                    GlassGlobeSettingsState.ViewpointLongitude,
                    GlassGlobeSettingsState.ViewpointLabel)
                : "gps";

            if (!force && signature == appliedViewpointSignature)
            {
                return;
            }

            bool sensorModeActive = poseSensors != null && poseSensors.SensorModeActive;
            if (phonePose != null && !sensorModeActive)
            {
                if (GlassGlobeSettingsState.ViewpointOverrideEnabled)
                {
                    phonePose.userCoordinate = GlassGlobeSettingsState.ViewpointCoordinate;
                }
                else if (hasSimulatorDefault)
                {
                    phonePose.userCoordinate = simulatorDefaultCoordinate;
                }

                phonePose.ApplyPose();
            }

            if (poseSensors != null)
            {
                poseSensors.RefreshViewpoint();
            }

            if (farSideRaycaster != null)
            {
                farSideRaycaster.UpdateRaycast();
            }

            appliedViewpointSignature = signature;
        }

        private void SeedCoordinateFields()
        {
            if (!GlassGlobeSettingsState.ViewpointOverrideEnabled)
            {
                return;
            }

            latitudeText = GlassGlobeSettingsState.ViewpointLatitude.ToString("0.0000", CultureInfo.InvariantCulture);
            longitudeText = GlassGlobeSettingsState.ViewpointLongitude.ToString("0.0000", CultureInfo.InvariantCulture);
            customViewpointName = GlassGlobeSettingsState.ViewpointLabel;
        }

        private void CaptureSimulatorDefault()
        {
            if (hasSimulatorDefault || phonePose == null)
            {
                return;
            }

            simulatorDefaultCoordinate = phonePose.userCoordinate;
            hasSimulatorDefault = true;
        }

        private void BuildViewpointChoices()
        {
            if (viewpointChoices.Count == 0)
            {
                viewpointChoices.AddRange(CityChoices);
            }

            if (choicesBuiltFromCountries ||
                borderRenderer == null ||
                borderRenderer.Outlines == null ||
                borderRenderer.Outlines.Count == 0)
            {
                return;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < viewpointChoices.Count; index++)
            {
                names.Add(viewpointChoices[index].Name);
            }

            for (int index = 0; index < borderRenderer.Outlines.Count; index++)
            {
                CountryBorderRenderer.GeoOutline outline = borderRenderer.Outlines[index];
                if (outline == null || !outline.isCountry || string.IsNullOrWhiteSpace(outline.name) || !names.Add(outline.name))
                {
                    continue;
                }

                viewpointChoices.Add(new ViewpointChoice(outline.name, "Country", outline.labelCoordinate));
            }

            viewpointChoices.Sort(delegate (ViewpointChoice left, ViewpointChoice right)
            {
                int kindOrder = string.Compare(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase);
                if (kindOrder != 0)
                {
                    return kindOrder;
                }

                return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });
            choicesBuiltFromCountries = true;
        }

        private void BuildFilteredChoices()
        {
            filteredChoices.Clear();
            string query = (viewpointSearch ?? string.Empty).Trim();

            if (query.Length == 0)
            {
                for (int index = 0; index < viewpointChoices.Count && filteredChoices.Count < 6; index++)
                {
                    if (viewpointChoices[index].Kind == "City")
                    {
                        filteredChoices.Add(viewpointChoices[index]);
                    }
                }

                return;
            }

            AddMatchingChoices(query, true);
            if (filteredChoices.Count < 12)
            {
                AddMatchingChoices(query, false);
            }
        }

        private void AddMatchingChoices(string query, bool startsWith)
        {
            for (int index = 0; index < viewpointChoices.Count && filteredChoices.Count < 12; index++)
            {
                ViewpointChoice choice = viewpointChoices[index];
                bool matches = startsWith
                    ? choice.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                    : choice.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matches || filteredChoices.Contains(choice))
                {
                    continue;
                }

                filteredChoices.Add(choice);
            }
        }

        private void ResolveReferences()
        {
            if (hud == null)
            {
                hud = GetComponent<GlassGlobeHUD>();
            }

            if (cameraFeed == null)
            {
                cameraFeed = FindFirstObjectByType<CameraFeedRenderer>();
            }

            if (poseSensors == null)
            {
                poseSensors = FindFirstObjectByType<PhonePoseSensors>();
            }

            if (phonePose == null)
            {
                phonePose = FindFirstObjectByType<PhonePoseSimulator>();
            }

            if (farSideRaycaster == null)
            {
                farSideRaycaster = FindFirstObjectByType<FarSideRaycaster>();
            }

            if (borderRenderer == null)
            {
                borderRenderer = FindFirstObjectByType<CountryBorderRenderer>();
            }

            if (labelController == null)
            {
                labelController = FindFirstObjectByType<CountryLabelController>();
            }

            if (gridRenderer == null)
            {
                gridRenderer = FindFirstObjectByType<GlobeGridRenderer>();
            }

            if (milkyWay == null)
            {
                milkyWay = FindFirstObjectByType<MilkyWayBackground>();
            }

            if (sunMoon == null)
            {
                sunMoon = FindFirstObjectByType<SunMoonBackground>();
            }

            if (earthStyle == null)
            {
                earthStyle = FindFirstObjectByType<EarthStyleController>();
                if (earthStyle == null)
                {
                    earthStyle = EarthStyleController.EnsureInstance(FindFirstObjectByType<GlobeRenderer>());
                }
            }

            if (earthArt == null)
            {
                earthArt = FindFirstObjectByType<EarthArtOverlay>();
            }

            if (weather == null)
            {
                weather = FindFirstObjectByType<WeatherOverlay>();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 28;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);

            headingStyle = new GUIStyle(GUI.skin.label);
            headingStyle.fontSize = 20;
            headingStyle.fontStyle = FontStyle.Bold;
            headingStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);
            headingStyle.wordWrap = true;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 16;
            bodyStyle.normal.textColor = new Color(0.88f, 0.94f, 1f, 1f);
            bodyStyle.wordWrap = true;

            statusStyle = new GUIStyle(bodyStyle);
            statusStyle.fontStyle = FontStyle.Italic;

            captureTextStyle = new GUIStyle(GUI.skin.label);
            captureTextStyle.fontSize = 15;
            captureTextStyle.normal.textColor = Color.white;
            captureTextStyle.wordWrap = true;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;
            buttonStyle.wordWrap = true;

            entryButtonStyle = new GUIStyle(buttonStyle);
        }
    }
}

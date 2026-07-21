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
    /// The class is split across partial files: this one owns state, lifecycle,
    /// and apply logic; GlassGlobeSettingsController.Pages.cs draws the pages;
    /// .Orient.cs holds the sky-alignment flow; .Input.cs the mobile touch layer.
    /// </summary>
    public sealed partial class GlassGlobeSettingsController : MonoBehaviour
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
        public SatelliteOverlay satelliteOverlay;
        public EarthquakeOverlay earthquakeOverlay;

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
            LiveData,
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
        private bool displaySettingsDirty = true;
        private bool milkyWaySettingApplied;
        private bool appliedMilkyWaySetting;
        private bool sunSettingApplied;
        private bool appliedSunSetting;
        private bool moonSettingApplied;
        private bool appliedMoonSetting;
        private EarthArtOverlay earthArt;
        private bool earthArtSettingsDirty = true;
        private bool nightLightsSettingApplied;
        private bool appliedNightLightsSetting;
        private bool rimGlowSettingApplied;
        private bool appliedRimGlowSetting;
        private bool weatherCloudsSettingApplied;
        private bool appliedWeatherCloudsSetting;
        private bool weatherRadarSettingApplied;
        private bool appliedWeatherRadarSetting;
        private bool satellitesSettingApplied;
        private bool appliedSatellitesSetting;
        private bool earthquakesSettingApplied;
        private bool appliedEarthquakesSetting;
        private AlignBody alignTarget = AlignBody.Sun;
        private Texture2D alignRingTexture;
        private GUIStyle captureTextStyle;
        private string orientStatusMessage = string.Empty;
        private bool viewpointSettingDirty = true;
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

        private void ShowPage(SettingsPage page)
        {
            currentPage = page;
            settingsScrollPosition = Vector2.zero;
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
            earthArtSettingsDirty = true;
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

        private void SetSatellitesEnabled(bool value)
        {
            GlassGlobeSettingsState.SetSatellitesEnabled(value);
            satellitesSettingApplied = false;
            ApplyLiveDataSettings();
        }

        private void SetEarthquakesEnabled(bool value)
        {
            GlassGlobeSettingsState.SetEarthquakesEnabled(value);
            earthquakesSettingApplied = false;
            ApplyLiveDataSettings();
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

        private void ApplySavedSettings()
        {
            GlassGlobeSettingsState.Load();
            ResolveReferences();
            displaySettingsDirty = true;
            earthArtSettingsDirty = true;
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
            ApplyDisplaySettings();
            ApplyMilkyWaySetting();
            ApplySunMoonSettings();
            ApplyEarthStyleSettings();
            ApplyEarthArtSettings();
            ApplyWeatherSettings();
            ApplyLiveDataSettings();
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
            ApplyLiveDataSettings();
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
            if (!displaySettingsDirty)
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

            displaySettingsDirty = false;
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

        private void ApplyLiveDataSettings()
        {
            if (satelliteOverlay != null)
            {
                bool desiredSatellites = GlassGlobeSettingsState.SatellitesEnabled;
                if (!satellitesSettingApplied || appliedSatellitesSetting != desiredSatellites)
                {
                    satelliteOverlay.SetSatellitesVisible(desiredSatellites);
                    appliedSatellitesSetting = desiredSatellites;
                    satellitesSettingApplied = true;
                }
            }

            if (earthquakeOverlay != null)
            {
                bool desiredEarthquakes = GlassGlobeSettingsState.EarthquakesEnabled;
                if (!earthquakesSettingApplied || appliedEarthquakesSetting != desiredEarthquakes)
                {
                    earthquakeOverlay.SetEarthquakesVisible(desiredEarthquakes);
                    appliedEarthquakesSetting = desiredEarthquakes;
                    earthquakesSettingApplied = true;
                }
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
            if (earthArt == null || !earthArtSettingsDirty)
            {
                return;
            }

            earthArt.SetWaterVisible(GlassGlobeSettingsState.WaterArtEnabled);
            earthArt.SetLandVisible(GlassGlobeSettingsState.LandArtEnabled);
            earthArt.SetOceanVisible(GlassGlobeSettingsState.OceanArtEnabled);
            earthArt.SetArtCloudsVisible(GlassGlobeSettingsState.ArtCloudsEnabled);
            earthArt.ApplyOpacities();
            earthArtSettingsDirty = false;
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
            if (force)
            {
                viewpointSettingDirty = true;
            }

            if (!viewpointSettingDirty)
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

            viewpointSettingDirty = false;
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

            if (satelliteOverlay == null)
            {
                satelliteOverlay = FindFirstObjectByType<SatelliteOverlay>();
            }

            if (earthquakeOverlay == null)
            {
                earthquakeOverlay = FindFirstObjectByType<EarthquakeOverlay>();
            }
        }
    }
}

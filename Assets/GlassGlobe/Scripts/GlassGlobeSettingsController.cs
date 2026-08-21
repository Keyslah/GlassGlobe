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
        public BlueMarbleSurface blueMarble;
        public WeatherOverlay weather;
        public GlobeGridRenderer gridRenderer;
        public SatelliteOverlay satelliteOverlay;
        public EarthquakeOverlay earthquakeOverlay;

        private enum SettingsPage
        {
            Closed,
            Settings,
            Viewpoint,
            Display,
            LiveData,
            Orient,
            OrientCapture
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

        /// <summary>
        /// How many viewpoint suggestions the page offers at once.
        /// </summary>
        private const int MaxViewpointResults = 8;

        /// <summary>
        /// How many country entries may take up room before the city matches.
        /// </summary>
        private const int MaxCountryResults = 3;

        private readonly List<ViewpointChoice> viewpointChoices = new List<ViewpointChoice>();
        private readonly List<ViewpointChoice> filteredChoices = new List<ViewpointChoice>();
        private readonly List<CityDataLoader.City> cityResults = new List<CityDataLoader.City>();
        private readonly List<TouchTarget> touchTargets = new List<TouchTarget>();

        private SettingsPage currentPage = SettingsPage.Closed;
        private GUIStyle titleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private GUIStyle buttonStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle seasonButtonStyle;
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
        private bool blueMarbleSettingsDirty = true;
        private bool surfaceSelectionUpdating;
        private bool resetConfirmPending;

        private Rect seasonCycleButtonRect;

        private enum SaveLoadMode
        {
            None,
            Naming,
            Choosing
        }

        private SaveLoadMode saveLoadMode = SaveLoadMode.None;
        private string saveSetName = "My settings";
        private bool nightLightsSettingApplied;
        private bool appliedNightLightsSetting;
        private bool nightLightsOpacitySettingApplied;
        private float appliedNightLightsOpacity;
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
        private string filteredChoicesQuery;
        private bool filteredChoicesValid;
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
        private bool pinchGestureActive;
        private bool pinchGestureConsumed;
        private float previousPinchDistance;
        private float zoomIndicatorVisibleUntil;
        private float zoomIndicatorCurrentFov = PhonePoseSimulator.DefaultViewportFovDegrees;
        private float zoomIndicatorDefaultFov = PhonePoseSimulator.DefaultViewportFovDegrees;
        private GUIStyle zoomIndicatorStyle;
        private GlassGlobePortraitUi.Rotation lastUiRotation =
            GlassGlobePortraitUi.Rotation.Portrait;

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

            GlassGlobePortraitUi.Rotation uiRotation =
                GlassGlobePortraitUi.CurrentRotation;
            if (uiRotation != lastUiRotation)
            {
                lastUiRotation = uiRotation;
                ResetTrackedTouch();
            }

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
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = GlassGlobePortraitUi.GuiMatrix;

            if (Application.isMobilePlatform && Event.current.type == EventType.Repaint)
            {
                touchTargets.Clear();
            }

            if (currentPage == SettingsPage.Closed)
            {
                DrawZoomIndicator();
                DrawSeasonCycleButton();

                if (!Application.isMobilePlatform &&
                    Event.current.type == EventType.MouseUp &&
                    Event.current.button == 0 &&
                    !seasonCycleButtonRect.Contains(Event.current.mousePosition) &&
                    (hud == null || !hud.IsInteractiveScreenPoint(Event.current.mousePosition)))
                {
                    OpenSettings();
                    Event.current.Use();
                }

                GUI.matrix = previousMatrix;
                return;
            }

            DrawSettingsPage();
            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Faint season button along the bottom of the viewport. It only means
        /// anything while Blue Marble is showing, so it hides with it. Drawn in
        /// the same UI space the touch layer reports points in, so the stored
        /// rect can be hit-tested directly against a tap.
        /// </summary>
        private void DrawSeasonCycleButton()
        {
            if (!GlassGlobeSettingsState.EffectiveBlueMarbleEnabled ||
                !GlassGlobeSettingsState.SeasonButtonVisible)
            {
                seasonCycleButtonRect = new Rect();
                return;
            }

            float width = Mathf.Min(460f, GlassGlobePortraitUi.Width - 32f);
            float height = GlassGlobeUi.GetInteractiveControlHeight(112f);
            // Flush to the bottom edge on purpose. With a margin there was a
            // thin strip below the button that still counted as the viewport,
            // so grabbing for the button and missing low opened settings.
            seasonCycleButtonRect = new Rect(
                (GlassGlobePortraitUi.Width - width) * 0.5f,
                GlassGlobePortraitUi.Height - height,
                width,
                height);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            bool clicked = GUI.Button(
                seasonCycleButtonRect,
                GlassGlobeSettingsState.BlueMarbleSeasonChoice.ToString(),
                seasonButtonStyle);
            GUI.color = previousColor;

            if (clicked && !Application.isMobilePlatform)
            {
                CycleBlueMarbleSeason();
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
            // Leaving the page answers "no" to a pending reset prompt, the same
            // as navigating between pages does.
            resetConfirmPending = false;
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
            // Navigating away is an answer of "no" to a pending reset prompt or
            // a half-finished save.
            resetConfirmPending = false;
            saveLoadMode = SaveLoadMode.None;
        }

        /// <summary>
        /// Wipes every saved setting and pushes the defaults back onto the
        /// scene. Two taps are required because a stray tap here would undo all
        /// of the user's choices at once.
        /// </summary>
        private void ResetAllSettingsToDefaults()
        {
            if (!resetConfirmPending)
            {
                resetConfirmPending = true;
                statusMessage = string.Empty;
                return;
            }

            resetConfirmPending = false;
            GlassGlobeSettingsState.ResetToDefaults();

            // Clearing the keys already drops the viewpoint override, but say it
            // outright: a reset has to put the user back over their real GPS
            // position, not leave them parked above whatever city they picked.
            GlassGlobeSettingsState.UseRealLocation();

            latitudeText = "0.0000";
            longitudeText = "0.0000";
            customViewpointName = string.Empty;
            viewpointSearch = string.Empty;
            orientStatusMessage = string.Empty;

            PushEverySettingToScene();
            statusMessage = "All settings restored to defaults. Viewing from your current location.";
        }

        private void BeginNamingSave()
        {
            saveLoadMode = SaveLoadMode.Naming;
            statusMessage = string.Empty;
            if (string.IsNullOrEmpty(saveSetName))
            {
                saveSetName = "My settings";
            }
        }

        private void BeginChoosingLoad()
        {
            saveLoadMode = SaveLoadMode.Choosing;
            statusMessage = string.Empty;
        }

        private void CancelSaveLoad()
        {
            saveLoadMode = SaveLoadMode.None;
            statusMessage = string.Empty;
        }

        private void ConfirmSaveWithName()
        {
            string name = (saveSetName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                statusMessage = "Give the set a name first.";
                return;
            }

            if (!GlassGlobeSettingsState.SaveSettingsAs(name))
            {
                statusMessage = "No room left; overwrite one of the existing names.";
                return;
            }

            saveLoadMode = SaveLoadMode.None;
            statusMessage = "Saved as \"" + name + "\".";
        }

        private void LoadSavedSet(int slot)
        {
            string name = GlassGlobeSettingsState.GetSavedSetName(slot);
            if (!GlassGlobeSettingsState.LoadSavedSet(slot))
            {
                statusMessage = "That set could not be loaded.";
                return;
            }

            saveLoadMode = SaveLoadMode.None;
            viewpointSearch = string.Empty;
            PushEverySettingToScene();
            SeedCoordinateFields();
            statusMessage = "Loaded \"" + name + "\".";
        }

        /// <summary>
        /// Re-pushes the whole settings block onto the scene. Every Apply pass
        /// caches the value it last applied, so a wholesale change of the
        /// underlying state has to clear those caches or most of it silently
        /// would not take.
        /// </summary>
        private void PushEverySettingToScene()
        {
            cameraSettingApplied = false;
            labelsSettingApplied = false;
            milkyWaySettingApplied = false;
            sunSettingApplied = false;
            moonSettingApplied = false;
            nightLightsSettingApplied = false;
            nightLightsOpacitySettingApplied = false;
            rimGlowSettingApplied = false;
            weatherCloudsSettingApplied = false;
            weatherRadarSettingApplied = false;
            satellitesSettingApplied = false;
            earthquakesSettingApplied = false;
            displaySettingsDirty = true;
            earthArtSettingsDirty = true;
            blueMarbleSettingsDirty = true;
            viewpointSettingDirty = true;
            filteredChoicesValid = false;

            ApplySavedSettings();
        }

        private void SetCameraFeedEnabled(bool value)
        {
            GlassGlobeSettingsState.SetCameraFeedEnabled(value);
            cameraSettingApplied = false;
            ApplyCameraSetting();
        }

        private void SetSettingsCategoryEnabled(SettingsPage page, bool value)
        {
            switch (page)
            {
                case SettingsPage.Viewpoint:
                    GlassGlobeSettingsState.SetViewpointCategoryEnabled(value);
                    break;
                case SettingsPage.Display:
                    GlassGlobeSettingsState.SetDisplayCategoryEnabled(value);
                    PushEverySettingToScene();
                    return;
                case SettingsPage.LiveData:
                    GlassGlobeSettingsState.SetLiveDataCategoryEnabled(value);
                    break;
                case SettingsPage.Orient:
                    GlassGlobeSettingsState.SetOrientCategoryEnabled(value);
                    orientStatusMessage = value
                        ? "Orient settings restored."
                        : "Orient settings disabled; your alignment is saved.";
                    break;
                default:
                    return;
            }

            ApplySavedSettings();
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

        /// <summary>
        /// Blue Moon and Blue Marble are two values of one enum, so choosing
        /// either always clears the other and the pair can never be both or
        /// neither. The guard stops the apply pass this kicks off from
        /// re-entering the checkbox callbacks it refreshes.
        /// </summary>
        private void SelectGlobeSurface(GlobeSurfaceMode mode)
        {
            if (surfaceSelectionUpdating)
            {
                return;
            }

            surfaceSelectionUpdating = true;
            try
            {
                GlassGlobeSettingsState.SetGlobeSurface(mode);
                MarkBlueMarbleDirty();
                earthArtSettingsDirty = true;
                weatherCloudsSettingApplied = false;
                weatherRadarSettingApplied = false;
                ApplyEarthArtSettings();
                ApplyWeatherSettings();
            }
            finally
            {
                surfaceSelectionUpdating = false;
            }
        }

        private void SelectBlueMarbleSeason(BlueMarbleSeason season)
        {
            if (surfaceSelectionUpdating)
            {
                return;
            }

            surfaceSelectionUpdating = true;
            try
            {
                GlassGlobeSettingsState.SetBlueMarbleSeason(season);
                MarkBlueMarbleDirty();
            }
            finally
            {
                surfaceSelectionUpdating = false;
            }
        }

        private void SetBlueMarbleOpacity(float value)
        {
            GlassGlobeSettingsState.SetBlueMarbleOpacity(value);
            MarkBlueMarbleDirty();
        }

        private void MarkBlueMarbleDirty()
        {
            blueMarbleSettingsDirty = true;
            ApplyBlueMarbleSettings();
        }

        private void SetNightLightsEnabled(bool value)
        {
            GlassGlobeSettingsState.SetNightLightsEnabled(value);
            nightLightsSettingApplied = false;
            ApplyEarthStyleSettings();
        }

        private void SetNightLightsOpacity(float value)
        {
            GlassGlobeSettingsState.SetNightLightsOpacity(value);
            nightLightsOpacitySettingApplied = false;
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

        /// <summary>
        /// Moves the viewpoint to whatever the reticle is currently on.
        /// </summary>
        private void JumpToCenterPoint()
        {
            if (farSideRaycaster == null)
            {
                statusMessage = "Center point is not available.";
                return;
            }

            GeoCoordinate coordinate;
            Vector3 point;
            if (!farSideRaycaster.TryGetFarSideHit(out point, out coordinate))
            {
                statusMessage = "Point the center dot at the Earth first.";
                return;
            }

            string label = DescribeCoordinate(coordinate);
            GlassGlobeSettingsState.SetViewpoint(coordinate, label);
            latitudeText = coordinate.Latitude.ToString("0.0000", CultureInfo.InvariantCulture);
            longitudeText = coordinate.Longitude.ToString("0.0000", CultureInfo.InvariantCulture);
            customViewpointName = label;
            statusMessage = "Viewpoint changed to " + label + ".";
            ApplyViewpointSetting(true);
        }

        /// <summary>
        /// Advances the Blue Marble season, wrapping round. Driven by the
        /// viewport button so seasons can be flipped without opening settings.
        /// </summary>
        private void CycleBlueMarbleSeason()
        {
            BlueMarbleSeason next =
                (BlueMarbleSeason)(((int)GlassGlobeSettingsState.BlueMarbleSeasonChoice + 1) % 4);
            SelectBlueMarbleSeason(next);
        }

        /// <summary>
        /// Best available name for a point: the country under it when there is
        /// one, otherwise the coordinates themselves.
        /// </summary>
        private string DescribeCoordinate(GeoCoordinate coordinate)
        {
            string formatted = string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.00}, {1:0.00}",
                coordinate.Latitude,
                coordinate.Longitude);

            if (borderRenderer == null)
            {
                return formatted;
            }

            string region = borderRenderer.GetRegionForCoordinate(coordinate);
            if (string.IsNullOrEmpty(region) ||
                region == "Unknown" ||
                region == "Open ocean")
            {
                return formatted;
            }

            return region.StartsWith("Nearest: ") ? region.Substring(9) : region;
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
            blueMarbleSettingsDirty = true;
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
            ApplyDisplaySettings();
            ApplyMilkyWaySetting();
            ApplySunMoonSettings();
            ApplyEarthStyleSettings();
            ApplyBlueMarbleSettings();
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
            ApplyBlueMarbleSettings();
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

            bool desired = GlassGlobeSettingsState.ViewpointCategoryEnabled &&
                GlassGlobeSettingsState.CountryLabelsVisible;
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
                bool bordersVisible = GlassGlobeSettingsState.DisplayCategoryEnabled;
                if (borderRenderer.showCountryOutlines != bordersVisible ||
                    borderRenderer.showContinentOutlines != bordersVisible)
                {
                    borderRenderer.showCountryOutlines = bordersVisible;
                    borderRenderer.showContinentOutlines = bordersVisible;
                    borderRenderer.RebuildBorders();
                }

                borderRenderer.SetCountryOutlineColor(GlassGlobeSettingsState.CountryOutlineColor);
                borderRenderer.SetCountryOutlineThickness(GlassGlobeSettingsState.CountryOutlineThickness);
            }

            if (gridRenderer != null)
            {
                gridRenderer.SetGridColor(GlassGlobeSettingsState.GridColor);
                gridRenderer.SetGridVisible(
                    GlassGlobeSettingsState.DisplayCategoryEnabled &&
                    GlassGlobeSettingsState.GridVisible);
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

            bool desiredSun = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.SunEnabled;
            if (!sunSettingApplied || appliedSunSetting != desiredSun)
            {
                sunMoon.SetSunVisible(desiredSun);
                appliedSunSetting = desiredSun;
                sunSettingApplied = true;
            }

            bool desiredMoon = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.MoonEnabled;
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

            bool overlaysCompatible =
                GlassGlobeSettingsState.GlobeSurface != GlobeSurfaceMode.BlueMarble;
            bool desiredClouds = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                overlaysCompatible &&
                GlassGlobeSettingsState.WeatherCloudsEnabled;
            if (!weatherCloudsSettingApplied || appliedWeatherCloudsSetting != desiredClouds)
            {
                weather.SetCloudsVisible(desiredClouds);
                appliedWeatherCloudsSetting = desiredClouds;
                weatherCloudsSettingApplied = true;
            }

            bool desiredRadar = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                overlaysCompatible &&
                GlassGlobeSettingsState.WeatherRadarEnabled;
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
                bool desiredSatellites = GlassGlobeSettingsState.LiveDataCategoryEnabled &&
                    GlassGlobeSettingsState.SatellitesEnabled;
                if (!satellitesSettingApplied || appliedSatellitesSetting != desiredSatellites)
                {
                    satelliteOverlay.SetSatellitesVisible(desiredSatellites);
                    appliedSatellitesSetting = desiredSatellites;
                    satellitesSettingApplied = true;
                }
            }

            if (earthquakeOverlay != null)
            {
                bool desiredEarthquakes = GlassGlobeSettingsState.LiveDataCategoryEnabled &&
                    GlassGlobeSettingsState.EarthquakesEnabled;
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

            bool desired = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.MilkyWayEnabled;
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

            bool categoryEnabled = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.GlobeSurface != GlobeSurfaceMode.BlueMarble;
            earthArt.SetWaterVisible(categoryEnabled && GlassGlobeSettingsState.WaterArtEnabled);
            earthArt.SetLandVisible(categoryEnabled && GlassGlobeSettingsState.LandArtEnabled);
            earthArt.SetOceanVisible(categoryEnabled && GlassGlobeSettingsState.OceanArtEnabled);
            earthArt.SetArtCloudsVisible(categoryEnabled && GlassGlobeSettingsState.ArtCloudsEnabled);
            earthArt.ApplyOpacities();
            earthArtSettingsDirty = false;
        }

        private void ApplyBlueMarbleSettings()
        {
            if (blueMarble == null || !blueMarbleSettingsDirty)
            {
                return;
            }

            // Only clear the flag once the globe material actually took the
            // values, so a startup apply that lands before the globe finishes
            // rebuilding is retried on the next pass.
            if (blueMarble.ApplySettings())
            {
                blueMarbleSettingsDirty = false;
            }
        }

        private void ApplyEarthStyleSettings()
        {
            if (earthStyle == null)
            {
                return;
            }

            bool desiredNight = GlassGlobeSettingsState.EffectiveNightLightsEnabled;
            float desiredNightOpacity = desiredNight
                ? GlassGlobeSettingsState.NightLightsOpacity
                : 0f;
            bool desiredRim = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.RimGlowEnabled;
            if (nightLightsSettingApplied && appliedNightLightsSetting == desiredNight &&
                nightLightsOpacitySettingApplied &&
                Mathf.Approximately(appliedNightLightsOpacity, desiredNightOpacity) &&
                rimGlowSettingApplied && appliedRimGlowSetting == desiredRim)
            {
                return;
            }

            // Do not record the state as applied until the globe material accepts
            // it. Startup can reach this controller before GlobeRenderer has
            // finished rebuilding its material, and that must be retried.
            if (!earthStyle.ApplySettings())
            {
                return;
            }

            appliedNightLightsSetting = desiredNight;
            nightLightsSettingApplied = true;
            appliedNightLightsOpacity = desiredNightOpacity;
            nightLightsOpacitySettingApplied = true;
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
                if (GlassGlobeSettingsState.EffectiveViewpointOverrideEnabled)
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
            // Cities come from the bundled gazetteer; this list is countries
            // only, so you can also view from a country as a whole.
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
            // New entries invalidate whatever the last search produced.
            filteredChoicesValid = false;
        }

        /// <summary>
        /// Rebuilds the suggestion list for the current search text. The result
        /// is cached against the query because this runs from OnGUI and the
        /// gazetteer holds tens of thousands of cities - rescanning it every
        /// repaint would be pure waste.
        /// </summary>
        private void BuildFilteredChoices()
        {
            string query = (viewpointSearch ?? string.Empty).Trim();
            if (filteredChoicesValid && string.Equals(filteredChoicesQuery, query, StringComparison.Ordinal))
            {
                return;
            }

            filteredChoicesQuery = query;
            filteredChoicesValid = true;
            filteredChoices.Clear();

            // A typed country name is offered as a viewpoint in its own right
            // before the cities inside it: "view from Japan" and "view from a
            // city in Japan" are different requests.
            if (query.Length > 0)
            {
                AddMatchingCountries(query, true);
                AddMatchingCountries(query, false);
            }

            CityDataLoader.Search(query, cityResults, MaxViewpointResults);
            for (int index = 0; index < cityResults.Count && filteredChoices.Count < MaxViewpointResults; index++)
            {
                CityDataLoader.City city = cityResults[index];
                string label = string.IsNullOrEmpty(city.Country)
                    ? city.Name
                    : city.Name + ", " + city.Country;
                filteredChoices.Add(new ViewpointChoice(label, "City", city.Coordinate));
            }
        }

        private void AddMatchingCountries(string query, bool startsWith)
        {
            int countryCount = 0;
            for (int index = 0; index < filteredChoices.Count; index++)
            {
                if (filteredChoices[index].Kind == "Country")
                {
                    countryCount++;
                }
            }

            for (int index = 0; index < viewpointChoices.Count; index++)
            {
                if (countryCount >= MaxCountryResults || filteredChoices.Count >= MaxViewpointResults)
                {
                    return;
                }

                ViewpointChoice choice = viewpointChoices[index];
                bool matches = startsWith
                    ? choice.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                    : choice.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matches || filteredChoices.Contains(choice))
                {
                    continue;
                }

                filteredChoices.Add(choice);
                countryCount++;
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

            if (blueMarble == null)
            {
                blueMarble = FindFirstObjectByType<BlueMarbleSurface>();
                if (blueMarble == null)
                {
                    blueMarble = BlueMarbleSurface.EnsureInstance(FindFirstObjectByType<GlobeRenderer>());
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

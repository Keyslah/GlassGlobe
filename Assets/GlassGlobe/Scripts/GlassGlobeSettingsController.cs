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
        public GlassGlobeHUD hud;
        public CameraFeedRenderer cameraFeed;
        public PhonePoseSensors poseSensors;
        public PhonePoseSimulator phonePose;
        public FarSideRaycaster farSideRaycaster;
        public CountryBorderRenderer borderRenderer;
        public CountryLabelController labelController;

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
            Privacy
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
        private string appliedViewpointSignature;
        private bool choicesBuiltFromCountries;
        private string viewpointSearch = string.Empty;
        private string latitudeText = "0.0000";
        private string longitudeText = "0.0000";
        private string customViewpointName = string.Empty;
        private string statusMessage = string.Empty;

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
        }

        private void Update()
        {
            ResolveReferences();
            CaptureSimulatorDefault();
            BuildViewpointChoices();
            TrackInteraction();
            HandleMobileTouch();
            ApplySettingsIfChanged();

            if (currentPage != SettingsPage.Closed && hud != null)
            {
                hud.showHud = false;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            touchTargets.Clear();

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

            float width = Mathf.Clamp(Screen.width * 0.26f, 132f, 220f);
            float height = Mathf.Clamp(Screen.height * 0.06f, 48f, 72f);
            Rect buttonRect = new Rect(Screen.width - width - 20f, Screen.height - height - 24f, width, height);

            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * alpha);
            GUI.enabled = alpha > 0.15f;
            bool clicked = GUI.Button(buttonRect, "Settings", buttonStyle);
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
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
            GUI.color = previousColor;

            activeUiScale = Application.isMobilePlatform
                ? Mathf.Clamp(Screen.width / 540f, 1f, 2f)
                : 1f;

            float logicalWidth = Screen.width / activeUiScale;
            float logicalHeight = Screen.height / activeUiScale;
            float panelWidth = Mathf.Min(540f, logicalWidth - 24f);
            float panelHeight = Mathf.Min(780f, logicalHeight - 24f);
            activeAreaRect = new Rect(
                (logicalWidth - panelWidth) * 0.5f,
                (logicalHeight - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(activeUiScale, activeUiScale, 1f));
            GUILayout.BeginArea(activeAreaRect, GUI.skin.box);

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
                case SettingsPage.Privacy:
                    DrawPrivacyPage();
                    break;
            }

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
            DrawButton("Camera", delegate { currentPage = SettingsPage.Camera; }, 58f);
            GUILayout.Space(8f);
            DrawButton("Viewpoint", delegate { currentPage = SettingsPage.Viewpoint; }, 58f);
            GUILayout.Space(8f);
            DrawButton("Privacy", delegate { currentPage = SettingsPage.Privacy; }, 58f);
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
            viewpointSearch = GUILayout.TextField(viewpointSearch ?? string.Empty, GUILayout.Height(38f));
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
            latitudeText = GUILayout.TextField(latitudeText ?? string.Empty, GUILayout.Height(36f));
            GUILayout.Space(8f);
            GUILayout.Label("Lon", bodyStyle, GUILayout.Width(32f));
            longitudeText = GUILayout.TextField(longitudeText ?? string.Empty, GUILayout.Height(36f));
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
            GUILayout.Label("Name shown in the HUD (optional)", bodyStyle);
            customViewpointName = GUILayout.TextField(customViewpointName ?? string.Empty, GUILayout.Height(36f));
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
            DrawButton("Back to Settings", delegate { currentPage = SettingsPage.Settings; }, 44f);
            GUILayout.EndHorizontal();
        }

        private void DrawCheckbox(string label, bool value, Action action)
        {
            DrawButton((value ? "[x] " : "[ ] ") + label, action, 42f);
        }

        private void DrawButton(string text, Action action, float height)
        {
            bool clicked = GUILayout.Button(text, buttonStyle, GUILayout.Height(height));
            Rect localRect = GUILayoutUtility.GetLastRect();
            RegisterLocalTouch(localRect, action);
            if (clicked && !Application.isMobilePlatform)
            {
                action();
            }
        }

        private void RegisterLocalTouch(Rect localRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null)
            {
                return;
            }

            Rect screenRect = new Rect(
                (activeAreaRect.x + localRect.x) * activeUiScale,
                (activeAreaRect.y + localRect.y) * activeUiScale,
                localRect.width * activeUiScale,
                localRect.height * activeUiScale);
            RegisterScreenTouch(screenRect, action);
        }

        private void RegisterScreenTouch(Rect screenRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null)
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
            if (touch.phase != TouchPhase.Ended)
            {
                return;
            }

            Vector2 screenPoint = new Vector2(touch.position.x, Screen.height - touch.position.y);
            for (int index = touchTargets.Count - 1; index >= 0; index--)
            {
                TouchTarget target = touchTargets[index];
                if (!target.ScreenRect.Contains(screenPoint))
                {
                    continue;
                }

                target.Action();
                lastInteractionTime = Time.unscaledTime;
                return;
            }
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

            currentPage = SettingsPage.Settings;
            statusMessage = string.Empty;
            lastInteractionTime = Time.unscaledTime;
            ApplySavedSettings();
        }

        private void BackToViewpoint()
        {
            currentPage = SettingsPage.Closed;
            if (hud != null)
            {
                hud.showHud = hudWasVisible;
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
            if (!TryParseFloat(latitudeText, out latitude) || !TryParseFloat(longitudeText, out longitude))
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
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
            ApplyViewpointSetting(true);
        }

        private void ApplySettingsIfChanged()
        {
            ApplyCameraSetting();
            ApplyCountryLabelSetting();
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
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 24;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);

            headingStyle = new GUIStyle(GUI.skin.label);
            headingStyle.fontSize = 17;
            headingStyle.fontStyle = FontStyle.Bold;
            headingStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);
            headingStyle.wordWrap = true;

            bodyStyle = new GUIStyle(GUI.skin.label);
            bodyStyle.fontSize = 14;
            bodyStyle.normal.textColor = new Color(0.88f, 0.94f, 1f, 1f);
            bodyStyle.wordWrap = true;

            statusStyle = new GUIStyle(bodyStyle);
            statusStyle.fontStyle = FontStyle.Italic;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 15;
            buttonStyle.wordWrap = true;
        }
    }
}

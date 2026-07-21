using System;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Page drawing for the settings flow. State and apply logic live in
    /// GlassGlobeSettingsController.cs.
    /// </summary>
    public sealed partial class GlassGlobeSettingsController
    {
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
                ? Mathf.RoundToInt(buttonStyle.fontSize * GlassGlobeUi.GetMobileUiScale())
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

            activeUiScale = GlassGlobeUi.GetMobileUiScale();

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
                case SettingsPage.LiveData:
                    DrawLiveDataPage();
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
            DrawButton("Live Data", delegate { ShowPage(SettingsPage.LiveData); }, 58f);
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
                "Moon (with real phase)",
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
            displaySettingsDirty = true;
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

                RefreshDisplaySettings();
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

        private void DrawOpacityRow(string label, float value, Action<float> setter)
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

        private void DrawLiveDataPage()
        {
            DrawCategoryHeader("Live Data");
            GUILayout.Space(18f);
            GUILayout.Label("Satellites", headingStyle);
            GUILayout.Label(
                "Shows the ISS and Tiangong space stations at their true positions right now, overhead in the sky or seen through the Earth. Point the center dot at one to see its name and altitude. Orbits refresh from CelesTrak every few hours.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Satellites (ISS + Tiangong)",
                GlassGlobeSettingsState.SatellitesEnabled,
                delegate { SetSatellitesEnabled(!GlassGlobeSettingsState.SatellitesEnabled); });
            GUILayout.Space(12f);
            string satelliteStatus = satelliteOverlay != null ? satelliteOverlay.Status : "Satellite component not found";
            GUILayout.Label("Satellites: " + satelliteStatus, statusStyle);

            GUILayout.Space(16f);
            GUILayout.Label("Earthquakes", headingStyle);
            GUILayout.Label(
                "Marks magnitude 4.5+ earthquakes from the last 24 hours as pulsing dots at their epicenters. Point the center dot at a marker to see its magnitude and location.",
                bodyStyle);
            GUILayout.Space(10f);
            DrawCheckbox(
                "Earthquakes (last 24h, M4.5+)",
                GlassGlobeSettingsState.EarthquakesEnabled,
                delegate { SetEarthquakesEnabled(!GlassGlobeSettingsState.EarthquakesEnabled); });
            GUILayout.Space(12f);
            string quakeStatus = earthquakeOverlay != null ? earthquakeOverlay.Status : "Earthquake component not found";
            GUILayout.Label("Earthquakes: " + quakeStatus, statusStyle);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Orbit data: CelesTrak two-line elements. Earthquake data: USGS Earthquake Hazards Program.",
                bodyStyle);
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
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(38f)));
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
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(36f)));
            GUILayout.Space(8f);
            GUILayout.Label("Lon", bodyStyle, GUILayout.Width(32f));
            longitudeText = GUILayout.TextField(
                longitudeText ?? string.Empty,
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(36f)));
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
            GUILayout.Label("Name shown in the HUD (optional)", bodyStyle);
            customViewpointName = GUILayout.TextField(
                customViewpointName ?? string.Empty,
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(36f)));
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
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(height)));
            Rect localRect = GUILayoutUtility.GetLastRect();
            RegisterLocalTouch(localRect, action);
            if (clicked && !Application.isMobilePlatform)
            {
                action();
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

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
        private void DrawSettingsPage()
        {
            if (currentPage == SettingsPage.OrientCapture)
            {
                DrawOrientCapturePage();
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(
                new Rect(
                    0f,
                    0f,
                    GlassGlobePortraitUi.Width,
                    GlassGlobePortraitUi.Height),
                GUIContent.none);
            GUI.color = previousColor;

            activeUiScale = GlassGlobeUi.GetMobileUiScale();

            Rect safeArea = GlassGlobePortraitUi.SafeArea;
            float safeX = safeArea.xMin / activeUiScale;
            float safeY = safeArea.yMin / activeUiScale;
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
            GUI.matrix =
                previousMatrix *
                Matrix4x4.Scale(
                    new Vector3(activeUiScale, activeUiScale, 1f));
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
                case SettingsPage.Viewpoint:
                    DrawViewpointPage();
                    break;
                case SettingsPage.Display:
                    DrawDisplayPage();
                    break;
                case SettingsPage.LiveData:
                    DrawLiveDataPage();
                    break;
                case SettingsPage.Orient:
                    DrawOrientPage();
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
            DrawButton(
                "Back to Viewport",
                BackToViewpoint,
                84f,
                true,
                primaryButtonStyle);
            GUILayout.Space(8f);
            DrawButton("Set North", AlignPhoneToNorth, 72f);
            if (!string.IsNullOrEmpty(orientStatusMessage))
            {
                GUILayout.Space(6f);
                GUILayout.Label(orientStatusMessage, statusStyle);
            }

            GUILayout.Space(18f);
            DrawSaveLoadControls();
            GUILayout.Space(8f);
            DrawButton(
                resetConfirmPending ? "Tap again to confirm reset" : "Reset to Defaults",
                ResetAllSettingsToDefaults,
                52f);
            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Space(6f);
                GUILayout.Label(statusMessage, statusStyle);
            }

            GUILayout.Space(18f);
            GUILayout.Label("Choose a settings category", bodyStyle);
            GUILayout.Space(10f);
            DrawSettingsCategoryRow(
                "Camera",
                GlassGlobeSettingsState.CameraFeedEnabled,
                delegate { SetCameraFeedEnabled(!GlassGlobeSettingsState.CameraFeedEnabled); },
                null);
            GUILayout.Space(8f);
            DrawSettingsCategoryRow(
                "Viewpoint",
                GlassGlobeSettingsState.ViewpointCategoryEnabled,
                delegate { SetSettingsCategoryEnabled(SettingsPage.Viewpoint, !GlassGlobeSettingsState.ViewpointCategoryEnabled); },
                delegate { ShowPage(SettingsPage.Viewpoint); });
            GUILayout.Space(8f);
            DrawSettingsCategoryRow(
                "Display",
                GlassGlobeSettingsState.DisplayCategoryEnabled,
                delegate { SetSettingsCategoryEnabled(SettingsPage.Display, !GlassGlobeSettingsState.DisplayCategoryEnabled); },
                delegate { ShowPage(SettingsPage.Display); });
            GUILayout.Space(8f);
            DrawSettingsCategoryRow(
                "Live Data",
                GlassGlobeSettingsState.LiveDataCategoryEnabled,
                delegate { SetSettingsCategoryEnabled(SettingsPage.LiveData, !GlassGlobeSettingsState.LiveDataCategoryEnabled); },
                delegate { ShowPage(SettingsPage.LiveData); });
            GUILayout.Space(8f);
            DrawSettingsCategoryRow(
                "Orient",
                GlassGlobeSettingsState.OrientCategoryEnabled,
                delegate { SetSettingsCategoryEnabled(SettingsPage.Orient, !GlassGlobeSettingsState.OrientCategoryEnabled); },
                delegate { ShowPage(SettingsPage.Orient); });
        }

        /// <summary>
        /// Save/Load with named sets. Naming and picking take over the same
        /// strip of the page rather than opening a separate screen, so the flow
        /// stays inside the settings home.
        /// </summary>
        private void DrawSaveLoadControls()
        {
            if (saveLoadMode == SaveLoadMode.Naming)
            {
                GUILayout.Label("Name this settings set", headingStyle);
                saveSetName = GUILayout.TextField(
                    saveSetName ?? string.Empty,
                    GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(38f)));
                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                DrawButton("Save", ConfirmSaveWithName, 48f);
                GUILayout.Space(8f);
                DrawButton("Cancel", CancelSaveLoad, 48f);
                GUILayout.EndHorizontal();
                return;
            }

            if (saveLoadMode == SaveLoadMode.Choosing)
            {
                GUILayout.Label("Load which set?", headingStyle);
                int count = GlassGlobeSettingsState.SavedSetCount;
                for (int slot = 0; slot < count; slot++)
                {
                    int capturedSlot = slot;
                    GUILayout.Space(4f);
                    DrawButton(
                        GlassGlobeSettingsState.GetSavedSetName(slot),
                        delegate { LoadSavedSet(capturedSlot); },
                        46f);
                }

                GUILayout.Space(6f);
                DrawButton("Cancel", CancelSaveLoad, 48f);
                return;
            }

            GUILayout.BeginHorizontal();
            DrawButton("Save Settings", BeginNamingSave, 52f);
            if (GlassGlobeSettingsState.HasSavedSettings)
            {
                GUILayout.Space(8f);
                DrawButton("Load Settings", BeginChoosingLoad, 52f);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSeasonCheckbox(string label, BlueMarbleSeason season)
        {
            DrawCheckbox(
                label,
                GlassGlobeSettingsState.BlueMarbleSeasonChoice == season,
                delegate { SelectBlueMarbleSeason(season); });
        }

        private void DrawDisplayPage()
        {
            DrawCategoryHeader("Display");
            GUILayout.Space(18f);

            // Background options lead the consolidated Display page.
            GUILayout.Label("Background", headingStyle);
            GUILayout.Space(8f);
            bool blueMarbleSelected =
                GlassGlobeSettingsState.GlobeSurface == GlobeSurfaceMode.BlueMarble;
            DrawCheckbox(
                "Blue Marble",
                blueMarbleSelected,
                delegate
                {
                    SelectGlobeSurface(
                        GlassGlobeSettingsState.GlobeSurface == GlobeSurfaceMode.BlueMarble
                            ? GlobeSurfaceMode.BlueMoon
                            : GlobeSurfaceMode.BlueMarble);
                });

            if (blueMarbleSelected)
            {
                GUILayout.Space(10f);
                DrawCheckbox(
                    "Hide surface button",
                    !GlassGlobeSettingsState.SeasonButtonVisible,
                    delegate
                    {
                        GlassGlobeSettingsState.SetSeasonButtonVisible(
                            !GlassGlobeSettingsState.SeasonButtonVisible);
                    });
                GUILayout.Space(10f);
                DrawSeasonCheckbox("Spring", BlueMarbleSeason.Spring);
                GUILayout.Space(6f);
                DrawSeasonCheckbox("Summer", BlueMarbleSeason.Summer);
                GUILayout.Space(6f);
                DrawSeasonCheckbox("Fall", BlueMarbleSeason.Fall);
                GUILayout.Space(6f);
                DrawSeasonCheckbox("Winter", BlueMarbleSeason.Winter);
                DrawOpacityRow(
                    "Blue Marble transparency",
                    GlassGlobeSettingsState.BlueMarbleOpacity,
                    delegate (float value) { SetBlueMarbleOpacity(value); },
                    268f);
            }

            GUILayout.Space(14f);
            DrawCheckbox(
                "Earth at Night",
                GlassGlobeSettingsState.NightLightsEnabled,
                delegate
                {
                    SetNightLightsEnabled(
                        !GlassGlobeSettingsState.NightLightsEnabled);
                });
            if (GlassGlobeSettingsState.NightLightsEnabled)
            {
                DrawOpacityRow(
                    "Earth at Night transparency",
                    GlassGlobeSettingsState.NightLightsOpacity,
                    delegate (float value) { SetNightLightsOpacity(value); },
                    268f);
            }

            GUILayout.Space(18f);
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

            GUILayout.Space(18f);
            GUILayout.Label("On-screen information", headingStyle);
            GUILayout.Space(8f);
            DrawCheckbox(
                "Main information and controls",
                GlassGlobeSettingsState.MainHudVisible,
                delegate { GlassGlobeSettingsState.SetMainHudVisible(!GlassGlobeSettingsState.MainHudVisible); });
            GUILayout.Space(6f);
            DrawCheckbox(
                "Set North",
                GlassGlobeSettingsState.ShowSetNorthButton,
                delegate { GlassGlobeSettingsState.SetShowSetNorthButton(!GlassGlobeSettingsState.ShowSetNorthButton); });

            // These shells sit below an opaque Blue Marble surface. Keep their
            // saved choices visible, but gray and block them until Blue Marble
            // is unchecked so their selections return automatically.
            GUILayout.Space(18f);
            DrawCheckbox(
                "Water",
                GlassGlobeSettingsState.WaterArtEnabled,
                delegate { SetWaterArtEnabled(!GlassGlobeSettingsState.WaterArtEnabled); },
                !blueMarbleSelected);
            GUILayout.Space(6f);
            DrawCheckbox(
                "Land",
                GlassGlobeSettingsState.LandArtEnabled,
                delegate { SetLandArtEnabled(!GlassGlobeSettingsState.LandArtEnabled); },
                !blueMarbleSelected);
            GUILayout.Space(6f);
            DrawCheckbox(
                "Clouds (satellite)",
                GlassGlobeSettingsState.WeatherCloudsEnabled,
                delegate { SetWeatherCloudsEnabled(!GlassGlobeSettingsState.WeatherCloudsEnabled); },
                !blueMarbleSelected);
            GUILayout.Space(6f);
            DrawCheckbox(
                "Rain / snow radar",
                GlassGlobeSettingsState.WeatherRadarEnabled,
                delegate { SetWeatherRadarEnabled(!GlassGlobeSettingsState.WeatherRadarEnabled); },
                !blueMarbleSelected);

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

            GUILayout.Space(18f);
            DrawCheckbox(
                "Country lines",
                GlassGlobeSettingsState.CountryLinesVisible,
                delegate
                {
                    GlassGlobeSettingsState.SetCountryLinesVisible(
                        !GlassGlobeSettingsState.CountryLinesVisible);
                    RefreshDisplaySettings();
                });
            GUILayout.Space(8f);
            GUILayout.Label("Country outline color", headingStyle);
            // Keep this six-color choice grid as the final content on the page.
            DrawColorChoices(true);
        }

        private void DrawThicknessRow(string label, float value, Action<float> setter, float step = 0.25f)
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + Mathf.RoundToInt(value * 100f) + "%", bodyStyle, GUILayout.Width(260f));
            float capturedValue = value;
            DrawButton("-", delegate { setter(capturedValue - step); }, 46f);
            GUILayout.Space(6f);
            DrawButton("+", delegate { setter(capturedValue + step); }, 46f);
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

        private void DrawOpacityRow(string label, float value, Action<float> setter, float labelWidth = 220f)
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                string.Format("{0}: {1:0}%", label, value * 100f),
                bodyStyle,
                GUILayout.Width(labelWidth));
            float capturedValue = value;
            DrawButton("-", delegate { setter(capturedValue - 0.1f); }, 38f);
            GUILayout.Space(6f);
            DrawButton("+", delegate { setter(capturedValue + 0.1f); }, 38f);
            GUILayout.EndHorizontal();
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

            DrawButton("Jump to current center point", JumpToCenterPoint, 46f);
            GUILayout.Space(8f);

            DrawCheckbox(
                "Show country names on Earth",
                GlassGlobeSettingsState.CountryLabelsVisible,
                delegate { SetCountryLabelsVisible(!GlassGlobeSettingsState.CountryLabelsVisible); });

            GUILayout.Space(12f);
            GUILayout.Label("View from a city or country", headingStyle);
            GUILayout.Label("Type any city, or a country to see its largest cities.", bodyStyle);
            viewpointSearch = GUILayout.TextField(
                viewpointSearch ?? string.Empty,
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(38f)));
            BuildFilteredChoices();

            int visibleChoiceCount = Mathf.Min(MaxViewpointResults, filteredChoices.Count);
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
            DrawButton("Back to Viewport", BackToViewpoint, 44f);
            GUILayout.Space(8f);
            DrawButton("Back to Settings", delegate { ShowPage(SettingsPage.Settings); }, 44f);
            GUILayout.EndHorizontal();
        }

        private void DrawSettingsCategoryRow(
            string label,
            bool enabled,
            Action toggleAction,
            Action openAction)
        {
            float controlHeight = GlassGlobeUi.GetInteractiveControlHeight(52f);
            GUILayout.BeginHorizontal();

            bool checkboxClicked = GUILayout.Button(
                enabled ? "[x]" : "[ ]",
                buttonStyle,
                GUILayout.Width(64f),
                GUILayout.Height(controlHeight));
            Rect checkboxRect = GUILayoutUtility.GetLastRect();
            RegisterLocalTouch(checkboxRect, toggleAction);
            if (checkboxClicked && !Application.isMobilePlatform)
            {
                toggleAction();
            }

            GUILayout.Space(8f);
            GUILayout.Label(
                label,
                headingStyle,
                GUILayout.Height(controlHeight),
                GUILayout.ExpandWidth(true));

            if (openAction != null)
            {
                GUILayout.Space(8f);
                bool openClicked = GUILayout.Button(
                    "Open",
                    buttonStyle,
                    GUILayout.Width(92f),
                    GUILayout.Height(controlHeight));
                Rect openRect = GUILayoutUtility.GetLastRect();
                RegisterLocalTouch(openRect, openAction);
                if (openClicked && !Application.isMobilePlatform)
                {
                    openAction();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawCheckbox(
            string label,
            bool value,
            Action action,
            bool interactable = true)
        {
            DrawButton(
                (value ? "[x] " : "[ ] ") + label,
                action,
                42f,
                interactable);
        }

        private void DrawButton(
            string text,
            Action action,
            float height,
            bool interactable = true,
            GUIStyle style = null)
        {
            bool previousEnabled = GUI.enabled;
            bool canInteract = previousEnabled && interactable;
            GUI.enabled = canInteract;
            bool clicked = GUILayout.Button(
                text,
                style ?? buttonStyle,
                GUILayout.Height(GlassGlobeUi.GetInteractiveControlHeight(height)));
            GUI.enabled = previousEnabled;

            Rect localRect = GUILayoutUtility.GetLastRect();
            if (canInteract)
            {
                RegisterLocalTouch(localRect, action);
                if (clicked && !Application.isMobilePlatform)
                {
                    action();
                }
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

            primaryButtonStyle = new GUIStyle(buttonStyle);
            primaryButtonStyle.fontSize = 23;
            primaryButtonStyle.fontStyle = FontStyle.Bold;

            // The viewport season button is twice the size of a settings
            // button, so its label is scaled to match instead of sitting small
            // in the middle of it.
            seasonButtonStyle = new GUIStyle(buttonStyle);
            seasonButtonStyle.fontSize = 40;

            entryButtonStyle = new GUIStyle(buttonStyle);
        }
    }
}

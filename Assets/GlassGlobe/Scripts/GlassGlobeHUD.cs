using System.Text;
using UnityEngine;

namespace GlassGlobe
{
    public sealed class GlassGlobeHUD : MonoBehaviour
    {
        private const float LaunchNorthPromptFadeDurationSeconds = 0.45f;

        public PhonePoseSimulator phonePose;
        public PhonePoseSensors poseSensors;
        public CameraFeedRenderer cameraFeed;
        public FarSideRaycaster farSideRaycaster;
        public CountryBorderRenderer borderRenderer;
        public bool showHud = true;
        public Rect panelRect = new Rect(20f, 20f, 460f, 330f);

        private GlassGlobeSettingsController settingsController;
        private GUIStyle titleStyle;
        private GUIStyle readoutStyle;
        private GUIStyle labelStyle;
        private GUIStyle countryBannerStyle;
        private GUIStyle launchNorthMessageStyle;
        private GUIStyle launchNorthButtonStyle;
        private GUIStyle launchNorthStatusStyle;
        private Rect tiltTouchRect;
        private Rect headingTouchRect;
        private Rect straightDownTouchRect;
        private Rect fortyFiveTouchRect;
        private Rect nearHorizonTouchRect;
        private Rect alignTouchRect;
        private Rect launchNorthPromptTouchRect;
        private Rect launchNorthPromptInteractiveRect;
        private string alignmentStatusText = string.Empty;
        private float alignmentStatusUntil;
        private string launchNorthPromptStatusText = string.Empty;
        private float launchNorthPromptFadeStartedAt = -1f;
        private bool launchNorthPromptDismissed;
        private Rect activePanelRect;
        private float activeUiScale = 1f;
        private TouchSlider activeTouchSlider;
        private SensorTouchAction activeSensorTouchAction;
        private int trackedTouchFingerId = -1;
        private Vector2 touchStartScreenPoint;
        private bool touchDragged;
        private GlassGlobePortraitUi.Rotation lastUiRotation =
            GlassGlobePortraitUi.Rotation.Portrait;

        private enum TouchSlider
        {
            None,
            Tilt,
            Heading
        }

        private enum SensorTouchAction
        {
            None,
            LaunchSetNorth,
            SetNorth
        }

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            if (Application.isMobilePlatform)
            {
                Input.simulateMouseWithTouches = true;
            }

            settingsController = GlassGlobeSettingsController.EnsureInstance(this);
        }

        private void Update()
        {
            ResolveReferences();
            GlassGlobePortraitUi.Rotation uiRotation =
                GlassGlobePortraitUi.CurrentRotation;
            if (uiRotation != lastUiRotation)
            {
                lastUiRotation = uiRotation;
                ResetTrackedTouch();
            }

            if (!showHud)
            {
                activeTouchSlider = TouchSlider.None;
                return;
            }

            HandleMobileTouch();
        }

        private void OnGUI()
        {
            if (!showHud)
            {
                return;
            }

            ResolveReferences();
            EnsureStyles();

            Matrix4x4 previousMatrix = GUI.matrix;
            float uiScale = GetMobileUiScale();
            activeUiScale = uiScale;
            GUI.matrix =
                GlassGlobePortraitUi.GuiMatrix *
                Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            Rect responsivePanel = panelRect;
            float logicalScreenWidth =
                GlassGlobePortraitUi.Width / uiScale;
            responsivePanel.width = Mathf.Min(
                panelRect.width,
                logicalScreenWidth - panelRect.x * 2f);
            if (Application.isMobilePlatform)
            {
                responsivePanel.x =
                    (logicalScreenWidth - responsivePanel.width) * 0.5f;
            }

            float bannerHeight = Application.isMobilePlatform ? 72f : 56f;
            bool displayEnabled = GlassGlobeSettingsState.DisplayCategoryEnabled;
            if (displayEnabled)
            {
                responsivePanel.y += bannerHeight + 8f;
            }

            if (displayEnabled)
            {
                Rect bannerRect = new Rect(
                    responsivePanel.x,
                    panelRect.y,
                    responsivePanel.width,
                    bannerHeight);
                GUI.Box(bannerRect, GetViewedCountryName(), countryBannerStyle);
            }

            activePanelRect = responsivePanel;
            if (displayEnabled && GlassGlobeSettingsState.MainHudVisible)
            {
                GUILayout.BeginArea(responsivePanel, GUI.skin.box);

                if (SensorModeActive())
                {
                    DrawSensorPanel();
                }
                else
                {
                    DrawSimulatorPanel();
                }

                GUILayout.EndArea();
            }

            DrawViewportSetNorthButton(uiScale);
            DrawLaunchNorthPrompt(uiScale);
            GUI.matrix = previousMatrix;
        }

        private string GetViewedCountryName()
        {
            if (farSideRaycaster == null)
            {
                return "Unknown";
            }

            farSideRaycaster.UpdateRaycast();
            if (!farSideRaycaster.HasIntersection || borderRenderer == null)
            {
                return "Unknown";
            }

            string name = borderRenderer.GetRegionForCoordinate(farSideRaycaster.FarSideCoordinate);
            return name.StartsWith("Nearest: ") ? name.Substring(9) : name;
        }

        private bool SensorModeActive()
        {
            return poseSensors != null && poseSensors.SensorModeActive;
        }

        private void DrawSimulatorPanel()
        {
            GUILayout.Label("GlassGlobe Preview", titleStyle);
            GUILayout.Space(4f);
            GUILayout.Label(BuildReadout(), readoutStyle);
            GUILayout.Space(8f);

            DrawSlider("Tilt", 0f, PhonePoseSimulator.MaxTiltDegrees, GetTilt(), SetTilt);
            DrawSlider("Heading", 0f, 360f, GetHeading(), SetHeading);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            bool straightDownClicked = GUILayout.Button("Straight Down", GUILayout.Height(30f));
            straightDownTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (straightDownClicked)
            {
                SetPresetStraightDown();
            }

            bool fortyFiveClicked = GUILayout.Button("45 Degree View", GUILayout.Height(30f));
            fortyFiveTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (fortyFiveClicked)
            {
                SetPresetFortyFiveDegreeView();
            }

            bool nearHorizonClicked = GUILayout.Button("Near Horizon", GUILayout.Height(30f));
            nearHorizonTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (nearHorizonClicked)
            {
                SetPresetNearHorizon();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSensorPanel()
        {
            tiltTouchRect = Rect.zero;
            headingTouchRect = Rect.zero;
            straightDownTouchRect = Rect.zero;
            fortyFiveTouchRect = Rect.zero;
            nearHorizonTouchRect = Rect.zero;

            GUILayout.Label("GlassGlobe Live", titleStyle);
            GUILayout.Space(4f);
            GUILayout.Label(BuildSensorReadout(), readoutStyle);

            if (Time.unscaledTime < alignmentStatusUntil && !string.IsNullOrEmpty(alignmentStatusText))
            {
                GUILayout.Space(4f);
                GUILayout.Label(alignmentStatusText, readoutStyle);
            }
        }

        private void DrawViewportSetNorthButton(float uiScale)
        {
            alignTouchRect = Rect.zero;
            if (!SensorModeActive() ||
                !GlassGlobeSettingsState.EffectiveShowSetNorthButton ||
                LaunchNorthPromptVisible())
            {
                return;
            }

            Rect safeArea = GlassGlobePortraitUi.SafeArea;
            float safeX = safeArea.xMin / uiScale;
            float safeWidth = safeArea.width / uiScale;
            float safeBottom = safeArea.yMax / uiScale;
            float width = Mathf.Min(240f, safeWidth - 40f);
            float height = Application.isMobilePlatform ? 64f : 46f;
            Rect buttonRect = new Rect(
                safeX + (safeWidth - width) * 0.5f,
                safeBottom - height - 24f,
                width,
                height);
            bool clicked = GUI.Button(buttonRect, "Set North");
            alignTouchRect = new Rect(
                buttonRect.x * uiScale,
                buttonRect.y * uiScale,
                buttonRect.width * uiScale,
                buttonRect.height * uiScale);
            if (clicked && !Application.isMobilePlatform)
            {
                AlignPhoneToNorth();
            }
        }

        private void DrawLaunchNorthPrompt(float uiScale)
        {
            launchNorthPromptTouchRect = Rect.zero;
            launchNorthPromptInteractiveRect = Rect.zero;
            if (!LaunchNorthPromptVisible())
            {
                return;
            }

            float alpha = 1f;
            if (launchNorthPromptFadeStartedAt >= 0f)
            {
                float fadeProgress =
                    (Time.unscaledTime - launchNorthPromptFadeStartedAt) /
                    LaunchNorthPromptFadeDurationSeconds;
                if (fadeProgress >= 1f)
                {
                    launchNorthPromptDismissed = true;
                    return;
                }

                alpha = 1f - Mathf.Clamp01(fadeProgress);
            }

            Rect safeArea = GlassGlobePortraitUi.SafeArea;
            float safeX = safeArea.xMin / uiScale;
            float safeTop = safeArea.yMin / uiScale;
            float safeWidth = safeArea.width / uiScale;
            float safeHeight = safeArea.height / uiScale;
            float contentWidth = Mathf.Min(460f, safeWidth - 40f);
            float messageHeight = 100f;
            float buttonHeight = 96f;
            float gap = 18f;
            float statusHeight = 48f;
            float contentHeight =
                messageHeight + gap + buttonHeight + statusHeight;
            float contentX =
                safeX + (safeWidth - contentWidth) * 0.5f;
            float contentY =
                safeTop + (safeHeight - contentHeight) * 0.5f;

            Rect messageRect = new Rect(
                contentX,
                contentY,
                contentWidth,
                messageHeight);
            Rect buttonRect = new Rect(
                contentX,
                messageRect.yMax + gap,
                contentWidth,
                buttonHeight);
            Rect statusRect = new Rect(
                contentX,
                buttonRect.yMax + 6f,
                contentWidth,
                statusHeight);

            launchNorthMessageStyle.normal.textColor =
                new Color(1f, 1f, 1f, alpha);
            GUI.Label(
                messageRect,
                "Set north while holding the phone upright and facing north",
                launchNorthMessageStyle);

            DrawSolidRect(
                buttonRect,
                new Color(0f, 0f, 0f, 0.18f * alpha));
            DrawRectOutline(
                buttonRect,
                2f,
                new Color(1f, 1f, 1f, 0.9f * alpha));

            launchNorthButtonStyle.normal.textColor =
                new Color(1f, 1f, 1f, alpha);
            launchNorthButtonStyle.hover.textColor =
                new Color(1f, 1f, 1f, alpha);
            launchNorthButtonStyle.active.textColor =
                new Color(0.75f, 0.95f, 1f, alpha);
            bool clicked = GUI.Button(
                buttonRect,
                "Set North",
                launchNorthButtonStyle);

            launchNorthPromptTouchRect = ScaleRect(buttonRect, uiScale);
            launchNorthPromptInteractiveRect = ScaleRect(
                new Rect(
                    contentX,
                    contentY,
                    contentWidth,
                    contentHeight),
                uiScale);

            if (!string.IsNullOrEmpty(launchNorthPromptStatusText))
            {
                launchNorthStatusStyle.normal.textColor =
                    new Color(0.88f, 0.96f, 1f, alpha);
                GUI.Label(
                    statusRect,
                    launchNorthPromptStatusText,
                    launchNorthStatusStyle);
            }

            if (clicked && !Application.isMobilePlatform)
            {
                TryCompleteLaunchNorthPrompt();
            }
        }

        private void TryCompleteLaunchNorthPrompt()
        {
            if (launchNorthPromptFadeStartedAt >= 0f)
            {
                return;
            }

            if (AlignPhoneToNorth())
            {
                launchNorthPromptStatusText = string.Empty;
                launchNorthPromptFadeStartedAt = Time.unscaledTime;
                return;
            }

            launchNorthPromptStatusText =
                "Getting orientation ready - try again in a moment.";
        }

        private bool LaunchNorthPromptVisible()
        {
            return Application.isMobilePlatform && !launchNorthPromptDismissed;
        }

        private bool AlignPhoneToNorth()
        {
            float correctionDegrees;
            if (poseSensors == null || !poseSensors.TryAlignCurrentHeadingToNorth(out correctionDegrees))
            {
                alignmentStatusText = poseSensors == null
                    ? "North not set: sensors unavailable"
                    : "North not set: " + poseSensors.OrientationStatus;
                alignmentStatusUntil = Time.unscaledTime + 3f;
                Debug.LogWarning("GlassGlobeHUD: Set North is waiting for orientation tracking.");
                return false;
            }

            alignmentStatusText = (poseSensors.ArNorthLockActive ? "ARCore north locked: " : "Gyro north locked: ") +
                correctionDegrees.ToString("+0.0;-0.0;0.0") + " deg";
            alignmentStatusUntil = Time.unscaledTime + 3f;
            Debug.Log("GlassGlobeHUD: north locked; correction=" +
                correctionDegrees.ToString("+0.0;-0.0;0.0") + " deg");
            return true;
        }

        public void SetPresetStraightDown()
        {
            ApplyPreset(0f, 0f);
        }

        public void SetPresetFortyFiveDegreeView()
        {
            ApplyPreset(45f, 120f);
        }

        public void SetPresetNearHorizon()
        {
            ApplyPreset(70f, 120f);
        }

        private void ApplyPreset(float tilt, float heading)
        {
            ResolveReferences();
            if (phonePose == null)
            {
                return;
            }

            phonePose.tiltDegrees = tilt;
            phonePose.headingDegrees = heading;
            phonePose.UsePhysicalViewportFov();
            RefreshPreview();
            Debug.Log("GlassGlobeHUD: preset applied tilt=" + tilt.ToString("0.0") + " heading=" + heading.ToString("0.0"));
        }

        private void DrawSlider(string label, float min, float max, float value, System.Action<float> setter)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(72f));
            float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220f));
            StoreSliderTouchRect(label, GUILayoutUtility.GetLastRect());
            GUILayout.Label(newValue.ToString("0.0"), labelStyle, GUILayout.Width(54f));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                setter(newValue);
            }
        }

        private void HandleMobileTouch()
        {
            if (!Application.isMobilePlatform || Input.touchCount == 0 || Input.touchCount > 1)
            {
                if (Input.touchCount > 1)
                {
                    touchDragged = true;
                    activeTouchSlider = TouchSlider.None;
                    activeSensorTouchAction = SensorTouchAction.None;
                }
                return;
            }

            if (!SensorModeActive() && phonePose == null)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);
            Vector2 screenPoint = GlassGlobePortraitUi.ScreenToUi(
                new Vector2(
                    touch.position.x,
                    Screen.height - touch.position.y));

            if (touch.phase == TouchPhase.Began)
            {
                trackedTouchFingerId = touch.fingerId;
                touchStartScreenPoint = screenPoint;
                touchDragged = false;
                activeSensorTouchAction = SensorModeActive()
                    ? FindSensorTouchAction(screenPoint)
                    : SensorTouchAction.None;

                if (!SensorModeActive())
                {
                    if (straightDownTouchRect.Contains(screenPoint))
                    {
                        SetPresetStraightDown();
                        return;
                    }

                    if (fortyFiveTouchRect.Contains(screenPoint))
                    {
                        SetPresetFortyFiveDegreeView();
                        return;
                    }

                    if (nearHorizonTouchRect.Contains(screenPoint))
                    {
                        SetPresetNearHorizon();
                        return;
                    }
                }

                if (!SensorModeActive() && tiltTouchRect.Contains(screenPoint))
                {
                    activeTouchSlider = TouchSlider.Tilt;
                }
                else if (!SensorModeActive() && headingTouchRect.Contains(screenPoint))
                {
                    activeTouchSlider = TouchSlider.Heading;
                }
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

            if (activeTouchSlider != TouchSlider.None &&
                (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
            {
                ApplyTouchSlider(screenPoint);
            }

            if (touch.phase == TouchPhase.Ended)
            {
                if (!touchDragged &&
                    activeSensorTouchAction != SensorTouchAction.None &&
                    GetSensorTouchRect(activeSensorTouchAction).Contains(screenPoint))
                {
                    ExecuteSensorTouchAction(activeSensorTouchAction);
                }

                ResetTrackedTouch();
            }
            else if (touch.phase == TouchPhase.Canceled)
            {
                ResetTrackedTouch();
            }
        }

        private SensorTouchAction FindSensorTouchAction(Vector2 screenPoint)
        {
            if (launchNorthPromptTouchRect.Contains(screenPoint))
            {
                return SensorTouchAction.LaunchSetNorth;
            }

            if (alignTouchRect.Contains(screenPoint)) return SensorTouchAction.SetNorth;
            return SensorTouchAction.None;
        }

        private Rect GetSensorTouchRect(SensorTouchAction action)
        {
            switch (action)
            {
                case SensorTouchAction.LaunchSetNorth:
                    return launchNorthPromptTouchRect;
                case SensorTouchAction.SetNorth: return alignTouchRect;
                default: return Rect.zero;
            }
        }

        private void ExecuteSensorTouchAction(SensorTouchAction action)
        {
            switch (action)
            {
                case SensorTouchAction.LaunchSetNorth:
                    TryCompleteLaunchNorthPrompt();
                    break;
                case SensorTouchAction.SetNorth:
                    AlignPhoneToNorth();
                    break;
            }
        }

        public bool IsInteractiveScreenPoint(Vector2 screenPoint)
        {
            Vector2 uiPoint =
                GlassGlobePortraitUi.ScreenToUi(screenPoint);
            return IsInteractiveUiPoint(uiPoint);
        }

        public bool IsInteractiveUiPoint(Vector2 uiPoint)
        {
            return launchNorthPromptInteractiveRect.Contains(uiPoint) ||
                (GlassGlobeSettingsState.EffectiveShowSetNorthButton &&
                    alignTouchRect.Contains(uiPoint));
        }

        private static Rect ScaleRect(Rect rect, float scale)
        {
            return new Rect(
                rect.x * scale,
                rect.y * scale,
                rect.width * scale,
                rect.height * scale);
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawRectOutline(
            Rect rect,
            float thickness,
            Color color)
        {
            DrawSolidRect(
                new Rect(rect.x, rect.y, rect.width, thickness),
                color);
            DrawSolidRect(
                new Rect(
                    rect.x,
                    rect.yMax - thickness,
                    rect.width,
                    thickness),
                color);
            DrawSolidRect(
                new Rect(rect.x, rect.y, thickness, rect.height),
                color);
            DrawSolidRect(
                new Rect(
                    rect.xMax - thickness,
                    rect.y,
                    thickness,
                    rect.height),
                color);
        }

        private void ResetTrackedTouch()
        {
            trackedTouchFingerId = -1;
            touchDragged = false;
            activeTouchSlider = TouchSlider.None;
            activeSensorTouchAction = SensorTouchAction.None;
        }

        private void StoreButtonTouchRect(ref Rect touchRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                touchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            }
        }

        private void ApplyTouchSlider(Vector2 screenPoint)
        {
            Rect rect;
            float min;
            float max;

            switch (activeTouchSlider)
            {
                case TouchSlider.Tilt:
                    rect = tiltTouchRect;
                    min = 0f;
                    max = PhonePoseSimulator.MaxTiltDegrees;
                    break;
                case TouchSlider.Heading:
                    rect = headingTouchRect;
                    min = 0f;
                    max = 360f;
                    break;
                default:
                    return;
            }

            float normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, screenPoint.x);
            float value = Mathf.Lerp(min, max, normalized);
            if (activeTouchSlider == TouchSlider.Tilt)
            {
                SetTilt(value);
            }
            else if (activeTouchSlider == TouchSlider.Heading)
            {
                SetHeading(value);
            }
        }

        private void StoreSliderTouchRect(string label, Rect localRect)
        {
            Rect screenRect = ToScreenRect(localRect);
            if (label == "Tilt")
            {
                tiltTouchRect = screenRect;
            }
            else if (label == "Heading")
            {
                headingTouchRect = screenRect;
            }
        }

        private Rect ToScreenRect(Rect localRect)
        {
            return new Rect(
                (activePanelRect.x + localRect.x) * activeUiScale,
                (activePanelRect.y + localRect.y) * activeUiScale,
                localRect.width * activeUiScale,
                localRect.height * activeUiScale);
        }

        private static float GetMobileUiScale()
        {
            return GlassGlobeUi.GetMobileUiScale();
        }

        private float GetTilt()
        {
            return phonePose != null ? phonePose.tiltDegrees : 0f;
        }

        private float GetHeading()
        {
            return phonePose != null ? phonePose.headingDegrees : 0f;
        }

        private void SetTilt(float value)
        {
            if (phonePose == null)
            {
                return;
            }

            phonePose.tiltDegrees = value;
            RefreshPreview();
        }

        private void SetHeading(float value)
        {
            if (phonePose == null)
            {
                return;
            }

            phonePose.headingDegrees = value;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            phonePose.ApplyPose();
            if (farSideRaycaster != null)
            {
                farSideRaycaster.UpdateRaycast();
            }
        }

        private string BuildReadout()
        {
            if (phonePose == null)
            {
                return "Phone pose simulator missing.";
            }

            string targetText = "No sphere intersection";
            string regionText = "Unknown";

            if (farSideRaycaster != null)
            {
                farSideRaycaster.UpdateRaycast();
                if (farSideRaycaster.HasIntersection)
                {
                    GeoCoordinate target = farSideRaycaster.FarSideCoordinate;
                    targetText = target.ToString();
                    if (borderRenderer != null)
                    {
                        regionText = borderRenderer.GetRegionForCoordinate(target);
                    }
                }
            }

            StringBuilder readout = new StringBuilder();
            if (GlassGlobeSettingsState.EffectiveShowViewedFromName)
            {
                readout.Append("Viewed From: ").AppendLine(GlassGlobeSettingsState.ViewedFromLabel);
            }

            if (!GlassGlobeSettingsState.EffectiveHideUserCoordinates)
            {
                readout.Append("User Lat/Lon: ").AppendLine(phonePose.userCoordinate.ToString());
            }

            if (!GlassGlobeSettingsState.EffectiveHideFarSideCoordinates)
            {
                readout.Append("Far-Side Lat/Lon: ").AppendLine(targetText);
            }

            if (!GlassGlobeSettingsState.EffectiveHideViewedRegion)
            {
                readout.Append("Country/Region: ").AppendLine(regionText);
            }

            readout.AppendFormat(
                "Tilt: {0:0.0} deg   Heading: {1:0.0} deg   FOV: {2:0.0} deg\n",
                phonePose.tiltDegrees,
                phonePose.headingDegrees,
                phonePose.cameraFovDegrees);
            readout.AppendFormat(
                "Eye-To-Phone: {0:0.0} in   Physical FOV: {1:0.0} deg",
                phonePose.eyeToPhoneDistanceInches,
                phonePose.PhysicalViewportFovDegrees);
            return readout.ToString();
        }

        private string BuildSensorReadout()
        {
            string targetText = "No sphere intersection";
            string regionText = "Unknown";

            if (farSideRaycaster != null)
            {
                farSideRaycaster.UpdateRaycast();
                if (farSideRaycaster.HasIntersection)
                {
                    GeoCoordinate target = farSideRaycaster.FarSideCoordinate;
                    targetText = target.ToString();
                    if (borderRenderer != null)
                    {
                        regionText = borderRenderer.GetRegionForCoordinate(target);
                    }
                }
            }

            string feedText = cameraFeed != null ? cameraFeed.FeedStatus : "n/a";
            StringBuilder readout = new StringBuilder();
            readout.Append(poseSensors.LocationStatus).Append("   Camera: ").AppendLine(feedText);

            if (GlassGlobeSettingsState.EffectiveShowViewedFromName)
            {
                readout.Append("Viewed From: ").AppendLine(GlassGlobeSettingsState.ViewedFromLabel);
            }

            if (!GlassGlobeSettingsState.EffectiveHideUserCoordinates)
            {
                readout.Append("User Lat/Lon: ").Append(poseSensors.CurrentCoordinate);
                if (!GlassGlobeSettingsState.EffectiveHideLocationAccuracy && poseSensors.HasLocationFix)
                {
                    readout.AppendFormat("  (+/-{0:0}m)", poseSensors.LocationAccuracyMeters);
                }

                readout.AppendLine();
            }

            if (!GlassGlobeSettingsState.EffectiveHideFarSideCoordinates)
            {
                readout.Append("Far-Side Lat/Lon: ").AppendLine(targetText);
            }

            if (!GlassGlobeSettingsState.EffectiveHideViewedRegion)
            {
                readout.Append("Country/Region: ").AppendLine(regionText);
            }

            readout.AppendFormat(
                "Heading: {0:0.0} deg   Tilt: {1:0.0} deg\n",
                poseSensors.HeadingDegrees,
                poseSensors.TiltDegrees);
            readout.AppendFormat(
                "Compass True: {0:0.0} deg   Correction: {1:0.0} deg   Offset: {2:0.0} deg",
                poseSensors.CompassTrueHeadingDegrees,
                poseSensors.CompassCorrectionDegrees,
                poseSensors.ActiveHeadingCorrectionDegrees);
            readout.Append("\nOrientation: ").Append(poseSensors.OrientationStatus);
            return readout.ToString();
        }

        private void ResolveReferences()
        {
            if (phonePose == null)
            {
                phonePose = FindFirstObjectByType<PhonePoseSimulator>();
            }

            if (poseSensors == null)
            {
                poseSensors = FindFirstObjectByType<PhonePoseSensors>();
            }

            if (cameraFeed == null)
            {
                cameraFeed = FindFirstObjectByType<CameraFeedRenderer>();
            }

            if (farSideRaycaster == null)
            {
                farSideRaycaster = FindFirstObjectByType<FarSideRaycaster>();
            }

            if (borderRenderer == null)
            {
                borderRenderer = FindFirstObjectByType<CountryBorderRenderer>();
            }

            if (settingsController == null)
            {
                settingsController = GlassGlobeSettingsController.EnsureInstance(this);
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 18;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.9f, 0.97f, 1f, 1f);

            readoutStyle = new GUIStyle(GUI.skin.label);
            readoutStyle.fontSize = 13;
            readoutStyle.normal.textColor = new Color(0.9f, 0.97f, 1f, 1f);
            readoutStyle.wordWrap = true;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = new Color(0.9f, 0.97f, 1f, 1f);

            countryBannerStyle = new GUIStyle(GUI.skin.box);
            countryBannerStyle.fontSize = Application.isMobilePlatform ? 30 : 24;
            countryBannerStyle.fontStyle = FontStyle.Bold;
            countryBannerStyle.alignment = TextAnchor.MiddleCenter;
            countryBannerStyle.wordWrap = true;
            countryBannerStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, 1f);

            launchNorthMessageStyle = new GUIStyle(GUI.skin.label);
            launchNorthMessageStyle.fontSize = 23;
            launchNorthMessageStyle.fontStyle = FontStyle.Bold;
            launchNorthMessageStyle.alignment = TextAnchor.MiddleCenter;
            launchNorthMessageStyle.wordWrap = true;

            launchNorthButtonStyle = new GUIStyle(GUI.skin.button);
            launchNorthButtonStyle.fontSize = 27;
            launchNorthButtonStyle.fontStyle = FontStyle.Bold;
            launchNorthButtonStyle.alignment = TextAnchor.MiddleCenter;
            launchNorthButtonStyle.normal.background = null;
            launchNorthButtonStyle.hover.background = null;
            launchNorthButtonStyle.active.background = null;
            launchNorthButtonStyle.focused.background = null;

            launchNorthStatusStyle = new GUIStyle(GUI.skin.label);
            launchNorthStatusStyle.fontSize = 15;
            launchNorthStatusStyle.alignment = TextAnchor.UpperCenter;
            launchNorthStatusStyle.wordWrap = true;
        }
    }
}

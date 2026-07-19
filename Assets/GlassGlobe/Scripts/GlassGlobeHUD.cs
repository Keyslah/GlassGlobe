using System.Text;
using UnityEngine;

namespace GlassGlobe
{
    public sealed class GlassGlobeHUD : MonoBehaviour
    {
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
        private Rect tiltTouchRect;
        private Rect headingTouchRect;
        private Rect fovTouchRect;
        private Rect straightDownTouchRect;
        private Rect fortyFiveTouchRect;
        private Rect nearHorizonTouchRect;
        private Rect alignTouchRect;
        private Rect nudgeMinusFiveTouchRect;
        private Rect nudgeMinusOneTouchRect;
        private Rect nudgePlusOneTouchRect;
        private Rect nudgePlusFiveTouchRect;
        private Rect arToggleTouchRect;
        private string alignmentStatusText = string.Empty;
        private float alignmentStatusUntil;
        private Rect activePanelRect;
        private float activeUiScale = 1f;
        private TouchSlider activeTouchSlider;
        private SensorTouchAction activeSensorTouchAction;
        private int trackedTouchFingerId = -1;
        private Vector2 touchStartScreenPoint;
        private bool touchDragged;

        private enum TouchSlider
        {
            None,
            Tilt,
            Heading,
            Fov
        }

        private enum SensorTouchAction
        {
            None,
            SetNorth,
            MinusFive,
            MinusOne,
            PlusOne,
            PlusFive,
            ToggleCamera
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
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            Rect responsivePanel = panelRect;
            responsivePanel.width = Mathf.Min(panelRect.width, Screen.width / uiScale - panelRect.x * 2f);
            activePanelRect = responsivePanel;
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
            GUI.matrix = previousMatrix;
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

            DrawSlider("Tilt", 0f, 72f, GetTilt(), SetTilt);
            DrawSlider("Heading", 0f, 360f, GetHeading(), SetHeading);
            DrawSlider("FOV", 20f, 75f, GetFov(), SetFov);

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
            GUILayout.Space(8f);

            DrawSlider("FOV", 20f, 75f, GetFov(), SetFov);

            GUILayout.Space(8f);
            float sensorButtonHeight = Application.isMobilePlatform ? 48f : 30f;
            GUILayout.BeginHorizontal();
            bool alignClicked = GUILayout.Button("Set North", GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref alignTouchRect);
            if (alignClicked && !Application.isMobilePlatform)
            {
                AlignPhoneToNorth();
            }

            bool minusFiveClicked = GUILayout.Button("-5", GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref nudgeMinusFiveTouchRect);
            if (minusFiveClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(-5f);
            }

            bool minusOneClicked = GUILayout.Button("-1", GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref nudgeMinusOneTouchRect);
            if (minusOneClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(-1f);
            }

            bool plusOneClicked = GUILayout.Button("+1", GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref nudgePlusOneTouchRect);
            if (plusOneClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(1f);
            }

            bool plusFiveClicked = GUILayout.Button("+5", GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref nudgePlusFiveTouchRect);
            if (plusFiveClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(5f);
            }

            bool arClicked = GUILayout.Button(
                GlassGlobeSettingsState.CameraFeedEnabled ? "AR On" : "AR Off",
                GUILayout.Height(sensorButtonHeight));
            StoreButtonTouchRect(ref arToggleTouchRect);
            if (arClicked && !Application.isMobilePlatform)
            {
                ToggleCameraFeed();
            }

            GUILayout.EndHorizontal();

            if (Time.unscaledTime < alignmentStatusUntil && !string.IsNullOrEmpty(alignmentStatusText))
            {
                GUILayout.Space(4f);
                GUILayout.Label(alignmentStatusText, readoutStyle);
            }
        }

        private void AlignPhoneToNorth()
        {
            float correctionDegrees;
            if (poseSensors == null || !poseSensors.TryAlignCurrentHeadingToNorth(out correctionDegrees))
            {
                alignmentStatusText = poseSensors != null && !poseSensors.GameRotationAvailable
                    ? "North not set: gyro-only sensor unavailable"
                    : "North not set: hold phone upright and wait for gyro";
                alignmentStatusUntil = Time.unscaledTime + 3f;
                Debug.LogWarning("GlassGlobeHUD: Set North needs the live gyro-only orientation and an upright phone.");
                return;
            }

            alignmentStatusText = "North locked; compass ignored: " +
                correctionDegrees.ToString("+0.0;-0.0;0.0") + " deg";
            alignmentStatusUntil = Time.unscaledTime + 3f;
            Debug.Log("GlassGlobeHUD: gyro north locked; compass ignored; correction=" +
                correctionDegrees.ToString("+0.0;-0.0;0.0") + " deg");
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
            if (!Application.isMobilePlatform || Input.touchCount == 0)
            {
                return;
            }

            if (!SensorModeActive() && phonePose == null)
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
                else if (fovTouchRect.Contains(screenPoint))
                {
                    activeTouchSlider = TouchSlider.Fov;
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
            if (alignTouchRect.Contains(screenPoint)) return SensorTouchAction.SetNorth;
            if (nudgeMinusFiveTouchRect.Contains(screenPoint)) return SensorTouchAction.MinusFive;
            if (nudgeMinusOneTouchRect.Contains(screenPoint)) return SensorTouchAction.MinusOne;
            if (nudgePlusOneTouchRect.Contains(screenPoint)) return SensorTouchAction.PlusOne;
            if (nudgePlusFiveTouchRect.Contains(screenPoint)) return SensorTouchAction.PlusFive;
            if (arToggleTouchRect.Contains(screenPoint)) return SensorTouchAction.ToggleCamera;
            return SensorTouchAction.None;
        }

        private Rect GetSensorTouchRect(SensorTouchAction action)
        {
            switch (action)
            {
                case SensorTouchAction.SetNorth: return alignTouchRect;
                case SensorTouchAction.MinusFive: return nudgeMinusFiveTouchRect;
                case SensorTouchAction.MinusOne: return nudgeMinusOneTouchRect;
                case SensorTouchAction.PlusOne: return nudgePlusOneTouchRect;
                case SensorTouchAction.PlusFive: return nudgePlusFiveTouchRect;
                case SensorTouchAction.ToggleCamera: return arToggleTouchRect;
                default: return Rect.zero;
            }
        }

        private void ExecuteSensorTouchAction(SensorTouchAction action)
        {
            switch (action)
            {
                case SensorTouchAction.SetNorth:
                    AlignPhoneToNorth();
                    break;
                case SensorTouchAction.MinusFive:
                    poseSensors.NudgeHeading(-5f);
                    break;
                case SensorTouchAction.MinusOne:
                    poseSensors.NudgeHeading(-1f);
                    break;
                case SensorTouchAction.PlusOne:
                    poseSensors.NudgeHeading(1f);
                    break;
                case SensorTouchAction.PlusFive:
                    poseSensors.NudgeHeading(5f);
                    break;
                case SensorTouchAction.ToggleCamera:
                    ToggleCameraFeed();
                    break;
            }
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
                    max = 72f;
                    break;
                case TouchSlider.Heading:
                    rect = headingTouchRect;
                    min = 0f;
                    max = 360f;
                    break;
                case TouchSlider.Fov:
                    rect = fovTouchRect;
                    min = 20f;
                    max = 75f;
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
            else
            {
                SetFov(value);
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
            else if (label == "FOV")
            {
                fovTouchRect = screenRect;
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

        private float GetTilt()
        {
            return phonePose != null ? phonePose.tiltDegrees : 0f;
        }

        private float GetHeading()
        {
            return phonePose != null ? phonePose.headingDegrees : 0f;
        }

        private float GetFov()
        {
            if (SensorModeActive())
            {
                return poseSensors.cameraFovDegrees;
            }

            return phonePose != null ? phonePose.cameraFovDegrees : 0f;
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

        private void SetFov(float value)
        {
            if (SensorModeActive())
            {
                poseSensors.cameraFovDegrees = value;
                return;
            }

            if (phonePose == null)
            {
                return;
            }

            phonePose.cameraFovDegrees = value;
            RefreshPreview();
        }

        private void ToggleCameraFeed()
        {
            bool desired = !GlassGlobeSettingsState.CameraFeedEnabled;
            GlassGlobeSettingsState.SetCameraFeedEnabled(desired);
            if (cameraFeed != null)
            {
                cameraFeed.SetFeedWanted(desired);
            }
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
            if (GlassGlobeSettingsState.ShowViewedFromName)
            {
                readout.Append("Viewed From: ").AppendLine(GlassGlobeSettingsState.ViewedFromLabel);
            }

            if (!GlassGlobeSettingsState.HideUserCoordinates)
            {
                readout.Append("User Lat/Lon: ").AppendLine(phonePose.userCoordinate.ToString());
            }

            if (!GlassGlobeSettingsState.HideFarSideCoordinates)
            {
                readout.Append("Far-Side Lat/Lon: ").AppendLine(targetText);
            }

            if (!GlassGlobeSettingsState.HideViewedRegion)
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

            if (GlassGlobeSettingsState.ShowViewedFromName)
            {
                readout.Append("Viewed From: ").AppendLine(GlassGlobeSettingsState.ViewedFromLabel);
            }

            if (!GlassGlobeSettingsState.HideUserCoordinates)
            {
                readout.Append("User Lat/Lon: ").Append(poseSensors.CurrentCoordinate);
                if (!GlassGlobeSettingsState.HideLocationAccuracy && poseSensors.HasLocationFix)
                {
                    readout.AppendFormat("  (+/-{0:0}m)", poseSensors.LocationAccuracyMeters);
                }

                readout.AppendLine();
            }

            if (!GlassGlobeSettingsState.HideFarSideCoordinates)
            {
                readout.Append("Far-Side Lat/Lon: ").AppendLine(targetText);
            }

            if (!GlassGlobeSettingsState.HideViewedRegion)
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
        }
    }
}

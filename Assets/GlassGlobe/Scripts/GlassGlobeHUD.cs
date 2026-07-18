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
        private Rect activePanelRect;
        private float activeUiScale = 1f;
        private TouchSlider activeTouchSlider;

        private enum TouchSlider
        {
            None,
            Tilt,
            Heading,
            Fov
        }

        private void Update()
        {
            ResolveReferences();
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
            float uiScale = Application.isMobilePlatform
                ? Mathf.Clamp(Screen.width / 540f, 1f, 2f)
                : 1f;
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
            GUILayout.BeginHorizontal();
            bool alignClicked = GUILayout.Button("Align", GUILayout.Height(30f));
            alignTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (alignClicked && !Application.isMobilePlatform)
            {
                poseSensors.SnapAlignToCompass();
            }

            bool minusFiveClicked = GUILayout.Button("-5", GUILayout.Height(30f));
            nudgeMinusFiveTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (minusFiveClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(-5f);
            }

            bool minusOneClicked = GUILayout.Button("-1", GUILayout.Height(30f));
            nudgeMinusOneTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (minusOneClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(-1f);
            }

            bool plusOneClicked = GUILayout.Button("+1", GUILayout.Height(30f));
            nudgePlusOneTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (plusOneClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(1f);
            }

            bool plusFiveClicked = GUILayout.Button("+5", GUILayout.Height(30f));
            nudgePlusFiveTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (plusFiveClicked && !Application.isMobilePlatform)
            {
                poseSensors.NudgeHeading(5f);
            }

            bool arClicked = GUILayout.Button(cameraFeed != null && cameraFeed.FeedActive ? "AR On" : "AR Off", GUILayout.Height(30f));
            arToggleTouchRect = ToScreenRect(GUILayoutUtility.GetLastRect());
            if (arClicked && !Application.isMobilePlatform && cameraFeed != null)
            {
                cameraFeed.ToggleFeed();
            }

            GUILayout.EndHorizontal();
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
                if (SensorModeActive())
                {
                    if (alignTouchRect.Contains(screenPoint))
                    {
                        poseSensors.SnapAlignToCompass();
                        return;
                    }

                    if (nudgeMinusFiveTouchRect.Contains(screenPoint))
                    {
                        poseSensors.NudgeHeading(-5f);
                        return;
                    }

                    if (nudgeMinusOneTouchRect.Contains(screenPoint))
                    {
                        poseSensors.NudgeHeading(-1f);
                        return;
                    }

                    if (nudgePlusOneTouchRect.Contains(screenPoint))
                    {
                        poseSensors.NudgeHeading(1f);
                        return;
                    }

                    if (nudgePlusFiveTouchRect.Contains(screenPoint))
                    {
                        poseSensors.NudgeHeading(5f);
                        return;
                    }

                    if (cameraFeed != null && arToggleTouchRect.Contains(screenPoint))
                    {
                        cameraFeed.ToggleFeed();
                        return;
                    }
                }
                else
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

            if (activeTouchSlider != TouchSlider.None &&
                (touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary))
            {
                ApplyTouchSlider(screenPoint);
            }

            if (touch.phase == TouchPhase.Ended)
            {
                activeTouchSlider = TouchSlider.None;
            }
            else if (touch.phase == TouchPhase.Canceled)
            {
                activeTouchSlider = TouchSlider.None;
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

            return string.Format(
                "User Lat/Lon: {0}\nFar-Side Lat/Lon: {1}\nCountry/Region: {2}\nTilt: {3:0.0} deg   Heading: {4:0.0} deg   FOV: {5:0.0} deg\nEye-To-Phone: {6:0.0} in   Physical FOV: {7:0.0} deg",
                phonePose.userCoordinate,
                targetText,
                regionText,
                phonePose.tiltDegrees,
                phonePose.headingDegrees,
                phonePose.cameraFovDegrees,
                phonePose.eyeToPhoneDistanceInches,
                phonePose.PhysicalViewportFovDegrees);
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

            return string.Format(
                "{0}   Camera: {1}\nUser Lat/Lon: {2}  (+/-{3:0}m)\nFar-Side Lat/Lon: {4}\nCountry/Region: {5}\nHeading: {6:0.0} deg   Tilt: {7:0.0} deg\nCompass True: {8:0.0} deg   Correction: {9:0.0} deg   Offset: {10:0.0} deg",
                poseSensors.LocationStatus,
                feedText,
                poseSensors.CurrentCoordinate,
                poseSensors.LocationAccuracyMeters,
                targetText,
                regionText,
                poseSensors.HeadingDegrees,
                poseSensors.TiltDegrees,
                poseSensors.CompassTrueHeadingDegrees,
                poseSensors.CompassCorrectionDegrees,
                poseSensors.headingOffsetDegrees);
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

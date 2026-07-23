using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Sky-alignment flow (Set North, Sun/Moon capture) for the settings
    /// controller. State lives in GlassGlobeSettingsController.cs.
    /// </summary>
    public sealed partial class GlassGlobeSettingsController
    {
        private void DrawOrientPage()
        {
            DrawCategoryHeader("Orient");
            GUILayout.Space(14f);
            GUILayout.Label("Align with the sky", headingStyle);
            GUILayout.Label(
                "Set north directly, or use the real Sun or Moon to align the ARCore orientation lock.",
                bodyStyle);
            GUILayout.Space(10f);

            bool sensorsActive = poseSensors != null && poseSensors.SensorModeActive;
            if (!sensorsActive)
            {
                GUILayout.Label("Alignment needs the phone's live sensors, so it is available in the device build only.", statusStyle);
                return;
            }

            GUILayout.Label("Quick north alignment", headingStyle);
            GUILayout.Label(
                "Hold the phone upright and aim the center dot toward true north, then tap the button. ARCore keeps that heading locked using camera and motion tracking. Set north again whenever you want to reset it.",
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
                "Point the center dot at the real Sun or Moon and capture. This uses your GPS position and the body's current sky position to correct the same ARCore heading lock. Never look directly at the Sun - watch the screen only.",
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
                    poseSensors.ArNorthLockActive
                        ? "ARCore north lock: ON"
                        : poseSensors.GyroNorthLockActive
                            ? "Gyro fallback north lock: ON"
                            : "North lock: OFF",
                    poseSensors.ActiveHeadingCorrectionDegrees,
                    sunAzimuth,
                    sunAltitude,
                    moonAzimuth,
                    moonAltitude),
                bodyStyle);
        }

        private void AlignPhoneToNorth()
        {
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                orientStatusMessage = "Enable Orient on the Settings page before setting north.";
                return;
            }

            if (poseSensors == null)
            {
                orientStatusMessage = "Sensors unavailable.";
                return;
            }

            float correctionDegrees;
            if (!poseSensors.TryAlignCurrentHeadingToNorth(out correctionDegrees))
            {
                orientStatusMessage =
                    "North lock is not ready yet: " + poseSensors.OrientationStatus + ".";
                return;
            }

            orientStatusMessage =
                (poseSensors.ArNorthLockActive ? "ARCore north locked. Applied " : "Gyro fallback north locked. Applied ") +
                correctionDegrees.ToString("+0.0;-0.0;0.0") +
                " deg; set it again whenever you want to reset the heading.";
        }

        private void StartAlignment(AlignBody body)
        {
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                orientStatusMessage = "Enable Orient on the Settings page before aligning.";
                return;
            }

            if (GlassGlobeSettingsState.EffectiveViewpointOverrideEnabled)
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
            float uiScale = GlassGlobeUi.GetMobileUiScale();
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
                "Aligned to the {0}. {1} corrected the heading by {2:+0.0;-0.0;0.0} deg.",
                BodyName(alignTarget),
                poseSensors.ArNorthLockActive ? "ARCore" : "Gyro fallback",
                azimuthCorrection);
            currentPage = SettingsPage.Orient;
        }

        private void CancelAlignment()
        {
            currentPage = SettingsPage.Orient;
        }

        private void ResetHeadingOffset()
        {
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                orientStatusMessage = "Enable Orient on the Settings page before resetting alignment.";
                return;
            }

            if (poseSensors != null)
            {
                poseSensors.ResetHeadingCorrection();
                orientStatusMessage = "North lock reset. Set north or align to the Sun or Moon again when ready.";
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
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    bool onRing = distance > 0.72f && distance < 0.95f;
                    pixels[y * size + x] = onRing ? (Color32)ringColor : new Color32(0, 0, 0, 0);
                }
            }

            alignRingTexture.SetPixels32(pixels);
            alignRingTexture.Apply();
            return alignRingTexture;
        }
    }
}

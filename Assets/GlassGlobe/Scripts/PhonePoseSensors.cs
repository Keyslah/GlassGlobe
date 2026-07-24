using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Drives the phone viewport from GPS plus device orientation. Automatic mode
    /// uses Unity's magnetic attitude. Set North switches to ARCore's visual-
    /// inertial tracking space, with the custom game-rotation vector retained only
    /// as a fallback for scenes that do not contain an AR tracker.
    /// </summary>
    public sealed class PhonePoseSensors : MonoBehaviour
    {
        public PhonePoseSimulator simulator;
        public GlobeRenderer globe;
        public Camera targetCamera;
        public ArCoreOrientationTracker arCoreTracking;
        public Transform userPositionMarker;
        public LineRenderer localUpLine;
        public LineRenderer localDownLine;
        public float debugLineLength = 2.5f;

        public bool useSensorsInEditor = false;

        [Min(0f)]
        public float observerHeightUnits = 0.35f;

        [Range(PhonePoseSimulator.MinimumViewportFovDegrees, 100f)]
        public float cameraFovDegrees = PhonePoseSimulator.DefaultViewportFovDegrees;

        [Range(0f, 1f)]
        public float attitudeSmoothing = 0.35f;

        [Tooltip("Fixed world-up yaw calibration, degrees.")]
        public float headingOffsetDegrees = 0f;

        [Tooltip("Seconds for the slow compass alignment filter.")]
        public float compassAlignSeconds = 15f;

        [Tooltip("Log sensor state (including GPS coordinates) to the system log once per second. Keep off for privacy outside debugging sessions.")]
        public bool verboseLogging = false;

        public bool SensorModeActive { get; private set; }
        public bool HasLocationFix { get; private set; }
        public GeoCoordinate CurrentCoordinate { get; private set; }
        public float LocationAccuracyMeters { get; private set; }
        public string LocationStatus { get; private set; }
        public float HeadingDegrees { get; private set; }
        public float TiltDegrees { get; private set; }
        public float AttitudeHeadingRawDegrees { get; private set; }
        public float CompassTrueHeadingDegrees { get; private set; }
        public float CompassCorrectionDegrees { get; private set; }
        public bool HasAttitude { get; private set; }
        public bool GameRotationAvailable { get; private set; }
        public bool GameRotationFresh { get; private set; }
        public bool ArTrackingAvailable
        {
            get { return arCoreTracking != null && arCoreTracking.TrackingAvailable; }
        }
        public bool ArTrackingFresh
        {
            get { return arCoreTracking != null && arCoreTracking.TrackingFresh; }
        }
        public bool ArNorthLockActive
        {
            get
            {
                return GlassGlobeSettingsState.OrientCategoryEnabled &&
                    arCoreTracking != null &&
                    arCoreTracking.NorthLockActive;
            }
        }
        // Kept for the existing HUD/settings API. It now means any stable North
        // lock, with ARCore preferred and the gyro-only path used only as fallback.
        public bool GyroNorthLockActive
        {
            get
            {
                return GlassGlobeSettingsState.OrientCategoryEnabled &&
                    (ArNorthLockActive || gyroFallbackNorthLockActive);
            }
        }
        public string OrientationStatus { get; private set; }
        public float ActiveHeadingCorrectionDegrees
        {
            get
            {
                if (!GlassGlobeSettingsState.OrientCategoryEnabled)
                {
                    return 0f;
                }

                if (ArNorthLockActive)
                {
                    return arCoreTracking.HeadingCorrectionDegrees;
                }

                return gyroFallbackNorthLockActive
                    ? gyroNorthYawCorrectionDegrees
                    : headingOffsetDegrees;
            }
        }

        private bool locationStartRequested;
        private bool locationPermissionRequested;
        private bool compassCorrectionInitialized;
        private AndroidGameRotationVector gameRotationVector;
        private Quaternion currentGameDeviceInEnu = Quaternion.identity;
        private bool hasCurrentGameDeviceInEnu;
        private int currentGameDisplayRotation = -1;
        private float gyroNorthYawCorrectionDegrees;
        private bool gyroFallbackNorthLockActive;
        private bool gyroNorthLockNeedsReset;
        private Quaternion smoothedRotation = Quaternion.identity;
        private bool hasSmoothedRotation;
        private float lastLogTime;
        private float nextLocationRestartTime;
        private MilkyWayBackground milkyWayBackground;
        private SunMoonBackground sunMoonBackground;

        private void Awake()
        {
            ResolveReferences();
            GlassGlobeSettingsState.Load();
            headingOffsetDegrees = GlassGlobeSettingsState.HeadingFineOffsetDegrees;
            gyroNorthYawCorrectionDegrees = 0f;
            gyroFallbackNorthLockActive = false;

            SensorModeActive = Application.isMobilePlatform || useSensorsInEditor;
            LocationStatus = "Not started";
            OrientationStatus = "Starting orientation tracking";

            if (!SensorModeActive)
            {
                enabled = false;
                return;
            }

            if (GlassGlobeSettingsState.EffectiveViewpointOverrideEnabled)
            {
                CurrentCoordinate = GlassGlobeSettingsState.ViewpointCoordinate;
                LocationStatus = "Viewpoint override";
            }
            else if (simulator != null)
            {
                CurrentCoordinate = simulator.userCoordinate;
            }

            if (simulator != null)
            {
                observerHeightUnits = simulator.observerHeightUnits;
                simulator.enabled = false;
            }

            Input.gyro.enabled = true;
            Input.gyro.updateInterval = 0.0167f;
            Input.compass.enabled = true;

            gameRotationVector = new AndroidGameRotationVector();
            GameRotationAvailable = gameRotationVector.Start();
            OrientationStatus = arCoreTracking != null
                ? "AR visual tracking starting"
                : GameRotationAvailable
                    ? "Automatic compass (gyro fallback ready)"
                    : "Automatic compass";

            if (!GlassGlobeSettingsState.EffectiveViewpointOverrideEnabled)
            {
                RequestLocationPermissionIfNeeded();
            }
        }

        private void Update()
        {
            if (!SensorModeActive)
            {
                return;
            }

            UpdateLocation();
            UpdateAttitudeAndPose();

            if (verboseLogging && Time.time - lastLogTime > 1f)
            {
                lastLogTime = Time.time;
                Debug.Log(string.Format(
                    "GlassGlobeSensors: fix={0} acc={1:0.0}m user={2} heading={3:0.0} tilt={4:0.0} rawHeading={5:0.0} compassTrue={6:0.0} corr={7:0.0} offset={8:0.0} arAvailable={9} arFresh={10} orientation={11}",
                    HasLocationFix,
                    LocationAccuracyMeters,
                    CurrentCoordinate,
                    HeadingDegrees,
                    TiltDegrees,
                    AttitudeHeadingRawDegrees,
                    CompassTrueHeadingDegrees,
                    CompassCorrectionDegrees,
                    ActiveHeadingCorrectionDegrees,
                    ArTrackingAvailable,
                    ArTrackingFresh,
                    OrientationStatus));
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!SensorModeActive)
            {
                return;
            }

            if (pauseStatus)
            {
                if (gameRotationVector != null)
                {
                    gameRotationVector.Stop();
                }

                GameRotationFresh = false;
                hasCurrentGameDeviceInEnu = false;
                currentGameDisplayRotation = -1;

                // The gyro-only fallback frame can restart with a different yaw,
                // so that fallback lock still needs another Set North. ARCore's
                // tracker preserves and rebases its Earth mapping separately.
                if (gyroFallbackNorthLockActive)
                {
                    gyroFallbackNorthLockActive = false;
                    gyroNorthYawCorrectionDegrees = 0f;
                    gyroNorthLockNeedsReset = true;
                    hasSmoothedRotation = false;
                }

                OrientationStatus = ArNorthLockActive
                    ? "AR north lock held through pause"
                    : "Automatic compass (Set North again after resume)";
                return;
            }

            if (gameRotationVector != null)
            {
                GameRotationAvailable = gameRotationVector.Start();
            }
        }

        private void OnDestroy()
        {
            if (gameRotationVector != null)
            {
                gameRotationVector.Dispose();
                gameRotationVector = null;
            }
        }

        public void SnapAlignToCompass()
        {
            ResetHeadingCorrection();
        }

        public void NudgeHeading(float degrees)
        {
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                return;
            }

            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return;
            }

            if (ArNorthLockActive)
            {
                arCoreTracking.NudgeHeading(degrees);
            }
            else if (gyroFallbackNorthLockActive)
            {
                gyroNorthYawCorrectionDegrees = NormalizeHeadingOffset(
                    gyroNorthYawCorrectionDegrees + degrees);
            }
            else
            {
                headingOffsetDegrees = NormalizeHeadingOffset(headingOffsetDegrees + degrees);
                GlassGlobeSettingsState.SetHeadingFineOffset(headingOffsetDegrees);
            }

            hasSmoothedRotation = false;
        }

        public bool TryAlignCurrentHeadingToNorth(out float correctionDegrees)
        {
            correctionDegrees = 0f;
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                OrientationStatus = "Orient settings disabled";
                return false;
            }

            // Set North is also the recovery path for a stale or bad heading
            // lock. Clear the same correction state as Reset Heading before
            // capturing a fresh north alignment.
            ResetHeadingCorrection();

            // If an AR tracker is present, do not silently drop back to the
            // drifting gyro frame while ARCore is merely initializing. The user
            // can retry as soon as visual tracking reports a live pose.
            if (arCoreTracking != null)
            {
                if (!arCoreTracking.TryAlignCurrentHeading(0f, out correctionDegrees))
                {
                    OrientationStatus = arCoreTracking.Status;
                    return false;
                }

                gyroFallbackNorthLockActive = false;
                gyroNorthYawCorrectionDegrees = 0f;
                gyroNorthLockNeedsReset = false;
                CompassCorrectionDegrees = 0f;
                compassCorrectionInitialized = false;
                OrientationStatus = arCoreTracking.Status;
                hasSmoothedRotation = false;
                return true;
            }

            UpdateGameRotationVector();
            if (!SensorModeActive ||
                !GameRotationAvailable ||
                !GameRotationFresh ||
                !hasCurrentGameDeviceInEnu)
            {
                return false;
            }

            Vector3 rawForward = currentGameDeviceInEnu * Vector3.forward;
            Vector3 horizontalForward = new Vector3(rawForward.x, 0f, rawForward.z);
            if (horizontalForward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            ApplyGyroHeadingLock(horizontalForward, 0f, out correctionDegrees);
            return true;
        }

        public bool TryAlignCurrentViewToSkyTarget(
            float targetAzimuthDegrees,
            float targetAltitudeDegrees,
            float maximumAltitudeErrorDegrees,
            out float correctionDegrees,
            out float altitudeErrorDegrees)
        {
            correctionDegrees = 0f;
            altitudeErrorDegrees = float.NaN;

            if (arCoreTracking != null)
            {
                if (!arCoreTracking.TryAlignCurrentViewToSkyTarget(
                        targetAzimuthDegrees,
                        targetAltitudeDegrees,
                        maximumAltitudeErrorDegrees,
                        out correctionDegrees,
                        out altitudeErrorDegrees))
                {
                    OrientationStatus = arCoreTracking.Status;
                    return false;
                }

                gyroFallbackNorthLockActive = false;
                gyroNorthYawCorrectionDegrees = 0f;
                gyroNorthLockNeedsReset = false;
                CompassCorrectionDegrees = 0f;
                compassCorrectionInitialized = false;
                OrientationStatus = arCoreTracking.Status;
                hasSmoothedRotation = false;
                return true;
            }

            UpdateGameRotationVector();
            if (!SensorModeActive ||
                !GameRotationAvailable ||
                !GameRotationFresh ||
                !hasCurrentGameDeviceInEnu)
            {
                return false;
            }

            Vector3 rawForward = currentGameDeviceInEnu * Vector3.forward;
            if (rawForward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            rawForward.Normalize();
            float currentAltitudeDegrees =
                Mathf.Asin(Mathf.Clamp(rawForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            altitudeErrorDegrees = targetAltitudeDegrees - currentAltitudeDegrees;
            if (Mathf.Abs(altitudeErrorDegrees) > Mathf.Max(0f, maximumAltitudeErrorDegrees))
            {
                return false;
            }

            Vector3 horizontalForward = new Vector3(rawForward.x, 0f, rawForward.z);
            if (horizontalForward.sqrMagnitude < 0.03f)
            {
                altitudeErrorDegrees = float.NaN;
                return false;
            }

            ApplyGyroHeadingLock(horizontalForward, targetAzimuthDegrees, out correctionDegrees);
            return true;
        }

        private void ApplyGyroHeadingLock(
            Vector3 horizontalForward,
            float targetAzimuthDegrees,
            out float correctionDegrees)
        {
            float currentTotalCorrection = gyroFallbackNorthLockActive
                ? gyroNorthYawCorrectionDegrees
                : 0f;
            Vector3 correctedForward =
                Quaternion.AngleAxis(currentTotalCorrection, Vector3.up) * horizontalForward;
            float correctedHeading = Mathf.Repeat(
                Mathf.Atan2(correctedForward.x, correctedForward.z) * Mathf.Rad2Deg,
                360f);
            correctionDegrees = Mathf.DeltaAngle(correctedHeading, targetAzimuthDegrees);

            gyroNorthYawCorrectionDegrees = NormalizeHeadingOffset(
                currentTotalCorrection + correctionDegrees);
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = false;
            gyroFallbackNorthLockActive = true;
            gyroNorthLockNeedsReset = false;
            OrientationStatus = "Gyro fallback north lock (compass ignored)";
            hasSmoothedRotation = false;
        }

        public void ResetHeadingCorrection()
        {
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = false;
            headingOffsetDegrees = 0f;
            gyroNorthYawCorrectionDegrees = 0f;
            gyroFallbackNorthLockActive = false;
            gyroNorthLockNeedsReset = false;
            GlassGlobeSettingsState.SetHeadingFineOffset(0f);

            if (arCoreTracking != null)
            {
                arCoreTracking.ResetNorthLock();
                OrientationStatus = arCoreTracking.Status;
            }
            else
            {
                OrientationStatus = GameRotationAvailable
                    ? "Automatic compass (gyro fallback ready)"
                    : "Automatic compass";
            }

            hasSmoothedRotation = false;
        }

        private static float NormalizeHeadingOffset(float degrees)
        {
            return Mathf.Repeat(degrees + 180f, 360f) - 180f;
        }

        public void RefreshViewpoint()
        {
            GlassGlobeSettingsState.Load();
            hasSmoothedRotation = false;
            if (!SensorModeActive)
            {
                return;
            }

            UpdateLocation();
            UpdateAttitudeAndPose();
        }

        private void UpdateLocation()
        {
            GlassGlobeSettingsState.Load();
            if (GlassGlobeSettingsState.EffectiveViewpointOverrideEnabled)
            {
                CurrentCoordinate = GlassGlobeSettingsState.ViewpointCoordinate;
                HasLocationFix = false;
                LocationAccuracyMeters = 0f;
                LocationStatus = "Viewpoint override";
                return;
            }

            bool hasPermission = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            hasPermission = Permission.HasUserAuthorizedPermission(Permission.FineLocation);
#endif
            if (!hasPermission)
            {
                LocationStatus = "Waiting for location permission";
                RequestLocationPermissionIfNeeded();
                return;
            }

            if (!locationStartRequested)
            {
                if (!Input.location.isEnabledByUser)
                {
                    LocationStatus = "Location disabled in system settings";
                    return;
                }

                if (Time.unscaledTime < nextLocationRestartTime)
                {
                    return;
                }

                Input.location.Start(3f, 1f);
                locationStartRequested = true;
                LocationStatus = "Acquiring GPS fix...";
                return;
            }

            switch (Input.location.status)
            {
                case LocationServiceStatus.Running:
                    LocationInfo data = Input.location.lastData;
                    CurrentCoordinate = new GeoCoordinate(data.latitude, data.longitude);
                    LocationAccuracyMeters = data.horizontalAccuracy;
                    HasLocationFix = true;
                    LocationStatus = "GPS running";
                    break;
                case LocationServiceStatus.Initializing:
                    LocationStatus = "Acquiring GPS fix...";
                    break;
                case LocationServiceStatus.Failed:
                    LocationStatus = "GPS failed; retrying shortly";
                    ScheduleLocationRestart();
                    break;
                default:
                    LocationStatus = "GPS stopped; restarting shortly";
                    ScheduleLocationRestart();
                    break;
            }
        }

        private void ScheduleLocationRestart()
        {
            Input.location.Stop();
            locationStartRequested = false;
            nextLocationRestartTime = Time.unscaledTime + 15f;
        }

        private void RequestLocationPermissionIfNeeded()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!locationPermissionRequested &&
                !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                locationPermissionRequested = true;
                Permission.RequestUserPermission(Permission.FineLocation);
            }
#endif
        }

        private void UpdateAttitudeAndPose()
        {
            ResolveReferences();
            if (globe == null || targetCamera == null)
            {
                return;
            }

            EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(CurrentCoordinate);
            Vector3 observerPosition =
                globe.Center + frame.Up * (globe.RadiusUnits + observerHeightUnits);

            UpdateGameRotationVector();

            Quaternion raw = Input.gyro.attitude;
            bool hasMagneticAttitude =
                raw.x != 0f || raw.y != 0f || raw.z != 0f || raw.w != 0f;

            Quaternion deviceInEnu;
            float totalYawCorrection;
            if (ArNorthLockActive)
            {
                bool frozen;
                if (!arCoreTracking.TryGetDeviceInEnu(out deviceInEnu, out frozen))
                {
                    HasAttitude = false;
                    OrientationStatus = arCoreTracking.Status;
                    return;
                }

                HasAttitude = true;
                CompassCorrectionDegrees = 0f;
                totalYawCorrection = 0f;
                OrientationStatus = frozen
                    ? "AR north lock (orientation frozen; tracking waiting)"
                    : arCoreTracking.Status;
            }
            else if (GlassGlobeSettingsState.OrientCategoryEnabled && gyroFallbackNorthLockActive)
            {
                if (!hasCurrentGameDeviceInEnu)
                {
                    HasAttitude = false;
                    OrientationStatus = "Gyro fallback north lock (waiting for sensor)";
                    return;
                }

                HasAttitude = true;
                deviceInEnu = currentGameDeviceInEnu;
                CompassCorrectionDegrees = 0f;
                totalYawCorrection = gyroNorthYawCorrectionDegrees;
                OrientationStatus = GameRotationFresh
                    ? "Gyro fallback north lock (compass ignored)"
                    : "Gyro fallback north lock (orientation frozen; sensor waiting)";
            }
            else
            {
                HasAttitude = hasMagneticAttitude;
                if (!HasAttitude)
                {
                    return;
                }

                // Device attitude in a left-handed sensor world: +x east,
                // +y up, +z magnetic north (canonical Unity gyro remap).
                deviceInEnu = Quaternion.Euler(90f, 0f, 0f) *
                    new Quaternion(raw.x, raw.y, -raw.z, -raw.w);
                UpdateCompassCorrection(deviceInEnu);
                totalYawCorrection = CompassCorrectionDegrees +
                    (GlassGlobeSettingsState.OrientCategoryEnabled ? headingOffsetDegrees : 0f);
                OrientationStatus = !GlassGlobeSettingsState.OrientCategoryEnabled
                    ? "Automatic compass (Orient settings disabled)"
                    : gyroNorthLockNeedsReset
                    ? "Automatic compass (Set North again after resume)"
                    : arCoreTracking != null
                        ? arCoreTracking.TrackingFresh
                            ? "Automatic compass (AR Set North ready)"
                            : "Automatic compass (AR tracking starting)"
                        : GameRotationAvailable
                            ? "Automatic compass (gyro fallback ready)"
                            : "Automatic compass";
            }

            Quaternion enuToWorld = Quaternion.LookRotation(frame.North, frame.Up);
            Quaternion targetRotation =
                Quaternion.AngleAxis(totalYawCorrection, frame.Up) *
                enuToWorld * deviceInEnu;

            if (!hasSmoothedRotation)
            {
                smoothedRotation = targetRotation;
                hasSmoothedRotation = true;
            }
            else
            {
                smoothedRotation = Quaternion.Slerp(
                    smoothedRotation,
                    targetRotation,
                    1f - Mathf.Pow(attitudeSmoothing, Time.deltaTime * 60f));
            }

            transform.SetPositionAndRotation(observerPosition, smoothedRotation);
            targetCamera.transform.SetPositionAndRotation(observerPosition, smoothedRotation);
            targetCamera.fieldOfView = cameraFovDegrees;
            targetCamera.nearClipPlane = EarthMath.CalculateThroughEarthNearClip(
                observerPosition,
                smoothedRotation * Vector3.forward,
                globe.Center,
                globe.RadiusUnits);
            targetCamera.farClipPlane = Mathf.Max(
                targetCamera.farClipPlane,
                EarthMath.CalculateSkyFarClip(globe.RadiusUnits, MaxSkyRadius()));

            UpdateReadouts(frame);
            UpdateDebugVisuals(frame, observerPosition);
        }

        private void UpdateGameRotationVector()
        {
            if (gameRotationVector == null)
            {
                GameRotationAvailable = false;
                GameRotationFresh = false;
                return;
            }

            GameRotationAvailable = gameRotationVector.IsSupported;
            Quaternion nextRotation;
            int displayRotation;
            if (!gameRotationVector.TryGetRotation(out nextRotation, out displayRotation))
            {
                GameRotationFresh = false;
                return;
            }

            if (currentGameDisplayRotation >= 0 &&
                displayRotation != currentGameDisplayRotation)
            {
                hasSmoothedRotation = false;
            }

            currentGameDisplayRotation = displayRotation;
            currentGameDeviceInEnu = nextRotation;
            hasCurrentGameDeviceInEnu = true;
            GameRotationFresh = true;
        }

        private void UpdateCompassCorrection(Quaternion deviceInEnu)
        {
            Vector3 forward = deviceInEnu * Vector3.forward;
            Vector3 up = deviceInEnu * Vector3.up;
            Vector3 headingVector;
            if (forward.y < -0.9f)
            {
                headingVector = new Vector3(up.x, 0f, up.z);
            }
            else if (forward.y > 0.9f)
            {
                headingVector = new Vector3(-up.x, 0f, -up.z);
            }
            else
            {
                headingVector = new Vector3(forward.x, 0f, forward.z);
            }

            if (headingVector.sqrMagnitude < 0.000001f)
            {
                return;
            }

            AttitudeHeadingRawDegrees = Mathf.Repeat(
                Mathf.Atan2(headingVector.x, headingVector.z) * Mathf.Rad2Deg,
                360f);
            CompassTrueHeadingDegrees = Input.compass.trueHeading;

            float desiredCorrection;
            if (!TryGetTrueNorthCorrection(out desiredCorrection))
            {
                return;
            }

            if (!compassCorrectionInitialized)
            {
                CompassCorrectionDegrees = desiredCorrection;
                compassCorrectionInitialized = true;
                return;
            }

            float lerpFactor =
                1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1f, compassAlignSeconds));
            CompassCorrectionDegrees = Mathf.LerpAngle(
                CompassCorrectionDegrees,
                desiredCorrection,
                lerpFactor);
        }

        private static bool TryGetTrueNorthCorrection(out float correctionDegrees)
        {
            correctionDegrees = 0f;
            if (!Input.compass.enabled || Input.compass.timestamp <= 0d)
            {
                return false;
            }

            float trueHeading = Input.compass.trueHeading;
            float magneticHeading = Input.compass.magneticHeading;
            if (float.IsNaN(trueHeading) || float.IsInfinity(trueHeading) ||
                float.IsNaN(magneticHeading) || float.IsInfinity(magneticHeading))
            {
                return false;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                return true;
            }

            correctionDegrees = Mathf.DeltaAngle(magneticHeading, trueHeading);
            return true;
        }

        private void UpdateReadouts(EarthMath.LocalFrame frame)
        {
            Vector3 forward = smoothedRotation * Vector3.forward;
            float eastComponent = Vector3.Dot(forward, frame.East);
            float northComponent = Vector3.Dot(forward, frame.North);
            if (Mathf.Abs(eastComponent) > 0.0001f ||
                Mathf.Abs(northComponent) > 0.0001f)
            {
                HeadingDegrees = Mathf.Repeat(
                    Mathf.Atan2(eastComponent, northComponent) * Mathf.Rad2Deg,
                    360f);
            }

            TiltDegrees = Vector3.Angle(frame.Down, forward);
        }

        private float MaxSkyRadius()
        {
            float maxRadius = 0f;
            if (milkyWayBackground != null)
            {
                maxRadius = Mathf.Max(maxRadius, milkyWayBackground.radiusUnits);
            }

            if (sunMoonBackground != null)
            {
                maxRadius = Mathf.Max(maxRadius, sunMoonBackground.radiusUnits);
            }

            return maxRadius;
        }

        private void UpdateDebugVisuals(
            EarthMath.LocalFrame frame,
            Vector3 observerPosition)
        {
            Vector3 surfacePosition = globe.Center + frame.Up * globe.RadiusUnits;

            if (userPositionMarker != null)
            {
                userPositionMarker.position = surfacePosition;
            }

            if (localUpLine != null)
            {
                localUpLine.positionCount = 2;
                localUpLine.SetPosition(0, surfacePosition);
                localUpLine.SetPosition(
                    1,
                    surfacePosition + frame.Up * debugLineLength);
            }

            if (localDownLine != null)
            {
                localDownLine.positionCount = 2;
                localDownLine.SetPosition(0, surfacePosition);
                localDownLine.SetPosition(
                    1,
                    surfacePosition + frame.Down * debugLineLength);
            }
        }

        private void ResolveReferences()
        {
            if (simulator == null)
            {
                simulator = FindFirstObjectByType<PhonePoseSimulator>();
            }

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (targetCamera == null && simulator != null)
            {
                targetCamera = simulator.targetCamera;
            }

            if (targetCamera == null)
            {
                targetCamera = GetComponentInChildren<Camera>();
            }

            if (arCoreTracking == null)
            {
                arCoreTracking = FindFirstObjectByType<ArCoreOrientationTracker>();
            }

            if (milkyWayBackground == null)
            {
                milkyWayBackground = FindFirstObjectByType<MilkyWayBackground>();
            }

            if (sunMoonBackground == null)
            {
                sunMoonBackground = FindFirstObjectByType<SunMoonBackground>();
            }
        }
    }
}

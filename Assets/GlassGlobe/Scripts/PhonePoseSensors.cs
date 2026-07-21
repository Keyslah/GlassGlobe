using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Drives the phone viewport from real device sensors: GPS for the observer's
    /// lat/lon and the fused attitude sensor (gravity + gyro + magnetometer) for
    /// orientation. Replaces PhonePoseSimulator at runtime on device; in the
    /// editor the simulator stays in charge unless useSensorsInEditor is set.
    /// </summary>
    public sealed class PhonePoseSensors : MonoBehaviour
    {
        public PhonePoseSimulator simulator;
        public GlobeRenderer globe;
        public Camera targetCamera;
        public Transform userPositionMarker;
        public LineRenderer localUpLine;
        public LineRenderer localDownLine;
        public float debugLineLength = 2.5f;

        public bool useSensorsInEditor = false;

        [Min(0f)]
        public float observerHeightUnits = 0.35f;

        [Range(20f, 100f)]
        public float cameraFovDegrees = 32.4f;

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
        public bool GyroNorthLockActive { get; private set; }
        public string OrientationStatus { get; private set; }
        public float ActiveHeadingCorrectionDegrees
        {
            get { return GyroNorthLockActive ? gyroNorthYawCorrectionDegrees : headingOffsetDegrees; }
        }

        private bool locationStartRequested;
        private bool locationPermissionRequested;
        private bool compassCorrectionInitialized;
        private AndroidGameRotationVector gameRotationVector;
        private Quaternion currentGameDeviceInEnu = Quaternion.identity;
        private bool hasCurrentGameDeviceInEnu;
        private int currentGameDisplayRotation = -1;
        private float gyroNorthYawCorrectionDegrees;
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
            // Fine adjustments remain persistent. The gyro north lock itself is
            // session-only because its arbitrary yaw reference can reset.
            headingOffsetDegrees = GlassGlobeSettingsState.HeadingFineOffsetDegrees;
            gyroNorthYawCorrectionDegrees = 0f;
            GyroNorthLockActive = false;

            SensorModeActive = Application.isMobilePlatform || useSensorsInEditor;
            LocationStatus = "Not started";
            OrientationStatus = "Starting orientation sensors";

            if (!SensorModeActive)
            {
                enabled = false;
                return;
            }

            if (GlassGlobeSettingsState.ViewpointOverrideEnabled)
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
            OrientationStatus = GameRotationAvailable
                ? "Automatic compass (gyro north lock ready)"
                : "Automatic compass (gyro north lock unavailable)";

            if (!GlassGlobeSettingsState.ViewpointOverrideEnabled)
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
                    "GlassGlobeSensors: fix={0} acc={1:0.0}m user={2} heading={3:0.0} tilt={4:0.0} rawHeading={5:0.0} compassTrue={6:0.0} corr={7:0.0} offset={8:0.0} orientation={9}",
                    HasLocationFix,
                    LocationAccuracyMeters,
                    CurrentCoordinate,
                    HeadingDegrees,
                    TiltDegrees,
                    AttitudeHeadingRawDegrees,
                    CompassTrueHeadingDegrees,
                    CompassCorrectionDegrees,
                    ActiveHeadingCorrectionDegrees,
                    OrientationStatus));
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!SensorModeActive || gameRotationVector == null)
            {
                return;
            }

            if (pauseStatus)
            {
                gameRotationVector.Stop();
                GameRotationFresh = false;
                hasCurrentGameDeviceInEnu = false;
                currentGameDisplayRotation = -1;
                if (GyroNorthLockActive)
                {
                    GyroNorthLockActive = false;
                    gyroNorthYawCorrectionDegrees = 0f;
                    gyroNorthLockNeedsReset = true;
                    hasSmoothedRotation = false;
                    OrientationStatus = "Automatic compass (Set North again after resume)";
                }

                return;
            }

            GameRotationAvailable = gameRotationVector.Start();
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
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return;
            }

            if (GyroNorthLockActive)
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
            UpdateGameRotationVector();
            if (!SensorModeActive ||
                !GameRotationAvailable ||
                !GameRotationFresh ||
                !hasCurrentGameDeviceInEnu)
            {
                return false;
            }

            // Set North is defined by the center of the upright viewport. A
            // nearly vertical camera-forward vector has no usable azimuth, so
            // require the phone to be held upright as the UI instructs.
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
            float currentAltitudeDegrees = Mathf.Asin(Mathf.Clamp(rawForward.y, -1f, 1f)) * Mathf.Rad2Deg;
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
            float currentTotalCorrection = GyroNorthLockActive
                ? gyroNorthYawCorrectionDegrees
                : 0f;
            Vector3 correctedForward =
                Quaternion.AngleAxis(currentTotalCorrection, Vector3.up) * horizontalForward;
            float correctedHeading = Mathf.Repeat(
                Mathf.Atan2(correctedForward.x, correctedForward.z) * Mathf.Rad2Deg,
                360f);
            correctionDegrees = Mathf.DeltaAngle(correctedHeading, targetAzimuthDegrees);

            // Save one constant yaw for the full magnetometer-free pose. Pitch,
            // roll, and deliberate turns now work without desk-driven compass yaw.
            gyroNorthYawCorrectionDegrees = NormalizeHeadingOffset(
                currentTotalCorrection + correctionDegrees);
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = false;
            GyroNorthLockActive = true;
            gyroNorthLockNeedsReset = false;
            OrientationStatus = "Gyro north lock (compass ignored)";
            hasSmoothedRotation = false;
        }

        public void ResetHeadingCorrection()
        {
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = false;
            headingOffsetDegrees = 0f;
            gyroNorthYawCorrectionDegrees = 0f;
            GyroNorthLockActive = false;
            gyroNorthLockNeedsReset = false;
            GlassGlobeSettingsState.SetHeadingFineOffset(0f);
            OrientationStatus = GameRotationAvailable
                ? "Automatic compass (gyro north lock ready)"
                : "Automatic compass (gyro north lock unavailable)";
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
            if (GlassGlobeSettingsState.ViewpointOverrideEnabled)
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
                    // The OS can stop the service (e.g. while backgrounded);
                    // schedule a restart instead of staying dead until relaunch.
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
            if (!locationPermissionRequested && !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
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
            Vector3 observerPosition = globe.Center + frame.Up * (globe.RadiusUnits + observerHeightUnits);

            UpdateGameRotationVector();

            // Unity's Android attitude uses the magnetic rotation vector. Keep
            // it for automatic north, but never use it while gyro lock is active.
            Quaternion raw = Input.gyro.attitude;
            bool hasMagneticAttitude =
                raw.x != 0f || raw.y != 0f || raw.z != 0f || raw.w != 0f;

            Quaternion deviceInEnu;
            float totalYawCorrection;
            if (GyroNorthLockActive)
            {
                if (!hasCurrentGameDeviceInEnu)
                {
                    HasAttitude = false;
                    OrientationStatus = "Gyro north lock (waiting for sensor)";
                    return;
                }

                HasAttitude = true;
                deviceInEnu = currentGameDeviceInEnu;
                CompassCorrectionDegrees = 0f;
                totalYawCorrection = gyroNorthYawCorrectionDegrees;
                OrientationStatus = GameRotationFresh
                    ? "Gyro north lock (compass ignored)"
                    : "Gyro north lock (orientation frozen; sensor waiting)";
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
                totalYawCorrection = CompassCorrectionDegrees + headingOffsetDegrees;
                OrientationStatus = gyroNorthLockNeedsReset
                    ? "Automatic compass (Set North again after resume)"
                    : GameRotationAvailable
                        ? "Automatic compass (Set North for metal resistance)"
                        : "Automatic compass (gyro north lock unavailable)";
            }

            // Map the sensor-world frame onto the globe's local frame at the
            // observer, then apply the selected source's fixed yaw correction.
            Quaternion enuToWorld = Quaternion.LookRotation(frame.North, frame.Up);
            Quaternion targetRotation =
                Quaternion.AngleAxis(totalYawCorrection, frame.Up) * enuToWorld * deviceInEnu;

            if (!hasSmoothedRotation)
            {
                smoothedRotation = targetRotation;
                hasSmoothedRotation = true;
            }
            else
            {
                smoothedRotation = Quaternion.Slerp(smoothedRotation, targetRotation, 1f - Mathf.Pow(attitudeSmoothing, Time.deltaTime * 60f));
            }

            transform.SetPositionAndRotation(observerPosition, smoothedRotation);
            targetCamera.transform.SetPositionAndRotation(observerPosition, smoothedRotation);
            targetCamera.fieldOfView = cameraFovDegrees;
            targetCamera.nearClipPlane = EarthMath.CalculateThroughEarthNearClip(
                observerPosition, smoothedRotation * Vector3.forward, globe.Center, globe.RadiusUnits);
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
            // This scalar heading is a readout only. It must never drive pose
            // correction because its reference axis changes between the
            // camera-forward and screen-top directions as the phone pitches.
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

            AttitudeHeadingRawDegrees = Mathf.Repeat(Mathf.Atan2(headingVector.x, headingVector.z) * Mathf.Rad2Deg, 360f);
            CompassTrueHeadingDegrees = Input.compass.trueHeading;

            float desiredCorrection;
            if (!TryGetTrueNorthCorrection(out desiredCorrection))
            {
                return;
            }

            // Android's rotation-vector frame is already magnetic-north based.
            // Only magnetic declination is needed for geographic true north.
            // Both compass values use the same screen axis, so subtracting them
            // cancels that axis without feeding pitch into camera yaw.
            if (!compassCorrectionInitialized)
            {
                CompassCorrectionDegrees = desiredCorrection;
                compassCorrectionInitialized = true;
                return;
            }

            float lerpFactor = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1f, compassAlignSeconds));
            CompassCorrectionDegrees = Mathf.LerpAngle(CompassCorrectionDegrees, desiredCorrection, lerpFactor);
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

            // Unity only guarantees trueHeading after location starts. Until
            // then, keep the rotation-vector's magnetic-north reference.
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
            if (Mathf.Abs(eastComponent) > 0.0001f || Mathf.Abs(northComponent) > 0.0001f)
            {
                HeadingDegrees = Mathf.Repeat(Mathf.Atan2(eastComponent, northComponent) * Mathf.Rad2Deg, 360f);
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

        private void UpdateDebugVisuals(EarthMath.LocalFrame frame, Vector3 observerPosition)
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
                localUpLine.SetPosition(1, surfacePosition + frame.Up * debugLineLength);
            }

            if (localDownLine != null)
            {
                localDownLine.positionCount = 2;
                localDownLine.SetPosition(0, surfacePosition);
                localDownLine.SetPosition(1, surfacePosition + frame.Down * debugLineLength);
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


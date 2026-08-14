using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Drives the phone viewport from GPS plus a continuous sensor orientation.
    /// Android's earth-referenced rotation vector owns orientation continuously.
    /// The native bridge timestamps every pose so a cached sample cannot masquerade
    /// as live orientation. AR is camera-only and never owns the view.
    /// </summary>
    public sealed class PhonePoseSensors : MonoBehaviour
    {
        private const float EarthRotationRestartDelaySeconds = 2f;

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
        public bool EarthRotationAvailable { get; private set; }
        public bool EarthRotationFresh { get; private set; }
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
                    arCoreTracking.NorthLockActive &&
                    arCoreTracking.TrackingFresh;
            }
        }
        // Kept for the existing HUD/settings API. It now means that a persistent
        // sensor calibration exists, whether or not optional AR is currently on.
        public bool GyroNorthLockActive
        {
            get
            {
                return GlassGlobeSettingsState.OrientCategoryEnabled &&
                    sensorNorthLockActive;
            }
        }
        public string OrientationStatus { get; private set; }
        public string ActiveOrientationSource { get; private set; }
        public float CompassAccuracyDegrees { get; private set; }
        public float ActiveHeadingCorrectionDegrees
        {
            get
            {
                if (!GlassGlobeSettingsState.OrientCategoryEnabled)
                {
                    return 0f;
                }

                return headingOffsetDegrees;
            }
        }

        private bool locationStartRequested;
        private bool locationPermissionRequested;
        private bool compassCorrectionInitialized;
        private AndroidEarthRotationVector earthRotationVector;
        private Quaternion currentEarthDeviceInEnu = Quaternion.identity;
        private bool hasCurrentEarthDeviceInEnu;
        private int currentEarthDisplayRotation = -1;
        private int lastEarthRotationUpdateFrame = -1;
        private float currentEarthHeadingAccuracyDegrees = -1f;
        private float nextEarthRotationRestartTime;
        private bool sensorNorthLockActive;
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
            sensorNorthLockActive =
                GlassGlobeSettingsState.HeadingCalibrationActive;
            if (sensorNorthLockActive)
            {
                // A persisted Set North bias is self-contained and must not be
                // shifted by a later compass/GPS initialization.
                CompassCorrectionDegrees = 0f;
                compassCorrectionInitialized = true;
            }

            SensorModeActive = Application.isMobilePlatform || useSensorsInEditor;
            LocationStatus = "Not started";
            OrientationStatus = "Starting orientation tracking";
            ActiveOrientationSource = "Starting";
            CompassAccuracyDegrees = -1f;

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

            earthRotationVector = new AndroidEarthRotationVector();
            EarthRotationAvailable = earthRotationVector.Start();
            nextEarthRotationRestartTime =
                Time.unscaledTime + EarthRotationRestartDelaySeconds;
#if UNITY_ANDROID && !UNITY_EDITOR
            OrientationStatus = EarthRotationAvailable
                ? "Timestamped earth-referenced rotation sensor starting"
                : "Timestamped earth rotation sensor unavailable; retrying";
#else
            OrientationStatus = EarthRotationAvailable
                ? "Timestamped earth-referenced rotation sensor starting"
                : "Unity orientation fallback starting";
#endif

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
                    "GlassGlobeSensors: fix={0} acc={1:0.0}m user={2} heading={3:0.0} tilt={4:0.0} rawHeading={5:0.0} compassTrue={6:0.0} compassAcc={7:0.0} corr={8:0.0} offset={9:0.0} earthFresh={10} arAvailable={11} arFresh={12} source={13} orientation={14}",
                    HasLocationFix,
                    LocationAccuracyMeters,
                    CurrentCoordinate,
                    HeadingDegrees,
                    TiltDegrees,
                    AttitudeHeadingRawDegrees,
                    CompassTrueHeadingDegrees,
                    CompassAccuracyDegrees,
                    CompassCorrectionDegrees,
                    ActiveHeadingCorrectionDegrees,
                    EarthRotationFresh,
                    ArTrackingAvailable,
                    ArTrackingFresh,
                    ActiveOrientationSource,
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
                if (earthRotationVector != null)
                {
                    earthRotationVector.Stop();
                }

                EarthRotationFresh = false;
                hasCurrentEarthDeviceInEnu = false;
                HasAttitude = false;
                currentEarthDisplayRotation = -1;
                lastEarthRotationUpdateFrame = -1;
                hasSmoothedRotation = false;
                ActiveOrientationSource = "Paused";
                OrientationStatus = sensorNorthLockActive
                    ? "Orientation paused; sensor north calibration retained"
                    : "Orientation sensors paused";
                return;
            }

            Input.gyro.enabled = true;
            Input.compass.enabled = true;
            if (earthRotationVector != null)
            {
                EarthRotationAvailable = earthRotationVector.Start();
            }

            nextEarthRotationRestartTime =
                Time.unscaledTime + EarthRotationRestartDelaySeconds;
            lastEarthRotationUpdateFrame = -1;

            OrientationStatus = sensorNorthLockActive
                ? "Live sensors resuming; north calibration retained"
                : "Live sensors resuming";
        }

        private void OnDestroy()
        {
            if (earthRotationVector != null)
            {
                earthRotationVector.Dispose();
                earthRotationVector = null;
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

            headingOffsetDegrees = NormalizeHeadingOffset(
                headingOffsetDegrees + degrees);
            GlassGlobeSettingsState.SetHeadingFineOffset(headingOffsetDegrees);

            if (sensorNorthLockActive &&
                arCoreTracking != null &&
                arCoreTracking.NorthLockActive)
            {
                arCoreTracking.NudgeHeading(degrees);
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

            if (!SensorModeActive ||
                !TryGetContinuousSensorDeviceInEnu(
                    false,
                    out Quaternion uncalibratedDeviceInEnu,
                    out string sensorSource))
            {
                OrientationStatus = "Set North waiting: no live orientation sensor";
                return false;
            }

            Quaternion currentlyCalibrated =
                Quaternion.AngleAxis(headingOffsetDegrees, Vector3.up) *
                uncalibratedDeviceInEnu;
            if (!TryGetHeadingDegrees(
                    currentlyCalibrated,
                    out float currentHeadingDegrees))
            {
                OrientationStatus =
                    "Set North waiting: hold the phone away from a vertical pointing singularity";
                return false;
            }

            correctionDegrees = Mathf.DeltaAngle(currentHeadingDegrees, 0f);
            float candidateOffsetDegrees = NormalizeHeadingOffset(
                headingOffsetDegrees + correctionDegrees);
            CommitSensorNorthLock(
                uncalibratedDeviceInEnu,
                candidateOffsetDegrees,
                sensorSource);
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
            if (!IsFinite(targetAzimuthDegrees) ||
                !IsFinite(targetAltitudeDegrees) ||
                !IsFinite(maximumAltitudeErrorDegrees))
            {
                OrientationStatus = "Sky alignment rejected invalid target data";
                return false;
            }

            if (!SensorModeActive ||
                !TryGetContinuousSensorDeviceInEnu(
                    false,
                    out Quaternion uncalibratedDeviceInEnu,
                    out string sensorSource))
            {
                OrientationStatus = "Sky alignment waiting: no live orientation sensor";
                return false;
            }

            Quaternion currentlyCalibrated =
                Quaternion.AngleAxis(headingOffsetDegrees, Vector3.up) *
                uncalibratedDeviceInEnu;
            Vector3 rawForward = currentlyCalibrated * Vector3.forward;
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

            if (!TryGetHeadingDegrees(
                    currentlyCalibrated,
                    out float currentHeadingDegrees))
            {
                altitudeErrorDegrees = float.NaN;
                return false;
            }

            correctionDegrees = Mathf.DeltaAngle(
                currentHeadingDegrees,
                targetAzimuthDegrees);
            float candidateOffsetDegrees = NormalizeHeadingOffset(
                headingOffsetDegrees + correctionDegrees);
            CommitSensorNorthLock(
                uncalibratedDeviceInEnu,
                candidateOffsetDegrees,
                sensorSource);
            return true;
        }

        private void CommitSensorNorthLock(
            Quaternion uncalibratedDeviceInEnu,
            float candidateOffsetDegrees,
            string sensorSource)
        {
            // All validation happens before this method. Commit the persistent
            // sensor calibration first, then give optional AR the resulting live
            // pose as its rebase target. A failed/inactive AR session cannot undo
            // a good Set North capture.
            // The sampled pose already contains the current magnetic-to-true
            // correction. Fold that correction into the persisted manual bias,
            // then hold the runtime correction at zero. The saved north lock is
            // therefore self-contained across GPS acquisition and app restarts.
            float capturedAutomaticCorrection = compassCorrectionInitialized
                ? CompassCorrectionDegrees
                : 0f;
            headingOffsetDegrees = NormalizeHeadingOffset(
                candidateOffsetDegrees + capturedAutomaticCorrection);
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = true;
            GlassGlobeSettingsState.SetHeadingFineOffset(headingOffsetDegrees);
            GlassGlobeSettingsState.SetHeadingCalibrationActive(true);
            sensorNorthLockActive = true;

            Quaternion calibratedDeviceInEnu =
                Quaternion.AngleAxis(candidateOffsetDegrees, Vector3.up) *
                uncalibratedDeviceInEnu;
            if (arCoreTracking != null)
            {
                arCoreTracking.SetNorthLockFromSensor(
                    calibratedDeviceInEnu,
                    headingOffsetDegrees);
            }

            ActiveOrientationSource = sensorSource;
            OrientationStatus =
                "Sensor north calibration set; camera tracking cannot change orientation";
            hasSmoothedRotation = false;
        }

        public void ResetHeadingCorrection()
        {
            headingOffsetDegrees = 0f;
            sensorNorthLockActive = false;
            GlassGlobeSettingsState.SetHeadingFineOffset(0f);
            GlassGlobeSettingsState.SetHeadingCalibrationActive(false);
            CompassCorrectionDegrees = 0f;
            compassCorrectionInitialized = false;

            if (arCoreTracking != null)
            {
                arCoreTracking.ResetNorthLock();
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            OrientationStatus = EarthRotationAvailable
                ? "Manual heading reset; timestamped earth rotation active"
                : "Manual heading reset; timestamped earth rotation unavailable, retrying";
#else
            OrientationStatus = EarthRotationAvailable
                ? "Manual heading reset; timestamped earth rotation active"
                : "Manual heading reset; Unity orientation fallback active";
#endif
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

            if (!TryGetContinuousSensorDeviceInEnu(
                    true,
                    out Quaternion sensorDeviceInEnu,
                    out string sensorSource))
            {
                HasAttitude = false;
                ActiveOrientationSource = "Unavailable";
                OrientationStatus = arCoreTracking != null
                    ? "Live orientation sensors unavailable; " + arCoreTracking.Status
                    : "Live orientation sensors unavailable";
                return;
            }

            HasAttitude = true;
            // Keep AR's optional north mapping informed for diagnostics, but do
            // not ever substitute an AR pose for the earth-referenced sensor
            // pose. ARCore can retain stale transforms and can relocalize its map;
            // neither event is allowed to move the globe.
            if (GlassGlobeSettingsState.OrientCategoryEnabled &&
                sensorNorthLockActive &&
                arCoreTracking != null)
            {
                if (!arCoreTracking.NorthLockActive)
                {
                    arCoreTracking.SetNorthLockFromSensor(
                        sensorDeviceInEnu,
                        headingOffsetDegrees);
                }

            }

            Quaternion deviceInEnu = sensorDeviceInEnu;
            ActiveOrientationSource = sensorSource;
            OrientationStatus = BuildOrientationStatus(sensorSource);

            Quaternion enuToWorld = Quaternion.LookRotation(frame.North, frame.Up);
            Quaternion targetRotation = enuToWorld * deviceInEnu;

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

        private bool TryGetContinuousSensorDeviceInEnu(
            bool applyManualCorrection,
            out Quaternion deviceInEnu,
            out string sensorSource)
        {
            deviceInEnu = Quaternion.identity;
            sensorSource = "Unavailable";

            UpdateEarthRotationVector();
            if (EarthRotationFresh && hasCurrentEarthDeviceInEnu)
            {
                deviceInEnu = currentEarthDeviceInEnu;
                UpdateCompassCorrection(
                    deviceInEnu,
                    currentEarthHeadingAccuracyDegrees);
                deviceInEnu =
                    Quaternion.AngleAxis(
                        CompassCorrectionDegrees,
                        Vector3.up) *
                    deviceInEnu;
                sensorSource = "Timestamped earth-referenced rotation vector";
            }
            else if (EarthRotationAvailable)
            {
                // The native provider has a rotation-vector sensor but its last
                // sample is not current. Do not swap to Unity's un-timestamped
                // cache: holding or switching that pose recreates the drift/snap
                // failure this path exists to prevent.
                return false;
            }
            else
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Android never accepts Unity's un-timestamped attitude cache as
                // a live pose. A missing native stream is surfaced as unavailable
                // and retried instead of silently becoming a frozen orientation.
                return false;
#else
                // Non-Android/editor fallback only. On the Android target the
                // timestamped provider above is authoritative whenever the
                // rotation-vector sensor exists.
                Quaternion rawAttitude = Input.gyro.attitude;
                if (!TryNormalizeRotation(ref rawAttitude))
                {
                    return false;
                }

                deviceInEnu = Quaternion.Euler(90f, 0f, 0f) *
                    new Quaternion(
                        rawAttitude.x,
                        rawAttitude.y,
                        -rawAttitude.z,
                        -rawAttitude.w);
                if (!TryNormalizeRotation(ref deviceInEnu))
                {
                    return false;
                }

                UpdateCompassCorrection(
                    deviceInEnu,
                    Input.compass.headingAccuracy);
                deviceInEnu =
                    Quaternion.AngleAxis(
                        CompassCorrectionDegrees,
                        Vector3.up) *
                    deviceInEnu;
                sensorSource =
                    "Unity earth-referenced orientation fallback (no native provider)";
#endif
            }

            if (applyManualCorrection &&
                GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                deviceInEnu =
                    Quaternion.AngleAxis(
                        headingOffsetDegrees,
                        Vector3.up) *
                    deviceInEnu;
            }

            return TryNormalizeRotation(ref deviceInEnu);
        }

        private string BuildOrientationStatus(string sensorSource)
        {
            if (!GlassGlobeSettingsState.OrientCategoryEnabled)
            {
                return "Live sensors: " + sensorSource +
                    " (Orient settings disabled)";
            }

            if (sensorNorthLockActive)
            {
                return arCoreTracking == null
                    ? "Sensor north lock: " + sensorSource +
                        "; AR enhancement unavailable"
                    : "Sensor north lock: " + sensorSource +
                        "; camera tracking cannot change orientation";
            }

            return arCoreTracking == null
                ? "Live sensors: " + sensorSource
                : "Live sensors: " + sensorSource +
                    "; " + arCoreTracking.Status;
        }

        private void UpdateEarthRotationVector()
        {
            if (lastEarthRotationUpdateFrame == Time.frameCount)
            {
                return;
            }

            lastEarthRotationUpdateFrame = Time.frameCount;
            if (earthRotationVector == null)
            {
                EarthRotationAvailable = false;
                EarthRotationFresh = false;
                return;
            }

            if (!earthRotationVector.IsSupported &&
                Time.unscaledTime >= nextEarthRotationRestartTime)
            {
                nextEarthRotationRestartTime =
                    Time.unscaledTime + EarthRotationRestartDelaySeconds;
                earthRotationVector.Start();
            }

            EarthRotationAvailable = earthRotationVector.IsSupported;
            Quaternion nextRotation;
            int displayRotation;
            float headingAccuracyDegrees;
            if (!earthRotationVector.TryGetRotation(
                    out nextRotation,
                    out displayRotation,
                    out headingAccuracyDegrees))
            {
                if (Time.unscaledTime >= nextEarthRotationRestartTime)
                {
                    nextEarthRotationRestartTime =
                        Time.unscaledTime + EarthRotationRestartDelaySeconds;
                    earthRotationVector.Stop();
                    EarthRotationAvailable = earthRotationVector.Start();
                }
                else
                {
                    EarthRotationAvailable = earthRotationVector.IsSupported;
                }

                EarthRotationFresh = false;
                return;
            }

            if (currentEarthDisplayRotation >= 0 &&
                displayRotation != currentEarthDisplayRotation)
            {
                hasSmoothedRotation = false;
            }

            currentEarthDisplayRotation = displayRotation;
            currentEarthDeviceInEnu = nextRotation;
            currentEarthHeadingAccuracyDegrees = headingAccuracyDegrees;
            hasCurrentEarthDeviceInEnu = true;
            EarthRotationFresh = true;
        }

        private void UpdateCompassCorrection(
            Quaternion deviceInEnu,
            float headingAccuracyDegrees)
        {
            CompassAccuracyDegrees =
                IsFinite(headingAccuracyDegrees) && headingAccuracyDegrees >= 0f
                    ? headingAccuracyDegrees
                    : Input.compass.headingAccuracy;
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

            // Set North is an explicit user calibration. Once captured, do not
            // let a late GPS fix or a changing magnetic-declination estimate
            // slowly move the reference underneath that fixed correction.
            if (sensorNorthLockActive)
            {
                return;
            }

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
                1f - Mathf.Exp(
                    -Time.unscaledDeltaTime /
                    Mathf.Max(1f, compassAlignSeconds));
            CompassCorrectionDegrees = Mathf.LerpAngle(
                CompassCorrectionDegrees,
                desiredCorrection,
                lerpFactor);
        }

        private static bool TryGetHeadingDegrees(
            Quaternion deviceInEnu,
            out float headingDegrees)
        {
            headingDegrees = 0f;
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
                return false;
            }

            headingDegrees = Mathf.Repeat(
                Mathf.Atan2(headingVector.x, headingVector.z) * Mathf.Rad2Deg,
                360f);
            return true;
        }

        private static bool TryNormalizeRotation(ref Quaternion rotation)
        {
            float magnitudeSquared =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            if (!IsFinite(rotation.x) ||
                !IsFinite(rotation.y) ||
                !IsFinite(rotation.z) ||
                !IsFinite(rotation.w) ||
                magnitudeSquared < 0.5f)
            {
                return false;
            }

            rotation = Quaternion.Normalize(rotation);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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

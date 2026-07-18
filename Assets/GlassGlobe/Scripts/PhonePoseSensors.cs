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

        [Tooltip("Manual heading calibration added on top of the compass alignment, degrees.")]
        public float headingOffsetDegrees = 0f;

        [Tooltip("Seconds for the slow compass alignment filter.")]
        public float compassAlignSeconds = 15f;

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

        private bool locationStartRequested;
        private bool locationPermissionRequested;
        private bool compassCorrectionInitialized;
        private Quaternion smoothedRotation = Quaternion.identity;
        private bool hasSmoothedRotation;
        private float lastLogTime;

        private void Awake()
        {
            ResolveReferences();
            GlassGlobeSettingsState.Load();

            SensorModeActive = Application.isMobilePlatform || useSensorsInEditor;
            LocationStatus = "Not started";

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

            if (Time.time - lastLogTime > 1f)
            {
                lastLogTime = Time.time;
                Debug.Log(string.Format(
                    "GlassGlobeSensors: fix={0} acc={1:0.0}m user={2} heading={3:0.0} tilt={4:0.0} rawHeading={5:0.0} compassTrue={6:0.0} corr={7:0.0} offset={8:0.0}",
                    HasLocationFix,
                    LocationAccuracyMeters,
                    CurrentCoordinate,
                    HeadingDegrees,
                    TiltDegrees,
                    AttitudeHeadingRawDegrees,
                    CompassTrueHeadingDegrees,
                    CompassCorrectionDegrees,
                    headingOffsetDegrees));
            }
        }

        public void SnapAlignToCompass()
        {
            compassCorrectionInitialized = false;
            headingOffsetDegrees = 0f;
        }

        public void NudgeHeading(float degrees)
        {
            headingOffsetDegrees = Mathf.Repeat(headingOffsetDegrees + degrees + 180f, 360f) - 180f;
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
                    LocationStatus = "GPS failed (using fallback location)";
                    break;
                default:
                    LocationStatus = "GPS stopped";
                    break;
            }
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

            // Device attitude in a left-handed sensor world: +x east, +y up,
            // +z magnetic north (canonical Unity gyro remap).
            Quaternion raw = Input.gyro.attitude;
            HasAttitude = raw.x != 0f || raw.y != 0f || raw.z != 0f || raw.w != 0f;
            if (!HasAttitude)
            {
                return;
            }

            Quaternion deviceInEnu = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(raw.x, raw.y, -raw.z, -raw.w);

            UpdateCompassCorrection(deviceInEnu);

            // Map the sensor-world frame onto the globe's local frame at the
            // observer, then correct magnetic->true north plus manual offset.
            Quaternion enuToWorld = Quaternion.LookRotation(frame.North, frame.Up);
            float totalYawCorrection = CompassCorrectionDegrees + headingOffsetDegrees;
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
            targetCamera.nearClipPlane = CalculateThroughEarthNearClip(observerPosition, smoothedRotation * Vector3.forward);
            targetCamera.farClipPlane = Mathf.Max(targetCamera.farClipPlane, globe.RadiusUnits * 8f);

            UpdateReadouts(frame);
            UpdateDebugVisuals(frame, observerPosition);
        }

        private void UpdateCompassCorrection(Quaternion deviceInEnu)
        {
            // Raw attitude heading in the sensor world. Use the camera forward
            // when it is reasonably horizontal, otherwise the top-of-screen
            // direction (phone pointing straight down at the ground).
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

            float compassTrue = Input.compass.trueHeading;
            float compassMagnetic = Input.compass.magneticHeading;
            bool compassValid = Input.compass.enabled &&
                (compassTrue != 0f || compassMagnetic != 0f) &&
                Input.compass.timestamp > 0d;
            if (!compassValid)
            {
                return;
            }

            CompassTrueHeadingDegrees = compassTrue;

            // The attitude sensor's yaw reference can be magnetic north or (on
            // some devices) arbitrary. Steer a slow correction so that the
            // attitude-derived heading agrees with the compass true heading.
            float desiredCorrection = Mathf.DeltaAngle(AttitudeHeadingRawDegrees, compassTrue);
            if (!compassCorrectionInitialized)
            {
                CompassCorrectionDegrees = desiredCorrection;
                compassCorrectionInitialized = true;
                return;
            }

            float lerpFactor = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1f, compassAlignSeconds));
            CompassCorrectionDegrees = Mathf.LerpAngle(CompassCorrectionDegrees, desiredCorrection, lerpFactor);
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

        private float CalculateThroughEarthNearClip(Vector3 observerPosition, Vector3 viewDirection)
        {
            float nearDistance;
            float farDistance;
            Ray viewRay = new Ray(observerPosition, viewDirection);
            if (!EarthMath.RaySphereIntersections(viewRay, globe.Center, globe.RadiusUnits, out nearDistance, out farDistance))
            {
                return 0.05f;
            }

            float intersectionDepth = Mathf.Max(0f, farDistance - nearDistance);
            float clipPadding = Mathf.Min(0.35f, intersectionDepth * 0.2f);
            return Mathf.Clamp(nearDistance + clipPadding, 0.01f, Mathf.Max(0.02f, farDistance - 0.05f));
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
        }
    }
}

using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace GlassGlobe
{
    /// <summary>
    /// Keeps an Earth-facing orientation on top of ARCore's visual-inertial
    /// tracking space. ARCore owns short-term motion; Set North supplies the one
    /// yaw needed to map that arbitrary tracking space into local ENU space.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class ArCoreOrientationTracker : MonoBehaviour
    {
        public ARSession arSession;
        public ARCameraManager cameraManager;
        public Transform trackingPose;

        public bool TrackingAvailable { get; private set; }
        public bool TrackingFresh { get; private set; }
        public bool NorthLockActive { get; private set; }
        public float HeadingCorrectionDegrees { get; private set; }
        public string Status { get; private set; }

        private InputDevice trackingDevice;
        private Quaternion currentTrackingRotation = Quaternion.identity;
        private bool hasCurrentTrackingRotation;
        private Quaternion enuFromTracking = Quaternion.identity;
        private Quaternion lastDeviceInEnu = Quaternion.identity;
        private bool hasLastDeviceInEnu;
        private bool rebaseWhenTrackingReturns;

        private void Awake()
        {
            ResolveReferences();
            Status = "AR tracking starting";
        }

        private void OnEnable()
        {
            ResolveReferences();
            KeepTrackingComponentsEnabled();
        }

        private void Update()
        {
            UpdateTrackingSample();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                TrackingFresh = false;
                hasCurrentTrackingRotation = false;
                trackingDevice = default;
                rebaseWhenTrackingReturns = NorthLockActive && hasLastDeviceInEnu;
                Status = NorthLockActive
                    ? "AR north lock held; waiting to rebase after resume"
                    : "AR tracking paused";
                return;
            }

            ResolveReferences();
            KeepTrackingComponentsEnabled();
            rebaseWhenTrackingReturns = NorthLockActive && hasLastDeviceInEnu;
            Status = NorthLockActive
                ? "AR north lock waiting for tracking"
                : "AR tracking resuming";
        }

        public bool TryGetDeviceInEnu(out Quaternion deviceInEnu, out bool frozen)
        {
            UpdateTrackingSample();
            frozen = false;

            if (NorthLockActive)
            {
                if (TrackingFresh && hasCurrentTrackingRotation)
                {
                    deviceInEnu = enuFromTracking * currentTrackingRotation;
                    RememberDevicePose(deviceInEnu);
                    return true;
                }

                if (hasLastDeviceInEnu)
                {
                    deviceInEnu = lastDeviceInEnu;
                    frozen = true;
                    return true;
                }

                deviceInEnu = Quaternion.identity;
                return false;
            }

            if (TrackingFresh && hasCurrentTrackingRotation)
            {
                deviceInEnu = currentTrackingRotation;
                return true;
            }

            deviceInEnu = Quaternion.identity;
            return false;
        }

        public bool TryAlignCurrentHeading(
            float targetAzimuthDegrees,
            out float correctionDegrees)
        {
            correctionDegrees = 0f;
            if (!TryGetFreshMappedRotation(out Quaternion mappedRotation))
            {
                return false;
            }

            Vector3 rawForward = mappedRotation * Vector3.forward;
            Vector3 horizontalForward = new Vector3(rawForward.x, 0f, rawForward.z);
            if (horizontalForward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            ApplyHeadingCorrection(
                horizontalForward,
                targetAzimuthDegrees,
                out correctionDegrees);
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
            if (!TryGetFreshMappedRotation(out Quaternion mappedRotation))
            {
                return false;
            }

            Vector3 rawForward = mappedRotation * Vector3.forward;
            if (rawForward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            rawForward.Normalize();
            float currentAltitudeDegrees =
                Mathf.Asin(Mathf.Clamp(rawForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            altitudeErrorDegrees = targetAltitudeDegrees - currentAltitudeDegrees;
            if (Mathf.Abs(altitudeErrorDegrees) >
                Mathf.Max(0f, maximumAltitudeErrorDegrees))
            {
                return false;
            }

            Vector3 horizontalForward = new Vector3(rawForward.x, 0f, rawForward.z);
            if (horizontalForward.sqrMagnitude < 0.03f)
            {
                altitudeErrorDegrees = float.NaN;
                return false;
            }

            ApplyHeadingCorrection(
                horizontalForward,
                targetAzimuthDegrees,
                out correctionDegrees);
            return true;
        }

        public void NudgeHeading(float degrees)
        {
            if (!NorthLockActive || !IsFinite(degrees))
            {
                return;
            }

            enuFromTracking =
                Quaternion.AngleAxis(degrees, Vector3.up) * enuFromTracking;
            HeadingCorrectionDegrees = NormalizeHeadingOffset(
                HeadingCorrectionDegrees + degrees);

            if (hasLastDeviceInEnu)
            {
                lastDeviceInEnu =
                    Quaternion.AngleAxis(degrees, Vector3.up) * lastDeviceInEnu;
            }

            if (TrackingFresh && hasCurrentTrackingRotation)
            {
                RememberDevicePose(enuFromTracking * currentTrackingRotation);
            }
        }

        public void ResetNorthLock()
        {
            NorthLockActive = false;
            HeadingCorrectionDegrees = 0f;
            enuFromTracking = Quaternion.identity;
            lastDeviceInEnu = Quaternion.identity;
            hasLastDeviceInEnu = false;
            rebaseWhenTrackingReturns = false;
            Status = TrackingFresh
                ? "AR tracking ready for Set North"
                : "AR tracking unavailable";
        }

        private bool TryGetFreshMappedRotation(out Quaternion mappedRotation)
        {
            UpdateTrackingSample();
            if (!TrackingFresh || !hasCurrentTrackingRotation)
            {
                mappedRotation = Quaternion.identity;
                return false;
            }

            mappedRotation = NorthLockActive
                ? enuFromTracking * currentTrackingRotation
                : currentTrackingRotation;
            return true;
        }

        private void ApplyHeadingCorrection(
            Vector3 horizontalForward,
            float targetAzimuthDegrees,
            out float correctionDegrees)
        {
            float currentHeading = Mathf.Repeat(
                Mathf.Atan2(horizontalForward.x, horizontalForward.z) *
                Mathf.Rad2Deg,
                360f);
            correctionDegrees = Mathf.DeltaAngle(
                currentHeading,
                targetAzimuthDegrees);

            enuFromTracking =
                Quaternion.AngleAxis(correctionDegrees, Vector3.up) *
                (NorthLockActive ? enuFromTracking : Quaternion.identity);
            HeadingCorrectionDegrees = NormalizeHeadingOffset(
                (NorthLockActive ? HeadingCorrectionDegrees : 0f) +
                correctionDegrees);
            NorthLockActive = true;
            rebaseWhenTrackingReturns = false;

            Quaternion mappedRotation =
                enuFromTracking * currentTrackingRotation;
            RememberDevicePose(mappedRotation);
            Status = "AR north lock (camera tracking, background optional)";
        }

        private void UpdateTrackingSample()
        {
            if (!Application.isMobilePlatform)
            {
                TrackingAvailable = false;
                TrackingFresh = false;
                Status = "AR tracking is available in the Android build";
                return;
            }

            ResolveReferences();
            KeepTrackingComponentsEnabled();

            ARSessionState sessionState = ARSession.state;
            bool sessionCanTrack =
                sessionState == ARSessionState.SessionTracking;
            TrackingAvailable = sessionState != ARSessionState.Unsupported &&
                sessionState != ARSessionState.None;

            if (!sessionCanTrack ||
                !TryReadTrackingRotation(out Quaternion nextRotation))
            {
                TrackingFresh = false;
                hasCurrentTrackingRotation = false;
                if (NorthLockActive && hasLastDeviceInEnu)
                {
                    rebaseWhenTrackingReturns = true;
                    Status =
                        "AR north lock (orientation frozen; tracking waiting)";
                }
                else
                {
                    Status = "AR tracking waiting: " + sessionState;
                }

                return;
            }

            TrackingAvailable = true;
            if (rebaseWhenTrackingReturns &&
                NorthLockActive &&
                hasLastDeviceInEnu)
            {
                // ARCore can create a new arbitrary tracking-space yaw after a
                // pause or tracking reset. Rebuild the mapping so the first pose
                // in the new space lands exactly on the last Earth-facing pose.
                enuFromTracking =
                    lastDeviceInEnu * Quaternion.Inverse(nextRotation);
                rebaseWhenTrackingReturns = false;
            }

            currentTrackingRotation = nextRotation;
            hasCurrentTrackingRotation = true;
            TrackingFresh = true;
            Status = NorthLockActive
                ? "AR north lock (camera tracking, background optional)"
                : "AR tracking ready for Set North";
        }

        private bool TryReadTrackingRotation(out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!trackingDevice.isValid)
            {
                trackingDevice =
                    InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            }

            if (trackingDevice.isValid)
            {
                bool isTracked;
                bool trackingStateAvailable =
                    trackingDevice.TryGetFeatureValue(
                        CommonUsages.isTracked,
                        out isTracked);
                if ((!trackingStateAvailable || isTracked) &&
                    trackingDevice.TryGetFeatureValue(
                        CommonUsages.deviceRotation,
                        out rotation) &&
                    TryNormalizeRotation(ref rotation))
                {
                    return true;
                }
            }

            // The XR Origin also carries an ARPoseDriver. Reading its transform
            // is a fallback for devices that expose the pose through the input
            // subsystem but do not populate XRNode.CenterEye immediately.
            if (trackingPose != null)
            {
                rotation = trackingPose.localRotation;
                if (TryNormalizeRotation(ref rotation))
                {
                    return true;
                }
            }

            rotation = Quaternion.identity;
            return false;
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

        private void RememberDevicePose(Quaternion deviceInEnu)
        {
            if (!IsFinite(deviceInEnu.x) ||
                !IsFinite(deviceInEnu.y) ||
                !IsFinite(deviceInEnu.z) ||
                !IsFinite(deviceInEnu.w))
            {
                return;
            }

            lastDeviceInEnu = deviceInEnu;
            hasLastDeviceInEnu = true;
        }

        private void ResolveReferences()
        {
            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>();
            }

            if (cameraManager == null)
            {
                cameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (trackingPose == null && cameraManager != null)
            {
                trackingPose = cameraManager.transform;
            }
        }

        private void KeepTrackingComponentsEnabled()
        {
            if (arSession != null && !arSession.enabled)
            {
                arSession.enabled = true;
            }

            if (cameraManager != null && !cameraManager.enabled)
            {
                cameraManager.enabled = true;
            }
        }

        private static float NormalizeHeadingOffset(float degrees)
        {
            return Mathf.Repeat(degrees + 180f, 360f) - 180f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

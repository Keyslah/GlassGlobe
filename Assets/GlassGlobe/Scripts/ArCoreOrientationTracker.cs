using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace GlassGlobe
{
    /// <summary>
    /// Maps optional ARCore visual-inertial motion into the live sensor ENU frame.
    /// Sensors remain authoritative: tracking loss falls straight back to their
    /// live pose, and reacquisition rebases AR to that pose before blending back.
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class ArCoreOrientationTracker : MonoBehaviour
    {
        // A real handheld turn cannot cover this much angle between normal AR
        // samples. Keep the threshold deliberately high so a fast intentional
        // motion still passes through, while an instantaneous tracking-space
        // relocalization can be rebased before it reaches the globe.
        private const float SilentRelocalizationMinimumJumpDegrees = 75f;
        private const float SilentRelocalizationMaximumGapSeconds = 0.15f;
        private const float CredibleHandheldAngularSpeedDegreesPerSecond = 1080f;
        private const float SilentRelocalizationAngleAllowanceDegrees = 20f;

        public ARSession arSession;
        public ARCameraManager cameraManager;
        public Transform trackingPose;

        [Min(0.1f)]
        [Tooltip("Seconds for AR's tracking-space mapping to converge back to the live sensor reference.")]
        public float sensorReferenceAlignSeconds = 8f;

        public bool TrackingAvailable { get; private set; }
        public bool TrackingFresh { get; private set; }
        public bool NorthLockActive { get; private set; }
        public float HeadingCorrectionDegrees { get; private set; }
        public string Status { get; private set; }
        public string FailureReason { get; private set; }
        public bool TrackingComponentsActive
        {
            get
            {
                return arSession != null &&
                    arSession.enabled &&
                    cameraManager != null &&
                    cameraManager.enabled;
            }
        }

        private InputDevice trackingDevice;
        private Quaternion currentTrackingRotation = Quaternion.identity;
        private bool hasCurrentTrackingRotation;
        private Quaternion enuFromTracking = Quaternion.identity;
        private bool hasTrackingMapping;
        private Quaternion latestSensorReference = Quaternion.identity;
        private bool hasLatestSensorReference;
        private Quaternion lastDeviceInEnu = Quaternion.identity;
        private bool hasLastDeviceInEnu;
        private bool rebaseWhenTrackingReturns;
        private int lastHardRebaseFrame = -1;
        private int lastTrackingUpdateFrame = -1;
        private float lastAcceptedTrackingSampleTime = float.NegativeInfinity;

        private void Awake()
        {
            ResolveReferences();
            Status = "AR tracking starting";
        }

        private void OnEnable()
        {
            ResolveReferences();
            Status = TrackingComponentsActive
                ? "AR tracking starting"
                : "AR camera off; live sensors active";
        }

        private void Update()
        {
            UpdateTrackingSample();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                lastTrackingUpdateFrame = -1;
                TrackingAvailable = false;
                TrackingFresh = false;
                hasCurrentTrackingRotation = false;
                trackingDevice = default;
                rebaseWhenTrackingReturns = NorthLockActive && hasLastDeviceInEnu;
                FailureReason = "Application paused";
                Status = NorthLockActive
                    ? "AR paused; live sensor north lock is retained"
                    : "AR tracking paused (optional)";
                return;
            }

            ResolveReferences();
            lastTrackingUpdateFrame = -1;
            rebaseWhenTrackingReturns = NorthLockActive && hasLastDeviceInEnu;
            Status = NorthLockActive
                ? "AR resuming; live sensors keep orientation active"
                : "AR tracking resuming (optional)";
        }

        public bool TryGetDeviceInEnu(
            Quaternion liveSensorReference,
            out Quaternion deviceInEnu)
        {
            if (!TryNormalizeRotation(ref liveSensorReference))
            {
                deviceInEnu = Quaternion.identity;
                FailureReason = "Live sensor reference is invalid";
                return false;
            }

            latestSensorReference = liveSensorReference;
            hasLatestSensorReference = true;
            if (NorthLockActive && !TrackingFresh)
            {
                // Keep following real motion while AR is absent. This is the pose
                // that a returning or reset tracking space must land on.
                RememberDevicePose(liveSensorReference);
                rebaseWhenTrackingReturns = true;
            }

            UpdateTrackingSample();
            if (!NorthLockActive ||
                !TrackingFresh ||
                !hasCurrentTrackingRotation)
            {
                deviceInEnu = Quaternion.identity;
                return false;
            }

            if (rebaseWhenTrackingReturns ||
                !hasTrackingMapping ||
                lastHardRebaseFrame == Time.frameCount)
            {
                RebaseTrackingTo(liveSensorReference);
            }
            else
            {
                Quaternion targetMapping =
                    liveSensorReference *
                    Quaternion.Inverse(currentTrackingRotation);
                float alignFactor = 1f - Mathf.Exp(
                    -Time.unscaledDeltaTime /
                    Mathf.Max(0.1f, sensorReferenceAlignSeconds));
                enuFromTracking = Quaternion.Slerp(
                    enuFromTracking,
                    targetMapping,
                    alignFactor);
            }

            deviceInEnu = enuFromTracking * currentTrackingRotation;
            if (!TryNormalizeRotation(ref deviceInEnu))
            {
                FailureReason = "Mapped AR pose is invalid";
                rebaseWhenTrackingReturns = true;
                return false;
            }

            RememberDevicePose(deviceInEnu);
            FailureReason = string.Empty;
            Status = "AR tracking active; blended to live sensors";
            return true;
        }

        // Compatibility path for callers that only want a currently tracked AR
        // pose. It deliberately never returns a frozen last pose.
        public bool TryGetDeviceInEnu(out Quaternion deviceInEnu, out bool frozen)
        {
            UpdateTrackingSample();
            frozen = false;
            if (!NorthLockActive ||
                !TrackingFresh ||
                !hasCurrentTrackingRotation ||
                !hasTrackingMapping)
            {
                deviceInEnu = Quaternion.identity;
                return false;
            }

            deviceInEnu = enuFromTracking * currentTrackingRotation;
            return TryNormalizeRotation(ref deviceInEnu);
        }

        public bool SetNorthLockFromSensor(
            Quaternion liveSensorReference,
            float headingCorrectionDegrees)
        {
            if (!TryNormalizeRotation(ref liveSensorReference) ||
                !IsFinite(headingCorrectionDegrees))
            {
                FailureReason = "Cannot set AR mapping from an invalid sensor pose";
                return false;
            }

            latestSensorReference = liveSensorReference;
            hasLatestSensorReference = true;
            NorthLockActive = true;
            HeadingCorrectionDegrees = NormalizeHeadingOffset(
                headingCorrectionDegrees);
            RememberDevicePose(liveSensorReference);

            UpdateTrackingSample();
            if (TrackingFresh && hasCurrentTrackingRotation)
            {
                RebaseTrackingTo(liveSensorReference);
                FailureReason = string.Empty;
                Status = "AR tracking rebased to live sensor north";
            }
            else
            {
                hasTrackingMapping = false;
                rebaseWhenTrackingReturns = true;
                Status = TrackingComponentsActive
                    ? "AR waiting; live sensor north lock remains active"
                    : "AR camera off; live sensor north lock remains active";
            }

            return true;
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

            if (hasLatestSensorReference)
            {
                latestSensorReference =
                    Quaternion.AngleAxis(degrees, Vector3.up) *
                    latestSensorReference;
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
            hasTrackingMapping = false;
            latestSensorReference = Quaternion.identity;
            hasLatestSensorReference = false;
            lastDeviceInEnu = Quaternion.identity;
            hasLastDeviceInEnu = false;
            rebaseWhenTrackingReturns = false;
            lastHardRebaseFrame = -1;
            FailureReason = string.Empty;
            Status = TrackingFresh
                ? "AR tracking ready (optional)"
                : TrackingComponentsActive
                    ? "AR tracking waiting (optional)"
                    : "AR camera off; live sensors active";
        }

        private bool TryGetFreshMappedRotation(out Quaternion mappedRotation)
        {
            UpdateTrackingSample();
            if (!TrackingFresh || !hasCurrentTrackingRotation)
            {
                mappedRotation = Quaternion.identity;
                return false;
            }

            if (NorthLockActive && !hasTrackingMapping)
            {
                if (!hasLatestSensorReference)
                {
                    mappedRotation = Quaternion.identity;
                    return false;
                }

                RebaseTrackingTo(latestSensorReference);
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
            hasTrackingMapping = true;
            rebaseWhenTrackingReturns = false;

            Quaternion mappedRotation =
                enuFromTracking * currentTrackingRotation;
            RememberDevicePose(mappedRotation);
            Status = "AR tracking active with north mapping";
        }

        private void UpdateTrackingSample()
        {
            if (lastTrackingUpdateFrame == Time.frameCount)
            {
                return;
            }

            lastTrackingUpdateFrame = Time.frameCount;
            if (!Application.isMobilePlatform)
            {
                TrackingAvailable = false;
                TrackingFresh = false;
                Status = "AR tracking is available in the Android build";
                FailureReason = "Not running on a mobile device";
                return;
            }

            ResolveReferences();
            if (arSession == null || cameraManager == null)
            {
                MarkTrackingUnavailable(
                    "AR components unavailable; live sensors active",
                    "ARSession or ARCameraManager is missing");
                return;
            }

            if (!TrackingComponentsActive)
            {
                MarkTrackingUnavailable(
                    NorthLockActive
                        ? "AR camera off; live sensor north lock remains active"
                        : "AR camera off; live sensors active",
                    "AR session is off");
                return;
            }

            ARSessionState sessionState = ARSession.state;
            bool sessionCanTrack =
                sessionState == ARSessionState.SessionTracking;
            TrackingAvailable = sessionState != ARSessionState.Unsupported &&
                sessionState != ARSessionState.None;

            if (!sessionCanTrack)
            {
                string reason = sessionState + "/" +
                    ARSession.notTrackingReason;
                MarkTrackingUnavailable(
                    "AR tracking waiting (" + reason +
                        "); live sensors active",
                    reason);
                return;
            }

            if (!TryReadTrackingRotation(out Quaternion nextRotation))
            {
                MarkTrackingUnavailable(
                    "AR pose unavailable; live sensors active",
                    "XR device pose is unavailable while the AR session is tracking");
                return;
            }

            TrackingAvailable = true;
            float sampleTime = Time.realtimeSinceStartup;
            bool trackingSpaceJump = NorthLockActive &&
                IsSilentTrackingSpaceDiscontinuity(nextRotation, sampleTime);

            currentTrackingRotation = nextRotation;
            hasCurrentTrackingRotation = true;
            TrackingFresh = true;
            lastAcceptedTrackingSampleTime = sampleTime;
            FailureReason = string.Empty;

            if (NorthLockActive &&
                (rebaseWhenTrackingReturns ||
                    !hasTrackingMapping ||
                    trackingSpaceJump))
            {
                if (hasLatestSensorReference)
                {
                    RebaseTrackingTo(latestSensorReference);
                }
                else if (hasLastDeviceInEnu)
                {
                    RebaseTrackingTo(lastDeviceInEnu);
                }
            }

            Status = NorthLockActive
                ? hasTrackingMapping
                    ? "AR tracking active; live sensor reference available"
                    : "AR tracking ready; waiting for live sensor reference"
                : "AR tracking ready (optional)";
        }

        private void MarkTrackingUnavailable(string status, string reason)
        {
            if (NorthLockActive)
            {
                if (hasLatestSensorReference)
                {
                    RememberDevicePose(latestSensorReference);
                }
                else if (TrackingFresh &&
                    hasCurrentTrackingRotation &&
                    hasTrackingMapping)
                {
                    RememberDevicePose(
                        enuFromTracking * currentTrackingRotation);
                }

                rebaseWhenTrackingReturns = true;
            }

            TrackingAvailable = false;
            TrackingFresh = false;
            hasCurrentTrackingRotation = false;
            trackingDevice = default;
            FailureReason = reason;
            Status = status;
        }

        private void RebaseTrackingTo(Quaternion deviceInEnu)
        {
            if (!hasCurrentTrackingRotation ||
                !TryNormalizeRotation(ref deviceInEnu))
            {
                hasTrackingMapping = false;
                rebaseWhenTrackingReturns = true;
                return;
            }

            enuFromTracking =
                deviceInEnu * Quaternion.Inverse(currentTrackingRotation);
            hasTrackingMapping = TryNormalizeRotation(ref enuFromTracking);
            rebaseWhenTrackingReturns = !hasTrackingMapping;
            if (hasTrackingMapping)
            {
                lastHardRebaseFrame = Time.frameCount;
                RememberDevicePose(deviceInEnu);
            }
        }

        private bool TryReadTrackingRotation(out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!trackingDevice.isValid)
            {
                trackingDevice =
                    InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            }

            if (!trackingDevice.isValid)
            {
                return false;
            }

            // Absence of an explicit tracked=true signal is not evidence of a
            // fresh pose. ARPoseDriver transforms can retain stale rotations
            // through tracking loss and relocalization, so never fall back to
            // the transform merely because XR omitted isTracked.
            if (!trackingDevice.TryGetFeatureValue(
                    CommonUsages.isTracked,
                    out bool isTracked) ||
                !isTracked)
            {
                return false;
            }

            return trackingDevice.TryGetFeatureValue(
                    CommonUsages.deviceRotation,
                    out rotation) &&
                TryNormalizeRotation(ref rotation);
        }

        private bool IsSilentTrackingSpaceDiscontinuity(
            Quaternion nextRotation,
            float sampleTime)
        {
            if (!TrackingFresh ||
                !hasCurrentTrackingRotation ||
                !IsFinite(lastAcceptedTrackingSampleTime))
            {
                return false;
            }

            float sampleGap = sampleTime - lastAcceptedTrackingSampleTime;
            if (sampleGap <= 0f ||
                sampleGap > SilentRelocalizationMaximumGapSeconds)
            {
                return false;
            }

            float credibleMotionLimit = Mathf.Max(
                SilentRelocalizationMinimumJumpDegrees,
                SilentRelocalizationAngleAllowanceDegrees +
                CredibleHandheldAngularSpeedDegreesPerSecond * sampleGap);
            return Quaternion.Angle(currentTrackingRotation, nextRotation) >
                credibleMotionLimit;
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

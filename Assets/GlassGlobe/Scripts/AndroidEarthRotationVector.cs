using System;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    internal struct AndroidRotationVectorSample
    {
        public Quaternion MotionDeviceInReference;
        public Quaternion EarthDeviceInEnu;
        public bool HasEarthReference;
        public bool UsesGameRotation;
        public float MotionSampleAgeSeconds;
        public float EarthSampleAgeSeconds;
        public int DisplayRotation;
        public float HeadingAccuracyDegrees;
        public int ProviderEpoch;
    }

    /// <summary>
    /// Polls Android's magnetometer-free motion vector and its separate
    /// magnetic-north reference, verifies both timestamps, and converts their
    /// screen-aligned bases into Unity coordinates.
    /// </summary>
    internal sealed class AndroidEarthRotationVector : IDisposable
    {
        private const string JavaClassName = "com.glassglobe.sensors.EarthRotationVectorProvider";
        private const float MaximumSampleAgeSeconds = 0.5f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass provider;
        private bool listening;
        private bool failureLogged;
#endif

        public bool IsSupported { get; private set; }
        public float LastSampleAgeSeconds { get; private set; } = float.PositiveInfinity;
        public float LastReferenceSampleAgeSeconds { get; private set; } = float.PositiveInfinity;
        public int StartAttemptCount { get; private set; }
        public int SuccessfulStartCount { get; private set; }

        public bool Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StartAttemptCount++;
            try
            {
                if (provider == null)
                {
                    provider = new AndroidJavaClass(JavaClassName);
                }

                AndroidJavaObject activity = AndroidApplication.currentActivity;
                if (activity == null)
                {
                    IsSupported = false;
                    return false;
                }

                bool wasListening = listening;
                listening = provider.CallStatic<bool>("start", activity);
                if (listening && !wasListening)
                {
                    SuccessfulStartCount++;
                }
                IsSupported = listening;
                return listening;
            }
            catch (Exception exception)
            {
                LogFailureOnce("start", exception);
                listening = false;
                IsSupported = false;
                return false;
            }
#else
            IsSupported = false;
            return false;
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (provider == null)
            {
                return;
            }

            try
            {
                provider.CallStatic("stop");
            }
            catch (Exception exception)
            {
                LogFailureOnce("stop", exception);
            }
            finally
            {
                listening = false;
                LastSampleAgeSeconds = float.PositiveInfinity;
                LastReferenceSampleAgeSeconds = float.PositiveInfinity;
            }
#endif
        }

        public bool TryGetRotation(out AndroidRotationVectorSample sample)
        {
            sample = default(AndroidRotationVectorSample);
            sample.MotionDeviceInReference = Quaternion.identity;
            sample.EarthDeviceInEnu = Quaternion.identity;
            sample.EarthSampleAgeSeconds = float.PositiveInfinity;
            sample.DisplayRotation = -1;
            sample.HeadingAccuracyDegrees = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (provider == null || !listening)
            {
                return false;
            }

            try
            {
                float[] snapshot = provider.CallStatic<float[]>("snapshot");
                if (snapshot == null || snapshot.Length < 24)
                {
                    return false;
                }

                float sampleAgeSeconds = snapshot[9];
                LastSampleAgeSeconds = sampleAgeSeconds;
                if (!IsFinite(sampleAgeSeconds) ||
                    sampleAgeSeconds < 0f ||
                    sampleAgeSeconds > MaximumSampleAgeSeconds)
                {
                    return false;
                }

                if (!TryConvertRotationMatrix(
                        snapshot,
                        0,
                        out Quaternion motionDeviceInReference))
                {
                    return false;
                }

                sample.MotionDeviceInReference = motionDeviceInReference;
                sample.MotionSampleAgeSeconds = sampleAgeSeconds;
                sample.DisplayRotation = Mathf.RoundToInt(snapshot[10]);
                sample.UsesGameRotation = snapshot[11] >= 0.5f;

                float referenceAgeSeconds = snapshot[21];
                LastReferenceSampleAgeSeconds = referenceAgeSeconds;
                if (IsFinite(referenceAgeSeconds) &&
                    referenceAgeSeconds >= 0f &&
                    referenceAgeSeconds <= MaximumSampleAgeSeconds &&
                    TryConvertRotationMatrix(
                        snapshot,
                        12,
                        out Quaternion earthDeviceInEnu))
                {
                    sample.EarthDeviceInEnu = earthDeviceInEnu;
                    sample.EarthSampleAgeSeconds = referenceAgeSeconds;
                    sample.HasEarthReference = true;
                }

                float headingAccuracyRadians = snapshot[22];
                if (IsFinite(headingAccuracyRadians) &&
                    headingAccuracyRadians >= 0f)
                {
                    sample.HeadingAccuracyDegrees =
                        headingAccuracyRadians * Mathf.Rad2Deg;
                }

                sample.ProviderEpoch = Mathf.RoundToInt(snapshot[23]);
                return true;
            }
            catch (Exception exception)
            {
                LogFailureOnce("read", exception);
                Stop();
                IsSupported = false;
                return false;
            }
#else
            return false;
#endif
        }

        internal static bool TryConvertRotationMatrix(float[] matrix, out Quaternion deviceInEnu)
        {
            return TryConvertRotationMatrix(matrix, 0, out deviceInEnu);
        }

        private static bool TryConvertRotationMatrix(
            float[] matrix,
            int offset,
            out Quaternion deviceInEnu)
        {
            deviceInEnu = Quaternion.identity;
            if (matrix == null || offset < 0 || matrix.Length < offset + 9)
            {
                return false;
            }

            for (int index = 0; index < 9; index++)
            {
                if (!IsFinite(matrix[offset + index]))
                {
                    return false;
                }
            }

            // Android's row-major matrix maps screen-aligned device axes into
            // world (east, magnetic north, sky). GlassGlobe's ENU order is
            // (east, sky, north), and the rear
            // camera looks opposite Android's +Z screen-normal axis.
            Vector3 cameraForward = new Vector3(
                -matrix[offset + 2],
                -matrix[offset + 8],
                -matrix[offset + 5]);
            Vector3 screenUp = new Vector3(
                matrix[offset + 1],
                matrix[offset + 7],
                matrix[offset + 4]);
            if (cameraForward.sqrMagnitude < 0.5f || screenUp.sqrMagnitude < 0.5f)
            {
                return false;
            }

            cameraForward.Normalize();
            screenUp = Vector3.ProjectOnPlane(screenUp, cameraForward);
            if (screenUp.sqrMagnitude < 0.5f)
            {
                return false;
            }

            screenUp.Normalize();
            Quaternion converted = Quaternion.LookRotation(cameraForward, screenUp);
            if (!IsFinite(converted.x) ||
                !IsFinite(converted.y) ||
                !IsFinite(converted.z) ||
                !IsFinite(converted.w))
            {
                return false;
            }

            deviceInEnu = converted;
            return true;
        }

        public void Dispose()
        {
            Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (provider != null)
            {
                provider.Dispose();
                provider = null;
            }
#endif
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LogFailureOnce(string operation, Exception exception)
        {
            if (failureLogged)
            {
                return;
            }

            failureLogged = true;
            Debug.LogWarning(
                "GlassGlobeSensors: Android earth rotation vector " + operation +
                " failed; the timestamped native sensor will retry. " +
                exception.Message);
        }
#endif
    }
}

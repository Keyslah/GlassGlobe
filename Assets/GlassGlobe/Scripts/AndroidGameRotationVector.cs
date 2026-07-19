using System;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Polls Android's magnetometer-free game rotation vector and converts its
    /// screen-aligned device basis into GlassGlobe's Unity ENU basis.
    /// </summary>
    internal sealed class AndroidGameRotationVector : IDisposable
    {
        private const string JavaClassName = "com.glassglobe.sensors.GameRotationVectorProvider";
        private const float MaximumSampleAgeSeconds = 0.5f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass provider;
        private bool listening;
        private bool failureLogged;
#endif

        public bool IsSupported { get; private set; }

        public bool Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
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

                listening = provider.CallStatic<bool>("start", activity);
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
            }
#endif
        }

        public bool TryGetRotation(out Quaternion deviceInEnu, out int displayRotation)
        {
            deviceInEnu = Quaternion.identity;
            displayRotation = -1;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (provider == null || !listening)
            {
                return false;
            }

            try
            {
                float[] snapshot = provider.CallStatic<float[]>("snapshot");
                if (snapshot == null || snapshot.Length < 11)
                {
                    return false;
                }

                float sampleAgeSeconds = snapshot[9];
                if (!IsFinite(sampleAgeSeconds) ||
                    sampleAgeSeconds < 0f ||
                    sampleAgeSeconds > MaximumSampleAgeSeconds)
                {
                    return false;
                }

                displayRotation = Mathf.RoundToInt(snapshot[10]);
                return TryConvertRotationMatrix(snapshot, out deviceInEnu);
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
            deviceInEnu = Quaternion.identity;
            if (matrix == null || matrix.Length < 9)
            {
                return false;
            }

            for (int index = 0; index < 9; index++)
            {
                if (!IsFinite(matrix[index]))
                {
                    return false;
                }
            }

            // Android's row-major matrix maps screen-aligned device axes into
            // world (east, arbitrary-horizontal-reference, sky). GlassGlobe's
            // ENU order is (east, sky, horizontal-reference), and the rear
            // camera looks opposite Android's +Z screen-normal axis.
            Vector3 cameraForward = new Vector3(-matrix[2], -matrix[8], -matrix[5]);
            Vector3 screenUp = new Vector3(matrix[1], matrix[7], matrix[4]);
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
                "GlassGlobeSensors: Android game rotation vector " + operation +
                " failed; gyro north lock is unavailable. " + exception.Message);
        }
#endif
    }
}

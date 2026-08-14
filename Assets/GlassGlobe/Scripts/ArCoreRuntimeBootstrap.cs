using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlassGlobe
{
    /// <summary>
    /// Adds the AR Foundation runtime rig to generated preview scenes. Tracking
    /// uses a dedicated XR camera, while the existing GlassGlobe camera remains
    /// responsible for rendering the Earth, Milky Way, and other scene content.
    /// </summary>
    public static class ArCoreRuntimeBootstrap
    {
        private const string HiddenTrackingDefaultMigrationKey =
            "GlassGlobe.Settings.HiddenTrackingDefaultV1";
        private const string TrackingOriginName =
            "GlassGlobe AR Tracking Origin";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Application.isMobilePlatform)
            {
                return;
            }

            PhonePoseSensors poseSensors =
                Object.FindFirstObjectByType<PhonePoseSensors>();
            Camera targetCamera = poseSensors != null && poseSensors.targetCamera != null
                ? poseSensors.targetCamera
                : Camera.main;
            if (poseSensors == null || targetCamera == null)
            {
                Debug.LogWarning(
                    "GlassGlobe AR bootstrap: PhonePoseSensors or Main Camera is missing.");
                return;
            }

            ARSession arSession = Object.FindFirstObjectByType<ARSession>();
            GameObject sessionObject;
            bool activateSessionObjectAfterSetup = false;
            if (arSession == null)
            {
                sessionObject = new GameObject("GlassGlobe AR Session");
                sessionObject.SetActive(false);
                activateSessionObjectAfterSetup = true;
                arSession = sessionObject.AddComponent<ARSession>();
            }
            else
            {
                sessionObject = arSession.gameObject;
            }

            // AR is an optional camera feature, not the orientation authority.
            // Configure it while inactive so startup never waits for or briefly
            // opens an AR camera frame before the display controller asks for it.
            arSession.enabled = false;
            arSession.matchFrameRateRequested = false;

            ARInputManager inputManager =
                sessionObject.GetComponent<ARInputManager>();
            if (inputManager == null)
            {
                inputManager = sessionObject.AddComponent<ARInputManager>();
            }

            inputManager.enabled = false;
            arSession.attemptUpdate = true;

            XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
            Camera trackingCamera = null;
            GameObject createdOriginObject = null;
            if (xrOrigin != null)
            {
                trackingCamera = xrOrigin.Camera != null
                    ? xrOrigin.Camera
                    : xrOrigin.GetComponentInChildren<Camera>(true);
            }

            if (xrOrigin == null || trackingCamera == null)
            {
                GameObject originObject = new GameObject(TrackingOriginName);
                originObject.SetActive(false);
                createdOriginObject = originObject;
                GameObject offsetObject = new GameObject("Camera Offset");
                offsetObject.transform.SetParent(originObject.transform, false);

                GameObject cameraObject = new GameObject("AR Tracking Camera");
                cameraObject.transform.SetParent(offsetObject.transform, false);
                trackingCamera = cameraObject.AddComponent<Camera>();
                trackingCamera.enabled = false;
                trackingCamera.cullingMask = 0;
                trackingCamera.clearFlags = CameraClearFlags.Depth;
                trackingCamera.depth = targetCamera.depth - 1f;
                trackingCamera.allowHDR = false;
                trackingCamera.allowMSAA = false;

#pragma warning disable 0618
                cameraObject.AddComponent<ARPoseDriver>();
#pragma warning restore 0618

                xrOrigin = originObject.AddComponent<XROrigin>();
                xrOrigin.Origin = originObject;
                xrOrigin.CameraFloorOffsetObject = offsetObject;
                xrOrigin.Camera = trackingCamera;
            }

            ARCameraManager cameraManager =
                trackingCamera.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager =
                    trackingCamera.gameObject.AddComponent<ARCameraManager>();
            }

            cameraManager.enabled = false;

            ARCameraBackground cameraBackground =
                trackingCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground =
                    trackingCamera.gameObject.AddComponent<ARCameraBackground>();
            }

            // The display controller enables the session, input, camera manager,
            // background, and tracking camera together only when Camera is on.
            cameraBackground.enabled = false;
            trackingCamera.enabled = false;

            if (createdOriginObject != null)
            {
                createdOriginObject.SetActive(true);
            }

            if (activateSessionObjectAfterSetup)
            {
                sessionObject.SetActive(true);
            }

            ArCoreOrientationTracker tracker =
                poseSensors.GetComponent<ArCoreOrientationTracker>();
            if (tracker == null)
            {
                tracker =
                    poseSensors.gameObject.AddComponent<ArCoreOrientationTracker>();
            }

            tracker.arSession = arSession;
            tracker.cameraManager = cameraManager;
            tracker.trackingPose = trackingCamera.transform;
            tracker.enabled = true;
            poseSensors.arCoreTracking = tracker;

            GlassGlobeSettingsState.Load();
            if (!PlayerPrefs.HasKey(HiddenTrackingDefaultMigrationKey))
            {
                // Existing installs used the camera image as the default. Move
                // everyone once to the sensor-only globe view; the Camera toggle
                // can still start AR and show the live image later.
                GlassGlobeSettingsState.SetCameraFeedEnabled(false);
                PlayerPrefs.SetInt(HiddenTrackingDefaultMigrationKey, 1);
                PlayerPrefs.Save();
            }

            CameraFeedRenderer cameraFeed =
                Object.FindFirstObjectByType<CameraFeedRenderer>();
            if (cameraFeed != null)
            {
                cameraFeed.targetCamera = targetCamera;
                cameraFeed.poseSensors = poseSensors;
                cameraFeed.arSession = arSession;
                cameraFeed.arInputManager = inputManager;
                cameraFeed.arTrackingCamera = trackingCamera;
                cameraFeed.arCameraManager = cameraManager;
                cameraFeed.arCameraBackground = cameraBackground;
                cameraFeed.SetFeedWanted(
                    GlassGlobeSettingsState.CameraFeedEnabled);
            }

            Debug.Log(
                "GlassGlobe AR bootstrap: optional XR tracking installed; " +
                (arSession.enabled && cameraManager.enabled
                    ? "camera requested by the display controller."
                    : "camera remains off while the feed is hidden.") +
                " Frame-rate matching is disabled.");
        }
    }
}

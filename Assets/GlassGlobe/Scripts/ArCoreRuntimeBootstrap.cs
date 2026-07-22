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
            if (arSession == null)
            {
                sessionObject = new GameObject("GlassGlobe AR Session");
                arSession = sessionObject.AddComponent<ARSession>();
            }
            else
            {
                sessionObject = arSession.gameObject;
            }

            if (sessionObject.GetComponent<ARInputManager>() == null)
            {
                sessionObject.AddComponent<ARInputManager>();
            }

            arSession.attemptUpdate = true;
            arSession.enabled = true;

            XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
            Camera trackingCamera = null;
            if (xrOrigin != null)
            {
                trackingCamera = xrOrigin.Camera != null
                    ? xrOrigin.Camera
                    : xrOrigin.GetComponentInChildren<Camera>(true);
            }

            if (xrOrigin == null || trackingCamera == null)
            {
                GameObject originObject = new GameObject(TrackingOriginName);
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

            cameraManager.enabled = true;

            ARCameraBackground cameraBackground =
                trackingCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground =
                    trackingCamera.gameObject.AddComponent<ARCameraBackground>();
            }

            // ARCore keeps receiving camera frames through ARCameraManager even
            // while this render component and its dedicated Camera are disabled.
            cameraBackground.enabled = false;
            trackingCamera.enabled = false;

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
                // everyone once to hidden-camera tracking; the display toggle can
                // still show the live camera image later.
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
                cameraFeed.arTrackingCamera = trackingCamera;
                cameraFeed.arCameraManager = cameraManager;
                cameraFeed.arCameraBackground = cameraBackground;
                cameraFeed.SetFeedWanted(
                    GlassGlobeSettingsState.CameraFeedEnabled);
            }

            Debug.Log(
                "GlassGlobe AR bootstrap: XR Origin and visual tracking active; camera background " +
                (cameraBackground.enabled ? "visible." : "hidden."));
        }
    }
}

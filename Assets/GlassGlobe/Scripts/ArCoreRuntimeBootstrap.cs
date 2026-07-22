using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GlassGlobe
{
    /// <summary>
    /// Adds the small AR Foundation runtime rig to generated preview scenes.
    /// GlassGlobe scenes are rebuilt from editor code, so doing this at runtime
    /// also keeps older saved scenes usable after the tracking upgrade.
    /// </summary>
    public static class ArCoreRuntimeBootstrap
    {
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
            if (arSession == null)
            {
                GameObject sessionObject = new GameObject("GlassGlobe AR Session");
                arSession = sessionObject.AddComponent<ARSession>();
                sessionObject.AddComponent<ARInputManager>();
            }

            arSession.attemptUpdate = true;
            arSession.enabled = true;

            ARCameraManager cameraManager =
                targetCamera.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = targetCamera.gameObject.AddComponent<ARCameraManager>();
            }

            cameraManager.enabled = true;

            ARCameraBackground cameraBackground =
                targetCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground =
                    targetCamera.gameObject.AddComponent<ARCameraBackground>();
            }

            // Tracking uses the camera frames through ARCameraManager. The image
            // itself starts hidden so GlassGlobe's Milky Way and Earth are drawn.
            cameraBackground.enabled = false;

            ArCoreOrientationTracker tracker =
                poseSensors.GetComponent<ArCoreOrientationTracker>();
            if (tracker == null)
            {
                tracker = poseSensors.gameObject.AddComponent<ArCoreOrientationTracker>();
            }

            tracker.arSession = arSession;
            tracker.cameraManager = cameraManager;
            tracker.enabled = true;
            poseSensors.arCoreTracking = tracker;

            CameraFeedRenderer cameraFeed =
                Object.FindFirstObjectByType<CameraFeedRenderer>();
            if (cameraFeed != null)
            {
                cameraFeed.targetCamera = targetCamera;
                cameraFeed.poseSensors = poseSensors;
                cameraFeed.arCameraManager = cameraManager;
                cameraFeed.arCameraBackground = cameraBackground;
                cameraFeed.SetFeedWanted(
                    GlassGlobeSettingsState.CameraFeedEnabled);
            }

            Debug.Log(
                "GlassGlobe AR bootstrap: visual tracking active; camera background " +
                (cameraBackground.enabled ? "visible." : "hidden."));
        }
    }
}

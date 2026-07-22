using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Controls only whether the live camera image is drawn. When AR Foundation
    /// is present, ARCameraManager stays enabled for visual-inertial tracking even
    /// while ARCameraBackground is disabled and GlassGlobe renders its own sky.
    /// The legacy WebCamTexture quad remains as a non-AR fallback.
    /// </summary>
    public sealed class CameraFeedRenderer : MonoBehaviour
    {
        public Camera targetCamera;
        public PhonePoseSensors poseSensors;
        public ARCameraManager arCameraManager;
        public ARCameraBackground arCameraBackground;

        [Tooltip("Assigned at scene build time so the legacy feed shader is not stripped from fallback builds.")]
        public Material feedMaterial;

        [Tooltip("Vertical FOV used while the live camera background is visible.")]
        [Range(30f, 100f)]
        public float feedVerticalFovDegrees = 70f;

        [Range(20f, 100f)]
        public float windowFovDegrees = 32.4f;

        [Tooltip("Optional camera device name used only by the legacy WebCamTexture fallback.")]
        public string preferredDeviceName = string.Empty;

        public bool startEnabledOnDevice = false;

        [Min(1f)]
        public float quadDistance = 60f;

        public bool FeedActive { get; private set; }
        public bool TrackingActive
        {
            get { return arCameraManager != null && arCameraManager.enabled; }
        }
        public string FeedStatus { get; private set; }

        private WebCamTexture webCamTexture;
        private Transform quadTransform;
        private bool permissionRequested;
        private bool wantFeed;
        private bool devicesLogged;
        private float savedWindowFovDegrees;
        private bool hasSavedWindowFov;

        private void Awake()
        {
            ResolveArReferences();
            wantFeed = startEnabledOnDevice;

            if (arCameraManager != null)
            {
                arCameraManager.enabled = true;
            }

            if (arCameraBackground != null)
            {
                arCameraBackground.enabled = wantFeed;
            }

            FeedActive = wantFeed && arCameraBackground != null &&
                arCameraBackground.enabled;
            FeedStatus = arCameraManager != null
                ? FeedActive ? "AR visible" : "AR tracking hidden"
                : "Off";

            if (!Application.isMobilePlatform)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            ResolveArReferences();
            if (arCameraManager != null)
            {
                UpdateArFoundationDisplay();
                return;
            }

            UpdateLegacyWebCamDisplay();
        }

        public void ToggleFeed()
        {
            SetFeedWanted(!wantFeed);
        }

        public void SetFeedWanted(bool value)
        {
            wantFeed = value;
            ResolveArReferences();

            if (arCameraManager != null)
            {
                arCameraManager.enabled = true;
                if (arCameraBackground != null)
                {
                    arCameraBackground.enabled = wantFeed;
                }

                FeedActive = wantFeed && arCameraBackground != null &&
                    arCameraBackground.enabled;
                FeedStatus = FeedActive ? "AR visible" : "AR tracking hidden";
                StopLegacyFeed();
                ApplyFov();
                return;
            }

            if (!value)
            {
                StopLegacyFeed();
                FeedActive = false;
                FeedStatus = "Off";
            }

            ApplyFov();
        }

        private void UpdateArFoundationDisplay()
        {
            // Never disable ARCameraManager here. It supplies camera frames to
            // ARCore for tracking whether or not those frames are shown.
            if (!arCameraManager.enabled)
            {
                arCameraManager.enabled = true;
            }

            if (arCameraBackground != null && arCameraBackground.enabled != wantFeed)
            {
                arCameraBackground.enabled = wantFeed;
            }

            FeedActive = wantFeed && arCameraBackground != null &&
                arCameraBackground.enabled;
            if (FeedActive)
            {
                FeedStatus = "AR visible";
            }
            else
            {
                ARSessionState state = ARSession.state;
                FeedStatus = state == ARSessionState.SessionTracking
                    ? "AR tracking hidden"
                    : "AR starting hidden";
            }

            StopLegacyFeed();
            ApplyFov();
        }

        private void UpdateLegacyWebCamDisplay()
        {
            if (!wantFeed)
            {
                if (FeedActive ||
                    (webCamTexture != null && webCamTexture.isPlaying) ||
                    FeedStatus != "Off")
                {
                    StopLegacyFeed();
                    FeedActive = false;
                    FeedStatus = "Off";
                    ApplyFov();
                }

                return;
            }

            if (!HasCameraPermission())
            {
                FeedStatus = "Waiting for camera permission";
                if (!permissionRequested)
                {
                    permissionRequested = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                    Permission.RequestUserPermission(Permission.Camera);
#endif
                }

                return;
            }

            if (webCamTexture == null && !TryStartWebCam())
            {
                return;
            }

            if (webCamTexture != null && !webCamTexture.isPlaying)
            {
                webCamTexture.Play();
            }

            if (webCamTexture == null || webCamTexture.width <= 16)
            {
                FeedStatus = "Starting camera...";
                return;
            }

            if (!FeedActive)
            {
                FeedActive = true;
                ApplyFov();
            }

            FeedStatus = string.Format(
                "Camera {0}x{1}",
                webCamTexture.width,
                webCamTexture.height);
            EnsureQuad();
            UpdateQuad();
        }

        private void ApplyFov()
        {
            if (poseSensors == null)
            {
                return;
            }

            if (wantFeed && FeedActive)
            {
                if (!hasSavedWindowFov)
                {
                    savedWindowFovDegrees = poseSensors.cameraFovDegrees;
                    hasSavedWindowFov = true;
                }

                poseSensors.cameraFovDegrees = feedVerticalFovDegrees;
            }
            else if (hasSavedWindowFov)
            {
                poseSensors.cameraFovDegrees = savedWindowFovDegrees;
                hasSavedWindowFov = false;
            }
            else if (!wantFeed)
            {
                poseSensors.cameraFovDegrees = windowFovDegrees;
            }
        }

        private void ResolveArReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (targetCamera == null)
            {
                return;
            }

            if (arCameraManager == null)
            {
                arCameraManager = targetCamera.GetComponent<ARCameraManager>();
            }

            if (arCameraBackground == null)
            {
                arCameraBackground = targetCamera.GetComponent<ARCameraBackground>();
            }
        }

        private bool HasCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
            return true;
#endif
        }

        private bool TryStartWebCam()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                FeedStatus = "No camera devices found";
                return false;
            }

            if (!devicesLogged)
            {
                devicesLogged = true;
                System.Text.StringBuilder deviceList = new System.Text.StringBuilder();
                for (int index = 0; index < devices.Length; index++)
                {
                    if (index > 0)
                    {
                        deviceList.Append(", ");
                    }

                    deviceList.Append(devices[index].name)
                        .Append(devices[index].isFrontFacing ? " (front)" : " (rear)");
                }

                Debug.Log(
                    "GlassGlobeCameraFeed: legacy devices: " + deviceList +
                    ". AR Foundation is preferred when available.");
            }

            string deviceName = SelectDeviceName(devices);
            webCamTexture = new WebCamTexture(deviceName, 1280, 720, 30);
            webCamTexture.Play();
            FeedStatus = "Starting camera...";
            return true;
        }

        private string SelectDeviceName(WebCamDevice[] devices)
        {
            if (!string.IsNullOrEmpty(preferredDeviceName))
            {
                foreach (WebCamDevice device in devices)
                {
                    if (device.name.IndexOf(
                            preferredDeviceName,
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return device.name;
                    }
                }
            }

            foreach (WebCamDevice device in devices)
            {
                if (!device.isFrontFacing)
                {
                    return device.name;
                }
            }

            return devices[0].name;
        }

        private void StopLegacyFeed()
        {
            if (webCamTexture != null && webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }

            if (quadTransform != null)
            {
                quadTransform.gameObject.SetActive(false);
            }
        }

        private void EnsureQuad()
        {
            if (quadTransform != null)
            {
                quadTransform.gameObject.SetActive(true);
                return;
            }

            if (feedMaterial == null)
            {
                Shader shader = Shader.Find("GlassGlobe/Camera Feed");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Texture");
                }

                if (shader == null)
                {
                    FeedStatus = "Feed shader missing from build";
                    return;
                }

                feedMaterial = new Material(shader);
            }

            GameObject quadObject = new GameObject("Camera Feed Quad");
            quadObject.transform.SetParent(ResolveCameraTransform(), false);
            quadObject.transform.localPosition = new Vector3(0f, 0f, quadDistance);

            MeshFilter meshFilter = quadObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh =
                GlassGlobeVisuals.BuildQuadMesh("GlassGlobe Camera Feed Quad");

            MeshRenderer meshRenderer = quadObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            feedMaterial.mainTexture = webCamTexture;
            meshRenderer.sharedMaterial = feedMaterial;
            quadTransform = quadObject.transform;
        }

        private void UpdateQuad()
        {
            if (quadTransform == null || webCamTexture == null)
            {
                return;
            }

            if (feedMaterial != null && feedMaterial.mainTexture != webCamTexture)
            {
                feedMaterial.mainTexture = webCamTexture;
            }

            int rotation = webCamTexture.videoRotationAngle;
            bool rotated = rotation == 90 || rotation == 270;
            quadTransform.localRotation = Quaternion.Euler(0f, 0f, -rotation);

            float verticalSize = 2f * quadDistance * Mathf.Tan(
                feedVerticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float textureAspect =
                (float)webCamTexture.height / Mathf.Max(1, webCamTexture.width);

            Vector3 scale = rotated
                ? new Vector3(verticalSize, verticalSize * textureAspect, 1f)
                : new Vector3(
                    verticalSize / Mathf.Max(0.0001f, textureAspect),
                    verticalSize,
                    1f);

            if (webCamTexture.videoVerticallyMirrored)
            {
                scale.y = -scale.y;
            }

            quadTransform.localScale = scale;
        }

        private Transform ResolveCameraTransform()
        {
            if (targetCamera != null)
            {
                return targetCamera.transform;
            }

            Camera childCamera = GetComponentInChildren<Camera>();
            return childCamera != null ? childCamera.transform : transform;
        }
    }
}

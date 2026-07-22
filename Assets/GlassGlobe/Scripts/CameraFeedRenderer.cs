using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Controls only whether the live camera image is drawn. ARCameraManager stays
    /// enabled for visual-inertial tracking even while the dedicated AR camera and
    /// ARCameraBackground are hidden, leaving GlassGlobe's own sky visible.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CameraFeedRenderer : MonoBehaviour
    {
        public Camera targetCamera;
        public PhonePoseSensors poseSensors;
        public Camera arTrackingCamera;
        public ARCameraManager arCameraManager;
        public ARCameraBackground arCameraBackground;

        [Tooltip("Assigned at scene build time so the legacy feed shader is not stripped from fallback builds.")]
        public Material feedMaterial;

        [Tooltip("Approximate vertical FOV shown in the HUD while the live camera background is visible. ARCore's projection matrix remains authoritative.")]
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
        private CameraClearFlags savedTargetClearFlags;
        private bool targetRenderStateOverridden;

        private void Awake()
        {
            ResolveArReferences();
            wantFeed = startEnabledOnDevice;
            FeedStatus = "Off";

            if (arCameraManager != null)
            {
                arCameraManager.enabled = true;
            }

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

        private void LateUpdate()
        {
            if (!FeedActive ||
                arTrackingCamera == null ||
                targetCamera == null)
            {
                return;
            }

            // ARCameraManager updates the tracking camera's projection from the
            // physical lens. Copy it after pose updates so the GlassGlobe overlay
            // remains aligned whenever the live image is intentionally visible.
            targetCamera.projectionMatrix = arTrackingCamera.projectionMatrix;
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
                UpdateArFoundationDisplay();
                return;
            }

            if (!value)
            {
                StopLegacyFeed();
                SetFeedActive(false);
                FeedStatus = "Off";
            }
        }

        private void UpdateArFoundationDisplay()
        {
            // Camera capture and tracking stay alive regardless of visibility.
            if (!arCameraManager.enabled)
            {
                arCameraManager.enabled = true;
            }

            bool displayReady =
                arTrackingCamera != null && arCameraBackground != null;

            if (arCameraBackground != null &&
                arCameraBackground.enabled != wantFeed)
            {
                arCameraBackground.enabled = wantFeed;
            }

            if (arTrackingCamera != null)
            {
                arTrackingCamera.cullingMask = 0;
                arTrackingCamera.depth = targetCamera != null
                    ? targetCamera.depth - 1f
                    : -2f;
                if (targetCamera != null)
                {
                    arTrackingCamera.rect = targetCamera.rect;
                    arTrackingCamera.targetDisplay = targetCamera.targetDisplay;
                }

                if (arTrackingCamera.enabled != wantFeed)
                {
                    arTrackingCamera.enabled = wantFeed;
                }
            }

            bool imageVisible = wantFeed && displayReady;
            SetFeedActive(imageVisible);

            if (imageVisible)
            {
                OverrideTargetRenderState();
                FeedStatus = ARSession.state == ARSessionState.SessionTracking
                    ? "AR visible"
                    : "AR starting visible";
            }
            else
            {
                RestoreTargetRenderState();
                if (wantFeed)
                {
                    FeedStatus = "AR display unavailable; tracking active";
                }
                else
                {
                    FeedStatus = ARSession.state == ARSessionState.SessionTracking
                        ? "AR tracking hidden"
                        : "AR starting hidden";
                }
            }

            // ARCore owns the camera hardware. Never start the competing legacy
            // WebCamTexture path once the AR camera subsystem is available.
            StopLegacyFeed();
        }

        private void OverrideTargetRenderState()
        {
            if (targetCamera == null)
            {
                return;
            }

            if (!targetRenderStateOverridden)
            {
                savedTargetClearFlags = targetCamera.clearFlags;
                targetRenderStateOverridden = true;
            }

            // The lower-depth AR camera supplies color. GlassGlobe clears only
            // depth, then renders the Earth, Milky Way, labels, and overlays.
            targetCamera.clearFlags = CameraClearFlags.Depth;
        }

        private void RestoreTargetRenderState()
        {
            if (targetCamera == null || !targetRenderStateOverridden)
            {
                return;
            }

            targetCamera.clearFlags = savedTargetClearFlags;
            targetCamera.ResetProjectionMatrix();
            targetRenderStateOverridden = false;
        }

        private void SetFeedActive(bool active)
        {
            if (FeedActive == active)
            {
                return;
            }

            FeedActive = active;
            if (poseSensors == null)
            {
                return;
            }

            if (active)
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
        }

        private void UpdateLegacyWebCamDisplay()
        {
            if (!wantFeed)
            {
                StopLegacyFeed();
                SetFeedActive(false);
                FeedStatus = "Off";
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

            SetFeedActive(true);
            FeedStatus = string.Format(
                "Camera {0}x{1}",
                webCamTexture.width,
                webCamTexture.height);
            EnsureQuad();
            UpdateQuad();
        }

        private void ResolveArReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (arCameraManager == null)
            {
                arCameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (arTrackingCamera == null && arCameraManager != null)
            {
                arTrackingCamera = arCameraManager.GetComponent<Camera>();
            }

            if (arCameraBackground == null && arTrackingCamera != null)
            {
                arCameraBackground =
                    arTrackingCamera.GetComponent<ARCameraBackground>();
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
                System.Text.StringBuilder deviceList =
                    new System.Text.StringBuilder();
                for (int index = 0; index < devices.Length; index++)
                {
                    if (index > 0)
                    {
                        deviceList.Append(", ");
                    }

                    deviceList.Append(devices[index].name)
                        .Append(devices[index].isFrontFacing
                            ? " (front)"
                            : " (rear)");
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
            quadObject.transform.localPosition =
                new Vector3(0f, 0f, quadDistance);

            MeshFilter meshFilter = quadObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh =
                GlassGlobeVisuals.BuildQuadMesh(
                    "GlassGlobe Camera Feed Quad");

            MeshRenderer meshRenderer =
                quadObject.AddComponent<MeshRenderer>();
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

            if (feedMaterial != null &&
                feedMaterial.mainTexture != webCamTexture)
            {
                feedMaterial.mainTexture = webCamTexture;
            }

            int rotation = webCamTexture.videoRotationAngle;
            bool rotated = rotation == 90 || rotation == 270;
            quadTransform.localRotation =
                Quaternion.Euler(0f, 0f, -rotation);

            float verticalSize =
                2f * quadDistance * Mathf.Tan(
                    feedVerticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float textureAspect =
                (float)webCamTexture.height /
                Mathf.Max(1, webCamTexture.width);

            Vector3 scale = rotated
                ? new Vector3(
                    verticalSize,
                    verticalSize * textureAspect,
                    1f)
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

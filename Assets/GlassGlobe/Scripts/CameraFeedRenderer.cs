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

        [Tooltip("Vertical FOV used only by the legacy WebCamTexture fallback. ARCore supplies its physical FOV at runtime.")]
        [Range(30f, 100f)]
        public float feedVerticalFovDegrees = 70f;

        [Range(PhonePoseSimulator.MinimumViewportFovDegrees, 100f)]
        public float windowFovDegrees = PhonePoseSimulator.DefaultViewportFovDegrees;

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
        public float ViewportFovDegrees
        {
            get
            {
                if (FeedActive && arCameraManager != null)
                {
                    float baseFov = CurrentArBaseFovDegrees();
                    return feedDesiredFovInitialized
                        ? feedDesiredFovDegrees
                        : ClampFeedFov(windowFovDegrees, baseFov);
                }

                if (poseSensors != null)
                {
                    return poseSensors.cameraFovDegrees;
                }

                return windowFovDegrees;
            }
        }
        public float NativeViewportFovDegrees
        {
            get
            {
                if (hasArProjectionMatrix)
                {
                    return ProjectionVerticalFov(latestArProjectionMatrix);
                }

                if (arTrackingCamera != null)
                {
                    return ProjectionVerticalFov(arTrackingCamera.projectionMatrix);
                }

                return feedVerticalFovDegrees;
            }
        }

        private WebCamTexture webCamTexture;
        private Transform quadTransform;
        private bool permissionRequested;
        private bool wantFeed;
        private bool devicesLogged;
        private CameraClearFlags savedTargetClearFlags;
        private bool targetRenderStateOverridden;
        private ARCameraManager subscribedCameraManager;
        private Matrix4x4 latestArProjectionMatrix;
        private Matrix4x4 latestArDisplayMatrix;
        private bool hasArProjectionMatrix;
        private bool hasArDisplayMatrix;
        private float feedDesiredFovDegrees;
        private bool feedDesiredFovInitialized;
        private static readonly int DisplayTransformId =
            Shader.PropertyToID("_UnityDisplayTransform");

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

        private void OnDisable()
        {
            RestoreRawArDisplayTransform();
            UnsubscribeFromCameraFrames();
        }

        private void LateUpdate()
        {
            if (!FeedActive ||
                arTrackingCamera == null ||
                targetCamera == null)
            {
                return;
            }

            Matrix4x4 baseProjection = hasArProjectionMatrix
                ? latestArProjectionMatrix
                : arTrackingCamera.projectionMatrix;
            float baseFov = ProjectionVerticalFov(baseProjection);
            if (!feedDesiredFovInitialized)
            {
                feedDesiredFovDegrees = ClampFeedFov(windowFovDegrees, baseFov);
                feedDesiredFovInitialized = true;
            }

            feedDesiredFovDegrees = ClampFeedFov(feedDesiredFovDegrees, baseFov);
            float desiredFov = feedDesiredFovDegrees;

            float zoomScale = Mathf.Max(
                1f,
                Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) /
                Mathf.Max(0.0001f, Mathf.Tan(desiredFov * 0.5f * Mathf.Deg2Rad)));

            // Match the overlay projection and AR background crop. ARCore's
            // background shader multiplies row-vector UVs by its display matrix,
            // so the centered crop matrix belongs on the left.
            targetCamera.projectionMatrix =
                Matrix4x4.Scale(new Vector3(zoomScale, zoomScale, 1f)) *
                baseProjection;

            if (hasArDisplayMatrix && arCameraBackground != null)
            {
                Material backgroundMaterial = arCameraBackground.material;
                if (backgroundMaterial != null)
                {
                    float inverseScale = 1f / zoomScale;
                    float centerOffset = (1f - inverseScale) * 0.5f;
                    Matrix4x4 crop = Matrix4x4.identity;
                    crop.m00 = inverseScale;
                    crop.m11 = inverseScale;
                    crop.m20 = centerOffset;
                    crop.m21 = centerOffset;
                    backgroundMaterial.SetMatrix(
                        DisplayTransformId,
                        crop * latestArDisplayMatrix);
                }
            }
        }

        public void ToggleFeed()
        {
            SetFeedWanted(!wantFeed);
        }

        public void SetViewportFovDegrees(float value)
        {
            if (FeedActive && arCameraManager != null)
            {
                Matrix4x4 baseProjection = hasArProjectionMatrix
                    ? latestArProjectionMatrix
                    : arTrackingCamera != null
                        ? arTrackingCamera.projectionMatrix
                        : Matrix4x4.identity;
                float baseFov = ProjectionVerticalFov(baseProjection);
                if (!feedDesiredFovInitialized)
                {
                    feedDesiredFovDegrees = ClampFeedFov(windowFovDegrees, baseFov);
                    feedDesiredFovInitialized = true;
                }

                feedDesiredFovDegrees = ClampFeedFov(value, baseFov);
                windowFovDegrees = feedDesiredFovDegrees;
                if (poseSensors != null)
                {
                    poseSensors.cameraFovDegrees = feedDesiredFovDegrees;
                }

                return;
            }

            float windowFov = Mathf.Clamp(
                value,
                PhonePoseSimulator.MinimumViewportFovDegrees,
                75f);
            if (poseSensors != null)
            {
                poseSensors.cameraFovDegrees = windowFov;
            }

            windowFovDegrees = windowFov;
            feedDesiredFovDegrees = windowFov;
            feedDesiredFovInitialized = false;
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

            // Camera-on digital zoom uses ARCore's display transform. Stabilized
            // ARCore backgrounds bypass that transform, so keep stabilization off
            // to preserve camera/overlay registration.
            if (arCameraManager.imageStabilizationRequested)
            {
                arCameraManager.imageStabilizationRequested = false;
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
            if (!active)
            {
                RestoreRawArDisplayTransform();
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

            RefreshCameraFrameSubscription();
        }

        private void RefreshCameraFrameSubscription()
        {
            if (subscribedCameraManager == arCameraManager)
            {
                return;
            }

            UnsubscribeFromCameraFrames();
            subscribedCameraManager = arCameraManager;
            hasArProjectionMatrix = false;
            hasArDisplayMatrix = false;
            if (subscribedCameraManager != null)
            {
                subscribedCameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void UnsubscribeFromCameraFrames()
        {
            if (subscribedCameraManager != null)
            {
                subscribedCameraManager.frameReceived -= OnCameraFrameReceived;
                subscribedCameraManager = null;
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (eventArgs.projectionMatrix.HasValue)
            {
                latestArProjectionMatrix = eventArgs.projectionMatrix.Value;
                hasArProjectionMatrix = true;
            }

            if (eventArgs.displayMatrix.HasValue)
            {
                latestArDisplayMatrix = eventArgs.displayMatrix.Value;
                hasArDisplayMatrix = true;
            }
        }

        private void RestoreRawArDisplayTransform()
        {
            if (hasArDisplayMatrix && arCameraBackground != null)
            {
                Material backgroundMaterial = arCameraBackground.material;
                if (backgroundMaterial != null)
                {
                    backgroundMaterial.SetMatrix(
                        DisplayTransformId,
                        latestArDisplayMatrix);
                }
            }

            if (targetCamera != null)
            {
                targetCamera.ResetProjectionMatrix();
            }
        }

        private static float ProjectionVerticalFov(Matrix4x4 projection)
        {
            float inverseM11 = 1f / Mathf.Max(0.0001f, Mathf.Abs(projection.m11));
            return 2f * Mathf.Atan(inverseM11) * Mathf.Rad2Deg;
        }

        private float CurrentArBaseFovDegrees()
        {
            Matrix4x4 projection = hasArProjectionMatrix
                ? latestArProjectionMatrix
                : arTrackingCamera != null
                    ? arTrackingCamera.projectionMatrix
                    : Matrix4x4.Perspective(
                        feedVerticalFovDegrees,
                        1f,
                        0.01f,
                        100f);
            return ProjectionVerticalFov(projection);
        }

        private static float ClampFeedFov(float value, float baseFov)
        {
            return Mathf.Clamp(
                value,
                Mathf.Min(PhonePoseSimulator.MinimumViewportFovDegrees, baseFov),
                baseFov);
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

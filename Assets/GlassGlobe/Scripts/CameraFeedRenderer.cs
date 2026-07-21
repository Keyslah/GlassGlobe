using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GlassGlobe
{
    /// <summary>
    /// Renders the rear camera feed on a background quad behind the globe so the
    /// border overlay sits on the real world. While the feed is on, the virtual
    /// camera FOV is switched to match the phone camera lens; while off, it
    /// returns to the physical eye-window FOV.
    /// </summary>
    public sealed class CameraFeedRenderer : MonoBehaviour
    {
        public Camera targetCamera;
        public PhonePoseSensors poseSensors;

        [Tooltip("Assigned at scene build time so the feed shader is not stripped from the player build.")]
        public Material feedMaterial;

        [Tooltip("Vertical FOV of the phone camera feed as displayed in portrait, degrees. Pixel 8 Pro main lens is roughly 70.")]
        [Range(30f, 100f)]
        public float feedVerticalFovDegrees = 70f;

        [Range(20f, 100f)]
        public float windowFovDegrees = 32.4f;

        [Tooltip("Optional camera device name (or fragment) to use for the feed. Multi-lens phones expose each rear lens as its own device and the first one is not always the main lens the FOV above was tuned for.")]
        public string preferredDeviceName = string.Empty;

        public bool startEnabledOnDevice = true;

        [Min(1f)]
        public float quadDistance = 60f;

        public bool FeedActive { get; private set; }
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
            FeedStatus = "Off";
            if (!Application.isMobilePlatform)
            {
                enabled = false;
                return;
            }

            wantFeed = startEnabledOnDevice;
        }

        private void Update()
        {
            if (!wantFeed)
            {
                if (FeedActive ||
                    (webCamTexture != null && webCamTexture.isPlaying) ||
                    FeedStatus != "Off")
                {
                    StopFeed();
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

            FeedStatus = string.Format("AR {0}x{1}", webCamTexture.width, webCamTexture.height);
            EnsureQuad();
            UpdateQuad();
        }

        public void ToggleFeed()
        {
            SetFeedWanted(!wantFeed);
        }

        public void SetFeedWanted(bool value)
        {
            wantFeed = value;
            if (!value)
            {
                StopFeed();
                return;
            }

            ApplyFov();
        }

        private void ApplyFov()
        {
            if (poseSensors == null)
            {
                return;
            }

            if (wantFeed && FeedActive)
            {
                // Remember whatever FOV the user had (default or slider-set) so
                // toggling the feed off restores it instead of stomping it.
                if (!hasSavedWindowFov)
                {
                    savedWindowFovDegrees = poseSensors.cameraFovDegrees;
                    hasSavedWindowFov = true;
                }

                poseSensors.cameraFovDegrees = feedVerticalFovDegrees;
            }
            else
            {
                poseSensors.cameraFovDegrees = hasSavedWindowFov ? savedWindowFovDegrees : windowFovDegrees;
                hasSavedWindowFov = false;
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

                Debug.Log("GlassGlobeCameraFeed: available devices: " + deviceList +
                    ". Set preferredDeviceName if the feed FOV looks wrong for the chosen lens.");
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
                    if (device.name.IndexOf(preferredDeviceName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return device.name;
                    }
                }

                Debug.LogWarning("GlassGlobeCameraFeed: preferred device '" + preferredDeviceName +
                    "' not found; falling back to the first rear camera.");
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

        private void StopFeed()
        {
            FeedActive = false;
            FeedStatus = "Off";
            if (webCamTexture != null && webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }

            if (quadTransform != null)
            {
                quadTransform.gameObject.SetActive(false);
            }

            ApplyFov();
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
            meshFilter.sharedMesh = GlassGlobeVisuals.BuildQuadMesh("GlassGlobe Camera Feed Quad");

            MeshRenderer meshRenderer = quadObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            feedMaterial.mainTexture = webCamTexture;
            meshRenderer.sharedMaterial = feedMaterial;

            quadTransform = quadObject.transform;
        }

        private void UpdateQuad()
        {
            if (quadTransform == null)
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

            float verticalSize = 2f * quadDistance * Mathf.Tan(feedVerticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float textureAspect = (float)webCamTexture.height / Mathf.Max(1, webCamTexture.width);

            Vector3 scale;
            if (rotated)
            {
                // Texture width axis is displayed vertically on screen.
                scale = new Vector3(verticalSize, verticalSize * textureAspect, 1f);
            }
            else
            {
                scale = new Vector3(verticalSize / Mathf.Max(0.0001f, textureAspect), verticalSize, 1f);
            }

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

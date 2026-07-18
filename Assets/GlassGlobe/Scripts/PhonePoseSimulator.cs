using UnityEngine;

namespace GlassGlobe
{
    [ExecuteAlways]
    public sealed class PhonePoseSimulator : MonoBehaviour
    {
        public GeoCoordinate userCoordinate = new GeoCoordinate(37.7749f, -122.4194f);
        public Vector3 earthCenter = Vector3.zero;

        [Min(0.1f)]
        public float earthRadiusUnits = EarthMath.DefaultEarthRadiusUnits;

        [Min(0f)]
        public float observerHeightUnits = 0.35f;

        [Min(1f)]
        public float eyeToPhoneDistanceInches = 10f;

        [Min(1f)]
        public float phoneViewportHeightInches = 5.8f;

        [Range(0f, 78f)]
        public float tiltDegrees = 45f;

        [Range(0f, 360f)]
        public float headingDegrees = 90f;

        [Range(20f, 100f)]
        public float cameraFovDegrees = 60f;

        public Camera targetCamera;
        public Transform userPositionMarker;
        public LineRenderer localUpLine;
        public LineRenderer localDownLine;
        public float debugLineLength = 2.5f;
        public bool enableDragControls = true;
        public float dragHeadingSensitivity = 0.18f;
        public float dragTiltSensitivity = 0.12f;

        public Vector3 UserSurfacePosition { get; private set; }
        public Vector3 ObserverPosition { get; private set; }
        public Vector3 LocalUp { get; private set; }
        public Vector3 LocalDown { get; private set; }
        public Vector3 ViewDirection { get; private set; }
        public Quaternion ObserverRotation { get; private set; }

        public float PhysicalViewportFovDegrees
        {
            get { return CalculatePhysicalViewportFov(phoneViewportHeightInches, eyeToPhoneDistanceInches); }
        }

        private void Reset()
        {
            targetCamera = GetComponentInChildren<Camera>();
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }
        }

        private void OnEnable()
        {
            ApplyPose();
        }

        private void OnValidate()
        {
            earthRadiusUnits = Mathf.Max(0.1f, earthRadiusUnits);
            observerHeightUnits = Mathf.Max(0f, observerHeightUnits);
            eyeToPhoneDistanceInches = Mathf.Max(1f, eyeToPhoneDistanceInches);
            phoneViewportHeightInches = Mathf.Max(1f, phoneViewportHeightInches);
            headingDegrees = Mathf.Repeat(headingDegrees, 360f);
            tiltDegrees = Mathf.Clamp(tiltDegrees, 0f, 78f);
            cameraFovDegrees = Mathf.Clamp(cameraFovDegrees, 20f, 100f);
            ApplyPose();
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                UpdateDragControls();
            }

            ApplyPose();
        }

        private Vector3 previousPointerPosition;
        private bool pointerDragging;

        private void UpdateDragControls()
        {
            if (!enableDragControls)
            {
                return;
            }

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Moved)
                {
                    ApplyDragDelta(touch.deltaPosition);
                }

                pointerDragging = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                previousPointerPosition = Input.mousePosition;
                pointerDragging = true;
            }

            if (pointerDragging && Input.GetMouseButton(0))
            {
                Vector3 currentPosition = Input.mousePosition;
                ApplyDragDelta(currentPosition - previousPointerPosition);
                previousPointerPosition = currentPosition;
            }

            if (Input.GetMouseButtonUp(0))
            {
                pointerDragging = false;
            }
        }

        private void ApplyDragDelta(Vector2 delta)
        {
            headingDegrees = Mathf.Repeat(headingDegrees - delta.x * dragHeadingSensitivity, 360f);
            tiltDegrees = Mathf.Clamp(tiltDegrees + delta.y * dragTiltSensitivity, 0f, 72f);
        }

        public Ray GetCenterRay()
        {
            Camera camera = ResolveCamera();
            if (camera != null)
            {
                return camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            }

            return new Ray(ObserverPosition, ViewDirection);
        }

        public void UsePhysicalViewportFov()
        {
            cameraFovDegrees = PhysicalViewportFovDegrees;
        }

        public static float CalculatePhysicalViewportFov(float viewportHeightInches, float eyeDistanceInches)
        {
            float safeDistance = Mathf.Max(1f, eyeDistanceInches);
            float safeHeight = Mathf.Max(1f, viewportHeightInches);
            return 2f * Mathf.Atan((safeHeight * 0.5f) / safeDistance) * Mathf.Rad2Deg;
        }

        public void ApplyPose()
        {
            EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(userCoordinate);
            LocalUp = frame.Up;
            LocalDown = frame.Down;
            UserSurfacePosition = earthCenter + LocalUp * earthRadiusUnits;
            ObserverPosition = earthCenter + LocalUp * (earthRadiusUnits + observerHeightUnits);
            ViewDirection = EarthMath.GetInwardViewDirection(userCoordinate, headingDegrees, tiltDegrees);
            ObserverRotation = EarthMath.GetInwardViewRotation(userCoordinate, headingDegrees, tiltDegrees);

            transform.SetPositionAndRotation(ObserverPosition, ObserverRotation);

            Camera camera = ResolveCamera();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(ObserverPosition, ObserverRotation);
                camera.fieldOfView = cameraFovDegrees;
                camera.nearClipPlane = CalculateThroughEarthNearClip();
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, earthRadiusUnits * 8f);
            }

            UpdateDebugVisuals();
        }

        private float CalculateThroughEarthNearClip()
        {
            float nearDistance;
            float farDistance;
            Ray viewRay = new Ray(ObserverPosition, ViewDirection);
            if (!EarthMath.RaySphereIntersections(
                    viewRay,
                    earthCenter,
                    earthRadiusUnits,
                    out nearDistance,
                    out farDistance))
            {
                return 0.01f;
            }

            float intersectionDepth = Mathf.Max(0f, farDistance - nearDistance);
            float clipPadding = Mathf.Min(0.35f, intersectionDepth * 0.2f);
            return Mathf.Clamp(nearDistance + clipPadding, 0.01f, Mathf.Max(0.02f, farDistance - 0.05f));
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponentInChildren<Camera>();
            }

            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            return targetCamera;
        }

        private void UpdateDebugVisuals()
        {
            if (userPositionMarker != null)
            {
                userPositionMarker.position = UserSurfacePosition;
            }

            if (localUpLine != null)
            {
                localUpLine.positionCount = 2;
                localUpLine.SetPosition(0, UserSurfacePosition);
                localUpLine.SetPosition(1, UserSurfacePosition + LocalUp * debugLineLength);
            }

            if (localDownLine != null)
            {
                localDownLine.positionCount = 2;
                localDownLine.SetPosition(0, UserSurfacePosition);
                localDownLine.SetPosition(1, UserSurfacePosition + LocalDown * debugLineLength);
            }
        }
    }
}

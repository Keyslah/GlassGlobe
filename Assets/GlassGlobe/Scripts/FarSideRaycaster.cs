using UnityEngine;

namespace GlassGlobe
{
    [ExecuteAlways]
    public sealed class FarSideRaycaster : MonoBehaviour
    {
        public PhonePoseSimulator phonePose;
        public GlobeRenderer globe;
        public Camera targetCamera;
        public LineRenderer centerRayLine;
        public Transform nearSideMarker;
        public Transform farSideMarker;
        public float missRayLength = 40f;

        public bool HasIntersection { get; private set; }
        public Vector3 NearSidePoint { get; private set; }
        public Vector3 FarSidePoint { get; private set; }
        public GeoCoordinate FarSideCoordinate { get; private set; }

        private void Reset()
        {
            phonePose = FindFirstObjectByType<PhonePoseSimulator>();
            globe = FindFirstObjectByType<GlobeRenderer>();
            if (phonePose != null)
            {
                targetCamera = phonePose.targetCamera;
            }
        }

        private void OnEnable()
        {
            UpdateRaycast();
        }

        private void Update()
        {
            UpdateRaycast();
        }

        public bool TryGetFarSideHit(out Vector3 farSidePoint, out GeoCoordinate farSideCoordinate)
        {
            UpdateRaycast();
            farSidePoint = FarSidePoint;
            farSideCoordinate = FarSideCoordinate;
            return HasIntersection;
        }

        public void UpdateRaycast()
        {
            ResolveReferences();

            if (phonePose == null || globe == null)
            {
                HasIntersection = false;
                return;
            }

            Ray ray = GetCenterRay();
            float nearDistance;
            float farDistance;
            HasIntersection = EarthMath.RaySphereIntersections(
                ray,
                globe.Center,
                globe.RadiusUnits,
                out nearDistance,
                out farDistance);

            Vector3 rayEnd = ray.origin + ray.direction * missRayLength;

            if (HasIntersection)
            {
                NearSidePoint = ray.origin + ray.direction * nearDistance;
                FarSidePoint = ray.origin + ray.direction * farDistance;
                FarSideCoordinate = globe.WorldToGeo(FarSidePoint);
                rayEnd = FarSidePoint;
            }

            UpdateDebugVisuals(ray.origin, rayEnd);
        }

        private Ray GetCenterRay()
        {
            if (phonePose != null)
            {
                return phonePose.GetCenterRay();
            }

            if (targetCamera != null)
            {
                return targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            }

            return new Ray(transform.position, transform.forward);
        }

        private void ResolveReferences()
        {
            if (phonePose == null)
            {
                phonePose = FindFirstObjectByType<PhonePoseSimulator>();
            }

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (targetCamera == null && phonePose != null)
            {
                targetCamera = phonePose.targetCamera;
            }
        }

        private void UpdateDebugVisuals(Vector3 rayStart, Vector3 rayEnd)
        {
            if (centerRayLine != null)
            {
                Vector3 direction = (rayEnd - rayStart).normalized;
                Vector3 lineStart = HasIntersection
                    ? NearSidePoint + direction * 0.25f
                    : rayStart + direction * 0.25f;
                centerRayLine.positionCount = 2;
                centerRayLine.SetPosition(0, lineStart);
                centerRayLine.SetPosition(1, rayEnd);
            }

            if (nearSideMarker != null)
            {
                nearSideMarker.gameObject.SetActive(HasIntersection);
                if (HasIntersection)
                {
                    nearSideMarker.position = NearSidePoint;
                }
            }

            if (farSideMarker != null)
            {
                farSideMarker.gameObject.SetActive(HasIntersection);
                if (HasIntersection)
                {
                    farSideMarker.position = FarSidePoint;
                }
            }
        }
    }
}

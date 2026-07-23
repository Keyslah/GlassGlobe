using System;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Renders the Milky Way as a distant sky sphere positioned where the galaxy
    /// really is for the observer's location and the current time. The panorama
    /// is authored in galactic coordinates; vertices are converted to equatorial
    /// space at mesh build time (with the x axis negated to match the globe's
    /// mirrored embedding), and the sphere is rotated each frame by local
    /// sidereal time so the sky turns with the Earth.
    /// </summary>
    public sealed class MilkyWayBackground : MonoBehaviour
    {
        public Camera targetCamera;
        public PhonePoseSensors poseSensors;
        public PhonePoseSimulator simulator;

        public bool verboseLogging = false;

        [Tooltip("Assigned at scene build time so the galaxy shader is not stripped from the player build.")]
        public Material galaxyMaterial;

        [Min(20f)]
        public float radiusUnits = 90f;

        [Range(8, 128)]
        public int longitudeSegments = 128;

        [Range(4, 64)]
        public int latitudeSegments = 64;

        public bool Visible { get; private set; }
        public string StatusText { get; private set; }

        private Transform sphereTransform;
        private MeshRenderer sphereRenderer;
        private Vector3 galacticXEquatorial;
        private Vector3 galacticYEquatorial;
        private Vector3 galacticZEquatorial;
        private float lastLogTime;

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            Visible = GlassGlobeSettingsState.BackgroundCategoryEnabled &&
                GlassGlobeSettingsState.MilkyWayEnabled;
            StatusText = "Initializing";
            // The photographic panorama contains fine, curved dust lanes.
            // Keep enough sphere tessellation that its equirectangular mapping
            // does not turn those lanes into visibly straight-edged facets.
            longitudeSegments = Mathf.Max(longitudeSegments, 128);
            latitudeSegments = Mathf.Max(latitudeSegments, 64);
            ComputeGalacticBasis();
            BuildSphere();
            ApplyVisibility();
        }

        public void SetVisible(bool value)
        {
            Visible = value;
            ApplyVisibility();
        }

        private void LateUpdate()
        {
            if (!Visible || sphereTransform == null)
            {
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return;
            }

            GeoCoordinate coordinate = ResolveCoordinate();
            EarthMath.LocalFrame frame = EarthMath.GetLocalFrame(coordinate);
            float lstDegrees = SkyMath.ComputeLocalSiderealDegrees(coordinate.Longitude);
            float latitude = coordinate.Latitude;

            Vector3 equatorialYWorld = SkyMath.EquatorialToWorld(new Vector3(0f, 1f, 0f), lstDegrees, latitude, frame);
            Vector3 equatorialZWorld = SkyMath.EquatorialToWorld(new Vector3(0f, 0f, 1f), lstDegrees, latitude, frame);

            sphereTransform.position = camera.transform.position;
            sphereTransform.rotation = Quaternion.LookRotation(equatorialZWorld, equatorialYWorld);

            UpdateStatus(lstDegrees, latitude, frame);
        }

        private void UpdateStatus(float lstDegrees, float latitude, EarthMath.LocalFrame frame)
        {
            Vector3 enu = SkyMath.EquatorialToEnu(galacticXEquatorial, lstDegrees, latitude);
            float azimuth;
            float altitude;
            SkyMath.EnuToAzimuthAltitude(enu, out azimuth, out altitude);
            StatusText = string.Format(
                "Visible. Galactic center: azimuth {0:0} deg, altitude {1:0} deg",
                azimuth,
                altitude);

            if (verboseLogging && Time.time - lastLogTime > 5f)
            {
                lastLogTime = Time.time;
                Debug.Log(string.Format(
                    "GlassGlobeMilkyWay: lst={0:0.0} galCenterAz={1:0.0} galCenterAlt={2:0.0}",
                    lstDegrees,
                    azimuth,
                    altitude));
            }
        }

        private void ApplyVisibility()
        {
            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = Visible;
            }

            if (!Visible)
            {
                StatusText = "Hidden";
            }
        }

        private GeoCoordinate ResolveCoordinate()
        {
            if (poseSensors != null && poseSensors.SensorModeActive)
            {
                return poseSensors.CurrentCoordinate;
            }

            if (simulator != null)
            {
                return simulator.userCoordinate;
            }

            return new GeoCoordinate(0f, 0f);
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            return targetCamera;
        }

        private void ComputeGalacticBasis()
        {
            // J2000: galactic center RA 266.4050, Dec -28.9362; north galactic
            // pole RA 192.8595, Dec +27.1284.
            galacticXEquatorial = SkyMath.RaDecToEquatorial(266.4050f, -28.9362f);
            galacticZEquatorial = SkyMath.RaDecToEquatorial(192.8595f, 27.1284f);
            galacticYEquatorial = Vector3.Cross(galacticZEquatorial, galacticXEquatorial).normalized;
            galacticXEquatorial = Vector3.Cross(galacticYEquatorial, galacticZEquatorial).normalized;
        }

        private void BuildSphere()
        {
            GameObject sphereObject = new GameObject("Milky Way Sphere");
            sphereObject.transform.SetParent(transform, false);

            MeshFilter meshFilter = sphereObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildGalacticSphereMesh();

            sphereRenderer = sphereObject.AddComponent<MeshRenderer>();
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;

            if (galaxyMaterial == null)
            {
                Shader shader = Shader.Find("GlassGlobe/Galaxy");
                if (shader == null)
                {
                    StatusText = "Galaxy shader missing from build";
                    sphereRenderer.enabled = false;
                    sphereTransform = sphereObject.transform;
                    return;
                }

                galaxyMaterial = new Material(shader);
            }

            sphereRenderer.sharedMaterial = galaxyMaterial;
            sphereObject.transform.localScale = Vector3.one * radiusUnits;
            sphereTransform = sphereObject.transform;
        }

        private Mesh BuildGalacticSphereMesh()
        {
            return GlassGlobeVisuals.BuildLatLonSphereMesh(
                "GlassGlobe Milky Way Sphere",
                longitudeSegments,
                latitudeSegments,
                90f,
                false,
                delegate (float galacticLatitude, float galacticLongitude, float u, float v, out Vector3 position, out Vector2 uv)
                {
                    float b = galacticLatitude * Mathf.Deg2Rad;
                    float l = galacticLongitude * Mathf.Deg2Rad;
                    float cosB = Mathf.Cos(b);

                    Vector3 equatorial =
                        galacticXEquatorial * (cosB * Mathf.Cos(l)) +
                        galacticYEquatorial * (cosB * Mathf.Sin(l)) +
                        galacticZEquatorial * Mathf.Sin(b);

                    // Negated x matches the globe's mirrored embedding so the
                    // sky is truthful through the same left-handed camera.
                    position = new Vector3(-equatorial.x, equatorial.y, equatorial.z);
                    uv = new Vector2(0.5f - galacticLongitude / 360f, v);
                });
        }
    }
}

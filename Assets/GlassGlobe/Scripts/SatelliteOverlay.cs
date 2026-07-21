using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GlassGlobe
{
    /// <summary>
    /// Live satellites (ISS, Tiangong) as bright dots at their true positions
    /// above the mirrored glass Earth. Orbits come from CelesTrak TLEs and are
    /// propagated locally each frame (OrbitMath: Kepler + J2 secular), so an
    /// overhead pass appears in the sky and a far-side pass shows through the
    /// planet. Markers render above the globe glass so they stay visible over
    /// the camera feed and through the Earth.
    /// </summary>
    public sealed class SatelliteOverlay : MonoBehaviour
    {
        public GlobeRenderer globe;
        public Camera targetCamera;

        [Tooltip("Assigned at scene build time so the sprite shader is not stripped from the player build.")]
        public Material markerMaterial;

        [Tooltip("Names (prefix match) to track from the CelesTrak stations group.")]
        public string[] trackedSatellites = { "ISS (ZARYA)", "CSS (TIANHE)" };

        [Range(1800f, 86400f)]
        public float tleRefreshSeconds = 21600f;

        [Range(30f, 1800f)]
        public float retrySeconds = 300f;

        [Range(0.5f, 6f)]
        public float markerAngularDegrees = 1.6f;

        public bool SatellitesVisible { get; private set; }
        public string Status { get; private set; }

        private const string TleUrl = "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle";
        private const int MarkerRenderQueue = 3012;

        private sealed class TrackedSatellite
        {
            public string DisplayName;
            public OrbitMath.TwoLineElements Elements;
            public Transform Transform;
            public MeshRenderer Renderer;
            public double LatestAltitudeKm;
        }

        private readonly List<TrackedSatellite> satellites = new List<TrackedSatellite>();
        private float nextFetchTime;
        private bool hasTleData;

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            SatellitesVisible = GlassGlobeSettingsState.SatellitesEnabled;
            Status = "Initializing";

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            markerMaterial = EnsureMaterial(markerMaterial);
            ApplyVisibility();
        }

        private void Start()
        {
            StartCoroutine(RefreshLoop());
        }

        public void SetSatellitesVisible(bool value)
        {
            SatellitesVisible = value;
            if (value && !hasTleData)
            {
                nextFetchTime = 0f;
            }

            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            for (int index = 0; index < satellites.Count; index++)
            {
                if (satellites[index].Renderer != null)
                {
                    satellites[index].Renderer.enabled = SatellitesVisible;
                }
            }

            if (!SatellitesVisible)
            {
                Status = "Hidden";
            }
        }

        private void Update()
        {
            if (!SatellitesVisible || !hasTleData || globe == null)
            {
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            double daysSinceJ2000 = SkyMath.DaysSinceJ2000();
            double gmstDegrees = SkyMath.ComputeLocalSiderealDegrees(0f);

            for (int index = 0; index < satellites.Count; index++)
            {
                TrackedSatellite satellite = satellites[index];
                if (satellite.Transform == null)
                {
                    continue;
                }

                float latitude;
                float longitude;
                double radiusKm;
                OrbitMath.PropagateToGeo(satellite.Elements, daysSinceJ2000, gmstDegrees, out latitude, out longitude, out radiusKm);
                satellite.LatestAltitudeKm = radiusKm - OrbitMath.MeanEarthRadiusKm;

                // Scale the true geocentric radius onto the globe so overhead
                // passes float above the surface and far-side passes sit just
                // beyond the far surface, seen through the glass.
                float radiusUnits = globe.RadiusUnits * (float)(radiusKm / OrbitMath.MeanEarthRadiusKm);
                Vector3 position = EarthMath.GeoToPoint(new GeoCoordinate(latitude, longitude), radiusUnits, globe.Center);
                Vector3 fromCamera = position - cameraPosition;
                float distance = fromCamera.magnitude;
                if (distance < 0.001f)
                {
                    continue;
                }

                float size = 2f * distance * Mathf.Tan(markerAngularDegrees * 0.5f * Mathf.Deg2Rad);
                satellite.Transform.position = position;
                satellite.Transform.rotation = Quaternion.LookRotation(fromCamera / distance);
                satellite.Transform.localScale = new Vector3(size, size, 1f);
            }
        }

        public bool TryGetPointedTarget(Vector3 worldDirection, Vector3 cameraPosition, out string label, out float angleDegrees)
        {
            label = null;
            angleDegrees = float.MaxValue;
            if (!SatellitesVisible || !hasTleData)
            {
                return false;
            }

            float selectionRadius = Mathf.Max(2.25f, markerAngularDegrees * 0.6f);
            for (int index = 0; index < satellites.Count; index++)
            {
                TrackedSatellite satellite = satellites[index];
                if (satellite.Transform == null)
                {
                    continue;
                }

                Vector3 targetDirection = (satellite.Transform.position - cameraPosition).normalized;
                float angle = Vector3.Angle(worldDirection, targetDirection);
                if (angle <= selectionRadius && angle < angleDegrees)
                {
                    angleDegrees = angle;
                    label = string.Format("{0} ({1:0} km up)", satellite.DisplayName, satellite.LatestAltitudeKm);
                }
            }

            return label != null;
        }

        private IEnumerator RefreshLoop()
        {
            WaitForSecondsRealtime pollWait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                if (SatellitesVisible && Time.unscaledTime >= nextFetchTime)
                {
                    yield return FetchTles();
                }

                yield return pollWait;
            }
        }

        private IEnumerator FetchTles()
        {
            Status = "Updating orbits...";
            string text;
            using (UnityWebRequest request = UnityWebRequest.Get(TleUrl))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Status = hasTleData
                        ? "Orbit update failed; using previous orbits"
                        : "Orbit data unavailable; retrying";
                    nextFetchTime = Time.unscaledTime + retrySeconds;
                    yield break;
                }

                text = request.downloadHandler.text;
            }

            int parsedCount = ParseTrackedTles(text);
            if (parsedCount == 0)
            {
                Status = hasTleData
                    ? "Orbit feed had no tracked satellites; using previous orbits"
                    : "Orbit feed had no tracked satellites; retrying";
                nextFetchTime = Time.unscaledTime + retrySeconds;
                yield break;
            }

            hasTleData = true;
            ApplyVisibility();
            Status = string.Format(
                "Tracking {0}. Orbits {1:HH:mm} UTC. Data: CelesTrak",
                DescribeTrackedNames(),
                DateTime.UtcNow);
            nextFetchTime = Time.unscaledTime + tleRefreshSeconds;
        }

        private int ParseTrackedTles(string tleText)
        {
            if (string.IsNullOrEmpty(tleText))
            {
                return 0;
            }

            string[] lines = tleText.Split('\n');
            int parsedCount = 0;
            for (int index = 0; index + 2 < lines.Length; index++)
            {
                string name = lines[index].TrimEnd('\r').Trim();
                if (name.Length == 0 || name.StartsWith("1 ", StringComparison.Ordinal) || name.StartsWith("2 ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsTrackedName(name))
                {
                    continue;
                }

                string line1 = lines[index + 1].TrimEnd('\r');
                string line2 = lines[index + 2].TrimEnd('\r');
                OrbitMath.TwoLineElements elements;
                if (!OrbitMath.TryParseTle(name, line1, line2, out elements))
                {
                    continue;
                }

                UpdateOrCreateSatellite(name, elements);
                parsedCount++;
            }

            return parsedCount;
        }

        private bool IsTrackedName(string name)
        {
            for (int index = 0; index < trackedSatellites.Length; index++)
            {
                if (name.StartsWith(trackedSatellites[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateOrCreateSatellite(string name, OrbitMath.TwoLineElements elements)
        {
            string displayName = ToDisplayName(name);
            for (int index = 0; index < satellites.Count; index++)
            {
                if (satellites[index].DisplayName == displayName)
                {
                    satellites[index].Elements = elements;
                    return;
                }
            }

            TrackedSatellite satellite = new TrackedSatellite();
            satellite.DisplayName = displayName;
            satellite.Elements = elements;

            GameObject markerObject = new GameObject("Satellite - " + displayName);
            markerObject.transform.SetParent(transform, false);
            MeshFilter meshFilter = markerObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GlassGlobeVisuals.BuildQuadMesh("Satellite Marker Quad");
            satellite.Renderer = markerObject.AddComponent<MeshRenderer>();
            satellite.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            satellite.Renderer.receiveShadows = false;
            satellite.Renderer.sharedMaterial = markerMaterial;
            satellite.Renderer.enabled = SatellitesVisible;
            satellite.Transform = markerObject.transform;
            satellites.Add(satellite);
        }

        private static string ToDisplayName(string tleName)
        {
            if (tleName.StartsWith("ISS", StringComparison.OrdinalIgnoreCase))
            {
                return "ISS";
            }

            if (tleName.StartsWith("CSS", StringComparison.OrdinalIgnoreCase))
            {
                return "Tiangong";
            }

            int parenthesis = tleName.IndexOf('(');
            return parenthesis > 0 ? tleName.Substring(0, parenthesis).Trim() : tleName;
        }

        private string DescribeTrackedNames()
        {
            if (satellites.Count == 0)
            {
                return "no satellites";
            }

            System.Text.StringBuilder names = new System.Text.StringBuilder();
            for (int index = 0; index < satellites.Count; index++)
            {
                if (index > 0)
                {
                    names.Append(" + ");
                }

                names.Append(satellites[index].DisplayName);
            }

            return names.ToString();
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            return targetCamera;
        }

        private Material EnsureMaterial(Material material)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("GlassGlobe/Sky Sprite");
                if (shader == null)
                {
                    Status = "Sprite shader missing from build";
                    return null;
                }

                material = new Material(shader);
            }

            if (material.mainTexture == null)
            {
                material.mainTexture = GlassGlobeVisuals.BuildRadialTexture(
                    64,
                    new Color(0.75f, 0.95f, 1f, 1f),
                    new Color(0.4f, 0.8f, 1f, 0.5f),
                    0.2f,
                    3f);
            }

            // Above the globe glass (3008) and border lines (3010): satellites
            // must read through the planet and over the camera feed.
            material.renderQueue = MarkerRenderQueue;
            return material;
        }
    }
}

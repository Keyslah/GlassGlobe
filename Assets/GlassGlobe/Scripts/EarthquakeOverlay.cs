using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GlassGlobe
{
    /// <summary>
    /// Recent significant earthquakes (USGS M4.5+ last 24 hours) as pulsing
    /// markers at their epicenters on the glass Earth. Marker positions use
    /// EarthMath.GeoToPoint, so they inherit the globe's mirrored embedding and
    /// appear at the true location seen through the planet; the near-clip
    /// look-through trick keeps only far-side markers visible, matching the
    /// border lines.
    /// </summary>
    public sealed class EarthquakeOverlay : MonoBehaviour
    {
        public GlobeRenderer globe;
        public Camera targetCamera;

        [Tooltip("Assigned at scene build time so the sprite shader is not stripped from the player build.")]
        public Material markerMaterial;

        [Min(0f)]
        public float surfaceOffset = 0.055f;

        [Range(60f, 3600f)]
        public float refreshSeconds = 600f;

        [Range(30f, 600f)]
        public float retrySeconds = 120f;

        public bool EarthquakesVisible { get; private set; }
        public string Status { get; private set; }

        private const string FeedUrl = "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/4.5_day.geojson";
        private const float FeedMinimumMagnitude = 4.5f;
        private const int MarkerRenderQueue = 3011;

        private sealed class QuakeMarker
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public float BaseSize;
            public string Label;
            public bool InUse;
        }

        private readonly List<QuakeMarker> markers = new List<QuakeMarker>();
        private float nextFetchTime;
        private bool hasQuakeData;
        private int activeQuakeCount;

        [Serializable]
        private sealed class UsgsFeed
        {
            public UsgsFeature[] features;
        }

        [Serializable]
        private sealed class UsgsFeature
        {
            public UsgsProperties properties;
            public UsgsGeometry geometry;
        }

        [Serializable]
        private sealed class UsgsProperties
        {
            public float mag;
            public string place;
        }

        [Serializable]
        private sealed class UsgsGeometry
        {
            public double[] coordinates;
        }

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            EarthquakesVisible = GlassGlobeSettingsState.EarthquakesEnabled;
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

        public void SetEarthquakesVisible(bool value)
        {
            EarthquakesVisible = value;
            if (value && !hasQuakeData)
            {
                nextFetchTime = 0f;
            }

            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            for (int index = 0; index < markers.Count; index++)
            {
                QuakeMarker marker = markers[index];
                if (marker.Renderer != null)
                {
                    marker.Renderer.enabled = EarthquakesVisible && marker.InUse;
                }
            }

            if (!EarthquakesVisible)
            {
                Status = "Hidden";
            }
        }

        private void Update()
        {
            if (!EarthquakesVisible || !hasQuakeData)
            {
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            for (int index = 0; index < markers.Count; index++)
            {
                QuakeMarker marker = markers[index];
                if (!marker.InUse || marker.Transform == null)
                {
                    continue;
                }

                Vector3 fromCamera = marker.Transform.position - cameraPosition;
                if (fromCamera.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                marker.Transform.rotation = Quaternion.LookRotation(fromCamera.normalized);
                float pulse = 1f + 0.3f * Mathf.Sin(Time.time * 4f + index * 1.7f);
                float size = marker.BaseSize * pulse;
                marker.Transform.localScale = new Vector3(size, size, 1f);
            }
        }

        public bool TryGetPointedTarget(Vector3 worldDirection, Vector3 cameraPosition, out string label, out float angleDegrees)
        {
            label = null;
            angleDegrees = float.MaxValue;
            if (!EarthquakesVisible || !hasQuakeData)
            {
                return false;
            }

            for (int index = 0; index < markers.Count; index++)
            {
                QuakeMarker marker = markers[index];
                if (!marker.InUse || marker.Transform == null)
                {
                    continue;
                }

                Vector3 targetDirection = (marker.Transform.position - cameraPosition).normalized;
                float angle = Vector3.Angle(worldDirection, targetDirection);
                if (angle <= 2.25f && angle < angleDegrees)
                {
                    angleDegrees = angle;
                    label = marker.Label;
                }
            }

            return label != null;
        }

        private IEnumerator RefreshLoop()
        {
            WaitForSecondsRealtime pollWait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                if (EarthquakesVisible && Time.unscaledTime >= nextFetchTime)
                {
                    yield return FetchQuakes();
                }

                yield return pollWait;
            }
        }

        private IEnumerator FetchQuakes()
        {
            Status = "Updating earthquakes...";
            string json;
            using (UnityWebRequest request = UnityWebRequest.Get(FeedUrl))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Status = hasQuakeData
                        ? "Earthquake update failed; showing previous data"
                        : "Earthquake feed unavailable; retrying";
                    nextFetchTime = Time.unscaledTime + retrySeconds;
                    yield break;
                }

                json = request.downloadHandler.text;
            }

            UsgsFeed feed = null;
            try
            {
                feed = JsonUtility.FromJson<UsgsFeed>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("GlassGlobeEarthquakes: feed parse failed: " + exception.Message);
            }

            if (feed == null || feed.features == null)
            {
                Status = hasQuakeData
                    ? "Earthquake feed unreadable; showing previous data"
                    : "Earthquake feed unreadable; retrying";
                nextFetchTime = Time.unscaledTime + retrySeconds;
                yield break;
            }

            RebuildMarkers(feed);
            hasQuakeData = true;
            Status = string.Format(
                "{0} earthquakes M{1:0.0}+ in the last 24h. Updated {2:HH:mm} UTC. Data: USGS",
                activeQuakeCount,
                FeedMinimumMagnitude,
                DateTime.UtcNow);
            nextFetchTime = Time.unscaledTime + refreshSeconds;
        }

        private void RebuildMarkers(UsgsFeed feed)
        {
            float radius = (globe != null ? globe.RadiusUnits : EarthMath.DefaultEarthRadiusUnits) + surfaceOffset;
            Vector3 center = globe != null ? globe.Center : Vector3.zero;

            activeQuakeCount = 0;
            for (int index = 0; index < feed.features.Length; index++)
            {
                UsgsFeature feature = feed.features[index];
                if (feature == null || feature.geometry == null ||
                    feature.geometry.coordinates == null || feature.geometry.coordinates.Length < 2)
                {
                    continue;
                }

                float longitude = (float)feature.geometry.coordinates[0];
                float latitude = (float)feature.geometry.coordinates[1];
                float magnitude = feature.properties != null ? feature.properties.mag : FeedMinimumMagnitude;
                string place = feature.properties != null && !string.IsNullOrEmpty(feature.properties.place)
                    ? feature.properties.place
                    : "epicenter";

                QuakeMarker marker = GetOrCreateMarker(activeQuakeCount);
                marker.InUse = true;
                marker.Label = string.Format("M {0:0.0} earthquake - {1}", magnitude, place);
                marker.BaseSize = 0.10f + Mathf.Clamp(magnitude - FeedMinimumMagnitude, 0f, 3.5f) * 0.06f;
                marker.Transform.position = EarthMath.GeoToPoint(
                    new GeoCoordinate(latitude, longitude), radius, center);
                marker.Renderer.enabled = EarthquakesVisible;
                activeQuakeCount++;
            }

            for (int index = activeQuakeCount; index < markers.Count; index++)
            {
                markers[index].InUse = false;
                if (markers[index].Renderer != null)
                {
                    markers[index].Renderer.enabled = false;
                }
            }
        }

        private QuakeMarker GetOrCreateMarker(int index)
        {
            while (markers.Count <= index)
            {
                QuakeMarker marker = new QuakeMarker();
                GameObject markerObject = new GameObject("Earthquake Marker " + markers.Count);
                markerObject.transform.SetParent(transform, false);
                MeshFilter meshFilter = markerObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = GlassGlobeVisuals.BuildQuadMesh("Earthquake Marker Quad");
                marker.Renderer = markerObject.AddComponent<MeshRenderer>();
                marker.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                marker.Renderer.receiveShadows = false;
                marker.Renderer.sharedMaterial = markerMaterial;
                marker.Renderer.enabled = false;
                marker.Transform = markerObject.transform;
                markers.Add(marker);
            }

            return markers[index];
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
                    new Color(1f, 0.45f, 0.2f, 1f),
                    new Color(1f, 0.25f, 0.1f, 0.55f),
                    0.18f,
                    2.5f);
            }

            // Above the border lines (3010) is too strong for surface markers;
            // sit just above the globe glass (3008) so quakes read as part of
            // the far-side surface but stay visible over the weather layers.
            material.renderQueue = MarkerRenderQueue;
            return material;
        }
    }
}

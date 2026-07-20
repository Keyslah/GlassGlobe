using System.Collections.Generic;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Renders the Sun and Moon as billboard sprites at their true sky
    /// positions for the observer's viewpoint and the current time. Sprites sit
    /// between the Milky Way and the camera feed in the render queue, so they
    /// are part of the sky background and hidden while the camera feed is on.
    /// </summary>
    public sealed class SunMoonBackground : MonoBehaviour
    {
        public Camera targetCamera;
        public PhonePoseSensors poseSensors;
        public PhonePoseSimulator simulator;

        [Tooltip("Assigned at scene build time so the sprite shader is not stripped from the player build.")]
        public Material sunMaterial;

        [Tooltip("Assigned at scene build time so the sprite shader is not stripped from the player build.")]
        public Material moonMaterial;

        [Min(20f)]
        public float radiusUnits = 85f;

        [Tooltip("On-screen angular size of the sun sprite including glow, degrees. The real disc is 0.53.")]
        [Range(1f, 20f)]
        public float sunAngularDegrees = 6f;

        [Range(1f, 20f)]
        public float moonAngularDegrees = 4f;

        public bool SunVisible { get; private set; }
        public bool MoonVisible { get; private set; }
        public string SunStatus { get; private set; }
        public string MoonStatus { get; private set; }

        private Transform sunTransform;
        private Transform moonTransform;
        private MeshRenderer sunRenderer;
        private MeshRenderer moonRenderer;
        private readonly List<CelestialMarker> foregroundMarkers = new List<CelestialMarker>();
        private float lastLogTime;
        private Vector3 lastCameraPosition;

        private sealed class CelestialMarker
        {
            public string Name;
            public Transform Transform;
            public Vector3 EquatorialDirection;
            public float AngularDegrees;
            public bool IsPlanet;
        }

        private static readonly object[] BrightStarCatalog =
        {
            "Sirius", 101.2875f, -16.7161f,
            "Canopus", 95.9879f, -52.6957f,
            "Arcturus", 213.9153f, 19.1824f,
            "Vega", 279.2347f, 38.7837f,
            "Capella", 79.1723f, 45.9980f,
            "Rigel", 78.6345f, -8.2016f,
            "Procyon", 114.8255f, 5.2250f,
            "Betelgeuse", 88.7929f, 7.4071f,
            "Achernar", 24.4286f, -57.2368f,
            "Hadar", 210.9559f, -60.3730f,
            "Altair", 297.6958f, 8.8683f,
            "Acrux", 186.6490f, -63.0991f,
            "Aldebaran", 68.9800f, 16.5093f,
            "Antares", 247.3519f, -26.4320f,
            "Spica", 201.2983f, -11.1614f,
            "Pollux", 116.3289f, 28.0262f,
            "Fomalhaut", 344.4128f, -29.6222f,
            "Deneb", 310.3579f, 45.2803f,
            "Polaris", 37.9546f, 89.2641f
        };

        private static readonly string[] PlanetNames = { "Venus", "Mars", "Jupiter", "Saturn" };

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            SunVisible = GlassGlobeSettingsState.SunEnabled;
            MoonVisible = GlassGlobeSettingsState.MoonEnabled;
            SunStatus = "Initializing";
            MoonStatus = "Initializing";

            sunMaterial = EnsureMaterial(sunMaterial, BuildSunTexture());
            moonMaterial = EnsureMaterial(moonMaterial, BuildMoonTexture());

            sunTransform = BuildSprite("Sun Sprite", sunMaterial, out sunRenderer);
            moonTransform = BuildSprite("Moon Sprite", moonMaterial, out moonRenderer);
            BuildForegroundMarkers();
            ApplyVisibility();
        }

        public void SetSunVisible(bool value)
        {
            SunVisible = value;
            ApplyVisibility();
        }

        public void SetMoonVisible(bool value)
        {
            MoonVisible = value;
            ApplyVisibility();
        }

        private void LateUpdate()
        {
            if (!SunVisible && !MoonVisible)
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
            Vector3 cameraPosition = camera.transform.position;
            lastCameraPosition = cameraPosition;

            float sunAzimuth = 0f;
            float sunAltitude = 0f;
            float moonAzimuth = 0f;
            float moonAltitude = 0f;

            if (SunVisible)
            {
                Vector3 sunEquatorial = SkyMath.SunEquatorialDirection();
                PlaceSprite(sunTransform, sunEquatorial, lstDegrees, latitude, frame, cameraPosition, sunAngularDegrees);
                SkyMath.EnuToAzimuthAltitude(
                    SkyMath.EquatorialToEnu(sunEquatorial, lstDegrees, latitude), out sunAzimuth, out sunAltitude);
                SunStatus = FormatBodyStatus(sunAzimuth, sunAltitude);
            }

            if (MoonVisible)
            {
                Vector3 moonEquatorial = SkyMath.MoonTopocentricEquatorialDirection(lstDegrees, latitude);
                PlaceSprite(moonTransform, moonEquatorial, lstDegrees, latitude, frame, cameraPosition, moonAngularDegrees);
                SkyMath.EnuToAzimuthAltitude(
                    SkyMath.EquatorialToEnu(moonEquatorial, lstDegrees, latitude), out moonAzimuth, out moonAltitude);
                MoonStatus = FormatBodyStatus(moonAzimuth, moonAltitude);
            }

            UpdateForegroundMarkers(lstDegrees, latitude, frame, cameraPosition);

            if (Time.time - lastLogTime > 5f)
            {
                lastLogTime = Time.time;
                Debug.Log(string.Format(
                    "GlassGlobeSunMoon: sunAz={0:0.0} sunAlt={1:0.0} moonAz={2:0.0} moonAlt={3:0.0}",
                    sunAzimuth,
                    sunAltitude,
                    moonAzimuth,
                    moonAltitude));
            }
        }

        private static string FormatBodyStatus(float azimuth, float altitude)
        {
            string placement = altitude < 0f ? "Visible through Earth" : "Visible above horizon";
            return string.Format("{0}. Azimuth {1:0} deg, altitude {2:0} deg", placement, azimuth, altitude);
        }

        public bool TryGetPointedBody(Vector3 worldDirection, out string bodyName)
        {
            bodyName = null;
            float closestAngle = float.MaxValue;
            TrySelect("Sun", sunTransform, SunVisible, sunAngularDegrees, worldDirection, lastCameraPosition, ref closestAngle, ref bodyName);
            TrySelect("Moon", moonTransform, MoonVisible, moonAngularDegrees, worldDirection, lastCameraPosition, ref closestAngle, ref bodyName);

            for (int index = 0; index < foregroundMarkers.Count; index++)
            {
                CelestialMarker marker = foregroundMarkers[index];
                TrySelect(marker.Name, marker.Transform, true, marker.AngularDegrees, worldDirection, lastCameraPosition, ref closestAngle, ref bodyName);
            }

            return bodyName != null;
        }

        private static void TrySelect(string name, Transform target, bool visible, float angularDegrees, Vector3 worldDirection, Vector3 cameraPosition, ref float closestAngle, ref string closestName)
        {
            if (!visible || target == null)
            {
                return;
            }

            Vector3 targetDirection = (target.position - cameraPosition).normalized;
            float angle = Vector3.Angle(worldDirection, targetDirection);
            float selectionRadius = Mathf.Max(2.25f, angularDegrees * 0.6f);
            if (angle <= selectionRadius && angle < closestAngle)
            {
                closestAngle = angle;
                closestName = name;
            }
        }

        private void BuildForegroundMarkers()
        {
            for (int index = 0; index < BrightStarCatalog.Length; index += 3)
            {
                string starName = (string)BrightStarCatalog[index];
                float ra = (float)BrightStarCatalog[index + 1];
                float dec = (float)BrightStarCatalog[index + 2];
                AddForegroundMarker(starName, SkyMath.RaDecToEquatorial(ra, dec), 1.15f, false);
            }

            for (int index = 0; index < PlanetNames.Length; index++)
            {
                AddForegroundMarker(PlanetNames[index], Vector3.forward, 1.8f, true);
            }
        }

        private void AddForegroundMarker(string markerName, Vector3 equatorialDirection, float angularDegrees, bool isPlanet)
        {
            MeshRenderer markerRenderer;
            Transform markerTransform = BuildSprite(markerName + " Marker", isPlanet ? moonMaterial : sunMaterial, out markerRenderer);
            foregroundMarkers.Add(new CelestialMarker
            {
                Name = markerName,
                Transform = markerTransform,
                EquatorialDirection = equatorialDirection,
                AngularDegrees = angularDegrees,
                IsPlanet = isPlanet
            });
        }

        private void UpdateForegroundMarkers(float lstDegrees, float latitude, EarthMath.LocalFrame frame, Vector3 cameraPosition)
        {
            for (int index = 0; index < foregroundMarkers.Count; index++)
            {
                CelestialMarker marker = foregroundMarkers[index];
                if (marker.IsPlanet)
                {
                    marker.EquatorialDirection = SkyMath.PlanetEquatorialDirection(marker.Name);
                }

                PlaceSprite(marker.Transform, marker.EquatorialDirection, lstDegrees, latitude, frame, cameraPosition, marker.AngularDegrees);
            }
        }

        private void PlaceSprite(
            Transform sprite,
            Vector3 equatorialDirection,
            float lstDegrees,
            float latitude,
            EarthMath.LocalFrame frame,
            Vector3 cameraPosition,
            float angularDegrees)
        {
            if (sprite == null)
            {
                return;
            }

            Vector3 worldDirection = SkyMath.EquatorialToWorld(equatorialDirection, lstDegrees, latitude, frame);
            Vector3 position = cameraPosition + worldDirection * radiusUnits;
            float size = 2f * radiusUnits * Mathf.Tan(angularDegrees * 0.5f * Mathf.Deg2Rad);

            sprite.position = position;
            sprite.rotation = Quaternion.LookRotation(worldDirection);
            sprite.localScale = new Vector3(size, size, 1f);
        }

        private void ApplyVisibility()
        {
            if (sunRenderer != null)
            {
                sunRenderer.enabled = SunVisible;
            }

            if (moonRenderer != null)
            {
                moonRenderer.enabled = MoonVisible;
            }

            if (!SunVisible)
            {
                SunStatus = "Hidden";
            }

            if (!MoonVisible)
            {
                MoonStatus = "Hidden";
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

        private Transform BuildSprite(string name, Material material, out MeshRenderer renderer)
        {
            GameObject spriteObject = new GameObject(name);
            spriteObject.transform.SetParent(transform, false);

            MeshFilter meshFilter = spriteObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildQuadMesh(name + " Quad");

            renderer = spriteObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            return spriteObject.transform;
        }

        private static Material EnsureMaterial(Material material, Texture2D texture)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("GlassGlobe/Sky Sprite");
                if (shader == null)
                {
                    return null;
                }

                material = new Material(shader);
            }

            if (material.mainTexture == null)
            {
                material.mainTexture = texture;
            }

            return material;
        }

        private static Texture2D BuildSunTexture()
        {
            return BuildRadialTexture(
                128,
                new Color(1f, 1f, 0.92f, 1f),
                new Color(1f, 0.85f, 0.55f, 0.55f),
                0.12f,
                2.2f);
        }

        private static Texture2D BuildMoonTexture()
        {
            return BuildRadialTexture(
                128,
                new Color(0.93f, 0.95f, 1f, 1f),
                new Color(0.75f, 0.8f, 0.9f, 0.25f),
                0.3f,
                5f);
        }

        private static Texture2D BuildRadialTexture(int size, Color core, Color glow, float coreRadius, float falloffPower)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    Color color;
                    if (distance <= coreRadius)
                    {
                        color = core;
                    }
                    else
                    {
                        float t = Mathf.Clamp01((distance - coreRadius) / Mathf.Max(0.0001f, 1f - coreRadius));
                        float alpha = Mathf.Pow(1f - t, falloffPower);
                        color = Color.Lerp(glow, core, alpha * 0.5f);
                        color.a = glow.a * alpha;
                    }

                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }

        private static Mesh BuildQuadMesh(string name)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

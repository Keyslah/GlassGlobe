using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Dreamy stylized Earth layers: shimmering water and drifting-pastel land
    /// on one shell (driven by a land mask baked from the bundled Natural Earth
    /// rings), and an art cloud shell that reuses the live satellite cloud
    /// field but renders it soft and slowly drifting. Shells are built with
    /// EarthMath.GeoToPoint so they inherit the globe's mirrored embedding, and
    /// cull the near hemisphere like the weather shells.
    /// </summary>
    public sealed class EarthArtOverlay : MonoBehaviour
    {
        private const string LandMaskResource = "GlassGlobeLandMask";

        public GlobeRenderer globe;
        public WeatherOverlay weatherOverlay;

        [Tooltip("Assigned at scene build time so the art shader is not stripped from the player build.")]
        public Material earthArtMaterial;

        [Tooltip("Assigned at scene build time so the art clouds shader is not stripped from the player build.")]
        public Material artCloudMaterial;

        [Min(0f)]
        public float artSurfaceOffset = 0.012f;

        [Min(0f)]
        public float artCloudSurfaceOffset = 0.028f;

        [Range(8, 128)]
        public int longitudeSegments = 96;

        [Range(4, 64)]
        public int latitudeSegments = 48;

        [Tooltip("Cloud drift in texture UV per second. 0.0002 wraps the world in about 80 minutes - noticeable only when you stare.")]
        [Range(0f, 0.002f)]
        public float cloudDriftSpeed = 0.0002f;

        public bool WaterVisible { get; private set; }
        public bool LandVisible { get; private set; }
        public bool ArtCloudsVisible { get; private set; }
        public string EarthArtStatus { get; private set; }
        public string ArtCloudsStatus { get; private set; }

        private const float MaxShellLatitude = 85f;

        private MeshRenderer earthArtRenderer;
        private MeshRenderer artCloudRenderer;
        private Texture2D landMask;
        private bool cloudTextureAssigned;

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            WaterVisible = GlassGlobeSettingsState.WaterArtEnabled;
            LandVisible = GlassGlobeSettingsState.LandArtEnabled;
            ArtCloudsVisible = GlassGlobeSettingsState.ArtCloudsEnabled;
            EarthArtStatus = "Initializing";
            ArtCloudsStatus = "Initializing";

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (weatherOverlay == null)
            {
                weatherOverlay = FindFirstObjectByType<WeatherOverlay>();
            }

            landMask = Resources.Load<Texture2D>(LandMaskResource);
            BuildLayers();
            ApplyAll();
        }

        private void Update()
        {
            if (!ArtCloudsVisible || artCloudMaterial == null || weatherOverlay == null)
            {
                return;
            }

            if (!cloudTextureAssigned && weatherOverlay.CloudTexture != null)
            {
                artCloudMaterial.mainTexture = weatherOverlay.CloudTexture;
                cloudTextureAssigned = true;
            }

            if (cloudTextureAssigned)
            {
                ArtCloudsStatus = weatherOverlay.HasCloudData
                    ? "Drifting. Shapes follow live satellite clouds"
                    : "Waiting for live cloud data...";
            }
        }

        public void SetWaterVisible(bool value)
        {
            WaterVisible = value;
            ApplyAll();
        }

        public void SetLandVisible(bool value)
        {
            LandVisible = value;
            ApplyAll();
        }

        public void SetArtCloudsVisible(bool value)
        {
            ArtCloudsVisible = value;
            ApplyAll();
        }

        public void ApplyOpacities()
        {
            ApplyAll();
        }

        private void ApplyAll()
        {
            if (earthArtRenderer != null)
            {
                earthArtRenderer.enabled = WaterVisible || LandVisible;
            }

            if (earthArtMaterial != null)
            {
                earthArtMaterial.SetFloat(
                    "_WaterAlpha", WaterVisible ? GlassGlobeSettingsState.WaterArtOpacity : 0f);
                earthArtMaterial.SetFloat(
                    "_LandAlpha", LandVisible ? GlassGlobeSettingsState.LandArtOpacity : 0f);
            }

            if (artCloudRenderer != null)
            {
                artCloudRenderer.enabled = ArtCloudsVisible;
            }

            if (artCloudMaterial != null)
            {
                artCloudMaterial.SetFloat("_Opacity", GlassGlobeSettingsState.ArtCloudsOpacity);
                artCloudMaterial.SetFloat("_DriftSpeed", cloudDriftSpeed);
            }

            if (weatherOverlay != null)
            {
                weatherOverlay.SetArtCloudsWantData(ArtCloudsVisible);
            }

            if (landMask == null)
            {
                EarthArtStatus = "Land mask missing from Resources";
            }
            else if (!WaterVisible && !LandVisible)
            {
                EarthArtStatus = "Hidden";
            }
            else
            {
                string layers = WaterVisible && LandVisible
                    ? "water + land"
                    : (WaterVisible ? "water" : "land");
                EarthArtStatus = string.Format(
                    "Shimmering ({0}). Water {1:0}%, land {2:0}%",
                    layers,
                    GlassGlobeSettingsState.WaterArtOpacity * 100f,
                    GlassGlobeSettingsState.LandArtOpacity * 100f);
            }

            if (!ArtCloudsVisible)
            {
                ArtCloudsStatus = "Hidden";
            }
        }

        private void BuildLayers()
        {
            if (earthArtMaterial == null)
            {
                Shader artShader = Shader.Find("GlassGlobe/Earth Art");
                if (artShader != null)
                {
                    earthArtMaterial = new Material(artShader);
                }
            }

            if (artCloudMaterial == null)
            {
                Shader cloudShader = Shader.Find("GlassGlobe/Art Clouds");
                if (cloudShader != null)
                {
                    artCloudMaterial = new Material(cloudShader);
                }
            }

            if (earthArtMaterial == null || artCloudMaterial == null)
            {
                EarthArtStatus = "Art shaders missing from build";
                ArtCloudsStatus = "Art shaders missing from build";
                return;
            }

            if (landMask != null)
            {
                earthArtMaterial.SetTexture("_LandMask", landMask);
            }

            // Below the live weather stack: art surface 3002, art clouds 3005
            // (between satellite clouds 3004 and radar 3006), globe glass 3008,
            // border lines 3010.
            earthArtMaterial.renderQueue = 3002;
            artCloudMaterial.renderQueue = 3005;

            float radius = globe != null ? globe.RadiusUnits : EarthMath.DefaultEarthRadiusUnits;
            Vector3 center = globe != null ? globe.Center : Vector3.zero;

            earthArtRenderer = BuildShell("Earth Art Shell", radius + artSurfaceOffset, center, earthArtMaterial);
            artCloudRenderer = BuildShell("Art Cloud Shell", radius + artCloudSurfaceOffset, center, artCloudMaterial);
        }

        private MeshRenderer BuildShell(string name, float radius, Vector3 center, Material material)
        {
            GameObject shellObject = new GameObject(name);
            shellObject.transform.SetParent(transform, false);
            shellObject.transform.position = center;

            MeshFilter meshFilter = shellObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildShellMesh(name + " Mesh", radius);

            MeshRenderer shellRenderer = shellObject.AddComponent<MeshRenderer>();
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
            shellRenderer.sharedMaterial = material;
            return shellRenderer;
        }

        private Mesh BuildShellMesh(string name, float radius)
        {
            int lonCount = Mathf.Max(8, longitudeSegments);
            int latCount = Mathf.Max(4, latitudeSegments);
            int vertexCount = (latCount + 1) * (lonCount + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[latCount * lonCount * 6];

            int vertexIndex = 0;
            for (int latIndex = 0; latIndex <= latCount; latIndex++)
            {
                float latitude = Mathf.Lerp(-MaxShellLatitude, MaxShellLatitude, latIndex / (float)latCount);
                for (int lonIndex = 0; lonIndex <= lonCount; lonIndex++)
                {
                    float u = lonIndex / (float)lonCount;
                    float longitude = Mathf.Lerp(-180f, 180f, u);
                    vertices[vertexIndex] = EarthMath.GeoToPoint(
                        new GeoCoordinate(latitude, longitude), radius, Vector3.zero);
                    uvs[vertexIndex] = new Vector2(u, (latitude + 90f) / 180f);
                    vertexIndex++;
                }
            }

            int triangleIndex = 0;
            int stride = lonCount + 1;
            for (int latIndex = 0; latIndex < latCount; latIndex++)
            {
                for (int lonIndex = 0; lonIndex < lonCount; lonIndex++)
                {
                    int current = latIndex * stride + lonIndex;
                    int next = current + stride;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;

                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

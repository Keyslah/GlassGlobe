using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace GlassGlobe
{
    /// <summary>
    /// Drapes live weather over the glass Earth as two independently toggleable
    /// layers: satellite infrared cloud cover and precipitation radar. Clouds
    /// come from NASA GIBS geostationary mosaics (GOES-East/West, Himawari)
    /// merged with EUMETSAT Meteosat imagery to fill the Europe/Africa gap;
    /// radar comes from RainViewer's public past-radar tiles. Both are
    /// composited into plain equirectangular textures, so the shell meshes are
    /// built from EarthMath.GeoToPoint and inherit the globe's mirrored
    /// embedding — weather lands on the true latitude/longitude seen through
    /// the planet. Shells cull the near hemisphere (Cull Front) so weather
    /// paints only the far side, matching the transparent globe's look-through.
    /// </summary>
    public sealed class WeatherOverlay : MonoBehaviour
    {
        public GlobeRenderer globe;

        [Tooltip("Assigned at scene build time so the weather shader is not stripped from the player build.")]
        public Material cloudMaterial;

        [Tooltip("Assigned at scene build time so the weather shader is not stripped from the player build.")]
        public Material radarMaterial;

        [Min(0f)]
        public float cloudSurfaceOffset = 0.02f;

        [Min(0f)]
        public float radarSurfaceOffset = 0.035f;

        [Range(8, 128)]
        public int longitudeSegments = 96;

        [Range(4, 64)]
        public int latitudeSegments = 48;

        [Tooltip("Equirectangular weather texture width; height is half. Must be 512, 1024, or 2048 so radar tiles stitch exactly.")]
        public int textureWidth = 2048;

        [Range(60f, 3600f)]
        public float refreshSeconds = 600f;

        [Range(30f, 600f)]
        public float retrySeconds = 120f;

        [Range(0f, 1f)]
        public float cloudOpacity = 0.85f;

        [Range(0f, 1f)]
        public float radarOpacity = 0.9f;

        // GIBS Clean Infrared is mid-gray for warm surface, brighter gray for
        // cloud, and switches to a color-enhanced palette for the coldest
        // tops; EUMETSAT IR 10.8 is plain grayscale with white cloud tops.
        [Range(0f, 1f)]
        public float gibsCloudFloor = 0.55f;

        [Range(0f, 1f)]
        public float gibsCloudCeiling = 0.9f;

        [Range(0f, 1f)]
        public float gibsColorSaturationThreshold = 0.25f;

        [Range(0f, 1f)]
        public float eumetsatCloudFloor = 0.45f;

        [Range(0f, 1f)]
        public float eumetsatCloudCeiling = 0.85f;

        // Infrared cannot tell cold polar sea or ice from cloud, so the winter
        // hemisphere's high latitudes read as a solid cloud sheet. Fade the
        // cloud layer out toward the poles instead of showing that artifact.
        [Range(0f, 90f)]
        public float cloudFadeStartLatitude = 55f;

        [Range(0f, 90f)]
        public float cloudFadeEndLatitude = 75f;

        public bool CloudsVisible { get; private set; }
        public bool RadarVisible { get; private set; }
        public string CloudsStatus { get; private set; }
        public string RadarStatus { get; private set; }

        public Texture2D CloudTexture
        {
            get { return cloudTexture; }
        }

        public bool HasCloudData
        {
            get { return hasCloudData; }
        }

        private const float MaxShellLatitude = 85f;
        private const float MaxMercatorLatitude = 85.0511f;
        private const int RadarTileSize = 512;
        private const string RadarColorScheme = "2";
        private const string RadarOptions = "1_1";
        private const int RowsPerSlice = 64;

        private const string GibsWmsUrlFormat =
            "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap" +
            "&LAYERS=GOES-West_ABI_Band13_Clean_Infrared,GOES-East_ABI_Band13_Clean_Infrared,Himawari_AHI_Band13_Clean_Infrared" +
            "&CRS=EPSG:4326&BBOX=-90,-180,90,180&WIDTH={0}&HEIGHT={1}&FORMAT=image/png&TRANSPARENT=TRUE&TIME=default";

        private const string EumetsatWmsUrlFormat =
            "https://view.eumetsat.int/geoserver/wms?service=WMS&version=1.3.0&request=GetMap" +
            "&layers=msg_fes:ir108,msg_iodc:ir108" +
            "&crs=EPSG:4326&bbox=-90,-180,90,180&width={0}&height={1}&format=image/png&transparent=true";

        private const string RainViewerIndexUrl = "https://api.rainviewer.com/public/weather-maps.json";

        private MeshRenderer cloudRenderer;
        private MeshRenderer radarRenderer;
        private Texture2D cloudTexture;
        private Texture2D radarTexture;
        private Color32[] radarPixels;
        private bool hasCloudData;
        private bool hasRadarData;
        private bool artCloudsWantData;
        private string lastRadarFramePath;
        private float nextCloudAttemptTime;
        private float nextRadarAttemptTime;

        [Serializable]
        private sealed class RainViewerMaps
        {
            public string host;
            public RainViewerRadar radar;
        }

        [Serializable]
        private sealed class RainViewerRadar
        {
            public RainViewerFrame[] past;
        }

        [Serializable]
        private sealed class RainViewerFrame
        {
            public long time;
            public string path;
        }

        private int TextureWidthClamped
        {
            get
            {
                if (textureWidth >= 2048)
                {
                    return 2048;
                }

                return textureWidth >= 1024 ? 1024 : 512;
            }
        }

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            CloudsVisible = GlassGlobeSettingsState.WeatherCloudsEnabled;
            RadarVisible = GlassGlobeSettingsState.WeatherRadarEnabled;
            CloudsStatus = "Initializing";
            RadarStatus = "Initializing";

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            BuildLayers();
            ApplyVisibility();
        }

        private void Start()
        {
            StartCoroutine(RefreshLoop());
        }

        public void SetCloudsVisible(bool value)
        {
            CloudsVisible = value;
            if (value && !hasCloudData)
            {
                nextCloudAttemptTime = 0f;
            }

            ApplyVisibility();
        }

        /// <summary>
        /// The art cloud layer feeds off the live cloud texture, so keep the
        /// cloud data refreshing even while the satellite cloud layer is off.
        /// </summary>
        public void SetArtCloudsWantData(bool value)
        {
            artCloudsWantData = value;
            if (value && !hasCloudData)
            {
                nextCloudAttemptTime = 0f;
            }
        }

        public void SetRadarVisible(bool value)
        {
            RadarVisible = value;
            if (value && !hasRadarData)
            {
                nextRadarAttemptTime = 0f;
            }

            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (cloudRenderer != null)
            {
                cloudRenderer.enabled = CloudsVisible;
            }

            if (radarRenderer != null)
            {
                radarRenderer.enabled = RadarVisible;
            }

            if (!CloudsVisible)
            {
                CloudsStatus = "Hidden";
            }

            if (!RadarVisible)
            {
                RadarStatus = "Hidden";
            }
        }

        private IEnumerator RefreshLoop()
        {
            WaitForSecondsRealtime pollWait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                if ((CloudsVisible || artCloudsWantData) && cloudTexture != null && Time.unscaledTime >= nextCloudAttemptTime)
                {
                    yield return RefreshClouds();
                }

                if (RadarVisible && radarTexture != null && Time.unscaledTime >= nextRadarAttemptTime)
                {
                    yield return RefreshRadar();
                }

                yield return pollWait;
            }
        }

        private IEnumerator RefreshClouds()
        {
            CloudsStatus = "Updating clouds...";
            int width = TextureWidthClamped;
            int height = width / 2;
            Color32[] merged = new Color32[width * height];
            bool[] gibsResult = { false };
            bool[] eumetsatResult = { false };

            yield return FetchCloudSource(
                string.Format(GibsWmsUrlFormat, width, height),
                gibsCloudFloor,
                gibsCloudCeiling,
                gibsColorSaturationThreshold,
                merged,
                width,
                height,
                gibsResult);

            yield return FetchCloudSource(
                string.Format(EumetsatWmsUrlFormat, width, height),
                eumetsatCloudFloor,
                eumetsatCloudCeiling,
                2f,
                merged,
                width,
                height,
                eumetsatResult);

            if (!gibsResult[0] && !eumetsatResult[0])
            {
                CloudsStatus = "Cloud imagery unavailable; retrying";
                nextCloudAttemptTime = Time.unscaledTime + retrySeconds;
                yield break;
            }

            cloudTexture.SetPixels32(merged);
            cloudTexture.Apply(false);
            hasCloudData = true;

            string sources = gibsResult[0] && eumetsatResult[0]
                ? "NASA GIBS + EUMETSAT"
                : (gibsResult[0] ? "NASA GIBS" : "EUMETSAT");
            CloudsStatus = string.Format("Updated {0:HH:mm} UTC. Imagery: {1}", DateTime.UtcNow, sources);
            nextCloudAttemptTime = Time.unscaledTime + refreshSeconds;
        }

        private IEnumerator FetchCloudSource(
            string url,
            float cloudFloor,
            float cloudCeiling,
            float saturationThreshold,
            Color32[] destination,
            int width,
            int height,
            bool[] result)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, false))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("GlassGlobeWeather: cloud source failed: " + request.error + " url=" + url);
                    yield break;
                }

                Texture2D downloaded = null;
                try
                {
                    downloaded = DownloadHandlerTexture.GetContent(request);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("GlassGlobeWeather: cloud source returned a non-image response: " + exception.Message);
                }

                if (downloaded == null || downloaded.width != width || downloaded.height != height)
                {
                    if (downloaded != null)
                    {
                        Destroy(downloaded);
                    }

                    yield break;
                }

                Color32[] pixels = downloaded.GetPixels32();
                Destroy(downloaded);

                float range = Mathf.Max(0.0001f, cloudCeiling - cloudFloor);
                float fadeEnd = Mathf.Max(cloudFadeEndLatitude, cloudFadeStartLatitude + 0.1f);
                for (int row = 0; row < height; row += RowsPerSlice)
                {
                    int rowEnd = Mathf.Min(height, row + RowsPerSlice);
                    for (int pixelIndex = row * width; pixelIndex < rowEnd * width; pixelIndex++)
                    {
                        float latitude = Mathf.Abs(-90f + (pixelIndex / width + 0.5f) * 180f / height);
                        float polarFade = 1f - Mathf.Clamp01(
                            (latitude - cloudFadeStartLatitude) / (fadeEnd - cloudFadeStartLatitude));
                        if (polarFade <= 0f)
                        {
                            continue;
                        }

                        Color32 color = pixels[pixelIndex];
                        if (color.a < 8)
                        {
                            continue;
                        }

                        int maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                        int minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
                        float brightness = maxChannel / 255f;
                        float saturation = maxChannel > 0 ? (maxChannel - minChannel) / (float)maxChannel : 0f;

                        float cloud = saturation > saturationThreshold
                            ? 1f
                            : Mathf.Clamp01((brightness - cloudFloor) / range);
                        cloud *= polarFade * (color.a / 255f);
                        if (cloud <= 0f)
                        {
                            continue;
                        }

                        byte alpha = (byte)Mathf.RoundToInt(cloud * 255f);
                        if (alpha > destination[pixelIndex].a)
                        {
                            destination[pixelIndex] = new Color32(255, 255, 255, alpha);
                        }
                    }

                    yield return null;
                }

                result[0] = true;
            }
        }

        private IEnumerator RefreshRadar()
        {
            RadarStatus = "Updating radar...";
            string indexText;
            using (UnityWebRequest request = UnityWebRequest.Get(RainViewerIndexUrl))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    RadarStatus = "Radar index unavailable; retrying";
                    nextRadarAttemptTime = Time.unscaledTime + retrySeconds;
                    yield break;
                }

                indexText = request.downloadHandler.text;
            }

            RainViewerMaps maps = null;
            try
            {
                maps = JsonUtility.FromJson<RainViewerMaps>(indexText);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("GlassGlobeWeather: radar index parse failed: " + exception.Message);
            }

            if (maps == null || string.IsNullOrEmpty(maps.host) ||
                maps.radar == null || maps.radar.past == null || maps.radar.past.Length == 0)
            {
                RadarStatus = "Radar index empty; retrying";
                nextRadarAttemptTime = Time.unscaledTime + retrySeconds;
                yield break;
            }

            RainViewerFrame frame = maps.radar.past[maps.radar.past.Length - 1];
            string frameTimeUtc = DateTimeOffset.FromUnixTimeSeconds(frame.time).UtcDateTime.ToString("HH:mm");
            if (frame.path == lastRadarFramePath && hasRadarData)
            {
                RadarStatus = string.Format("Radar {0} UTC. Data: RainViewer.com", frameTimeUtc);
                nextRadarAttemptTime = Time.unscaledTime + refreshSeconds;
                yield break;
            }

            int width = TextureWidthClamped;
            int height = width / 2;
            int zoom = 0;
            while (RadarTileSize << (zoom + 1) <= width)
            {
                zoom++;
            }

            int tilesPerSide = 1 << zoom;
            int canvasSide = tilesPerSide * RadarTileSize;
            Color32[] canvas = new Color32[canvasSide * canvasSide];

            for (int tileY = 0; tileY < tilesPerSide; tileY++)
            {
                for (int tileX = 0; tileX < tilesPerSide; tileX++)
                {
                    string tileUrl = maps.host + frame.path + "/" + RadarTileSize + "/" + zoom + "/" +
                        tileX + "/" + tileY + "/" + RadarColorScheme + "/" + RadarOptions + ".png";

                    using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(tileUrl, false))
                    {
                        request.timeout = 30;
                        yield return request.SendWebRequest();

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            RadarStatus = "Radar tiles unavailable; retrying";
                            nextRadarAttemptTime = Time.unscaledTime + retrySeconds;
                            yield break;
                        }

                        Texture2D tile = DownloadHandlerTexture.GetContent(request);
                        if (tile == null || tile.width != RadarTileSize || tile.height != RadarTileSize)
                        {
                            if (tile != null)
                            {
                                Destroy(tile);
                            }

                            RadarStatus = "Radar tile malformed; retrying";
                            nextRadarAttemptTime = Time.unscaledTime + retrySeconds;
                            yield break;
                        }

                        Color32[] tilePixels = tile.GetPixels32();
                        Destroy(tile);

                        // XYZ tile row 0 is the north edge; both the tile pixel
                        // array and the canvas use bottom-left origin.
                        int canvasBaseRow = canvasSide - (tileY + 1) * RadarTileSize;
                        for (int localRow = 0; localRow < RadarTileSize; localRow++)
                        {
                            Array.Copy(
                                tilePixels,
                                localRow * RadarTileSize,
                                canvas,
                                (canvasBaseRow + localRow) * canvasSide + tileX * RadarTileSize,
                                RadarTileSize);
                        }
                    }

                    yield return null;
                }
            }

            if (radarPixels == null || radarPixels.Length != width * height)
            {
                radarPixels = new Color32[width * height];
            }

            // Web Mercator x is linear in longitude, matching equirectangular
            // columns exactly, so reprojection reduces to a per-row remap.
            for (int row = 0; row < height; row++)
            {
                float latitude = -90f + (row + 0.5f) * 180f / height;
                if (Mathf.Abs(latitude) >= MaxMercatorLatitude)
                {
                    Array.Clear(radarPixels, row * width, width);
                    continue;
                }

                float latitudeRadians = latitude * Mathf.Deg2Rad;
                float mercatorFromTop =
                    (1f - Mathf.Log(Mathf.Tan(Mathf.PI * 0.25f + latitudeRadians * 0.5f)) / Mathf.PI) * 0.5f;
                int sourceRowFromTop = Mathf.Clamp(
                    Mathf.FloorToInt(mercatorFromTop * canvasSide), 0, canvasSide - 1);
                int sourceRow = canvasSide - 1 - sourceRowFromTop;
                Array.Copy(canvas, sourceRow * canvasSide, radarPixels, row * width, width);
            }

            radarTexture.SetPixels32(radarPixels);
            radarTexture.Apply(false);
            hasRadarData = true;
            lastRadarFramePath = frame.path;
            RadarStatus = string.Format("Radar {0} UTC. Data: RainViewer.com", frameTimeUtc);
            nextRadarAttemptTime = Time.unscaledTime + refreshSeconds;
        }

        private void BuildLayers()
        {
            int width = TextureWidthClamped;
            int height = width / 2;

            cloudTexture = CreateWeatherTexture("GlassGlobe Cloud Map", width, height);
            radarTexture = CreateWeatherTexture("GlassGlobe Radar Map", width, height);

            cloudMaterial = EnsureMaterial(cloudMaterial, cloudTexture, cloudOpacity, 3004);
            radarMaterial = EnsureMaterial(radarMaterial, radarTexture, radarOpacity, 3006);
            if (cloudMaterial == null || radarMaterial == null)
            {
                CloudsStatus = "Weather shader missing from build";
                RadarStatus = "Weather shader missing from build";
                return;
            }

            float radius = globe != null ? globe.RadiusUnits : EarthMath.DefaultEarthRadiusUnits;
            Vector3 center = globe != null ? globe.Center : Vector3.zero;

            cloudRenderer = BuildShell("Cloud Shell", radius + cloudSurfaceOffset, center, cloudMaterial);
            radarRenderer = BuildShell("Radar Shell", radius + radarSurfaceOffset, center, radarMaterial);
        }

        private static Texture2D CreateWeatherTexture(string name, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapModeU = TextureWrapMode.Repeat;
            texture.wrapModeV = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(new Color32[width * height]);
            texture.Apply(false);
            return texture;
        }

        private static Material EnsureMaterial(Material material, Texture2D texture, float opacity, int renderQueue)
        {
            if (material == null)
            {
                Shader shader = Shader.Find("GlassGlobe/Weather");
                if (shader == null)
                {
                    return null;
                }

                material = new Material(shader);
            }

            material.mainTexture = texture;
            material.renderQueue = renderQueue;
            if (material.HasProperty("_Tint"))
            {
                material.SetColor("_Tint", new Color(1f, 1f, 1f, opacity));
            }

            return material;
        }

        private MeshRenderer BuildShell(string name, float radius, Vector3 center, Material material)
        {
            GameObject shellObject = new GameObject(name);
            shellObject.transform.SetParent(transform, false);
            shellObject.transform.position = center;

            MeshFilter meshFilter = shellObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildShellMesh(name + " Mesh", radius);

            MeshRenderer renderer = shellObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            return renderer;
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

                    // GeoToPoint carries the globe's mirrored embedding, so the
                    // equirectangular weather texture reads true through the
                    // left-handed camera. It also flips triangle orientation
                    // relative to a standard sphere: with this index order the
                    // far-side interior is the back face, which is what the
                    // shader's Cull Front keeps.
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

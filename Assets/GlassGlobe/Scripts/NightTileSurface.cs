using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlassGlobe
{
    /// <summary>
    /// Streams the visible portion of NASA's literal 86400x43200 Black Marble
    /// source on Android. The existing 3600x1800 map remains visible until a
    /// decoded patch is uploaded, so enabling Earth at Night is immediate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NightTileSurface : MonoBehaviour
    {
        private const int MaxResidentTiles = 32;
        private const int LowMemoryResidentTiles = 12;
        private const int MaxPendingDecodes = 2;
        private const float ViewRefreshSeconds = 0.18f;
        private const float LodStabilitySeconds = 0.75f;
        private const float SourceProbeRetryBaseSeconds = 2f;
        private const float SourceProbeRetryMaxSeconds = 30f;
        private const float PrefetchDegrees = 1.5f;
        private const int ViewSampleGridSize = 5;

        private static readonly int CoverageTextureId =
            Shader.PropertyToID("_NightCoverageTex");
        private static readonly int TileTextureId =
            Shader.PropertyToID("_NightTileTex");
        private static readonly int NightOpacityId =
            Shader.PropertyToID("_NightOpacity");
        private static readonly int RimColorId =
            Shader.PropertyToID("_RimColor");
        private static readonly int RimIntensityId =
            Shader.PropertyToID("_RimIntensity");
        private static readonly int RimPowerId =
            Shader.PropertyToID("_RimPower");

        public GlobeRenderer globe;
        public Camera targetCamera;

        public string Status { get; private set; }

        public int ResidentTileCount
        {
            get { return residentTiles.Count; }
        }

        public int VisibleTileCount { get; private set; }

        public NightMapTileLod ActiveLod
        {
            get { return currentLod; }
        }

        private readonly Dictionary<NightMapTileKey, ResidentTile> residentTiles =
            new Dictionary<NightMapTileKey, ResidentTile>();
        private readonly HashSet<NightMapTileKey> requiredKeys =
            new HashSet<NightMapTileKey>();
        private readonly Dictionary<NightMapTileKey, TileFailure> tileFailures =
            new Dictionary<NightMapTileKey, TileFailure>();
        private readonly List<NightMapTileKey> requiredOrder =
            new List<NightMapTileKey>();
        private readonly List<PendingTile> pendingTiles =
            new List<PendingTile>();

        private Material baseMaterial;
        private Material tileMaterial;
        private MaterialPropertyBlock tileProperties;
        private Texture2D coverageTexture;
        private Color32[] coveragePixels;
        private CancellationTokenSource lifetimeCancellation;
        private CancellationTokenSource decodeCancellation;
        private Task<int[]> sourceProbeTask;
        private bool sourceProbeStarted;
        private bool sourceReady;
        private bool nightEnabled;
        private bool destroying;
        private bool hasCurrentLod;
        private bool hasPendingLod;
        private bool loggedFirstTileForLod;
        private bool residentLimitConfigured;
        private float nightOpacity;
        private float nextSourceProbeTime;
        private float nextViewRefreshTime;
        private float pendingLodSince;
        private int generation;
        private int residentTileLimit = MaxResidentTiles;
        private int sourceProbeFailureCount;
        private NightMapTileLod currentLod = NightMapTileLod.Sample4;
        private NightMapTileLod pendingLod = NightMapTileLod.Sample4;

        public static NightTileSurface EnsureInstance(GlobeRenderer globeRenderer)
        {
            if (globeRenderer == null)
            {
                return null;
            }

            NightTileSurface surface =
                globeRenderer.GetComponent<NightTileSurface>();
            if (surface == null)
            {
                surface = globeRenderer.gameObject.AddComponent<NightTileSurface>();
            }

            surface.globe = globeRenderer;
            return surface;
        }

        private void Awake()
        {
            lifetimeCancellation = new CancellationTokenSource();
            Application.lowMemory += HandleLowMemory;
            Status = "Fallback active";
        }

        private void Update()
        {
            ProcessSourceProbe();
            if (nightEnabled &&
                AndroidNightMapRegionDecoder.IsSupported &&
                !sourceReady &&
                !destroying)
            {
                StartSourceProbe();
            }

            ProcessOneCompletedDecode();

            if (!nightEnabled ||
                !AndroidNightMapRegionDecoder.IsSupported ||
                !sourceReady ||
                destroying)
            {
                return;
            }

            SynchronizeTileMaterial();

            if (Time.unscaledTime >= nextViewRefreshTime)
            {
                nextViewRefreshTime = Time.unscaledTime + ViewRefreshSeconds;
                RefreshRequiredTiles();
            }

            ScheduleRequiredTiles();
        }

        private void OnDestroy()
        {
            destroying = true;
            nightEnabled = false;
            Application.lowMemory -= HandleLowMemory;

            if (decodeCancellation != null)
            {
                decodeCancellation.Cancel();
                decodeCancellation.Dispose();
                decodeCancellation = null;
            }

            if (lifetimeCancellation != null)
            {
                lifetimeCancellation.Cancel();
                lifetimeCancellation.Dispose();
                lifetimeCancellation = null;
            }

            if (baseMaterial != null && baseMaterial.HasProperty(CoverageTextureId))
            {
                baseMaterial.SetTexture(CoverageTextureId, null);
            }

            ClearResidentTiles();
            DestroyRuntimeObject(tileMaterial);
            DestroyRuntimeObject(coverageTexture);
            tileMaterial = null;
            coverageTexture = null;
        }

        private void OnApplicationQuit()
        {
            // Scene reloads reuse the static Java decoder cache. Only process
            // shutdown closes it; the bridge globally coalesces duplicate quit
            // requests from any additional globe instance.
            if (AndroidNightMapRegionDecoder.IsSupported)
            {
                AndroidNightMapRegionDecoder.ShutdownAsync();
            }
        }

        /// <summary>
        /// Enables or disables full-resolution patches without changing the
        /// global fallback map controlled by EarthStyleController.
        /// </summary>
        public void SetNightState(
            Material globeMaterial,
            bool enabled,
            float opacity)
        {
            if (destroying)
            {
                return;
            }

            if (baseMaterial != globeMaterial)
            {
                if (baseMaterial != null &&
                    baseMaterial.HasProperty(CoverageTextureId))
                {
                    baseMaterial.SetTexture(CoverageTextureId, null);
                }

                baseMaterial = globeMaterial;
            }

            EnsureCoverageTexture();
            BindCoverageTexture();
            nightOpacity = Mathf.Clamp01(opacity);

            bool stateChanged = nightEnabled != enabled;
            nightEnabled = enabled;
            if (!enabled)
            {
                if (stateChanged || residentTiles.Count > 0)
                {
                    BeginDecodeGeneration(false);
                    ClearResidentTiles();
                }

                ClearCoverage();
                hasCurrentLod = false;
                hasPendingLod = false;
                Status = "Hidden";
                return;
            }

            if (!AndroidNightMapRegionDecoder.IsSupported)
            {
                ClearCoverage();
                Status = "3600x1800 fallback active (full resolution is Android-only)";
                return;
            }

            ConfigureResidentLimit();

            EnsureTileMaterial();
            if (tileMaterial == null)
            {
                ClearCoverage();
                Status = "3600x1800 fallback active (night tile shader missing)";
                return;
            }

            if (stateChanged || decodeCancellation == null)
            {
                BeginDecodeGeneration(true);
                nextViewRefreshTime = 0f;
            }

            SynchronizeTileMaterial();
            StartSourceProbe();
        }

        private void StartSourceProbe()
        {
            if (sourceProbeStarted ||
                sourceReady ||
                lifetimeCancellation == null ||
                Time.unscaledTime < nextSourceProbeTime)
            {
                return;
            }

            sourceProbeStarted = true;
            Status = "Verifying eight full-resolution NASA night sources";
            sourceProbeTask =
                AndroidNightMapRegionDecoder.ProbeSourceDimensionsAsync(
                    lifetimeCancellation.Token);
        }

        private void ProcessSourceProbe()
        {
            if (sourceProbeTask == null || !sourceProbeTask.IsCompleted)
            {
                return;
            }

            Task<int[]> completed = sourceProbeTask;
            sourceProbeTask = null;
            sourceProbeStarted = false;
            try
            {
                completed.GetAwaiter().GetResult();
                sourceReady = true;
                sourceProbeFailureCount = 0;
                nextSourceProbeTime = 0f;
                Status = "Full-resolution NASA sources verified; loading visible tiles";
                Debug.Log(
                    "GlassGlobe full-resolution night sources verified: " +
                    "8 x 21600x21600, global 86400x43200.");
                nextViewRefreshTime = 0f;
            }
            catch (OperationCanceledException)
            {
                if (!destroying)
                {
                    Status = "Full-resolution source verification canceled";
                }
            }
            catch (Exception exception)
            {
                sourceReady = false;
                sourceProbeFailureCount++;
                float retryDelay = Mathf.Min(
                    SourceProbeRetryMaxSeconds,
                    SourceProbeRetryBaseSeconds * Mathf.Pow(
                        2f,
                        Mathf.Min(sourceProbeFailureCount - 1, 4)));
                nextSourceProbeTime = Time.unscaledTime + retryDelay;
                Status = "3600x1800 fallback active (retrying full-resolution sources)";
                Debug.LogWarning(
                    "GlassGlobe full-resolution night source verification failed: " +
                    exception + " Retrying in " + retryDelay.ToString("0") +
                    " seconds.");
            }
        }

        private void RefreshRequiredTiles()
        {
            ViewInfo view;
            if (!TryGetViewInfo(out view))
            {
                ClearRequiredView();
                Status = "3600x1800 fallback active (globe is outside the viewport)";
                return;
            }

            NightMapTileLod wantedLod = SelectLod(view.PixelsPerDegree);
            List<TileCandidate> candidates = BuildCandidates(wantedLod, view);
            while (candidates.Count > residentTileLimit &&
                wantedLod != NightMapTileLod.Sample4)
            {
                wantedLod = CoarserLod(wantedLod);
                candidates = BuildCandidates(wantedLod, view);
            }

            if (candidates.Count > residentTileLimit)
            {
                candidates.RemoveRange(
                    residentTileLimit,
                    candidates.Count - residentTileLimit);
            }

            if (ShouldDeferLodChange(wantedLod))
            {
                wantedLod = currentLod;
                candidates = BuildCandidates(wantedLod, view);
                if (candidates.Count > residentTileLimit)
                {
                    candidates.RemoveRange(
                        residentTileLimit,
                        candidates.Count - residentTileLimit);
                }
            }

            if (!hasCurrentLod || currentLod != wantedLod)
            {
                currentLod = wantedLod;
                hasCurrentLod = true;
                hasPendingLod = false;
                loggedFirstTileForLod = false;
                BeginDecodeGeneration(true);
                ClearResidentTiles();
                Debug.Log(
                    "GlassGlobe full-resolution night selected " + currentLod +
                    " (" + PixelsPerDegree(currentLod) +
                    " source pixels/degree for " +
                    view.PixelsPerDegree.ToString("0.0") +
                    " screen pixels/degree).");
            }

            requiredKeys.Clear();
            requiredOrder.Clear();
            for (int index = 0; index < candidates.Count; index++)
            {
                NightMapTileKey key = candidates[index].Key;
                requiredKeys.Add(key);
                requiredOrder.Add(key);
            }

            bool coverageChanged = false;
            foreach (ResidentTile tile in residentTiles.Values)
            {
                bool visible = requiredKeys.Contains(tile.Key);
                if (tile.Renderer.enabled != visible)
                {
                    tile.Renderer.enabled = visible;
                    coverageChanged = true;
                }

                if (visible)
                {
                    tile.LastUsedFrame = Time.frameCount;
                }
            }

            EvictUnneededTiles();
            if (coverageChanged)
            {
                RebuildCoverage();
            }

            UpdateStatus();
        }

        private bool ShouldDeferLodChange(NightMapTileLod wantedLod)
        {
            if (!hasCurrentLod || wantedLod == currentLod)
            {
                hasPendingLod = false;
                return false;
            }

            if (!hasPendingLod || pendingLod != wantedLod)
            {
                pendingLod = wantedLod;
                pendingLodSince = Time.unscaledTime;
                hasPendingLod = true;
                return true;
            }

            if (Time.unscaledTime - pendingLodSince < LodStabilitySeconds)
            {
                return true;
            }

            hasPendingLod = false;
            return false;
        }

        private void ClearRequiredView()
        {
            hasPendingLod = false;
            if (requiredKeys.Count > 0 || HasPendingCurrentGeneration())
            {
                BeginDecodeGeneration(true);
            }

            requiredKeys.Clear();
            requiredOrder.Clear();
            bool coverageChanged = false;
            foreach (ResidentTile tile in residentTiles.Values)
            {
                if (tile.Renderer.enabled)
                {
                    tile.Renderer.enabled = false;
                    coverageChanged = true;
                }
            }

            if (coverageChanged || VisibleTileCount != 0)
            {
                RebuildCoverage();
            }
        }

        private bool HasPendingCurrentGeneration()
        {
            for (int index = 0; index < pendingTiles.Count; index++)
            {
                if (pendingTiles[index].Generation == generation)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetViewInfo(out ViewInfo view)
        {
            view = default(ViewInfo);
            if (globe == null)
            {
                globe = GetComponent<GlobeRenderer>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (globe == null || targetCamera == null)
            {
                return false;
            }

            Vector3 center = globe.transform.TransformPoint(Vector3.zero);
            float radius = globe.RadiusUnits;
            List<ViewHit> hits = new List<ViewHit>(
                ViewSampleGridSize * ViewSampleGridSize);
            int anchorIndex = -1;
            float anchorViewportDistance = float.MaxValue;
            for (int sampleY = 0; sampleY < ViewSampleGridSize; sampleY++)
            {
                float viewportY = sampleY / (float)(ViewSampleGridSize - 1);
                for (int sampleX = 0; sampleX < ViewSampleGridSize; sampleX++)
                {
                    float viewportX =
                        sampleX / (float)(ViewSampleGridSize - 1);
                    Ray ray = targetCamera.ViewportPointToRay(
                        new Vector3(viewportX, viewportY, 0f));
                    Vector3 hitUnit;
                    if (!TryGetFarIntersectionUnit(
                        ray,
                        center,
                        radius,
                        out hitUnit))
                    {
                        continue;
                    }

                    ViewHit hit = new ViewHit(
                        new Vector2(viewportX, viewportY),
                        hitUnit);
                    hits.Add(hit);
                    float viewportDistance =
                        (viewportX - 0.5f) * (viewportX - 0.5f) +
                        (viewportY - 0.5f) * (viewportY - 0.5f);
                    if (viewportDistance < anchorViewportDistance)
                    {
                        anchorViewportDistance = viewportDistance;
                        anchorIndex = hits.Count - 1;
                    }
                }
            }

            if (anchorIndex < 0)
            {
                return false;
            }

            Vector3 centerUnit = hits[anchorIndex].Unit;
            float angularRadius = 0.5f;
            for (int index = 0; index < hits.Count; index++)
            {
                angularRadius = Mathf.Max(
                    angularRadius,
                    AngularDistanceDegrees(centerUnit, hits[index].Unit));
            }

            int pixelWidth = Mathf.Max(1, targetCamera.pixelWidth);
            int pixelHeight = Mathf.Max(1, targetCamera.pixelHeight);
            float pixelsPerDegree = 0f;
            for (int leftIndex = 0; leftIndex < hits.Count; leftIndex++)
            {
                ViewHit left = hits[leftIndex];
                for (int rightIndex = leftIndex + 1;
                    rightIndex < hits.Count;
                    rightIndex++)
                {
                    ViewHit right = hits[rightIndex];
                    float viewportDeltaX = Mathf.Abs(
                        right.Viewport.x - left.Viewport.x);
                    float viewportDeltaY = Mathf.Abs(
                        right.Viewport.y - left.Viewport.y);
                    bool horizontalPair =
                        viewportDeltaX > 0.0001f &&
                        viewportDeltaY < 0.0001f;
                    bool verticalPair =
                        viewportDeltaY > 0.0001f &&
                        viewportDeltaX < 0.0001f;
                    if (!horizontalPair && !verticalPair)
                    {
                        continue;
                    }

                    float angularDistance =
                        AngularDistanceDegrees(left.Unit, right.Unit);
                    if (angularDistance < 0.0001f)
                    {
                        continue;
                    }

                    float screenPixels = horizontalPair
                        ? viewportDeltaX * pixelWidth
                        : viewportDeltaY * pixelHeight;
                    pixelsPerDegree = Mathf.Max(
                        pixelsPerDegree,
                        screenPixels / angularDistance);
                }
            }

            if (pixelsPerDegree <= 0f)
            {
                pixelsPerDegree =
                    Mathf.Min(pixelWidth, pixelHeight) /
                    Mathf.Max(1f, angularRadius * 2f);
            }

            view = new ViewInfo
            {
                CenterUnit = centerUnit,
                AngularRadiusDegrees = angularRadius,
                PixelsPerDegree = pixelsPerDegree
            };
            return true;
        }

        private static bool TryGetFarIntersectionUnit(
            Ray ray,
            Vector3 center,
            float radius,
            out Vector3 unit)
        {
            float nearDistance;
            float farDistance;
            if (!EarthMath.RaySphereIntersections(
                ray,
                center,
                radius,
                out nearDistance,
                out farDistance))
            {
                unit = Vector3.zero;
                return false;
            }

            Vector3 offset = ray.GetPoint(farDistance) - center;
            if (offset.sqrMagnitude < 0.000001f)
            {
                unit = Vector3.zero;
                return false;
            }

            unit = offset.normalized;
            return true;
        }

        private NightMapTileLod SelectLod(float screenPixelsPerDegree)
        {
            if (!hasCurrentLod)
            {
                if (screenPixelsPerDegree > 132f)
                {
                    return NightMapTileLod.Sample1;
                }

                return screenPixelsPerDegree > 66f
                    ? NightMapTileLod.Sample2
                    : NightMapTileLod.Sample4;
            }

            switch (currentLod)
            {
                case NightMapTileLod.Sample4:
                    return screenPixelsPerDegree > 72f
                        ? NightMapTileLod.Sample2
                        : NightMapTileLod.Sample4;
                case NightMapTileLod.Sample2:
                    if (screenPixelsPerDegree < 52f)
                    {
                        return NightMapTileLod.Sample4;
                    }

                    return screenPixelsPerDegree > 138f
                        ? NightMapTileLod.Sample1
                        : NightMapTileLod.Sample2;
                case NightMapTileLod.Sample1:
                    return screenPixelsPerDegree < 108f
                        ? NightMapTileLod.Sample2
                        : NightMapTileLod.Sample1;
                default:
                    return NightMapTileLod.Sample4;
            }
        }

        private static NightMapTileLod CoarserLod(NightMapTileLod lod)
        {
            return lod == NightMapTileLod.Sample1
                ? NightMapTileLod.Sample2
                : NightMapTileLod.Sample4;
        }

        private static int PixelsPerDegree(NightMapTileLod lod)
        {
            return NightMapTileLayout.PixelsPerDegree / (int)lod;
        }

        private static List<TileCandidate> BuildCandidates(
            NightMapTileLod lod,
            ViewInfo view)
        {
            NightMapLodInfo info = NightMapTileLayout.GetLodInfo(lod);
            float latitudeSpan = 180f / info.Rows;
            float longitudeSpan = 360f / info.Columns;
            float halfDiagonal = 0.5f * Mathf.Sqrt(
                latitudeSpan * latitudeSpan +
                longitudeSpan * longitudeSpan);
            float distanceLimit = view.AngularRadiusDegrees +
                halfDiagonal + PrefetchDegrees;
            List<TileCandidate> candidates = new List<TileCandidate>();

            for (int row = 0; row < info.Rows; row++)
            {
                for (int column = 0; column < info.Columns; column++)
                {
                    NightMapTileKey key = new NightMapTileKey(lod, column, row);
                    NightMapGeoBounds bounds =
                        NightMapTileLayout.GetGeographicBounds(key);
                    Vector3 tileCenter = EarthMath.GeoToUnitVector(
                        new GeoCoordinate(
                            (float)bounds.CenterLatitude,
                            (float)bounds.CenterLongitude));
                    float distance =
                        AngularDistanceDegrees(view.CenterUnit, tileCenter);
                    if (distance <= distanceLimit)
                    {
                        candidates.Add(new TileCandidate(key, distance));
                    }
                }
            }

            candidates.Sort(delegate (TileCandidate left, TileCandidate right)
            {
                int distanceComparison = left.Distance.CompareTo(right.Distance);
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                int rowComparison = left.Key.Row.CompareTo(right.Key.Row);
                return rowComparison != 0
                    ? rowComparison
                    : left.Key.Column.CompareTo(right.Key.Column);
            });
            return candidates;
        }

        private static float AngularDistanceDegrees(Vector3 a, Vector3 b)
        {
            return Mathf.Acos(
                Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f)) *
                Mathf.Rad2Deg;
        }

        private void ScheduleRequiredTiles()
        {
            if (decodeCancellation == null)
            {
                return;
            }

            for (int index = 0;
                index < requiredOrder.Count &&
                pendingTiles.Count < MaxPendingDecodes;
                index++)
            {
                NightMapTileKey key = requiredOrder[index];
                if (residentTiles.ContainsKey(key) ||
                    !CanRetryNow(key) ||
                    IsPending(key))
                {
                    continue;
                }

                int decoderLod = ToDecoderLod(key.Lod);
                Task<AndroidNightMapRegionDecoder.DecodedTile> task =
                    AndroidNightMapRegionDecoder.DecodeTileAsync(
                        decoderLod,
                        key.Column,
                        key.Row,
                        decodeCancellation.Token);
                pendingTiles.Add(new PendingTile(key, generation, task));
            }
        }

        private bool IsPending(NightMapTileKey key)
        {
            for (int index = 0; index < pendingTiles.Count; index++)
            {
                if (pendingTiles[index].Key == key)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ToDecoderLod(NightMapTileLod lod)
        {
            switch (lod)
            {
                case NightMapTileLod.Sample4:
                    return 0;
                case NightMapTileLod.Sample2:
                    return 1;
                case NightMapTileLod.Sample1:
                    return 2;
                default:
                    throw new ArgumentOutOfRangeException("lod");
            }
        }

        private void ProcessOneCompletedDecode()
        {
            for (int index = 0; index < pendingTiles.Count; index++)
            {
                PendingTile pending = pendingTiles[index];
                if (!pending.Task.IsCompleted)
                {
                    continue;
                }

                pendingTiles.RemoveAt(index);
                try
                {
                    AndroidNightMapRegionDecoder.DecodedTile decoded =
                        pending.Task.GetAwaiter().GetResult();
                    if (!destroying &&
                        nightEnabled &&
                        pending.Generation == generation &&
                        pending.Key.Lod == currentLod)
                    {
                        tileFailures.Remove(pending.Key);
                        UploadTile(pending.Key, decoded);
                    }
                }
                catch (OperationCanceledException)
                {
                    // A view/LOD change intentionally discards stale work.
                }
                catch (Exception exception)
                {
                    if (!destroying && pending.Generation == generation)
                    {
                        TileFailure previous;
                        int failureCount = tileFailures.TryGetValue(
                            pending.Key,
                            out previous)
                            ? previous.Count + 1
                            : 1;
                        float retryDelay = Mathf.Min(
                            30f,
                            Mathf.Pow(2f, Mathf.Min(failureCount, 5)));
                        tileFailures[pending.Key] = new TileFailure(
                            failureCount,
                            Time.unscaledTime + retryDelay);
                        Status = "3600x1800 fallback active for a failed tile";
                        Debug.LogWarning(
                            "GlassGlobe full-resolution night decode failed for " +
                            pending.Key + "; retrying in " +
                            retryDelay.ToString("0") + " seconds. " + exception);
                    }
                }

                // At most one 1082x1082 texture upload is allowed per frame.
                return;
            }
        }

        private bool CanRetryNow(NightMapTileKey key)
        {
            TileFailure failure;
            return !tileFailures.TryGetValue(key, out failure) ||
                Time.unscaledTime >= failure.NextRetryTime;
        }

        private void UploadTile(
            NightMapTileKey key,
            AndroidNightMapRegionDecoder.DecodedTile decoded)
        {
            if (residentTiles.ContainsKey(key))
            {
                return;
            }

            EnsureRoomForTile();
            if (residentTiles.Count >= residentTileLimit)
            {
                return;
            }

            Texture2D texture = new Texture2D(
                decoded.Width,
                decoded.Height,
                TextureFormat.RGB24,
                false,
                false);
            texture.name = "NASA Black Marble " + key;
            texture.hideFlags = HideFlags.DontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 2;
            texture.SetPixelData(
                decoded.Payload,
                0,
                decoded.PixelDataOffset);
            texture.Apply(false, true);

            Mesh mesh = BuildTileMesh(key);
            GameObject tileObject = new GameObject(
                "Full-Resolution Night " + key);
            tileObject.transform.SetParent(globe.transform, false);
            tileObject.transform.localPosition = Vector3.zero;
            tileObject.transform.localRotation = Quaternion.identity;
            tileObject.transform.localScale = Vector3.one;

            MeshFilter meshFilter = tileObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = tileObject.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = tileMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            if (tileProperties == null)
            {
                tileProperties = new MaterialPropertyBlock();
            }

            tileProperties.Clear();
            tileProperties.SetTexture(TileTextureId, texture);
            meshRenderer.SetPropertyBlock(tileProperties);
            bool visible = requiredKeys.Contains(key);
            meshRenderer.enabled = visible;

            residentTiles.Add(
                key,
                new ResidentTile(
                    key,
                    texture,
                    mesh,
                    tileObject,
                    meshRenderer,
                    Time.frameCount));
            if (visible)
            {
                RebuildCoverage();
            }

            UpdateStatus();
            if (!loggedFirstTileForLod)
            {
                loggedFirstTileForLod = true;
                Debug.Log(
                    "GlassGlobe full-resolution night decoded " + key +
                    " at 1082x1082 (1080 core plus gutter). " +
                    VisibleTileCount + "/" + requiredKeys.Count +
                    " visible tiles are resident.");
            }
        }

        private Mesh BuildTileMesh(NightMapTileKey key)
        {
            NightMapGeoBounds bounds =
                NightMapTileLayout.GetGeographicBounds(key);
            int longitudeSegments = Mathf.Max(
                2,
                Mathf.CeilToInt(
                    (float)(bounds.EastLongitude - bounds.WestLongitude) /
                    1.5f));
            int latitudeSegments = Mathf.Max(
                2,
                Mathf.CeilToInt(
                    (float)(bounds.NorthLatitude - bounds.SouthLatitude) /
                    1.5f));
            int stride = longitudeSegments + 1;
            int vertexCount = stride * (latitudeSegments + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles =
                new int[longitudeSegments * latitudeSegments * 6];

            float textureSize = AndroidNightMapRegionDecoder.OutputSize;
            float coreSize = AndroidNightMapRegionDecoder.OutputInteriorSize;
            int vertexIndex = 0;
            for (int latitudeIndex = 0;
                latitudeIndex <= latitudeSegments;
                latitudeIndex++)
            {
                float localV = latitudeIndex / (float)latitudeSegments;
                float latitude = (float)(
                    bounds.SouthLatitude +
                    (bounds.NorthLatitude - bounds.SouthLatitude) * localV);
                for (int longitudeIndex = 0;
                    longitudeIndex <= longitudeSegments;
                    longitudeIndex++)
                {
                    float localU = longitudeIndex / (float)longitudeSegments;
                    float longitude = (float)(
                        bounds.WestLongitude +
                        (bounds.EastLongitude - bounds.WestLongitude) * localU);
                    Vector3 unit = EarthMath.GeoToUnitVector(
                        new GeoCoordinate(latitude, longitude));
                    vertices[vertexIndex] = unit * globe.RadiusUnits;
                    normals[vertexIndex] = unit;
                    uvs[vertexIndex] = new Vector2(
                        (1f + localU * coreSize) / textureSize,
                        (1f + localV * coreSize) / textureSize);
                    vertexIndex++;
                }
            }

            int triangleIndex = 0;
            for (int latitudeIndex = 0;
                latitudeIndex < latitudeSegments;
                latitudeIndex++)
            {
                for (int longitudeIndex = 0;
                    longitudeIndex < longitudeSegments;
                    longitudeIndex++)
                {
                    int current = latitudeIndex * stride + longitudeIndex;
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
            mesh.name = "Full-Resolution Night Mesh " + key;
            mesh.hideFlags = HideFlags.DontSave;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void EnsureRoomForTile()
        {
            while (residentTiles.Count >= residentTileLimit)
            {
                ResidentTile oldest = null;
                foreach (ResidentTile candidate in residentTiles.Values)
                {
                    if (requiredKeys.Contains(candidate.Key))
                    {
                        continue;
                    }

                    if (oldest == null ||
                        candidate.LastUsedFrame < oldest.LastUsedFrame)
                    {
                        oldest = candidate;
                    }
                }

                if (oldest == null)
                {
                    return;
                }

                RemoveResidentTile(oldest.Key);
            }
        }

        private void EvictUnneededTiles()
        {
            EnsureRoomForTile();
        }

        private void ConfigureResidentLimit()
        {
            if (residentLimitConfigured)
            {
                return;
            }

            residentLimitConfigured = true;
            int systemMemoryMegabytes = SystemInfo.systemMemorySize;
            if (systemMemoryMegabytes > 0 && systemMemoryMegabytes <= 4096)
            {
                residentTileLimit = LowMemoryResidentTiles;
            }
            else if (systemMemoryMegabytes > 0 &&
                systemMemoryMegabytes <= 6144)
            {
                residentTileLimit = 20;
            }
            else
            {
                residentTileLimit = MaxResidentTiles;
            }

            Debug.Log(
                "GlassGlobe full-resolution night cache limit=" +
                residentTileLimit + " tiles for " +
                systemMemoryMegabytes + " MB system memory.");
        }

        private void HandleLowMemory()
        {
            if (destroying)
            {
                return;
            }

            residentLimitConfigured = true;
            residentTileLimit = Mathf.Min(
                residentTileLimit,
                LowMemoryResidentTiles);
            BeginDecodeGeneration(
                nightEnabled && AndroidNightMapRegionDecoder.IsSupported);

            if (requiredOrder.Count > residentTileLimit)
            {
                requiredOrder.RemoveRange(
                    residentTileLimit,
                    requiredOrder.Count - residentTileLimit);
            }

            requiredKeys.Clear();
            for (int index = 0; index < requiredOrder.Count; index++)
            {
                requiredKeys.Add(requiredOrder[index]);
            }

            foreach (ResidentTile tile in residentTiles.Values)
            {
                tile.Renderer.enabled = requiredKeys.Contains(tile.Key);
            }

            while (residentTiles.Count > residentTileLimit)
            {
                ResidentTile oldest = null;
                foreach (ResidentTile candidate in residentTiles.Values)
                {
                    if (requiredKeys.Contains(candidate.Key))
                    {
                        continue;
                    }

                    if (oldest == null ||
                        candidate.LastUsedFrame < oldest.LastUsedFrame)
                    {
                        oldest = candidate;
                    }
                }

                if (oldest == null)
                {
                    break;
                }

                RemoveResidentTile(oldest.Key);
            }

            RebuildCoverage();
            nextViewRefreshTime = 0f;
            UpdateStatus();
            Debug.LogWarning(
                "GlassGlobe received Android low-memory pressure; " +
                "night cache reduced to " + residentTileLimit + " tiles.");
        }

        private void RemoveResidentTile(NightMapTileKey key)
        {
            ResidentTile tile;
            if (!residentTiles.TryGetValue(key, out tile))
            {
                return;
            }

            residentTiles.Remove(key);
            DestroyRuntimeObject(tile.Texture);
            DestroyRuntimeObject(tile.Mesh);
            DestroyRuntimeObject(tile.GameObject);
        }

        private void ClearResidentTiles()
        {
            foreach (ResidentTile tile in residentTiles.Values)
            {
                DestroyRuntimeObject(tile.Texture);
                DestroyRuntimeObject(tile.Mesh);
                DestroyRuntimeObject(tile.GameObject);
            }

            residentTiles.Clear();
            requiredKeys.Clear();
            requiredOrder.Clear();
            tileFailures.Clear();
            VisibleTileCount = 0;
            ClearCoverage();
        }

        private void EnsureCoverageTexture()
        {
            if (coverageTexture != null)
            {
                return;
            }

            coverageTexture = new Texture2D(
                NightMapTileLayout.CoverageColumns,
                NightMapTileLayout.CoverageRows,
                TextureFormat.RGBA32,
                false,
                true);
            coverageTexture.name = "Full-Resolution Night Coverage";
            coverageTexture.hideFlags = HideFlags.DontSave;
            coverageTexture.filterMode = FilterMode.Point;
            coverageTexture.wrapModeU = TextureWrapMode.Repeat;
            coverageTexture.wrapModeV = TextureWrapMode.Clamp;
            coveragePixels = new Color32[
                NightMapTileLayout.CoverageColumns *
                NightMapTileLayout.CoverageRows];
            ClearCoverage();
        }

        private void BindCoverageTexture()
        {
            if (baseMaterial != null &&
                coverageTexture != null &&
                baseMaterial.HasProperty(CoverageTextureId))
            {
                baseMaterial.SetTexture(CoverageTextureId, coverageTexture);
            }
        }

        private void ClearCoverage()
        {
            if (coverageTexture == null || coveragePixels == null)
            {
                return;
            }

            Array.Clear(coveragePixels, 0, coveragePixels.Length);
            coverageTexture.SetPixels32(coveragePixels);
            coverageTexture.Apply(false, false);
            VisibleTileCount = 0;
        }

        private void RebuildCoverage()
        {
            if (coverageTexture == null || coveragePixels == null)
            {
                return;
            }

            Array.Clear(coveragePixels, 0, coveragePixels.Length);
            int visibleCount = 0;
            foreach (ResidentTile tile in residentTiles.Values)
            {
                if (!tile.Renderer.enabled)
                {
                    continue;
                }

                visibleCount++;
                NightMapIntRect cells =
                    NightMapTileLayout.GetCoverageCellBounds(tile.Key);
                for (int logicalRow = cells.Y;
                    logicalRow < cells.YMax;
                    logicalRow++)
                {
                    int textureRow =
                        NightMapTileLayout.CoverageRows - 1 - logicalRow;
                    for (int column = cells.X;
                        column < cells.XMax;
                        column++)
                    {
                        int pixelIndex =
                            textureRow * NightMapTileLayout.CoverageColumns +
                            column;
                        coveragePixels[pixelIndex] = Color.white;
                    }
                }
            }

            coverageTexture.SetPixels32(coveragePixels);
            coverageTexture.Apply(false, false);
            VisibleTileCount = visibleCount;
        }

        private void EnsureTileMaterial()
        {
            if (tileMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("GlassGlobe/Earth at Night Tile");
            if (shader == null)
            {
                return;
            }

            tileMaterial = new Material(shader);
            tileMaterial.name = "Full-Resolution Earth at Night Tiles";
            tileMaterial.hideFlags = HideFlags.DontSave;
            tileMaterial.renderQueue = 3009;
        }

        private void SynchronizeTileMaterial()
        {
            if (tileMaterial == null)
            {
                return;
            }

            tileMaterial.SetFloat(NightOpacityId, nightOpacity);
            if (baseMaterial == null)
            {
                return;
            }

            if (baseMaterial.HasProperty(RimColorId))
            {
                tileMaterial.SetColor(
                    RimColorId,
                    baseMaterial.GetColor(RimColorId));
            }

            if (baseMaterial.HasProperty(RimIntensityId))
            {
                tileMaterial.SetFloat(
                    RimIntensityId,
                    baseMaterial.GetFloat(RimIntensityId));
            }

            if (baseMaterial.HasProperty(RimPowerId))
            {
                tileMaterial.SetFloat(
                    RimPowerId,
                    baseMaterial.GetFloat(RimPowerId));
            }
        }

        private void BeginDecodeGeneration(bool createReplacement)
        {
            generation++;
            if (decodeCancellation != null)
            {
                decodeCancellation.Cancel();
                decodeCancellation.Dispose();
                decodeCancellation = null;
            }

            tileFailures.Clear();
            if (createReplacement && lifetimeCancellation != null)
            {
                decodeCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        lifetimeCancellation.Token);
            }
        }

        private void UpdateStatus()
        {
            Status = string.Format(
                "Full resolution {0}: {1}/{2} visible tiles, {3} cached",
                currentLod,
                VisibleTileCount,
                requiredKeys.Count,
                residentTiles.Count);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private sealed class ResidentTile
        {
            public ResidentTile(
                NightMapTileKey key,
                Texture2D texture,
                Mesh mesh,
                GameObject gameObject,
                MeshRenderer renderer,
                int lastUsedFrame)
            {
                Key = key;
                Texture = texture;
                Mesh = mesh;
                GameObject = gameObject;
                Renderer = renderer;
                LastUsedFrame = lastUsedFrame;
            }

            public NightMapTileKey Key;
            public Texture2D Texture;
            public Mesh Mesh;
            public GameObject GameObject;
            public MeshRenderer Renderer;
            public int LastUsedFrame;
        }

        private struct PendingTile
        {
            public PendingTile(
                NightMapTileKey key,
                int generation,
                Task<AndroidNightMapRegionDecoder.DecodedTile> task)
            {
                Key = key;
                Generation = generation;
                Task = task;
            }

            public NightMapTileKey Key;
            public int Generation;
            public Task<AndroidNightMapRegionDecoder.DecodedTile> Task;
        }

        private struct ViewInfo
        {
            public Vector3 CenterUnit;
            public float AngularRadiusDegrees;
            public float PixelsPerDegree;
        }

        private struct ViewHit
        {
            public ViewHit(Vector2 viewport, Vector3 unit)
            {
                Viewport = viewport;
                Unit = unit;
            }

            public Vector2 Viewport;
            public Vector3 Unit;
        }

        private struct TileFailure
        {
            public TileFailure(int count, float nextRetryTime)
            {
                Count = count;
                NextRetryTime = nextRetryTime;
            }

            public int Count;
            public float NextRetryTime;
        }

        private struct TileCandidate
        {
            public TileCandidate(NightMapTileKey key, float distance)
            {
                Key = key;
                Distance = distance;
            }

            public NightMapTileKey Key;
            public float Distance;
        }
    }
}

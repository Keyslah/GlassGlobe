using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlassGlobe
{
    [ExecuteAlways]
    public sealed class CountryBorderRenderer : MonoBehaviour
    {
        [Serializable]
        public sealed class GeoOutline
        {
            public string name;
            public string region;
            public bool isCountry = true;
            public bool closed = true;
            public Color color = Color.white;
            public float lineWidth = 0.035f;
            public GeoCoordinate labelCoordinate;
            public List<GeoCoordinate> points = new List<GeoCoordinate>();
        }

        public GlobeRenderer globe;
        public Material borderMaterial;
        public float surfaceOffset = 0.07f;
        public float maxSegmentDegrees = 2f;
        public bool showCountryOutlines = true;
        public bool showContinentOutlines = true;
        public bool overrideCountryOutlineColor;
        public Color countryOutlineColor = new Color(1f, 0.92f, 0.42f, 1f);
        [Range(0.25f, 3f)]
        public float countryOutlineThickness = 1f;
        public List<GeoOutline> outlines = new List<GeoOutline>();

        public IList<GeoOutline> Outlines
        {
            get { return outlines; }
        }

        /// <summary>
        /// Per-outline data derived from the ring points: unit vectors for the
        /// spherical containment test, a bounding cap for cheap rejection, and
        /// the label direction for the nearest-region fallback.
        /// </summary>
        private sealed class OutlineCache
        {
            public Vector3[] UnitVectors;
            public Vector3 CapCenter;
            public float CapCosRadius;
            public Vector3 LabelUnit;
            public float CapRadians;
        }

        private sealed class GeneratedLine
        {
            public LineRenderer Line;
            public bool IsCountry;
            public float BaseWidth;
        }

        /// <summary>
        /// cos(6 deg). Past roughly 650 km from the nearest coastline the
        /// readout reports open ocean instead of naming a country.
        /// </summary>
        private const float NearestRegionMinDot = 0.99452f;

        private OutlineCache[] outlineCaches;
        private List<GeoOutline> cachedOutlinesSource;
        private readonly List<GeneratedLine> generatedLines = new List<GeneratedLine>();
        private readonly Dictionary<uint, Material> lineMaterialCache = new Dictionary<uint, Material>();
        private int regionCacheFrame = -1;
        private GeoCoordinate regionCacheCoordinate;
        private string regionCacheResult;

        private void Reset()
        {
            globe = FindFirstObjectByType<GlobeRenderer>();
            ResetToSampleData();
        }

        private void OnEnable()
        {
            if (outlines == null || outlines.Count == 0)
            {
                if (!LoadRealOutlines())
                {
                    ResetToSampleData();
                }
            }
        }

        public bool LoadRealOutlines()
        {
            List<GeoOutline> loaded = CountryDataLoader.LoadOutlines();
            if (loaded == null || loaded.Count == 0)
            {
                return false;
            }

            outlines = loaded;
            return true;
        }

        public void ResetToSampleData()
        {
            outlines = CreateSampleOutlines();
        }

        public void RebuildBorders()
        {
            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (globe == null)
            {
                Debug.LogError("GlassGlobe CountryBorderRenderer cannot rebuild borders because no GlobeRenderer is assigned.", this);
                return;
            }

            if (outlines == null || outlines.Count == 0)
            {
                ResetToSampleData();
            }

            ClearGeneratedBorders();

            if (borderMaterial == null)
            {
                borderMaterial = CreateDefaultBorderMaterial(Color.white);
            }

            foreach (GeoOutline outline in outlines)
            {
                if (outline == null || outline.points == null || outline.points.Count < 2)
                {
                    continue;
                }

                if ((outline.isCountry && !showCountryOutlines) ||
                    (!outline.isCountry && !showContinentOutlines))
                {
                    continue;
                }

                List<Vector3> positions = BuildOutlinePositions(outline);
                if (positions.Count < 2)
                {
                    continue;
                }

                GameObject borderObject = new GameObject("Border - " + outline.name);
                borderObject.transform.SetParent(transform, false);

                LineRenderer lineRenderer = borderObject.AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = true;
                lineRenderer.positionCount = positions.Count;
                lineRenderer.SetPositions(positions.ToArray());
                float thickness = outline.isCountry ? countryOutlineThickness : 1f;
                lineRenderer.widthMultiplier = ThicknessToWidth(outline.lineWidth, thickness);
                // Thickness 0 means the country lines are meant to be gone, not
                // hairline thin.
                lineRenderer.enabled = thickness > 0f;
                lineRenderer.numCornerVertices = 3;
                lineRenderer.numCapVertices = outline.closed ? 0 : 3;
                lineRenderer.loop = false;
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lineRenderer.receiveShadows = false;
                Color lineColor = outline.isCountry && overrideCountryOutlineColor
                    ? countryOutlineColor
                    : outline.color;
                // One shared material per color: restyling swaps or edits the
                // shared instance instead of leaking a material per outline.
                lineRenderer.sharedMaterial = GetSharedLineMaterial(lineColor);

                generatedLines.Add(new GeneratedLine
                {
                    Line = lineRenderer,
                    IsCountry = outline.isCountry,
                    BaseWidth = outline.lineWidth
                });
            }
        }

        public void SetCountryOutlineColor(Color color)
        {
            countryOutlineColor = color;
            overrideCountryOutlineColor = true;
            if (!HasLineTracking())
            {
                RebuildBorders();
                return;
            }

            Material material = GetSharedLineMaterial(color);
            for (int index = 0; index < generatedLines.Count; index++)
            {
                GeneratedLine entry = generatedLines[index];
                if (entry.IsCountry)
                {
                    entry.Line.sharedMaterial = material;
                }
            }
        }

        public void SetCountryOutlineThickness(float thickness)
        {
            countryOutlineThickness = Mathf.Clamp(thickness, 0f, 3f);
            if (!HasLineTracking())
            {
                RebuildBorders();
                return;
            }

            for (int index = 0; index < generatedLines.Count; index++)
            {
                GeneratedLine entry = generatedLines[index];
                if (entry.IsCountry)
                {
                    entry.Line.widthMultiplier =
                        ThicknessToWidth(entry.BaseWidth, countryOutlineThickness);
                    entry.Line.enabled = countryOutlineThickness > 0f;
                }
            }
        }

        private static float ThicknessToWidth(float baseWidth, float thickness)
        {
            return thickness <= 0f ? 0f : Mathf.Max(0.005f, baseWidth * thickness);
        }

        public string GetRegionForCoordinate(GeoCoordinate coordinate)
        {
            if (outlines == null || outlines.Count == 0)
            {
                return "No sample data";
            }

            // The HUD banner and readout each query once per OnGUI event, so
            // memoize per frame; the far-side point barely moves between calls.
            if (regionCacheFrame == Time.frameCount &&
                regionCacheResult != null &&
                Mathf.Abs(coordinate.Latitude - regionCacheCoordinate.Latitude) < 0.005f &&
                Mathf.Abs(Mathf.DeltaAngle(coordinate.Longitude, regionCacheCoordinate.Longitude)) < 0.005f)
            {
                return regionCacheResult;
            }

            EnsureOutlineCaches();
            Vector3 pointUnit = EarthMath.GeoToUnitVector(coordinate);
            GeoOutline nearestOutline = null;
            float nearestDot = -2f;
            string result = null;

            for (int index = 0; index < outlines.Count; index++)
            {
                GeoOutline outline = outlines[index];
                OutlineCache cache = outlineCaches[index];
                if (outline == null || cache == null)
                {
                    continue;
                }

                float capDot = Vector3.Dot(pointUnit, cache.CapCenter);
                if (outline.isCountry &&
                    capDot >= cache.CapCosRadius &&
                    ContainsOnSphere(pointUnit, cache.UnitVectors))
                {
                    result = outline.name;
                    break;
                }

                // Cheap reject before touching this ring's vertices: nothing in
                // it can be nearer than its bounding cap. Without this the
                // fallback walks all ~10k vertices in the dataset every frame
                // the reticle is off-shore.
                float capGapRadians =
                    Mathf.Acos(Mathf.Clamp(capDot, -1f, 1f)) - cache.CapRadians;
                if (capGapRadians > 0f && Mathf.Cos(capGapRadians) <= nearestDot)
                {
                    continue;
                }

                // Rank the fallback by how close the ring itself comes, not by
                // the label anchor. A label sits at the country's centre, so
                // island groups lose badly to whatever large country happens to
                // be centred nearby: standing on Hawaii, the US label is in
                // Kansas 49 deg away while Fiji's is 45.8 deg away, and the
                // readout said Fiji. The rings themselves put Hawaii 0.2 deg
                // from a United States ring.
                for (int vertex = 0; vertex < cache.UnitVectors.Length; vertex++)
                {
                    float dot = Vector3.Dot(pointUnit, cache.UnitVectors[vertex]);
                    if (dot > nearestDot)
                    {
                        nearestDot = dot;
                        nearestOutline = outline;
                    }
                }
            }

            if (result == null)
            {
                if (nearestOutline == null)
                {
                    result = "Unknown";
                }
                else if (nearestDot < NearestRegionMinDot)
                {
                    // Far from every coastline: naming a country here would be
                    // a guess dressed up as a fact.
                    result = "Open ocean";
                }
                else
                {
                    result = string.Format("Nearest: {0}", nearestOutline.name);
                }
            }

            regionCacheFrame = Time.frameCount;
            regionCacheCoordinate = coordinate;
            regionCacheResult = result;
            return result;
        }

        /// <summary>
        /// Point-in-polygon on the sphere via the winding of vertex azimuths in
        /// the tangent plane at the point. Unlike a flat lat/lon test this is
        /// correct for rings that cross the antimeridian (Russia, Fiji) and
        /// rings that enclose a pole (Antarctica).
        /// </summary>
        public static bool ContainsOnSphere(GeoCoordinate point, IList<GeoCoordinate> ring)
        {
            if (ring == null || ring.Count < 3)
            {
                return false;
            }

            Vector3[] units = new Vector3[ring.Count];
            for (int index = 0; index < ring.Count; index++)
            {
                units[index] = EarthMath.GeoToUnitVector(ring[index]);
            }

            return ContainsOnSphere(EarthMath.GeoToUnitVector(point), units);
        }

        public static bool ContainsOnSphere(Vector3 pointUnit, Vector3[] ringUnitVectors)
        {
            if (ringUnitVectors == null || ringUnitVectors.Length < 3)
            {
                return false;
            }

            // A closed ring encircles both its own region and the antipodal
            // region, so the tangent-plane winding alone reports true at the
            // antipode. Country rings are far smaller than a hemisphere, so
            // reject points in the hemisphere opposite the ring centroid before
            // winding. (Ill-defined centroid, e.g. a near-global ring, skips
            // this guard and relies on winding.)
            Vector3 centroid = Vector3.zero;
            for (int index = 0; index < ringUnitVectors.Length; index++)
            {
                centroid += ringUnitVectors[index];
            }

            if (centroid.sqrMagnitude > 0.000001f &&
                Vector3.Dot(pointUnit, centroid.normalized) <= 0f)
            {
                return false;
            }

            Vector3 east = Vector3.Cross(Vector3.up, pointUnit);
            if (east.sqrMagnitude < 0.000001f)
            {
                east = Vector3.Cross(Vector3.forward, pointUnit);
            }

            east.Normalize();
            Vector3 north = Vector3.Cross(pointUnit, east).normalized;

            float totalDegrees = 0f;
            float previousAzimuth = float.NaN;
            int count = ringUnitVectors.Length;
            for (int step = 0; step <= count; step++)
            {
                Vector3 vertex = ringUnitVectors[step % count];
                float eastComponent = Vector3.Dot(vertex, east);
                float northComponent = Vector3.Dot(vertex, north);
                if (eastComponent * eastComponent + northComponent * northComponent < 1e-10f)
                {
                    // Vertex at the query point or its antipode has no azimuth.
                    continue;
                }

                float azimuth = Mathf.Atan2(eastComponent, northComponent) * Mathf.Rad2Deg;
                if (!float.IsNaN(previousAzimuth))
                {
                    totalDegrees += Mathf.DeltaAngle(previousAzimuth, azimuth);
                }

                previousAzimuth = azimuth;
            }

            // Inside: azimuths wind a full turn around the point. Outside: they
            // sum to roughly zero.
            return Mathf.Abs(totalDegrees) > 180f;
        }

        private void EnsureOutlineCaches()
        {
            if (outlineCaches != null &&
                cachedOutlinesSource == outlines &&
                outlineCaches.Length == outlines.Count)
            {
                return;
            }

            outlineCaches = new OutlineCache[outlines.Count];
            for (int index = 0; index < outlines.Count; index++)
            {
                GeoOutline outline = outlines[index];
                if (outline == null || outline.points == null || outline.points.Count < 3)
                {
                    continue;
                }

                OutlineCache cache = new OutlineCache();
                cache.UnitVectors = new Vector3[outline.points.Count];
                Vector3 sum = Vector3.zero;
                for (int pointIndex = 0; pointIndex < outline.points.Count; pointIndex++)
                {
                    Vector3 unit = EarthMath.GeoToUnitVector(outline.points[pointIndex]);
                    cache.UnitVectors[pointIndex] = unit;
                    sum += unit;
                }

                if (sum.sqrMagnitude < 0.000001f)
                {
                    // Ring wraps most of the sphere; the cap cannot reject.
                    cache.CapCenter = Vector3.up;
                    cache.CapCosRadius = -1f;
                }
                else
                {
                    cache.CapCenter = sum.normalized;
                    float minDot = 1f;
                    for (int pointIndex = 0; pointIndex < cache.UnitVectors.Length; pointIndex++)
                    {
                        float dot = Vector3.Dot(cache.CapCenter, cache.UnitVectors[pointIndex]);
                        if (dot < minDot)
                        {
                            minDot = dot;
                        }
                    }

                    // Small margin so points just outside the vertex hull still
                    // reach the exact test.
                    cache.CapCosRadius = Mathf.Max(-1f, minDot - 0.01f);
                }

                cache.LabelUnit = EarthMath.GeoToUnitVector(outline.labelCoordinate);
                cache.CapRadians = Mathf.Acos(Mathf.Clamp(cache.CapCosRadius, -1f, 1f));
                outlineCaches[index] = cache;
            }

            cachedOutlinesSource = outlines;
        }

        private bool HasLineTracking()
        {
            if (generatedLines.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < generatedLines.Count; index++)
            {
                if (generatedLines[index].Line == null)
                {
                    return false;
                }
            }

            return true;
        }

        private Material GetSharedLineMaterial(Color color)
        {
            Color32 color32 = color;
            uint key = (uint)(color32.r | (color32.g << 8) | (color32.b << 16) | (color32.a << 24));
            Material material;
            if (lineMaterialCache.TryGetValue(key, out material) && material != null)
            {
                return material;
            }

            material = CreateDefaultBorderMaterial(color);
            lineMaterialCache[key] = material;
            return material;
        }

        public static Material CreateDefaultBorderMaterial(Color color)
        {
            Shader shader = Shader.Find("GlassGlobe/Line");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = new Material(shader);
            material.name = "GlassGlobe Border Line";

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        public static List<GeoOutline> CreateSampleOutlines()
        {
            Color continentColor = new Color(1f, 0.82f, 0.22f, 0.95f);
            Color countryColor = new Color(1f, 0.92f, 0.42f, 1f);

            List<GeoOutline> sample = new List<GeoOutline>();

            sample.Add(Outline(
                "North America",
                "Continent sample",
                false,
                new GeoCoordinate(48f, -103f),
                continentColor,
                0.025f,
                new GeoCoordinate(71f, -168f),
                new GeoCoordinate(68f, -140f),
                new GeoCoordinate(72f, -95f),
                new GeoCoordinate(61f, -60f),
                new GeoCoordinate(45f, -53f),
                new GeoCoordinate(25f, -81f),
                new GeoCoordinate(9f, -83f),
                new GeoCoordinate(16f, -97f),
                new GeoCoordinate(24f, -110f),
                new GeoCoordinate(38f, -124f),
                new GeoCoordinate(56f, -135f)));

            sample.Add(Outline(
                "South America",
                "Continent sample",
                false,
                new GeoCoordinate(-18f, -60f),
                continentColor,
                0.025f,
                new GeoCoordinate(12f, -72f),
                new GeoCoordinate(8f, -58f),
                new GeoCoordinate(3f, -50f),
                new GeoCoordinate(-8f, -35f),
                new GeoCoordinate(-22f, -41f),
                new GeoCoordinate(-34f, -53f),
                new GeoCoordinate(-55f, -68f),
                new GeoCoordinate(-42f, -73f),
                new GeoCoordinate(-25f, -70f),
                new GeoCoordinate(-8f, -78f)));

            sample.Add(Outline(
                "Africa",
                "Continent sample",
                false,
                new GeoCoordinate(4f, 20f),
                continentColor,
                0.025f,
                new GeoCoordinate(37f, -10f),
                new GeoCoordinate(32f, 12f),
                new GeoCoordinate(31f, 32f),
                new GeoCoordinate(12f, 51f),
                new GeoCoordinate(-12f, 40f),
                new GeoCoordinate(-35f, 20f),
                new GeoCoordinate(-29f, 15f),
                new GeoCoordinate(-17f, 12f),
                new GeoCoordinate(5f, -5f),
                new GeoCoordinate(15f, -17f),
                new GeoCoordinate(30f, -10f)));

            sample.Add(Outline(
                "Eurasia",
                "Continent sample",
                false,
                new GeoCoordinate(48f, 62f),
                continentColor,
                0.025f,
                new GeoCoordinate(36f, -10f),
                new GeoCoordinate(58f, -10f),
                new GeoCoordinate(71f, 25f),
                new GeoCoordinate(72f, 100f),
                new GeoCoordinate(62f, 160f),
                new GeoCoordinate(45f, 145f),
                new GeoCoordinate(22f, 120f),
                new GeoCoordinate(8f, 105f),
                new GeoCoordinate(8f, 78f),
                new GeoCoordinate(25f, 58f),
                new GeoCoordinate(35f, 35f),
                new GeoCoordinate(45f, 15f)));

            sample.Add(Outline(
                "United States",
                "Country sample",
                true,
                new GeoCoordinate(39f, -98f),
                countryColor,
                0.045f,
                new GeoCoordinate(49f, -125f),
                new GeoCoordinate(49f, -67f),
                new GeoCoordinate(45f, -67f),
                new GeoCoordinate(30f, -81f),
                new GeoCoordinate(25f, -97f),
                new GeoCoordinate(32f, -117f)));

            sample.Add(Outline(
                "Canada",
                "Country sample",
                true,
                new GeoCoordinate(58f, -106f),
                countryColor,
                0.04f,
                new GeoCoordinate(60f, -140f),
                new GeoCoordinate(70f, -100f),
                new GeoCoordinate(58f, -55f),
                new GeoCoordinate(49f, -67f),
                new GeoCoordinate(49f, -125f)));

            sample.Add(Outline(
                "Mexico",
                "Country sample",
                true,
                new GeoCoordinate(23f, -102f),
                countryColor,
                0.04f,
                new GeoCoordinate(32f, -117f),
                new GeoCoordinate(31f, -107f),
                new GeoCoordinate(26f, -97f),
                new GeoCoordinate(18f, -94f),
                new GeoCoordinate(15f, -104f),
                new GeoCoordinate(23f, -110f)));

            sample.Add(Outline(
                "Brazil",
                "Country sample",
                true,
                new GeoCoordinate(-10f, -52f),
                countryColor,
                0.04f,
                new GeoCoordinate(5f, -74f),
                new GeoCoordinate(5f, -34f),
                new GeoCoordinate(-34f, -52f),
                new GeoCoordinate(-25f, -74f)));

            sample.Add(Outline(
                "United Kingdom",
                "Country sample",
                true,
                new GeoCoordinate(54f, -2f),
                countryColor,
                0.04f,
                new GeoCoordinate(58f, -6f),
                new GeoCoordinate(57f, 1f),
                new GeoCoordinate(50f, 2f),
                new GeoCoordinate(50f, -5f)));

            sample.Add(Outline(
                "France",
                "Country sample",
                true,
                new GeoCoordinate(46f, 2f),
                countryColor,
                0.04f,
                new GeoCoordinate(51f, -5f),
                new GeoCoordinate(51f, 8f),
                new GeoCoordinate(43f, 7f),
                new GeoCoordinate(42f, -1f),
                new GeoCoordinate(46f, -5f)));

            sample.Add(Outline(
                "India",
                "Country sample",
                true,
                new GeoCoordinate(22f, 79f),
                countryColor,
                0.04f,
                new GeoCoordinate(35f, 68f),
                new GeoCoordinate(28f, 88f),
                new GeoCoordinate(8f, 78f),
                new GeoCoordinate(8f, 72f),
                new GeoCoordinate(23f, 68f)));

            sample.Add(Outline(
                "China",
                "Country sample",
                true,
                new GeoCoordinate(35f, 104f),
                countryColor,
                0.04f,
                new GeoCoordinate(49f, 74f),
                new GeoCoordinate(53f, 120f),
                new GeoCoordinate(22f, 122f),
                new GeoCoordinate(18f, 100f),
                new GeoCoordinate(29f, 78f)));

            sample.Add(Outline(
                "Australia",
                "Continent sample",
                false,
                new GeoCoordinate(-25f, 133f),
                continentColor,
                0.025f,
                new GeoCoordinate(-14f, 129f),
                new GeoCoordinate(-12f, 142f),
                new GeoCoordinate(-18f, 153f),
                new GeoCoordinate(-28f, 153f),
                new GeoCoordinate(-39f, 146f),
                new GeoCoordinate(-38f, 132f),
                new GeoCoordinate(-34f, 115f),
                new GeoCoordinate(-22f, 113f),
                new GeoCoordinate(-12f, 123f)));

            sample.Add(Outline(
                "Antarctica",
                "Continent sample",
                false,
                new GeoCoordinate(-82f, 20f),
                continentColor,
                0.026f,
                new GeoCoordinate(-63f, -60f),
                new GeoCoordinate(-72f, -30f),
                new GeoCoordinate(-77f, 0f),
                new GeoCoordinate(-70f, 35f),
                new GeoCoordinate(-68f, 75f),
                new GeoCoordinate(-66f, 110f),
                new GeoCoordinate(-72f, 150f),
                new GeoCoordinate(-70f, -170f),
                new GeoCoordinate(-74f, -130f),
                new GeoCoordinate(-68f, -95f)));

            sample.Add(Outline(
                "Indonesia",
                "Country sample",
                true,
                new GeoCoordinate(-2f, 118f),
                countryColor,
                0.038f,
                new GeoCoordinate(5f, 95f),
                new GeoCoordinate(4f, 107f),
                new GeoCoordinate(1f, 124f),
                new GeoCoordinate(-3f, 141f),
                new GeoCoordinate(-10f, 130f),
                new GeoCoordinate(-9f, 107f)));

            sample.Add(Outline(
                "New Zealand",
                "Country sample",
                true,
                new GeoCoordinate(-42f, 174f),
                countryColor,
                0.038f,
                new GeoCoordinate(-34f, 166f),
                new GeoCoordinate(-36f, 179f),
                new GeoCoordinate(-47f, 178f),
                new GeoCoordinate(-47f, 166f)));

            sample.Add(Outline(
                "South Africa",
                "Country sample",
                true,
                new GeoCoordinate(-30f, 24f),
                countryColor,
                0.04f,
                new GeoCoordinate(-22f, 16f),
                new GeoCoordinate(-25f, 33f),
                new GeoCoordinate(-35f, 28f),
                new GeoCoordinate(-34f, 18f)));

            return sample;
        }

        private List<Vector3> BuildOutlinePositions(GeoOutline outline)
        {
            List<Vector3> positions = new List<Vector3>();
            float radius = globe.RadiusUnits + surfaceOffset;
            Vector3 center = globe.Center;
            int last = outline.points.Count - 1;

            for (int index = 0; index < last; index++)
            {
                EarthMath.AppendGreatCircleArc(
                    positions,
                    outline.points[index],
                    outline.points[index + 1],
                    radius,
                    center,
                    maxSegmentDegrees,
                    index == 0);
            }

            if (outline.closed)
            {
                EarthMath.AppendGreatCircleArc(
                    positions,
                    outline.points[last],
                    outline.points[0],
                    radius,
                    center,
                    maxSegmentDegrees,
                    false);
            }

            return positions;
        }

        private static GeoOutline Outline(
            string name,
            string region,
            bool isCountry,
            GeoCoordinate labelCoordinate,
            Color color,
            float lineWidth,
            params GeoCoordinate[] points)
        {
            GeoOutline outline = new GeoOutline();
            outline.name = name;
            outline.region = region;
            outline.isCountry = isCountry;
            outline.labelCoordinate = labelCoordinate;
            outline.color = color;
            outline.lineWidth = lineWidth;
            outline.closed = true;
            outline.points = new List<GeoCoordinate>(points);
            return outline;
        }

        private void ClearGeneratedBorders()
        {
            generatedLines.Clear();
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (!child.name.StartsWith("Border - ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
    }
}

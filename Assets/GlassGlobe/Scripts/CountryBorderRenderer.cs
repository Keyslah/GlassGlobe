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
        public List<GeoOutline> outlines = new List<GeoOutline>();

        public IList<GeoOutline> Outlines
        {
            get { return outlines; }
        }

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
                lineRenderer.widthMultiplier = Mathf.Max(0.005f, outline.lineWidth);
                lineRenderer.numCornerVertices = 3;
                lineRenderer.numCapVertices = outline.closed ? 0 : 3;
                lineRenderer.loop = false;
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lineRenderer.receiveShadows = false;
                lineRenderer.material = CreateDefaultBorderMaterial(outline.color);
            }
        }

        public string GetRegionForCoordinate(GeoCoordinate coordinate)
        {
            if (outlines == null || outlines.Count == 0)
            {
                return "No sample data";
            }

            GeoOutline nearestOutline = null;
            float nearestDistance = float.MaxValue;

            foreach (GeoOutline outline in outlines)
            {
                if (outline == null || outline.points == null || outline.points.Count < 3)
                {
                    continue;
                }

                if (outline.isCountry && ContainsFlatLatLon(coordinate, outline.points))
                {
                    return outline.name;
                }

                float distance = EarthMath.AngularDistanceDegrees(coordinate, outline.labelCoordinate);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestOutline = outline;
                }
            }

            if (nearestOutline == null)
            {
                return "Unknown";
            }

            return string.Format("Nearest: {0}", nearestOutline.name);
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

        private static bool ContainsFlatLatLon(GeoCoordinate point, IList<GeoCoordinate> polygon)
        {
            bool inside = false;
            float x = point.Longitude;
            float y = point.Latitude;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float xi = polygon[i].Longitude;
                float yi = polygon[i].Latitude;
                float xj = polygon[j].Longitude;
                float yj = polygon[j].Latitude;
                float denominator = yj - yi;
                if (Mathf.Abs(denominator) < 0.0001f)
                {
                    continue;
                }

                bool intersects = ((yi > y) != (yj > y)) &&
                    (x < (xj - xi) * (y - yi) / denominator + xi);

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private void ClearGeneratedBorders()
        {
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

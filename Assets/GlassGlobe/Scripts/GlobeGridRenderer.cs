using System;
using UnityEngine;

namespace GlassGlobe
{
    [ExecuteAlways]
    public sealed class GlobeGridRenderer : MonoBehaviour
    {
        public GlobeRenderer globe;
        public Material gridMaterial;
        public Color gridColor = new Color(0.15f, 0.88f, 1f, 0.95f);

        [Range(6, 24)]
        public int longitudeLineCount = 12;

        [Range(3, 13)]
        public int latitudeLineCount = 7;

        [Range(12, 96)]
        public int segmentsPerLine = 48;

        public float surfaceOffset = 0.045f;
        public float lineWidth = 0.018f;
        public bool gridVisible = true;
        [Range(0.25f, 3f)]
        public float gridThickness = 1f;

        private void OnEnable()
        {
            if (transform.childCount == 0)
            {
                RebuildGrid();
            }
        }

        public void RebuildGrid()
        {
            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (globe == null)
            {
                return;
            }

            ClearGrid();
            if (gridMaterial == null)
            {
                gridMaterial = CountryBorderRenderer.CreateDefaultBorderMaterial(gridColor);
            }

            for (int index = 0; index < longitudeLineCount; index++)
            {
                float longitude = -180f + 360f * index / longitudeLineCount;
                Vector3[] points = new Vector3[segmentsPerLine + 1];
                for (int segment = 0; segment <= segmentsPerLine; segment++)
                {
                    float latitude = Mathf.Lerp(-90f, 90f, segment / (float)segmentsPerLine);
                    points[segment] = globe.GeoToWorld(new GeoCoordinate(latitude, longitude), surfaceOffset);
                }

                CreateLine("Grid - Longitude " + longitude.ToString("0"), points, false);
            }

            for (int index = 0; index < latitudeLineCount; index++)
            {
                float latitude = Mathf.Lerp(-75f, 75f, index / (float)(latitudeLineCount - 1));
                Vector3[] points = new Vector3[segmentsPerLine];
                for (int segment = 0; segment < segmentsPerLine; segment++)
                {
                    float longitude = -180f + 360f * segment / segmentsPerLine;
                    points[segment] = globe.GeoToWorld(new GeoCoordinate(latitude, longitude), surfaceOffset);
                }

                CreateLine("Grid - Latitude " + latitude.ToString("0"), points, true);
            }
        }

        public void SetGridColor(Color color)
        {
            gridColor = color;
            if (gridMaterial == null)
            {
                gridMaterial = CountryBorderRenderer.CreateDefaultBorderMaterial(color);
            }

            if (gridMaterial.HasProperty("_Color"))
            {
                gridMaterial.SetColor("_Color", color);
            }

            if (gridMaterial.HasProperty("_BaseColor"))
            {
                gridMaterial.SetColor("_BaseColor", color);
            }

            for (int index = 0; index < transform.childCount; index++)
            {
                LineRenderer line = transform.GetChild(index).GetComponent<LineRenderer>();
                if (line != null)
                {
                    line.sharedMaterial = gridMaterial;
                }
            }
        }

        public void SetGridVisible(bool visible)
        {
            gridVisible = visible;
            for (int index = 0; index < transform.childCount; index++)
            {
                LineRenderer line = transform.GetChild(index).GetComponent<LineRenderer>();
                if (line != null)
                {
                    line.enabled = visible;
                }
            }
        }

        public void SetGridThickness(float thickness)
        {
            gridThickness = Mathf.Clamp(thickness, 0.25f, 3f);
            for (int index = 0; index < transform.childCount; index++)
            {
                LineRenderer line = transform.GetChild(index).GetComponent<LineRenderer>();
                if (line != null)
                {
                    line.widthMultiplier = lineWidth * gridThickness;
                }
            }
        }

        private void CreateLine(string lineName, Vector3[] points, bool loop)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = loop;
            line.widthMultiplier = lineWidth * gridThickness;
            line.enabled = gridVisible;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = gridMaterial;
        }

        private void ClearGrid()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (!child.name.StartsWith("Grid - ", StringComparison.Ordinal))
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

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

        private void CreateLine(string lineName, Vector3[] points, bool loop)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = loop;
            line.widthMultiplier = lineWidth;
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

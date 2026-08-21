using UnityEngine;
using UnityEngine.Rendering;

namespace GlassGlobe
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class GlobeRenderer : MonoBehaviour
    {
        [Min(0.1f)]
        public float radiusUnits = EarthMath.DefaultEarthRadiusUnits;

        public Vector3 center = Vector3.zero;

        [Range(12, 240)]
        public int longitudeSegments = 96;

        [Range(6, 120)]
        public int latitudeSegments = 48;

        public Color globeColor = new Color(0.08f, 0.27f, 0.38f, 0.18f);
        public Material globeMaterial;

        public float RadiusUnits
        {
            get { return radiusUnits; }
        }

        public Vector3 Center
        {
            get { return center; }
        }

        private void Reset()
        {
            radiusUnits = EarthMath.DefaultEarthRadiusUnits;
            longitudeSegments = 96;
            latitudeSegments = 48;
            globeColor = new Color(0.08f, 0.27f, 0.38f, 0.18f);
        }

        private void OnEnable()
        {
            RebuildGlobe();
        }

        private void OnValidate()
        {
            longitudeSegments = Mathf.Max(12, longitudeSegments);
            latitudeSegments = Mathf.Max(6, latitudeSegments);
            radiusUnits = Mathf.Max(0.1f, radiusUnits);

            if (Application.isPlaying)
            {
                RebuildGlobe();
            }
        }

        public Vector3 GeoToWorld(GeoCoordinate coordinate, float surfaceOffset = 0f)
        {
            return EarthMath.GeoToPoint(coordinate, radiusUnits + surfaceOffset, center);
        }

        public GeoCoordinate WorldToGeo(Vector3 worldPoint)
        {
            return EarthMath.PointToGeo(worldPoint, center);
        }

        public void RebuildGlobe()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            transform.position = center;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            Mesh mesh = BuildSphereMesh(radiusUnits, latitudeSegments, longitudeSegments);
            mesh.name = "GlassGlobe Generated Globe";
            meshFilter.sharedMesh = mesh;

            if (globeMaterial == null)
            {
                globeMaterial = CreateDefaultGlobeMaterial(globeColor);
            }

            ApplyColor(globeMaterial, globeColor);
            meshRenderer.sharedMaterial = globeMaterial;
        }

        public static Material CreateDefaultGlobeMaterial(Color color)
        {
            Shader shader = Shader.Find("GlassGlobe/Transparent Globe");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.name = "GlassGlobe Transparent Globe";
            ConfigureTransparentMaterial(material);
            ApplyColor(material, color);
            return material;
        }

        public static void ApplyColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        public static void ConfigureTransparentMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }
        }

        private static Mesh BuildSphereMesh(float radius, int latitudeCount, int longitudeCount)
        {
            int verticalSegments = Mathf.Max(6, latitudeCount);
            int horizontalSegments = Mathf.Max(12, longitudeCount);
            int vertexCount = (verticalSegments + 1) * (horizontalSegments + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[verticalSegments * horizontalSegments * 6];

            int vertexIndex = 0;
            for (int latitudeIndex = 0; latitudeIndex <= verticalSegments; latitudeIndex++)
            {
                float v = latitudeIndex / (float)verticalSegments;
                float latitude = Mathf.Lerp(-90f, 90f, v) * Mathf.Deg2Rad;
                float cosLatitude = Mathf.Cos(latitude);
                float sinLatitude = Mathf.Sin(latitude);

                for (int longitudeIndex = 0; longitudeIndex <= horizontalSegments; longitudeIndex++)
                {
                    float u = longitudeIndex / (float)horizontalSegments;
                    float longitude = Mathf.Lerp(-180f, 180f, u) * Mathf.Deg2Rad;
                    Vector3 unit = new Vector3(
                        cosLatitude * Mathf.Sin(longitude),
                        sinLatitude,
                        cosLatitude * Mathf.Cos(longitude)).normalized;

                    vertices[vertexIndex] = unit * radius;
                    normals[vertexIndex] = unit;
                    uvs[vertexIndex] = new Vector2(u, v);
                    vertexIndex++;
                }
            }

            int triangleIndex = 0;
            int stride = horizontalSegments + 1;
            for (int latitudeIndex = 0; latitudeIndex < verticalSegments; latitudeIndex++)
            {
                for (int longitudeIndex = 0; longitudeIndex < horizontalSegments; longitudeIndex++)
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
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

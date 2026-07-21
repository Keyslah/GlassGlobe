using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Shared procedural mesh and sprite-texture builders. The lat/lon sphere
    /// meshes here are the single source of the triangle layout used by the
    /// weather shells, art shells, and the Milky Way sphere, so their winding
    /// (and therefore which hemisphere Cull Front keeps) stays consistent.
    /// </summary>
    public static class GlassGlobeVisuals
    {
        /// <summary>
        /// Supplies the vertex position and UV for a lat/lon sphere vertex.
        /// </summary>
        public delegate void SphereVertexFunc(
            float latitudeDegrees,
            float longitudeDegrees,
            float u,
            float v,
            out Vector3 position,
            out Vector2 uv);

        /// <summary>
        /// Far-side shell mesh in the globe's mirrored embedding: vertices come
        /// from EarthMath.GeoToPoint, so the equirectangular texture reads true
        /// through the left-handed camera and the far-side interior is the back
        /// face that Cull Front keeps.
        /// </summary>
        public static Mesh BuildGeoShellMesh(
            string name,
            float radius,
            int longitudeSegments,
            int latitudeSegments,
            float maxLatitudeDegrees,
            bool withNormals)
        {
            return BuildLatLonSphereMesh(
                name,
                longitudeSegments,
                latitudeSegments,
                maxLatitudeDegrees,
                withNormals,
                delegate (float latitude, float longitude, float u, float v, out Vector3 position, out Vector2 uv)
                {
                    position = EarthMath.GeoToPoint(
                        new GeoCoordinate(latitude, longitude), radius, Vector3.zero);
                    uv = new Vector2(u, (latitude + 90f) / 180f);
                });
        }

        public static Mesh BuildLatLonSphereMesh(
            string name,
            int longitudeSegments,
            int latitudeSegments,
            float maxLatitudeDegrees,
            bool withNormals,
            SphereVertexFunc vertexFunc)
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
                float v = latIndex / (float)latCount;
                float latitude = Mathf.Lerp(-maxLatitudeDegrees, maxLatitudeDegrees, v);
                for (int lonIndex = 0; lonIndex <= lonCount; lonIndex++)
                {
                    float u = lonIndex / (float)lonCount;
                    float longitude = Mathf.Lerp(-180f, 180f, u);
                    Vector3 position;
                    Vector2 uv;
                    vertexFunc(latitude, longitude, u, v, out position, out uv);
                    vertices[vertexIndex] = position;
                    uvs[vertexIndex] = uv;
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
            if (withNormals)
            {
                // Normal-mapped, lit shells (the stylized ocean) need a smooth
                // per-vertex basis; flat shells skip this to stay lean.
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Unit quad centered on the origin in the XY plane, used for billboard
        /// sprites and the camera feed background.
        /// </summary>
        public static Mesh BuildQuadMesh(string name)
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

        /// <summary>
        /// Soft radial glow texture: solid core out to coreRadius, then a
        /// power-falloff halo to the edge.
        /// </summary>
        public static Texture2D BuildRadialTexture(int size, Color core, Color glow, float coreRadius, float falloffPower)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
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

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}

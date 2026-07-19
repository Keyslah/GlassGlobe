using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Bakes an equirectangular land/sea mask (white land on black sea) from the
    /// bundled Natural Earth country rings, so the Earth Art shader gets its
    /// coastlines from the exact same data the border lines use. NE 110m rings
    /// are pre-split at the dateline, so a plain scanline fill in lon/lat space
    /// is valid for every ring including Antarctica.
    /// </summary>
    public static class GlassGlobeLandMaskBaker
    {
        private const string OutputPath = "Assets/GlassGlobe/Resources/GlassGlobeLandMask.png";
        private const string VersionPath = "Assets/GlassGlobe/Resources/GlassGlobeLandMaskVersion.txt";
        private const string BakeVersion = "2";
        private const int Width = 2048;
        private const int Height = 1024;

        [MenuItem("GlassGlobe/Bake Land Mask")]
        public static void BakeMenu()
        {
            Bake();
        }

        public static bool EnsureBaked()
        {
            if (File.Exists(OutputPath) && File.Exists(VersionPath) &&
                File.ReadAllText(VersionPath).Trim() == BakeVersion)
            {
                return true;
            }

            return Bake();
        }

        private static bool Bake()
        {
            List<CountryBorderRenderer.GeoOutline> outlines = CountryDataLoader.LoadOutlines();
            if (outlines == null || outlines.Count == 0)
            {
                Debug.LogError("GlassGlobeLandMaskBaker: country data not found in Resources; cannot bake land mask.");
                return false;
            }

            // The loader wraps longitude -180 to +180, which tears rings that
            // were split along the dateline (Antarctica, Fiji, Russia). Unwrap
            // each ring for longitude continuity before scanline filling, and
            // fill columns modulo the map width so out-of-range crossings wrap
            // instead of smearing across the map.
            List<float[]> ringLats = new List<float[]>();
            List<float[]> ringLons = new List<float[]>();
            foreach (CountryBorderRenderer.GeoOutline outline in outlines)
            {
                List<GeoCoordinate> points = outline.points;
                if (points == null || points.Count < 3)
                {
                    continue;
                }

                float[] lats = new float[points.Count];
                float[] lons = new float[points.Count];
                lats[0] = points[0].Latitude;
                lons[0] = points[0].Longitude;
                for (int index = 1; index < points.Count; index++)
                {
                    lats[index] = points[index].Latitude;
                    float longitude = points[index].Longitude;
                    float previous = lons[index - 1];
                    while (longitude - previous > 180f)
                    {
                        longitude -= 360f;
                    }

                    while (previous - longitude > 180f)
                    {
                        longitude += 360f;
                    }

                    lons[index] = longitude;
                }

                ringLats.Add(lats);
                ringLons.Add(lons);
            }

            bool[] landRow = new bool[Width];
            byte[] mask = new byte[Width * Height];
            List<float> crossings = new List<float>(32);

            for (int row = 0; row < Height; row++)
            {
                float latitude = -90f + (row + 0.5f) * 180f / Height;
                System.Array.Clear(landRow, 0, Width);
                bool rowHasLand = false;

                for (int ringIndex = 0; ringIndex < ringLats.Count; ringIndex++)
                {
                    float[] lats = ringLats[ringIndex];
                    float[] lons = ringLons[ringIndex];
                    int count = lats.Length;

                    crossings.Clear();
                    for (int index = 0; index < count; index++)
                    {
                        int nextIndex = (index + 1) % count;
                        float latA = lats[index];
                        float latB = lats[nextIndex];
                        if ((latA > latitude) == (latB > latitude))
                        {
                            continue;
                        }

                        float lonA = lons[index];
                        float lonB = lons[nextIndex];
                        if (Mathf.Abs(lonB - lonA) > 180f)
                        {
                            // Closing edge of a ring whose unwrap drifted a full
                            // turn; skipping it keeps parity sane.
                            continue;
                        }

                        float t = (latitude - latA) / (latB - latA);
                        crossings.Add(lonA + t * (lonB - lonA));
                    }

                    if (crossings.Count < 2)
                    {
                        continue;
                    }

                    crossings.Sort();
                    for (int pair = 0; pair + 1 < crossings.Count; pair += 2)
                    {
                        int startColumn = Mathf.RoundToInt((crossings[pair] + 180f) / 360f * Width);
                        int endColumn = Mathf.RoundToInt((crossings[pair + 1] + 180f) / 360f * Width);
                        for (int column = startColumn; column < endColumn; column++)
                        {
                            landRow[((column % Width) + Width) % Width] = true;
                            rowHasLand = true;
                        }
                    }
                }

                if (!rowHasLand)
                {
                    continue;
                }

                int rowBase = row * Width;
                for (int column = 0; column < Width; column++)
                {
                    if (landRow[column])
                    {
                        mask[rowBase + column] = 255;
                    }
                }
            }

            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[Width * Height];
            for (int index = 0; index < pixels.Length; index++)
            {
                byte value = mask[index];
                pixels[index] = new Color32(value, value, value, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);

            File.WriteAllBytes(OutputPath, texture.EncodeToPNG());
            File.WriteAllText(VersionPath, BakeVersion);
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(OutputPath);
            AssetDatabase.ImportAsset(VersionPath);

            TextureImporter importer = AssetImporter.GetAtPath(OutputPath) as TextureImporter;
            if (importer != null)
            {
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }

            Debug.Log("GlassGlobeLandMaskBaker: baked land mask to " + OutputPath);
            return true;
        }
    }
}

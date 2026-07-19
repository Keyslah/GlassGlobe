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
        private const int Width = 2048;
        private const int Height = 1024;

        [MenuItem("GlassGlobe/Bake Land Mask")]
        public static void BakeMenu()
        {
            Bake();
        }

        public static bool EnsureBaked()
        {
            if (File.Exists(OutputPath))
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

            bool[] landRow = new bool[Width];
            byte[] mask = new byte[Width * Height];
            List<float> crossings = new List<float>(32);

            for (int row = 0; row < Height; row++)
            {
                float latitude = -90f + (row + 0.5f) * 180f / Height;
                System.Array.Clear(landRow, 0, Width);
                bool rowHasLand = false;

                foreach (CountryBorderRenderer.GeoOutline outline in outlines)
                {
                    List<GeoCoordinate> points = outline.points;
                    if (points == null || points.Count < 3)
                    {
                        continue;
                    }

                    crossings.Clear();
                    for (int index = 0; index < points.Count; index++)
                    {
                        GeoCoordinate a = points[index];
                        GeoCoordinate b = points[(index + 1) % points.Count];
                        float latA = a.Latitude;
                        float latB = b.Latitude;
                        if ((latA > latitude) == (latB > latitude))
                        {
                            continue;
                        }

                        float t = (latitude - latA) / (latB - latA);
                        crossings.Add(a.Longitude + t * (b.Longitude - a.Longitude));
                    }

                    if (crossings.Count < 2)
                    {
                        continue;
                    }

                    crossings.Sort();
                    for (int pair = 0; pair + 1 < crossings.Count; pair += 2)
                    {
                        int startColumn = Mathf.Clamp(
                            Mathf.RoundToInt((crossings[pair] + 180f) / 360f * Width), 0, Width);
                        int endColumn = Mathf.Clamp(
                            Mathf.RoundToInt((crossings[pair + 1] + 180f) / 360f * Width), 0, Width);
                        for (int column = startColumn; column < endColumn; column++)
                        {
                            landRow[column] = true;
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
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(OutputPath);

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

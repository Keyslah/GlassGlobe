using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GlassGlobe;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Release guardrails for the Earth at Night feature. These checks target the
/// exact failure modes of the retired additive version: wrong source imagery,
/// power-of-two distortion, missing shader properties, near-hemisphere culling,
/// and a missing mirrored-longitude correction.
/// </summary>
public static class GlassGlobeEarthAtNightChecks
{
    private const string FallbackTexturePath =
        "Assets/GlassGlobe/Resources/GlassGlobeNightLights.jpg";
    private const string SummerTexturePath =
        "Assets/GlassGlobe/Resources/GlassGlobeBlueMarbleSummer.jpg";
    private const string HistoricalTexturePath =
        "Assets/GlassGlobe/Resources/GlassGlobeHistoricalMelish1817.jpg";
    private const string BaseShaderPath =
        "Assets/GlassGlobe/Shaders/GlassGlobeTransparentGlobe.shader";
    private const string NightTileShaderPath =
        "Assets/GlassGlobe/Shaders/GlassGlobeNightTile.shader";
    private const string FullNightSourceRoot =
        "Assets/StreamingAssets/GlassGlobeNightFullRes";
    private const string JavaDecoderPath =
        "Assets/GlassGlobe/AndroidPlugins/NightMapRegionDecoder.java";
    private const string CSharpDecoderPath =
        "Assets/GlassGlobe/Scripts/AndroidNightMapRegionDecoder.cs";
    private const string NightTileSurfacePath =
        "Assets/GlassGlobe/Scripts/NightTileSurface.cs";
    private const string EarthStyleControllerPath =
        "Assets/GlassGlobe/Scripts/EarthStyleController.cs";
    private const string ExpectedFallbackSha256 =
        "d87de751a264e4f8ff69c68de5dab9606daee87a6f15ae743c93200743bd7ec1";
    private const string ExpectedSummerSha256 =
        "D225F1F35A6448A4D1D8F6DE6E48F3433E470085B70A35800E64F384F269A7B0";
    private const string ExpectedHistoricalSha256 =
        "c34a82d233b192bf0ccff21654e609ccbbe64790dca5250d2843937f08b208fe";

    private static readonly NightSourceManifestEntry[] FullNightSourceManifest =
    {
        new NightSourceManifestEntry(
            "A1", 30825698L,
            "f9e9a0fc1c6227bfb174639e5ff70d4f081810b4c72834a1ccdb9687150436f2"),
        new NightSourceManifestEntry(
            "A2", 7830664L,
            "ed5c047b606caca73a829c912c8388db07ecfa6670baf1358fd9e538ef773428"),
        new NightSourceManifestEntry(
            "B1", 29472401L,
            "d0ef6a0b15032d277235d59c336e82c6fbe9693cfa26aaa72fec92962574d11e"),
        new NightSourceManifestEntry(
            "B2", 21337236L,
            "f56f36e2b76993b34dcf83163d8c8a23255251031597337833c9eb2400c5aa9b"),
        new NightSourceManifestEntry(
            "C1", 59097456L,
            "446fcc72755ebc3625f96d39f2bcb247016038b74c90c655a65991e6de71b3b5"),
        new NightSourceManifestEntry(
            "C2", 13553256L,
            "f52cb69c0eadb2efa0868472cca59ac5461df5c4a0ce27846bff1e60cbafbe44"),
        new NightSourceManifestEntry(
            "D1", 39122860L,
            "449fdd6b2f6eec087f56b5a0dcee5ab3213ea0ade75ec746f38cea76f3acd442"),
        new NightSourceManifestEntry(
            "D2", 14400257L,
            "295a0b58aefc581ee3bcc8e513ce4e1a475057ab3de9dc94ccf5c0fe39be9972")
    };

    private static readonly string[] DecoderSourceOrder =
        { "A1", "B1", "C1", "D1", "A2", "B2", "C2", "D2" };

    [MenuItem("GlassGlobe/Run Earth at Night Checks")]
    public static void RunMenu()
    {
        if (!Run())
        {
            throw new Exception(
                "GlassGlobe Earth at Night checks failed. See Console errors above.");
        }
    }

    public static void RunBatch()
    {
        if (!Run())
        {
            EditorApplication.Exit(1);
        }
    }

    public static bool Run()
    {
        int failures = 0;

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(FallbackTexturePath);
        Check(ref failures, texture != null, "Earth at Night texture is missing.");
        if (texture != null)
        {
            Check(
                ref failures,
                texture.width == 3600 && texture.height == 1800,
                "Earth at Night must import at 3600x1800, got " +
                texture.width + "x" + texture.height + ".");
        }

        TextureImporter importer =
            AssetImporter.GetAtPath(FallbackTexturePath) as TextureImporter;
        Check(ref failures, importer != null, "Earth at Night TextureImporter is missing.");
        if (importer != null)
        {
            Check(ref failures, importer.sRGBTexture, "Earth at Night must import as sRGB.");
            Check(ref failures, importer.mipmapEnabled, "Earth at Night mipmaps must be enabled.");
            Check(ref failures, !importer.isReadable, "Earth at Night must not keep a CPU-readable copy.");
            Check(
                ref failures,
                importer.npotScale == TextureImporterNPOTScale.None,
                "Earth at Night must preserve its native non-power-of-two dimensions.");
            Check(
                ref failures,
                importer.wrapModeU == TextureWrapMode.Repeat &&
                importer.wrapModeV == TextureWrapMode.Clamp,
                "Earth at Night must repeat longitude and clamp latitude.");

            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            Check(
                ref failures,
                android.overridden &&
                android.maxTextureSize == 4096 &&
                android.format == TextureImporterFormat.ASTC_6x6,
                "Earth at Night Android import must use 4096 max size and ASTC 6x6.");
        }

        string absoluteTexturePath = Path.Combine(
            Directory.GetCurrentDirectory(), FallbackTexturePath);
        Check(ref failures, File.Exists(absoluteTexturePath), "Earth at Night source file is missing.");
        if (File.Exists(absoluteTexturePath))
        {
            string actualSha256 = ComputeSha256(absoluteTexturePath);
            Check(
                ref failures,
                string.Equals(
                    actualSha256,
                    ExpectedFallbackSha256,
                    StringComparison.OrdinalIgnoreCase),
                "Earth at Night source does not match NASA BlackMarble_2016_01deg.jpg. " +
                "Expected " + ExpectedFallbackSha256 + ", got " + actualSha256 + ".");
        }

        CheckFullNightSourceManifest(ref failures);
        CheckSummerTexture(ref failures);
        CheckHistoricalMelishTexture(ref failures);

        Shader shader = Shader.Find("GlassGlobe/Transparent Globe");
        Check(ref failures, shader != null, "Transparent Globe shader is missing.");
        if (shader != null)
        {
            Material material = new Material(shader);
            Check(ref failures, material.HasProperty("_NightTex"), "Shader is missing _NightTex.");
            Check(
                ref failures,
                material.HasProperty("_NightCoverageTex"),
                "Shader is missing _NightCoverageTex.");
            Check(ref failures, material.HasProperty("_NightOpacity"), "Shader is missing _NightOpacity.");
            UnityEngine.Object.DestroyImmediate(material);
        }

        string absoluteShaderPath = Path.Combine(
            Directory.GetCurrentDirectory(), BaseShaderPath);
        Check(ref failures, File.Exists(absoluteShaderPath), "Transparent Globe shader source is missing.");
        if (File.Exists(absoluteShaderPath))
        {
            string shaderSource = File.ReadAllText(absoluteShaderPath);
            Check(
                ref failures,
                shaderSource.Contains("Cull Back"),
                "Earth at Night must render the far hemisphere with Cull Back.");
            Check(
                ref failures,
                shaderSource.Contains(
                    "output.uv = float2(1.0 - input.uv.x, input.uv.y);"),
                "Earth at Night is missing the mirrored-longitude U correction.");
            Check(
                ref failures,
                shaderSource.Contains(
                    "color.rgb = lerp(color.rgb, nightMap, nightOpacity);"),
                "Earth at Night must use a real surface blend, not additive lighting.");
            Check(
                ref failures,
                shaderSource.Contains(
                    "saturate(tex2D(_NightCoverageTex, input.uv).r)"),
                "The base globe shader must sample full-resolution night coverage.");
            Check(
                ref failures,
                shaderSource.Contains(
                    "saturate(_NightOpacity) * (1.0 - fullResolutionCoverage)"),
                "The base globe shader must suppress fallback night light under full-resolution tiles.");
        }

        CheckNightTileShader(ref failures);
        CheckNightMapTileLayout(ref failures);
        CheckDecoderContracts(ref failures);
        CheckNightTileRuntime(ref failures);
        CheckViewportSurfaceCycle(ref failures);

        if (failures == 0)
        {
            Debug.Log(
                "GlassGlobe Earth at Night checks passed: the official 86400x43200 " +
                "NASA tiled night map, Android region decoder, full-resolution coverage " +
                "suppression, 3600x1800 fallback, 16384x8192 Summer ASTC import, " +
                "8192x4096 Melish 1817 ASTC import, geographic seams, and viewport " +
                "surface cycle are all intact.");
            return true;
        }

        Debug.LogError(
            "GlassGlobe Earth at Night checks failed with " + failures + " issue(s).");
        return false;
    }

    private static void CheckFullNightSourceManifest(ref int failures)
    {
        string absoluteRoot = Path.Combine(
            Directory.GetCurrentDirectory(), FullNightSourceRoot);
        Check(
            ref failures,
            Directory.Exists(absoluteRoot),
            "Full-resolution Earth at Night source directory is missing.");
        if (!Directory.Exists(absoluteRoot))
        {
            return;
        }

        string[] packagedSources = Directory.GetFiles(
            absoluteRoot,
            "BlackMarble_2016_*.resource",
            SearchOption.TopDirectoryOnly);
        Check(
            ref failures,
            packagedSources.Length == FullNightSourceManifest.Length,
            "Full-resolution Earth at Night must package exactly eight NASA .resource files; got " +
            packagedSources.Length + ".");

        HashSet<string> exactFilenames = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0; index < packagedSources.Length; index++)
        {
            exactFilenames.Add(Path.GetFileName(packagedSources[index]));
        }

        for (int index = 0; index < FullNightSourceManifest.Length; index++)
        {
            NightSourceManifestEntry expected = FullNightSourceManifest[index];
            string filename = "BlackMarble_2016_" + expected.Id + ".resource";
            Check(
                ref failures,
                exactFilenames.Contains(filename),
                "Full-resolution NASA source filename must match Android case exactly: " +
                filename + ".");
            string absolutePath = Path.Combine(absoluteRoot, filename);
            Check(
                ref failures,
                File.Exists(absolutePath),
                "Full-resolution NASA source " + filename + " is missing.");
            if (!File.Exists(absolutePath))
            {
                continue;
            }

            long actualByteLength = new FileInfo(absolutePath).Length;
            Check(
                ref failures,
                actualByteLength == expected.ByteLength,
                filename + " must be exactly " + expected.ByteLength +
                " bytes; got " + actualByteLength + ".");

            string actualSha256 = ComputeSha256(absolutePath);
            Check(
                ref failures,
                string.Equals(
                    actualSha256,
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase),
                filename + " SHA-256 mismatch. Expected " + expected.Sha256 +
                ", got " + actualSha256 + ".");
        }
    }

    private static void CheckSummerTexture(ref int failures)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SummerTexturePath);
        if (texture == null || texture.width != 16384 || texture.height != 8192)
        {
            // A previous -nographics import can leave a failed 8192-limited
            // artifact in the cache. Retry under the real graphics device so
            // this check proves Unity's actual 16384 texture path.
            AssetDatabase.ImportAsset(
                SummerTexturePath,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(SummerTexturePath);
        }

        Check(ref failures, texture != null, "Summer globe texture is missing.");
        if (texture != null)
        {
            Check(
                ref failures,
                texture.width == 16384 && texture.height == 8192,
                "Summer globe must import at 16384x8192, got " +
                texture.width + "x" + texture.height + ".");
        }

        TextureImporter importer =
            AssetImporter.GetAtPath(SummerTexturePath) as TextureImporter;
        Check(ref failures, importer != null, "Summer globe TextureImporter is missing.");
        if (importer != null)
        {
            Check(
                ref failures,
                importer.maxTextureSize == 16384,
                "Summer globe default max texture size must be 16384.");

            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            Check(
                ref failures,
                android.overridden &&
                android.maxTextureSize == 16384 &&
                android.format == TextureImporterFormat.ASTC_6x6,
                "Summer globe Android import must use 16384 max size and ASTC 6x6.");
        }

        string absoluteTexturePath = Path.Combine(
            Directory.GetCurrentDirectory(), SummerTexturePath);
        Check(ref failures, File.Exists(absoluteTexturePath), "Summer globe source file is missing.");
        if (File.Exists(absoluteTexturePath))
        {
            string actualSha256 = ComputeSha256(absoluteTexturePath);
            Check(
                ref failures,
                string.Equals(
                    actualSha256,
                    ExpectedSummerSha256,
                    StringComparison.OrdinalIgnoreCase),
                "Summer globe source SHA-256 mismatch. Expected " +
                ExpectedSummerSha256 + ", got " + actualSha256 + ".");
        }
    }

    private static void CheckHistoricalMelishTexture(ref int failures)
    {
        Texture2D texture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(HistoricalTexturePath);
        Check(ref failures, texture != null, "Melish 1817 globe texture is missing.");
        if (texture != null)
        {
            Check(
                ref failures,
                texture.width == 8192 && texture.height == 4096,
                "Melish 1817 globe texture must import at 8192x4096, got " +
                texture.width + "x" + texture.height + ".");
        }

        TextureImporter importer =
            AssetImporter.GetAtPath(HistoricalTexturePath) as TextureImporter;
        Check(
            ref failures,
            importer != null,
            "Melish 1817 globe TextureImporter is missing.");
        if (importer != null)
        {
            Check(
                ref failures,
                importer.maxTextureSize == 8192,
                "Melish 1817 globe default max texture size must be 8192.");
            Check(
                ref failures,
                importer.sRGBTexture,
                "Melish 1817 globe texture must import as sRGB.");
            Check(
                ref failures,
                importer.mipmapEnabled,
                "Melish 1817 globe texture mipmaps must be enabled.");
            Check(
                ref failures,
                !importer.isReadable,
                "Melish 1817 globe texture must not keep a CPU-readable copy.");
            Check(
                ref failures,
                importer.npotScale == TextureImporterNPOTScale.None,
                "Melish 1817 globe texture must preserve its native dimensions.");
            Check(
                ref failures,
                importer.wrapModeU == TextureWrapMode.Repeat &&
                importer.wrapModeV == TextureWrapMode.Clamp,
                "Melish 1817 globe texture must repeat longitude and clamp latitude.");

            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            Check(
                ref failures,
                android.overridden &&
                android.maxTextureSize == 8192 &&
                android.format == TextureImporterFormat.ASTC_6x6,
                "Melish 1817 Android import must use 8192 max size and ASTC 6x6.");
        }

        string absoluteTexturePath = Path.Combine(
            Directory.GetCurrentDirectory(), HistoricalTexturePath);
        Check(
            ref failures,
            File.Exists(absoluteTexturePath),
            "Melish 1817 globe source file is missing.");
        if (File.Exists(absoluteTexturePath))
        {
            string actualSha256 = ComputeSha256(absoluteTexturePath);
            Check(
                ref failures,
                string.Equals(
                    actualSha256,
                    ExpectedHistoricalSha256,
                    StringComparison.OrdinalIgnoreCase),
                "Melish 1817 globe source SHA-256 mismatch. Expected " +
                ExpectedHistoricalSha256 + ", got " + actualSha256 + ".");
        }
    }

    private static void CheckNightTileShader(ref int failures)
    {
        Shader shader = Shader.Find("GlassGlobe/Earth at Night Tile");
        Check(ref failures, shader != null, "Earth at Night tile shader is missing.");
        if (shader != null)
        {
            Material material = new Material(shader);
            Check(
                ref failures,
                material.HasProperty("_NightTileTex"),
                "Earth at Night tile shader is missing _NightTileTex.");
            Check(
                ref failures,
                material.HasProperty("_NightOpacity"),
                "Earth at Night tile shader is missing _NightOpacity.");
            Check(
                ref failures,
                material.HasProperty("_RimColor") &&
                material.HasProperty("_RimIntensity") &&
                material.HasProperty("_RimPower"),
                "Earth at Night tile shader is missing its rim-light properties.");
            UnityEngine.Object.DestroyImmediate(material);
        }

        string absoluteShaderPath = Path.Combine(
            Directory.GetCurrentDirectory(), NightTileShaderPath);
        Check(
            ref failures,
            File.Exists(absoluteShaderPath),
            "Earth at Night tile shader source is missing.");
        if (!File.Exists(absoluteShaderPath))
        {
            return;
        }

        string shaderSource = File.ReadAllText(absoluteShaderPath);
        CheckSourceContains(
            ref failures,
            shaderSource,
            "Earth at Night tile shader",
            "Blend SrcAlpha OneMinusSrcAlpha",
            "ZWrite Off",
            "Cull Front",
            "output.uv = input.uv;",
            "fixed3 color = tex2D(_NightTileTex, input.uv).rgb;",
            "return fixed4(color, saturate(_NightOpacity));");
    }

    private static void CheckNightMapTileLayout(ref int failures)
    {
        string layoutError;
        bool layoutIsValid = NightMapTileLayout.ValidateContract(out layoutError);
        Check(
            ref failures,
            layoutIsValid,
            "Full-resolution night-map layout contract failed: " +
            (layoutError ?? "unknown error") + ".");

        CheckCoordinate(
            ref failures, "San Francisco", 37.7749, -122.4194,
            12, 11, "A1", 12960, 11880);
        CheckCoordinate(
            ref failures, "New York City", 40.7128, -74.0060,
            23, 10, "B1", 3240, 10800);
        CheckCoordinate(
            ref failures, "London", 51.5074, -0.1278,
            39, 8, "B1", 20520, 8640);
        CheckCoordinate(
            ref failures, "Cairo", 30.0444, 31.2357,
            46, 13, "C1", 6480, 14040);
        CheckCoordinate(
            ref failures, "Delhi", 28.6139, 77.2090,
            57, 13, "C1", 18360, 14040);
        CheckCoordinate(
            ref failures, "Tokyo", 35.6762, 139.6503,
            71, 12, "D1", 11880, 12960);
        CheckCoordinate(
            ref failures, "Sao Paulo", -23.5505, -46.6333,
            29, 25, "B2", 9720, 5400);
        CheckCoordinate(
            ref failures, "Sydney", -33.8688, 151.2093,
            73, 27, "D2", 14040, 7560);

        CheckCoordinate(
            ref failures, "antimeridian west", 0.0, -180.0,
            0, 20, "A2", 0, 0);
        CheckCoordinate(
            ref failures, "antimeridian east wrap", 0.0, 180.0,
            0, 20, "A2", 0, 0);
        CheckCoordinate(
            ref failures, "antimeridian east edge", 0.0, 179.999999,
            79, 20, "D2", 20520, 0);
        CheckCoordinate(
            ref failures, "A/B source seam west", 0.0, -90.000001,
            19, 20, "A2", 20520, 0);
        CheckCoordinate(
            ref failures, "A/B source seam east", 0.0, -90.0,
            20, 20, "B2", 0, 0);
        CheckCoordinate(
            ref failures, "B/C source seam west", 0.0, -0.000001,
            39, 20, "B2", 20520, 0);
        CheckCoordinate(
            ref failures, "B/C source seam east", 0.0, 0.0,
            40, 20, "C2", 0, 0);
        CheckCoordinate(
            ref failures, "C/D source seam west", 0.0, 89.999999,
            59, 20, "C2", 20520, 0);
        CheckCoordinate(
            ref failures, "C/D source seam east", 0.0, 90.0,
            60, 20, "D2", 0, 0);
        CheckCoordinate(
            ref failures, "equator north edge", 0.000001, 0.0,
            40, 19, "C1", 0, 20520);
        CheckCoordinate(
            ref failures, "north pole", 90.0, 0.0,
            40, 0, "C1", 0, 0);
        CheckCoordinate(
            ref failures, "south pole", -90.0, 0.0,
            40, 39, "C2", 0, 20520);
    }

    private static void CheckCoordinate(
        ref int failures,
        string label,
        double latitude,
        double longitude,
        int expectedColumn,
        int expectedRow,
        string expectedSourceId,
        int expectedCropX,
        int expectedCropY)
    {
        try
        {
            NightMapTileKey key = NightMapTileLayout.GetTileForCoordinate(
                NightMapTileLod.Sample1,
                latitude,
                longitude);
            Check(
                ref failures,
                key == new NightMapTileKey(
                    NightMapTileLod.Sample1,
                    expectedColumn,
                    expectedRow),
                label + " must map to native tile c" + expectedColumn +
                "/r" + expectedRow + ", got " + key + ".");

            NightMapNasaSourceCrop crop =
                NightMapTileLayout.GetNasaSourceCrop(key);
            Check(
                ref failures,
                crop.SourceId == expectedSourceId &&
                crop.CropPixels.X == expectedCropX &&
                crop.CropPixels.Y == expectedCropY &&
                crop.CropPixels.Width == 1080 &&
                crop.CropPixels.Height == 1080 &&
                crop.SampleStep == 1 &&
                crop.OutputWidth == 1080 &&
                crop.OutputHeight == 1080,
                label + " must use " + expectedSourceId + " crop (" +
                expectedCropX + "," + expectedCropY + ",1080,1080) at sample 1.");

            NightMapCoverageCell coverage = NightMapTileLayout.GetCoverageCell(
                latitude,
                longitude);
            NightMapTileKey coverageTile =
                NightMapTileLayout.GetTileForCoverageCell(
                    NightMapTileLod.Sample1,
                    coverage);
            Check(
                ref failures,
                coverage.Column == expectedColumn &&
                coverage.Row == expectedRow &&
                coverageTile == key,
                label + " coverage cell must round-trip to its native tile.");
        }
        catch (Exception exception)
        {
            Check(
                ref failures,
                false,
                label + " coordinate contract threw " +
                exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void CheckDecoderContracts(ref int failures)
    {
        string javaSource = ReadRequiredSource(
            ref failures,
            JavaDecoderPath,
            "Android night-map region decoder");
        if (javaSource != null)
        {
            CheckSourceContains(
                ref failures,
                javaSource,
                "Android night-map region decoder",
                "package com.glassglobe.night;",
                "public final class NightMapRegionDecoder",
                "BitmapRegionDecoder",
                "SOURCE_TILE_SIZE = 21600",
                "SOURCE_COLUMNS = 4",
                "SOURCE_ROWS = 2",
                "LOD_COUNT = 3",
                "OUTPUT_INTERIOR_SIZE = 1080",
                "OUTPUT_GUTTER_SIZE = 1",
                "HEADER_SIZE = 16",
                "public static int[] probeSourceDimensions()",
                "public static byte[] decodeTile(int lod, int tileX, int tileY)",
                "public static void shutdown()",
                "options.inSampleSize = sampleSize;",
                "payload[0] = 'G';",
                "payload[1] = 'G';",
                "payload[2] = 'N';",
                "payload[3] = 'T';",
                "PAYLOAD_FLAG_BOTTOM_UP | PAYLOAD_FLAG_SRGB",
                "\"bin/Data/StreamingAssets/GlassGlobeNightFullRes/\"",
                "\"GlassGlobeNightFullRes/\"");

            int previousSourcePosition = -1;
            for (int index = 0; index < DecoderSourceOrder.Length; index++)
            {
                string filename = "BlackMarble_2016_" +
                    DecoderSourceOrder[index] + ".resource";
                int sourcePosition = javaSource.IndexOf(
                    "\"" + filename + "\"",
                    StringComparison.Ordinal);
                Check(
                    ref failures,
                    sourcePosition > previousSourcePosition,
                    "Android night-map decoder source order must be " +
                    "A1,B1,C1,D1,A2,B2,C2,D2; misplaced " + filename + ".");
                previousSourcePosition = sourcePosition;
            }
        }

        string cSharpSource = ReadRequiredSource(
            ref failures,
            CSharpDecoderPath,
            "C# Android night-map decoder bridge");
        if (cSharpSource != null)
        {
            CheckSourceContains(
                ref failures,
                cSharpSource,
                "C# Android night-map decoder bridge",
                "public static class AndroidNightMapRegionDecoder",
                "public const int SourceTileSize = 21600;",
                "public const int SourceColumns = 4;",
                "public const int SourceRows = 2;",
                "public const int LodCount = 3;",
                "public const int OutputInteriorSize = 1080;",
                "public const int OutputGutterSize = 1;",
                "public const int PayloadHeaderSize = 16;",
                "public const int PayloadChannelCount = 3;",
                "com.glassglobe.night.NightMapRegionDecoder",
                "public static Task<DecodedTile> DecodeTileAsync(",
                "public static Task<int[]> ProbeSourceDimensionsAsync(",
                "public static Task ShutdownAsync()",
                "CallStatic<sbyte[]>",
                "Buffer.BlockCopy(",
                "\"decodeTile\"",
                "\"probeSourceDimensions\"",
                "CallStatic(\"shutdown\")",
                "payload[0] != (byte)'G'",
                "payload[1] != (byte)'G'",
                "payload[2] != (byte)'N'",
                "payload[3] != (byte)'T'",
                "PayloadFlagBottomUp | PayloadFlagSrgb");
        }

        Check(
            ref failures,
            AndroidNightMapRegionDecoder.GetColumnCount(0) == 20 &&
            AndroidNightMapRegionDecoder.GetRowCount(0) == 10 &&
            AndroidNightMapRegionDecoder.GetSampleSize(0) == 4 &&
            AndroidNightMapRegionDecoder.GetColumnCount(1) == 40 &&
            AndroidNightMapRegionDecoder.GetRowCount(1) == 20 &&
            AndroidNightMapRegionDecoder.GetSampleSize(1) == 2 &&
            AndroidNightMapRegionDecoder.GetColumnCount(2) == 80 &&
            AndroidNightMapRegionDecoder.GetRowCount(2) == 40 &&
            AndroidNightMapRegionDecoder.GetSampleSize(2) == 1,
            "Android decoder LOD 0/1/2 must map to 20x10 sample-4, " +
            "40x20 sample-2, and 80x40 sample-1.");
        Check(
            ref failures,
            AndroidNightMapRegionDecoder.OutputInteriorSize == 1080 &&
            AndroidNightMapRegionDecoder.OutputGutterSize == 1 &&
            AndroidNightMapRegionDecoder.OutputSize == 1082 &&
            AndroidNightMapRegionDecoder.PayloadHeaderSize == 16 &&
            AndroidNightMapRegionDecoder.PayloadChannelCount == 3,
            "Android decoder payload must be 16-byte GGNT plus 1082x1082 RGB24.");
    }

    private static void CheckNightTileRuntime(ref int failures)
    {
        Check(
            ref failures,
            typeof(NightTileSurface).GetMethod("EnsureInstance") != null &&
            typeof(NightTileSurface).GetMethod("SetNightState") != null,
            "NightTileSurface must expose its EarthStyleController runtime API.");

        string surfaceSource = ReadRequiredSource(
            ref failures,
            NightTileSurfacePath,
            "full-resolution night tile surface");
        if (surfaceSource != null)
        {
            string normalized = surfaceSource.Replace("\r\n", "\n");
            CheckSourceContains(
                ref failures,
                normalized,
                "full-resolution night tile surface",
                "case NightMapTileLod.Sample4:\n                    return 0;",
                "case NightMapTileLod.Sample2:\n                    return 1;",
                "case NightMapTileLod.Sample1:\n                    return 2;",
                "decoded.PixelDataOffset",
                "TextureFormat.RGB24",
                "TextureFormat.RGBA32",
                "FilterMode.Point",
                "coverageTexture.wrapModeU = TextureWrapMode.Repeat;",
                "coverageTexture.wrapModeV = TextureWrapMode.Clamp;",
                "NightMapTileLayout.CoverageRows - 1 - logicalRow",
                "(1f + localU * coreSize) / textureSize",
                "(1f + localV * coreSize) / textureSize",
                "baseMaterial.SetTexture(CoverageTextureId, coverageTexture);",
                "ClearCoverage();",
                "Application.lowMemory += HandleLowMemory;",
                "private const float LodStabilitySeconds = 0.75f;",
                "Time.unscaledTime - pendingLodSince < LodStabilitySeconds",
                "sourceProbeStarted = false;",
                "nextSourceProbeTime = Time.unscaledTime + retryDelay;",
                "private const int MaxPendingDecodes = 2;");
        }

        string styleSource = ReadRequiredSource(
            ref failures,
            EarthStyleControllerPath,
            "Earth at Night style controller");
        if (styleSource != null)
        {
            CheckSourceContains(
                ref failures,
                styleSource,
                "Earth at Night style controller",
                "material.HasProperty(NightCoverageTextureId)",
                "surface.SetNightState(material, false, 0f);",
                "surface.SetNightState(material, true, opacity);");
        }

        Check(
            ref failures,
            GlassGlobeSettingsState.DefaultBlueMarbleSeason ==
                BlueMarbleSeason.Summer &&
            !GlassGlobeSettingsState.DefaultNightLightsEnabled,
            "A fresh install must start on Summer with Earth at Night disabled.");
    }

    private static string ReadRequiredSource(
        ref int failures,
        string projectPath,
        string label)
    {
        string absolutePath = Path.Combine(
            Directory.GetCurrentDirectory(), projectPath);
        Check(ref failures, File.Exists(absolutePath), label + " source is missing.");
        return File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null;
    }

    private static void CheckSourceContains(
        ref int failures,
        string source,
        string label,
        params string[] requiredText)
    {
        for (int index = 0; index < requiredText.Length; index++)
        {
            string expected = requiredText[index];
            Check(
                ref failures,
                source.Contains(expected),
                label + " is missing required contract text: " + expected);
        }
    }

    private static void CheckViewportSurfaceCycle(ref int failures)
    {
        CheckCycleStep(
            ref failures,
            false,
            false,
            BlueMarbleSeason.Summer,
            false,
            false,
            BlueMarbleSeason.Fall);
        CheckCycleStep(
            ref failures,
            false,
            false,
            BlueMarbleSeason.Fall,
            false,
            false,
            BlueMarbleSeason.Winter);
        CheckCycleStep(
            ref failures,
            false,
            false,
            BlueMarbleSeason.Winter,
            false,
            false,
            BlueMarbleSeason.Spring);
        CheckCycleStep(
            ref failures,
            false,
            false,
            BlueMarbleSeason.Spring,
            true,
            false,
            BlueMarbleSeason.Spring);
        CheckCycleStep(
            ref failures,
            true,
            false,
            BlueMarbleSeason.Spring,
            false,
            true,
            BlueMarbleSeason.Spring);
        CheckCycleStep(
            ref failures,
            false,
            true,
            BlueMarbleSeason.Spring,
            false,
            false,
            BlueMarbleSeason.Summer);

        Check(
            ref failures,
            !GlassGlobeSettingsState.DefaultHistoricalMapEnabled,
            "Melish 1817 mode must default off.");

        CheckCycleLabel(
            ref failures, false, false, BlueMarbleSeason.Summer, "Summer");
        CheckCycleLabel(
            ref failures, false, false, BlueMarbleSeason.Fall, "Fall");
        CheckCycleLabel(
            ref failures, false, false, BlueMarbleSeason.Winter, "Winter");
        CheckCycleLabel(
            ref failures, false, false, BlueMarbleSeason.Spring, "Spring");
        CheckCycleLabel(
            ref failures, true, false, BlueMarbleSeason.Spring, "Earth at Night");
        CheckCycleLabel(
            ref failures, false, true, BlueMarbleSeason.Spring, "Melish 1817");

        // Invalid seasons normalize through the existing Winter-to-Spring step.
        CheckCycleStep(
            ref failures,
            false,
            false,
            (BlueMarbleSeason)99,
            false,
            false,
            BlueMarbleSeason.Spring);

        // Historical wins defensively if corrupted state enables both modes.
        CheckCycleStep(
            ref failures,
            true,
            true,
            BlueMarbleSeason.Fall,
            false,
            false,
            BlueMarbleSeason.Summer);
        CheckCycleLabel(
            ref failures,
            true,
            true,
            BlueMarbleSeason.Fall,
            "Melish 1817");
    }

    private static void CheckCycleStep(
        ref int failures,
        bool nightLightsEnabled,
        bool historicalMapEnabled,
        BlueMarbleSeason season,
        bool expectedNightLightsEnabled,
        bool expectedHistoricalMapEnabled,
        BlueMarbleSeason expectedSeason)
    {
        bool nextNightLightsEnabled;
        bool nextHistoricalMapEnabled;
        BlueMarbleSeason nextSeason;
        ViewportSurfaceCycle.ResolveNext(
            nightLightsEnabled,
            historicalMapEnabled,
            season,
            out nextNightLightsEnabled,
            out nextHistoricalMapEnabled,
            out nextSeason);

        Check(
            ref failures,
            nextNightLightsEnabled == expectedNightLightsEnabled &&
            nextHistoricalMapEnabled == expectedHistoricalMapEnabled &&
            nextSeason == expectedSeason,
            "Viewport surface cycle produced the wrong transition from " +
            ViewportSurfaceCycle.GetLabel(
                nightLightsEnabled,
                historicalMapEnabled,
                season) + ".");
    }

    private static void CheckCycleLabel(
        ref int failures,
        bool nightLightsEnabled,
        bool historicalMapEnabled,
        BlueMarbleSeason season,
        string expectedLabel)
    {
        string actualLabel = ViewportSurfaceCycle.GetLabel(
            nightLightsEnabled,
            historicalMapEnabled,
            season);
        Check(
            ref failures,
            actualLabel == expectedLabel,
            "Viewport surface button label must be " + expectedLabel +
            ", got " + actualLabel + ".");
    }

    private struct NightSourceManifestEntry
    {
        public NightSourceManifestEntry(
            string id,
            long byteLength,
            string sha256)
        {
            Id = id;
            ByteLength = byteLength;
            Sha256 = sha256;
        }

        public readonly string Id;
        public readonly long ByteLength;
        public readonly string Sha256;
    }

    private static string ComputeSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] digest = sha.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
            {
                builder.Append(digest[index].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    private static void Check(ref int failures, bool condition, string message)
    {
        if (condition)
        {
            return;
        }

        failures++;
        Debug.LogError("GlassGlobeEarthAtNightChecks: " + message);
    }
}

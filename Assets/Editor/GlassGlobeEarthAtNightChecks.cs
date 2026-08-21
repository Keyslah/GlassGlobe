using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    private const string TexturePath =
        "Assets/GlassGlobe/Resources/GlassGlobeNightLights.jpg";
    private const string ShaderPath =
        "Assets/GlassGlobe/Shaders/GlassGlobeTransparentGlobe.shader";
    private const string ExpectedSha256 =
        "d87de751a264e4f8ff69c68de5dab9606daee87a6f15ae743c93200743bd7ec1";

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

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Check(ref failures, texture != null, "Earth at Night texture is missing.");
        if (texture != null)
        {
            Check(
                ref failures,
                texture.width == 3600 && texture.height == 1800,
                "Earth at Night must import at 3600x1800, got " +
                texture.width + "x" + texture.height + ".");
        }

        TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
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
            Directory.GetCurrentDirectory(), TexturePath);
        Check(ref failures, File.Exists(absoluteTexturePath), "Earth at Night source file is missing.");
        if (File.Exists(absoluteTexturePath))
        {
            string actualSha256 = ComputeSha256(absoluteTexturePath);
            Check(
                ref failures,
                string.Equals(actualSha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase),
                "Earth at Night source does not match NASA BlackMarble_2016_01deg.jpg. " +
                "Expected " + ExpectedSha256 + ", got " + actualSha256 + ".");
        }

        Shader shader = Shader.Find("GlassGlobe/Transparent Globe");
        Check(ref failures, shader != null, "Transparent Globe shader is missing.");
        if (shader != null)
        {
            Material material = new Material(shader);
            Check(ref failures, material.HasProperty("_NightTex"), "Shader is missing _NightTex.");
            Check(ref failures, material.HasProperty("_NightOpacity"), "Shader is missing _NightOpacity.");
            UnityEngine.Object.DestroyImmediate(material);
        }

        string absoluteShaderPath = Path.Combine(
            Directory.GetCurrentDirectory(), ShaderPath);
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
        }

        if (failures == 0)
        {
            Debug.Log(
                "GlassGlobe Earth at Night checks passed: official NASA source, " +
                "native 3600x1800 import, Android ASTC, far-side culling, mirrored U, " +
                "and opacity blending are all intact.");
            return true;
        }

        Debug.LogError(
            "GlassGlobe Earth at Night checks failed with " + failures + " issue(s).");
        return false;
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

using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Applies the globe surface setting (Blue Moon glass or a NASA Blue Marble
    /// seasonal map) to the existing globe material. Like EarthStyleController
    /// this is attached to the GlobeRenderer object and driven by
    /// GlassGlobeSettingsController rather than polling in Update. The seasonal
    /// maps are serialized references assigned at scene build time, so they are
    /// never stripped from the player build and never loaded from Resources.
    /// </summary>
    public sealed class BlueMarbleSurface : MonoBehaviour
    {
        public GlobeRenderer globe;

        [Tooltip("NASA Blue Marble Next Generation April composite.")]
        public Texture2D springTexture;

        [Tooltip("NASA Blue Marble Next Generation July composite.")]
        public Texture2D summerTexture;

        [Tooltip("NASA Blue Marble Next Generation October composite.")]
        public Texture2D fallTexture;

        [Tooltip("NASA Blue Marble Next Generation January composite.")]
        public Texture2D winterTexture;

        public string Status { get; private set; }

        private static readonly int BlueMarbleTexId = Shader.PropertyToID("_BlueMarbleTex");
        private static readonly int BlueMarbleOpacityId = Shader.PropertyToID("_BlueMarbleOpacity");

        private Material globeMaterial;
        private Texture2D appliedTexture;
        private bool hasAppliedTexture;

        public static BlueMarbleSurface EnsureInstance(GlobeRenderer globeRenderer)
        {
            if (globeRenderer == null)
            {
                return null;
            }

            BlueMarbleSurface surface = globeRenderer.GetComponent<BlueMarbleSurface>();
            if (surface == null)
            {
                surface = globeRenderer.gameObject.AddComponent<BlueMarbleSurface>();
            }

            surface.globe = globeRenderer;
            return surface;
        }

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            Status = "Initializing";
        }

        private void Start()
        {
            ApplySettings();
        }

        /// <summary>
        /// Pushes the saved surface mode, season, and transparency onto the
        /// globe material. Returns false while the globe renderer or its
        /// material is not ready yet so the caller can retry instead of
        /// recording the settings as applied.
        /// </summary>
        public bool ApplySettings()
        {
            Material material = ResolveGlobeMaterial();
            if (material == null)
            {
                Status = "Globe material not found";
                return false;
            }

            if (!material.HasProperty(BlueMarbleTexId) || !material.HasProperty(BlueMarbleOpacityId))
            {
                Status = "Globe shader does not support Blue Marble";
                return false;
            }

            if (!GlassGlobeSettingsState.EffectiveBlueMarbleEnabled)
            {
                material.SetFloat(BlueMarbleOpacityId, 0f);
                Status = "Blue Moon (glass globe)";
                return true;
            }

            BlueMarbleSeason season = GlassGlobeSettingsState.BlueMarbleSeasonChoice;
            Texture2D texture = ResolveSeasonTexture(season);
            if (texture == null)
            {
                // The shader falls back to a black map, so leaving the layer
                // switched on would paint the planet solid black.
                material.SetFloat(BlueMarbleOpacityId, 0f);
                Status = season + " Blue Marble map missing from the build";
                return true;
            }

            if (!hasAppliedTexture || appliedTexture != texture)
            {
                material.SetTexture(BlueMarbleTexId, texture);
                appliedTexture = texture;
                hasAppliedTexture = true;
            }

            float opacity = GlassGlobeSettingsState.BlueMarbleOpacity;
            material.SetFloat(BlueMarbleOpacityId, opacity);
            Status = string.Format(
                "Blue Marble ({0}). Transparency {1:0}%",
                season,
                opacity * 100f);
            return true;
        }

        private Texture2D ResolveSeasonTexture(BlueMarbleSeason season)
        {
            switch (season)
            {
                case BlueMarbleSeason.Spring:
                    return springTexture;
                case BlueMarbleSeason.Fall:
                    return fallTexture;
                case BlueMarbleSeason.Winter:
                    return winterTexture;
                default:
                    return summerTexture;
            }
        }

        private Material ResolveGlobeMaterial()
        {
            if (globeMaterial != null)
            {
                return globeMaterial;
            }

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (globe == null)
            {
                return null;
            }

            if (globe.globeMaterial != null)
            {
                globeMaterial = globe.globeMaterial;
                return globeMaterial;
            }

            MeshRenderer meshRenderer = globe.GetComponent<MeshRenderer>();
            globeMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;
            return globeMaterial;
        }
    }
}

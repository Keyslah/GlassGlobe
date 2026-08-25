using System.Collections;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Applies the globe surface setting (Blue Moon glass or a NASA Blue Marble
    /// seasonal map) to the existing globe material. Like EarthStyleController
    /// this is attached to the GlobeRenderer object and driven by
    /// GlassGlobeSettingsController rather than polling in Update. Seasonal maps
    /// are loaded from Resources on demand so only the selected map occupies
    /// runtime texture memory.
    /// </summary>
    public sealed class BlueMarbleSurface : MonoBehaviour
    {
        public GlobeRenderer globe;

        public string Status { get; private set; }

        private const string ResourcePrefix = "GlassGlobeBlueMarble";
        private static readonly int BlueMarbleTexId = Shader.PropertyToID("_BlueMarbleTex");
        private static readonly int BlueMarbleOpacityId = Shader.PropertyToID("_BlueMarbleOpacity");

        private Material globeMaterial;
        private Texture2D loadedTexture;
        private string loadedResourceName;
        private string loadingResourceName;
        private int loadGeneration;

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

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                ApplySettings();
            }
        }

        private void OnDisable()
        {
            loadGeneration++;
            loadingResourceName = null;
            ReleaseLoadedTexture(globeMaterial);
        }

        private void OnDestroy()
        {
            loadGeneration++;
            loadingResourceName = null;
            ReleaseLoadedTexture(globeMaterial);
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
                loadGeneration++;
                loadingResourceName = null;
                material.SetFloat(BlueMarbleOpacityId, 0f);
                ReleaseLoadedTexture(material);
                Status = "Blue Moon (glass globe)";
                return true;
            }

            BlueMarbleSeason season = GlassGlobeSettingsState.BlueMarbleSeasonChoice;
            string resourceName = ResolveSeasonResourceName(season);
            if (loadedTexture != null && loadedResourceName == resourceName)
            {
                // A request for a different season may still be in flight. It no
                // longer owns the desired state and must not block a later retry.
                if (loadingResourceName != null)
                {
                    loadGeneration++;
                    loadingResourceName = null;
                }

                ApplyLoadedTexture(material, season);
                return true;
            }

            if (loadingResourceName != resourceName)
            {
                int generation = ++loadGeneration;
                loadingResourceName = resourceName;
                StartCoroutine(LoadSeasonTexture(resourceName, season, generation));
            }

            // Keep the previous season visible while the replacement is decoded.
            // On first use there is no previous texture, so leave the layer clear
            // instead of briefly showing the shader's black fallback.
            if (loadedTexture == null)
            {
                material.SetFloat(BlueMarbleOpacityId, 0f);
            }

            Status = "Loading Blue Marble (" + season + ")";
            return true;
        }

        private IEnumerator LoadSeasonTexture(
            string resourceName,
            BlueMarbleSeason requestedSeason,
            int generation)
        {
            ResourceRequest request = Resources.LoadAsync<Texture2D>(resourceName);
            yield return request;

            Texture2D texture = request.asset as Texture2D;
            bool ownsRequestMarker =
                generation == loadGeneration &&
                loadingResourceName == resourceName;
            if (ownsRequestMarker)
            {
                loadingResourceName = null;
            }

            bool requestIsCurrent =
                ownsRequestMarker &&
                GlassGlobeSettingsState.EffectiveBlueMarbleEnabled &&
                ResolveSeasonResourceName(GlassGlobeSettingsState.BlueMarbleSeasonChoice) == resourceName;

            if (!requestIsCurrent)
            {
                bool sameResourceStillWanted =
                    isActiveAndEnabled &&
                    GlassGlobeSettingsState.EffectiveBlueMarbleEnabled &&
                    ResolveSeasonResourceName(GlassGlobeSettingsState.BlueMarbleSeasonChoice) == resourceName;
                if (texture != null &&
                    texture != loadedTexture &&
                    !sameResourceStillWanted)
                {
                    Resources.UnloadAsset(texture);
                }

                yield break;
            }

            Material material = ResolveGlobeMaterial();
            if (material == null)
            {
                if (texture != null && texture != loadedTexture)
                {
                    Resources.UnloadAsset(texture);
                }

                Status = "Globe material not found after loading " + requestedSeason;
                yield break;
            }

            if (texture == null)
            {
                material.SetFloat(BlueMarbleOpacityId, 0f);
                ReleaseLoadedTexture(material);
                Status = requestedSeason + " Blue Marble map missing from Resources";
                yield break;
            }

            Texture2D previousTexture = loadedTexture;
            loadedTexture = texture;
            loadedResourceName = resourceName;
            ApplyLoadedTexture(material, requestedSeason);
            Debug.Log(
                "GlassGlobe Blue Marble " + requestedSeason + " loaded at " +
                texture.width + "x" + texture.height +
                "; device max texture size=" + SystemInfo.maxTextureSize + ".");

            if (previousTexture != null && previousTexture != texture)
            {
                Resources.UnloadAsset(previousTexture);
            }
        }

        private void ApplyLoadedTexture(Material material, BlueMarbleSeason season)
        {
            material.SetTexture(BlueMarbleTexId, loadedTexture);
            float opacity = GlassGlobeSettingsState.BlueMarbleOpacity;
            material.SetFloat(BlueMarbleOpacityId, opacity);
            Status = string.Format(
                "Blue Marble ({0}). Transparency {1:0}%",
                season,
                opacity * 100f);
        }

        private void ReleaseLoadedTexture(Material material)
        {
            if (loadedTexture == null)
            {
                loadedResourceName = null;
                return;
            }

            if (material != null && material.HasProperty(BlueMarbleTexId))
            {
                material.SetTexture(BlueMarbleTexId, null);
            }

            Resources.UnloadAsset(loadedTexture);
            loadedTexture = null;
            loadedResourceName = null;
        }

        private static string ResolveSeasonResourceName(BlueMarbleSeason season)
        {
            switch (season)
            {
                case BlueMarbleSeason.Spring:
                    return ResourcePrefix + "Spring";
                case BlueMarbleSeason.Fall:
                    return ResourcePrefix + "Fall";
                case BlueMarbleSeason.Winter:
                    return ResourcePrefix + "Winter";
                default:
                    return ResourcePrefix + "Summer";
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

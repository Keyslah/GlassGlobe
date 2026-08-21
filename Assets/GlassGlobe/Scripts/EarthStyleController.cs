using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Applies the Earth at Night surface and the legacy rim-glow setting to the
    /// same globe material used by Blue Marble. The Black Marble image is a real
    /// surface blend, not the old additive glow experiment, so its transparency
    /// behaves exactly like the Blue Marble transparency control.
    /// </summary>
    public sealed class EarthStyleController : MonoBehaviour
    {
        private const string NightTextureResource = "GlassGlobeNightLights";

        private static readonly int NightTextureId = Shader.PropertyToID("_NightTex");
        private static readonly int NightCoverageTextureId =
            Shader.PropertyToID("_NightCoverageTex");
        private static readonly int NightOpacityId = Shader.PropertyToID("_NightOpacity");
        private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");

        public GlobeRenderer globe;

        [Range(0f, 3f)]
        public float rimIntensity = 0.9f;

        public string NightLightsStatus { get; private set; }
        public string RimGlowStatus { get; private set; }

        private Material globeMaterial;
        private Texture2D nightTexture;
        private bool nightTextureLoadAttempted;
        private NightTileSurface fullResolutionNight;

        public static EarthStyleController EnsureInstance(GlobeRenderer globeRenderer)
        {
            if (globeRenderer == null)
            {
                return null;
            }

            EarthStyleController controller = globeRenderer.GetComponent<EarthStyleController>();
            if (controller == null)
            {
                controller = globeRenderer.gameObject.AddComponent<EarthStyleController>();
            }

            controller.globe = globeRenderer;
            return controller;
        }

        private void Awake()
        {
            GlassGlobeSettingsState.Load();
            NightLightsStatus = "Initializing";
            RimGlowStatus = "Initializing";
        }

        private void Start()
        {
            ApplySettings();
        }

        private void OnDestroy()
        {
            if (fullResolutionNight != null)
            {
                fullResolutionNight.SetNightState(globeMaterial, false, 0f);
            }

            ReleaseNightTexture(globeMaterial);
        }

        /// <summary>
        /// Pushes Earth at Night onto the globe. Returns false only when the
        /// globe material is not ready, allowing the settings controller to retry
        /// on the next update instead of silently treating the change as applied.
        /// </summary>
        public bool ApplySettings()
        {
            Material material = ResolveGlobeMaterial();
            if (material == null)
            {
                NightLightsStatus = "Globe material not found";
                RimGlowStatus = "Globe material not found";
                return false;
            }

            if (!material.HasProperty(NightTextureId) ||
                !material.HasProperty(NightCoverageTextureId) ||
                !material.HasProperty(NightOpacityId) ||
                !material.HasProperty(RimIntensityId))
            {
                NightLightsStatus = "Globe shader does not support Earth at Night";
                RimGlowStatus = "Globe shader does not support Earth styles";
                return false;
            }

            // Seen from inside the glass Earth, the far-side surface is nearer
            // to the camera than the weather shells. Draw the globe after those
            // shells so a visible night surface is not buried beneath them.
            material.renderQueue = 3008;

            // Full-resolution patches repeat the base material's rim term.
            // Apply the current rim state before handing the material to the
            // tile surface so the first rendered frame is identical beneath
            // and outside the streamed coverage mask.
            bool rimWanted = GlassGlobeSettingsState.DisplayCategoryEnabled &&
                GlassGlobeSettingsState.RimGlowEnabled;
            material.SetFloat(RimIntensityId, rimWanted ? rimIntensity : 0f);
            RimGlowStatus = rimWanted ? "Visible" : "Hidden";

            bool nightWanted = GlassGlobeSettingsState.EffectiveNightLightsEnabled;
            if (!nightWanted)
            {
                material.SetFloat(NightOpacityId, 0f);
                NightTileSurface surface = ResolveFullResolutionNight();
                if (surface != null)
                {
                    surface.SetNightState(material, false, 0f);
                }

                ReleaseNightTexture(material);
                NightLightsStatus = "Hidden";
            }
            else
            {
                float opacity = GlassGlobeSettingsState.NightLightsOpacity;
                Texture2D texture = ResolveNightTexture();
                if (texture == null)
                {
                    material.SetFloat(NightOpacityId, 0f);
                    NightLightsStatus =
                        "Loading full-resolution NASA tiles (global fallback missing)";
                }
                else
                {
                    material.SetTexture(NightTextureId, texture);
                    material.SetFloat(NightOpacityId, opacity);
                    NightLightsStatus = string.Format(
                        "Visible (NASA Black Marble 2016, full-resolution tiles). Opacity {0:0}%",
                        opacity * 100f);
                }

                NightTileSurface surface = ResolveFullResolutionNight();
                if (surface != null)
                {
                    surface.SetNightState(material, true, opacity);
                }
            }

            return true;
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

        private NightTileSurface ResolveFullResolutionNight()
        {
            if (fullResolutionNight == null)
            {
                fullResolutionNight = NightTileSurface.EnsureInstance(globe);
            }

            return fullResolutionNight;
        }

        private Texture2D ResolveNightTexture()
        {
            if (nightTexture != null || nightTextureLoadAttempted)
            {
                return nightTexture;
            }

            nightTextureLoadAttempted = true;
            nightTexture = Resources.Load<Texture2D>(NightTextureResource);
            if (nightTexture == null)
            {
                Debug.LogWarning(
                    "EarthStyleController: Earth at Night texture not found at Resources/" +
                    NightTextureResource + ".");
            }

            return nightTexture;
        }

        private void ReleaseNightTexture(Material material)
        {
            if (nightTexture == null)
            {
                nightTextureLoadAttempted = false;
                return;
            }

            if (material != null && material.HasProperty(NightTextureId))
            {
                material.SetTexture(NightTextureId, null);
            }

            Resources.UnloadAsset(nightTexture);
            nightTexture = null;
            nightTextureLoadAttempted = false;
        }
    }
}

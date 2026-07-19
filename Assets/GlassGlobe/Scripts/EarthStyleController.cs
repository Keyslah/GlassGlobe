using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Applies the Earth Styles settings (night lights, rim glow) to the globe
    /// material. Attached at runtime by GlassGlobeSettingsController, so the
    /// generated preview scene keeps working without a hand-maintained
    /// reference. The night-lights map ships in Resources so it is never
    /// stripped from device builds.
    /// </summary>
    public sealed class EarthStyleController : MonoBehaviour
    {
        private const string NightTextureResource = "GlassGlobeNightLights";

        public GlobeRenderer globe;

        [Range(0f, 3f)]
        public float nightIntensity = 1.6f;

        [Range(0f, 3f)]
        public float rimIntensity = 0.9f;

        public string NightLightsStatus { get; private set; }
        public string RimGlowStatus { get; private set; }

        private Texture2D nightTexture;
        private bool nightTextureLoadAttempted;

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

        public void ApplySettings()
        {
            Material material = ResolveGlobeMaterial();
            if (material == null)
            {
                NightLightsStatus = "Globe material not found";
                RimGlowStatus = "Globe material not found";
                return;
            }

            if (!material.HasProperty("_NightIntensity") || !material.HasProperty("_RimIntensity"))
            {
                NightLightsStatus = "Globe shader does not support Earth styles";
                RimGlowStatus = "Globe shader does not support Earth styles";
                return;
            }

            // Seen from inside the glass Earth, the far-side surface is nearer
            // to the camera than the weather shells above it (clouds 3004,
            // radar 3006), so the globe draws last or its lights wash out.
            material.renderQueue = 3008;

            bool nightWanted = GlassGlobeSettingsState.NightLightsEnabled;
            Texture2D texture = nightWanted ? ResolveNightTexture() : nightTexture;
            if (nightWanted && texture == null)
            {
                material.SetFloat("_NightIntensity", 0f);
                NightLightsStatus = "Night-lights map missing from Resources";
            }
            else
            {
                if (texture != null)
                {
                    material.SetTexture("_NightTex", texture);
                }

                material.SetFloat("_NightIntensity", nightWanted ? nightIntensity : 0f);
                NightLightsStatus = nightWanted ? "Visible (NASA Black Marble)" : "Hidden";
            }

            bool rimWanted = GlassGlobeSettingsState.RimGlowEnabled;
            material.SetFloat("_RimIntensity", rimWanted ? rimIntensity : 0f);
            RimGlowStatus = rimWanted ? "Visible" : "Hidden";
        }

        private Material ResolveGlobeMaterial()
        {
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
                return globe.globeMaterial;
            }

            MeshRenderer meshRenderer = globe.GetComponent<MeshRenderer>();
            return meshRenderer != null ? meshRenderer.sharedMaterial : null;
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
                Debug.LogWarning("EarthStyleController: night-lights texture not found at Resources/" + NightTextureResource + ".");
            }

            return nightTexture;
        }
    }
}

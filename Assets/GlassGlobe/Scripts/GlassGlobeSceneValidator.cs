using UnityEngine;

namespace GlassGlobe
{
    public sealed class GlassGlobeSceneValidator : MonoBehaviour
    {
        public bool validateOnStart = true;

        private void Start()
        {
            if (validateOnStart)
            {
                ValidateScene();
            }
        }

        public bool ValidateScene()
        {
            int errors = 0;

            Camera mainCamera = Camera.main;
            errors += Require(mainCamera != null, "GlassGlobe validation failed: scene needs a Camera tagged MainCamera.");

            PhonePoseSimulator phonePose = FindFirstObjectByType<PhonePoseSimulator>();
            errors += Require(phonePose != null, "GlassGlobe validation failed: missing PhonePoseSimulator.");
            if (phonePose != null)
            {
                errors += Require(phonePose.targetCamera != null, "GlassGlobe validation failed: PhonePoseSimulator is missing targetCamera.");
                errors += Require(phonePose.eyeToPhoneDistanceInches >= 9f && phonePose.eyeToPhoneDistanceInches <= 11f, "GlassGlobe validation failed: phone viewport baseline should be about 10 inches from the face.");
            }

            errors += Require(FindFirstObjectByType<FarSideRaycaster>() != null, "GlassGlobe validation failed: missing FarSideRaycaster.");
            errors += Require(FindFirstObjectByType<GlobeRenderer>() != null, "GlassGlobe validation failed: missing GlobeRenderer.");
            errors += Require(FindFirstObjectByType<GlobeGridRenderer>() != null, "GlassGlobe validation failed: missing low-poly GlobeGridRenderer.");
            errors += Require(FindFirstObjectByType<CountryLabelController>() != null, "GlassGlobe validation failed: missing CountryLabelController.");
            errors += Require(FindFirstObjectByType<WeatherOverlay>() != null, "GlassGlobe validation failed: missing WeatherOverlay.");
            errors += Require(FindFirstObjectByType<SatelliteOverlay>() != null, "GlassGlobe validation failed: missing SatelliteOverlay.");
            errors += Require(FindFirstObjectByType<EarthquakeOverlay>() != null, "GlassGlobe validation failed: missing EarthquakeOverlay.");
            errors += Require(FindFirstObjectByType<GlassGlobeReticle>() != null, "GlassGlobe validation failed: missing center reticle.");

            FarSideRaycaster raycaster = FindFirstObjectByType<FarSideRaycaster>();
            if (raycaster != null)
            {
                raycaster.UpdateRaycast();
                errors += Require(raycaster.HasIntersection, "GlassGlobe validation failed: center reticle ray does not intersect the globe.");
            }

            CountryBorderRenderer borders = FindFirstObjectByType<CountryBorderRenderer>();
            errors += Require(borders != null, "GlassGlobe validation failed: missing CountryBorderRenderer.");
            if (borders != null)
            {
                LineRenderer[] lineRenderers = borders.GetComponentsInChildren<LineRenderer>(true);
                errors += Require(lineRenderers.Length > 0, "GlassGlobe validation failed: country border line renderers were not generated.");
                errors += Require(HasOutline(borders, "Australia"), "GlassGlobe validation failed: geography missing Australia.");
                errors += Require(HasOutline(borders, "Antarctica"), "GlassGlobe validation failed: geography missing Antarctica.");
                errors += Require(HasOutline(borders, "Brazil"), "GlassGlobe validation failed: geography missing Brazil.");
                errors += Require(HasOutline(borders, "South Africa"), "GlassGlobe validation failed: geography missing South Africa.");
                errors += Require(HasOutline(borders, "New Zealand") || HasOutline(borders, "Indonesia"), "GlassGlobe validation failed: geography missing New Zealand or Indonesia.");
            }

            GlassGlobeHUD hud = FindFirstObjectByType<GlassGlobeHUD>();
            errors += Require(hud != null, "GlassGlobe validation failed: missing GlassGlobeHUD.");
            if (hud != null)
            {
                errors += Require(hud.showHud, "GlassGlobe validation failed: GlassGlobeHUD is present but disabled.");
                errors += Require(hud.phonePose != null, "GlassGlobe validation failed: GlassGlobeHUD is not wired to PhonePoseSimulator.");
                errors += Require(hud.farSideRaycaster != null, "GlassGlobe validation failed: GlassGlobeHUD is not wired to FarSideRaycaster.");
            }

            errors += Require(GameObject.Find("Debug User Position") != null, "GlassGlobe validation failed: missing debug user position marker.");
            errors += Require(GameObject.Find("Debug Local Up") != null, "GlassGlobe validation failed: missing debug local up line.");
            errors += Require(GameObject.Find("Debug Local Down") != null, "GlassGlobe validation failed: missing debug local down line.");
            errors += Require(GameObject.Find("Debug Center Ray") != null, "GlassGlobe validation failed: missing debug center-screen ray line.");
            errors += Require(GameObject.Find("Debug Far-Side Intersection") != null, "GlassGlobe validation failed: missing debug far-side intersection marker.");

            if (errors == 0)
            {
                Debug.Log("GlassGlobe validation passed: simulator preview has camera, globe, 3D wrapped sample geography, HUD controls, presets, reticle, and debug visualization.", this);
                return true;
            }

            Debug.LogError("GlassGlobe validation failed with " + errors + " issue(s). See errors above for missing scene pieces.", this);
            return false;
        }

        private static bool HasOutline(CountryBorderRenderer borders, string outlineName)
        {
            if (borders.Outlines == null)
            {
                return false;
            }

            for (int index = 0; index < borders.Outlines.Count; index++)
            {
                CountryBorderRenderer.GeoOutline outline = borders.Outlines[index];
                if (outline != null && outline.name == outlineName)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Require(bool condition, string error)
        {
            if (condition)
            {
                return 0;
            }

            Debug.LogError(error);
            return 1;
        }
    }
}

using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Shared immediate-mode UI sizing helpers used by the HUD, the settings
    /// controller, and the reticle so mobile scaling behaves identically
    /// everywhere.
    /// </summary>
    public static class GlassGlobeUi
    {
        public static float GetMobileUiScale()
        {
            if (!Application.isMobilePlatform)
            {
                return 1f;
            }

            if (Screen.dpi > 0f)
            {
                return Mathf.Clamp(Screen.dpi / 160f, 1f, 4f);
            }

            float shortestSide = Mathf.Min(Screen.width, Screen.height);
            return Mathf.Clamp(shortestSide / 360f, 1f, 4f);
        }

        public static float GetInteractiveControlHeight(float requestedHeight)
        {
            return Application.isMobilePlatform
                ? Mathf.Max(48f, requestedHeight)
                : requestedHeight;
        }
    }
}

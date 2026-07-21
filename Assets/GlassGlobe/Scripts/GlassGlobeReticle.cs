using UnityEngine;

namespace GlassGlobe
{
    public sealed class GlassGlobeReticle : MonoBehaviour
    {
        public bool showReticle = true;
        public Color reticleColor = new Color(1f, 0.95f, 0.35f, 0.95f);
        public float armLengthPixels = 11f;
        public float gapPixels = 4f;
        public float thicknessPixels = 2f;

        [Min(0.25f)]
        public float celestialNameHoldSeconds = 1.7f;

        [Min(0.1f)]
        public float celestialNameFadeSeconds = 0.65f;

        private SunMoonBackground celestialBackground;
        private SatelliteOverlay satelliteOverlay;
        private EarthquakeOverlay earthquakeOverlay;
        private GUIStyle nameStyle;
        private string pointedBodyName;
        private string popupBodyName;
        private float popupStartedAt = float.NegativeInfinity;

        private void OnGUI()
        {
            if (!showReticle)
            {
                return;
            }

            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            Color previousColor = GUI.color;
            GUI.color = reticleColor;

            DrawRect(x - gapPixels - armLengthPixels, y - thicknessPixels * 0.5f, armLengthPixels, thicknessPixels);
            DrawRect(x + gapPixels, y - thicknessPixels * 0.5f, armLengthPixels, thicknessPixels);
            DrawRect(x - thicknessPixels * 0.5f, y - gapPixels - armLengthPixels, thicknessPixels, armLengthPixels);
            DrawRect(x - thicknessPixels * 0.5f, y + gapPixels, thicknessPixels, armLengthPixels);
            DrawRect(x - thicknessPixels * 0.5f, y - thicknessPixels * 0.5f, thicknessPixels, thicknessPixels);

            DrawCelestialName(x, y);

            GUI.color = previousColor;
        }

        private void DrawCelestialName(float centerX, float centerY)
        {
            if (celestialBackground == null)
            {
                celestialBackground = FindFirstObjectByType<SunMoonBackground>();
            }

            if (satelliteOverlay == null)
            {
                satelliteOverlay = FindFirstObjectByType<SatelliteOverlay>();
            }

            if (earthquakeOverlay == null)
            {
                earthquakeOverlay = FindFirstObjectByType<EarthquakeOverlay>();
            }

            Camera camera = Camera.main;
            string bodyName = null;
            bool hasPointedBody = camera != null &&
                TryGetNearestPointedTarget(camera, out bodyName);

            if (hasPointedBody)
            {
                if (bodyName != pointedBodyName)
                {
                    pointedBodyName = bodyName;
                    popupBodyName = bodyName;
                    popupStartedAt = Time.unscaledTime;
                }
            }
            else
            {
                pointedBodyName = null;
            }

            if (string.IsNullOrEmpty(popupBodyName))
            {
                return;
            }

            float age = Time.unscaledTime - popupStartedAt;
            float totalDuration = celestialNameHoldSeconds + celestialNameFadeSeconds;
            if (age >= totalDuration)
            {
                popupBodyName = null;
                return;
            }

            float alpha = age <= celestialNameHoldSeconds
                ? 1f
                : 1f - Mathf.InverseLerp(celestialNameHoldSeconds, totalDuration, age);

            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label);
                nameStyle.alignment = TextAnchor.MiddleCenter;
                nameStyle.fontSize = Mathf.Max(16, Screen.height / 45);
                nameStyle.fontStyle = FontStyle.Bold;
                nameStyle.normal.textColor = Color.white;
                nameStyle.clipping = TextClipping.Overflow;
            }

            float labelHeight = Mathf.Max(48f, nameStyle.fontSize * 1.65f);
            float labelWidth = Mathf.Max(240f, nameStyle.CalcSize(new GUIContent(popupBodyName)).x + 36f);
            Rect labelRect = new Rect(
                centerX - labelWidth * 0.5f,
                centerY + gapPixels + armLengthPixels + 6f,
                labelWidth,
                labelHeight);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(labelRect, popupBodyName, nameStyle);
            GUI.color = previousColor;
        }

        /// <summary>
        /// Asks every naming provider (celestial bodies, satellites,
        /// earthquakes) what sits under the reticle and keeps the closest by
        /// angle.
        /// </summary>
        private bool TryGetNearestPointedTarget(Camera camera, out string label)
        {
            label = null;
            float bestAngle = float.MaxValue;
            Vector3 forward = camera.transform.forward;
            Vector3 cameraPosition = camera.transform.position;

            string candidate;
            float angle;
            if (celestialBackground != null &&
                celestialBackground.TryGetPointedBody(forward, out candidate, out angle) &&
                angle < bestAngle)
            {
                bestAngle = angle;
                label = candidate;
            }

            if (satelliteOverlay != null &&
                satelliteOverlay.TryGetPointedTarget(forward, cameraPosition, out candidate, out angle) &&
                angle < bestAngle)
            {
                bestAngle = angle;
                label = candidate;
            }

            if (earthquakeOverlay != null &&
                earthquakeOverlay.TryGetPointedTarget(forward, cameraPosition, out candidate, out angle) &&
                angle < bestAngle)
            {
                bestAngle = angle;
                label = candidate;
            }

            return label != null;
        }

        private static void DrawRect(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        }
    }
}

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

            GUI.color = previousColor;
        }

        private static void DrawRect(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        }
    }
}

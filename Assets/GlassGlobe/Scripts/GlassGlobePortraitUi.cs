using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Keeps Android's render surface locked to portrait while exposing a
    /// landscape-shaped logical UI canvas when the phone is physically held
    /// sideways. World rendering remains in the portrait surface; only IMGUI
    /// overlays and their touch coordinates use this transform.
    /// </summary>
    public static class GlassGlobePortraitUi
    {
        private const float LandscapeGravityThreshold = 0.84f;
        private const float PortraitGravityThreshold = 0.58f;
        private const float OrientationSettleSeconds = 0.12f;

        public enum Rotation
        {
            Portrait,
            LandscapeLeft,
            LandscapeRight
        }

        private static Rotation lastStableRotation = Rotation.Portrait;
        private static Rotation candidateRotation = Rotation.Portrait;
        private static float candidateStartedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LockDisplayToPortrait()
        {
            lastStableRotation = Rotation.Portrait;
            candidateRotation = Rotation.Portrait;
            candidateStartedAt = Time.unscaledTime;
            if (!Application.isMobilePlatform)
            {
                return;
            }

            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
        }

        public static Rotation CurrentRotation
        {
            get
            {
                if (!Application.isMobilePlatform)
                {
                    return Rotation.Portrait;
                }

                Vector3 gravity = Input.acceleration;
                float horizontalGravity = Mathf.Abs(gravity.x);
                float verticalGravity = Mathf.Abs(gravity.y);
                Rotation desiredRotation = lastStableRotation;

                bool fullyLandscape =
                    horizontalGravity >= LandscapeGravityThreshold &&
                    horizontalGravity > verticalGravity * 1.8f;
                bool clearlyPortrait =
                    verticalGravity >= PortraitGravityThreshold &&
                    verticalGravity > horizontalGravity * 1.05f;

                if (fullyLandscape)
                {
                    DeviceOrientation deviceOrientation =
                        Input.deviceOrientation;
                    if (deviceOrientation == DeviceOrientation.LandscapeLeft)
                    {
                        desiredRotation = Rotation.LandscapeLeft;
                    }
                    else if (deviceOrientation ==
                        DeviceOrientation.LandscapeRight)
                    {
                        desiredRotation = Rotation.LandscapeRight;
                    }
                    else
                    {
                        desiredRotation = gravity.x < 0f
                            ? Rotation.LandscapeLeft
                            : Rotation.LandscapeRight;
                    }
                }
                else if (clearlyPortrait)
                {
                    desiredRotation = Rotation.Portrait;
                }

                if (desiredRotation == lastStableRotation)
                {
                    candidateRotation = lastStableRotation;
                    candidateStartedAt = Time.unscaledTime;
                    return lastStableRotation;
                }

                if (candidateRotation != desiredRotation)
                {
                    candidateRotation = desiredRotation;
                    candidateStartedAt = Time.unscaledTime;
                    return lastStableRotation;
                }

                if (Time.unscaledTime - candidateStartedAt >=
                    OrientationSettleSeconds)
                {
                    lastStableRotation = candidateRotation;
                }

                // Ambiguous, face-up, and face-down poses retain the last
                // stable state so the controls do not flicker while aiming.
                return lastStableRotation;
            }
        }

        public static float Width
        {
            get
            {
                return CurrentRotation == Rotation.Portrait
                    ? Screen.width
                    : Screen.height;
            }
        }

        public static float Height
        {
            get
            {
                return CurrentRotation == Rotation.Portrait
                    ? Screen.height
                    : Screen.width;
            }
        }

        /// <summary>
        /// Maps the logical portrait-or-landscape UI canvas into Unity's
        /// permanently portrait, top-left-origin IMGUI surface.
        /// </summary>
        public static Matrix4x4 GuiMatrix
        {
            get
            {
                switch (CurrentRotation)
                {
                    case Rotation.LandscapeLeft:
                        return Matrix4x4.TRS(
                            new Vector3(Screen.width, 0f, 0f),
                            Quaternion.Euler(0f, 0f, 90f),
                            Vector3.one);
                    case Rotation.LandscapeRight:
                        return Matrix4x4.TRS(
                            new Vector3(0f, Screen.height, 0f),
                            Quaternion.Euler(0f, 0f, -90f),
                            Vector3.one);
                    default:
                        return Matrix4x4.identity;
                }
            }
        }

        /// <summary>
        /// Converts a physical screen point with a top-left origin into the
        /// rotated logical UI canvas used by the IMGUI overlays.
        /// </summary>
        public static Vector2 ScreenToUi(Vector2 screenPoint)
        {
            switch (CurrentRotation)
            {
                case Rotation.LandscapeLeft:
                    return new Vector2(
                        screenPoint.y,
                        Screen.width - screenPoint.x);
                case Rotation.LandscapeRight:
                    return new Vector2(
                        Screen.height - screenPoint.y,
                        screenPoint.x);
                default:
                    return screenPoint;
            }
        }

        public static Rect SafeArea
        {
            get
            {
                Rect physicalSafeArea = Screen.safeArea;
                Rect physicalTopLeft = new Rect(
                    physicalSafeArea.xMin,
                    Screen.height - physicalSafeArea.yMax,
                    physicalSafeArea.width,
                    physicalSafeArea.height);

                Vector2 first = ScreenToUi(
                    new Vector2(physicalTopLeft.xMin, physicalTopLeft.yMin));
                Vector2 second = ScreenToUi(
                    new Vector2(physicalTopLeft.xMax, physicalTopLeft.yMin));
                Vector2 third = ScreenToUi(
                    new Vector2(physicalTopLeft.xMin, physicalTopLeft.yMax));
                Vector2 fourth = ScreenToUi(
                    new Vector2(physicalTopLeft.xMax, physicalTopLeft.yMax));

                float xMin = Mathf.Min(first.x, second.x, third.x, fourth.x);
                float yMin = Mathf.Min(first.y, second.y, third.y, fourth.y);
                float xMax = Mathf.Max(first.x, second.x, third.x, fourth.x);
                float yMax = Mathf.Max(first.y, second.y, third.y, fourth.y);
                return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }
    }
}

using System;
using UnityEngine;

namespace GlassGlobe
{
    /// <summary>
    /// Mobile touch handling and interaction tracking for the settings
    /// controller. On device the immediate-mode buttons register screen-space
    /// touch targets during Repaint and this layer dispatches taps and scroll.
    /// </summary>
    public sealed partial class GlassGlobeSettingsController
    {
        private void RegisterLocalTouch(Rect localRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect screenRect = new Rect(
                (activeAreaRect.x + localRect.x - settingsScrollPosition.x) * activeUiScale,
                (activeAreaRect.y + localRect.y - settingsScrollPosition.y) * activeUiScale,
                localRect.width * activeUiScale,
                localRect.height * activeUiScale);
            Rect clippedRect = IntersectRects(screenRect, activeTouchViewportRect);
            if (clippedRect.width > 0f && clippedRect.height > 0f)
            {
                RegisterScreenTouch(clippedRect, action);
            }
        }

        private void RegisterScreenTouch(Rect screenRect, Action action)
        {
            if (!Application.isMobilePlatform || action == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            touchTargets.Add(new TouchTarget(screenRect, action));
        }

        private void HandleMobileTouch()
        {
            if (!Application.isMobilePlatform)
            {
                return;
            }

            if (Input.touchCount == 0)
            {
                pinchGestureActive = false;
                pinchGestureConsumed = false;
                previousPinchDistance = 0f;
                return;
            }

            if (currentPage == SettingsPage.Closed && Input.touchCount >= 2)
            {
                pinchGestureConsumed = true;
                touchDragged = true;
                HandleViewportPinchZoom();
                return;
            }

            // Keep consuming the remaining finger until every finger from the
            // pinch has lifted. Otherwise the final Ended touch would look like
            // a normal viewport tap and reopen Settings.
            if (pinchGestureConsumed)
            {
                touchDragged = true;
                Touch remainingTouch = Input.GetTouch(0);
                if (remainingTouch.phase == TouchPhase.Ended ||
                    remainingTouch.phase == TouchPhase.Canceled)
                {
                    ResetTrackedTouch();
                }

                return;
            }

            Touch touch = Input.GetTouch(0);
            Vector2 physicalScreenPoint = new Vector2(
                touch.position.x,
                Screen.height - touch.position.y);
            Vector2 screenPoint =
                GlassGlobePortraitUi.ScreenToUi(physicalScreenPoint);
            if (touch.phase == TouchPhase.Began)
            {
                trackedTouchFingerId = touch.fingerId;
                touchStartScreenPoint = screenPoint;
                touchDragged = false;
                scrollTouchActive = currentPage != SettingsPage.Closed &&
                    activeTouchViewportRect.Contains(screenPoint);
                return;
            }

            if (touch.fingerId != trackedTouchFingerId)
            {
                return;
            }

            float dragThreshold = 18f * Mathf.Max(1f, activeUiScale);
            if ((screenPoint - touchStartScreenPoint).sqrMagnitude > dragThreshold * dragThreshold)
            {
                touchDragged = true;
            }

            if (touch.phase == TouchPhase.Moved && scrollTouchActive)
            {
                Vector2 previousTouchPosition =
                    touch.position - touch.deltaPosition;
                Vector2 previousPhysicalScreenPoint = new Vector2(
                    previousTouchPosition.x,
                    Screen.height - previousTouchPosition.y);
                Vector2 previousUiPoint =
                    GlassGlobePortraitUi.ScreenToUi(
                        previousPhysicalScreenPoint);
                Vector2 uiDelta = screenPoint - previousUiPoint;
                settingsScrollPosition.y = Mathf.Max(
                    0f,
                    settingsScrollPosition.y -
                    uiDelta.y / Mathf.Max(1f, activeUiScale));
                lastInteractionTime = Time.unscaledTime;
                return;
            }

            if (touch.phase == TouchPhase.Canceled)
            {
                ResetTrackedTouch();
                return;
            }

            if (touch.phase != TouchPhase.Ended)
            {
                return;
            }

            if (currentPage == SettingsPage.Closed)
            {
                if (!touchDragged &&
                    (hud == null || !hud.IsInteractiveUiPoint(touchStartScreenPoint)))
                {
                    OpenSettings();
                }

                ResetTrackedTouch();
                return;
            }

            if (touchDragged)
            {
                ResetTrackedTouch();
                return;
            }

            for (int index = touchTargets.Count - 1; index >= 0; index--)
            {
                TouchTarget target = touchTargets[index];
                if (!target.ScreenRect.Contains(touchStartScreenPoint) ||
                    !target.ScreenRect.Contains(screenPoint))
                {
                    continue;
                }

                target.Action();
                lastInteractionTime = Time.unscaledTime;
                ResetTrackedTouch();
                return;
            }

            ResetTrackedTouch();
        }

        private void HandleViewportPinchZoom()
        {
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            float distance = Vector2.Distance(first.position, second.position);
            if (!pinchGestureActive || previousPinchDistance <= 0f)
            {
                pinchGestureActive = true;
                previousPinchDistance = distance;
                RefreshZoomIndicator();
                return;
            }

            float distanceRatio = distance / Mathf.Max(1f, previousPinchDistance);
            previousPinchDistance = distance;
            if (Mathf.Abs(distanceRatio - 1f) < 0.0001f)
            {
                return;
            }

            float currentFov = GetViewportFov();
            float currentHalfFovRadians = currentFov * 0.5f * Mathf.Deg2Rad;
            float nextFov = 2f * Mathf.Atan(
                Mathf.Tan(currentHalfFovRadians) / distanceRatio) * Mathf.Rad2Deg;
            float maximumFov = cameraFeed != null && cameraFeed.FeedActive
                ? cameraFeed.NativeViewportFovDegrees
                : 75f;
            SetViewportFov(Mathf.Clamp(
                nextFov,
                PhonePoseSimulator.MinimumViewportFovDegrees,
                maximumFov));
            RefreshZoomIndicator();
            lastInteractionTime = Time.unscaledTime;
        }

        private void RefreshZoomIndicator()
        {
            zoomIndicatorCurrentFov = GetViewportFov();
            zoomIndicatorDefaultFov = GetDefaultViewportFov();
            zoomIndicatorVisibleUntil = Time.unscaledTime + 0.9f;
        }

        private float GetDefaultViewportFov()
        {
            return PhonePoseSimulator.DefaultViewportFovDegrees;
        }

        private void DrawZoomIndicator()
        {
            if (!pinchGestureActive && Time.unscaledTime >= zoomIndicatorVisibleUntil)
            {
                return;
            }

            float fade = pinchGestureActive
                ? 1f
                : Mathf.Clamp01((zoomIndicatorVisibleUntil - Time.unscaledTime) / 0.9f);
            float scale = Mathf.Min(1.6f, GlassGlobeUi.GetMobileUiScale());
            Rect safeArea = GlassGlobePortraitUi.SafeArea;
            float topInset = safeArea.yMin;
            float width = Mathf.Min(safeArea.width - 24f, 400f * scale);
            float height = 62f * scale;
            Rect panel = new Rect(
                safeArea.xMin + (safeArea.width - width) * 0.5f,
                topInset + 8f,
                width,
                height);

            DrawZoomSolidRect(panel, new Color(0f, 0f, 0f, 0.72f * fade));

            if (zoomIndicatorStyle == null)
            {
                zoomIndicatorStyle = new GUIStyle(GUI.skin.label);
                zoomIndicatorStyle.alignment = TextAnchor.MiddleCenter;
                zoomIndicatorStyle.fontStyle = FontStyle.Bold;
            }

            zoomIndicatorStyle.fontSize = Mathf.RoundToInt(11f * scale);
            zoomIndicatorStyle.normal.textColor = new Color(0.92f, 0.98f, 1f, fade);

            float currentScale = ZoomMagnification(
                zoomIndicatorCurrentFov,
                zoomIndicatorDefaultFov);
            GUI.Label(
                new Rect(panel.x, panel.y + 2f * scale, panel.width, 18f * scale),
                "Zoom " + currentScale.ToString("0.00") + "x",
                zoomIndicatorStyle);

            float padding = 24f * scale;
            Rect track = new Rect(
                panel.x + padding,
                panel.y + 27f * scale,
                panel.width - padding * 2f,
                4f * scale);
            DrawZoomSolidRect(track, new Color(0.55f, 0.66f, 0.72f, 0.7f * fade));

            float normalized = NormalizeZoomIndicator(
                zoomIndicatorCurrentFov,
                zoomIndicatorDefaultFov,
                cameraFeed != null && cameraFeed.FeedActive
                    ? cameraFeed.NativeViewportFovDegrees
                    : 75f);
            float centerX = track.x + track.width * 0.5f;
            float markerX = Mathf.Lerp(track.xMin, track.xMax, normalized);
            DrawZoomSolidRect(
                new Rect(centerX - 1.5f * scale, track.y - 5f * scale, 3f * scale, 14f * scale),
                new Color(1f, 0.78f, 0.18f, fade));
            DrawZoomSolidRect(
                new Rect(markerX - 3f * scale, track.y - 4f * scale, 6f * scale, 12f * scale),
                new Color(0.1f, 0.9f, 1f, fade));

            zoomIndicatorStyle.fontSize = Mathf.RoundToInt(9f * scale);
            float labelY = panel.y + 37f * scale;
            GUI.Label(
                new Rect(track.x - 16f * scale, labelY, 52f * scale, 16f * scale),
                "OUT",
                zoomIndicatorStyle);
            GUI.Label(
                new Rect(centerX - 34f * scale, labelY, 68f * scale, 16f * scale),
                "DEFAULT",
                zoomIndicatorStyle);
            GUI.Label(
                new Rect(track.xMax - 36f * scale, labelY, 52f * scale, 16f * scale),
                "IN",
                zoomIndicatorStyle);
        }

        private static float NormalizeZoomIndicator(
            float currentFov,
            float defaultFov,
            float maximumFov)
        {
            float scale = ZoomMagnification(currentFov, defaultFov);
            float zoomInMaximum = ZoomMagnification(
                PhonePoseSimulator.MinimumViewportFovDegrees,
                defaultFov);
            if (scale >= 1f)
            {
                float zoomInAmount = (scale - 1f) /
                    Mathf.Max(0.0001f, zoomInMaximum - 1f);
                return 0.5f + 0.5f * Mathf.Clamp01(zoomInAmount);
            }

            float zoomOutMinimum = ZoomMagnification(maximumFov, defaultFov);
            float zoomOutAmount = (1f - scale) /
                Mathf.Max(0.0001f, 1f - zoomOutMinimum);
            return 0.5f - 0.5f * Mathf.Clamp01(zoomOutAmount);
        }

        private static float ZoomMagnification(float currentFov, float defaultFov)
        {
            float defaultHalfTangent = Mathf.Tan(
                Mathf.Clamp(defaultFov, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            float currentHalfTangent = Mathf.Tan(
                Mathf.Clamp(currentFov, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            return defaultHalfTangent / Mathf.Max(0.0001f, currentHalfTangent);
        }

        private static void DrawZoomSolidRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private float GetViewportFov()
        {
            if (cameraFeed != null && cameraFeed.FeedActive)
            {
                return cameraFeed.ViewportFovDegrees;
            }

            if (poseSensors != null && poseSensors.SensorModeActive)
            {
                return poseSensors.cameraFovDegrees;
            }

            return phonePose != null
                ? phonePose.cameraFovDegrees
                : PhonePoseSimulator.DefaultViewportFovDegrees;
        }

        private void SetViewportFov(float value)
        {
            if (cameraFeed != null)
            {
                cameraFeed.SetViewportFovDegrees(value);
            }

            bool sensorsActive = poseSensors != null && poseSensors.SensorModeActive;
            if (poseSensors != null)
            {
                poseSensors.cameraFovDegrees = value;
            }

            if (phonePose != null)
            {
                phonePose.cameraFovDegrees = value;
                if (!sensorsActive)
                {
                    phonePose.ApplyPose();
                }
            }

            if (farSideRaycaster != null)
            {
                farSideRaycaster.UpdateRaycast();
            }
        }

        private void TrackInteraction()
        {
            bool interacted = Input.touchCount > 0 || Input.anyKeyDown;
            Vector3 currentMousePosition = Input.mousePosition;
            if (!hasLastMousePosition)
            {
                hasLastMousePosition = true;
                lastMousePosition = currentMousePosition;
            }
            else if ((currentMousePosition - lastMousePosition).sqrMagnitude > 0.25f)
            {
                interacted = true;
                lastMousePosition = currentMousePosition;
            }

            if (interacted)
            {
                lastInteractionTime = Time.unscaledTime;
            }
        }

        private void ResetTrackedTouch()
        {
            trackedTouchFingerId = -1;
            touchDragged = false;
            scrollTouchActive = false;
        }

        private static Rect IntersectRects(Rect left, Rect right)
        {
            float xMin = Mathf.Max(left.xMin, right.xMin);
            float yMin = Mathf.Max(left.yMin, right.yMin);
            float xMax = Mathf.Min(left.xMax, right.xMax);
            float yMax = Mathf.Min(left.yMax, right.yMax);
            return xMax > xMin && yMax > yMin
                ? Rect.MinMaxRect(xMin, yMin, xMax, yMax)
                : Rect.zero;
        }
    }
}

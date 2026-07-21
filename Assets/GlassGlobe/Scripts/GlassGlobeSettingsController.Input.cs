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
            if (!Application.isMobilePlatform || Input.touchCount == 0)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);
            Vector2 screenPoint = new Vector2(touch.position.x, Screen.height - touch.position.y);
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
                settingsScrollPosition.y = Mathf.Max(
                    0f,
                    settingsScrollPosition.y + touch.deltaPosition.y / Mathf.Max(1f, activeUiScale));
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

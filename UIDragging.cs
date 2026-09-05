using BepInEx.Configuration;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ExtraSlots
{
    internal static class UIDragging
    {
        internal sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            internal Func<bool> CanDrag;
            internal Func<Vector2> GetPosition;
            internal Action<Vector2> PreviewPosition;
            internal Action<Vector2> CommitPosition;
            internal Func<Vector2, Vector2> ConvertDelta;
            internal Func<Vector2, Vector2> SnapPosition;
            internal RectTransform MovementSpace;
            internal RectTransform RaycastSurface;
            internal bool IsDragging => dragging;

            private bool dragging;
            private float canvasScale = 1f;
            private Vector2 rawPosition;
            private Vector2 previewPosition;

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (dragging || eventData.button != PointerEventData.InputButton.Left || CanDrag?.Invoke() != true || GetPosition == null)
                    return;

                dragging = true;
                rawPosition = GetPosition();
                previewPosition = rawPosition;
                canvasScale = GetComponentInParent<Canvas>()?.scaleFactor ?? 1f;
                if (canvasScale <= 0f)
                    canvasScale = 1f;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!dragging)
                    return;

                Vector2 delta = eventData.delta / canvasScale;
                if (MovementSpace
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(MovementSpace, eventData.position, eventData.pressEventCamera, out Vector2 current)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(MovementSpace, eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 previous))
                {
                    // Offsets are stored in parent coordinates. Canvas.scaleFactor alone does not
                    // account for a scaled HUD/inventory parent or a non-overlay canvas.
                    delta = current - previous;
                }

                if (ConvertDelta != null)
                    delta = ConvertDelta(delta);

                rawPosition += delta;
                previewPosition = ApplySnap(rawPosition);
                PreviewPosition?.Invoke(previewPosition);
            }

            public void OnEndDrag(PointerEventData eventData) => FinishDrag();

            private void OnDisable() => FinishDrag();

            private void FinishDrag()
            {
                if (!dragging)
                    return;

                dragging = false;
                CommitPosition?.Invoke(previewPosition);
            }

            private Vector2 ApplySnap(Vector2 position) => SnapPosition != null ? SnapPosition(position) : position;
        }

        // Only the drag surface is filtered. Other graphics/controls on the panel keep their normal
        // raycast behavior, and this transparent surface cannot intercept clicks without the drag key.
        internal sealed class DragSurface : MonoBehaviour, ICanvasRaycastFilter
        {
            internal DragHandle Handle;

            public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) =>
                Handle && Handle.isActiveAndEnabled && (Handle.IsDragging || Handle.CanDrag?.Invoke() == true);
        }

        internal static DragHandle Configure(
            GameObject handleObject,
            Func<bool> canDrag,
            Func<Vector2> getPosition,
            Action<Vector2> previewPosition,
            Action<Vector2> commitPosition,
            Func<Vector2, Vector2> convertDelta = null,
            Func<Vector2, Vector2> snapPosition = null,
            RectTransform movementSpace = null)
        {
            if (!handleObject)
                return null;

            DragHandle handle = handleObject.GetComponent<DragHandle>() ?? handleObject.AddComponent<DragHandle>();
            handle.CanDrag = canDrag;
            handle.GetPosition = getPosition;
            handle.PreviewPosition = previewPosition;
            handle.CommitPosition = commitPosition;
            handle.ConvertDelta = convertDelta;
            handle.SnapPosition = snapPosition;
            handle.MovementSpace = movementSpace ? movementSpace : handleObject.transform.parent as RectTransform;
            return handle;
        }

        internal static void SetRaycastSurface(DragHandle handle, Rect bounds, bool visible)
        {
            if (!handle || handle.transform is not RectTransform panel)
                return;

            if (!handle.RaycastSurface && visible)
            {
                GameObject surface = new GameObject("ExtraSlotsDragSurface", typeof(RectTransform), typeof(Image), typeof(DragSurface));
                surface.layer = handle.gameObject.layer;
                handle.RaycastSurface = surface.GetComponent<RectTransform>();
                handle.RaycastSurface.SetParent(panel, worldPositionStays: false);
                // Intercept a drag over the whole panel only while dragging is explicitly allowed.
                handle.RaycastSurface.SetAsLastSibling();
                Image image = surface.GetComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = true;
                surface.GetComponent<DragSurface>().Handle = handle;
            }

            if (!handle.RaycastSurface)
                return;

            handle.RaycastSurface.gameObject.SetActive(visible);
            if (!visible)
                return;

            // bounds is expressed relative to the panel's local origin, not its lower-left corner.
            handle.RaycastSurface.anchorMin = panel.pivot;
            handle.RaycastSurface.anchorMax = panel.pivot;
            handle.RaycastSurface.pivot = Vector2.zero;
            handle.RaycastSurface.anchoredPosition = bounds.min;
            handle.RaycastSurface.sizeDelta = bounds.size;
            handle.RaycastSurface.SetAsLastSibling();
        }

        internal static bool CanDrag(bool alwaysDraggable, KeyboardShortcut dragKey) => alwaysDraggable || IsShortcutHeld(dragKey);

        private static bool IsShortcutHeld(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !ZInput.GetKey(shortcut.MainKey))
                return false;

            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!ZInput.GetKey(modifier))
                    return false;

            return true;
        }
    }
}

using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

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

        internal static DragHandle Configure(
            GameObject handleObject,
            Func<bool> canDrag,
            Func<Vector2> getPosition,
            Action<Vector2> previewPosition,
            Action<Vector2> commitPosition,
            Func<Vector2, Vector2> convertDelta = null,
            Func<Vector2, Vector2> snapPosition = null)
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
            return handle;
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

        [HarmonyPatch]
        private static class QuickBars_ConfigureHotbarDragHandle_UseBarRoot
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(HotBars.QuickBars), "ConfigureHotbarDragHandle");

            private static void Prefix(HotkeyBar bar, ref GameObject handle)
            {
                // Pointer drag handlers are resolved through the clicked object's hierarchy. Attach
                // one handler to the whole hotbar instead of attaching handlers to individual slot
                // buttons; this makes the complete panel the drag surface and avoids competing item UI.
                if (bar)
                    handle = bar.gameObject;
            }
        }
    }
}

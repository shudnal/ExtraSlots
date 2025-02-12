using UnityEngine;

namespace ExtraSlots
{
    public static class RectTransformExtensions
    {
        public enum ElementAnchor
        {
            TopLeft,
            Top,
            TopRight,
            Right,
            BottomRight,
            Bottom,
            BottomLeft,
            Left,
            Middle
        }

        public static void SetAnchor(this RectTransform rectTransform, ElementAnchor anchor)
        {
            switch (anchor)
            {
                case ElementAnchor.TopLeft:
                    rectTransform.anchorMin = new Vector2(0f, 1f);
                    rectTransform.anchorMax = new Vector2(0f, 1f);
                    break;
                case ElementAnchor.Top:
                    rectTransform.anchorMin = new Vector2(0.5f, 1f);
                    rectTransform.anchorMax = new Vector2(0.5f, 1f);
                    break;
                case ElementAnchor.TopRight:
                    rectTransform.anchorMin = new Vector2(1f, 1f);
                    rectTransform.anchorMax = new Vector2(1f, 1f);
                    break;
                case ElementAnchor.Right:
                    rectTransform.anchorMin = new Vector2(1f, 0.5f);
                    rectTransform.anchorMax = new Vector2(1f, 0.5f);
                    break;
                case ElementAnchor.BottomRight:
                    rectTransform.anchorMin = new Vector2(1f, 0f);
                    rectTransform.anchorMax = new Vector2(1f, 0f);
                    break;
                case ElementAnchor.Bottom:
                    rectTransform.anchorMin = new Vector2(0.5f, 0f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0f);
                    break;
                case ElementAnchor.BottomLeft:
                    rectTransform.anchorMin = new Vector2(0f, 0f);
                    rectTransform.anchorMax = new Vector2(0f, 0f);
                    break;
                case ElementAnchor.Left:
                    rectTransform.anchorMin = new Vector2(0f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0f, 0.5f);
                    break;
                case ElementAnchor.Middle:
                    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(anchor), anchor, null);
            }
        }
    }
}

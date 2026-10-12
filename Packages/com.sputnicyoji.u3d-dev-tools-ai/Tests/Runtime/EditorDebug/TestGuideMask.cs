using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Tests
{
    /// 仿游戏引导遮罩：自定义 ICanvasRaycastFilter，除 Target 的屏幕矩形外全吃点击。
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TestGuideMask : MaskableGraphic, ICanvasRaycastFilter, IPointerClickHandler
    {
        private readonly Vector3[] m_Corners = new Vector3[4];
        public RectTransform Target;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (Target == null)
                return true;
            Target.GetWorldCorners(m_Corners);
            var hole = Rect.MinMaxRect(m_Corners[0].x, m_Corners[0].y, m_Corners[2].x, m_Corners[2].y);
            return !hole.Contains(screenPoint);
        }

        public void OnPointerClick(PointerEventData eventData) { }
    }
}

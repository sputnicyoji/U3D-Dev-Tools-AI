using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Yoji.EditorDebug.Tests
{
    public sealed class TestViewportProbe : MonoBehaviour
    {
        public int Width;
        public int Height;
        public GameObject Top;
        private void LateUpdate()
        {
            Width = Screen.width;
            Height = Screen.height;
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            if (EventSystem.current != null)
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) {
                    position = new Vector2(Width / 2f, Height / 2f)
                }, hits);
            Top = hits.Count == 0 ? null : hits[0].gameObject;
        }
    }
}

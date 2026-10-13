using UnityEngine;
using UnityEngine.EventSystems;

namespace Yoji.EditorDebug.Tests
{
    /// 记录 /ui/act 拖拽注入收到的事件序列；RevealOnBegin 仿"开始拖拽才出现放置区"。
    public sealed class TestDragReceiver : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public int Init, Begin, Drags, End, Clicks;
        public Vector2 Last;
        public GameObject RevealOnBegin;

        public void OnInitializePotentialDrag(PointerEventData eventData) => Init++;

        public void OnBeginDrag(PointerEventData eventData)
        {
            Begin++;
            if (RevealOnBegin != null)
                RevealOnBegin.SetActive(true);
        }

        public void OnDrag(PointerEventData eventData) { Drags++; Last = eventData.position; }
        public void OnEndDrag(PointerEventData eventData) => End++;
        public void OnPointerClick(PointerEventData eventData) => Clicks++;
    }
}

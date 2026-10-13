using UnityEngine;
using UnityEngine.EventSystems;

namespace Yoji.EditorDebug.Tests
{
    /// 记录悬停与放置：验证 /ui/act 的 enter/exit 沿父链传递、drop 落到松手点。
    public sealed class TestPointerRecorder : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IDropHandler
    {
        public int Enters, Exits, Drops;
        public Behaviour EnableOnDown;
        public Behaviour DisableOnDown;
        public System.Action GameUpdate;

        private void Update() => GameUpdate?.Invoke();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (EnableOnDown != null)
                EnableOnDown.enabled = true;
            if (DisableOnDown != null)
                DisableOnDown.enabled = false;
        }

        public void OnPointerEnter(PointerEventData eventData) => Enters++;
        public void OnPointerExit(PointerEventData eventData) => Exits++;
        public void OnDrop(PointerEventData eventData) => Drops++;
    }
}

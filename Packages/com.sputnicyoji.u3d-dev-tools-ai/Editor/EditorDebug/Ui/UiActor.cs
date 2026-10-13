using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Ui
{
    /// /ui/act：按语义 id 注入 uGUI 指针事件。注入前按 snapshot 同一套规则重算命中，
    /// 真人点不到或点了无效（被遮、离屏、处理者不可交互）一律拒绝并给 reason，不替 agent 绕过遮挡。
    /// 事件序列逐段照搬 StandaloneInputModule 的鼠标路径（ProcessMove -> ProcessMousePress -> ProcessDrag -> ReleaseMouse，
    /// 悬停照搬 BaseInputModule.HandlePointerExitAndEnter），最后补一次指针离开（等同手指抬起）。
    /// 不经 input module 与设备层，且同一帧内发完：长按、双击（clickCount 恒 1）、依赖帧间时间的处理不在模拟范围。
    internal static class UiActor
    {
        private const int k_DefaultDragSteps = 8;
        private const int k_MaxDragSteps = 120;
        // PointerInputModule.kMouseLeftId：Editor 里的真实点击就是这个指针
        private const int k_PointerId = -1;
        // EventSystem.pixelDragThreshold 的默认值；EditMode 测试里没有 EventSystem 时用它
        private const int k_DefaultDragThreshold = 10;

        /// <param name="live">true = 真实 Editor 请求：要求 Play 且有 EventSystem；EditMode 测试传 false。</param>
        public static JObject Act(JObject req, bool live)
        {
            string id = req.Value<string>("id");
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("ui/act requires 'id' (a node id from /ui/snapshot)");
            string action = req.Value<string>("action") ?? "click";
            if (action != "click" && action != "drag")
                throw new ArgumentException("ui/act 'action' must be click or drag, got '" + action + "'");
            Vector2? at = PointArg(req, "at");
            Vector2? to = PointArg(req, "to");
            if (action == "drag" && to == null)
                throw new ArgumentException("ui/act drag requires 'to': {x, y} (screen pixels, top-left origin)");
            int steps = IntArg(req, "steps") ?? k_DefaultDragSteps;
            if (steps < 1 || steps > k_MaxDragSteps)
                throw new ArgumentException("ui/act 'steps' must be 1.." + k_MaxDragSteps);
            int? iid = IntArg(req, "iid");

            var result = new JObject { ["id"] = id, ["action"] = action, ["acted"] = false };
            if (live && !EditorApplication.isPlaying)
                return Refuse(result, "not_playing", "act only runs in Play mode; Edit-mode clicks would fire persistent listeners on scene assets");
            var eventSystem = EventSystem.current;
            if (live && eventSystem == null)
                return Refuse(result, "no_event_system", "no active EventSystem: real input would not reach any UI either");

            Canvas.ForceUpdateCanvases();
            var ctx = new UiContext();
            var matches = Resolve(ctx, id);
            if (iid.HasValue)
                matches.RemoveAll(t => t.gameObject.GetInstanceID() != iid.Value);
            if (matches.Count == 0)
                return Refuse(result, "not_found", "no active object under a canvas has this id" + (iid.HasValue ? " and iid" : "") + "; take a fresh /ui/snapshot");
            if (matches.Count > 1)
                return Refuse(result, "ambiguous", matches.Count + " objects share this id (same-named roots in different scenes); pass the node's 'iid' from the same snapshot");
            var target = matches[0];

            if (!(target is RectTransform rect))
                return Refuse(result, "not_ui", "target has no RectTransform");
            var screen = ctx.ScreenRect(rect);
            // 对外坐标左上原点，事件里用 Unity 屏幕坐标（左下原点）
            var press = at.HasValue ? new Vector2(at.Value.x, ctx.Height - at.Value.y) : screen.center;
            result["at"] = PointJson(ctx, press);
            if (!ctx.OnScreen(press))
                return Refuse(result, "offscreen", "press point is outside the screen");
            if (at.HasValue && !screen.Contains(press))
                return Refuse(result, "outside_target", "'at' is outside the target's screen rect");

            var hits = ctx.Raycast.At(press);
            if (hits.Count == 0)
                return Refuse(result, "no_hit", "nothing receives a pointer at this point");
            var top = hits[0];
            var current = top.gameObject;
            result["top"] = ctx.IdOf(current.transform);
            if (current.transform != target && !current.transform.IsChildOf(target))
            {
                result["coveredBy"] = result["top"];
                return Refuse(result, "covered", "another control receives the pointer here");
            }

            // 处理者从命中对象往上冒泡解析，可交互性也查在真正接事件的那个 Selectable 上：
            // 点禁用按钮的子 Text，冒泡到的 Button.Press 会因 !IsInteractable 直接返回，等于没点
            var pressHandler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(current);
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(current);
            var dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(current);
            var receiver = action == "drag" ? dragHandler : (clickHandler != null ? clickHandler : pressHandler);
            if (receiver == null)
                return Refuse(result, action == "drag" ? "no_drag_handler" : "no_handler",
                    "no " + (action == "drag" ? "IDragHandler" : "pointer-down or click handler") + " on the hit object or its parents");
            var selectable = receiver.GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable())
            {
                result["receiver"] = ctx.IdOf(receiver.transform);
                return Refuse(result, "not_interactable", "the receiving Selectable is not interactable; a real touch would do nothing");
            }

            var data = new PointerEventData(eventSystem)
            {
                pointerId = k_PointerId,
                button = PointerEventData.InputButton.Left,
                position = press,
                pointerCurrentRaycast = top,
            };

            // ---- ProcessMove：指针来到按下点 ----
            HandlePointerExitAndEnter(data, current);

            // ---- ProcessMousePress ----
            data.eligibleForClick = true;
            data.delta = Vector2.zero;
            data.dragging = false;
            data.useDragThreshold = true;
            data.pressPosition = data.position;
            data.pointerPressRaycast = data.pointerCurrentRaycast;
            if (eventSystem != null && ExecuteEvents.GetEventHandler<ISelectHandler>(current) != eventSystem.currentSelectedGameObject)
                eventSystem.SetSelectedGameObject(null, data);
            var pressed = ExecuteEvents.ExecuteHierarchy(current, data, ExecuteEvents.pointerDownHandler);
            clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(current);
            dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(current);
            data.clickCount = 1;
            data.clickTime = Time.unscaledTime;
            data.pointerPress = pressed != null ? pressed : clickHandler;
            data.rawPointerPress = current;
            data.pointerClick = clickHandler;
            data.pointerDrag = dragHandler;
            if (dragHandler != null)
                ExecuteEvents.Execute(dragHandler, data, ExecuteEvents.initializePotentialDrag);

            bool dragged = false;
            if (action == "drag")
            {
                int threshold = eventSystem != null ? eventSystem.pixelDragThreshold : k_DefaultDragThreshold;
                current = Drag(data, press, new Vector2(to.Value.x, ctx.Height - to.Value.y), steps, threshold);
                dragged = data.dragging;
            }

            // ---- ReleaseMouse ----
            Canvas.ForceUpdateCanvases();
            var releaseHits = new UiRaycast().At(data.position);
            data.pointerCurrentRaycast = releaseHits.Count > 0 ? releaseHits[0] : default;
            current = data.pointerCurrentRaycast.gameObject;
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            var releaseClick = ExecuteEvents.GetEventHandler<IPointerClickHandler>(current);
            bool clicked = false;
            if (data.pointerClick == releaseClick && data.eligibleForClick)
                clicked = ExecuteEvents.Execute(data.pointerClick, data, ExecuteEvents.pointerClickHandler);
            if (data.pointerDrag != null && data.dragging)
                ExecuteEvents.ExecuteHierarchy(current, data, ExecuteEvents.dropHandler);
            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;
            data.pointerClick = null;
            if (data.pointerDrag != null && data.dragging)
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            data.dragging = false;
            data.pointerDrag = null;
            if (current != data.pointerEnter)
            {
                HandlePointerExitAndEnter(data, null);
                HandlePointerExitAndEnter(data, current);
            }
            // 手指抬起：鼠标会继续悬停，触屏不会；不留悬停态，免得按钮停在高亮
            HandlePointerExitAndEnter(data, null);

            result["acted"] = true;
            result["pressHandler"] = pressed != null ? ctx.IdOf(pressed.transform) : null;
            result["clickHandler"] = clicked ? ctx.IdOf(releaseClick.transform) : null;
            if (action == "drag")
            {
                result["dragHandler"] = dragHandler != null ? ctx.IdOf(dragHandler.transform) : null;
                result["dragged"] = dragged;
                result["to"] = PointJson(ctx, data.position);
                result["release"] = current != null ? ctx.IdOf(current.transform) : null;
            }
            return result;
        }

        /// 每步一帧的 ProcessMove + ProcessDrag。每步重建命中器：beginDrag/drag 里新开的放置区、浮层 canvas
        /// 要在下一步就能被命中，与真机下一帧的状态一致。返回最后一步最顶命中，松手前再次重算。
        private static GameObject Drag(PointerEventData data, Vector2 from, Vector2 to, int steps, int threshold)
        {
            GameObject current = data.pointerCurrentRaycast.gameObject;
            var last = from;
            for (int i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(from, to, (float)i / steps);
                data.delta = p - last;
                data.position = p;
                last = p;

                Canvas.ForceUpdateCanvases();
                var hits = new UiRaycast().At(p);
                data.pointerCurrentRaycast = hits.Count > 0 ? hits[0] : default;
                current = data.pointerCurrentRaycast.gameObject;
                HandlePointerExitAndEnter(data, current);

                if (data.pointerDrag == null)
                    continue;
                if (!data.dragging && ShouldStartDrag(data.pressPosition, p, threshold, data.useDragThreshold))
                {
                    ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                    data.dragging = true;
                }
                if (!data.dragging)
                    continue;
                if (data.pointerPress != data.pointerDrag)
                {
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                    data.eligibleForClick = false;
                    data.pointerPress = null;
                    data.rawPointerPress = null;
                }
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
            return current;
        }

        private static bool ShouldStartDrag(Vector2 pressPos, Vector2 currentPos, float threshold, bool useDragThreshold) =>
            !useDragThreshold || (pressPos - currentPos).sqrMagnitude >= threshold * threshold;

        /// 照搬 BaseInputModule.HandlePointerExitAndEnter（protected，拿不到实例），按其默认 sendPointerHoverToParent=true：
        /// 离开 / 进入事件沿父链发到公共根为止，hovered 同步维护。
        private static void HandlePointerExitAndEnter(PointerEventData data, GameObject newEnter)
        {
            if (newEnter == null || data.pointerEnter == null)
            {
                for (int i = 0; i < data.hovered.Count; i++)
                {
                    data.fullyExited = true;
                    ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerMoveHandler);
                    ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerExitHandler);
                }
                data.hovered.Clear();
                if (newEnter == null)
                {
                    data.pointerEnter = null;
                    return;
                }
            }

            if (data.pointerEnter == newEnter && newEnter)
            {
                if (data.IsPointerMoving())
                {
                    for (int i = 0; i < data.hovered.Count; i++)
                        ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerMoveHandler);
                }
                return;
            }

            var commonRoot = FindCommonRoot(data.pointerEnter, newEnter);
            if (data.pointerEnter != null)
            {
                var t = data.pointerEnter.transform;
                while (t != null)
                {
                    if (commonRoot != null && commonRoot.transform == t)
                        break;
                    data.fullyExited = t.gameObject != commonRoot && data.pointerEnter != newEnter;
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerMoveHandler);
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerExitHandler);
                    data.hovered.Remove(t.gameObject);
                    t = t.parent;
                    if (commonRoot != null && commonRoot.transform == t)
                        break;
                }
            }

            var oldEnter = data.pointerEnter;
            data.pointerEnter = newEnter;
            for (var t = newEnter.transform; t != null;)
            {
                data.reentered = t.gameObject == commonRoot && t.gameObject != oldEnter;
                if (data.reentered)
                    break;
                ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerMoveHandler);
                data.hovered.Add(t.gameObject);
                t = t.parent;
                if (commonRoot != null && commonRoot.transform == t)
                    break;
            }
        }

        private static GameObject FindCommonRoot(GameObject a, GameObject b)
        {
            if (a == null || b == null)
                return null;
            for (var t1 = a.transform; t1 != null; t1 = t1.parent)
            {
                for (var t2 = b.transform; t2 != null; t2 = t2.parent)
                {
                    if (t1 == t2)
                        return t1.gameObject;
                }
            }
            return null;
        }

        /// 场景里所有 canvas 子树中 id 等于给定值的对象。重名（不同场景同名根）时返回多个，由调用方用 iid 消歧或拒绝。
        internal static List<Transform> Resolve(UiContext ctx, string id)
        {
            var matches = new List<Transform>();
            foreach (var canvas in UiObserver.CollectLayers())
            {
                if (canvas.isRootCanvas)
                    Collect(ctx, canvas.transform, id, matches);
            }
            return matches;
        }

        private static void Collect(UiContext ctx, Transform t, string id, List<Transform> matches)
        {
            if (!t.gameObject.activeInHierarchy)
                return;
            // 前缀剪枝：子节点 id 必以父节点 id + "/" 开头
            string own = ctx.IdOf(t);
            if (own == id)
                matches.Add(t);
            else if (!id.StartsWith(own + "/", StringComparison.Ordinal))
                return;
            for (int i = 0; i < t.childCount; i++)
                Collect(ctx, t.GetChild(i), id, matches);
        }

        private static JObject Refuse(JObject result, string reason, string message)
        {
            result["reason"] = reason;
            result["message"] = message;
            return result;
        }

        private static JObject PointJson(UiContext ctx, Vector2 unityScreen) => new JObject
        {
            ["x"] = Mathf.RoundToInt(unityScreen.x),
            ["y"] = Mathf.RoundToInt(ctx.Height - unityScreen.y),
        };

        // JSON null 视同缺省；给了就必须是整数（数字或数字串），否则报出键名，不让 cast 异常裸奔
        private static int? IntArg(JObject req, string key)
        {
            var token = req[key];
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token.Type == JTokenType.Integer && (long)token >= int.MinValue && (long)token <= int.MaxValue)
                return (int)token;
            if (token.Type == JTokenType.String && int.TryParse((string)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;
            throw new ArgumentException("ui/act '" + key + "' must be an integer, got " + token.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static Vector2? PointArg(JObject req, string key)
        {
            var token = req[key];
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (!(token is JObject o))
                throw new ArgumentException("ui/act '" + key + "' must be {x, y} (screen pixels, top-left origin)");
            return new Vector2(Coordinate(o["x"], key + ".x"), Coordinate(o["y"], key + ".y"));
        }

        private static float Coordinate(JToken token, string name)
        {
            float value;
            if (token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
                value = (float)token;
            else if (token == null || token.Type != JTokenType.String
                || !float.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new ArgumentException("ui/act '" + name + "' must be a number");
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("ui/act '" + name + "' must be finite");
            return value;
        }
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Ui
{
    /// 语义 UI 观测（只读）：/ui/snapshot 与 /ui/hit。设计见仓库 docs/ui-observation-layer.md。
    /// 输出只含扁平 int/string/bool，不走 ResultSerializer（Vector3/Quaternion 会被展开成 KB 级）。
    internal static class UiObserver
    {
        private const int k_DefaultMax = 500;
        private const int k_MaxHits = 8;
        private const int k_MaxTextChars = 80;
        // 铺满判定阈值：遮罩可能挂在安全区面板下，不必严格等于全屏。
        internal const float BlockingCoverage = 0.8f;

        private static readonly List<IEventSystemHandler> s_Handlers = new List<IEventSystemHandler>();
        private static readonly List<Graphic> s_Graphics = new List<Graphic>();

        internal sealed class Node
        {
            public Transform Transform;
            public string Id;
            public string Type;
            public Rect Screen;
            public bool Visible;
            public bool Interactable;
            public bool RaycastTarget;
            public bool HitTop;
            public string CoveredBy;
            public string Text;
        }

        public static JObject Snapshot(JObject req)
        {
            Canvas.ForceUpdateCanvases();
            bool includeAll = string.Equals(req.Value<string>("include"), "all", StringComparison.OrdinalIgnoreCase);
            int max = Math.Max(1, IntArg(req, "max", k_DefaultMax));
            int offset = Math.Max(0, IntArg(req, "offset", 0));
            int step = Math.Max(4, IntArg(req, "step", UiGuideProbe.DefaultStep));

            var ctx = new UiContext();
            var layers = CollectLayers();
            var layerIndex = new Dictionary<Canvas, int>();
            for (int i = 0; i < layers.Count; i++)
                layerIndex[layers[i]] = i;

            var nodes = CollectNodes(ctx, layers, includeAll);
            var nodeSet = new HashSet<Transform>();
            foreach (var node in nodes)
                nodeSet.Add(node.Transform);
            foreach (var node in nodes)
                ResolveHit(ctx, node, nodeSet);

            var blocks = new bool[layers.Count];
            Graphic mask = null;
            int maskLayer = int.MaxValue;
            foreach (var graphic in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // 真实命中只经图元最近 canvas 上的 GraphicRaycaster；没有它的纯展示 canvas 不挡点击
                if (!graphic.isActiveAndEnabled || !graphic.raycastTarget || UiContext.Culled(graphic) || !ReceivesInput(graphic.canvas))
                    continue;
                UiContext.GroupState(graphic.transform, out _, out _, out bool blocksRaycasts);
                if (!blocksRaycasts || ctx.Coverage(ctx.ScreenRect(graphic.rectTransform)) < BlockingCoverage)
                    continue;
                int layer = LayerOf(graphic.transform, layerIndex);
                if (layer < 0)
                    continue;
                blocks[layer] = true;
                if (UiGuideProbe.IsHostFilter(graphic) && layer < maskLayer)
                {
                    mask = graphic;
                    maskLayer = layer;
                }
            }

            // topmost 给最上面能接收点击的层：引导手指这类纯展示的高序 canvas 不算
            var layersJson = new JArray();
            bool topmostTaken = false;
            for (int i = 0; i < layers.Count; i++)
            {
                var canvas = layers[i];
                bool receivesInput = ReceivesInput(canvas);
                layersJson.Add(new JObject
                {
                    ["id"] = ctx.IdOf(canvas.transform),
                    ["name"] = canvas.name,
                    ["sortingOrder"] = canvas.sortingOrder,
                    ["overrideSorting"] = canvas.overrideSorting,
                    ["renderMode"] = canvas.rootCanvas.renderMode.ToString(),
                    ["receivesInput"] = receivesInput,
                    ["blocksBelow"] = blocks[i],
                    ["topmost"] = receivesInput && !topmostTaken,
                });
                topmostTaken |= receivesInput;
            }

            var nodesJson = new JArray();
            for (int i = offset; i < nodes.Count && nodesJson.Count < max; i++)
                nodesJson.Add(NodeJson(ctx, nodes[i]));

            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            return new JObject
            {
                ["meta"] = new JObject
                {
                    ["frame"] = Time.frameCount,
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["playing"] = EditorApplication.isPlaying,
                    ["screen"] = new JObject { ["w"] = ctx.Width, ["h"] = ctx.Height },
                    ["eventSystemPresent"] = eventSystem != null,
                    ["currentSelected"] = selected != null ? ctx.IdOf(selected.transform) : null,
                },
                ["layers"] = layersJson,
                ["nodes"] = nodesJson,
                ["total"] = nodes.Count,
                ["offset"] = offset,
                ["truncated"] = offset + nodesJson.Count < nodes.Count,
                ["guide"] = UiGuideProbe.Probe(ctx, mask, nodes, step),
            };
        }

        public static JObject Hit(JObject req)
        {
            Canvas.ForceUpdateCanvases();
            float x = FloatArg(req, "x");
            float y = FloatArg(req, "y");
            var ctx = new UiContext();
            var hits = ctx.Raycast.At(new Vector2(x, ctx.Height - y));

            var hitsJson = new JArray();
            for (int i = 0; i < hits.Count && i < k_MaxHits; i++)
            {
                var go = hits[i].gameObject;
                hitsJson.Add(new JObject
                {
                    ["id"] = ctx.IdOf(go.transform),
                    ["type"] = TypeName(go.transform, null),
                    ["sortingOrder"] = hits[i].sortingOrder,
                    ["depth"] = hits[i].depth,
                });
            }

            var top = hits.Count > 0 ? hits[0].gameObject : null;
            var clickHandler = top != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(top) : null;
            return new JObject
            {
                ["x"] = x,
                ["y"] = y,
                ["screen"] = new JObject { ["w"] = ctx.Width, ["h"] = ctx.Height },
                ["top"] = top != null ? ctx.IdOf(top.transform) : null,
                // 点下去真正收到 click 的对象（命中的 Graphic 往上冒泡到的处理者）
                ["clickHandler"] = clickHandler != null ? ctx.IdOf(clickHandler.transform) : null,
                ["total"] = hits.Count,
                ["hits"] = hitsJson,
            };
        }

        /// 页 / 弹窗栈：根 canvas 与 overrideSorting 的嵌套 canvas，按有效绘制顺序排，首项最顶。
        internal static List<Canvas> CollectLayers()
        {
            var layers = new List<Canvas>();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas.isActiveAndEnabled && (canvas.isRootCanvas || canvas.overrideSorting))
                    layers.Add(canvas);
            }
            layers.Sort(CompareLayersTopFirst);
            return layers;
        }

        // ponytail: 未计 ScreenSpaceCamera 的 planeDistance 与同序 overlay 的引擎内部次序，以层级后者为上近似。
        private static int CompareLayersTopFirst(Canvas a, Canvas b)
        {
            var rootA = a.rootCanvas;
            var rootB = b.rootCanvas;
            bool overlayA = UiContext.DrawsAsOverlay(rootA);
            bool overlayB = UiContext.DrawsAsOverlay(rootB);
            if (overlayA != overlayB)
                return overlayA ? -1 : 1;
            if (!overlayA)
            {
                var cameraA = UiContext.EventCamera(rootA);
                var cameraB = UiContext.EventCamera(rootB);
                float depthA = cameraA != null ? cameraA.depth : float.MinValue;
                float depthB = cameraB != null ? cameraB.depth : float.MinValue;
                if (depthA != depthB)
                    return depthB.CompareTo(depthA);
            }
            int layerA = SortingLayer.GetLayerValueFromID(a.sortingLayerID);
            int layerB = SortingLayer.GetLayerValueFromID(b.sortingLayerID);
            if (layerA != layerB)
                return layerB.CompareTo(layerA);
            if (a.sortingOrder != b.sortingOrder)
                return b.sortingOrder.CompareTo(a.sortingOrder);
            return UiContext.CompareHierarchy(b.transform, a.transform);
        }

        private static bool ReceivesInput(Canvas canvas)
        {
            var raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
            return raycaster != null && raycaster.isActiveAndEnabled;
        }

        private static int LayerOf(Transform t, Dictionary<Canvas, int> layerIndex)
        {
            for (var x = t; x != null; x = x.parent)
            {
                var canvas = x.GetComponent<Canvas>();
                if (canvas != null && layerIndex.TryGetValue(canvas, out int index))
                    return index;
            }
            return -1;
        }

        /// 默认只收可交互控件（挂着启用的 IEventSystemHandler：Selectable、ScrollRect、自定义点击处理等）；
        /// include=all 再放开到全部 Graphic。顺序：根 canvas 按层序（顶先），canvas 内深度优先。
        private static List<Node> CollectNodes(UiContext ctx, List<Canvas> layers, bool includeAll)
        {
            var nodes = new List<Node>();
            foreach (var canvas in layers)
            {
                if (canvas.isRootCanvas)
                    Visit(ctx, canvas.transform, includeAll, nodes);
            }
            return nodes;
        }

        private static void Visit(UiContext ctx, Transform t, bool includeAll, List<Node> nodes)
        {
            if (!t.gameObject.activeInHierarchy)
                return;

            var graphic = t.GetComponent<Graphic>();
            var handler = FirstEnabledHandler(t);
            if (handler != null || (includeAll && graphic != null))
                nodes.Add(BuildNode(ctx, t, graphic, handler));

            for (int i = 0; i < t.childCount; i++)
                Visit(ctx, t.GetChild(i), includeAll, nodes);
        }

        private static Behaviour FirstEnabledHandler(Transform t)
        {
            t.GetComponents(s_Handlers);
            for (int i = 0; i < s_Handlers.Count; i++)
            {
                if (s_Handlers[i] is Behaviour behaviour && behaviour.isActiveAndEnabled)
                    return behaviour;
            }
            return null;
        }

        private static Node BuildNode(UiContext ctx, Transform t, Graphic graphic, Behaviour handler)
        {
            var selectable = t.GetComponent<Selectable>();
            UiContext.GroupState(t, out float alpha, out bool groupInteractable, out _);
            var rectTransform = t as RectTransform;
            return new Node
            {
                Transform = t,
                Id = ctx.IdOf(t),
                Type = TypeName(t, handler),
                Screen = rectTransform != null ? ctx.ScreenRect(rectTransform) : default,
                Visible = alpha > 0f && (graphic == null || graphic.enabled),
                Interactable = selectable != null ? selectable.IsInteractable() : groupInteractable,
                RaycastTarget = graphic != null && graphic.raycastTarget,
                Text = LabelOf(t, graphic),
            };
        }

        /// 对控件中心做一次命中：最顶命中落在控件自身或其子节点上即 hitTop；否则 coveredBy 给压在上面的控件。
        private static void ResolveHit(UiContext ctx, Node node, HashSet<Transform> nodeSet)
        {
            var center = node.Screen.center;
            if (!node.Visible || !ctx.OnScreen(center))
                return;
            var hits = ctx.Raycast.At(center);
            if (hits.Count == 0)
                return;
            var top = hits[0].gameObject.transform;
            node.HitTop = top == node.Transform || top.IsChildOf(node.Transform);
            if (!node.HitTop)
                node.CoveredBy = ctx.IdOf(OwnerOf(top, nodeSet));
        }

        private static Transform OwnerOf(Transform t, HashSet<Transform> nodeSet)
        {
            for (var x = t; x != null; x = x.parent)
            {
                if (nodeSet.Contains(x))
                    return x;
            }
            return t;
        }

        private static string TypeName(Transform t, Behaviour handler)
        {
            var selectable = t.GetComponent<Selectable>();
            if (selectable != null)
                return selectable.GetType().Name;
            if (handler != null)
                return handler.GetType().Name;
            var graphic = t.GetComponent<Graphic>();
            return graphic != null ? graphic.GetType().Name : "GameObject";
        }

        private static string LabelOf(Transform t, Graphic own)
        {
            string text = own != null ? UiContext.TextOf(own) : null;
            if (text == null)
            {
                t.GetComponentsInChildren(false, s_Graphics);
                for (int i = 0; i < s_Graphics.Count && text == null; i++)
                    text = UiContext.TextOf(s_Graphics[i]);
            }
            if (text != null && text.Length > k_MaxTextChars)
                text = text.Substring(0, k_MaxTextChars);
            return text;
        }

        private static JObject NodeJson(UiContext ctx, Node node) => new JObject
        {
            ["id"] = node.Id,
            ["iid"] = node.Transform.gameObject.GetInstanceID(),
            ["path"] = UiContext.FullPath(node.Transform),
            ["type"] = node.Type,
            ["text"] = node.Text,
            ["rect"] = ctx.RectJson(node.Screen),
            ["visible"] = node.Visible,
            ["interactable"] = node.Interactable,
            ["raycastTarget"] = node.RaycastTarget,
            ["hitTop"] = node.HitTop,
            ["coveredBy"] = node.CoveredBy,
        };

        // 缺省才回退默认值；给了但非法直接报错，免得笔误被静默吞掉
        private static int IntArg(JObject req, string key, int fallback)
        {
            var raw = req.Value<string>(key);
            if (raw == null)
                return fallback;
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
                throw new ArgumentException("'" + key + "' must be an integer, got '" + raw + "'");
            return value;
        }

        private static float FloatArg(JObject req, string key)
        {
            var raw = req.Value<string>(key);
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("ui/hit requires finite numeric '" + key + "' (screen pixels, top-left origin)");
            return value;
        }
    }
}

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Ui
{
    /// 引导抢占判定。host 的遮罩把洞与目标藏在私有字段里，这里只用公开的 ICanvasRaycastFilter 反推：
    /// 网格 + 各控件中心逐点问 IsRaycastLocationValid，返回 false（点击穿透）的点即洞。
    internal static class UiGuideProbe
    {
        internal const int DefaultStep = 24;

        /// 游戏自定义的点击过滤器才算引导遮罩。Image 本身实现 ICanvasRaycastFilter，全屏背景图不能被当成遮罩。
        internal static bool IsHostFilter(Graphic graphic)
        {
            if (!(graphic is ICanvasRaycastFilter))
                return false;
            var ns = graphic.GetType().Namespace ?? string.Empty;
            return !(ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro") || ns.StartsWith("Unity."));
        }

        /// 网格步长 step 决定 holeRect 精度（±step）；小于 step 的洞靠控件中心采样补上，targetId 不受步长影响。
        internal static JObject Probe(UiContext ctx, Graphic mask, List<UiObserver.Node> nodes, int step)
        {
            if (mask == null)
                return new JObject { ["active"] = false };

            var filter = (ICanvasRaycastFilter)mask;
            var camera = UiContext.EventCamera(mask.canvas);
            float half = step * 0.5f;
            var hole = new Rect();
            bool any = false;

            for (float y = half; y < ctx.Height; y += step)
            {
                for (float x = half; x < ctx.Width; x += step)
                {
                    if (!filter.IsRaycastLocationValid(new Vector2(x, y), camera))
                        Grow(ref hole, ref any, Rect.MinMaxRect(x - half, y - half, x + half, y + half));
                }
            }

            UiObserver.Node target = null;
            foreach (var node in nodes)
            {
                var center = node.Screen.center;
                if (!node.Visible || !ctx.OnScreen(center) || filter.IsRaycastLocationValid(center, camera))
                    continue;
                Grow(ref hole, ref any, new Rect(center, Vector2.zero));
                if (node.HitTop && (target == null || Area(node.Screen) < Area(target.Screen)))
                    target = node;
            }

            return new JObject
            {
                ["active"] = true,
                ["maskId"] = ctx.IdOf(mask.transform),
                ["step"] = step,
                // 全屏吃点击（无洞）时为 null；洞下无 uGUI 控件（世界空间目标）时 targetId 为 null 而 holeRect 有值
                ["holeRect"] = any ? ctx.RectJson(hole) : null,
                ["targetId"] = target != null ? target.Id : null,
            };
        }

        private static void Grow(ref Rect hole, ref bool any, Rect cell)
        {
            hole = any
                ? Rect.MinMaxRect(Mathf.Min(hole.xMin, cell.xMin), Mathf.Min(hole.yMin, cell.yMin), Mathf.Max(hole.xMax, cell.xMax), Mathf.Max(hole.yMax, cell.yMax))
                : cell;
            any = true;
        }

        private static float Area(Rect r) => r.width * r.height;
    }
}

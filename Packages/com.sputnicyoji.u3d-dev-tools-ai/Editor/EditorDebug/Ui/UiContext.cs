using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Ui
{
    /// 一次观测请求的共享状态：屏幕尺寸、命中器、语义 id 缓存与坐标换算。只在主线程构造与使用。
    internal sealed class UiContext
    {
        // TMP 不是本包依赖（2022 在 com.unity.textmeshpro，Unity 6 并入 ugui）；只读 text 一个属性，按名取一次。
        private static readonly Type s_TmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        private static readonly PropertyInfo s_TmpTextProperty = s_TmpTextType != null ? s_TmpTextType.GetProperty("text") : null;

        private static readonly List<CanvasGroup> s_Groups = new List<CanvasGroup>();
        private static readonly List<int> s_ChainA = new List<int>();
        private static readonly List<int> s_ChainB = new List<int>();

        public readonly int Width = Screen.width;
        public readonly int Height = Screen.height;
        public readonly UiRaycast Raycast = new UiRaycast();

        private readonly Dictionary<Transform, string> m_Ids = new Dictionary<Transform, string>();
        private readonly Vector3[] m_Corners = new Vector3[4];
        private readonly StringBuilder m_Builder = new StringBuilder();
        private readonly Dictionary<UnityEngine.SceneManagement.Scene, GameObject[]> m_SceneRoots = new Dictionary<UnityEngine.SceneManagement.Scene, GameObject[]>();

        /// 稳定语义 id：层级路径，同名兄弟（含同名场景根）才追加 [i]。
        /// 根 canvas 位于场景根时路径从它起（省掉无意义的前缀）；挂在别的节点下时（如世界空间点击区挂在各自的角色根下）
        /// 从场景根起，否则多个同名根 canvas 会撞成同一个 id。
        public string IdOf(Transform t)
        {
            if (t == null)
                return null;
            if (m_Ids.TryGetValue(t, out var id))
                return id;

            var canvas = t.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : null;
            var stop = root != null && root.parent == null ? root : null;
            m_Builder.Length = 0;
            AppendSegments(t, stop, m_Builder);
            id = m_Builder.ToString();
            m_Ids[t] = id;
            return id;
        }

        private void AppendSegments(Transform t, Transform stop, StringBuilder sb)
        {
            if (t != stop && t.parent != null)
            {
                AppendSegments(t.parent, stop, sb);
                sb.Append('/');
            }
            sb.Append(t.name);

            int index = t.parent != null ? SiblingIndex(t) : SceneRootIndex(t);
            if (index >= 0)
                sb.Append('[').Append(index).Append(']');
        }

        // 同名兄弟中的序号；无同名兄弟返回 -1
        private static int SiblingIndex(Transform t)
        {
            var parent = t.parent;
            int same = 0, index = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i);
                if (sibling.name != t.name)
                    continue;
                if (sibling == t)
                    index = same;
                same++;
            }
            return same > 1 ? index : -1;
        }

        private int SceneRootIndex(Transform t)
        {
            var scene = t.gameObject.scene;
            if (!m_SceneRoots.TryGetValue(scene, out var roots))
            {
                roots = scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects() : new GameObject[0];
                m_SceneRoots[scene] = roots;
            }
            int same = 0, index = 0;
            foreach (var go in roots)
            {
                if (go.name != t.name)
                    continue;
                if (go.transform == t)
                    index = same;
                same++;
            }
            return same > 1 ? index : -1;
        }

        /// 场景根起的完整层级路径（不带下标），给 /invoke 或人工定位用。
        public static string FullPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// RectTransform 的屏幕包围矩形，Unity 屏幕坐标（左下原点）。
        public Rect ScreenRect(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            var camera = canvas != null ? EventCamera(canvas) : null;
            rt.GetWorldCorners(m_Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, m_Corners[0]);
            Vector2 max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, m_Corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// 输出坐标统一为左上原点整数像素，与截图一致。
        public JObject RectJson(Rect r) => new JObject
        {
            ["x"] = Mathf.RoundToInt(r.xMin),
            ["y"] = Mathf.RoundToInt(Height - r.yMax),
            ["w"] = Mathf.RoundToInt(r.width),
            ["h"] = Mathf.RoundToInt(r.height),
        };

        public bool OnScreen(Vector2 p) => p.x >= 0f && p.y >= 0f && p.x < Width && p.y < Height;

        /// 矩形与屏幕交集占屏幕面积的比例。
        public float Coverage(Rect r)
        {
            if (Width <= 0 || Height <= 0)
                return 0f;
            float w = Mathf.Min(r.xMax, Width) - Mathf.Max(r.xMin, 0f);
            float h = Mathf.Min(r.yMax, Height) - Mathf.Max(r.yMin, 0f);
            return w <= 0f || h <= 0f ? 0f : w * h / ((float)Width * Height);
        }

        /// 按 overlay 绘制：ScreenSpaceCamera 没挂相机时 Unity 也走 overlay 通道。
        public static bool DrawsAsOverlay(Canvas root) =>
            root.renderMode == RenderMode.ScreenSpaceOverlay
            || (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera == null);

        /// 不参与 uGUI 绘制与命中。Graphic 本身不 RequireComponent CanvasRenderer：世界空间 TMPro.TextMeshPro
        /// 走 MeshRenderer、没有 CanvasRenderer，直接读 canvasRenderer.cull 会抛 MissingComponentException。
        public static bool Culled(Graphic graphic) =>
            !graphic.TryGetComponent(out CanvasRenderer renderer) || renderer.cull;

        /// 与 GraphicRaycaster.eventCamera 同规则；嵌套 canvas 继承根 canvas 的渲染模式与相机。
        public static Camera EventCamera(Canvas canvas)
        {
            var root = canvas.rootCanvas;
            if (root.renderMode == RenderMode.ScreenSpaceOverlay
                || (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera == null))
                return null;
            return root.worldCamera != null ? root.worldCamera : Camera.main;
        }

        /// single-screen API 只观测主显示器，显示器选择与 GraphicRaycaster 一致。
        public static bool OnPrimaryDisplay(Canvas canvas)
        {
            var camera = EventCamera(canvas);
            return (camera != null ? camera.targetDisplay : canvas.targetDisplay) == 0;
        }

        /// CanvasGroup 链遇 ignoreParentGroups 截断；overrideSorting 只截断父级 blocksRaycasts。
        public static void GroupState(Transform t, out float alpha, out bool interactable, out bool blocksRaycasts)
        {
            alpha = 1f;
            interactable = true;
            blocksRaycasts = true;
            bool collectRaycasts = true;
            for (var x = t; x != null; x = x.parent)
            {
                x.GetComponents(s_Groups);
                bool stop = false;
                for (int i = 0; i < s_Groups.Count; i++)
                {
                    var group = s_Groups[i];
                    if (!group.enabled)
                        continue;
                    alpha *= group.alpha;
                    interactable &= group.interactable;
                    if (collectRaycasts)
                        blocksRaycasts &= group.blocksRaycasts;
                    stop |= group.ignoreParentGroups;
                }
                if (stop)
                    break;
                var canvas = x.GetComponent<Canvas>();
                if (canvas != null && canvas.overrideSorting)
                    collectRaycasts = false;
            }
        }

        public static string TextOf(Graphic graphic)
        {
            if (graphic is Text text)
                return text.text;
            if (s_TmpTextProperty != null && s_TmpTextType.IsInstanceOfType(graphic))
                return s_TmpTextProperty.GetValue(graphic) as string;
            return null;
        }

        /// 层级先后：a 在 b 之前返回负数。跨场景按加载序（DontDestroyOnLoad 不在列表里，排最后），同场景按逐级 sibling index 字典序。
        public static int CompareHierarchy(Transform a, Transform b)
        {
            var sceneA = a.gameObject.scene;
            var sceneB = b.gameObject.scene;
            if (sceneA != sceneB)
                return SceneOrder(sceneA).CompareTo(SceneOrder(sceneB));
            Chain(a, s_ChainA);
            Chain(b, s_ChainB);
            int n = Mathf.Min(s_ChainA.Count, s_ChainB.Count);
            for (int i = 0; i < n; i++)
            {
                if (s_ChainA[i] != s_ChainB[i])
                    return s_ChainA[i].CompareTo(s_ChainB[i]);
            }
            return s_ChainA.Count.CompareTo(s_ChainB.Count);
        }

        // Scene.handle 在 Unity 6.3 起不再是 int，不能拿来比
        private static int SceneOrder(UnityEngine.SceneManagement.Scene scene)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i) == scene)
                    return i;
            }
            return int.MaxValue;
        }

        private static void Chain(Transform t, List<int> chain)
        {
            chain.Clear();
            for (var x = t; x != null; x = x.parent)
                chain.Add(x.GetSiblingIndex());
            chain.Reverse();
        }
    }
}

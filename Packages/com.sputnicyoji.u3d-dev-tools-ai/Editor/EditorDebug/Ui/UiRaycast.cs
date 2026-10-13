using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Ui
{
    /// 屏幕点命中：EventSystem.RaycastAll 的 uGUI 子集，结果按真实点击顺序排好（首项最顶）。
    /// 不调 RaycastAll / GraphicRaycaster.Raycast，两处都依赖渲染副产物：RaycasterManager 只收 OnEnable 过的
    /// raycaster（BaseRaycaster 非 ExecuteAlways），GraphicRaycaster 跳过 depth==-1（canvas 未渲染）的图元；
    /// 编辑模式与 batchmode 下两者都是空的。这里照搬 GraphicRaycaster 的逐图元过滤，depth 换成根 canvas 下的
    /// 深度优先序（与 absoluteDepth 同义：层级里后画者在上）。只算 GraphicRaycaster；
    /// PhysicsRaycaster 的世界命中与 blockingObjects 物理遮挡不在观测范围。
    internal sealed class UiRaycast
    {
        private readonly List<GraphicRaycaster> m_Raycasters = new List<GraphicRaycaster>();
        private readonly List<RaycastResult> m_Results = new List<RaycastResult>();
        private readonly Dictionary<Graphic, int> m_Order = new Dictionary<Graphic, int>();

        public UiRaycast()
        {
            var graphics = new List<Graphic>();
            var numbered = new HashSet<Canvas>();
            foreach (var raycaster in Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!raycaster.IsActive())
                    continue;
                var canvas = raycaster.GetComponent<Canvas>();
                if (canvas == null || !UiContext.OnPrimaryDisplay(canvas))
                    continue;
                m_Raycasters.Add(raycaster);
                // 根 canvas 自己没有 raycaster 时，嵌套弹窗的 raycaster 也要给整棵根树编号
                var root = canvas.rootCanvas;
                if (root == null || !numbered.Add(root))
                    continue;
                // GetComponentsInChildren 返回先序遍历，即 uGUI 的绘制顺序
                root.GetComponentsInChildren(false, graphics);
                for (int i = 0; i < graphics.Count; i++)
                    m_Order[graphics[i]] = i;
            }
        }

        /// 参数是 Unity 屏幕坐标（左下原点）。返回的列表被下次调用复用。
        public List<RaycastResult> At(Vector2 unityScreenPoint)
        {
            m_Results.Clear();
            for (int i = 0; i < m_Raycasters.Count; i++)
                Raycast(m_Raycasters[i], unityScreenPoint);
            m_Results.Sort(Compare);
            return m_Results;
        }

        private void Raycast(GraphicRaycaster raycaster, Vector2 position)
        {
            var canvas = raycaster.GetComponent<Canvas>();
            if (canvas == null)
                return;
            var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
            if (graphics == null || graphics.Count == 0)
                return;

            var camera = raycaster.eventCamera;
            var viewport = camera != null
                ? (Vector2)camera.ScreenToViewportPoint(position)
                : new Vector2(position.x / Screen.width, position.y / Screen.height);
            if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                return;
            var ray = camera != null ? camera.ScreenPointToRay(position) : new Ray();

            for (int i = 0; i < graphics.Count; i++)
            {
                var graphic = graphics[i];
                if (!graphic.raycastTarget || UiContext.Culled(graphic))
                    continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, position, camera, graphic.raycastPadding))
                    continue;
                if (camera != null && camera.WorldToScreenPoint(graphic.rectTransform.position).z > camera.farClipPlane)
                    continue;
                if (!graphic.Raycast(position, camera))
                    continue;

                var trans = graphic.transform;
                if (raycaster.ignoreReversedGraphics && !FacesCamera(trans, camera))
                    continue;
                float distance = 0f;
                if (camera != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    distance = Vector3.Dot(trans.forward, trans.position - ray.origin) / Vector3.Dot(trans.forward, ray.direction);
                    if (distance < 0f)
                        continue;
                }

                m_Results.Add(new RaycastResult
                {
                    gameObject = graphic.gameObject,
                    module = raycaster,
                    distance = distance,
                    screenPosition = position,
                    index = m_Results.Count,
                    depth = m_Order.TryGetValue(graphic, out int order) ? order : -1,
                    sortingLayer = canvas.sortingLayerID,
                    sortingOrder = canvas.sortingOrder,
                });
            }
        }

        private static bool FacesCamera(Transform trans, Camera camera)
        {
            if (camera == null)
                return Vector3.Dot(Vector3.forward, trans.rotation * Vector3.forward) > 0f;
            var cameraForward = camera.transform.rotation * Vector3.forward * camera.nearClipPlane;
            return Vector3.Dot(trans.position - camera.transform.position - cameraForward, trans.forward) >= 0f;
        }

        // 镜像 EventSystem.RaycastComparer（private）的 uGUI 分支；ugui 改了比较规则时这里要跟。
        private static int Compare(RaycastResult lhs, RaycastResult rhs)
        {
            if (lhs.module != rhs.module)
            {
                var lhsCamera = lhs.module.eventCamera;
                var rhsCamera = rhs.module.eventCamera;
                if (lhsCamera != null && rhsCamera != null && lhsCamera.depth != rhsCamera.depth)
                    return rhsCamera.depth.CompareTo(lhsCamera.depth);
                if (lhs.module.sortOrderPriority != rhs.module.sortOrderPriority)
                    return rhs.module.sortOrderPriority.CompareTo(lhs.module.sortOrderPriority);
                if (lhs.module.renderOrderPriority != rhs.module.renderOrderPriority)
                    return rhs.module.renderOrderPriority.CompareTo(lhs.module.renderOrderPriority);
            }

            if (lhs.sortingLayer != rhs.sortingLayer)
                return SortingLayer.GetLayerValueFromID(rhs.sortingLayer).CompareTo(SortingLayer.GetLayerValueFromID(lhs.sortingLayer));
            if (lhs.sortingOrder != rhs.sortingOrder)
                return rhs.sortingOrder.CompareTo(lhs.sortingOrder);
            // depth 只在同一根 canvas 内可比（ugui case 912396）
            if (lhs.depth != rhs.depth && lhs.module.rootRaycaster == rhs.module.rootRaycaster)
                return rhs.depth.CompareTo(lhs.depth);
            if (lhs.distance != rhs.distance)
                return lhs.distance.CompareTo(rhs.distance);
            return lhs.index.CompareTo(rhs.index);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yoji.EditorDebug.Ui
{
    /// /ui/act 的世界目标（Phase 3）：按场景路径 + instanceId 指定世界物体，用目标相机 WorldToScreenPoint 现算屏幕点，
    /// 经游戏注册的 IScriptedPointer 逐帧喂 down / move / up（以及滚轮、双指），让游戏自己的 tap 判定、拖动阈值、
    /// UI 遮挡判定原样跑。点击结束后以游戏上报的实际命中为准（没有上报才退回物理射线），与目标不一致判失败。
    ///
    /// 帧规则：每个计划帧只在 Time.frameCount 前进后 Apply 一次，Down 与 Up 各只占一帧（下一帧分别是 Held / None）。
    /// 编辑器暂停时帧号不动，计划随之停住，不会把一个 Down 喂两帧造成双击。
    internal static class UiWorldActor
    {
        private const int k_DefaultDragSteps = 8;
        private const int k_DefaultPinchSteps = 8;
        private const int k_MaxSteps = 120;
        private const float k_DefaultPinchSpan = 200f;

        private static Session s_Active;

        internal static Session Active => s_Active;

        /// 带 target，或不带 id 只带 at（屏幕点上的拖动 / 滚轮 / 双指）时走世界路径。
        public static bool IsWorldRequest(JObject req)
        {
            if (!IsAbsent(req["target"]))
                return true;
            return IsAbsent(req["id"]) && !IsAbsent(req["at"]);
        }

        [InitializeOnLoadMethod]
        private static void Install()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        private static void Tick()
        {
            s_Active?.Step();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                s_Active?.Cancel("play_mode_exited", "Play mode ended before the gesture finished");
        }

        private static void OnBeforeReload()
        {
            s_Active?.Cancel("domain_reload", "a domain reload started before the gesture finished");
        }

        /// 主线程：校验、解析目标、排好逐帧计划并接管指针。返回值非 null 表示已拒绝（或参数错由异常抛出），
        /// 此时 session 为 null；返回 null 时 session 已开始，等 session.Done 后取 session.Result。
        /// <param name="live">true = 真实 Editor 请求（要求 Play，帧号取 Time.frameCount）；测试传 false 并给 clock。</param>
        public static JObject Start(JObject req, bool live, IScriptedPointer pointer, Func<int> clock, out Session session)
        {
            session = null;
            string action = req.Value<string>("action") ?? "click";
            if (action != "click" && action != "drag" && action != "scroll" && action != "pinch")
                throw new ArgumentException("ui/act world 'action' must be click, drag, scroll or pinch, got '" + action + "'");

            var targetToken = req["target"];
            string path = null;
            int? iid = null;
            if (!IsAbsent(targetToken))
            {
                if (!(targetToken is JObject targetObj))
                    throw new ArgumentException("ui/act 'target' must be {path, iid}");
                path = targetObj.Value<string>("path");
                iid = IntArg(targetObj, "iid", "target.iid");
                if (string.IsNullOrEmpty(path) && !iid.HasValue)
                    throw new ArgumentException("ui/act 'target' needs 'path' (scene path from the hierarchy) and/or 'iid'");
            }
            bool hasTarget = path != null || iid.HasValue;
            Vector2? at = PointArg(req, "at");
            if (hasTarget && at.HasValue)
                throw new ArgumentException("ui/act takes either 'target' or 'at', not both");
            if (!hasTarget && !at.HasValue)
                throw new ArgumentException("ui/act world gesture needs 'target' or 'at'");
            if (action == "click" && !hasTarget)
                throw new ArgumentException("ui/act click on the world needs 'target': the hit is checked against it");

            Vector2? to = PointArg(req, "to");
            Vector2? by = PointArg(req, "by");
            if (action == "drag" && to.HasValue == by.HasValue)
                throw new ArgumentException("ui/act drag needs exactly one of 'to' {x,y} or 'by' {x,y} (screen pixels, top-left origin)");
            float scroll = FloatArg(req, "scroll") ?? 0f;
            if (action == "scroll" && scroll == 0f)
                throw new ArgumentException("ui/act scroll needs a non-zero 'scroll' (wheel notches, positive = away from the user)");
            float scale = FloatArg(req, "scale") ?? 0f;
            if (action == "pinch" && !(scale > 0f))
                throw new ArgumentException("ui/act pinch needs 'scale' > 0 (finger span end / start; > 1 spreads the fingers)");
            float span = FloatArg(req, "span") ?? k_DefaultPinchSpan;
            if (span <= 0f)
                throw new ArgumentException("ui/act 'span' must be > 0");
            int defaultSteps = action == "drag" ? k_DefaultDragSteps : action == "pinch" ? k_DefaultPinchSteps : 1;
            int steps = IntArg(req, "steps", "steps") ?? defaultSteps;
            if (steps < 1 || steps > k_MaxSteps)
                throw new ArgumentException("ui/act 'steps' must be 1.." + k_MaxSteps);
            string cameraPath = req.Value<string>("camera");

            var result = new JObject { ["action"] = action, ["acted"] = false };
            if (hasTarget)
                result["target"] = new JObject { ["path"] = path, ["iid"] = iid };
            if (live && !EditorApplication.isPlaying)
                return Refuse(result, "not_playing", "act only runs in Play mode");
            if (pointer == null)
                return Refuse(result, "no_scripted_pointer", "the game registered no IScriptedPointer (ScriptedPointerRegistry.Register); world input cannot be driven");
            if (s_Active != null)
                return Refuse(result, "busy", "another world gesture is still running");

            Canvas.ForceUpdateCanvases();
            var ctx = new UiContext();

            Transform target = null;
            if (hasTarget)
            {
                string why = ResolveTarget(ctx, path, iid, out target, out string reason);
                if (target == null)
                    return Refuse(result, reason, why);
                result["target"] = TransformJson(ctx, target);
            }

            Camera camera;
            if (!string.IsNullOrEmpty(cameraPath))
            {
                var found = FindByPath(ctx, cameraPath);
                camera = found.Count == 1 ? found[0].GetComponent<Camera>() : null;
                if (camera == null || !camera.isActiveAndEnabled)
                    return Refuse(result, "no_camera", "'camera' does not name exactly one active Camera");
            }
            else
            {
                camera = PickCamera(target, at.HasValue ? new Vector2(at.Value.x, ctx.Height - at.Value.y) : (Vector2?)null, ctx);
                if (camera == null)
                    return Refuse(result, "no_camera", "no active camera renders the target's layer");
            }
            result["camera"] = ctx.IdOf(camera.transform);

            Vector2 press;
            if (target != null)
            {
                var world = AimPoint(target);
                var screen = camera.WorldToScreenPoint(world);
                result["world"] = new JObject { ["x"] = world.x, ["y"] = world.y, ["z"] = world.z };
                if (screen.z <= camera.nearClipPlane * 0.5f || screen.z <= 0f)
                    return Refuse(result, "behind_camera", "the target is behind the camera");
                press = new Vector2(screen.x, screen.y);
                result["at"] = PointJson(ctx, press);
                if (screen.z > camera.farClipPlane)
                    return Refuse(result, "offscreen", "the target is beyond the camera's far clip plane");
            }
            else
            {
                press = new Vector2(at.Value.x, ctx.Height - at.Value.y);
                result["at"] = PointJson(ctx, press);
            }
            if (!OnScreen(ctx, camera, press))
                return Refuse(result, "offscreen", "the point is outside the screen or the camera's viewport");

            // 与游戏的遮挡判定同口径：只看 GraphicRaycaster；被 UI 挡住时游戏会把这一下判给 UI
            var hits = ctx.Raycast.At(press);
            if (hits.Count > 0)
            {
                result["coveredBy"] = ctx.IdOf(hits[0].gameObject.transform);
                return Refuse(result, "covered", "a UI graphic receives the pointer here; the game would give this press to the UI");
            }

            List<ScriptedPointerFrame> frames;
            switch (action)
            {
                case "click":
                    frames = ClickFrames(press);
                    break;
                case "drag":
                {
                    var end = to.HasValue ? new Vector2(to.Value.x, ctx.Height - to.Value.y) : press + new Vector2(by.Value.x, -by.Value.y);
                    if (!OnScreen(ctx, camera, end))
                        return Refuse(result, "offscreen", "the drag end point is outside the screen");
                    result["to"] = PointJson(ctx, end);
                    frames = DragFrames(press, end, steps);
                    break;
                }
                case "scroll":
                    frames = ScrollFrames(press, scroll, steps);
                    break;
                default:
                {
                    frames = PinchFrames(press, span, scale, steps);
                    foreach (var frame in frames)
                    {
                        foreach (var touch in frame.Touches)
                        {
                            if (!OnScreen(ctx, camera, touch.position))
                                return Refuse(result, "offscreen", "a finger of the pinch leaves the screen; lower 'span' or 'scale'");
                        }
                    }
                    result["span"] = new JObject { ["from"] = span, ["to"] = span * scale };
                    break;
                }
            }

            session = new Session(result, pointer, frames, target, camera, press, action == "click", clock ?? (() => Time.frameCount));
            s_Active = session;
            session.Begin();
            return null;
        }

        // —— 计划 ——

        internal static List<ScriptedPointerFrame> ClickFrames(Vector2 p) => new List<ScriptedPointerFrame>
        {
            Mouse(ScriptedPointerPhase.Down, p),
            Mouse(ScriptedPointerPhase.Held, p),
            Mouse(ScriptedPointerPhase.Up, p),
            Mouse(ScriptedPointerPhase.None, p),
        };

        internal static List<ScriptedPointerFrame> DragFrames(Vector2 from, Vector2 to, int steps)
        {
            var frames = new List<ScriptedPointerFrame> { Mouse(ScriptedPointerPhase.Down, from) };
            for (int i = 1; i <= steps; i++)
                frames.Add(Mouse(ScriptedPointerPhase.Held, Vector2.Lerp(from, to, (float)i / steps)));
            frames.Add(Mouse(ScriptedPointerPhase.Up, to));
            frames.Add(Mouse(ScriptedPointerPhase.None, to));
            return frames;
        }

        internal static List<ScriptedPointerFrame> ScrollFrames(Vector2 p, float notches, int steps)
        {
            var frames = new List<ScriptedPointerFrame>();
            for (int i = 0; i < steps; i++)
            {
                var f = Mouse(ScriptedPointerPhase.None, p);
                f.Scroll = new Vector2(0f, notches / steps);
                frames.Add(f);
            }
            frames.Add(Mouse(ScriptedPointerPhase.None, p));
            return frames;
        }

        /// 两指沿水平线对称分开（scale>1）或合拢，中点固定在 p。Began 一帧、Moved steps 帧、Ended 一帧，再一帧无触摸。
        internal static List<ScriptedPointerFrame> PinchFrames(Vector2 p, float span, float scale, int steps)
        {
            var frames = new List<ScriptedPointerFrame>();
            float start = span * 0.5f, end = span * scale * 0.5f;
            Vector2 a = p + Vector2.left * start, b = p + Vector2.right * start;
            frames.Add(Touches(p, Finger(0, a, Vector2.zero, TouchPhase.Began), Finger(1, b, Vector2.zero, TouchPhase.Began)));
            for (int i = 1; i <= steps; i++)
            {
                float half = Mathf.Lerp(start, end, (float)i / steps);
                Vector2 na = p + Vector2.left * half, nb = p + Vector2.right * half;
                frames.Add(Touches(p, Finger(0, na, na - a, TouchPhase.Moved), Finger(1, nb, nb - b, TouchPhase.Moved)));
                a = na;
                b = nb;
            }
            frames.Add(Touches(p, Finger(0, a, Vector2.zero, TouchPhase.Ended), Finger(1, b, Vector2.zero, TouchPhase.Ended)));
            frames.Add(Mouse(ScriptedPointerPhase.None, p));
            return frames;
        }

        private static ScriptedPointerFrame Mouse(ScriptedPointerPhase phase, Vector2 p) =>
            new ScriptedPointerFrame { Phase = phase, Position = p, Scroll = Vector2.zero, Touches = Array.Empty<Touch>() };

        private static ScriptedPointerFrame Touches(Vector2 p, params Touch[] touches) =>
            new ScriptedPointerFrame { Phase = ScriptedPointerPhase.None, Position = p, Scroll = Vector2.zero, Touches = touches };

        private static Touch Finger(int id, Vector2 position, Vector2 delta, TouchPhase phase) => new Touch
        {
            fingerId = id,
            position = position,
            rawPosition = position,
            deltaPosition = delta,
            phase = phase,
            tapCount = 1,
            type = TouchType.Direct,
            pressure = 1f,
            maximumPossiblePressure = 1f,
        };

        // —— 会话 ——

        internal sealed class Session
        {
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public JObject Result => m_Result;

            private readonly JObject m_Result;
            private readonly IScriptedPointer m_Pointer;
            private readonly List<ScriptedPointerFrame> m_Frames;
            private readonly Transform m_Target;
            private readonly Camera m_Camera;
            private readonly Vector2 m_Press;
            private readonly bool m_Verify;
            private readonly Func<int> m_Clock;
            private int m_Index;
            private int m_LastFrame;
            private bool m_Captured;
            private bool m_Reported;
            private GameObject m_ReportedHit;
            private bool m_Finished;

            public int FrameCount => m_Frames.Count;

            internal Session(JObject result, IScriptedPointer pointer, List<ScriptedPointerFrame> frames, Transform target, Camera camera, Vector2 press, bool verify, Func<int> clock)
            {
                m_Result = result;
                m_Pointer = pointer;
                m_Frames = frames;
                m_Target = target;
                m_Camera = camera;
                m_Press = press;
                m_Verify = verify;
                m_Clock = clock;
            }

            internal void Begin()
            {
                m_Pointer.Begin();
                // 开始这一帧不喂：本帧的游戏 Update 可能已跑完一半，下一个帧号才是第一帧
                m_LastFrame = m_Clock();
            }

            /// 每次 editor tick 调用；帧号没前进就什么都不做，所以每个计划帧恰好被游戏读一帧。
            internal void Step()
            {
                if (m_Finished)
                    return;
                int frame = m_Clock();
                if (frame == m_LastFrame)
                    return;
                m_LastFrame = frame;

                if (m_Index > 0 && m_Frames[m_Index - 1].Phase == ScriptedPointerPhase.Up && !m_Captured)
                    Capture();
                if (m_Index < m_Frames.Count)
                {
                    m_Pointer.Apply(m_Frames[m_Index]);
                    m_Index++;
                    return;
                }
                if (!m_Captured)
                    Capture();
                Finish();
            }

            // Up 那一帧的游戏 Update 已跑完：游戏的 tap 处理是同步的，命中此时已上报
            private void Capture()
            {
                m_Captured = true;
                m_Reported = m_Pointer.TryGetReportedHit(out m_ReportedHit);
            }

            internal void Cancel(string reason, string message)
            {
                if (m_Finished)
                    return;
                m_Result["framesApplied"] = m_Index;
                End();
                Refuse(m_Result, reason, message);
            }

            private void Finish()
            {
                m_Result["acted"] = true;
                m_Result["framesApplied"] = m_Index;
                if (m_Verify)
                    Verify();
                End();
            }

            private void End()
            {
                m_Finished = true;
                if (ReferenceEquals(s_Active, this))
                    s_Active = null;
                try { m_Pointer.End(); }
                finally { Done.Set(); }
            }

            private void Verify()
            {
                var ctx = new UiContext();
                GameObject hit = m_ReportedHit;
                string source = "game";
                if (!m_Reported)
                {
                    hit = PhysicsHit(m_Camera, m_Press);
                    source = hit != null ? "physics" : "none";
                }
                m_Result["hitSource"] = source;
                m_Result["hit"] = hit != null ? TransformJson(ctx, hit.transform) : null;
                if (!m_Reported && hit == null)
                {
                    m_Result["verified"] = false;
                    Refuse(m_Result, "unverified", "the game reported no hit and no collider is under the point; the click was sent but its effect is unknown");
                    return;
                }
                bool match = hit != null && m_Target != null && (hit.transform == m_Target || hit.transform.IsChildOf(m_Target));
                m_Result["verified"] = match;
                if (!match)
                    Refuse(m_Result, "hit_mismatch", hit == null
                        ? "the game's tap hit nothing"
                        : "the game's tap hit another object (see 'hit')");
            }
        }

        // —— 解析 ——

        private static string ResolveTarget(UiContext ctx, string path, int? iid, out Transform target, out string reason)
        {
            target = null;
            reason = null;
            if (iid.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(iid.Value);
                var t = obj is GameObject go ? go.transform : obj is Component c ? c.transform : null;
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    reason = "not_found";
                    return "no active object has this iid; look it up again";
                }
                if (!string.IsNullOrEmpty(path) && ctx.IdOf(t) != path)
                {
                    reason = "stale";
                    return "the iid now names '" + ctx.IdOf(t) + "', not the given path; look it up again";
                }
                target = t;
            }
            else
            {
                var matches = FindByPath(ctx, path);
                if (matches.Count == 0)
                {
                    reason = "not_found";
                    return "no active object has this path";
                }
                if (matches.Count > 1)
                {
                    reason = "ambiguous";
                    return matches.Count + " objects share this path; pass 'iid'";
                }
                target = matches[0];
            }
            if (target is RectTransform && target.GetComponentInParent<Canvas>() != null)
            {
                target = null;
                reason = "not_world";
                return "the target is a UI control under a canvas; use /ui/act with its snapshot 'id'";
            }
            return null;
        }

        internal static List<Transform> FindByPath(UiContext ctx, string path)
        {
            var matches = new List<Transform>();
            foreach (var root in Roots())
                Collect(ctx, root.transform, path, matches);
            return matches;
        }

        private static IEnumerable<GameObject> Roots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var go in scene.GetRootGameObjects())
                    yield return go;
            }
            if (!Application.isPlaying)
                yield break;
            // DontDestroyOnLoad 场景拿不到句柄，借一个临时物体取它的根
            var probe = new GameObject("__u3d_ddol_probe") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(probe);
            var ddol = probe.scene.GetRootGameObjects();
            UnityEngine.Object.DestroyImmediate(probe);
            foreach (var go in ddol)
            {
                if (go != null && go != probe)
                    yield return go;
            }
        }

        private static void Collect(UiContext ctx, Transform t, string path, List<Transform> matches)
        {
            if (!t.gameObject.activeInHierarchy)
                return;
            string own = ctx.IdOf(t);
            if (own == path)
                matches.Add(t);
            else if (!path.StartsWith(own + "/", StringComparison.Ordinal))
                return;
            for (int i = 0; i < t.childCount; i++)
                Collect(ctx, t.GetChild(i), path, matches);
        }

        /// 目标的瞄准点：子树内启用 Renderer 的包围盒中心，其次 Collider / Collider2D，最后 transform.position。
        internal static Vector3 AimPoint(Transform target)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (any)
                return bounds.center;
            var c3 = target.GetComponentInChildren<Collider>();
            if (c3 != null && c3.enabled)
                return c3.bounds.center;
            var c2 = target.GetComponentInChildren<Collider2D>();
            if (c2 != null && c2.enabled)
                return c2.bounds.center;
            return target.position;
        }

        /// 渲染目标层的相机：Camera.main 优先，其次按 depth 从高到低；能在屏幕上看到点的优先。
        private static Camera PickCamera(Transform target, Vector2? screenPoint, UiContext ctx)
        {
            var candidates = new List<Camera>();
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.isActiveAndEnabled || cam.targetTexture != null)
                    continue;
                if (target != null && (cam.cullingMask & (1 << target.gameObject.layer)) == 0)
                    continue;
                candidates.Add(cam);
            }
            var main = Camera.main;
            candidates.Sort((a, b) =>
            {
                if (a == main) return -1;
                if (b == main) return 1;
                return b.depth.CompareTo(a.depth);
            });
            if (candidates.Count == 0)
                return null;
            foreach (var cam in candidates)
            {
                Vector2 p;
                if (target != null)
                {
                    var s = cam.WorldToScreenPoint(AimPoint(target));
                    if (s.z <= 0f)
                        continue;
                    p = s;
                }
                else
                {
                    p = screenPoint.Value;
                }
                if (OnScreen(ctx, cam, p))
                    return cam;
            }
            return candidates[0];
        }

        private static bool OnScreen(UiContext ctx, Camera camera, Vector2 p) =>
            ctx.OnScreen(p) && camera.pixelRect.Contains(p);

        /// 游戏没上报时的兜底：3D 物理优先，其次 2D（只是猜测游戏会命中什么，结果标 hitSource=physics）。
        /// 物理模块是可选的：工程没装 com.unity.modules.physics / physics2d 时对应分支编译掉，
        /// 两个都没有时恒返回 null，点击结果就是 unverified。
        internal static GameObject PhysicsHit(Camera camera, Vector2 screen)
        {
            if (camera == null)
                return null;
#if U3D_HAS_PHYSICS || U3D_HAS_PHYSICS2D
            var ray = camera.ScreenPointToRay(screen);
#endif
#if U3D_HAS_PHYSICS
            if (Physics.Raycast(ray, out var hit, camera.farClipPlane))
                return hit.collider.gameObject;
#endif
#if U3D_HAS_PHYSICS2D
            var hit2 = Physics2D.GetRayIntersection(ray, camera.farClipPlane);
            if (hit2.collider != null)
                return hit2.collider.gameObject;
#endif
            return null;
        }

        // —— 工具 ——

        private static JObject TransformJson(UiContext ctx, Transform t) => new JObject
        {
            ["path"] = ctx.IdOf(t),
            ["iid"] = t.gameObject.GetInstanceID(),
        };

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

        private static bool IsAbsent(JToken token) => token == null || token.Type == JTokenType.Null;

        private static int? IntArg(JObject req, string key, string name)
        {
            var token = req[key];
            if (IsAbsent(token))
                return null;
            if (token.Type == JTokenType.Integer && (long)token >= int.MinValue && (long)token <= int.MaxValue)
                return (int)token;
            if (token.Type == JTokenType.String && int.TryParse((string)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;
            throw new ArgumentException("ui/act '" + name + "' must be an integer, got " + token.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static float? FloatArg(JObject req, string key)
        {
            var token = req[key];
            if (IsAbsent(token))
                return null;
            return Number(token, key);
        }

        private static Vector2? PointArg(JObject req, string key)
        {
            var token = req[key];
            if (IsAbsent(token))
                return null;
            if (!(token is JObject o))
                throw new ArgumentException("ui/act '" + key + "' must be {x, y} (screen pixels, top-left origin)");
            return new Vector2(Number(o["x"], key + ".x"), Number(o["y"], key + ".y"));
        }

        private static float Number(JToken token, string name)
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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Yoji.EditorDebug.Ui;

namespace Yoji.EditorDebug.Tests
{
    /// /ui/act 世界目标：相机前方一个方块，伪造的脚本指针记录每帧收到什么。帧号由测试推进，
    /// 所以 "Down / Up 各一帧" 与 "帧号不动不重喂" 能直接断言。
    public sealed class UiWorldActorTests
    {
        private sealed class FakePointer : IScriptedPointer
        {
            public readonly List<ScriptedPointerFrame> Applied = new List<ScriptedPointerFrame>();
            public int Begins, Ends;
            public bool Reports;
            public GameObject ReportedHit;
            public bool Active;
            public string ThrowAt;
            public bool EndAlsoThrows;
            public ScriptedPointerFrame Current;
            /// 模拟游戏读到这一帧后的反应（如按滚轮缩放相机）。
            public System.Action<ScriptedPointerFrame> OnApply;

            public void Begin() { Begins++; Active = true; ThrowIf("begin"); }
            public void Apply(ScriptedPointerFrame frame)
            {
                Assert.IsTrue(Active, "Apply outside Begin/End");
                Applied.Add(frame);
                ThrowIf("apply");
                Current = frame;
                OnApply?.Invoke(frame);
            }
            public void End() { Ends++; Active = false; ThrowIf("end"); }

            public bool TryGetReportedHit(out GameObject hit)
            {
                hit = ReportedHit;
                ThrowIf("report");
                return Reports;
            }

            private void ThrowIf(string stage)
            {
                if (ThrowAt == stage || (EndAlsoThrows && stage == "end"))
                    throw new System.InvalidOperationException("pointer " + stage + " failed");
            }
        }

        private readonly List<GameObject> m_Created = new List<GameObject>();
        private Camera m_Camera;
        private GameObject m_Target;
        private FakePointer m_Pointer;
        private int m_Frame;

        [SetUp]
        public void SetUp()
        {
            var camGo = new GameObject("WorldActCam", typeof(Camera));
            m_Created.Add(camGo);
            m_Camera = camGo.GetComponent<Camera>();
            m_Camera.transform.position = Vector3.zero;
            m_Camera.transform.rotation = Quaternion.identity;
            m_Camera.depth = 50;

            m_Target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Target.name = "WorldActTarget";
            m_Target.transform.position = new Vector3(0f, 0f, 10f);
            m_Created.Add(m_Target);

            m_Pointer = new FakePointer();
            m_Frame = 100;
        }

        [TearDown]
        public void TearDown()
        {
            UiWorldActor.Active?.Cancel("test_teardown", "test ended");
            foreach (var go in m_Created)
                Object.DestroyImmediate(go);
            m_Created.Clear();
        }

        private JObject Request(string action = "click") => new JObject
        {
            ["action"] = action,
            ["target"] = new JObject { ["path"] = "WorldActTarget", ["iid"] = m_Target.GetInstanceID() },
            ["camera"] = "WorldActCam",
        };

        private JObject Run(JObject req, int maxFrames = 400)
        {
            var refused = UiWorldActor.Start(req, live: false, m_Pointer, () => m_Frame, out var session);
            if (session == null)
                return refused;
            for (int i = 0; i < maxFrames && !session.Done.IsSet; i++)
            {
                m_Frame++;
                session.Step();
                session.Step(); // 同一帧号多次 tick：不得重喂
            }
            Assert.IsTrue(session.Done.IsSet, "session never finished");
            return session.Result;
        }

        private List<ScriptedPointerPhase> Phases()
        {
            var list = new List<ScriptedPointerPhase>();
            foreach (var f in m_Pointer.Applied)
                list.Add(f.Phase);
            return list;
        }

        [Test]
        public void Click_DownAndUpEachLastExactlyOneFrame()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = m_Target;
            var result = Run(Request());

            Assert.IsTrue((bool)result["acted"], result.ToString());
            CollectionAssert.AreEqual(new[]
            {
                ScriptedPointerPhase.Down, ScriptedPointerPhase.Held, ScriptedPointerPhase.Up, ScriptedPointerPhase.None,
            }, Phases(), "Down must become Held the next frame and Up must become None the next frame");
            Assert.AreEqual(1, m_Pointer.Begins);
            Assert.AreEqual(1, m_Pointer.Ends);
            Assert.AreEqual(4, (int)result["framesApplied"]);
        }

        [Test]
        public void Step_DoesNotReapplyWhileFrameStandsStill()
        {
            UiWorldActor.Start(Request(), live: false, m_Pointer, () => m_Frame, out var session);
            Assert.NotNull(session);
            session.Step();
            Assert.AreEqual(0, m_Pointer.Applied.Count, "nothing is fed on the frame the gesture starts");
            m_Frame++;
            for (int i = 0; i < 5; i++)
                session.Step(); // 暂停的编辑器：tick 照来，帧号不动
            Assert.AreEqual(1, m_Pointer.Applied.Count, "a paused frame would hold Down and cause a double tap");
            Assert.AreEqual(ScriptedPointerPhase.Down, m_Pointer.Applied[0].Phase);
            session.Cancel("test", "done");
            Assert.AreEqual(1, m_Pointer.Ends, "cancel hands the pointer back");
        }

        [TestCase("begin")]
        [TestCase("apply")]
        [TestCase("report")]
        [TestCase("end")]
        public void PointerFailure_TerminatesAndCannotBecomePhysicsVerified(string stage)
        {
            m_Pointer.ThrowAt = stage;
            if (stage == "end")
            {
                m_Pointer.Reports = true;
                m_Pointer.ReportedHit = m_Target;
            }
#if U3D_HAS_PHYSICS
            Physics.SyncTransforms();
#endif
            UiWorldActor.Session session = null;
            Assert.DoesNotThrow(() => UiWorldActor.Start(Request(), live: false, m_Pointer, () => m_Frame, out session));
            Assert.NotNull(session);
            for (int i = 0; i < 10 && !session.Done.IsSet; i++)
            {
                m_Frame++;
                Assert.DoesNotThrow(session.Step);
            }
            Assert.IsTrue(session.Done.IsSet, "pointer failures must release waiting HTTP requests");
            Assert.IsNull(UiWorldActor.Active, "a failed gesture must not keep later requests busy");
            Assert.AreEqual("pointer_error", (string)session.Result["reason"], session.Result.ToString());
            Assert.AreNotEqual(true, (bool?)session.Result["verified"], "a failed hit report is not absence of a report");
            Assert.AreEqual(1, m_Pointer.Ends, "even a partially failing Begin must return pointer ownership once");
            int applied = m_Pointer.Applied.Count;
            m_Frame++;
            Assert.DoesNotThrow(session.Step);
            Assert.DoesNotThrow(() => session.Cancel("test", "already ended"));
            Assert.AreEqual(applied, m_Pointer.Applied.Count);
            Assert.AreEqual(1, m_Pointer.Ends);
        }

        [Test]
        public void Cancel_EndFailureStillTerminatesWithOriginalCancellation()
        {
            UiWorldActor.Start(Request(), live: false, m_Pointer, () => m_Frame, out var session);
            m_Pointer.ThrowAt = "end";
            Assert.DoesNotThrow(() => session.Cancel("test", "cancelled"));
            Assert.IsTrue(session.Done.IsSet);
            Assert.IsNull(UiWorldActor.Active);
            Assert.AreEqual("test", (string)session.Result["reason"]);
            StringAssert.Contains("cancelled", (string)session.Result["message"]);
            StringAssert.Contains("pointer end failed", (string)session.Result["message"]);
            Assert.DoesNotThrow(() => session.Cancel("test", "again"));
            Assert.AreEqual(1, m_Pointer.Ends);
        }

        [Test]
        public void BeginAndEndFailure_PreservesBothErrorsAndTerminatesOnce()
        {
            m_Pointer.ThrowAt = "begin";
            m_Pointer.EndAlsoThrows = true;
            UiWorldActor.Session session = null;
            Assert.DoesNotThrow(() => UiWorldActor.Start(Request(), live: false, m_Pointer, () => m_Frame, out session));
            Assert.NotNull(session);
            Assert.IsTrue(session.Done.IsSet);
            Assert.IsNull(UiWorldActor.Active);
            Assert.AreEqual("pointer_error", (string)session.Result["reason"]);
            StringAssert.Contains("pointer begin failed", (string)session.Result["message"]);
            StringAssert.Contains("pointer end failed", (string)session.Result["message"]);
            m_Frame++;
            Assert.DoesNotThrow(session.Step);
            Assert.DoesNotThrow(() => session.Cancel("test", "already ended"));
            Assert.AreEqual(0, m_Pointer.Applied.Count);
            Assert.AreEqual(1, m_Pointer.Ends);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Camera_NotRenderingPrimaryScreen_IsRefused(bool auto, bool texture)
        {
            var rt = texture ? new RenderTexture(64, 64, 0) : null;
            var disabled = new List<Camera>();
            try
            {
                if (auto) DisableOtherCameras(disabled);
                m_Camera.targetTexture = rt;
                m_Camera.targetDisplay = texture ? 0 : 1;
                var req = Request();
                if (auto) req.Remove("camera");
                var result = Run(req);
                Assert.AreEqual("no_camera", (string)result["reason"], result.ToString());
                Assert.IsFalse((bool)result["acted"]);
                Assert.AreEqual(0, m_Pointer.Begins, "unsupported camera coordinates must not reach game input");
            }
            finally
            {
                m_Camera.targetTexture = null;
                if (rt != null) Object.DestroyImmediate(rt);
                foreach (var camera in disabled) if (camera != null) camera.enabled = true;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AutoCamera_SkipsUnsupportedCameraAndUsesPrimaryScreenFallback(bool texture)
        {
            var disabled = new List<Camera>();
            var rt = texture ? new RenderTexture(64, 64, 0) : null;
            try
            {
                DisableOtherCameras(disabled);
                var fallbackGo = new GameObject("PrimaryFallback", typeof(Camera));
                m_Created.Add(fallbackGo);
                var fallback = fallbackGo.GetComponent<Camera>();
                fallback.transform.position = m_Camera.transform.position;
                fallback.transform.rotation = m_Camera.transform.rotation;
                fallback.depth = 40;
                m_Camera.targetTexture = rt;
                m_Camera.targetDisplay = texture ? 0 : 1;
                m_Pointer.Reports = true;
                m_Pointer.ReportedHit = m_Target;
                var req = Request();
                req.Remove("camera");
                var result = Run(req);
                Assert.IsTrue((bool)result["verified"], result.ToString());
                Assert.AreEqual("PrimaryFallback", (string)result["camera"]);
                Assert.AreEqual(1, m_Pointer.Begins);
            }
            finally
            {
                m_Camera.targetTexture = null;
                if (rt != null) Object.DestroyImmediate(rt);
                foreach (var camera in disabled) if (camera != null) camera.enabled = true;
            }
        }

        private void DisableOtherCameras(List<Camera> disabled)
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera == m_Camera) continue;
                disabled.Add(camera);
                camera.enabled = false;
            }
        }

        [Test]
        public void ExplicitCamera_ExcludingTargetLayer_IsRefused()
        {
            m_Camera.cullingMask = 0;
            var result = Run(Request());
            Assert.AreEqual("no_camera", (string)result["reason"], result.ToString());
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void NonLiveGesture_DoesNotRegisterANativeLoopDriver()
        {
            int before = CountLoopNodes(PlayerLoop.GetCurrentPlayerLoop(), typeof(UiWorldActor));
            UiWorldActor.Start(Request(), live: false, m_Pointer, () => m_Frame, out var session);
            Assert.NotNull(session);
            Assert.AreEqual(before, CountLoopNodes(PlayerLoop.GetCurrentPlayerLoop(), typeof(UiWorldActor)));
            session.Cancel("test", "done");
            Assert.AreEqual(before, CountLoopNodes(PlayerLoop.GetCurrentPlayerLoop(), typeof(UiWorldActor)));
        }

        [UnityTest]
        public IEnumerator Live_ClickIsConsumedBeforeGameUpdate_AndRemovesOnlyItsOwnLoopNode() => LiveInGameLoop("complete");

        [UnityTest]
        public IEnumerator Live_PointerFailureRemovesOnlyItsOwnLoopNode() => LiveInGameLoop("apply_error");

        [UnityTest]
        public IEnumerator Live_CancelRemovesOnlyItsOwnLoopNode() => LiveInGameLoop("cancel");

        [UnityTest]
        public IEnumerator Live_CancelPreservesForeignChildAttachedToActorDriver() => LiveInGameLoop("cancel_nested");

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
                yield return new ExitPlayMode();
        }

        private IEnumerator LiveInGameLoop(string terminal)
        {
            foreach (var go in m_Created) Object.DestroyImmediate(go);
            m_Created.Clear();
            yield return new EnterPlayMode();
            TearDown();
            SetUp();
            // EnterPlayMode reloads the domain; callback closures must be created afterwards.
            yield return ConsumeLiveInGameLoop(terminal);
        }

        private IEnumerator ConsumeLiveInGameLoop(string terminal)
        {
            var consumer = m_Target.AddComponent<TestPointerRecorder>();
            var consumed = new List<ScriptedPointerPhase>();
            var consumedFrames = new List<int>();
            var appliedFrames = new List<int>();
            m_Pointer.OnApply = _ => appliedFrames.Add(Time.frameCount);
            consumer.GameUpdate = () =>
            {
                if (!m_Pointer.Active || m_Pointer.Applied.Count == 0)
                    return;
                consumed.Add(m_Pointer.Current.Phase);
                consumedFrames.Add(Time.frameCount);
                if (m_Pointer.Current.Phase == ScriptedPointerPhase.Up)
                {
                    m_Pointer.Reports = true;
                    m_Pointer.ReportedHit = m_Target;
                }
            };
            var originalLoop = PlayerLoop.GetCurrentPlayerLoop();
            int foreignTicks = 0;

            // The live driver must work without the old editor tick racing the game frame.
            var tickMethod = typeof(UiWorldActor).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
            var editorTick = tickMethod == null ? null : (EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), tickMethod);
            var subscribed = typeof(EditorApplication).GetField("update", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) as System.Delegate;
            bool hadEditorTick = editorTick != null && subscribed != null && System.Array.Exists(subscribed.GetInvocationList(), d => d.Equals(editorTick));
            if (editorTick != null) EditorApplication.update -= editorTick;
            UiWorldActor.Session session = null;
            try
            {
                if (terminal == "apply_error") m_Pointer.ThrowAt = "apply";
                var refused = UiWorldActor.Start(Request(), live: true, m_Pointer, null, out session);
                Assert.IsNull(refused, refused?.ToString());
                Assert.NotNull(session);
                var runningLoop = PlayerLoop.GetCurrentPlayerLoop();
                AddForeignNode(ref runningLoop, () => foreignTicks++, terminal == "cancel_nested");
                PlayerLoop.SetPlayerLoop(runningLoop);
                Assert.AreEqual(1, CountLoopNodes(runningLoop, typeof(UiWorldActor)));
                AssertActorRunsBeforeGameUpdate(runningLoop);
                if (terminal == "cancel" || terminal == "cancel_nested") session.Cancel("test_cancel", "cancelled before the next frame");
                for (int i = 0; i < 20 && !session.Done.IsSet; i++)
                    yield return null;
                Assert.IsTrue(session.Done.IsSet, "native game frames must finish the live gesture without an editor tick");
                yield return null;
                Assert.IsNull(UiWorldActor.Active);
                var endedLoop = PlayerLoop.GetCurrentPlayerLoop();
                Assert.AreEqual(0, CountLoopNodes(endedLoop, typeof(UiWorldActor)));
                Assert.AreEqual(1, CountLoopNodes(endedLoop, typeof(UiWorldActorTests)), "cleanup must preserve other PlayerLoop registrations");
                Assert.Greater(foreignTicks, 0);
                Assert.AreEqual(1, m_Pointer.Ends);
                if (terminal == "complete")
                {
                    Assert.IsTrue((bool)session.Result["verified"], session.Result.ToString());
                    Assert.AreEqual("game", (string)session.Result["hitSource"], "the MonoBehaviour must report its consumed Up before verification");
                    CollectionAssert.AreEqual(new[] { ScriptedPointerPhase.Down, ScriptedPointerPhase.Held, ScriptedPointerPhase.Up, ScriptedPointerPhase.None }, consumed);
                    CollectionAssert.AreEqual(appliedFrames, consumedFrames, "Apply must occur before MonoBehaviour.Update in the same real frame");
                    for (int i = 1; i < consumedFrames.Count; i++)
                        Assert.AreEqual(consumedFrames[i - 1] + 1, consumedFrames[i]);
                }
                else
                {
                    Assert.AreEqual(terminal == "apply_error" ? "pointer_error" : "test_cancel", (string)session.Result["reason"]);
                    Assert.AreNotEqual(true, (bool?)session.Result["verified"]);
                }
            }
            finally
            {
                consumer.GameUpdate = null;
                session?.Cancel("test_teardown", "test ended");
                PlayerLoop.SetPlayerLoop(originalLoop);
                if (hadEditorTick) EditorApplication.update += editorTick;
                foreach (var go in m_Created) Object.DestroyImmediate(go);
                m_Created.Clear();
            }
        }

        private static void AddForeignNode(ref PlayerLoopSystem loop, PlayerLoopSystem.UpdateFunction tick, bool underActor = false)
        {
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.Update))
                    continue;
                var update = loop.subSystemList[i];
                int actorIndex = underActor ? System.Array.FindIndex(update.subSystemList, n => n.type == typeof(UiWorldActor)) : -1;
                if (underActor) Assert.GreaterOrEqual(actorIndex, 0);
                var owner = underActor ? update.subSystemList[actorIndex] : update;
                var previous = owner.subSystemList ?? System.Array.Empty<PlayerLoopSystem>();
                var nodes = new PlayerLoopSystem[previous.Length + 1];
                System.Array.Copy(previous, nodes, previous.Length);
                nodes[nodes.Length - 1] = new PlayerLoopSystem { type = typeof(UiWorldActorTests), updateDelegate = tick };
                owner.subSystemList = nodes;
                if (underActor)
                {
                    update.subSystemList = (PlayerLoopSystem[])update.subSystemList.Clone();
                    update.subSystemList[actorIndex] = owner;
                }
                else update = owner;
                loop.subSystemList = (PlayerLoopSystem[])loop.subSystemList.Clone();
                loop.subSystemList[i] = update;
                return;
            }
            Assert.Fail("the native loop has no Update phase");
        }

        private static int CountLoopNodes(PlayerLoopSystem loop, System.Type type)
        {
            int count = loop.type == type ? 1 : 0;
            if (loop.subSystemList != null)
                foreach (var child in loop.subSystemList) count += CountLoopNodes(child, type);
            return count;
        }

        private static void AssertActorRunsBeforeGameUpdate(PlayerLoopSystem loop)
        {
            foreach (var phase in loop.subSystemList)
            {
                if (phase.type != typeof(UnityEngine.PlayerLoop.Update)) continue;
                int actor = System.Array.FindIndex(phase.subSystemList, n => n.type == typeof(UiWorldActor));
                int scripts = System.Array.FindIndex(phase.subSystemList, n => n.type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
                Assert.GreaterOrEqual(actor, 0);
                Assert.Greater(scripts, actor);
                return;
            }
            Assert.Fail("the native loop has no Update phase");
        }

        [Test]
        public void Click_PressPointIsTheTargetOnScreen()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = m_Target;
            Run(Request());
            var expected = (Vector2)m_Camera.WorldToScreenPoint(m_Target.transform.position);
            Assert.That(Vector2.Distance(expected, m_Pointer.Applied[0].Position), Is.LessThan(0.01f));
        }

        [Test]
        public void Click_GameHitMatchesTarget_Verified()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = m_Target;
            var result = Run(Request());
            Assert.IsTrue((bool)result["verified"], result.ToString());
            Assert.IsNull(result["reason"]);
            Assert.AreEqual("game", (string)result["hitSource"]);
            Assert.AreEqual(m_Target.GetInstanceID(), (int)result["hit"]["iid"]);
        }

        [Test]
        public void Click_GameHitAnotherObject_Fails()
        {
            var other = new GameObject("Other");
            m_Created.Add(other);
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = other;
            var result = Run(Request());
            Assert.AreEqual("hit_mismatch", (string)result["reason"]);
            Assert.IsFalse((bool)result["verified"]);
            Assert.AreEqual("Other", (string)result["hit"]["path"]);
        }

        [Test]
        public void Click_GameHitNothing_Fails()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = null;
            Assert.AreEqual("hit_mismatch", (string)Run(Request())["reason"]);
        }

#if U3D_HAS_PHYSICS
        [Test]
        public void Click_NoReport_FallsBackToPhysics()
        {
            Physics.SyncTransforms();
            var result = Run(Request());
            Assert.AreEqual("physics", (string)result["hitSource"], result.ToString());
            Assert.IsTrue((bool)result["verified"]);
        }
#endif

        [Test]
        public void Click_NoReportNoCollider_Unverified()
        {
#if U3D_HAS_PHYSICS
            Object.DestroyImmediate(m_Target.GetComponent<Collider>());
#endif
            var result = Run(Request());
            Assert.AreEqual("unverified", (string)result["reason"]);
            Assert.IsTrue((bool)result["acted"], "the click was sent; only its effect is unknown");
        }

        [Test]
        public void BehindCamera_Refused()
        {
            m_Target.transform.position = new Vector3(0f, 0f, -10f);
            var result = Run(Request());
            Assert.AreEqual("behind_camera", (string)result["reason"]);
            Assert.IsFalse((bool)result["acted"]);
            Assert.AreEqual(0, m_Pointer.Begins, "a refused gesture never takes the pointer");
        }

        [Test]
        public void BehindCamera_Orthographic_Refused()
        {
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 20f;
            // 正交相机的视口按 xy 截取，目标仍在画面范围内，只是在相机背后
            m_Target.transform.position = new Vector3(0f, 0f, -10f);
            var result = Run(Request());
            Assert.AreEqual("behind_camera", (string)result["reason"], result.ToString());
            Assert.IsFalse((bool)result["acted"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void BehindCamera_Perspective_TargetBesideAndBehind_Refused()
        {
            // 斜后方：WorldToScreenPoint 的 xy 可能落在屏幕内，只有 z 能判出背后
            m_Target.transform.position = new Vector3(0.5f, 0.2f, -3f);
            var result = Run(Request());
            Assert.AreEqual("behind_camera", (string)result["reason"], result.ToString());
            Assert.AreEqual(0, m_Pointer.Applied.Count, "a target behind the camera is never fed");
        }

        [Test]
        public void BehindCamera_AutoPickedCamera_Refused()
        {
            m_Target.transform.position = new Vector3(0f, 0f, -10f);
            var req = Request();
            req.Remove("camera");
            var result = Run(req);
            Assert.AreEqual("behind_camera", (string)result["reason"], result.ToString());
        }

        [Test]
        public void NearerThanNearClip_IsOffscreenNotBehind()
        {
            m_Camera.nearClipPlane = 1f;
            m_Target.transform.position = new Vector3(0f, 0f, 0.5f);
            // 只留 transform.position 当瞄准点（编辑器里未同步的 collider bounds 可能停在原点）
            Object.DestroyImmediate(m_Target.GetComponent<Renderer>());
            Object.DestroyImmediate(m_Target.GetComponent<Collider>());
            Assert.AreEqual("offscreen", (string)Run(Request())["reason"]);
        }

        [Test]
        public void Scroll_ReadsBackOrthographicSizeBeforeAndAfter()
        {
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 10f;
            m_Pointer.OnApply = f => m_Camera.orthographicSize -= f.Scroll.y; // 游戏：一格滚轮缩 1
            var req = Request("scroll");
            req["scroll"] = 3;
            req["steps"] = 3;
            var result = Run(req);
            Assert.AreEqual(10f, (float)result["cameraBefore"]["orthographicSize"], 1e-4f, result.ToString());
            Assert.AreEqual(7f, (float)result["cameraAfter"]["orthographicSize"], 1e-4f);
            Assert.IsTrue((bool)result["cameraAfter"]["orthographic"]);
            Assert.IsNull(result["cameraAfter"]["fieldOfView"]);
        }

        [Test]
        public void Pinch_ReadsBackFieldOfViewAndPosition()
        {
            m_Camera.orthographic = false;
            m_Camera.fieldOfView = 60f;
            m_Pointer.OnApply = f =>
            {
                if (f.Touches.Length == 2 && f.Touches[0].phase == TouchPhase.Moved)
                    m_Camera.fieldOfView -= 5f;
            };
            var req = Request("pinch");
            req["scale"] = 1.5f;
            req["steps"] = 2;
            var result = Run(req);
            Assert.AreEqual(60f, (float)result["cameraBefore"]["fieldOfView"], 1e-4f, result.ToString());
            Assert.AreEqual(50f, (float)result["cameraAfter"]["fieldOfView"], 1e-4f);
            Assert.IsNotNull(result["cameraAfter"]["position"]["z"]);
        }

        [Test]
        public void Readback_IsTakenAfterTheLastFrame()
        {
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 10f;
            // 游戏在最后一个计划帧（无滚轮的收尾帧）才动相机：cameraAfter 必须已经看到它
            m_Pointer.OnApply = f => { if (f.Scroll.y == 0f) m_Camera.orthographicSize = 4f; };
            var req = Request("scroll");
            req["scroll"] = 1;
            var result = Run(req);
            Assert.AreEqual(4f, (float)result["cameraAfter"]["orthographicSize"], 1e-4f, result.ToString());
        }

        [Test]
        public void ClickAndDrag_HaveNoCameraReadback()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = m_Target;
            var result = Run(Request());
            Assert.IsNull(result["cameraBefore"]);
            Assert.IsNull(result["cameraAfter"]);
        }

        [Test]
        public void Offscreen_Refused()
        {
            m_Target.transform.position = new Vector3(500f, 0f, 10f);
            Assert.AreEqual("offscreen", (string)Run(Request())["reason"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void CoveredByUi_RefusedWithCoveredBy()
        {
            var canvasGo = new GameObject("Hud", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            m_Created.Add(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.AddComponent<Image>();

            var result = Run(Request());
            Assert.AreEqual("covered", (string)result["reason"], result.ToString());
            Assert.AreEqual("Hud/Panel", (string)result["coveredBy"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void StaleIid_Refused()
        {
            var other = new GameObject("Other");
            m_Created.Add(other);
            var req = Request();
            req["target"]["iid"] = other.GetInstanceID();
            Assert.AreEqual("stale", (string)Run(req)["reason"]);
        }

        [Test]
        public void PathOnly_ResolvesAndMissingPathIsNotFound()
        {
            m_Pointer.Reports = true;
            m_Pointer.ReportedHit = m_Target;
            var req = Request();
            ((JObject)req["target"]).Remove("iid");
            Assert.IsTrue((bool)Run(req)["verified"]);

            req["target"]["path"] = "Nope";
            Assert.AreEqual("not_found", (string)Run(req)["reason"]);
        }

        [Test]
        public void NoPointer_Refused()
        {
            var result = UiWorldActor.Start(Request(), live: false, null, () => m_Frame, out var session);
            Assert.IsNull(session);
            Assert.AreEqual("no_scripted_pointer", (string)result["reason"]);
        }

        [Test]
        public void Drag_DownHeldStepsUpNone()
        {
            var req = Request("drag");
            req["by"] = new JObject { ["x"] = 40, ["y"] = 0 };
            req["steps"] = 4;
            var result = Run(req);
            Assert.IsTrue((bool)result["acted"], result.ToString());
            var phases = Phases();
            Assert.AreEqual(7, phases.Count);
            Assert.AreEqual(ScriptedPointerPhase.Down, phases[0]);
            Assert.AreEqual(ScriptedPointerPhase.Up, phases[5]);
            Assert.AreEqual(ScriptedPointerPhase.None, phases[6]);
            Assert.AreEqual(1, phases.FindAll(p => p == ScriptedPointerPhase.Down).Count);
            Assert.AreEqual(1, phases.FindAll(p => p == ScriptedPointerPhase.Up).Count);
            Assert.That(m_Pointer.Applied[5].Position.x - m_Pointer.Applied[0].Position.x, Is.EqualTo(40f).Within(0.01f));
            Assert.IsNull(result["verified"], "drags are not hit-checked");
        }

        [Test]
        public void Scroll_SplitsNotchesThenZero()
        {
            var req = Request("scroll");
            req["scroll"] = 2;
            req["steps"] = 2;
            Run(req);
            Assert.AreEqual(3, m_Pointer.Applied.Count);
            Assert.AreEqual(1f, m_Pointer.Applied[0].Scroll.y, 1e-4f);
            Assert.AreEqual(1f, m_Pointer.Applied[1].Scroll.y, 1e-4f);
            Assert.AreEqual(0f, m_Pointer.Applied[2].Scroll.y);
            Assert.IsTrue(m_Pointer.Applied.TrueForAll(f => f.Phase == ScriptedPointerPhase.None), "scroll never presses");
        }

        [Test]
        public void Pinch_TwoFingersBeganMovedEndedThenNone()
        {
            var req = Request("pinch");
            req["scale"] = 1.5f;
            req["span"] = 100;
            req["steps"] = 3;
            Run(req);
            var frames = m_Pointer.Applied;
            Assert.AreEqual(6, frames.Count);
            Assert.AreEqual(TouchPhase.Began, frames[0].Touches[0].phase);
            Assert.AreEqual(TouchPhase.Moved, frames[1].Touches[1].phase);
            Assert.AreEqual(TouchPhase.Ended, frames[4].Touches[0].phase);
            Assert.AreEqual(0, frames[5].Touches.Length);
            float startSpan = Vector2.Distance(frames[0].Touches[0].position, frames[0].Touches[1].position);
            float endSpan = Vector2.Distance(frames[3].Touches[0].position, frames[3].Touches[1].position);
            Assert.AreEqual(100f, startSpan, 0.01f);
            Assert.AreEqual(150f, endSpan, 0.01f);
            // deltaPosition 能还原上一帧位置（游戏按它算捏合距离差）
            var t = frames[2].Touches[0];
            Assert.AreEqual(frames[1].Touches[0].position, t.position - t.deltaPosition);
        }

        [Test]
        public void AtWithoutTarget_ClickIsAnError()
        {
            var req = new JObject { ["action"] = "click", ["at"] = new JObject { ["x"] = 10, ["y"] = 10 } };
            Assert.Throws<System.ArgumentException>(() => UiWorldActor.Start(req, live: false, m_Pointer, () => m_Frame, out _));
        }

        [Test]
        public void IsWorldRequest_RoutesTargetAndBareAt()
        {
            Assert.IsTrue(UiWorldActor.IsWorldRequest(new JObject { ["target"] = new JObject { ["path"] = "A" } }));
            Assert.IsTrue(UiWorldActor.IsWorldRequest(new JObject { ["at"] = new JObject { ["x"] = 1, ["y"] = 1 } }));
            Assert.IsFalse(UiWorldActor.IsWorldRequest(new JObject { ["id"] = "Base/Btn", ["at"] = new JObject { ["x"] = 1, ["y"] = 1 } }));
            Assert.IsFalse(UiWorldActor.IsWorldRequest(new JObject { ["id"] = "Base/Btn", ["target"] = null }));
        }

        [Test]
        public void Registry_UnregisterOnlyRemovesTheRegisteredPointer()
        {
            var previous = ScriptedPointerRegistry.Current;
            try
            {
                var a = new FakePointer();
                var b = new FakePointer();
                ScriptedPointerRegistry.Register(a);
                ScriptedPointerRegistry.Unregister(b);
                Assert.AreSame(a, ScriptedPointerRegistry.Current);
                ScriptedPointerRegistry.Unregister(a);
                Assert.IsNull(ScriptedPointerRegistry.Current);
            }
            finally
            {
                if (previous != null) ScriptedPointerRegistry.Register(previous);
            }
        }
    }
}

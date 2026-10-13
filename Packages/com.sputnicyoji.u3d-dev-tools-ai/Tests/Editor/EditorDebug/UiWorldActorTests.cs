using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
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
            /// 模拟游戏读到这一帧后的反应（如按滚轮缩放相机）。
            public System.Action<ScriptedPointerFrame> OnApply;

            public void Begin() { Begins++; Active = true; }
            public void Apply(ScriptedPointerFrame frame) { Assert.IsTrue(Active, "Apply outside Begin/End"); Applied.Add(frame); OnApply?.Invoke(frame); }
            public void End() { Ends++; Active = false; }

            public bool TryGetReportedHit(out GameObject hit)
            {
                hit = ReportedHit;
                return Reports;
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
#if U3D_HAS_PHYSICS
            Object.DestroyImmediate(m_Target.GetComponent<Collider>());
#endif
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
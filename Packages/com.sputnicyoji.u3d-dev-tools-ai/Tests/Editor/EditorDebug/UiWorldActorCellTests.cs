using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Yoji.EditorDebug.Ui;

namespace Yoji.EditorDebug.Tests
{
    /// /ui/act 格子目标（04）：相机在原点朝 +z，地图是 z = 10 平面上的单位格。伪造的指针同时实现
    /// IScriptedPointerCells，Up 那一帧按游戏的规则把按下点判成格子并上报（可改成上报别的格子或不上报）。
    public sealed class UiWorldActorCellTests
    {
        private const float k_MapZ = 10f;
        private const int k_MapHalf = 1000;

        private sealed class FakeCellPointer : IScriptedPointer, IScriptedPointerCells
        {
            public readonly List<ScriptedPointerFrame> Applied = new List<ScriptedPointerFrame>();
            public int Begins, Ends;
            public Camera Camera;
            public bool ReportsCell = true;
            public Vector2Int? CellOverride;
            public bool OverrideSet;
            public bool RoundTripLies;
            public bool ThrowOnCellReport;
            public GameObject Unit;

            private bool m_CellReported;
            private Vector2Int? m_Cell;
            private bool m_HitReported;
            private GameObject m_Hit;

            public void Begin() { Begins++; m_CellReported = m_HitReported = false; m_Cell = null; m_Hit = null; }
            public void End() { Ends++; }

            public void Apply(ScriptedPointerFrame frame)
            {
                Applied.Add(frame);
                if (frame.Phase != ScriptedPointerPhase.Up || !ReportsCell)
                    return;
                // 游戏的 tap：按下点 -> 格子，上报格子；格子上有单位时也上报单位
                bool on = Judge(Camera, frame.Position, out var judged);
                m_CellReported = true;
                m_Cell = OverrideSet ? CellOverride : on ? judged : (Vector2Int?)null;
                m_HitReported = true;
                m_Hit = Unit;
            }

            public bool TryGetReportedHit(out GameObject hit) { hit = m_Hit; return m_HitReported; }

            public Camera CellCamera => Camera;

            public bool TryCellToWorld(Vector2Int cell, out Vector3 world)
            {
                world = new Vector3(cell.x + 0.5f, cell.y + 0.5f, k_MapZ);
                return Mathf.Abs(cell.x) <= k_MapHalf && Mathf.Abs(cell.y) <= k_MapHalf;
            }

            public bool TryScreenToCell(Camera camera, Vector2 screen, out Vector2Int cell)
            {
                bool on = Judge(camera, screen, out cell);
                if (RoundTripLies) cell += Vector2Int.right;
                return on;
            }

            public bool TryGetReportedCell(out Vector2Int? cell)
            {
                cell = m_Cell;
                if (ThrowOnCellReport)
                    throw new System.InvalidOperationException("cell report failed");
                return m_CellReported;
            }

            private static bool Judge(Camera camera, Vector2 screen, out Vector2Int cell)
            {
                cell = default;
                var ray = camera.ScreenPointToRay(screen);
                if (!new Plane(Vector3.back, new Vector3(0f, 0f, k_MapZ)).Raycast(ray, out float d) || d < 0f)
                    return false;
                var p = ray.GetPoint(d);
                cell = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                return Mathf.Abs(cell.x) <= k_MapHalf && Mathf.Abs(cell.y) <= k_MapHalf;
            }
        }

        private readonly List<GameObject> m_Created = new List<GameObject>();
        private Camera m_Camera;
        private FakeCellPointer m_Pointer;
        private int m_Frame;

        [SetUp]
        public void SetUp()
        {
            var camGo = new GameObject("CellActCam", typeof(Camera));
            m_Created.Add(camGo);
            m_Camera = camGo.GetComponent<Camera>();
            m_Camera.transform.position = Vector3.zero;
            m_Camera.transform.rotation = Quaternion.identity;
            m_Pointer = new FakeCellPointer { Camera = m_Camera };
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

        private static JObject Request(int x, int y) => new JObject
        {
            ["action"] = "click",
            ["target"] = new JObject { ["cell"] = new JObject { ["x"] = x, ["y"] = y } },
        };

        private JObject Run(JObject req, IScriptedPointer pointer = null)
        {
            var refused = UiWorldActor.Start(req, live: false, pointer ?? m_Pointer, () => m_Frame, out var session);
            if (session == null)
                return refused;
            for (int i = 0; i < 100 && !session.Done.IsSet; i++)
            {
                m_Frame++;
                session.Step();
                session.Step();
            }
            Assert.IsTrue(session.Done.IsSet, "session never finished");
            return session.Result;
        }

        [Test]
        public void Cell_GameReportsSameCell_Verified()
        {
            var result = Run(Request(0, 0));
            Assert.IsTrue((bool)result["acted"], result.ToString());
            Assert.IsTrue((bool)result["verified"], result.ToString());
            Assert.IsNull(result["reason"]);
            Assert.AreEqual("game", (string)result["hitSource"]);
            Assert.AreEqual(0, (int)result["cell"]["x"]);
            Assert.AreEqual(0, (int)result["cell"]["y"]);
            Assert.AreEqual(0, (int)result["target"]["cell"]["x"]);
            Assert.AreEqual("CellActCam", (string)result["camera"]);
        }

        [Test]
        public void Cell_DownAndUpEachLastExactlyOneFrame()
        {
            Run(Request(1, -1));
            var phases = new List<ScriptedPointerPhase>();
            foreach (var f in m_Pointer.Applied) phases.Add(f.Phase);
            CollectionAssert.AreEqual(new[]
            {
                ScriptedPointerPhase.Down, ScriptedPointerPhase.Held, ScriptedPointerPhase.Up, ScriptedPointerPhase.None,
            }, phases);
            Assert.AreEqual(1, m_Pointer.Begins);
            Assert.AreEqual(1, m_Pointer.Ends);
        }

        [Test]
        public void Cell_PressPointIsTheCellCentreOnScreen()
        {
            Run(Request(1, 1));
            var expected = (Vector2)m_Camera.WorldToScreenPoint(new Vector3(1.5f, 1.5f, k_MapZ));
            Assert.That(Vector2.Distance(expected, m_Pointer.Applied[0].Position), Is.LessThan(0.01f));
        }

        [Test]
        public void Cell_WithUnit_ResultMergesCellAndUnit()
        {
            var unit = new GameObject("Unit_7");
            m_Created.Add(unit);
            m_Pointer.Unit = unit;
            var result = Run(Request(0, 1));
            Assert.IsTrue((bool)result["verified"], result.ToString());
            Assert.AreEqual(1, (int)result["cell"]["y"]);
            Assert.AreEqual("Unit_7", (string)result["unit"]["path"]);
            Assert.AreEqual(unit.GetInstanceID(), (int)result["unit"]["iid"]);
        }

        [Test]
        public void Cell_EmptyCell_HasNoUnit()
        {
            var result = Run(Request(0, 0));
            Assert.IsNull(result["unit"]);
        }

        [Test]
        public void Cell_GameReportsAnotherCell_CellMismatch()
        {
            m_Pointer.OverrideSet = true;
            m_Pointer.CellOverride = new Vector2Int(3, 3);
            var result = Run(Request(0, 0));
            Assert.AreEqual("cell_mismatch", (string)result["reason"], result.ToString());
            Assert.IsFalse((bool)result["verified"]);
            Assert.IsTrue((bool)result["acted"], "the click was sent");
            Assert.AreEqual(3, (int)result["cell"]["x"]);
        }

        [Test]
        public void Cell_GameReportsNoCell_CellMismatch()
        {
            m_Pointer.OverrideSet = true;
            m_Pointer.CellOverride = null;
            var result = Run(Request(0, 0));
            Assert.AreEqual("cell_mismatch", (string)result["reason"], result.ToString());
            Assert.AreEqual(JTokenType.Null, result["cell"].Type);
        }

        [Test]
        public void Cell_GameReportsNothing_Unverified()
        {
            m_Pointer.ReportsCell = false;
            var result = Run(Request(0, 0));
            Assert.AreEqual("unverified", (string)result["reason"], result.ToString());
            Assert.IsFalse((bool)result["verified"]);
            Assert.IsTrue((bool)result["acted"]);
        }

        [Test]
        public void Cell_ReportFailure_TerminatesWithoutRetryOrSuccessfulVerification()
        {
            m_Pointer.ThrowOnCellReport = true;
            UiWorldActor.Start(Request(0, 0), live: false, m_Pointer, () => m_Frame, out var session);
            Assert.NotNull(session);
            for (int i = 0; i < 10 && !session.Done.IsSet; i++)
            {
                m_Frame++;
                Assert.DoesNotThrow(session.Step);
            }
            Assert.IsTrue(session.Done.IsSet);
            Assert.IsNull(UiWorldActor.Active);
            Assert.AreEqual("pointer_error", (string)session.Result["reason"], session.Result.ToString());
            Assert.AreNotEqual(true, (bool?)session.Result["verified"]);
            int applied = m_Pointer.Applied.Count;
            m_Frame++;
            Assert.DoesNotThrow(session.Step);
            Assert.AreEqual(applied, m_Pointer.Applied.Count);
            Assert.AreEqual(1, m_Pointer.Ends);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CellCamera_NotRenderingPrimaryScreen_IsRefused(bool explicitCamera, bool texture)
        {
            var rt = texture ? new RenderTexture(64, 64, 0) : null;
            try
            {
                m_Camera.targetTexture = rt;
                m_Camera.targetDisplay = texture ? 0 : 1;
                var req = Request(0, 0);
                if (explicitCamera) req["camera"] = "CellActCam";
                var result = Run(req);
                Assert.AreEqual("no_camera", (string)result["reason"], result.ToString());
                Assert.IsFalse((bool)result["acted"]);
                Assert.AreEqual(0, m_Pointer.Begins);
            }
            finally
            {
                m_Camera.targetTexture = null;
                if (rt != null) Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void Cell_OutOfMap_Refused()
        {
            var result = Run(Request(k_MapHalf + 1, 0));
            Assert.AreEqual("out_of_map", (string)result["reason"]);
            Assert.AreEqual(0, m_Pointer.Begins, "a refused gesture never takes the pointer");
        }

        [Test]
        public void Cell_Offscreen_Refused()
        {
            Assert.AreEqual("offscreen", (string)Run(Request(500, 0))["reason"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void Cell_BehindCamera_Refused()
        {
            m_Camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var result = Run(Request(0, 0));
            Assert.AreEqual("behind_camera", (string)result["reason"], result.ToString());
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void Cell_RoundTripLandsElsewhere_AmbiguousAndNotFed()
        {
            m_Pointer.RoundTripLies = true;
            var result = Run(Request(0, 0));
            Assert.AreEqual("ambiguous_cell", (string)result["reason"], result.ToString());
            Assert.AreEqual(1, (int)result["roundTrip"]["x"]);
            Assert.AreEqual(0, m_Pointer.Begins);
            Assert.AreEqual(0, m_Pointer.Applied.Count);
        }

        [Test]
        public void Cell_CoveredByUi_RefusedWithCoveredBy()
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

            var result = Run(Request(0, 0));
            Assert.AreEqual("covered", (string)result["reason"], result.ToString());
            Assert.AreEqual("Hud/Panel", (string)result["coveredBy"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }

        [Test]
        public void Cell_NoCameraFromGame_Refused()
        {
            m_Pointer.Camera = null;
            Assert.AreEqual("no_camera", (string)Run(Request(0, 0))["reason"]);
        }

        [Test]
        public void Cell_PointerWithoutCellCapability_Refused()
        {
            var plain = new PlainPointer();
            var result = Run(Request(0, 0), plain);
            Assert.AreEqual("no_cell_target", (string)result["reason"]);
            Assert.AreEqual(0, plain.Begins);
        }

        [Test]
        public void Cell_WithPathOrDrag_IsAnError()
        {
            var both = Request(0, 0);
            both["target"]["path"] = "Somewhere";
            Assert.Throws<System.ArgumentException>(() => UiWorldActor.Start(both, live: false, m_Pointer, () => m_Frame, out _));

            var drag = Request(0, 0);
            drag["action"] = "drag";
            drag["by"] = new JObject { ["x"] = 10, ["y"] = 0 };
            Assert.Throws<System.ArgumentException>(() => UiWorldActor.Start(drag, live: false, m_Pointer, () => m_Frame, out _));

            var bad = new JObject { ["action"] = "click", ["target"] = new JObject { ["cell"] = new JObject { ["x"] = 1 } } };
            Assert.Throws<System.ArgumentException>(() => UiWorldActor.Start(bad, live: false, m_Pointer, () => m_Frame, out _));
        }

        private sealed class PlainPointer : IScriptedPointer
        {
            public int Begins;
            public void Begin() { Begins++; }
            public void Apply(ScriptedPointerFrame frame) { }
            public void End() { }
            public bool TryGetReportedHit(out GameObject hit) { hit = null; return false; }
        }
    }
}

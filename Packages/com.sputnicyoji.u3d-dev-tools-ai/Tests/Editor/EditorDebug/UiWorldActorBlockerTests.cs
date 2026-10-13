using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Yoji.EditorDebug.Ui;

namespace Yoji.EditorDebug.Tests
{
    /// /ui/act 输入阻塞（06）：游戏整块吞掉地图输入时（例如打开的面板让地图不收点击，哪怕面板没盖住按下点），
    /// 指针可再实现 IScriptedPointerInputBlocker 报出阻塞者，act 在喂帧前拒绝为 blocked 并在 coveredBy 给出阻塞者。
    /// 相机在原点朝 +z，地图是 z = 10 平面上的单位格。
    public sealed class UiWorldActorBlockerTests
    {
        private const float k_MapZ = 10f;

        private sealed class FakeBlockingPointer : IScriptedPointer, IScriptedPointerCells, IScriptedPointerInputBlocker
        {
            public readonly List<ScriptedPointerFrame> Applied = new List<ScriptedPointerFrame>();
            public int Begins, Ends, BlockerQueries;
            public Camera Camera;
            public GameObject Blocker;
            public bool ReportsBlocker;

            private bool m_CellReported;
            private Vector2Int? m_Cell;

            public void Begin() { Begins++; m_CellReported = false; m_Cell = null; }
            public void End() { Ends++; }

            public void Apply(ScriptedPointerFrame frame)
            {
                Applied.Add(frame);
                if (frame.Phase != ScriptedPointerPhase.Up) return;
                bool on = Judge(Camera, frame.Position, out var judged);
                m_CellReported = true;
                m_Cell = on ? judged : (Vector2Int?)null;
            }

            public bool TryGetReportedHit(out GameObject hit) { hit = null; return false; }

            public Camera CellCamera => Camera;

            public bool TryCellToWorld(Vector2Int cell, out Vector3 world)
            {
                world = new Vector3(cell.x + 0.5f, cell.y + 0.5f, k_MapZ);
                return Mathf.Abs(cell.x) <= 1000 && Mathf.Abs(cell.y) <= 1000;
            }

            public bool TryScreenToCell(Camera camera, Vector2 screen, out Vector2Int cell) => Judge(camera, screen, out cell);

            public bool TryGetReportedCell(out Vector2Int? cell) { cell = m_Cell; return m_CellReported; }

            public bool TryGetInputBlocker(out GameObject blocker)
            {
                BlockerQueries++;
                blocker = Blocker;
                return ReportsBlocker;
            }

            private static bool Judge(Camera camera, Vector2 screen, out Vector2Int cell)
            {
                cell = default;
                var ray = camera.ScreenPointToRay(screen);
                if (!new Plane(Vector3.back, new Vector3(0f, 0f, k_MapZ)).Raycast(ray, out float d) || d < 0f)
                    return false;
                var p = ray.GetPoint(d);
                cell = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                return true;
            }
        }

        private readonly List<GameObject> m_Created = new List<GameObject>();
        private Camera m_Camera;
        private GameObject m_Target;
        private FakeBlockingPointer m_Pointer;
        private int m_Frame;

        [SetUp]
        public void SetUp()
        {
            var camGo = new GameObject("BlockerActCam", typeof(Camera));
            m_Created.Add(camGo);
            m_Camera = camGo.GetComponent<Camera>();
            m_Camera.transform.position = Vector3.zero;
            m_Camera.transform.rotation = Quaternion.identity;
            m_Camera.depth = 50;

            m_Target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Target.name = "BlockerActTarget";
            m_Target.transform.position = new Vector3(0f, 0f, k_MapZ);
            m_Created.Add(m_Target);

            var root = new GameObject("WorldHud");
            m_Created.Add(root);
            var window = new GameObject("Window");
            window.transform.SetParent(root.transform, false);

            m_Pointer = new FakeBlockingPointer { Camera = m_Camera, Blocker = window };
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

        private static JObject CellRequest(int x, int y) => new JObject
        {
            ["action"] = "click",
            ["target"] = new JObject { ["cell"] = new JObject { ["x"] = x, ["y"] = y } },
        };

        private JObject ObjectRequest(string action) => new JObject
        {
            ["action"] = action,
            ["target"] = new JObject { ["path"] = "BlockerActTarget", ["iid"] = m_Target.GetInstanceID() },
            ["camera"] = "BlockerActCam",
        };

        private JObject Run(JObject req)
        {
            var refused = UiWorldActor.Start(req, live: false, m_Pointer, () => m_Frame, out var session);
            if (session == null)
                return refused;
            for (int i = 0; i < 400 && !session.Done.IsSet; i++)
            {
                m_Frame++;
                session.Step();
            }
            Assert.IsTrue(session.Done.IsSet, "session never finished");
            return session.Result;
        }

        [Test]
        public void Cell_GameBlocksInput_RefusedAsBlockedWithBlockerAndNotFed()
        {
            m_Pointer.ReportsBlocker = true;
            var result = Run(CellRequest(0, 0));
            Assert.IsFalse((bool)result["acted"], result.ToString());
            Assert.AreEqual("blocked", (string)result["reason"], result.ToString());
            Assert.AreEqual("WorldHud/Window", (string)result["coveredBy"]);
            Assert.AreEqual(0, m_Pointer.Begins, "a refused gesture never takes the pointer");
            Assert.AreEqual(0, m_Pointer.Applied.Count);
        }

        [Test]
        public void Cell_NoBlocker_ClicksAndVerifiesAsBefore()
        {
            m_Pointer.ReportsBlocker = false;
            var result = Run(CellRequest(0, 0));
            Assert.IsTrue((bool)result["acted"], result.ToString());
            Assert.IsTrue((bool)result["verified"], result.ToString());
            Assert.AreEqual(1, m_Pointer.BlockerQueries, "the blocker is asked once, before pressing");
        }

        [Test]
        public void Cell_BlockerReportedButGone_IsNotBlocked()
        {
            // 游戏报了阻塞，但阻塞者已经销毁：不能凭空拒绝，也不能把空路径写进 coveredBy
            m_Pointer.ReportsBlocker = true;
            Object.DestroyImmediate(m_Pointer.Blocker);
            var result = Run(CellRequest(0, 0));
            Assert.IsTrue((bool)result["acted"], result.ToString());
        }

        [TestCase("click")]
        [TestCase("drag")]
        [TestCase("scroll")]
        [TestCase("pinch")]
        public void WorldObject_GameBlocksInput_EveryGestureRefused(string action)
        {
            m_Pointer.ReportsBlocker = true;
            var req = ObjectRequest(action);
            if (action == "drag") req["by"] = new JObject { ["x"] = 20, ["y"] = 0 };
            if (action == "scroll") req["scroll"] = 1;
            if (action == "pinch") { req["scale"] = 1.5; req["span"] = 40; }
            var result = Run(req);
            Assert.AreEqual("blocked", (string)result["reason"], result.ToString());
            Assert.AreEqual("WorldHud/Window", (string)result["coveredBy"]);
            Assert.AreEqual(0, m_Pointer.Begins);
        }
    }
}
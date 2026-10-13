using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yoji.EditorDebug.Ui;

namespace Yoji.EditorDebug.Tests
{
    /// 合成 Canvas：全屏背景图 + 两个按钮 + 一层在 BtnOpen 上开洞的引导遮罩。
    /// 排序或命中逻辑一坏，hitTop / coveredBy / guide 就对不上。
    public sealed class UiObserverTests
    {
        private readonly List<GameObject> m_Created = new List<GameObject>();
        private RectTransform m_Open;
        private RectTransform m_Covered;
        private TestGuideMask m_Mask;

        [SetUp]
        public void SetUp()
        {
            var root = new GameObject("Base", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            m_Created.Add(root);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var background = Child(root.transform, "Background", 0f, 0f, 1f, 1f);
            background.gameObject.AddComponent<Image>();

            m_Open = Child(root.transform, "BtnOpen", 0.1f, 0.1f, 0.4f, 0.3f);
            m_Open.gameObject.AddComponent<Image>();
            m_Open.gameObject.AddComponent<Button>();
            var label = Child(m_Open, "Label", 0f, 0f, 1f, 1f);
            var text = label.gameObject.AddComponent<Text>();
            text.text = "Open";
            text.raycastTarget = false;

            m_Covered = Child(root.transform, "BtnCovered", 0.6f, 0.6f, 0.9f, 0.8f);
            m_Covered.gameObject.AddComponent<Image>();
            m_Covered.gameObject.AddComponent<Button>();

            var guideLayer = Child(root.transform, "GuideLayer", 0f, 0f, 1f, 1f);
            var guideCanvas = guideLayer.gameObject.AddComponent<Canvas>();
            guideLayer.gameObject.AddComponent<GraphicRaycaster>();
            guideCanvas.overrideSorting = true;
            guideCanvas.sortingOrder = 100;
            var maskRect = Child(guideLayer, "GuideMask", 0f, 0f, 1f, 1f);
            m_Mask = maskRect.gameObject.AddComponent<TestGuideMask>();
            m_Mask.Target = m_Open;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_Created)
                Object.DestroyImmediate(go);
            m_Created.Clear();
        }

        [Test]
        public void Snapshot_OrdersLayersTopFirst_AndMarksTheMaskAsBlocking()
        {
            var snapshot = UiObserver.Snapshot(new JObject());
            var layers = (JArray)snapshot["layers"];

            Assert.AreEqual("Base/GuideLayer", (string)layers[0]["id"]);
            Assert.IsTrue((bool)layers[0]["topmost"]);
            Assert.IsTrue((bool)layers[0]["blocksBelow"]);
            Assert.AreEqual("Base", (string)layers[1]["id"]);
        }

        [Test]
        public void Snapshot_ButtonInTheHoleIsHitTop_CoveredButtonNamesTheMask()
        {
            var snapshot = UiObserver.Snapshot(new JObject());

            var open = Node(snapshot, "Base/BtnOpen");
            Assert.IsTrue((bool)open["hitTop"], open.ToString());
            Assert.AreEqual("Open", (string)open["text"]);
            Assert.AreEqual("Button", (string)open["type"]);

            var covered = Node(snapshot, "Base/BtnCovered");
            Assert.IsFalse((bool)covered["hitTop"]);
            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)covered["coveredBy"]);
        }

        [Test]
        public void Snapshot_GuideReportsMaskHoleAndTarget()
        {
            var snapshot = UiObserver.Snapshot(new JObject());
            var guide = snapshot["guide"];

            Assert.IsTrue((bool)guide["active"]);
            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)guide["maskId"]);
            Assert.AreEqual("Base/BtnOpen", (string)guide["targetId"]);

            var hole = guide["holeRect"];
            var open = Node(snapshot, "Base/BtnOpen")["rect"];
            int step = (int)guide["step"];
            Assert.LessOrEqual(Mathf.Abs((int)hole["x"] - (int)open["x"]), step);
            Assert.LessOrEqual(Mathf.Abs((int)hole["y"] - (int)open["y"]), step);
            Assert.LessOrEqual(Mathf.Abs((int)hole["w"] - (int)open["w"]), step);
            Assert.LessOrEqual(Mathf.Abs((int)hole["h"] - (int)open["h"]), step);
        }

        [Test]
        public void Snapshot_HoleOverNoControl_ReportsHoleWithoutTarget()
        {
            var empty = Child(m_Created[0].transform, "EmptySpot", 0.1f, 0.6f, 0.3f, 0.7f);
            m_Mask.Target = empty;

            var guide = UiObserver.Snapshot(new JObject())["guide"];

            Assert.IsTrue((bool)guide["active"]);
            Assert.AreEqual(JTokenType.Object, guide["holeRect"].Type);
            Assert.IsNull((string)guide["targetId"]);
        }

        [Test]
        public void Snapshot_FullScreenImageAloneIsNotAGuide()
        {
            m_Mask.transform.parent.gameObject.SetActive(false);

            var snapshot = UiObserver.Snapshot(new JObject());

            Assert.IsFalse((bool)snapshot["guide"]["active"]);
            Assert.IsTrue((bool)Node(snapshot, "Base/BtnCovered")["hitTop"], snapshot.ToString());
        }

        [Test]
        public void Snapshot_PagesNodesWithTruncationMarker()
        {
            var snapshot = UiObserver.Snapshot(new JObject { ["max"] = "1" });

            Assert.AreEqual(1, ((JArray)snapshot["nodes"]).Count);
            Assert.AreEqual(3, (int)snapshot["total"]);
            Assert.IsTrue((bool)snapshot["truncated"]);
        }

        [Test]
        public void Hit_OnCoveredButton_TopIsTheMask()
        {
            var snapshot = UiObserver.Snapshot(new JObject());
            var rect = Node(snapshot, "Base/BtnCovered")["rect"];
            float x = (int)rect["x"] + (int)rect["w"] * 0.5f;
            float y = (int)rect["y"] + (int)rect["h"] * 0.5f;

            var hit = UiObserver.Hit(new JObject { ["x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture), ["y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture) });

            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)hit["top"]);
            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)hit["clickHandler"]);
        }

        [Test]
        public void MergeQuery_FillsMissingKeysOnly()
        {
            var req = new JObject { ["x"] = "1" };
            EditorDebugMCP.MergeQuery(new NameValueCollection { { "x", "9" }, { "y", "2" } }, req);

            Assert.AreEqual("1", (string)req["x"]);
            Assert.AreEqual("2", (string)req["y"]);
        }

        [Test]
        public void Snapshot_DecorativeCanvasAboveTheMask_IsNotTopmost()
        {
            // 仿 Cargo 引导手指：sortingOrder 更高、没有 GraphicRaycaster 的展示层
            var finger = Child(m_Created[0].transform, "Finger", 0.1f, 0.1f, 0.2f, 0.2f);
            var fingerCanvas = finger.gameObject.AddComponent<Canvas>();
            fingerCanvas.overrideSorting = true;
            fingerCanvas.sortingOrder = 101;

            var layers = (JArray)UiObserver.Snapshot(new JObject())["layers"];

            Assert.AreEqual("Base/Finger", (string)layers[0]["id"]);
            Assert.IsFalse((bool)layers[0]["receivesInput"]);
            Assert.IsFalse((bool)layers[0]["topmost"]);
            Assert.AreEqual("Base/GuideLayer", (string)layers[1]["id"]);
            Assert.IsTrue((bool)layers[1]["topmost"]);
        }

        [Test]
        public void Snapshot_SameNamedRootCanvasesUnderDifferentParents_GetDistinctIds()
        {
            foreach (var rig in new[] { "RigA", "RigB" })
            {
                var parent = new GameObject(rig);
                m_Created.Add(parent);
                var area = new GameObject("ClickArea", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                area.transform.SetParent(parent.transform, false);
                area.AddComponent<Image>();
                area.AddComponent<Button>();
            }

            var snapshot = UiObserver.Snapshot(new JObject());

            Node(snapshot, "RigA/ClickArea");
            Node(snapshot, "RigB/ClickArea");
        }

        [Test]
        public void GraphicsWithoutCanvasRenderer_AreSkipped()
        {
            // Cargo 内城：世界空间 TextMeshPro 散在 canvas 外；canvas 内也可能挂着没有 CanvasRenderer 的 Graphic
            var world = new GameObject("WorldLabel", typeof(RectTransform), typeof(TestBareGraphic));
            m_Created.Add(world);
            var inCanvas = Child(m_Created[0].transform, "BareInCanvas", 0.6f, 0.6f, 0.9f, 0.8f);
            inCanvas.gameObject.AddComponent<TestBareGraphic>();
            Assert.IsNull(world.GetComponent<CanvasRenderer>());

            var snapshot = UiObserver.Snapshot(new JObject());
            var rect = Node(snapshot, "Base/BtnCovered")["rect"];
            float x = (int)rect["x"] + (int)rect["w"] * 0.5f;
            float y = (int)rect["y"] + (int)rect["h"] * 0.5f;
            var hit = UiObserver.Hit(new JObject { ["x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture), ["y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture) });

            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)hit["top"]);
        }

        // ---- /ui/act（live=false：EditMode 下没有 Play 与 EventSystem）----

        [Test]
        public void Act_ClickInTheHole_InvokesOnClick()
        {
            int clicks = 0;
            m_Open.GetComponent<Button>().onClick.AddListener(() => clicks++);

            var result = UiActor.Act(new JObject { ["id"] = "Base/BtnOpen" }, live: false);

            Assert.IsTrue((bool)result["acted"], result.ToString());
            Assert.AreEqual(1, clicks);
            Assert.AreEqual("Base/BtnOpen", (string)result["clickHandler"]);
        }

        [Test]
        public void Act_CoveredButton_RefusesAndNamesTheMask()
        {
            int clicks = 0;
            m_Covered.GetComponent<Button>().onClick.AddListener(() => clicks++);

            var result = UiActor.Act(new JObject { ["id"] = "Base/BtnCovered" }, live: false);

            Assert.IsFalse((bool)result["acted"]);
            Assert.AreEqual("covered", (string)result["reason"]);
            Assert.AreEqual("Base/GuideLayer/GuideMask", (string)result["coveredBy"]);
            Assert.AreEqual(0, clicks);
        }

        [Test]
        public void Act_RefusesUnknownIdsAndNonInteractableControls()
        {
            Assert.AreEqual("not_found", (string)UiActor.Act(new JObject { ["id"] = "Base/Nope" }, live: false)["reason"]);

            int clicks = 0;
            var button = m_Open.GetComponent<Button>();
            button.onClick.AddListener(() => clicks++);
            button.interactable = false;
            Assert.AreEqual("not_interactable", (string)UiActor.Act(new JObject { ["id"] = "Base/BtnOpen" }, live: false)["reason"]);
            Assert.AreEqual(0, clicks);
        }

        [Test]
        public void Act_LiveRequestOutsidePlayMode_IsRefused()
        {
            var result = UiActor.Act(new JObject { ["id"] = "Base/BtnOpen" }, live: true);

            Assert.IsFalse((bool)result["acted"]);
            Assert.AreEqual("not_playing", (string)result["reason"]);
        }

        [Test]
        public void Act_Drag_DeliversTheModuleSequenceAndEndsAtTo()
        {
            m_Mask.transform.parent.gameObject.SetActive(false);
            var area = Child(m_Created[0].transform, "DragArea", 0.1f, 0.6f, 0.3f, 0.7f);
            area.gameObject.AddComponent<Image>();
            var receiver = area.gameObject.AddComponent<TestDragReceiver>();
            var rect = Node(UiObserver.Snapshot(new JObject()), "Base/DragArea")["rect"];
            int toX = (int)rect["x"] + (int)rect["w"] / 2 + 100;
            int toY = (int)rect["y"] + (int)rect["h"] / 2 + 50;

            var result = UiActor.Act(new JObject
            {
                ["id"] = "Base/DragArea",
                ["action"] = "drag",
                ["to"] = new JObject { ["x"] = toX, ["y"] = toY },
                ["steps"] = 4,
            }, live: false);

            Assert.IsTrue((bool)result["acted"], result.ToString());
            Assert.AreEqual(1, receiver.Init);
            Assert.AreEqual(1, receiver.Begin);
            Assert.AreEqual(4, receiver.Drags);
            Assert.AreEqual(1, receiver.End);
            Assert.AreEqual(0, receiver.Clicks, "a drag must not also click");
            Assert.AreEqual(new Vector2(toX, Screen.height - toY), receiver.Last);
        }

        [Test]
        public void Act_ChildOfADisabledButton_IsRefusedOnTheReceiver()
        {
            // 命中落在子 Label 上、冒泡到禁用的 Button：真机点了也不触发 onClick
            var label = m_Open.Find("Label").GetComponent<Text>();
            label.raycastTarget = true;
            m_Open.GetComponent<Button>().interactable = false;

            var result = UiActor.Act(new JObject { ["id"] = "Base/BtnOpen/Label" }, live: false);

            Assert.AreEqual("not_interactable", (string)result["reason"], result.ToString());
            Assert.AreEqual("Base/BtnOpen", (string)result["receiver"]);
        }

        [Test]
        public void Act_Click_SendsEnterAndExitUpTheParentChain()
        {
            var recorder = m_Created[0].AddComponent<TestPointerRecorder>();

            Assert.IsTrue((bool)UiActor.Act(new JObject { ["id"] = "Base/BtnOpen" }, live: false)["acted"]);

            Assert.AreEqual(1, recorder.Enters, "the root sees the pointer arrive");
            Assert.AreEqual(1, recorder.Exits, "and leave when the finger lifts");
        }

        [Test]
        public void Act_Drag_DropsOnATargetRevealedByBeginDrag()
        {
            m_Mask.transform.parent.gameObject.SetActive(false);
            var source = Child(m_Created[0].transform, "Source", 0.1f, 0.1f, 0.3f, 0.2f);
            source.gameObject.AddComponent<Image>();
            var receiver = source.gameObject.AddComponent<TestDragReceiver>();
            // 后建的兄弟画在上层，盖住背景；开始拖拽前未激活
            var zone = Child(m_Created[0].transform, "DropZone", 0.6f, 0.1f, 0.9f, 0.3f);
            zone.gameObject.AddComponent<Image>();
            var drop = zone.gameObject.AddComponent<TestPointerRecorder>();
            receiver.RevealOnBegin = zone.gameObject;
            var zoneRect = Node(UiObserver.Snapshot(new JObject { ["include"] = "all" }), "Base/DropZone")["rect"];
            zone.gameObject.SetActive(false);

            var result = UiActor.Act(new JObject
            {
                ["id"] = "Base/Source",
                ["action"] = "drag",
                ["to"] = new JObject { ["x"] = (int)zoneRect["x"] + (int)zoneRect["w"] / 2, ["y"] = (int)zoneRect["y"] + (int)zoneRect["h"] / 2 },
            }, live: false);

            Assert.IsTrue((bool)result["acted"], result.ToString());
            Assert.AreEqual("Base/DropZone", (string)result["release"]);
            Assert.AreEqual(1, drop.Drops);
            Assert.AreEqual(1, drop.Enters, "the drop zone is hovered during the drag");
            Assert.AreEqual(1, receiver.End);
        }

        [Test]
        public void Act_Iid_FiltersTheIdMatches()
        {
            // 同 id 跨场景重名时靠 iid 消歧；EditMode 下未保存的 untitled 场景不能叠加新场景，这里只验证过滤本身
            int clicks = 0;
            m_Open.GetComponent<Button>().onClick.AddListener(() => clicks++);

            var stale = UiActor.Act(new JObject { ["id"] = "Base/BtnOpen", ["iid"] = m_Covered.gameObject.GetInstanceID() }, live: false);
            Assert.AreEqual("not_found", (string)stale["reason"]);

            var picked = UiActor.Act(new JObject { ["id"] = "Base/BtnOpen", ["iid"] = m_Open.gameObject.GetInstanceID() }, live: false);
            Assert.IsTrue((bool)picked["acted"], picked.ToString());
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void Act_MalformedParameters_NameTheKey()
        {
            var e = Assert.Throws<System.ArgumentException>(() => UiActor.Act(new JObject { ["id"] = "Base/BtnOpen", ["steps"] = "abc" }, live: false));
            StringAssert.Contains("'steps'", e.Message);
            e = Assert.Throws<System.ArgumentException>(() => UiActor.Act(new JObject { ["id"] = "Base/BtnOpen", ["at"] = new JObject { ["x"] = null, ["y"] = 1 } }, live: false));
            StringAssert.Contains("'at.x'", e.Message);
            Assert.IsTrue((bool)UiActor.Act(new JObject { ["id"] = "Base/BtnOpen", ["steps"] = null }, live: false)["acted"], "JSON null means absent");
        }

        [Test]
        public void GuardUiWrite_ActNeedsJsonPost_OtherPathsUntouched()
        {
            Assert.Throws<System.UnauthorizedAccessException>(() => EditorDebugMCP.GuardUiWrite("/ui/act", "GET", null));
            Assert.Throws<System.UnauthorizedAccessException>(() => EditorDebugMCP.GuardUiWrite("/ui/act", "POST", "application/x-www-form-urlencoded"));
            Assert.Throws<System.UnauthorizedAccessException>(() => EditorDebugMCP.GuardUiWrite("/ui/act", "POST", "text/plain"));
            EditorDebugMCP.GuardUiWrite("/ui/act", "POST", "application/json; charset=utf-8");
            EditorDebugMCP.GuardUiWrite("/ui/snapshot", "GET", null);
        }

        [Test]
        public void Snapshot_InvalidPagingArgument_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => UiObserver.Snapshot(new JObject { ["max"] = "10O" }));
            Assert.Throws<System.ArgumentException>(() => UiObserver.Hit(new JObject { ["x"] = "NaN", ["y"] = "1" }));
        }

        private static JToken Node(JObject snapshot, string id)
        {
            foreach (var node in (JArray)snapshot["nodes"])
            {
                if ((string)node["id"] == id)
                    return node;
            }
            Assert.Fail("node not found: " + id + "\n" + snapshot["nodes"]);
            return null;
        }

        private static RectTransform Child(Transform parent, string name, float minX, float minY, float maxX, float maxY)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}

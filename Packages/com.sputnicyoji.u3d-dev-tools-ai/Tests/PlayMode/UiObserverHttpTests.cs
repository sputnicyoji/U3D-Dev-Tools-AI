#if UNITY_EDITOR
using System.Collections;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Yoji.EditorDebug.Tests
{
    public sealed class UiObserverHttpTests
    {
        private GameObject m_Root;
        private GameObject m_EventRoot;
        private UnityEditor.EditorWindow m_View;
        private object m_Group;
        private int m_PreviousSize;
        private int m_AddedSizeIndex;

        [TearDown]
        public void TearDown()
        {
            if (m_Root != null) Object.DestroyImmediate(m_Root);
            if (m_EventRoot != null) Object.DestroyImmediate(m_EventRoot);
            if (m_View == null) return;
            m_View.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(m_View, m_PreviousSize);
            // RemoveCustomSize takes a total index (builtin + custom), not a custom-list index.
            m_Group.GetType().GetMethod("RemoveCustomSize").Invoke(m_Group, new object[] { m_AddedSizeIndex });
        }

        [UnityTest]
        public IEnumerator HttpObservation_MatchesGameFrameViewportAndRaycast()
        {
            SetViewport();
            yield return null;
            yield return null;
            var root = new GameObject("HttpViewport", typeof(Canvas), typeof(GraphicRaycaster));
            m_Root = root;
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = short.MaxValue;
            var button = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(root.transform, false);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(100, 100);
            var eventRoot = EventSystem.current == null ? new GameObject("HttpEvents", typeof(EventSystem)) : null;
            m_EventRoot = eventRoot;
            var probe = root.AddComponent<TestViewportProbe>();
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 10;
            while ((probe.Top == null || probe.Width != 1080 || probe.Height != 1920) && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                yield return null;
            var width = probe.Width;
            var height = probe.Height;
            Assert.That(probe.Top, Is.SameAs(button));
            var port = Port();
            var snapshotTask = Task.Run(() => Read(port, "/ui/snapshot", "snapshot"));
            while (!snapshotTask.IsCompleted) yield return null;
            var snapshot = snapshotTask.GetAwaiter().GetResult();
            var hitTask = Task.Run(() => Read(port, "/ui/hit?x=" + width / 2 + "&y=" + height / 2, "hit"));
            while (!hitTask.IsCompleted) yield return null;
            var hit = hitTask.GetAwaiter().GetResult();
            WriteReceipt("frame", new JObject { ["width"] = width, ["height"] = height, ["nativeTop"] = probe.Top.name });
            Assert.That(width, Is.EqualTo(1080));
            Assert.That(height, Is.EqualTo(1920));
            Assert.That((bool)snapshot["ok"], Is.True, snapshot.ToString());
            Assert.That((bool)hit["ok"], Is.True, hit.ToString());
            Assert.That((int)snapshot["result"]["meta"]["screen"]["w"], Is.EqualTo(width));
            Assert.That((int)snapshot["result"]["meta"]["screen"]["h"], Is.EqualTo(height));
            Assert.That((int)hit["result"]["screen"]["w"], Is.EqualTo(width));
            Assert.That((int)hit["result"]["screen"]["h"], Is.EqualTo(height));
            Assert.That((string)hit["result"]["top"], Is.EqualTo("HttpViewport/Button"));
            var node = Node(snapshot, "HttpViewport/Button");
            Assert.That(node, Is.Not.Null, snapshot.ToString());
            Assert.That((bool)node["hitTop"], Is.True);
        }

        [UnityTest]
        public IEnumerator HttpAct_ClicksOffCentreButtonInGameFrame()
        {
            SetViewport();
            yield return null;
            yield return null;
            var root = new GameObject("HttpAct", typeof(Canvas), typeof(GraphicRaycaster));
            m_Root = root;
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = short.MaxValue;
            // Off centre (Unity y = 1632 of 1920): a press checked against smaller Editor-window dimensions reads as offscreen.
            var clicks = 0;
            AddButton(root.transform, "Corner", new Vector2(.85f, .85f), Vector2.zero).onClick.AddListener(() => clicks++);
            // Centre past the right edge: a press checked against a wider Editor-window width would click it.
            var offscreenClicks = 0;
            AddButton(root.transform, "Offscreen", new Vector2(1f, .85f), new Vector2(120, 0)).onClick.AddListener(() => offscreenClicks++);
            m_EventRoot = EventSystem.current == null ? new GameObject("HttpEvents", typeof(EventSystem)) : null;
            var probe = root.AddComponent<TestViewportProbe>();
            // Screen as the Editor update tick (plain RunOnMain) sees it, kept in the receipt as evidence.
            var editorTick = Vector2Int.zero;
            UnityEditor.EditorApplication.CallbackFunction sample = () => editorTick = new Vector2Int(Screen.width, Screen.height);
            UnityEditor.EditorApplication.update += sample;
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 10;
            while ((probe.Width != 1080 || probe.Height != 1920) && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                yield return null;
            yield return null;
            UnityEditor.EditorApplication.update -= sample;
            Assert.That(new Vector2Int(probe.Width, probe.Height), Is.EqualTo(new Vector2Int(1080, 1920)));
            var port = Port();
            var snapshotTask = Task.Run(() => Read(port, "/ui/snapshot", "act-snapshot"));
            while (!snapshotTask.IsCompleted) yield return null;
            var snapshot = snapshotTask.GetAwaiter().GetResult();
            Assert.That((bool)snapshot["ok"], Is.True, snapshot.ToString());
            var corner = Node(snapshot, "HttpAct/Corner");
            var offscreen = Node(snapshot, "HttpAct/Offscreen");
            Assert.That(corner, Is.Not.Null, snapshot.ToString());
            Assert.That(offscreen, Is.Not.Null, snapshot.ToString());
            Assert.That((bool)corner["hitTop"], Is.True);

            var actTask = Task.Run(() => Read(port, "/ui/act", "act", new JObject { ["id"] = (string)corner["id"] }));
            while (!actTask.IsCompleted) yield return null;
            var act = actTask.GetAwaiter().GetResult();
            var refusedTask = Task.Run(() => Read(port, "/ui/act", "act-offscreen", new JObject { ["id"] = (string)offscreen["id"] }));
            while (!refusedTask.IsCompleted) yield return null;
            var refused = refusedTask.GetAwaiter().GetResult();
            WriteReceipt("act-frame", new JObject { ["width"] = probe.Width, ["height"] = probe.Height, ["editorTickWidth"] = editorTick.x, ["editorTickHeight"] = editorTick.y });

            Assert.That((bool)act["ok"], Is.True, act.ToString());
            Assert.That((bool)act["result"]["acted"], Is.True, act.ToString());
            Assert.That((string)act["result"]["top"], Is.EqualTo("HttpAct/Corner"));
            Assert.That((string)act["result"]["clickHandler"], Is.EqualTo("HttpAct/Corner"));
            var rect = corner["rect"];
            Assert.That((int)act["result"]["at"]["x"], Is.EqualTo((int)rect["x"] + (int)rect["w"] / 2).Within(1), act.ToString());
            Assert.That((int)act["result"]["at"]["y"], Is.EqualTo((int)rect["y"] + (int)rect["h"] / 2).Within(1), act.ToString());
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That((bool)refused["ok"], Is.True, refused.ToString());
            Assert.That((bool)refused["result"]["acted"], Is.False, refused.ToString());
            Assert.That((string)refused["result"]["reason"], Is.EqualTo("offscreen"), refused.ToString());
            Assert.That(offscreenClicks, Is.EqualTo(0));
            Assert.That(clicks, Is.EqualTo(1));
        }

        private void SetViewport()
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kind = System.Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
            var size = System.Activator.CreateInstance(sizeType, new object[] { kind, 1080, 1920, "HTTP viewport test" });
            m_Group = group;
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            var count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            m_AddedSizeIndex = count - 1;
            var viewType = assembly.GetType("UnityEditor.GameView");
            var view = UnityEditor.EditorWindow.GetWindow(viewType);
            m_View = view;
            var selected = viewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m_PreviousSize = (int)selected.GetValue(view);
            selected.SetValue(view, count - 1);
            view.Focus();
            view.Repaint();
        }

        private static void WriteReceipt(string name, JObject receipt)
        {
            var folder = System.IO.Path.Combine(System.Environment.CurrentDirectory, "Logs", "UiObserverHttp");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, name + ".json"), receipt.ToString());
        }

        private static int Port() =>
            (int)System.Type.GetType("Yoji.EditorDebug.EditorDebugMCP, Yoji.EditorDebug.Editor").GetField("s_Port", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static Button AddButton(Transform parent, string name, Vector2 anchor, Vector2 position)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            button.transform.SetParent(parent, false);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(120, 120);
            return button.GetComponent<Button>();
        }

        private static JToken Node(JObject snapshot, string id)
        {
            foreach (var candidate in (JArray)snapshot["result"]["nodes"])
                if ((string)candidate["id"] == id) return candidate;
            return null;
        }

        /// A null body sends GET; otherwise POST with a JSON body, as /ui/act requires.
        private static JObject Read(int port, string path, string receipt, JObject body = null)
        {
            var request = WebRequest.CreateHttp("http://127.0.0.1:" + port + path);
            request.Timeout = 15000;
            if (body != null)
            {
                request.Method = "POST";
                request.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes(body.ToString());
                using (var stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = request.GetResponse())
            using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
            {
                var result = JObject.Parse(reader.ReadToEnd());
                WriteReceipt(receipt, new JObject { ["request"] = path, ["body"] = body, ["response"] = result });
                return result;
            }
        }
    }
}

#endif

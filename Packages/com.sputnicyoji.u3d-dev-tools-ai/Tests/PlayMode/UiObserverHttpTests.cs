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
            var port = (int)System.Type.GetType("Yoji.EditorDebug.EditorDebugMCP, Yoji.EditorDebug.Editor").GetField("s_Port", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var snapshotTask = Task.Run(() => Read(port, "/ui/snapshot"));
            while (!snapshotTask.IsCompleted) yield return null;
            var snapshot = snapshotTask.GetAwaiter().GetResult();
            var hitTask = Task.Run(() => Read(port, "/ui/hit?x=" + width / 2 + "&y=" + height / 2));
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
            var node = ((JArray)snapshot["result"]["nodes"]).First;
            foreach (var candidate in (JArray)snapshot["result"]["nodes"])
                if ((string)candidate["id"] == "HttpViewport/Button") node = candidate;
            Assert.That((string)node["id"], Is.EqualTo("HttpViewport/Button"));
            Assert.That((bool)node["hitTop"], Is.True);
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

        private static JObject Read(int port, string path)
        {
            var request = WebRequest.CreateHttp("http://127.0.0.1:" + port + path);
            request.Timeout = 15000;
            using (var response = request.GetResponse())
            using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
            {
                var result = JObject.Parse(reader.ReadToEnd());
                WriteReceipt(path.StartsWith("/ui/snapshot") ? "snapshot" : "hit", new JObject { ["request"] = path, ["response"] = result });
                return result;
            }
        }
    }
}

#endif

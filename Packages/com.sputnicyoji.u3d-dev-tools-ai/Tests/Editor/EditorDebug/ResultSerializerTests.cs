using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yoji.EditorDebug.Tests
{
    public class ResultSerializerTests
    {
        [Test] public void Primitive_Int()
            => Assert.AreEqual(3, (int)ResultSerializer.ToJson(3));

        [Test] public void Primitive_String()
            => Assert.AreEqual("abc", (string)ResultSerializer.ToJson("abc"));

        [Test] public void Null_ReturnsJsonNull()
            => Assert.AreEqual(JTokenType.Null, ResultSerializer.ToJson(null).Type);

        // ToPayload：/invoke 与 /batch 共用的结果整形，三分支不可发散。
        [Test] public void ToPayload_VoidResult_SetsVoidFlag()
        {
            var p = ResultSerializer.ToPayload(VoidResult.Instance);
            Assert.IsTrue((bool)p["void"]);
            Assert.AreEqual(JTokenType.Null, p["result"].Type);
        }

        [Test] public void ToPayload_JToken_PassesThroughSameInstance()
        {
            var jt = new JObject { ["a"] = 1 };
            var p = ResultSerializer.ToPayload(jt);
            Assert.IsFalse(p.ContainsKey("void"));
            Assert.AreSame(jt, p["result"]);
        }

        [Test] public void ToPayload_PlainObject_Serializes()
            => Assert.AreEqual(42, (int)ResultSerializer.ToPayload(42)["result"]);

        [Test] public void Enum_AsName()
            => Assert.AreEqual("A", (string)ResultSerializer.ToJson(KeyCode.A));

        [Test] public void FloatNaN_AsString()
            => Assert.AreEqual("NaN", (string)ResultSerializer.ToJson(float.NaN));

        // 非有限数与数字键一律按 InvariantCulture 输出，不随本机区域（中文系统曾输出"非數值"）。
        private static void InCulture(string name, Action body)
        {
            var thread = System.Threading.Thread.CurrentThread;
            var saved = thread.CurrentCulture;
            thread.CurrentCulture = new System.Globalization.CultureInfo(name);
            try { body(); }
            finally { thread.CurrentCulture = saved; }
        }

        // zh-CN 是约定的回归区域；zh-TW 是本机区域，Mono 下它的 NaN 符号才是"非數值"，真正复现旧 bug。
        [TestCase("zh-CN")]
        [TestCase("zh-TW")]
        public void NonFinite_UnderChineseCulture_UseInvariantNames(string culture)
            => InCulture(culture, () =>
            {
                Assert.AreEqual("NaN", (string)ResultSerializer.ToJson(float.NaN));
                Assert.AreEqual("NaN", (string)ResultSerializer.ToJson(double.NaN));
                Assert.AreEqual("Infinity", (string)ResultSerializer.ToJson(float.PositiveInfinity));
                Assert.AreEqual("-Infinity", (string)ResultSerializer.ToJson(double.NegativeInfinity));
            });

        [TestCase("zh-CN")]
        [TestCase("zh-TW")]
        public void NumericDictionaryKeys_UnderChineseCulture_RoundTripInvariant(string culture)
            => InCulture(culture, () =>
            {
                var d = new Dictionary<double, int> { [0.1] = 1, [1e-7] = 2 };
                var f = new Dictionary<float, int> { [0.1f] = 3 };
                var jd = (JObject)ResultSerializer.ToJson(d);
                var jf = (JObject)ResultSerializer.ToJson(f);
                CollectionAssert.AreEquivalent(new[] { 0.1, 1e-7 },
                    jd.Properties().Select(p => double.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)));
                Assert.AreEqual(0.1f, float.Parse(jf.Properties().Single().Name, System.Globalization.CultureInfo.InvariantCulture));
            });

        [Test] public void Culture_IsRestoredAfterInCulture()
        {
            var before = System.Threading.Thread.CurrentThread.CurrentCulture.Name;
            InCulture("zh-CN", () => { });
            Assert.AreEqual(before, System.Threading.Thread.CurrentThread.CurrentCulture.Name);
        }
        [Test] public void GameObject_BecomesRefSummary()
        {
            var parent = new GameObject("Root");
            var child = new GameObject("Player");
            child.transform.SetParent(parent.transform);
            try
            {
                var j = (JObject)ResultSerializer.ToJson(child);
                Assert.IsTrue((bool)j["__ref"]);
#if UNITY_6000_4_OR_NEWER
                Assert.AreEqual(
                    EntityId.ToULong(child.GetEntityId()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (string)j["instanceID"]);
#else
                Assert.AreEqual(child.GetInstanceID(), (int)j["instanceID"]);
#endif
                Assert.AreEqual("UnityEngine.GameObject", (string)j["type"]);
                Assert.AreEqual("Player", (string)j["name"]);
                Assert.AreEqual("Root/Player", (string)j["path"]);
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test] public void DestroyedObject_BecomesNull()
        {
            var go = new GameObject("Doomed");
            UnityEngine.Object.DestroyImmediate(go);
            Assert.AreEqual(JTokenType.Null, ResultSerializer.ToJson(go).Type);
        }

        [Test] public void Array_BecomesJArray()
        {
            var j = (JArray)ResultSerializer.ToJson(new[] { 1, 2, 3 });
            Assert.AreEqual(3, j.Count);
            Assert.AreEqual(2, (int)j[1]);
        }

        [Test] public void Dictionary_BecomesJObject()
        {
            var j = (JObject)ResultSerializer.ToJson(new Dictionary<string, int> { ["a"] = 1 });
            Assert.AreEqual(1, (int)j["a"]);
        }

        [Test] public void Delegate_BecomesPlaceholder()
        {
            Action a = InvokeProbe.Reset;
            var j = (JObject)ResultSerializer.ToJson(a);
            Assert.IsTrue((bool)j["__delegate"]);
            StringAssert.Contains("Reset", (string)j["target"]);
        }

        [Test] public void IntPtr_BecomesSkipped()
        {
            var j = (JObject)ResultSerializer.ToJson(new IntPtr(123));
            Assert.AreEqual("IntPtr", (string)j["__skipped"]);
        }

        [Test] public void Enumerator_BecomesPlaceholder()
        {
            var j = (JObject)ResultSerializer.ToJson(new List<int>().GetEnumerator());
            Assert.IsNotNull(j["__enumerator"]);
        }

        [Test] public void PlainObject_PublicFieldsAndProps()
        {
            var j = (JObject)ResultSerializer.ToJson(new InvokeProbe());
            Assert.AreEqual(5, (int)j["InstanceValue"]);
        }

        [Test] public void DeepNesting_TruncatedAtMaxDepth()
        {
            object node = "leaf";
            for (int i = 0; i < 20; i++) node = new List<object> { node };
            var json = ResultSerializer.ToJson(node).ToString();
            StringAssert.Contains("maxDepth", json);
        }

        [Test] public void ManyNodes_TruncatedAtMaxNodes()
        {
            var big = new List<int>();
            for (int i = 0; i < 3000; i++) big.Add(i);
            var json = ResultSerializer.ToJson(big).ToString();
            StringAssert.Contains("maxNodes", json);
        }

        [Test] public void VisualElement_TreeSummary()
        {
            var root = new VisualElement { name = "GamePlayRoot" };
            root.AddToClassList("ux-td-gameplay");
            var btn = new Button { name = "PauseBtn", text = "||" };
            root.Add(btn);

            var j = (JObject)ResultSerializer.ToJson(root);
            Assert.AreEqual("GamePlayRoot", (string)j["name"]);
            Assert.AreEqual("VisualElement", (string)j["type"]);
            Assert.AreEqual("ux-td-gameplay", (string)j["classList"][0]);
            Assert.AreEqual("NaN", (string)j["layout"]["w"]);
            Assert.AreEqual(1, (int)j["childCount"]);
            var c = (JObject)j["children"][0];
            Assert.AreEqual("Button", (string)c["type"]);
            Assert.AreEqual("||", (string)c["text"]);
        }

        [Test] public void FaultedTask_BecomesErrorNode()
        {
            var t = System.Threading.Tasks.Task.FromException<int>(new InvalidOperationException("boom"));
            var j = (JObject)ResultSerializer.ToJson(t);
            Assert.AreEqual("InvalidOperationException", (string)j["__error"]);
            StringAssert.Contains("boom", (string)j["message"]);
        }

        [Test] public void HugeDictionary_TruncatesWithoutNonSpecKey()
        {
            var big = new Dictionary<string, int>();
            for (int i = 0; i < 3000; i++) big["k" + i] = i;
            var json = ResultSerializer.ToJson(big).ToString();
            StringAssert.DoesNotContain("__truncated_entry", json);
        }
    }
}

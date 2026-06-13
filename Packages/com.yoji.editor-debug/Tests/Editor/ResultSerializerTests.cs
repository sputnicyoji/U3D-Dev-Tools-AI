using System;
using System.Collections.Generic;
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

        [Test] public void Enum_AsName()
            => Assert.AreEqual("A", (string)ResultSerializer.ToJson(KeyCode.A));

        [Test] public void FloatNaN_AsString()
            => Assert.AreEqual("NaN", (string)ResultSerializer.ToJson(float.NaN));

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

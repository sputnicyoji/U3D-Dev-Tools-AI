using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    public class ReflectionInvokerTests
    {
        private const string Probe = "Yoji.EditorDebug.Tests.InvokeProbe";

        [SetUp] public void SetUp() => InvokeProbe.Reset();

        private static object Exec(string json) => ReflectionInvoker.Execute(JObject.Parse(json));

        [Test] public void Get_StaticProperty()
            => Assert.AreEqual("hello", Exec("{\"type\":\"" + Probe + "\",\"member\":\"StaticProp\",\"kind\":\"get\"}"));

        [Test] public void Get_PrivateStaticField()
            => Assert.AreEqual(42, Exec("{\"type\":\"" + Probe + "\",\"member\":\"s_Secret\",\"kind\":\"get\"}"));

        [Test] public void Get_MissingMember_Throws()
            => Assert.Throws<MissingMethodException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"NoSuch\",\"kind\":\"get\"}"));

        [Test] public void Set_StaticField()
        {
            var r = Exec("{\"type\":\"" + Probe + "\",\"member\":\"StaticField\",\"kind\":\"set\",\"args\":[9]}");
            Assert.IsInstanceOf<VoidResult>(r);
            Assert.AreEqual(9, InvokeProbe.StaticField);
        }

        [Test] public void Call_SingleOverload()
            => Assert.AreEqual(10, Exec("{\"type\":\"" + Probe + "\",\"member\":\"Add\",\"kind\":\"call\",\"args\":[3,7]}"));

        [Test] public void Call_VoidMethod_ReturnsVoidResult()
            => Assert.IsInstanceOf<VoidResult>(
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"DoNothing\",\"kind\":\"call\"}"));

        [Test] public void Call_OptionalParam_UsesDefault()
            => Assert.AreEqual(12, Exec("{\"type\":\"" + Probe + "\",\"member\":\"Mul\",\"kind\":\"call\",\"args\":[4]}"));

        [Test] public void Call_ArgTypes_PicksIntOverload()
            => Assert.AreEqual("int", Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\"," +
                "\"args\":[5],\"argTypes\":[\"System.Int32\"]}"));

        [Test] public void Call_ArgTypes_PicksStringOverload()
            => Assert.AreEqual("string", Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\"," +
                "\"args\":[\"x\"],\"argTypes\":[\"System.String\"]}"));

        [Test] public void Call_Ambiguous_Throws()
            => Assert.Throws<AmbiguousMatchException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"Kind\",\"kind\":\"call\",\"args\":[5]}"));

        [Test] public void Call_MissingMethod_Throws()
            => Assert.Throws<MissingMethodException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"NoMethod\",\"kind\":\"call\"}"));

        [Test] public void Call_EnumArg()
            => Assert.AreEqual("A", Exec("{\"type\":\"" + Probe + "\",\"member\":\"EnumName\",\"kind\":\"call\"," +
                "\"args\":[\"UnityEngine.KeyCode.A\"]}"));

        [Test] public void Call_MathfMin_WithArgTypes()
            => Assert.AreEqual(3, Exec("{\"type\":\"UnityEngine.Mathf\",\"member\":\"Min\",\"kind\":\"call\"," +
                "\"args\":[3,7],\"argTypes\":[\"System.Int32\",\"System.Int32\"]}"));

        [Test] public void Chain_CallThenIndex()
            => Assert.AreEqual(2, Exec("{\"type\":\"" + Probe + "\",\"steps\":[" +
                "{\"member\":\"Numbers\",\"kind\":\"call\"},{\"kind\":\"index\",\"args\":[1]}]}"));

        [Test] public void Chain_CallThenGet()
            => Assert.AreEqual(3, Exec("{\"type\":\"" + Probe + "\",\"steps\":[" +
                "{\"member\":\"Numbers\",\"kind\":\"call\"},{\"member\":\"Count\",\"kind\":\"get\"}]}"));

        [Test] public void TargetInstanceId_ResolvesObject()
        {
            var go = new GameObject("InvokerProbe");
            try
            {
                var r = Exec("{\"type\":\"UnityEngine.GameObject\",\"target\":{\"instanceID\":" +
                    go.GetInstanceID() + "},\"member\":\"name\",\"kind\":\"get\"}");
                Assert.AreEqual("InvokerProbe", r);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void TypeNotFound_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() =>
                Exec("{\"type\":\"Foo.NotExisting.For.E2E\",\"member\":\"X\",\"kind\":\"get\"}"));

        [Test] public void UnknownKind_Throws()
            => Assert.Throws<ArgumentException>(() =>
                Exec("{\"type\":\"" + Probe + "\",\"member\":\"Add\",\"kind\":\"frobnicate\"}"));

        [Test] public void SceneChain_LikeE2ECase04()
        {
            var r = Exec("{\"type\":\"UnityEngine.SceneManagement.SceneManager\",\"steps\":[" +
                "{\"member\":\"GetActiveScene\",\"kind\":\"call\"},{\"member\":\"GetRootGameObjects\",\"kind\":\"call\"}]}");
            Assert.IsInstanceOf<GameObject[]>(r);
        }
    }
}

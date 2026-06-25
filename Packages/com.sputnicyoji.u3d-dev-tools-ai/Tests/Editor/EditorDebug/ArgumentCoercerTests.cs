using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    public class ArgumentCoercerTests
    {
        private static JToken J(string json) => JToken.Parse(json);

        [Test] public void Coerce_Null_ReturnsNull()
            => Assert.IsNull(ArgumentCoercer.Coerce(J("null"), typeof(string)));

        [Test] public void Coerce_IntToInt32()
            => Assert.AreEqual(3, ArgumentCoercer.Coerce(J("3"), typeof(int)));

        [Test] public void Coerce_IntToSingle()
            => Assert.AreEqual(3f, ArgumentCoercer.Coerce(J("3"), typeof(float)));

        [Test] public void Coerce_StringToType()
            => Assert.AreEqual(typeof(GameObject),
                ArgumentCoercer.Coerce(J("\"UnityEngine.GameObject\""), typeof(Type)));

        [Test] public void Coerce_DottedStringToEnum()
            => Assert.AreEqual(KeyCode.A,
                ArgumentCoercer.Coerce(J("\"UnityEngine.KeyCode.A\""), typeof(KeyCode)));

        [Test] public void Coerce_BareStringToEnum()
            => Assert.AreEqual(KeyCode.Space, ArgumentCoercer.Coerce(J("\"Space\""), typeof(KeyCode)));

        [Test] public void Coerce_NumberToEnum()
            => Assert.AreEqual(KeyCode.A, ArgumentCoercer.Coerce(J(((int)KeyCode.A).ToString()), typeof(KeyCode)));

        [Test] public void Coerce_InstanceIdObjectToUnityObject()
        {
            var go = new GameObject("CoerceProbe");
            try
            {
                var arg = J("{\"instanceID\":" + go.GetInstanceID() + "}");
                Assert.AreSame(go, ArgumentCoercer.Coerce(arg, typeof(GameObject)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void ParseEntityIdRaw_AcceptsFullUInt64String()
            => Assert.AreEqual(ulong.MaxValue,
                ArgumentCoercer.ParseEntityIdRaw(J("\"18446744073709551615\"")));

        [Test] public void Coerce_ToObject_KeepsNaturalJsonType()
        {
            Assert.AreEqual(5L, ArgumentCoercer.Coerce(J("5"), typeof(object)));
            Assert.AreEqual("x", ArgumentCoercer.Coerce(J("\"x\""), typeof(object)));
            Assert.AreEqual(true, ArgumentCoercer.Coerce(J("true"), typeof(object)));
        }
    }
}

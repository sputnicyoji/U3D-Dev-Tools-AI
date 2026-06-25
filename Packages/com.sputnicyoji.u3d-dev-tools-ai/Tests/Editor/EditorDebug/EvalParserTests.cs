using System;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class EvalParserTests
    {
        [Test] public void Eval_StaticProperty()
        {
            var r = EvalParser.Evaluate("UnityEditor.EditorApplication.applicationPath");
            Assert.IsInstanceOf<string>(r);
            Assert.IsTrue(((string)r).Length > 0);
        }

        [Test] public void Eval_InternalMethodCall()
        {
            var r = EvalParser.Evaluate("UnityEditor.LogEntries.GetCount()");
            Assert.IsInstanceOf<int>(r);
        }

        [Test] public void Eval_ChainedMemberAfterValue()
        {
            var r = EvalParser.Evaluate("UnityEditor.EditorApplication.applicationPath.Length");
            Assert.IsInstanceOf<int>(r);
            Assert.Greater((int)r, 0);
        }

        [Test] public void Eval_MethodWithIntArgs()
            => Assert.AreEqual(5, EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.Add(2, 3)"));

        [Test] public void Eval_MethodWithStringArgs()
            => Assert.AreEqual("ab", EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.Concat(\"a\", \"b\")"));

        [Test] public void Eval_MethodWithEnumLiteralArg()
            => Assert.AreEqual("A", EvalParser.Evaluate("Yoji.EditorDebug.Tests.InvokeProbe.EnumName(UnityEngine.KeyCode.A)"));

        [Test] public void Eval_UnterminatedString_ThrowsFormatException()
            => Assert.Throws<FormatException>(() =>
                EvalParser.Evaluate("UnityEditor.LogEntries.GetCount(\"unterminated"));

        [Test] public void Eval_TypeofArg_ThrowsFormatException()
            => Assert.Throws<FormatException>(() =>
                EvalParser.Evaluate("Foo.Bar(typeof(UnityEngine.GameObject))"));

        [Test] public void Eval_EmptyExpression_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("   "));

        [Test] public void Eval_DoubleDot_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("UnityEditor..Selection"));

        [Test] public void Eval_BareTypeName_ThrowsFormatException()
            => Assert.Throws<FormatException>(() => EvalParser.Evaluate("UnityEditor.Selection"));

        [Test] public void Eval_UnresolvableRoot_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => EvalParser.Evaluate("Nope.Nothing.Here"));
    }
}

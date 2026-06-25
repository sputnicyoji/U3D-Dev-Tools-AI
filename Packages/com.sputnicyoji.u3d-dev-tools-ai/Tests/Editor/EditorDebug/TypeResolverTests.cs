using System;
using NUnit.Framework;

namespace Yoji.EditorDebug.Tests
{
    public class TypeResolverTests
    {
        [Test] public void Resolve_PublicEditorType()
            => Assert.AreEqual("UnityEditor.Selection", TypeResolver.Resolve("UnityEditor.Selection").FullName);

        [Test] public void Resolve_InternalEditorType()
            => Assert.AreEqual("UnityEditor.LogEntries", TypeResolver.Resolve("UnityEditor.LogEntries").FullName);

        [Test] public void Resolve_InternalTypeInCoreModule()
            => Assert.AreEqual("UnityEditorInternal.ProfilerDriver",
                TypeResolver.Resolve("UnityEditorInternal.ProfilerDriver").FullName);

        [Test] public void Resolve_WithExplicitAssemblySuffix()
            => Assert.AreEqual("UnityEditor.LogEntries", TypeResolver.Resolve("UnityEditor.LogEntries, UnityEditor").FullName);

        [Test] public void Resolve_SystemType()
            => Assert.AreEqual(typeof(string), TypeResolver.Resolve("System.String"));

        [Test] public void Resolve_TestAssemblyType_ViaFullScan()
            => Assert.AreEqual(typeof(TypeResolverTests), TypeResolver.Resolve("Yoji.EditorDebug.Tests.TypeResolverTests"));

        [Test] public void Resolve_Unknown_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => TypeResolver.Resolve("Foo.NotExisting.For.E2E"));

        [Test] public void Resolve_Empty_ThrowsTypeAccessException()
            => Assert.Throws<TypeAccessException>(() => TypeResolver.Resolve("  "));

        [Test] public void TryResolve_Unknown_ReturnsFalse()
        {
            Assert.IsFalse(TypeResolver.TryResolve("Foo.Bar.Baz", out var t));
            Assert.IsNull(t);
        }
    }
}

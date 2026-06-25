using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class EnumParsingTests
    {
        [Test] public void Status_Ready_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("ready", out var s));
            Assert.AreEqual(ToolStatus.Ready, s);
        }

        [Test] public void Status_SkillOnly_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("skill-only", out var s));
            Assert.AreEqual(ToolStatus.SkillOnly, s);
        }

        [Test] public void Status_Planned_Parses()
        {
            Assert.IsTrue(ToolStatusExtensions.TryParse("planned", out var s));
            Assert.AreEqual(ToolStatus.Planned, s);
        }

        [Test] public void Status_Unknown_Fails()
            => Assert.IsFalse(ToolStatusExtensions.TryParse("retired", out _));

        [Test] public void Status_CaseSensitive_Fails()
            => Assert.IsFalse(ToolStatusExtensions.TryParse("Ready", out _));

        [Test] public void Kind_Tool_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("tool", out var k));
            Assert.AreEqual(ToolKind.Tool, k);
        }

        [Test] public void Kind_Infra_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("infra", out var k));
            Assert.AreEqual(ToolKind.Infra, k);
        }

        [Test] public void Kind_Linker_Parses()
        {
            Assert.IsTrue(ToolKindExtensions.TryParse("linker", out var k));
            Assert.AreEqual(ToolKind.Linker, k);
        }

        [Test] public void Kind_Unknown_Fails()
            => Assert.IsFalse(ToolKindExtensions.TryParse("plugin", out _));
    }
}

using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class RegistryValidatorTests
    {
        private static RegistryEntry ValidStableEntry()
        {
            return new RegistryEntry
            {
                Id = "editor-debug",
                Status = "ready",
                Kind = "tool",
                Order = 20,
                PackageName = "com.yoji.editor-debug",
                PackagePath = "Packages/com.yoji.editor-debug",
                Revision = "editor-debug-v1.2.0",
                DefaultEnabled = true,
                UserToggle = true,
                AgentAssets = "Agent~",
                MinUnity = "2022.3",
                DependsOn = new string[0]
            };
        }

        private static RegistryDocument Doc(params RegistryEntry[] entries)
        {
            return new RegistryDocument
            {
                SchemaVersion = 1,
                Channel = "stable",
                Entries = entries
            };
        }

        private static RegistryValidationException AssertInvalid(RegistryDocument doc, RegistryChannel channel)
        {
            return Assert.Throws<RegistryValidationException>(() => RegistryValidator.Validate(doc, channel));
        }

        [Test] public void Validate_ValidStableDoc_Passes()
        {
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(ValidStableEntry()), RegistryChannel.Stable));
        }

        [Test] public void Validate_UnknownStatus_Fails()
        {
            var e = ValidStableEntry();
            e.Status = "retired";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("status", ex.Message);
        }

        [Test] public void Validate_UnknownKind_Fails()
        {
            var e = ValidStableEntry();
            e.Kind = "plugin";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("kind", ex.Message);
        }

        [Test] public void Validate_PackageNameWrongPrefix_Fails()
        {
            var e = ValidStableEntry();
            e.PackageName = "com.acme.editor-debug";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("com.yoji.", ex.Message);
        }

        [Test] public void Validate_PackagePathMismatch_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "Packages/com.yoji.other";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("packagePath", ex.Message);
        }

        [Test] public void Validate_PackagePathWithDotDot_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "Packages/../com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_PackagePathAbsolute_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "C:/Packages/com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_PackagePathUrl_Fails()
        {
            var e = ValidStableEntry();
            e.PackagePath = "https://example.com/com.yoji.editor-debug";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_StableRevisionMatchesIdAndSemver_Passes()
        {
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(ValidStableEntry()), RegistryChannel.Stable));
        }

        [Test] public void Validate_StableRevisionWrongPrefix_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "editor-core-v1.2.0";
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("revision", ex.Message);
        }

        [Test] public void Validate_StableRevisionNotSemver_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "editor-debug-v1.2";
            AssertInvalid(Doc(e), RegistryChannel.Stable);
        }

        [Test] public void Validate_DevRevisionFullSha_Passes()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456789abcdef0123456789abcdef01234567";
            Assert.DoesNotThrow(() => RegistryValidator.Validate(Doc(e), RegistryChannel.Dev));
        }

        [Test] public void Validate_DevRevisionShortSha_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456";
            AssertInvalid(Doc(e), RegistryChannel.Dev);
        }

        [Test] public void Validate_DevRevisionUppercaseSha_Fails()
        {
            var e = ValidStableEntry();
            e.Revision = "0123456789ABCDEF0123456789abcdef01234567";
            AssertInvalid(Doc(e), RegistryChannel.Dev);
        }

        [Test] public void Validate_MissingMinUnity_Fails()
        {
            var e = ValidStableEntry();
            e.MinUnity = null;
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("minUnity", ex.Message);
        }

        [Test] public void Validate_DuplicateId_Fails()
        {
            var a = ValidStableEntry();
            var b = ValidStableEntry();
            b.PackageName = "com.yoji.editor-debug-2";
            b.PackagePath = "Packages/com.yoji.editor-debug-2";
            b.Revision = "editor-debug-v9.9.9";
            var ex = AssertInvalid(Doc(a, b), RegistryChannel.Stable);
            StringAssert.Contains("id", ex.Message);
        }

        [Test] public void Validate_DuplicatePackageName_Fails()
        {
            var a = ValidStableEntry();
            var b = ValidStableEntry();
            b.Id = "editor-debug-2";
            b.Revision = "editor-debug-2-v1.0.0";
            var ex = AssertInvalid(Doc(a, b), RegistryChannel.Stable);
            StringAssert.Contains("packageName", ex.Message);
        }

        [Test] public void Validate_DevBranchOnlyMain_FailsWhenNotMain()
        {
            var doc = Doc(ValidStableEntry());
            doc.Entries[0].Revision = "0123456789abcdef0123456789abcdef01234567";
            doc.Branch = "feature/x";
            AssertInvalid(doc, RegistryChannel.Dev);
        }

        [Test] public void Validate_AggregatesMultipleErrors()
        {
            var e = ValidStableEntry();
            e.Status = "retired";
            e.Kind = "plugin";
            e.MinUnity = null;
            var ex = AssertInvalid(Doc(e), RegistryChannel.Stable);
            StringAssert.Contains("status", ex.Message);
            StringAssert.Contains("kind", ex.Message);
            StringAssert.Contains("minUnity", ex.Message);
        }
    }
}

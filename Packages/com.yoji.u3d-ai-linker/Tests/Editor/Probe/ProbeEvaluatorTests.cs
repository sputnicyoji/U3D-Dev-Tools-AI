using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yoji.U3DAILinker.Probe;

namespace Yoji.U3DAILinker.Tests.Probe
{
    public sealed class ProbeEvaluatorTests
    {
        private static List<ProbeTarget> Targets(params bool[] existsFlags)
        {
            var list = new List<ProbeTarget>();
            for (int i = 0; i < existsFlags.Length; i++)
            {
                list.Add(new ProbeTarget(
                    id: "t" + i,
                    path: "/fake/path/" + i,
                    kind: i % 2 == 0 ? "File" : "Directory",
                    exists: existsFlags[i]));
            }
            return list;
        }

        [Test]
        public void Evaluate_AllExist_RecommendsDirectoryMode()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true, true, true));

            Assert.IsTrue(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.Directory, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_AnyMissing_RecommendsZipFallback()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true, false, true));

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_AllMissing_RecommendsZipFallback()
        {
            var result = ProbeEvaluator.Evaluate(Targets(false, false, false));

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_PreservesTargetOrderAndFacts()
        {
            var input = Targets(true, false, true);
            var result = ProbeEvaluator.Evaluate(input);

            Assert.AreEqual(3, result.Targets.Count);
            Assert.AreEqual("t0", result.Targets[0].Id);
            Assert.AreEqual("t1", result.Targets[1].Id);
            Assert.AreEqual("t2", result.Targets[2].Id);
            Assert.IsFalse(result.Targets[1].Exists);
        }

        [Test]
        public void Evaluate_SetsSchemaVersionAndProbedAtUtc()
        {
            var result = ProbeEvaluator.Evaluate(Targets(true));

            Assert.AreEqual(1, result.SchemaVersion);
            Assert.IsFalse(string.IsNullOrEmpty(result.ProbedAtUtc));
            // ProbedAtUtc 必须可被解析回 DateTime(round-trip 格式)
            Assert.DoesNotThrow(() => DateTime.Parse(result.ProbedAtUtc));
        }

        [Test]
        public void Evaluate_EmptyInput_TreatedAsNotReadable()
        {
            var result = ProbeEvaluator.Evaluate(new List<ProbeTarget>());

            Assert.IsFalse(result.AllTargetsReadable);
            Assert.AreEqual(ProbeMode.ZipFallback, result.RecommendedMode);
        }

        [Test]
        public void Evaluate_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ProbeEvaluator.Evaluate(null));
        }
    }
}

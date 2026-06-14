using System;
using System.Collections.Generic;
using System.Globalization;

namespace Yoji.U3DAILinker.Probe
{
    /// <summary>
    /// 纯逻辑:把逐个目标的存在性事实归约为总判定与推荐模式。无任何 IO,可在 EditMode 直接单测。
    /// </summary>
    public static class ProbeEvaluator
    {
        /// <summary>
        /// 评估一组探测目标。空集合视为不可读(无证据 = 不能走目录模式)。
        /// </summary>
        public static ProbeResult Evaluate(IReadOnlyList<ProbeTarget> targets)
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            bool allReadable = targets.Count > 0;
            var copied = new List<ProbeTarget>(targets.Count);
            foreach (var t in targets)
            {
                copied.Add(new ProbeTarget(t.Id, t.Path, t.Kind, t.Exists));
                if (!t.Exists)
                {
                    allReadable = false;
                }
            }

            return new ProbeResult
            {
                SchemaVersion = 1,
                ProbedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AllTargetsReadable = allReadable,
                RecommendedMode = allReadable ? ProbeMode.Directory : ProbeMode.ZipFallback,
                Targets = copied
            };
        }
    }
}

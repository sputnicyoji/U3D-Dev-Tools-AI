using NUnit.Framework;
using Yoji.U3DAILinker.Operations;

namespace Yoji.U3DAILinker.Tests.Operations
{
    public sealed class U3DAILinkerBootstrapDecisionTests
    {
        [Test]
        public void ShouldRunAgentSync_AfterQueueAlreadySatisfied()
        {
            Assert.IsTrue(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.AlreadySatisfied));
            Assert.IsFalse(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.Requested));
            Assert.IsFalse(U3DAILinkerBootstrap.ShouldRunAgentSync(QueueStepResult.Faulted));
        }
    }
}

using System;
using System.Threading;
using NUnit.Framework;

namespace Yoji.EditorCore.Tests
{
    public sealed class MainThreadDispatcherTests
    {
        [TearDown]
        public void TearDown()
        {
            MainThreadDispatcher.ResetDrainingForTests();
        }

        [Test]
        public void Run_OnMainThread_ExecutesDirectly()
        {
            var result = MainThreadDispatcher.Run(() => 42, out _);
            Assert.AreEqual(42, result);
        }

        // reload 死锁回归: 排队中的后台 waiter 必须被 DrainForReload 立刻唤醒并拿到取消异常,
        // 否则 domain unload 会等待处于 managed wait 的 HTTP 线程, 与主线程互等停摆.
        [Test]
        public void DrainForReload_FaultsQueuedBackgroundWaiter()
        {
            Exception captured = null;
            var started = new ManualResetEventSlim(false);
            var finished = new ManualResetEventSlim(false);
            var worker = new Thread(() =>
            {
                started.Set();
                try { MainThreadDispatcher.Run(() => null, out _, 10000); }
                catch (Exception e) { captured = e; }
                finished.Set();
            }) { IsBackground = true };
            worker.Start();

            Assert.IsTrue(started.Wait(2000), "worker did not start");
            // EditMode 测试体内主线程不跑 update Pump, job 只会停留在队列里等 drain.
            Thread.Sleep(100);
            MainThreadDispatcher.DrainForReload();

            Assert.IsTrue(finished.Wait(5000), "worker did not finish after drain");
            Assert.IsInstanceOf<OperationCanceledException>(captured);
        }

        [Test]
        public void Run_WhileDraining_RejectsImmediately()
        {
            MainThreadDispatcher.DrainForReload();
            Exception captured = null;
            var finished = new ManualResetEventSlim(false);
            var worker = new Thread(() =>
            {
                try { MainThreadDispatcher.Run(() => null, out _, 10000); }
                catch (Exception e) { captured = e; }
                finished.Set();
            }) { IsBackground = true };
            worker.Start();

            Assert.IsTrue(finished.Wait(5000), "worker did not finish");
            Assert.IsInstanceOf<OperationCanceledException>(captured);
        }
    }
}

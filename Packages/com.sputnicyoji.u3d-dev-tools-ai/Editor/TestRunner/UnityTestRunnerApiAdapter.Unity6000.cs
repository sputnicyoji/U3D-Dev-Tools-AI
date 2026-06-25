#if UNITY_6000_0_OR_NEWER
using System;
using System.Collections.Generic;
using System.Xml;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// Unity 6 keeps the typed TestRunnerApi path.
    /// Older editors use UnityTestRunnerApiAdapter.cs to avoid 2022 asmdef reference loss.
    internal sealed class UnityTestRunnerApiAdapter
    {
        private readonly TestRunnerApi m_Api;
        private CallbackBridge m_Callbacks;

        public UnityTestRunnerApiAdapter()
        {
            m_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        }

        public void RegisterCallbacks(ICallbacksSink sink)
        {
            if (m_Callbacks != null) return;
            m_Callbacks = new CallbackBridge(sink);
            m_Api.RegisterCallbacks(m_Callbacks);
        }

        public void Execute(FilterSpec spec)
        {
            m_Api.Execute(new ExecutionSettings(BuildFilter(spec)));
        }

        public void RetrieveTestList(string mode, Action<TestNode> callback)
        {
            var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            m_Api.RetrieveTestList(testMode, root => callback(TestNode.From(root)));
        }

        private static Filter BuildFilter(FilterSpec spec)
        {
            var testMode = spec.TestMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = testMode };
            if (spec.IsRunAll) return filter;
            if (spec.TestNames.Length > 0) filter.testNames = spec.TestNames;
            if (spec.AssemblyNames.Length > 0) filter.assemblyNames = spec.AssemblyNames;
            if (spec.CategoryNames.Length > 0) filter.categoryNames = spec.CategoryNames;
            if (spec.GroupNames.Length > 0) filter.groupNames = spec.GroupNames;
            return filter;
        }

        internal interface ICallbacksSink
        {
            void RunStarted(TestNode testsToRun);
            void TestStarted(TestNode test);
            void TestFinished(TestResult result);
            void RunFinished(TestResult result);
        }

        private sealed class CallbackBridge : ICallbacks
        {
            private readonly ICallbacksSink m_Sink;

            public CallbackBridge(ICallbacksSink sink)
            {
                m_Sink = sink;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                m_Sink.RunStarted(TestNode.From(testsToRun));
            }

            public void TestStarted(ITestAdaptor test)
            {
                m_Sink.TestStarted(TestNode.From(test));
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                m_Sink.TestFinished(TestResult.From(result));
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                m_Sink.RunFinished(TestResult.From(result));
            }
        }

        internal sealed class TestNode
        {
            private readonly ITestAdaptor m_Raw;

            private TestNode(ITestAdaptor raw)
            {
                m_Raw = raw;
            }

            public static TestNode From(ITestAdaptor raw)
            {
                return raw == null ? null : new TestNode(raw);
            }

            public bool IsSuite => m_Raw.IsSuite;
            public bool HasChildren => m_Raw.HasChildren;
            public string FullName => m_Raw.FullName;

            public IEnumerable<TestNode> Children
            {
                get
                {
                    foreach (var child in m_Raw.Children)
                    {
                        var node = From(child);
                        if (node != null) yield return node;
                    }
                }
            }
        }

        internal sealed class TestResult
        {
            private readonly ITestResultAdaptor m_Raw;

            private TestResult(ITestResultAdaptor raw)
            {
                m_Raw = raw;
            }

            public static TestResult From(ITestResultAdaptor raw)
            {
                return raw == null ? null : new TestResult(raw);
            }

            public bool IsFailed => m_Raw.TestStatus == TestStatus.Failed;
            public bool HasChildren => m_Raw.HasChildren;
            public string Name => m_Raw.Name;
            public string Message => m_Raw.Message;
            public string StackTrace => m_Raw.StackTrace;
            public int PassCount => m_Raw.PassCount;
            public int FailCount => m_Raw.FailCount;
            public int SkipCount => m_Raw.SkipCount;
            public int InconclusiveCount => m_Raw.InconclusiveCount;
            public string TestFullName => m_Raw.Test != null ? m_Raw.Test.FullName : null;

            public void WriteXml(XmlWriter writer)
            {
                m_Raw.ToXml().WriteTo(writer);
            }
        }
    }
}
#endif

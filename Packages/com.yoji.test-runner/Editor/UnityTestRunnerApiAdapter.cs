#if !UNITY_6000_0_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml;
using UnityEngine;

namespace Yoji.TestRunner
{
    /// Runtime reflection facade over UnityEditor.TestTools.TestRunner.Api.
    ///
    /// Unity 2022 can drop explicit UnityEditor.TestRunner/UnityEngine.TestRunner asmdef
    /// references from non-test Editor assemblies. Keeping this assembly free of
    /// compile-time TestRunner API types makes the service compile on 2022 while still
    /// using the same TestRunnerApi at runtime on 2022 and Unity 6.
    internal sealed class UnityTestRunnerApiAdapter
    {
        private const string c_ApiNamespace = "UnityEditor.TestTools.TestRunner.Api.";
        private const string c_EditorTestRunnerAssembly = "UnityEditor.TestRunner";

        private readonly Type m_TestRunnerApiType;
        private readonly Type m_CallbacksType;
        private readonly Type m_TestAdaptorType;
        private readonly Type m_TestModeType;
        private readonly Type m_FilterType;
        private readonly Type m_ExecutionSettingsType;
        private readonly MethodInfo m_RegisterCallbacksMethod;
        private readonly MethodInfo m_ExecuteMethod;
        private readonly MethodInfo m_RetrieveTestListMethod;
        private readonly ScriptableObject m_Api;
        private object m_CallbackProxy;

        public UnityTestRunnerApiAdapter()
        {
            m_TestRunnerApiType = RequireApiType("TestRunnerApi");
            m_CallbacksType = RequireApiType("ICallbacks");
            m_TestAdaptorType = RequireApiType("ITestAdaptor");
            m_TestModeType = RequireApiType("TestMode");
            m_FilterType = RequireApiType("Filter");
            m_ExecutionSettingsType = RequireApiType("ExecutionSettings");

            m_RegisterCallbacksMethod = RequireRegisterCallbacks(m_TestRunnerApiType);
            m_ExecuteMethod = RequireMethod(m_TestRunnerApiType, "Execute", m_ExecutionSettingsType);
            m_RetrieveTestListMethod = RequireRetrieveTestList(m_TestRunnerApiType, m_TestModeType, m_TestAdaptorType);

            m_Api = ScriptableObject.CreateInstance(m_TestRunnerApiType);
        }

        public void RegisterCallbacks(ICallbacksSink sink)
        {
            if (m_CallbackProxy != null) return;

            m_CallbackProxy = CreateDispatchProxy(m_CallbacksType, typeof(TestRunnerCallbackProxy));
            ((TestRunnerCallbackProxy)m_CallbackProxy).Sink = sink;
            Invoke(
                m_RegisterCallbacksMethod.MakeGenericMethod(m_CallbacksType),
                m_Api,
                new[] { m_CallbackProxy, (object)0 });
        }

        public void Execute(FilterSpec spec)
        {
            Invoke(m_ExecuteMethod, m_Api, new[] { BuildExecutionSettings(spec) });
        }

        public void RetrieveTestList(string mode, Action<TestNode> callback)
        {
            var callbackDelegate = CreateTestListCallback(callback);
            Invoke(m_RetrieveTestListMethod, m_Api, new[] { ToTestMode(mode), callbackDelegate });
        }

        private object BuildExecutionSettings(FilterSpec spec)
        {
            var filter = Activator.CreateInstance(m_FilterType);
            SetField(filter, "testMode", ToTestMode(spec.TestMode));
            if (spec.TestNames.Length > 0) SetField(filter, "testNames", spec.TestNames);
            if (spec.AssemblyNames.Length > 0) SetField(filter, "assemblyNames", spec.AssemblyNames);
            if (spec.CategoryNames.Length > 0) SetField(filter, "categoryNames", spec.CategoryNames);
            if (spec.GroupNames.Length > 0) SetField(filter, "groupNames", spec.GroupNames);

            var filters = Array.CreateInstance(m_FilterType, 1);
            filters.SetValue(filter, 0);
            return Activator.CreateInstance(m_ExecutionSettingsType, new object[] { filters });
        }

        private object ToTestMode(string mode)
        {
            return Enum.Parse(m_TestModeType, mode);
        }

        private Delegate CreateTestListCallback(Action<TestNode> callback)
        {
            var callbackType = typeof(Action<>).MakeGenericType(m_TestAdaptorType);
            var holder = new TestListCallbackHolder(callback);
            var parameter = Expression.Parameter(m_TestAdaptorType, "root");
            var body = Expression.Call(
                Expression.Constant(holder),
                typeof(TestListCallbackHolder).GetMethod(nameof(TestListCallbackHolder.Invoke)),
                Expression.Convert(parameter, typeof(object)));
            return Expression.Lambda(callbackType, body, parameter).Compile();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
                throw new MissingMemberException(target.GetType().FullName, fieldName);
            field.SetValue(target, value);
        }

        private static Type RequireApiType(string typeName)
        {
            var fullName = c_ApiNamespace + typeName;
            var type = Type.GetType(fullName + ", " + c_EditorTestRunnerAssembly);
            if (type != null) return type;

            Exception loadException = null;
            try
            {
                type = Assembly.Load(c_EditorTestRunnerAssembly).GetType(fullName, false);
                if (type != null) return type;
            }
            catch (Exception e)
            {
                loadException = e;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            throw new InvalidOperationException(
                "Unity Test Framework API type not found: " + fullName +
                ". Ensure com.unity.test-framework is installed.",
                loadException);
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static MethodInfo RequireRegisterCallbacks(Type apiType)
        {
            var method = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "RegisterCallbacks" &&
                                     m.IsGenericMethodDefinition &&
                                     m.GetParameters().Length == 2);
            if (method == null)
                throw new MissingMethodException(apiType.FullName, "RegisterCallbacks");
            return method;
        }

        private static MethodInfo RequireRetrieveTestList(Type apiType, Type testModeType, Type testAdaptorType)
        {
            var callbackType = typeof(Action<>).MakeGenericType(testAdaptorType);
            var method = apiType.GetMethod(
                "RetrieveTestList",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { testModeType, callbackType },
                null);
            if (method == null)
                throw new MissingMethodException(apiType.FullName, "RetrieveTestList");
            return method;
        }

        private static object CreateDispatchProxy(Type interfaceType, Type proxyType)
        {
            var method = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Create" &&
                                     m.IsGenericMethodDefinition &&
                                     m.GetGenericArguments().Length == 2 &&
                                     m.GetParameters().Length == 0);
            if (method == null)
                throw new MissingMethodException(typeof(DispatchProxy).FullName, "Create");
            return Invoke(method.MakeGenericMethod(interfaceType, proxyType), null, null);
        }

        private static object Invoke(MethodInfo method, object target, object[] parameters)
        {
            try
            {
                return method.Invoke(target, parameters);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        internal interface ICallbacksSink
        {
            void RunStarted(TestNode testsToRun);
            void TestStarted(TestNode test);
            void TestFinished(TestResult result);
            void RunFinished(TestResult result);
        }

        private sealed class TestRunnerCallbackProxy : DispatchProxy
        {
            public ICallbacksSink Sink;

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (Sink == null || targetMethod == null) return null;

                switch (targetMethod.Name)
                {
                    case "RunStarted":
                        Sink.RunStarted(TestNode.From(args[0]));
                        break;
                    case "TestStarted":
                        Sink.TestStarted(TestNode.From(args[0]));
                        break;
                    case "TestFinished":
                        Sink.TestFinished(TestResult.From(args[0]));
                        break;
                    case "RunFinished":
                        Sink.RunFinished(TestResult.From(args[0]));
                        break;
                }
                return null;
            }
        }

        private sealed class TestListCallbackHolder
        {
            private readonly Action<TestNode> m_Callback;

            public TestListCallbackHolder(Action<TestNode> callback)
            {
                m_Callback = callback;
            }

            public void Invoke(object root)
            {
                m_Callback(TestNode.From(root));
            }
        }

        internal sealed class TestNode
        {
            private readonly object m_Raw;
            private readonly Type m_Type;

            private TestNode(object raw)
            {
                m_Raw = raw;
                m_Type = raw.GetType();
            }

            public static TestNode From(object raw)
            {
                return raw == null ? null : new TestNode(raw);
            }

            public bool IsSuite => GetBool("IsSuite");
            public bool HasChildren => GetBool("HasChildren");
            public string FullName => GetString("FullName");

            public IEnumerable<TestNode> Children
            {
                get
                {
                    var children = GetValue("Children") as IEnumerable;
                    if (children == null) yield break;
                    foreach (var child in children)
                    {
                        var node = From(child);
                        if (node != null) yield return node;
                    }
                }
            }

            private bool GetBool(string propertyName)
            {
                var value = GetValue(propertyName);
                return value != null && Convert.ToBoolean(value);
            }

            private string GetString(string propertyName)
            {
                return Convert.ToString(GetValue(propertyName));
            }

            private object GetValue(string propertyName)
            {
                var property = m_Type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null)
                    throw new MissingMemberException(m_Type.FullName, propertyName);
                return property.GetValue(m_Raw, null);
            }
        }

        internal sealed class TestResult
        {
            private readonly object m_Raw;
            private readonly Type m_Type;

            private TestResult(object raw)
            {
                m_Raw = raw;
                m_Type = raw.GetType();
            }

            public static TestResult From(object raw)
            {
                return raw == null ? null : new TestResult(raw);
            }

            public bool IsFailed => string.Equals(GetString("TestStatus"), "Failed", StringComparison.Ordinal);
            public bool HasChildren => GetBool("HasChildren");
            public string Name => GetString("Name");
            public string Message => GetString("Message");
            public string StackTrace => GetString("StackTrace");
            public int PassCount => GetInt("PassCount");
            public int FailCount => GetInt("FailCount");
            public int SkipCount => GetInt("SkipCount");
            public int InconclusiveCount => GetInt("InconclusiveCount");

            public string TestFullName
            {
                get
                {
                    var test = GetValue("Test");
                    var node = TestNode.From(test);
                    return node != null ? node.FullName : null;
                }
            }

            public void WriteXml(XmlWriter writer)
            {
                var toXml = m_Type.GetMethod("ToXml", BindingFlags.Instance | BindingFlags.Public);
                if (toXml == null)
                    throw new MissingMethodException(m_Type.FullName, "ToXml");

                var xml = Invoke(toXml, m_Raw, null);
                if (xml == null) return;

                var writeTo = xml.GetType().GetMethod(
                    "WriteTo",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(XmlWriter) },
                    null);
                if (writeTo == null)
                    throw new MissingMethodException(xml.GetType().FullName, "WriteTo");
                Invoke(writeTo, xml, new object[] { writer });
            }

            private bool GetBool(string propertyName)
            {
                var value = GetValue(propertyName);
                return value != null && Convert.ToBoolean(value);
            }

            private int GetInt(string propertyName)
            {
                var value = GetValue(propertyName);
                return value == null ? 0 : Convert.ToInt32(value);
            }

            private string GetString(string propertyName)
            {
                return Convert.ToString(GetValue(propertyName));
            }

            private object GetValue(string propertyName)
            {
                var property = m_Type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null)
                    throw new MissingMemberException(m_Type.FullName, propertyName);
                return property.GetValue(m_Raw, null);
            }
        }
    }
}
#endif

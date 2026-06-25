using System.Collections.Generic;
using UnityEngine;

namespace Yoji.EditorDebug.Tests
{
    /// 反射调用测试靶子。所有状态静态，每个测试自己 Reset。
    public class InvokeProbe
    {
        public static int StaticField = 7;
        public static string StaticProp { get; set; } = "hello";
#pragma warning disable 0414 // s_Secret 仅供后续任务反射读取
        private static int s_Secret = 42;
#pragma warning restore 0414

        public int InstanceValue = 5;

        public static void Reset() { StaticField = 7; StaticProp = "hello"; }

        public static int Add(int a, int b) => a + b;
        public static string Concat(string a, string b) => a + b;
        public static int Mul(int a, int b = 3) => a * b;
        public static string Kind(int v) => "int";
        public static string Kind(string v) => "string";
        public static string EnumName(KeyCode k) => k.ToString();
        public static List<int> Numbers() => new List<int> { 1, 2, 3 };
        public static void DoNothing() { }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Yoji.EditorDebug
{
    /// /eval 轻量表达式求值器。仅支持 Type.Member.Member(args) 链式访问；
    /// 实参支持字符串/数字/true/false/null/点分枚举字面量。
    /// 不支持 lambda / new / typeof / 运算符。语法错误抛 System.FormatException。
    /// 类型前缀贪心最长匹配：从全部前导纯标识符段由长到短尝试 TypeResolver。
    internal static class EvalParser
    {
        public static object Evaluate(string code)
        {
            var segs = Parse(code);

            int maxPrefix = 0;
            while (maxPrefix < segs.Count && !segs[maxPrefix].IsCall) maxPrefix++;

            Type rootType = null;
            int typeLen = 0;
            for (int n = maxPrefix; n >= 1; n--)
            {
                var name = string.Join(".", segs.Take(n).Select(x => x.Name));
                if (TypeResolver.TryResolve(name, out rootType)) { typeLen = n; break; }
            }
            if (rootType == null)
                throw new TypeAccessException("cannot resolve a type from expression prefix: " + code);
            if (typeLen == segs.Count)
                throw new FormatException("expression is just a type name, expected member access: " + code);

            object current = null;
            Type currentType = rootType;
            for (int i = typeLen; i < segs.Count; i++)
            {
                var seg = segs[i];
                var step = new JObject
                {
                    ["member"] = seg.Name,
                    ["kind"] = seg.IsCall ? "call" : "get",
                };
                if (seg.IsCall) step["args"] = new JArray(seg.Args.ToArray());
                current = ReflectionInvoker.ExecuteStep(current, currentType, step);
                if (current is VoidResult) return current;
                currentType = current?.GetType() ?? typeof(object);
            }
            return current;
        }

        private sealed class Segment
        {
            public string Name;
            public bool IsCall;
            public readonly List<JToken> Args = new List<JToken>();
        }

        private static List<Segment> Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new FormatException("empty expression");
            int pos = 0;
            var segs = new List<Segment>();
            while (true)
            {
                SkipWs(code, ref pos);
                var seg = new Segment { Name = ReadIdentifier(code, ref pos) };
                SkipWs(code, ref pos);
                if (Peek(code, pos) == '(')
                {
                    pos++;
                    seg.IsCall = true;
                    SkipWs(code, ref pos);
                    if (Peek(code, pos) != ')')
                    {
                        while (true)
                        {
                            seg.Args.Add(ReadLiteral(code, ref pos));
                            SkipWs(code, ref pos);
                            var c = Peek(code, pos);
                            if (c == ',') { pos++; SkipWs(code, ref pos); continue; }
                            if (c == ')') break;
                            throw new FormatException("expected ',' or ')' at offset " + pos);
                        }
                    }
                    pos++;
                }
                segs.Add(seg);
                SkipWs(code, ref pos);
                if (pos >= code.Length) break;
                if (code[pos] != '.')
                    throw new FormatException("unexpected character '" + code[pos] + "' at offset " + pos);
                pos++;
            }
            return segs;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static char Peek(string s, int pos) => pos < s.Length ? s[pos] : '\0';

        private static string ReadIdentifier(string s, ref int pos)
        {
            int start = pos;
            if (pos < s.Length && (char.IsLetter(s[pos]) || s[pos] == '_')) pos++;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            if (pos == start)
                throw new FormatException("expected identifier at offset " + start);
            return s.Substring(start, pos - start);
        }

        /// 实参字面量：字符串 / 数字 / true / false / null / 点分标识符（枚举等，存为字符串交给 coercer）。
        private static JToken ReadLiteral(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            var c = Peek(s, pos);
            if (c == '"') return new JValue(ReadString(s, ref pos));
            if (c == '-' || char.IsDigit(c)) return ReadNumber(s, ref pos);
            if (char.IsLetter(c) || c == '_')
            {
                var ident = ReadDottedIdentifier(s, ref pos);
                if (Peek(s, pos) == '(')
                    throw new FormatException("call expression not supported as argument: " + ident + "(...)");
                switch (ident)
                {
                    case "true": return new JValue(true);
                    case "false": return new JValue(false);
                    case "null": return JValue.CreateNull();
                    default: return new JValue(ident);
                }
            }
            throw new FormatException("cannot parse argument at offset " + pos);
        }

        private static string ReadDottedIdentifier(string s, ref int pos)
        {
            var sb = new StringBuilder(ReadIdentifier(s, ref pos));
            while (Peek(s, pos) == '.')
            {
                pos++;
                sb.Append('.').Append(ReadIdentifier(s, ref pos));
            }
            return sb.ToString();
        }

        private static string ReadString(string s, ref int pos)
        {
            pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw new FormatException("unterminated string literal");
                var c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= s.Length) throw new FormatException("unterminated escape in string literal");
                    var e = s[pos++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: throw new FormatException("unsupported escape \\" + e);
                    }
                }
                else sb.Append(c);
            }
        }

        private static JToken ReadNumber(string s, ref int pos)
        {
            int start = pos;
            if (Peek(s, pos) == '-') pos++;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            var text = s.Substring(start, pos - start);
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return new JValue(l);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return new JValue(d);
            throw new FormatException("invalid number literal: " + text);
        }
    }
}

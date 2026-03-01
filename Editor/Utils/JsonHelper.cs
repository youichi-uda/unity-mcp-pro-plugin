using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UnityMcpPro
{
    /// <summary>
    /// JSON-RPC request structure
    /// </summary>
    [Serializable]
    public class JsonRpcRequest
    {
        public string jsonrpc;
        public string method;
        public string id;
        public Dictionary<string, object> @params;
    }

    /// <summary>
    /// Lightweight JSON serializer/deserializer for MCP protocol.
    /// Avoids external dependencies while supporting nested objects, arrays, and Unity types.
    /// </summary>
    public static class JsonHelper
    {
        public static T Deserialize<T>(string json) where T : class, new()
        {
            var dict = ParseObject(json, 0, out _);
            if (typeof(T) == typeof(JsonRpcRequest))
            {
                var req = new JsonRpcRequest
                {
                    jsonrpc = dict.ContainsKey("jsonrpc") ? dict["jsonrpc"]?.ToString() : "2.0",
                    method = dict.ContainsKey("method") ? dict["method"]?.ToString() : "",
                    id = dict.ContainsKey("id") ? dict["id"]?.ToString() : null,
                    @params = dict.ContainsKey("params") ? dict["params"] as Dictionary<string, object> : new Dictionary<string, object>()
                };
                return req as T;
            }
            return default;
        }

        public static string CreateSuccessResponse(string id, object result)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            AppendJsonValue(sb, id);
            sb.Append(",\"result\":");
            AppendJsonValue(sb, result);
            sb.Append("}");
            return sb.ToString();
        }

        public static string CreateErrorResponse(string id, int code, string message)
        {
            var sb = new StringBuilder();
            sb.Append("{\"jsonrpc\":\"2.0\",\"id\":");
            AppendJsonValue(sb, id);
            sb.Append(",\"error\":{\"code\":");
            sb.Append(code);
            sb.Append(",\"message\":");
            AppendJsonValue(sb, message);
            sb.Append("}}");
            return sb.ToString();
        }

        // --- JSON Parser ---

        private static object ParseValue(string json, int index, out int nextIndex)
        {
            SkipWhitespace(json, ref index);

            if (index >= json.Length)
            {
                nextIndex = index;
                return null;
            }

            char c = json[index];

            if (c == '{') return ParseObject(json, index, out nextIndex);
            if (c == '[') return ParseArray(json, index, out nextIndex);
            if (c == '"') return ParseString(json, index, out nextIndex);
            if (c == 't' || c == 'f') return ParseBool(json, index, out nextIndex);
            if (c == 'n') return ParseNull(json, index, out nextIndex);
            return ParseNumber(json, index, out nextIndex);
        }

        private static Dictionary<string, object> ParseObject(string json, int index, out int nextIndex)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip {
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                nextIndex = index + 1;
                return dict;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, index, out index);
                SkipWhitespace(json, ref index);
                index++; // skip :
                object value = ParseValue(json, index, out index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
                else
                    break;
            }

            if (index < json.Length && json[index] == '}')
                index++;

            nextIndex = index;
            return dict;
        }

        private static List<object> ParseArray(string json, int index, out int nextIndex)
        {
            var list = new List<object>();
            index++; // skip [
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                nextIndex = index + 1;
                return list;
            }

            while (index < json.Length)
            {
                object value = ParseValue(json, index, out index);
                list.Add(value);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                    index++;
                else
                    break;
            }

            if (index < json.Length && json[index] == ']')
                index++;

            nextIndex = index;
            return list;
        }

        private static string ParseString(string json, int index, out int nextIndex)
        {
            index++; // skip opening "
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];
                if (c == '\\')
                {
                    index++;
                    if (index < json.Length)
                    {
                        char escaped = json[index];
                        switch (escaped)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (index + 4 < json.Length)
                                {
                                    string hex = json.Substring(index + 1, 4);
                                    sb.Append((char)Convert.ToInt32(hex, 16));
                                    index += 4;
                                }
                                break;
                        }
                    }
                }
                else if (c == '"')
                {
                    nextIndex = index + 1;
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }

            nextIndex = index;
            return sb.ToString();
        }

        private static object ParseNumber(string json, int index, out int nextIndex)
        {
            int start = index;
            bool isFloat = false;

            if (index < json.Length && json[index] == '-') index++;

            while (index < json.Length && char.IsDigit(json[index]))
                index++;

            if (index < json.Length && json[index] == '.')
            {
                isFloat = true;
                index++;
                while (index < json.Length && char.IsDigit(json[index]))
                    index++;
            }

            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    index++;
                while (index < json.Length && char.IsDigit(json[index]))
                    index++;
            }

            string numStr = json.Substring(start, index - start);
            nextIndex = index;

            if (isFloat)
                return double.Parse(numStr, CultureInfo.InvariantCulture);

            if (long.TryParse(numStr, out long longVal))
                return longVal;

            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string json, int index, out int nextIndex)
        {
            if (json.Substring(index, 4) == "true")
            {
                nextIndex = index + 4;
                return true;
            }
            nextIndex = index + 5;
            return false;
        }

        private static object ParseNull(string json, int index, out int nextIndex)
        {
            nextIndex = index + 4;
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }

        // --- JSON Serializer ---

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                sb.Append('"');
                AppendEscapedString(sb, s);
                sb.Append('"');
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(value);
                return;
            }

            if (value is float f)
            {
                sb.Append(f.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is double d)
            {
                sb.Append(d.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is Dictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    AppendEscapedString(sb, kvp.Key);
                    sb.Append("\":");
                    AppendJsonValue(sb, kvp.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is IList list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonValue(sb, list[i]);
                }
                sb.Append(']');
                return;
            }

            if (value is Array array)
            {
                sb.Append('[');
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonValue(sb, array.GetValue(i));
                }
                sb.Append(']');
                return;
            }

            // Fallback: serialize as string
            sb.Append('"');
            AppendEscapedString(sb, value.ToString());
            sb.Append('"');
        }

        private static void AppendEscapedString(StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
        }
    }
}

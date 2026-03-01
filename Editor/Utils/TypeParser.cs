using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace UnityMcpPro
{
    public static class TypeParser
    {
        /// <summary>
        /// Parse a string like "1,2,3" or "Vector3(1,2,3)" into Vector3
        /// </summary>
        public static Vector3 ParseVector3(string value)
        {
            var nums = ExtractNumbers(value);
            if (nums.Length < 3)
                throw new ArgumentException($"Cannot parse Vector3 from '{value}'. Expected 3 numbers.");
            return new Vector3(nums[0], nums[1], nums[2]);
        }

        public static Vector2 ParseVector2(string value)
        {
            var nums = ExtractNumbers(value);
            if (nums.Length < 2)
                throw new ArgumentException($"Cannot parse Vector2 from '{value}'. Expected 2 numbers.");
            return new Vector2(nums[0], nums[1]);
        }

        public static Vector4 ParseVector4(string value)
        {
            var nums = ExtractNumbers(value);
            if (nums.Length < 4)
                throw new ArgumentException($"Cannot parse Vector4 from '{value}'. Expected 4 numbers.");
            return new Vector4(nums[0], nums[1], nums[2], nums[3]);
        }

        /// <summary>
        /// Parse "Color(r,g,b,a)", "r,g,b,a", or "#RRGGBB" into Color
        /// </summary>
        public static Color ParseColor(string value)
        {
            value = value.Trim();

            // Hex color
            if (value.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(value, out Color hexColor))
                    return hexColor;
                throw new ArgumentException($"Invalid hex color: {value}");
            }

            var nums = ExtractNumbers(value);
            if (nums.Length >= 4)
                return new Color(nums[0], nums[1], nums[2], nums[3]);
            if (nums.Length >= 3)
                return new Color(nums[0], nums[1], nums[2], 1f);

            throw new ArgumentException($"Cannot parse Color from '{value}'");
        }

        /// <summary>
        /// Convert a value to the target type, with smart string parsing
        /// </summary>
        public static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            string strVal = value.ToString();

            if (targetType == typeof(Vector3)) return ParseVector3(strVal);
            if (targetType == typeof(Vector2)) return ParseVector2(strVal);
            if (targetType == typeof(Vector4)) return ParseVector4(strVal);
            if (targetType == typeof(Color)) return ParseColor(strVal);
            if (targetType == typeof(Quaternion)) return Quaternion.Euler(ParseVector3(strVal));
            if (targetType == typeof(int)) return Convert.ToInt32(value);
            if (targetType == typeof(float)) return Convert.ToSingle(value);
            if (targetType == typeof(double)) return Convert.ToDouble(value);
            if (targetType == typeof(bool)) return Convert.ToBoolean(value);
            if (targetType == typeof(string)) return strVal;
            if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, strVal, true, out object enumVal))
                    return enumVal;
            }

            return Convert.ChangeType(value, targetType);
        }

        /// <summary>
        /// Find a Unity component type by name (searches all loaded assemblies)
        /// </summary>
        public static Type FindComponentType(string typeName)
        {
            // Common Unity types shortcut
            var unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Search UnityEngine.CoreModule
            unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Search UnityEngine.PhysicsModule
            unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.PhysicsModule");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Search UnityEngine.AudioModule
            unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.AudioModule");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Search UnityEngine.UIModule
            unityType = Type.GetType($"UnityEngine.{typeName}, UnityEngine.UIModule");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Search UnityEngine.UI
            unityType = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (unityType != null && typeof(Component).IsAssignableFrom(unityType))
                return unityType;

            // Brute-force search all assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == typeName && typeof(Component).IsAssignableFrom(type))
                        return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Extract float numbers from a string, stripping function names like "Vector3(...)"
        /// </summary>
        private static float[] ExtractNumbers(string value)
        {
            value = value.Trim();

            // Strip type prefix like "Vector3(" or "Color("
            int parenStart = value.IndexOf('(');
            if (parenStart >= 0)
            {
                int parenEnd = value.LastIndexOf(')');
                if (parenEnd > parenStart)
                    value = value.Substring(parenStart + 1, parenEnd - parenStart - 1);
                else
                    value = value.Substring(parenStart + 1);
            }

            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<float>();

            foreach (var part in parts)
            {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float num))
                    nums.Add(num);
            }

            return nums.ToArray();
        }
    }
}

using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcpPro.Tests
{
    [TestFixture]
    public class TypeParserTests
    {
        // --- ParseVector3 ---

        [Test]
        public void ParseVector3_CommaSeparated()
        {
            var v = TypeParser.ParseVector3("1,2,3");
            Assert.AreEqual(new Vector3(1, 2, 3), v);
        }

        [Test]
        public void ParseVector3_WithPrefix()
        {
            var v = TypeParser.ParseVector3("Vector3(4,5,6)");
            Assert.AreEqual(new Vector3(4, 5, 6), v);
        }

        [Test]
        public void ParseVector3_SpaceSeparated()
        {
            var v = TypeParser.ParseVector3("7 8 9");
            Assert.AreEqual(new Vector3(7, 8, 9), v);
        }

        [Test]
        public void ParseVector3_Floats()
        {
            var v = TypeParser.ParseVector3("1.5,2.5,3.5");
            Assert.AreEqual(1.5f, v.x, 0.001f);
            Assert.AreEqual(2.5f, v.y, 0.001f);
            Assert.AreEqual(3.5f, v.z, 0.001f);
        }

        [Test]
        public void ParseVector3_Negative()
        {
            var v = TypeParser.ParseVector3("-1,-2,-3");
            Assert.AreEqual(new Vector3(-1, -2, -3), v);
        }

        [Test]
        public void ParseVector3_TooFewNumbers_Throws()
        {
            Assert.Throws<ArgumentException>(() => TypeParser.ParseVector3("1,2"));
        }

        // --- ParseVector2 ---

        [Test]
        public void ParseVector2_CommaSeparated()
        {
            var v = TypeParser.ParseVector2("10,20");
            Assert.AreEqual(new Vector2(10, 20), v);
        }

        [Test]
        public void ParseVector2_WithPrefix()
        {
            var v = TypeParser.ParseVector2("Vector2(3.5,4.5)");
            Assert.AreEqual(3.5f, v.x, 0.001f);
            Assert.AreEqual(4.5f, v.y, 0.001f);
        }

        [Test]
        public void ParseVector2_TooFewNumbers_Throws()
        {
            Assert.Throws<ArgumentException>(() => TypeParser.ParseVector2("1"));
        }

        // --- ParseVector4 ---

        [Test]
        public void ParseVector4_CommaSeparated()
        {
            var v = TypeParser.ParseVector4("1,2,3,4");
            Assert.AreEqual(new Vector4(1, 2, 3, 4), v);
        }

        [Test]
        public void ParseVector4_WithPrefix()
        {
            var v = TypeParser.ParseVector4("Vector4(5,6,7,8)");
            Assert.AreEqual(new Vector4(5, 6, 7, 8), v);
        }

        [Test]
        public void ParseVector4_TooFewNumbers_Throws()
        {
            Assert.Throws<ArgumentException>(() => TypeParser.ParseVector4("1,2,3"));
        }

        // --- ParseColor ---

        [Test]
        public void ParseColor_RGB_CommaSeparated()
        {
            var c = TypeParser.ParseColor("1,0,0");
            Assert.AreEqual(new Color(1, 0, 0, 1), c);
        }

        [Test]
        public void ParseColor_RGBA_CommaSeparated()
        {
            var c = TypeParser.ParseColor("0.5,0.5,0.5,0.8");
            Assert.AreEqual(0.5f, c.r, 0.001f);
            Assert.AreEqual(0.5f, c.g, 0.001f);
            Assert.AreEqual(0.5f, c.b, 0.001f);
            Assert.AreEqual(0.8f, c.a, 0.001f);
        }

        [Test]
        public void ParseColor_WithPrefix()
        {
            var c = TypeParser.ParseColor("Color(0,1,0,1)");
            Assert.AreEqual(new Color(0, 1, 0, 1), c);
        }

        [Test]
        public void ParseColor_HexRGB()
        {
            var c = TypeParser.ParseColor("#FF0000");
            Assert.AreEqual(1f, c.r, 0.01f);
            Assert.AreEqual(0f, c.g, 0.01f);
            Assert.AreEqual(0f, c.b, 0.01f);
        }

        [Test]
        public void ParseColor_HexLowercase()
        {
            var c = TypeParser.ParseColor("#00ff00");
            Assert.AreEqual(0f, c.r, 0.01f);
            Assert.AreEqual(1f, c.g, 0.01f);
            Assert.AreEqual(0f, c.b, 0.01f);
        }

        [Test]
        public void ParseColor_InvalidHex_Throws()
        {
            Assert.Throws<ArgumentException>(() => TypeParser.ParseColor("#ZZZZZZ"));
        }

        [Test]
        public void ParseColor_TooFewNumbers_Throws()
        {
            Assert.Throws<ArgumentException>(() => TypeParser.ParseColor("1,2"));
        }

        // --- ConvertValue ---

        [Test]
        public void ConvertValue_ToVector3()
        {
            var result = TypeParser.ConvertValue("1,2,3", typeof(Vector3));
            Assert.AreEqual(new Vector3(1, 2, 3), result);
        }

        [Test]
        public void ConvertValue_ToInt()
        {
            var result = TypeParser.ConvertValue(3.7, typeof(int));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void ConvertValue_ToFloat()
        {
            var result = TypeParser.ConvertValue(2.5, typeof(float));
            Assert.AreEqual(2.5f, result);
        }

        [Test]
        public void ConvertValue_ToBool()
        {
            var result = TypeParser.ConvertValue(true, typeof(bool));
            Assert.AreEqual(true, result);
        }

        [Test]
        public void ConvertValue_ToString()
        {
            var result = TypeParser.ConvertValue(42, typeof(string));
            Assert.AreEqual("42", result);
        }

        [Test]
        public void ConvertValue_Null_ReturnsNull()
        {
            Assert.IsNull(TypeParser.ConvertValue(null, typeof(Vector3)));
        }
    }
}

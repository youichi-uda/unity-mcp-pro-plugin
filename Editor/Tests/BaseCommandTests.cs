using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityMcpPro.Tests
{
    /// <summary>
    /// Exposes protected static helpers from BaseCommand for testing.
    /// </summary>
    public class TestableBaseCommand : BaseCommand
    {
        public static new object Success(object data = null) => BaseCommand.Success(data);
        public static new object Success(string message) => BaseCommand.Success(message);
        public static new string GetStringParam(Dictionary<string, object> p, string key, string defaultValue = null)
            => BaseCommand.GetStringParam(p, key, defaultValue);
        public static new int GetIntParam(Dictionary<string, object> p, string key, int defaultValue = 0)
            => BaseCommand.GetIntParam(p, key, defaultValue);
        public static new bool GetBoolParam(Dictionary<string, object> p, string key, bool defaultValue = false)
            => BaseCommand.GetBoolParam(p, key, defaultValue);
        public static new float GetFloatParam(Dictionary<string, object> p, string key, float defaultValue = 0f)
            => BaseCommand.GetFloatParam(p, key, defaultValue);
        public static new Dictionary<string, object> GetDictParam(Dictionary<string, object> p, string key)
            => BaseCommand.GetDictParam(p, key);
        public static new string[] GetStringListParam(Dictionary<string, object> p, string key)
            => BaseCommand.GetStringListParam(p, key);
    }

    [TestFixture]
    public class BaseCommandTests
    {
        // --- Success helpers ---

        [Test]
        public void Success_WithNoArgs_ReturnsSuccessTrue()
        {
            var result = TestableBaseCommand.Success() as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(true, result["success"]);
            Assert.IsFalse(result.ContainsKey("data"));
        }

        [Test]
        public void Success_WithData_IncludesDataKey()
        {
            var data = new Dictionary<string, object> { { "count", 42 } };
            var result = TestableBaseCommand.Success(data) as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(true, result["success"]);
            Assert.AreEqual(data, result["data"]);
        }

        [Test]
        public void Success_WithStringMessage_IncludesMessageKey()
        {
            var result = TestableBaseCommand.Success("done") as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(true, result["success"]);
            Assert.AreEqual("done", result["message"]);
        }

        // --- GetStringParam ---

        [Test]
        public void GetStringParam_ExistingKey_ReturnsValue()
        {
            var p = new Dictionary<string, object> { { "name", "TestObj" } };
            Assert.AreEqual("TestObj", TestableBaseCommand.GetStringParam(p, "name"));
        }

        [Test]
        public void GetStringParam_MissingKey_ReturnsDefault()
        {
            var p = new Dictionary<string, object>();
            Assert.AreEqual("fallback", TestableBaseCommand.GetStringParam(p, "name", "fallback"));
        }

        [Test]
        public void GetStringParam_NullValue_ReturnsDefault()
        {
            var p = new Dictionary<string, object> { { "name", null } };
            Assert.IsNull(TestableBaseCommand.GetStringParam(p, "name"));
        }

        [Test]
        public void GetStringParam_IntValue_ConvertedToString()
        {
            var p = new Dictionary<string, object> { { "id", 123 } };
            Assert.AreEqual("123", TestableBaseCommand.GetStringParam(p, "id"));
        }

        // --- GetIntParam ---

        [Test]
        public void GetIntParam_FromDouble_CastsToInt()
        {
            var p = new Dictionary<string, object> { { "count", 3.0 } };
            Assert.AreEqual(3, TestableBaseCommand.GetIntParam(p, "count"));
        }

        [Test]
        public void GetIntParam_FromLong_CastsToInt()
        {
            var p = new Dictionary<string, object> { { "count", 42L } };
            Assert.AreEqual(42, TestableBaseCommand.GetIntParam(p, "count"));
        }

        [Test]
        public void GetIntParam_FromInt_ReturnsDirectly()
        {
            var p = new Dictionary<string, object> { { "count", 7 } };
            Assert.AreEqual(7, TestableBaseCommand.GetIntParam(p, "count"));
        }

        [Test]
        public void GetIntParam_FromString_Parses()
        {
            var p = new Dictionary<string, object> { { "count", "99" } };
            Assert.AreEqual(99, TestableBaseCommand.GetIntParam(p, "count"));
        }

        [Test]
        public void GetIntParam_MissingKey_ReturnsDefault()
        {
            var p = new Dictionary<string, object>();
            Assert.AreEqual(-1, TestableBaseCommand.GetIntParam(p, "count", -1));
        }

        // --- GetBoolParam ---

        [Test]
        public void GetBoolParam_FromBool_ReturnsDirect()
        {
            var p = new Dictionary<string, object> { { "flag", true } };
            Assert.IsTrue(TestableBaseCommand.GetBoolParam(p, "flag"));
        }

        [Test]
        public void GetBoolParam_FromString_Parses()
        {
            var p = new Dictionary<string, object> { { "flag", "true" } };
            Assert.IsTrue(TestableBaseCommand.GetBoolParam(p, "flag"));
        }

        [Test]
        public void GetBoolParam_MissingKey_ReturnsDefault()
        {
            var p = new Dictionary<string, object>();
            Assert.IsTrue(TestableBaseCommand.GetBoolParam(p, "flag", true));
        }

        // --- GetFloatParam ---

        [Test]
        public void GetFloatParam_FromDouble_CastsToFloat()
        {
            var p = new Dictionary<string, object> { { "speed", 1.5 } };
            Assert.AreEqual(1.5f, TestableBaseCommand.GetFloatParam(p, "speed"), 0.001f);
        }

        [Test]
        public void GetFloatParam_FromFloat_ReturnsDirect()
        {
            var p = new Dictionary<string, object> { { "speed", 2.5f } };
            Assert.AreEqual(2.5f, TestableBaseCommand.GetFloatParam(p, "speed"), 0.001f);
        }

        [Test]
        public void GetFloatParam_FromLong_CastsToFloat()
        {
            var p = new Dictionary<string, object> { { "speed", 10L } };
            Assert.AreEqual(10f, TestableBaseCommand.GetFloatParam(p, "speed"), 0.001f);
        }

        [Test]
        public void GetFloatParam_FromString_Parses()
        {
            var p = new Dictionary<string, object> { { "speed", "3.14" } };
            Assert.AreEqual(3.14f, TestableBaseCommand.GetFloatParam(p, "speed"), 0.01f);
        }

        [Test]
        public void GetFloatParam_MissingKey_ReturnsDefault()
        {
            var p = new Dictionary<string, object>();
            Assert.AreEqual(-1f, TestableBaseCommand.GetFloatParam(p, "missing", -1f), 0.001f);
        }

        // --- GetDictParam ---

        [Test]
        public void GetDictParam_ValidDict_ReturnsDict()
        {
            var inner = new Dictionary<string, object> { { "x", 1 } };
            var p = new Dictionary<string, object> { { "data", inner } };
            Assert.AreEqual(inner, TestableBaseCommand.GetDictParam(p, "data"));
        }

        [Test]
        public void GetDictParam_WrongType_ReturnsNull()
        {
            var p = new Dictionary<string, object> { { "data", "not a dict" } };
            Assert.IsNull(TestableBaseCommand.GetDictParam(p, "data"));
        }

        // --- GetStringListParam ---

        [Test]
        public void GetStringListParam_FromList_ReturnsArray()
        {
            var list = new List<object> { "a", "b", "c" };
            var p = new Dictionary<string, object> { { "tags", list } };
            var result = TestableBaseCommand.GetStringListParam(p, "tags");
            Assert.AreEqual(new[] { "a", "b", "c" }, result);
        }

        [Test]
        public void GetStringListParam_FromString_ReturnsWrappedArray()
        {
            var p = new Dictionary<string, object> { { "tags", "solo" } };
            var result = TestableBaseCommand.GetStringListParam(p, "tags");
            Assert.AreEqual(new[] { "solo" }, result);
        }

        [Test]
        public void GetStringListParam_Missing_ReturnsNull()
        {
            var p = new Dictionary<string, object>();
            Assert.IsNull(TestableBaseCommand.GetStringListParam(p, "tags"));
        }
    }
}

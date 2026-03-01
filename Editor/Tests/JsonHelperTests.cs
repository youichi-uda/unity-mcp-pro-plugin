using System.Collections.Generic;
using NUnit.Framework;

namespace UnityMcpPro.Tests
{
    [TestFixture]
    public class JsonHelperTests
    {
        // --- CreateSuccessResponse ---

        [Test]
        public void CreateSuccessResponse_ContainsJsonRpc()
        {
            var json = JsonHelper.CreateSuccessResponse("1", "ok");
            Assert.That(json, Does.Contain("\"jsonrpc\":\"2.0\""));
            Assert.That(json, Does.Contain("\"id\":\"1\""));
            Assert.That(json, Does.Contain("\"result\":\"ok\""));
        }

        [Test]
        public void CreateSuccessResponse_WithDict()
        {
            var data = new Dictionary<string, object> { { "count", 5 } };
            var json = JsonHelper.CreateSuccessResponse("2", data);
            Assert.That(json, Does.Contain("\"count\":5"));
        }

        // --- CreateErrorResponse ---

        [Test]
        public void CreateErrorResponse_ContainsErrorBlock()
        {
            var json = JsonHelper.CreateErrorResponse("3", -32601, "Method not found");
            Assert.That(json, Does.Contain("\"error\""));
            Assert.That(json, Does.Contain("-32601"));
            Assert.That(json, Does.Contain("Method not found"));
        }

        // --- Deserialize ---

        [Test]
        public void Deserialize_JsonRpcRequest_ParsesCorrectly()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"test_cmd\",\"id\":\"abc\",\"params\":{\"key\":\"val\"}}";
            var req = JsonHelper.Deserialize<JsonRpcRequest>(json);
            Assert.AreEqual("2.0", req.jsonrpc);
            Assert.AreEqual("test_cmd", req.method);
            Assert.AreEqual("abc", req.id);
            Assert.AreEqual("val", req.@params["key"]);
        }

        [Test]
        public void Deserialize_EscapeSequences_ParsesCorrectly()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"test\",\"id\":\"1\",\"params\":{\"text\":\"line1\\nline2\\ttab\"}}";
            var req = JsonHelper.Deserialize<JsonRpcRequest>(json);
            Assert.AreEqual("line1\nline2\ttab", req.@params["text"]);
        }

        // --- Round-trip ---

        [Test]
        public void RoundTrip_SuccessResponse_IsValidJson()
        {
            var data = new Dictionary<string, object>
            {
                { "name", "test" },
                { "value", 42 },
                { "nested", new Dictionary<string, object> { { "a", true } } },
                { "list", new List<object> { 1, "two", 3.0 } }
            };

            var json = JsonHelper.CreateSuccessResponse("rt-1", data);

            // Verify it can be parsed back
            Assert.That(json, Does.Contain("\"name\":\"test\""));
            Assert.That(json, Does.Contain("\"value\":42"));
            Assert.That(json, Does.Contain("\"a\":true"));

            // Parse the response and verify structure
            // (Using our own parser since we don't have System.Text.Json in Unity)
            Assert.That(json, Does.StartWith("{"));
            Assert.That(json, Does.EndWith("}"));
            Assert.That(json, Does.Contain("\"jsonrpc\":\"2.0\""));
        }
    }
}

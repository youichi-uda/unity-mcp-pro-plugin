using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityMcpPro.Tests
{
    [TestFixture]
    public class CommandRouterTests
    {
        private CommandRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = new CommandRouter();
        }

        [Test]
        public void Register_And_ExecuteDirect_CallsHandler()
        {
            _router.Register("test_method", p =>
            {
                return new Dictionary<string, object> { { "called", true } };
            });

            var result = _router.ExecuteDirect("test_method", new Dictionary<string, object>()) as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(true, result["called"]);
        }

        [Test]
        public void ExecuteDirect_UnknownMethod_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _router.ExecuteDirect("no_such_method", new Dictionary<string, object>()));
        }

        [Test]
        public void Register_OverwritesPreviousHandler()
        {
            _router.Register("method", p => "first");
            _router.Register("method", p => "second");

            var result = _router.ExecuteDirect("method", new Dictionary<string, object>());
            Assert.AreEqual("second", result);
        }

        [Test]
        public void Dispatch_Success_SendsJsonRpcSuccessResponse()
        {
            _router.Register("greet", p => new Dictionary<string, object> { { "msg", "hello" } });

            string response = null;
            var request = new JsonRpcRequest
            {
                jsonrpc = "2.0",
                method = "greet",
                id = "1",
                @params = new Dictionary<string, object>()
            };

            _router.Dispatch(request, r => response = r);

            Assert.IsNotNull(response);
            Assert.That(response, Does.Contain("\"result\""));
            Assert.That(response, Does.Contain("\"id\""));
            Assert.That(response, Does.Not.Contain("\"error\""));
        }

        [Test]
        public void Dispatch_UnknownMethod_SendsErrorResponse()
        {
            string response = null;
            var request = new JsonRpcRequest
            {
                jsonrpc = "2.0",
                method = "missing",
                id = "2",
                @params = new Dictionary<string, object>()
            };

            _router.Dispatch(request, r => response = r);

            Assert.IsNotNull(response);
            Assert.That(response, Does.Contain("\"error\""));
            Assert.That(response, Does.Contain("-32601"));
        }

        [Test]
        public void Dispatch_HandlerThrows_SendsErrorResponse()
        {
            _router.Register("boom", p => throw new InvalidOperationException("kaboom"));

            string response = null;
            var request = new JsonRpcRequest
            {
                jsonrpc = "2.0",
                method = "boom",
                id = "3",
                @params = new Dictionary<string, object>()
            };

            _router.Dispatch(request, r => response = r);

            Assert.IsNotNull(response);
            Assert.That(response, Does.Contain("\"error\""));
            Assert.That(response, Does.Contain("kaboom"));
            Assert.That(response, Does.Contain("-32000"));
        }

        [Test]
        public void Dispatch_PassesParamsToHandler()
        {
            Dictionary<string, object> receivedParams = null;
            _router.Register("echo", p =>
            {
                receivedParams = p;
                return p;
            });

            var inputParams = new Dictionary<string, object> { { "key", "value" } };
            var request = new JsonRpcRequest
            {
                jsonrpc = "2.0",
                method = "echo",
                id = "4",
                @params = inputParams
            };

            _router.Dispatch(request, _ => { });

            Assert.IsNotNull(receivedParams);
            Assert.AreEqual("value", receivedParams["key"]);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMcpPro
{
    public class CommandRouter
    {
        private readonly Dictionary<string, Func<Dictionary<string, object>, object>> _handlers
            = new Dictionary<string, Func<Dictionary<string, object>, object>>();

        public void Register(string method, Func<Dictionary<string, object>, object> handler)
        {
            _handlers[method] = handler;
        }

        public void Dispatch(JsonRpcRequest request, Action<string> sendResponse)
        {
            string responseJson;

            try
            {
                if (!_handlers.TryGetValue(request.method, out var handler))
                {
                    responseJson = JsonHelper.CreateErrorResponse(request.id, -32601,
                        $"Method not found: {request.method}");
                    sendResponse(responseJson);
                    return;
                }

                var paramDict = request.@params ?? new Dictionary<string, object>();
                var result = handler(paramDict);
                responseJson = JsonHelper.CreateSuccessResponse(request.id, result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Command error ({request.method}): {ex.Message}\n{ex.StackTrace}");
                responseJson = JsonHelper.CreateErrorResponse(request.id, -32000, ex.Message);
            }

            sendResponse(responseJson);
        }

        public object ExecuteDirect(string method, Dictionary<string, object> parameters)
        {
            if (!_handlers.TryGetValue(method, out var handler))
                throw new ArgumentException($"Method not found: {method}");

            return handler(parameters);
        }
    }
}

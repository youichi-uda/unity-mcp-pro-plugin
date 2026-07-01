using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class NetcodeCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("setup_network_manager", SetupNetworkManager);
            router.Register("add_network_object", AddNetworkObject);
            router.Register("create_network_behaviour", CreateNetworkBehaviour);
            router.Register("get_network_info", GetNetworkInfo);
        }

        private static Type FindNetcodeType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == typeName || type.FullName == typeName)
                        return type;
                }
            }
            return null;
        }

        private static object SetupNetworkManager(Dictionary<string, object> p)
        {
            ThrowIfPlaying("setup_network_manager");

            string transport = GetStringParam(p, "transport", "UnityTransport");
            int port = GetIntParam(p, "port", 7777);
            int maxPlayers = GetIntParam(p, "max_players", 10);
            int tickRate = GetIntParam(p, "tick_rate", 30);

            var networkManagerType = FindNetcodeType("NetworkManager");
            if (networkManagerType == null)
                throw new InvalidOperationException(
                    "Netcode for GameObjects not found. Install 'com.unity.netcode.gameobjects' via Package Manager.");

            // Find existing NetworkManager or create one
            var existingManager = FindObjectsByTypeCompat(networkManagerType);
            GameObject managerGo;
            Component managerComp;

            if (existingManager.Length > 0)
            {
                managerComp = existingManager[0] as Component;
                managerGo = managerComp.gameObject;
                RecordUndo(managerGo, "Configure NetworkManager");
            }
            else
            {
                managerGo = new GameObject("NetworkManager");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create NetworkManager");
                managerComp = Undo.AddComponent(managerGo, networkManagerType);
            }

            // Add transport
            var transportType = FindNetcodeType(transport);
            if (transportType != null)
            {
                var transportComp = managerGo.GetComponent(transportType);
                if (transportComp == null)
                    transportComp = Undo.AddComponent(managerGo, transportType);

                // Set port via ConnectionData
                try
                {
                    var connectionDataProp = transportType.GetProperty("ConnectionData");
                    if (connectionDataProp != null)
                    {
                        var connectionData = connectionDataProp.GetValue(transportComp);
                        var portPropInfo = connectionData.GetType().GetProperty("Port");
                        var portFieldInfo = connectionData.GetType().GetField("Port");
                        if (portPropInfo != null)
                            portPropInfo.SetValue(connectionData, (ushort)port);
                        else if (portFieldInfo != null)
                            portFieldInfo.SetValue(connectionData, (ushort)port);
                        connectionDataProp.SetValue(transportComp, connectionData);
                    }
                    else
                    {
                        // Try direct SetConnectionData method
                        var setMethod = transportType.GetMethod("SetConnectionData",
                            new[] { typeof(string), typeof(ushort) });
                        if (setMethod != null)
                            setMethod.Invoke(transportComp, new object[] { "127.0.0.1", (ushort)port });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not set transport port: {ex.Message}");
                }

                // Set NetworkConfig.NetworkTransport
                var configProp = networkManagerType.GetProperty("NetworkConfig") ??
                                 networkManagerType.GetField("NetworkConfig") as MemberInfo;
                if (configProp != null)
                {
                    object config = null;
                    if (configProp is PropertyInfo pi2) config = pi2.GetValue(managerComp);
                    else if (configProp is FieldInfo fi2) config = fi2.GetValue(managerComp);

                    if (config != null)
                    {
                        var transportField = config.GetType().GetField("NetworkTransport",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (transportField != null)
                            transportField.SetValue(config, transportComp);

                        // Set tick rate
                        var tickRateField = config.GetType().GetField("TickRate",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (tickRateField != null)
                            tickRateField.SetValue(config, (uint)tickRate);
                    }
                }
            }

            // Set max players (PlayerPrefab is usually set manually)
            try
            {
                var maxPlayersProp = networkManagerType.GetProperty("MaxConnectedPlayers");
                if (maxPlayersProp != null && maxPlayersProp.CanWrite)
                    maxPlayersProp.SetValue(managerComp, maxPlayers);
            }
            catch { /* Some versions don't have this property */ }

            EditorUtility.SetDirty(managerGo);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", managerGo.name },
                { "transport", transport },
                { "port", port },
                { "max_players", maxPlayers },
                { "tick_rate", tickRate }
            };
        }

        private static object AddNetworkObject(Dictionary<string, object> p)
        {
            ThrowIfPlaying("add_network_object");

            string targetPath = GetStringParam(p, "target");
            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("target is required");

            bool autoParentSync = GetBoolParam(p, "auto_object_parent_sync", true);
            bool dontDestroyWithOwner = GetBoolParam(p, "dont_destroy_with_owner", false);

            var networkObjectType = FindNetcodeType("NetworkObject");
            if (networkObjectType == null)
                throw new InvalidOperationException(
                    "Netcode for GameObjects not found. Install 'com.unity.netcode.gameobjects' via Package Manager.");

            var go = FindGameObject(targetPath);
            var networkObj = go.GetComponent(networkObjectType);

            if (networkObj == null)
                networkObj = Undo.AddComponent(go, networkObjectType);
            else
                RecordUndo(networkObj, "Configure NetworkObject");

            // Set AutoObjectParentSync
            try
            {
                var autoSyncProp = networkObjectType.GetProperty("AutoObjectParentSync");
                if (autoSyncProp != null && autoSyncProp.CanWrite)
                    autoSyncProp.SetValue(networkObj, autoParentSync);

                var dontDestroyProp = networkObjectType.GetProperty("DontDestroyWithOwner");
                if (dontDestroyProp != null && dontDestroyProp.CanWrite)
                    dontDestroyProp.SetValue(networkObj, dontDestroyWithOwner);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not set NetworkObject properties: {ex.Message}");
            }

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "path", GetGameObjectPath(go) },
                { "auto_object_parent_sync", autoParentSync },
                { "dont_destroy_with_owner", dontDestroyWithOwner }
            };
        }

        private static object CreateNetworkBehaviour(Dictionary<string, object> p)
        {
            ThrowIfPlaying("create_network_behaviour");

            string name = GetStringParam(p, "name");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required");

            string scriptPath = GetStringParam(p, "script_path", $"Assets/Scripts/Network/{name}.cs");
            if (!scriptPath.EndsWith(".cs"))
                scriptPath += ".cs";

            var networkVars = p.ContainsKey("network_variables") ? p["network_variables"] as List<object> : null;
            var rpcsList = p.ContainsKey("rpcs") ? p["rpcs"] as List<object> : null;

            // Determine using statements
            bool needsCollections = false;
            if (networkVars != null)
            {
                foreach (var varObj in networkVars)
                {
                    var nv = varObj as Dictionary<string, object>;
                    if (nv == null) continue;
                    string varType = nv.ContainsKey("type") ? nv["type"].ToString() : "float";
                    if (varType.Contains("FixedString"))
                        needsCollections = true;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("using Unity.Netcode;");
            sb.AppendLine("using UnityEngine;");
            if (needsCollections)
                sb.AppendLine("using Unity.Collections;");
            sb.AppendLine();
            sb.AppendLine($"public class {name} : NetworkBehaviour");
            sb.AppendLine("{");

            // Generate NetworkVariable fields
            if (networkVars != null)
            {
                foreach (var varObj in networkVars)
                {
                    var nv = varObj as Dictionary<string, object>;
                    if (nv == null) continue;
                    string varName = nv.ContainsKey("name") ? nv["name"].ToString() : "";
                    string varType = nv.ContainsKey("type") ? nv["type"].ToString() : "float";
                    string writePerm = nv.ContainsKey("write_permission") ? nv["write_permission"].ToString() : "Server";
                    if (string.IsNullOrEmpty(varName)) continue;

                    string permissionValue = writePerm == "Owner"
                        ? "NetworkVariableWritePermission.Owner"
                        : "NetworkVariableWritePermission.Server";

                    sb.AppendLine($"    public NetworkVariable<{varType}> {varName} = new NetworkVariable<{varType}>(");
                    sb.AppendLine($"        default, NetworkVariableReadPermission.Everyone, {permissionValue});");
                    sb.AppendLine();
                }
            }

            // OnNetworkSpawn
            sb.AppendLine("    public override void OnNetworkSpawn()");
            sb.AppendLine("    {");
            sb.AppendLine("        base.OnNetworkSpawn();");

            if (networkVars != null)
            {
                foreach (var varObj in networkVars)
                {
                    var nv = varObj as Dictionary<string, object>;
                    if (nv == null) continue;
                    string varName = nv.ContainsKey("name") ? nv["name"].ToString() : "";
                    if (string.IsNullOrEmpty(varName)) continue;

                    sb.AppendLine($"        {varName}.OnValueChanged += On{varName}Changed;");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // OnNetworkDespawn
            sb.AppendLine("    public override void OnNetworkDespawn()");
            sb.AppendLine("    {");

            if (networkVars != null)
            {
                foreach (var varObj in networkVars)
                {
                    var nv = varObj as Dictionary<string, object>;
                    if (nv == null) continue;
                    string varName = nv.ContainsKey("name") ? nv["name"].ToString() : "";
                    if (string.IsNullOrEmpty(varName)) continue;

                    sb.AppendLine($"        {varName}.OnValueChanged -= On{varName}Changed;");
                }
            }

            sb.AppendLine("        base.OnNetworkDespawn();");
            sb.AppendLine("    }");
            sb.AppendLine();

            // NetworkVariable change callbacks
            if (networkVars != null)
            {
                foreach (var varObj in networkVars)
                {
                    var nv = varObj as Dictionary<string, object>;
                    if (nv == null) continue;
                    string varName = nv.ContainsKey("name") ? nv["name"].ToString() : "";
                    string varType = nv.ContainsKey("type") ? nv["type"].ToString() : "float";
                    if (string.IsNullOrEmpty(varName)) continue;

                    sb.AppendLine($"    private void On{varName}Changed({varType} previousValue, {varType} newValue)");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        // TODO: Handle {varName} change");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }

            // Generate RPCs
            if (rpcsList != null)
            {
                foreach (var rpcObj in rpcsList)
                {
                    var rpc = rpcObj as Dictionary<string, object>;
                    if (rpc == null) continue;
                    string rpcName = rpc.ContainsKey("name") ? rpc["name"].ToString() : "";
                    string rpcType = rpc.ContainsKey("type") ? rpc["type"].ToString() : "ServerRpc";
                    var rpcParams = rpc.ContainsKey("params") ? rpc["params"] as List<object> : null;
                    if (string.IsNullOrEmpty(rpcName)) continue;

                    // Ensure method name ends with Rpc suffix
                    string methodName = rpcName;
                    if (rpcType == "ServerRpc" && !methodName.EndsWith("ServerRpc"))
                        methodName += "ServerRpc";
                    else if (rpcType == "ClientRpc" && !methodName.EndsWith("ClientRpc"))
                        methodName += "ClientRpc";

                    sb.AppendLine($"    [{rpcType}]");

                    // Build parameter list
                    var paramStrings = new List<string>();
                    if (rpcParams != null)
                    {
                        foreach (var paramObj in rpcParams)
                        {
                            var rpcParam = paramObj as Dictionary<string, object>;
                            if (rpcParam == null) continue;
                            string pName = rpcParam.ContainsKey("name") ? rpcParam["name"].ToString() : "";
                            string pType = rpcParam.ContainsKey("type") ? rpcParam["type"].ToString() : "float";
                            if (!string.IsNullOrEmpty(pName))
                                paramStrings.Add($"{pType} {pName}");
                        }
                    }

                    // ServerRpc can have ServerRpcParams
                    if (rpcType == "ServerRpc")
                        paramStrings.Add("ServerRpcParams serverRpcParams = default");
                    else if (rpcType == "ClientRpc")
                        paramStrings.Add("ClientRpcParams clientRpcParams = default");

                    sb.AppendLine($"    public void {methodName}({string.Join(", ", paramStrings)})");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        // TODO: Implement {rpcName} logic");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("}");

            // Write file
            if (!scriptPath.StartsWith("Assets/"))
                scriptPath = "Assets/" + scriptPath;

            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, sb.ToString());
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", scriptPath },
                { "name", name },
                { "network_variables", networkVars?.Count ?? 0 },
                { "rpcs", rpcsList?.Count ?? 0 }
            };
        }

        private static object GetNetworkInfo(Dictionary<string, object> p)
        {
            var networkManagerType = FindNetcodeType("NetworkManager");
            var networkObjectType = FindNetcodeType("NetworkObject");

            bool netcodeInstalled = networkManagerType != null;
            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "netcode_installed", netcodeInstalled }
            };

            if (!netcodeInstalled)
            {
                result["message"] = "Netcode for GameObjects package not installed";
                return result;
            }

            // Find NetworkManager
            var managers = FindObjectsByTypeCompat(networkManagerType);
            if (managers.Length > 0)
            {
                var manager = managers[0] as Component;
                var managerInfo = new Dictionary<string, object>
                {
                    { "gameObject", manager.gameObject.name },
                    { "path", GetGameObjectPath(manager.gameObject) }
                };

                // Get NetworkConfig
                try
                {
                    var configProp = networkManagerType.GetProperty("NetworkConfig");
                    if (configProp == null)
                    {
                        var configField = networkManagerType.GetField("NetworkConfig",
                            BindingFlags.Instance | BindingFlags.Public);
                        if (configField != null)
                        {
                            var config = configField.GetValue(manager);
                            if (config != null)
                            {
                                var tickRateField = config.GetType().GetField("TickRate",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (tickRateField != null)
                                    managerInfo["tick_rate"] = tickRateField.GetValue(config);

                                var transportField = config.GetType().GetField("NetworkTransport",
                                    BindingFlags.Instance | BindingFlags.Public);
                                if (transportField != null)
                                {
                                    var transport = transportField.GetValue(config);
                                    if (transport != null)
                                        managerInfo["transport"] = transport.GetType().Name;
                                }
                            }
                        }
                    }
                }
                catch { /* Best effort */ }

                result["network_manager"] = managerInfo;
            }
            else
            {
                result["network_manager"] = null;
            }

            // Find all NetworkObjects
            if (networkObjectType != null)
            {
                var networkObjects = FindObjectsByTypeCompat(networkObjectType);
                var netObjList = new List<object>();

                foreach (var netObj in networkObjects)
                {
                    var comp = netObj as Component;
                    if (comp == null) continue;

                    var objInfo = new Dictionary<string, object>
                    {
                        { "name", comp.gameObject.name },
                        { "path", GetGameObjectPath(comp.gameObject) }
                    };

                    // Check for NetworkBehaviour components
                    var networkBehaviourType = FindNetcodeType("NetworkBehaviour");
                    if (networkBehaviourType != null)
                    {
                        var behaviours = comp.gameObject.GetComponents(networkBehaviourType);
                        var behaviourNames = behaviours.Select(b => b.GetType().Name).ToArray();
                        objInfo["network_behaviours"] = behaviourNames;
                    }

                    netObjList.Add(objInfo);
                }

                result["network_objects"] = netObjList;
                result["network_object_count"] = netObjList.Count;
            }

            return result;
        }
    }
}

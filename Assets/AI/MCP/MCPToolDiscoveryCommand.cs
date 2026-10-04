using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Dynamic MCP tool discovery endpoint.
    /// Returns all registered MCP tools with metadata and input schemas.
    ///
    /// ============ NEDEN REFLECTION KULLANIYORUZ ============
    /// Bu sýnýf MCP for Unity paketinin iç kayýt defterine (CommandRegistry) bakýyor.
    /// O defterin dýþ yüzeyi paket sürümleri arasýnda deðiþti:
    ///
    ///   error CS0117: 'CommandRegistry' does not contain a definition
    ///                 for 'GetRegisteredTools'
    ///
    /// Doðrudan çaðrý, metodun adý her deðiþtiðinde derlemeyi kýrýyor. Reflection ile
    /// birkaç olasý ad denenerek çaðrý yapýlýyor; hiçbiri bulunamazsa derleme kýrýlmak
    /// yerine boþ liste dönüyor ve Console'a anlaþýlýr bir uyarý yazýlýyor.
    ///
    /// Bu kayýp deðil: OllamaToolAgent araç keþfi için MCPToolDiscovery sýnýfýný
    /// (ayrý C# yolu) kullanýyor. Buradaki endpoint yardýmcý bir yol; boþ dönmesi
    /// ajanýn çalýþmasýný engellemiyor.
    /// =======================================================
    /// </summary>
    [McpForUnityTool(
        "get_mcp_tools",
        Description = "Returns all registered MCP tools with metadata and schemas."
    )]
    public static class MCPToolDiscoveryCommand
    {
        public static object HandleCommand(JObject parameters)
        {
            Dictionary<string, object> registeredTools = GetRegisteredToolsSafe();

            JArray tools = new JArray();

            foreach (var item in registeredTools)
            {
                string toolName = item.Key;

                JObject metadata;

                try
                {
                    metadata = JObject.FromObject(item.Value);
                }
                catch
                {
                    metadata = new JObject();
                }

                JObject inputSchema = ResolveSchema(toolName, metadata);

                JObject tool = new JObject
                {
                    ["name"] = toolName,
                    ["description"] = metadata["description"]?.ToString() ?? "",
                    ["group"] = metadata["group"]?.ToString() ?? "core",
                    ["structuredOutput"] = metadata["structuredOutput"]?.Value<bool>() ?? true,
                    ["requiresPolling"] = metadata["requiresPolling"]?.Value<bool>() ?? false,
                    ["autoRegister"] = metadata["autoRegister"]?.Value<bool>() ?? true,
                    ["inputSchema"] = inputSchema
                };

                tools.Add(tool);

                Debug.Log(
                    "========== MCP TOOL ==========\n" +
                    toolName +
                    "\n" +
                    inputSchema.ToString()
                );
            }

            JObject result = new JObject
            {
                ["success"] = true,
                ["tools"] = tools
            };

            Debug.Log("========== MCP TOOL DISCOVERY ==========");
            Debug.Log(result.ToString());

            return result;
        }

        // =====================================================
        // REGISTRY ACCESS (REFLECTION BRIDGE)
        // =====================================================

        private static bool _registryWarningLogged;

        // Paketin farklý sürümlerinde kayýt defterini döndüren üye adlarý.
        // Sýrayla denenir; ilk çalýþan kullanýlýr.
        private static readonly string[] RegistryMethodNames =
        {
            "GetRegisteredTools", "GetAllTools", "GetTools", "GetAll", "GetHandlers", "GetCommands"
        };

        private static readonly string[] RegistryPropertyNames =
        {
            "RegisteredTools", "Tools", "All", "Handlers", "Commands"
        };

        /// <summary>
        /// CommandRegistry'den kayýtlý araç listesini alýr. Metot adý deðiþmiþ ya da
        /// tamamen kaldýrýlmýþsa boþ sözlük döner - derleme kýrýlmaz, ajan çalýþmaya
        /// devam eder.
        /// </summary>
        private static Dictionary<string, object> GetRegisteredToolsSafe()
        {
            var empty = new Dictionary<string, object>();

            Type registryType = FindType("CommandRegistry");

            if (registryType == null)
            {
                WarnOnce("CommandRegistry type was not found in any loaded assembly.");
                return empty;
            }

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            object raw = null;

            foreach (string name in RegistryMethodNames)
            {
                MethodInfo m = registryType.GetMethod(name, flags);

                if (m == null || m.GetParameters().Length != 0)
                    continue;

                try
                {
                    raw = m.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    WarnOnce($"CommandRegistry.{name}() threw: {ex.Message}");
                    return empty;
                }

                if (raw != null)
                    break;
            }

            if (raw == null)
            {
                foreach (string name in RegistryPropertyNames)
                {
                    PropertyInfo p = registryType.GetProperty(name, flags);

                    if (p == null)
                        continue;

                    try
                    {
                        raw = p.GetValue(null);
                    }
                    catch
                    {
                        continue;
                    }

                    if (raw != null)
                        break;
                }
            }

            if (raw == null)
            {
                WarnOnce(
                    "No registry accessor could be resolved on CommandRegistry. " +
                    "The MCP for Unity package API has changed; tool discovery via this endpoint returns an empty list.");
                return empty;
            }

            return Normalize(raw);
        }

        /// <summary>
        /// Kayýt defteri sözlük olarak da, koleksiyon olarak da dönebilir. Ýkisini de
        /// tek bir Dictionary&lt;string, object&gt; biçimine indiriyoruz.
        /// </summary>
        private static Dictionary<string, object> Normalize(object raw)
        {
            var result = new Dictionary<string, object>();

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    string key = entry.Key?.ToString();

                    if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                    {
                        result[key] = entry.Value;
                    }
                }

                return result;
            }

            if (raw is IEnumerable list && !(raw is string))
            {
                foreach (object item in list)
                {
                    if (item == null)
                        continue;

                    string key = ExtractName(item);

                    if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                    {
                        result[key] = item;
                    }
                }

                return result;
            }

            WarnOnce($"Registry returned an unexpected type: {raw.GetType().FullName}");
            return result;
        }

        /// <summary>
        /// Bir araç nesnesinden adýný okur. Alan adý sürüme göre deðiþebiliyor.
        /// </summary>
        private static string ExtractName(object item)
        {
            Type t = item.GetType();

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (string candidate in new[] { "Name", "ToolName", "CommandName", "Key" })
            {
                PropertyInfo p = t.GetProperty(candidate, flags);
                if (p != null)
                {
                    object value = p.GetValue(item);
                    if (value != null)
                        return value.ToString();
                }

                FieldInfo f = t.GetField(candidate, flags);
                if (f != null)
                {
                    object value = f.GetValue(item);
                    if (value != null)
                        return value.ToString();
                }
            }

            return null;
        }

        private static Type FindType(string simpleName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    // Yansýtýlamayan assembly'leri atla.
                    continue;
                }

                foreach (Type t in types)
                {
                    if (string.Equals(t.Name, simpleName, StringComparison.Ordinal))
                        return t;
                }
            }

            return null;
        }

        private static void WarnOnce(string message)
        {
            if (_registryWarningLogged)
                return;

            _registryWarningLogged = true;
            Debug.LogWarning($"[MCPToolDiscoveryCommand] {message}");
        }

        // =====================================================
        // SCHEMA RESOLVER
        // =====================================================

        /// <summary>
        /// Elle yazýlmýþ, otorite þema gerektiren tool'lar için (execute_code gibi -
        /// iç içe [ToolParameter] Parameters sýnýfý desenini KULLANMAYAN tool'lar).
        ///
        /// PUBLIC ve ResolveSchema'dan ayrýþtýrýldý ki MCPToolDiscovery (OllamaToolAgent'ýn
        /// gerçekten kullandýðý C# discovery yolu) bu özel-durum listesini burada, TEK yerde
        /// tutabilsin - reflection tabanlý yolda ayný if'leri ikinci kez yazmak yerine.
        /// Yeni bir tool ayný deseni kullanmaya baþlarsa, eklemesi gereken TEK yer burasý.
        ///
        /// NOT: 'manage_ui' özel durumu KALDIRILDI. Ýki sebeple:
        ///   1. ManageUI.GetToolSchema() paketin yeni sürümünde artýk yok
        ///      (error CS0117) - derlemeyi kýran hatalardan biri buydu.
        ///   2. Bu projede manage_ui zaten yasak: PromptRulesUI açýkça
        ///      "DO NOT use manage_ui" diyor, çünkü UI Toolkit tüm eleman aðacýný tek
        ///      bir UIDocument içinde tutuyor ve elemanlar Hierarchy'de görünmüyor.
        /// Yani bu baðýmlýlýðý kaldýrmak hem hatayý çözüyor hem de projenin hedefiyle
        /// tutarlý. manage_ui yine de çaðrýlýrsa aþaðýdaki genel þema kullanýlýr.
        /// </summary>
        public static JObject GetSpecialCaseSchema(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            if (toolName == "execute_code")
            {
                return new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["code"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "C# code to execute inside Unity"
                        }
                    },
                    ["required"] = new JArray
                    {
                        "code"
                    },
                    ["additionalProperties"] = false
                };
            }

            return null;
        }

        private static JObject ResolveSchema(string toolName, JObject metadata)
        {
            JObject specialCase = GetSpecialCaseSchema(toolName);
            if (specialCase == null && metadata["name"] != null)
            {
                specialCase = GetSpecialCaseSchema(metadata["name"].ToString());
            }

            if (specialCase != null)
            {
                return specialCase;
            }

            JObject schema = metadata["inputSchema"] as JObject;

            if (schema != null && schema.HasValues)
            {
                return schema;
            }

            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray(),
                ["additionalProperties"] = true
            };
        }
    }
}
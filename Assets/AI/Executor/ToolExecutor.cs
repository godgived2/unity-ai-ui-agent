using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using AI.Discovery;

namespace AI.Executor
{
    /// <summary>
    /// AI Agent için üst seviye tool çalýþtýrýcý. MCPExecutor'ýn üstünde çalýþýr ve
    /// çaðrýyý göndermeden önce ToolRegistry'den doðrular.
    ///
    /// ============ KAPSAM UYARISI ============
    /// Canlý ReAct döngüsü (OllamaToolAgent) bu sýnýfý KULLANMIYOR - o, MCPExecutor'ý
    /// doðrudan çaðýrýyor. Bu sýnýf muhtemelen silinmiþ orkestratör zincirinden kalma.
    /// Silmeden önce ToolRegistry ve ToolExecutor'a baþka bir baþvuru olup olmadýðýný
    /// kontrol edin.
    /// ========================================
    ///
    /// ============ DÜZELTÝLEN KRÝTÝK HATA ============
    /// Bu sýnýf hata durumlarýnda DÜZ METÝN döndürüyordu:
    ///     return $"Tool not registered: {toolName}";
    ///
    /// OllamaToolAgent ve UiMacroExpander bir sonucu deðerlendirirken önce JSON
    /// ayrýþtýrmayý deniyor, olmazsa LooksLikeError ile "error" / "exception" /
    /// "failed" / "not found" kelimelerini arýyor.
    ///
    /// "Tool not registered: X" bu kelimelerin HÝÇBÝRÝNÝ içermiyor. Yani kayýtlý
    /// olmayan bir tool çaðrýsý BAÞARILI sayýlýyor, imzasý kaydediliyor ve geçmiþe
    /// "çalýþtý" olarak yazýlýyordu. Model hiçbir þeyin olmadýðýný fark edemiyordu.
    ///
    /// Artýk tüm dönüþler MCP zarfýyla ayný biçimde: {"status","result":{"success",
    /// "message"}}.
    /// ================================================
    /// </summary>
    public class ToolExecutor
    {
        public bool EnableLogging { get; set; }

        // =====================================
        // EXECUTE TOOL
        // =====================================

        public async Task<string> Execute(
            string toolName,
            JObject parameters,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return Failure("The tool name is empty. Every call must name a tool.");
            }

            toolName = toolName.Trim();

            // =====================================
            // TOOL REGISTRY CHECK
            // =====================================

            ToolMetadata tool;

            try
            {
                tool = ToolRegistry.Get(toolName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolExecutor] Registry lookup for '{toolName}' threw: {ex.Message}");
                return Failure($"Could not look up '{toolName}' in the tool registry: {ex.Message}");
            }

            if (tool == null)
            {
                Debug.LogWarning($"[ToolExecutor] Tool not found in registry: {toolName}");

                return Failure(
                    $"Tool '{toolName}' is not registered. It does not exist, or tool discovery has not run yet. " +
                    "Use one of the tools listed in the schema.");
            }

            if (!tool.Enabled)
            {
                return Failure($"Tool '{toolName}' exists but is disabled, so it cannot be called.");
            }

            // =====================================
            // COMMAND BUILD
            // =====================================

            JObject command = new JObject
            {
                ["type"] = toolName,
                ["params"] = parameters ?? new JObject()
            };

            try
            {
                if (EnableLogging)
                {
                    Debug.Log($"[ToolExecutor] Executing '{toolName}'.");
                }

                return await MCPExecutor.Execute(command, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Failure($"Execution of '{toolName}' was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolExecutor] '{toolName}' threw: {ex}");
                return Failure($"'{toolName}' failed: {ex.Message}");
            }
        }

        // =====================================
        // CHECK TOOL
        // =====================================

        public bool CanExecute(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            try
            {
                ToolMetadata tool = ToolRegistry.Get(toolName.Trim());
                return tool != null && tool.Enabled;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ToolExecutor] CanExecute('{toolName}') failed: {ex.Message}");
                return false;
            }
        }

        // =====================================
        // DEBUG
        // =====================================

        /// <summary>
        /// Bir tool'un kayýt bilgisini insan tarafýndan okunabilir biçimde döndürür.
        ///
        /// NOT: metot adý bilerek 'Describe' - 'Debug' OLMAMALI. Bir sýnýfýn içinde
        /// 'Debug' adlý bir üye varsa, o sýnýftaki her 'Debug.Log(...)' çaðrýsý
        /// UnityEngine.Debug yerine o üyeye çözülür ve anlaþýlmaz bir derleme hatasý
        /// verir. VerificationPipeline'da tam olarak bu tuzak vardý.
        /// </summary>
        public string Describe(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return "No tool name given.";

            ToolMetadata tool;

            try
            {
                tool = ToolRegistry.Get(toolName.Trim());
            }
            catch (Exception ex)
            {
                return $"Registry lookup failed: {ex.Message}";
            }

            if (tool == null)
                return $"Tool '{toolName}' is not registered.";

            return
                $"Tool     : {tool.Name}\n" +
                $"Category : {tool.Category}\n" +
                $"Enabled  : {tool.Enabled}";
        }

        /// <summary>Geriye dönük uyumluluk. Yeni kod Describe kullanmalý.</summary>
        public string DebugTool(string toolName) => Describe(toolName);

        // =====================================
        // HELPERS
        // =====================================

        /// <summary>
        /// MCP zarfýyla AYNI biçimde bir hata sonucu üretir.
        ///
        /// Bu þart: OllamaToolAgent.WasExecutionSuccessful ve
        /// UiMacroExpander.WasStepSuccessful önce result.success'e, sonra status'e
        /// bakýyor. Düz metin dönmek, hatanýn baþarý sayýlmasýna yol açýyordu.
        /// </summary>
        private static string Failure(string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "Unknown tool executor error." : message;

            return new JObject
            {
                ["status"] = "error",
                ["success"] = false,
                ["error"] = text,
                ["result"] = new JObject
                {
                    ["success"] = false,
                    ["message"] = text
                }
            }.ToString(Formatting.None);
        }
    }
}
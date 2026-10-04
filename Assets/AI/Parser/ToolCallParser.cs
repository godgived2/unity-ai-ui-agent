using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using AI.Models;

namespace AI.Parser
{
    /// <summary>
    /// Ollama/Qwen cevabýndan MCP araç çaðrýsýný ToolCall modeline çevirir.
    ///
    /// ============ KAPSAM UYARISI ============
    /// Canlý ReAct döngüsü (OllamaToolAgent) bu sýnýfý KULLANMIYOR - o, ResponseParser
    /// üzerinden çalýþýyor. Bu sýnýf AI.Models.ToolCall'a baðlý ve muhtemelen silinmiþ
    /// olan orkestratör zincirinden kalma.
    ///
    /// Önceki sürümde bu bir sorundu: iki ayrý ayrýþtýrma yolu vardý, farklý
    /// davranýyorlardý ve biri (JsonRepair üzerinden giden bu yol) hiç onarým
    /// yapmýyordu. Ýkisi ayrýþtýkça hata ayýklama imkânsýzlaþýyordu.
    ///
    /// Artýk bu sýnýf TEK BÝR ayrýþtýrma yoluna, ResponseParser'a devrediyor. Sarma
    /// açma, onarým, kesilme tespiti - hepsi orada, tek yerde. Burada yalnýzca
    /// ToolCall'a dönüþtürme kaldý.
    ///
    /// Bu sýnýfý hiçbir yer kullanmýyorsa silinebilir; ama silmeden önce
    /// AI.Models.ToolCall'ý kullanan baþka bir yer olup olmadýðý kontrol edilmeli.
    /// ========================================
    /// </summary>
    public static class ToolCallParser
    {
        private static readonly ResponseParser Parser = new ResponseParser();

        // =====================================
        // PARSE STRING RESPONSE
        // =====================================

        public static ToolCall Parse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            ParsedResponse parsed = Parser.ParseResponse(response);

            // Bitiþ ve bozuk cevap bir araç çaðrýsý DEÐÝLDÝR. Eski kod ikisini de
            // sessizce null döndürüyordu ve çaðýran aradaki farký göremiyordu.
            if (parsed.Kind != ResponseKind.ToolCall)
                return null;

            return Parse(parsed.Command);
        }

        // =====================================
        // PARSE JSON OBJECT
        // =====================================

        public static ToolCall Parse(JObject json)
        {
            if (json == null)
                return null;

            string toolName = json["type"]?.ToString();

            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            JObject paramsObject = json["params"] as JObject ?? new JObject();

            Dictionary<string, object> arguments;

            try
            {
                arguments = paramsObject.ToObject<Dictionary<string, object>>()
                            ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                // Ýç içe JSON yapýlarý Dictionary<string, object>'e her zaman temiz
                // dönüþmüyor. Eski kod burada patlýyordu; artýk alan alan, en kötü
                // ihtimalle ham JToken olarak taþýyoruz.
                arguments = new Dictionary<string, object>();

                foreach (JProperty prop in paramsObject.Properties())
                {
                    arguments[prop.Name] = prop.Value is JValue value
                        ? value.Value
                        : (object)prop.Value;
                }
            }

            return new ToolCall
            {
                ToolName = toolName.Trim(),
                Arguments = arguments
            };
        }

        // =====================================
        // HELPERS
        // =====================================

        public static bool HasToolCall(string response)
        {
            return Parse(response) != null;
        }

        public static string GetToolName(string response)
        {
            return Parse(response)?.ToolName;
        }

        public static Dictionary<string, object> GetArguments(string response)
        {
            return Parse(response)?.Arguments ?? new Dictionary<string, object>();
        }
    }
}
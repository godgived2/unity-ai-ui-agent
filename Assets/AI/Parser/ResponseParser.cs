using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Parser
{
    /// <summary>
    /// Modelin bir adýmdaki cevabýnýn NE OLDUÐU.
    ///
    /// ============ NEDEN BU TÝP EKLENDÝ - KRÝTÝK ============
    /// Eski API tek bir JObject döndürüyordu ve null "bitirmek istiyor" demekti.
    /// Ama ParseToolCommand ÜÇ tamamen farklý durumda null dönüyordu:
    ///
    ///   1. Model gerçekten bitirmek istiyor      -> doðru davranýþ
    ///   2. JSON KESÝLMÝÞ (num_predict doldu)     -> HATA, bitiþ sanýlýyordu
    ///   3. Model sarmalayýcý kullanmýþ
    ///      {"tool_call":{"type":"..."}}          -> HATA, bitiþ sanýlýyordu
    ///
    /// 2 ve 3 birer hatadýr ama ajan ikisini de "görev tamamlandý" okuyup duruyordu.
    /// Yarým kalmýþ bir panel çaðrýsý, iþin bittiði anlamýna geliyordu - ve hiçbir
    /// uyarý basýlmadýðý için teþhis edilmesi neredeyse imkânsýzdý.
    ///
    /// Artýk üçü ayrý: ToolCall / Finished / Malformed.
    /// =======================================================
    /// </summary>
    public enum ResponseKind
    {
        /// <summary>Çalýþtýrýlabilir bir araç çaðrýsý.</summary>
        ToolCall,

        /// <summary>Model görevin bittiðini söylüyor. Message kullanýcýya gösterilir.</summary>
        Finished,

        /// <summary>Cevap bozuk. Bitiþ DEÐÝL - modele hata olarak bildirilmeli.</summary>
        Malformed
    }

    /// <summary>Tek bir model cevabýnýn ayrýþtýrýlmýþ hâli.</summary>
    public sealed class ParsedResponse
    {
        public ResponseKind Kind;

        /// <summary>Kind == ToolCall ise normalize edilmiþ komut.</summary>
        public JObject Command;

        /// <summary>Kind == Finished ise kullanýcýya gösterilecek DÜZ METÝN.</summary>
        public string Message = string.Empty;

        /// <summary>Kind == Malformed ise modele söylenecek sorun.</summary>
        public string Problem = string.Empty;

        /// <summary>Ham JSON geçerli deðildi, onarým gerekti.</summary>
        public bool WasRepaired;

        /// <summary>Cevap ortasýnda kesilmiþ görünüyor (num_predict sýnýrý).</summary>
        public bool LooksTruncated;

        public static ParsedResponse Tool(JObject command, bool repaired, bool truncated)
        {
            return new ParsedResponse
            {
                Kind = ResponseKind.ToolCall,
                Command = command,
                WasRepaired = repaired,
                LooksTruncated = truncated
            };
        }

        public static ParsedResponse Done(string message)
        {
            return new ParsedResponse
            {
                Kind = ResponseKind.Finished,
                Message = message ?? string.Empty
            };
        }

        public static ParsedResponse Broken(string problem, bool truncated = false)
        {
            return new ParsedResponse
            {
                Kind = ResponseKind.Malformed,
                Problem = problem ?? "The response could not be parsed.",
                LooksTruncated = truncated
            };
        }
    }

    /// <summary>
    /// MCP JSON ayrýþtýrýcýsý.
    ///
    /// Sorumluluklar:
    /// - JSON'u güvenle çýkarmak (iç içe parantez ve string kaçýþýna dayanýklý)
    /// - MCP biçimini normalize etmek (tool -> type, parameters -> params)
    /// - Bozuk cevabý BÝTÝÞTEN ayýrmak
    /// - Bitiþ cevabýný kullanýcýya gösterilebilir düz metne çevirmek
    /// </summary>
    public class ResponseParser
    {
        /// <summary>
        /// Modelin araç çaðrýsýný bir kabuðun içine sardýðý durumlarda açýlacak
        /// anahtarlar. Gerçekte görülen biçimler.
        /// </summary>
        private static readonly string[] WrapperKeys =
        {
            "tool_call", "toolCall", "function_call", "functionCall",
            "function", "tool", "call", "command", "action_input"
        };

        /// <summary>Bitiþ cevabýnda metnin bulunabileceði alan adlarý.</summary>
        private static readonly string[] MessageKeys =
        {
            "message", "response", "answer", "summary", "text", "content",
            "final", "final_answer", "finalAnswer", "explanation", "description",
            "result", "output", "reply"
        };

        /// <summary>
        /// Model açýkça "bitti" demek için bu tool adlarýný kullanabilir. Prompt bunu
        /// istemiyor ama modeller kendiliðinden üretiyor; bitiþi araç çaðrýsý sanýp
        /// "böyle bir tool yok" hatasý vermek boþa adým harcýyordu.
        /// </summary>
        private static readonly HashSet<string> FinishToolNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "task_complete", "task_completed", "finish", "finished", "done",
                "complete", "final_answer", "finalanswer", "stop", "end_task", "no_op"
            };

        /// <summary>
        /// Bir objede bunlardan biri varsa, 'type' eksik olsa bile bu bir ARAÇ ÇAÐRISI
        /// denemesidir - bitiþ deðil. Bozuk çaðrýyý bitiþ sanmaya karþý son savunma.
        /// </summary>
        private static readonly string[] ToolParamHints =
        {
            "parent", "target", "componentType", "component_type", "anchorMin",
            "anchorMax", "code", "properties", "isOn", "placeholder", "buttonColor"
        };

        public bool EnableVerboseLogging { get; set; }

        // =====================================
        // ANA GÝRÝÞ NOKTASI
        // =====================================

        /// <summary>
        /// Model cevabýný sýnýflandýrýr. Ajan bunu kullanmalý - ParseToolCommand'ýn
        /// null dönüþü üç farklý durumu birbirine karýþtýrýyor.
        /// </summary>
        public ParsedResponse ParseResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                // Boþ cevap bitiþ DEÐÝL. Ollama boþ döndüyse (model yükleniyor,
                // baðlam taþtý, kaynak sýkýþtý) bu bir arýzadýr. Eski kod bunu
                // "görev bitti" sayýp kullanýcýya boþ bir cevap gösteriyordu.
                return ParsedResponse.Broken(
                    "The model returned an empty response. Emit either one valid tool call as JSON, or a short completion message.");
            }

            string jsonText = JsonRepair.ExtractJson(response, out bool truncated);

            // Hiç JSON yok: düz metin cevap. forceJson açýkken nadir ama mümkün.
            if (string.IsNullOrEmpty(jsonText))
            {
                return ParsedResponse.Done(response.Trim());
            }

            if (!JsonRepair.TryRepair(response, out JObject parsed, out bool repaired))
            {
                string problem = truncated
                    ? "Your JSON was cut off before it finished - the response hit the token limit. Emit a SHORTER call: fewer optional parameters, and no long inline code."
                    : "Your response was not valid JSON and could not be repaired. Emit exactly one JSON object with a 'type' field and a 'params' object, and nothing else.";

                return ParsedResponse.Broken(problem, truncated);
            }

            if (repaired && EnableVerboseLogging)
            {
                Debug.LogWarning(
                    "[ResponseParser] The model's JSON was malformed and had to be repaired" +
                    (truncated ? " (it was truncated - consider lowering the prompt size or raising num_predict)." : "."));
            }

            JObject unwrapped = Unwrap(parsed);

            // ---------- Araç çaðrýsý mý? ----------
            string toolName = ReadToolName(unwrapped);

            if (!string.IsNullOrWhiteSpace(toolName))
            {
                if (FinishToolNames.Contains(toolName))
                {
                    // Model açýkça bitiþ sinyali üretmiþ. Bunu "bilinmeyen tool"
                    // hatasý olarak iþlemek bir adým boþa harcýyordu.
                    return ParsedResponse.Done(ExtractMessage(unwrapped));
                }

                JObject normalized = Normalize(unwrapped);

                if (normalized != null)
                    return ParsedResponse.Tool(normalized, repaired, truncated);
            }

            // ---------- 'type' yok. Bitiþ mi, bozuk çaðrý mý? ----------
            if (truncated)
            {
                return ParsedResponse.Broken(
                    "Your JSON was cut off before the 'type' field was complete. Emit a shorter, complete tool call.",
                    true);
            }

            if (LooksLikeBrokenToolCall(unwrapped))
            {
                return ParsedResponse.Broken(
                    "Your JSON looks like a tool call but has no 'type' field. Every tool call must be exactly: " +
                    "{\"type\":\"<tool_name>\",\"params\":{...}}. Re-send it with the 'type' field.");
            }

            string message = ExtractMessage(unwrapped);

            if (!string.IsNullOrWhiteSpace(message))
                return ParsedResponse.Done(message);

            // Ne araç çaðrýsý ne okunabilir metin. Bitiþ saymak riskli - modelden
            // netleþtirmesini istiyoruz.
            return ParsedResponse.Broken(
                "Your response was neither a valid tool call nor a readable message. " +
                "Emit either {\"type\":\"<tool_name>\",\"params\":{...}} to continue, or a short plain sentence to finish.");
        }

        /// <summary>
        /// Bir cevabý kullanýcýya gösterilecek düz metne çevirir.
        ///
        /// forceJson açýkken model düz metin ÜRETEMEZ - gramer JSON'a zorluyor. Bu
        /// yüzden bitiþ cevabý {"message":"..."} gibi geliyordu ve eski kod bunu ham
        /// JSON olarak kullanýcýya gösteriyordu.
        /// </summary>
        public string ToDisplayText(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            string jsonText = JsonRepair.ExtractJson(response, out _);

            if (string.IsNullOrEmpty(jsonText))
                return response.Trim();

            if (!JsonRepair.TryRepair(response, out JObject parsed))
                return response.Trim();

            string message = ExtractMessage(Unwrap(parsed));
            return string.IsNullOrWhiteSpace(message) ? response.Trim() : message;
        }

        // =====================================
        // GERÝYE DÖNÜK UYUMLU API
        // =====================================

        /// <summary>
        /// Eski imza. Araç çaðrýsý deðilse null döner.
        ///
        /// DÝKKAT: null artýk üç farklý durumu temsil ediyor olabilir. Yeni kod
        /// ParseResponse kullanmalý - bitiþ ile bozuk cevabý ayýrt edebilmek için.
        /// </summary>
        public JObject ParseToolCommand(string response)
        {
            ParsedResponse result = ParseResponse(response);
            return result.Kind == ResponseKind.ToolCall ? result.Command : null;
        }

        public bool IsToolCommand(string response, HashSet<string> availableTools = null)
        {
            ParsedResponse result = ParseResponse(response);

            if (result.Kind != ResponseKind.ToolCall)
                return false;

            string tool = GetToolName(result.Command);

            if (string.IsNullOrWhiteSpace(tool))
                return false;

            if (availableTools != null && availableTools.Count > 0 && !availableTools.Contains(tool))
            {
                Debug.LogWarning($"[ResponseParser] Unknown MCP tool: {tool}");
                return false;
            }

            return true;
        }

        public string GetToolName(JObject command)
        {
            return command?["type"]?.ToString() ?? string.Empty;
        }

        public JObject GetParameters(JObject command)
        {
            if (command?["params"] is JObject parameters)
                return parameters;

            return new JObject();
        }

        // =====================================
        // ÇOKLU KOMUT
        // =====================================

        /// <summary>
        /// Bir dizi içindeki komutlarý çýkarýr. Ajan adým baþýna TEK çaðrý bekliyor,
        /// bu yüzden dizi gelmesi genelde modelin kuralý ihlal ettiðini gösterir -
        /// ama veriyi kaybetmemek için yine de ayrýþtýrýyoruz.
        /// </summary>
        public List<JObject> ParseMultipleCommands(string response)
        {
            var commands = new List<JObject>();

            if (string.IsNullOrWhiteSpace(response))
                return commands;

            // Derinlik duyarlý dizi çýkarýmý: eski naif ilk '[' / son ']' aramasý
            // execute_code yüklerinde (kod içinde köþeli parantez) kýrýlýyordu.
            string arrayText = JsonRepair.ExtractJsonArray(response);

            if (!string.IsNullOrEmpty(arrayText))
            {
                try
                {
                    JArray array = JArray.Parse(arrayText);

                    foreach (JToken item in array)
                    {
                        if (!(item is JObject obj))
                            continue;

                        JObject cmd = Normalize(Unwrap(obj));

                        if (cmd != null)
                            commands.Add(cmd);
                    }

                    if (commands.Count > 0)
                        return commands;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ResponseParser] Array parse failed: {ex.Message}");
                }
            }

            JObject single = ParseToolCommand(response);

            if (single != null)
                commands.Add(single);

            return commands;
        }

        // =====================================
        // NORMALIZATION
        // =====================================

        /// <summary>
        /// Modelin araç çaðrýsýný sardýðý kabuklarý açar.
        ///
        /// Gerçekte görülen biçimler:
        ///   {"tool_call":{"type":"create_ui_panel","params":{...}}}
        ///   {"function":{"name":"create_ui_panel","arguments":{...}}}
        ///   [{"type":"create_ui_panel",...}]
        ///
        /// Eskiden bunlarýn hepsi üst düzeyde 'type' bulunamadýðý için null dönüyor,
        /// ajan da bunu "görev bitti" sanýyordu. Sarmalayýcýyý açmak, boþa giden bir
        /// adýmý ve yanlýþ bir erken bitiþi birden önlüyor.
        /// </summary>
        private JObject Unwrap(JObject command)
        {
            if (command == null)
                return null;

            // Zaten doðru biçimde.
            if (!string.IsNullOrWhiteSpace(ReadToolName(command)))
                return command;

            foreach (string key in WrapperKeys)
            {
                JToken inner = command[key];

                if (inner is JObject innerObj && !string.IsNullOrWhiteSpace(ReadToolName(innerObj)))
                {
                    if (EnableVerboseLogging)
                        Debug.LogWarning($"[ResponseParser] Unwrapped a '{key}' wrapper around the tool call.");

                    return innerObj;
                }

                // {"function":{"name":"x","arguments":{...}}}
                if (inner is JObject fn && fn["name"] != null)
                {
                    var rebuilt = new JObject
                    {
                        ["type"] = fn["name"],
                        ["params"] = fn["arguments"] as JObject
                                     ?? fn["parameters"] as JObject
                                     ?? fn["params"] as JObject
                                     ?? new JObject()
                    };

                    if (EnableVerboseLogging)
                        Debug.LogWarning($"[ResponseParser] Rebuilt a '{key}' function-style call.");

                    return rebuilt;
                }
            }

            // Tek elemanlý dizi sarmalayýcýsý: {"calls":[{...}]}
            foreach (JProperty prop in command.Properties())
            {
                if (prop.Value is JArray arr && arr.Count == 1 && arr[0] is JObject only &&
                    !string.IsNullOrWhiteSpace(ReadToolName(only)))
                {
                    return only;
                }
            }

            return command;
        }

        /// <summary>'type' veya 'tool' veya 'name' alanýndan araç adýný okur.</summary>
        private static string ReadToolName(JObject command)
        {
            if (command == null)
                return null;

            string name = command["type"]?.ToString();

            if (string.IsNullOrWhiteSpace(name))
                name = command["tool"]?.ToString();

            if (string.IsNullOrWhiteSpace(name))
                name = command["tool_name"]?.ToString();

            // 'name' yalnýzca yanýnda bir parametre kabý varsa araç adýdýr - aksi
            // hâlde create_ui_panel'in kendi 'name' parametresiyle karýþýr.
            if (string.IsNullOrWhiteSpace(name) &&
                (command["arguments"] != null || command["parameters"] != null))
            {
                name = command["name"]?.ToString();
            }

            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        private JObject Normalize(JObject command)
        {
            if (command == null)
                return null;

            string toolName = ReadToolName(command);

            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            command["type"] = toolName;

            // parameters / arguments -> params
            if (command["params"] == null)
            {
                if (command["parameters"] != null)
                    command["params"] = command["parameters"];
                else if (command["arguments"] != null)
                    command["params"] = command["arguments"];
            }

            // MCP standart olarak bir 'params' objesi bekler.
            if (!(command["params"] is JObject))
            {
                command["params"] = new JObject();
            }

            // NOT: burada eskiden manage_ui'ye özel bir uyarý vardý. manage_ui bu
            // projede tamamen yasak (UI Toolkit hedef sistem deðil), o yüzden o dal
            // hem ölü koddu hem de aracý adýyla meþrulaþtýrýyordu. Kaldýrýldý.

            return command;
        }

        // =====================================
        // METÝN ÇIKARIMI
        // =====================================

        /// <summary>
        /// Bitiþ cevabýndaki insan tarafýndan okunacak metni bulur.
        /// </summary>
        private string ExtractMessage(JObject obj)
        {
            if (obj == null)
                return string.Empty;

            // 1. Bilinen metin alanlarý, öncelik sýrasýyla.
            foreach (string key in MessageKeys)
            {
                JToken value = obj[key];

                if (value is JValue v && v.Type == JTokenType.String)
                {
                    string text = v.ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            // 2. params/arguments içinde olabilir.
            if (obj["params"] is JObject p)
            {
                foreach (string key in MessageKeys)
                {
                    if (p[key] is JValue pv && pv.Type == JTokenType.String)
                    {
                        string text = pv.ToString().Trim();

                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }
            }

            // 3. Son çare: üst düzeydeki tüm string deðerleri birleþtir. Model
            // {"durum":"tamamlandý","not":"4 eleman oluþturuldu"} gibi bir þey
            // üretmiþ olabilir.
            var sb = new StringBuilder();

            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value is JValue jv && jv.Type == JTokenType.String)
                {
                    string text = jv.ToString().Trim();

                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (sb.Length > 0)
                        sb.Append(' ');

                    sb.Append(text);
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// 'type' yok ama araç parametrelerine benzeyen alanlar var mý. Varsa bu bir
        /// bozuk araç çaðrýsýdýr, bitiþ deðil.
        /// </summary>
        private static bool LooksLikeBrokenToolCall(JObject obj)
        {
            if (obj == null)
                return false;

            if (obj["params"] is JObject || obj["parameters"] is JObject || obj["arguments"] is JObject)
                return true;

            foreach (string hint in ToolParamHints)
            {
                if (obj[hint] != null)
                    return true;
            }

            return false;
        }
    }
}
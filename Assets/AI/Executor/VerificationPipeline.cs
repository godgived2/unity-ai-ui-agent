using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Executor
{
    /// <summary>
    /// Tool çalýþtýrma sonrasý doðrulama. Agent'ýn yaptýðý iþlemin baþarýlý olup
    /// olmadýðýný kontrol eder.
    ///
    /// ============ DÜZELTÝLEN ÝKÝ SORUN ============
    ///
    /// 1. GÝZLÝ DERLEME TUZAÐI: bu sýnýfta 'Debug' adlý bir metot vardý. Bir sýnýfýn
    ///    içinde 'Debug' adlý bir üye bulunduðunda, o sýnýftaki her 'Debug.Log(...)'
    ///    çaðrýsý UnityEngine.Debug yerine O ÜYEYE çözülür ve anlaþýlmaz bir derleme
    ///    hatasý verir. Sýnýf hiç log yazmadýðý için bugün derleniyordu - ama bir
    ///    satýr eklendiði an patlardý. Metot 'Describe' olarak yeniden adlandýrýldý;
    ///    eski ad geriye dönük uyumluluk için sarmalayýcý olarak duruyor.
    ///
    /// 2. ALT DÝZE ÝLE HATA AVI: IsError tüm çýktýda "error" alt dizesini arýyordu.
    ///    Bir etiketin metni "No errors" olan BAÞARILI bir sonuç hata sayýlýyordu.
    ///    Ayný þekilde "Failed at step" içeren gerçek bir hata da yakalanýyordu ama
    ///    yanlýþ pozitifler daha sýk. Artýk ÖNCE JSON zarfý okunuyor (status /
    ///    result.success), alt dize taramasý yalnýzca JSON olmayan çýktýlarda son çare
    ///    olarak kullanýlýyor - OllamaToolAgent ile ayný sýra.
    /// ==============================================
    /// </summary>
    public class VerificationPipeline
    {
        // =====================================
        // VERIFY RESULT
        // =====================================

        public VerificationResult Verify(string toolName, string output)
        {
            var result = new VerificationResult
            {
                ToolName = toolName,
                Output = output
            };

            // EMPTY CHECK
            if (string.IsNullOrWhiteSpace(output))
            {
                result.Success = false;
                result.Message = "Tool returned an empty result.";
                return result;
            }

            // 1. ZARF OKUMA - asýl yöntem.
            //
            // MCP ve macro katmaný sonucu {"status":..,"result":{"success":..}}
            // biçiminde döndürüyor. Bu alanlar varsa tahmine gerek yok.
            try
            {
                JObject parsed = JObject.Parse(output);

                string status = parsed["status"]?.ToString();

                if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = false;
                    result.Message = ReadMessage(parsed) ?? "Tool execution returned an error.";
                    return result;
                }

                if (parsed["result"] is JObject inner && inner["success"] != null)
                {
                    bool ok = inner["success"].ToObject<bool>();

                    result.Success = ok;
                    result.Message = ReadMessage(parsed)
                                     ?? (ok ? "Tool executed successfully." : "Tool reported failure.");
                    return result;
                }

                if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    result.Success = true;
                    result.Message = ReadMessage(parsed) ?? "Tool executed successfully.";
                    return result;
                }

                // Geçerli JSON ama tanýdýk bir zarf deðil. Metin taramasýna düþ.
            }
            catch (JsonException)
            {
                // JSON deðil - aþaðýdaki metin taramasýna düþüyor.
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VerificationPipeline] Unexpected parse error: {ex.Message}");
            }

            // 2. SON ÇARE - metin taramasý.
            //
            // Yalnýzca yapýsal bilgi hiç yoksa. Yanlýþ pozitif üretebildiði için
            // birinci yöntem deðil.
            if (LooksLikeError(output))
            {
                result.Success = false;
                result.Message = "Tool output looks like an error (no structured status was present).";
                return result;
            }

            result.Success = true;
            result.Message = "Tool executed successfully.";
            return result;
        }

        // =====================================
        // JSON VERIFY
        // =====================================

        public VerificationResult VerifyJson(string toolName, JObject json)
        {
            if (json == null)
            {
                return new VerificationResult
                {
                    ToolName = toolName,
                    Success = false,
                    Message = "Invalid JSON result."
                };
            }

            // Ayný zarf mantýðýný yeniden kullan - eskiden bu metot her JSON'u
            // koþulsuz baþarý sayýyordu, içinde açýk bir hata olsa bile.
            return Verify(toolName, json.ToString(Formatting.None));
        }

        // =====================================
        // ERROR DETECTION
        // =====================================

        /// <summary>
        /// Yapýsal bilgi yokken kullanýlan kaba sezgi. OllamaToolAgent.LooksLikeError
        /// ile ayný kelime kümesi - ikisi ayrýþýrsa ayný çýktý iki katmanda farklý
        /// yorumlanýr.
        /// </summary>
        private static bool LooksLikeError(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadMessage(JObject parsed)
        {
            string message = parsed["result"]?["message"]?.ToString();

            if (string.IsNullOrWhiteSpace(message))
                message = parsed["error"]?.ToString();

            if (string.IsNullOrWhiteSpace(message))
                message = parsed["message"]?.ToString();

            return string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        }

        // =====================================
        // DESCRIBE
        // =====================================

        /// <summary>
        /// Bir doðrulama sonucunu okunabilir metne çevirir.
        ///
        /// ADI BÝLEREK 'Describe'. 'Debug' OLARAK ADLANDIRMAYIN: bu sýnýfýn içindeki
        /// her Debug.Log çaðrýsý UnityEngine.Debug yerine o üyeye çözülür ve derleme
        /// anlaþýlmaz bir hatayla kýrýlýr.
        /// </summary>
        public string Describe(VerificationResult result)
        {
            if (result == null)
                return "No verification result";

            return
                $"Tool    : {result.ToolName}\n" +
                $"Success : {result.Success}\n" +
                $"Message : {result.Message}";
        }

        /// <summary>Geriye dönük uyumluluk. Yeni kod Describe kullanmalý.</summary>
        public string DebugResult(VerificationResult result) => Describe(result);
    }

    // =====================================
    // RESULT MODEL
    // =====================================

    /// <summary>
    /// DÝKKAT - AD ÇAKIÞMASI: AI.Models ad alanýnda da VerificationResult adlý bir tip
    /// var. Ýkisi farklý ad alanlarýnda olduðu için derleme kýrýlmýyor, ama ayný
    /// dosyada hem 'using AI.Models' hem 'using AI.Executor' varsa çaðrý belirsiz hale
    /// gelir ve derleyici tam nitelikli ad ister.
    ///
    /// Ýkisinden birinin silinmesi doðru olur; hangisinin kullanýldýðý netleþene kadar
    /// ikisi de duruyor.
    /// </summary>
    [Serializable]
    public class VerificationResult
    {
        public string ToolName;
        public bool Success;
        public string Message;
        public string Output;
    }
}
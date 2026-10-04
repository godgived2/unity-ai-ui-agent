using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI.Parser
{
    /// <summary>
    /// LLM (Qwen/Ollama) çýktýlarýndaki bozuk JSON'larý çýkarýr ve onarýr.
    ///
    /// ============ NEDEN YENÝDEN YAZILDI ============
    /// Önceki sürümün Repair() metodu gövdesiz bir TODO idi:
    ///     // Sonradan buraya:
    ///     // - eksik virgül düzeltme
    ///     // - quote düzeltme
    ///     // - trailing comma temizleme
    ///     return json;
    /// Yani "onarým" adýný taþýyan sýnýf hiçbir þey onarmýyordu. Üstelik canlý yol
    /// olan ResponseParser bu sýnýfý HÝÇ çaðýrmýyordu - iki paralel ayrýþtýrma yolu
    /// vardý ve biri ölüydü.
    ///
    /// Artýk gerçek onarým yapýyor ve ResponseParser'ýn tek kurtarma katmaný bu.
    ///
    /// EN ÖNEMLÝ YETENEK - KESÝLMÝÞ JSON:
    /// num_predict (1024) dolduðunda model JSON'un ortasýnda kesiliyor. Eski kodda
    /// bu JObject.Parse hatasýna, o da ParseToolCommand'ýn null dönmesine, o da
    /// ajanýn "görev bitti" sanmasýna yol açýyordu. Artýk açýk kalan string ve
    /// parantezler kapatýlýp kurtarma deneniyor, kurtarýlamazsa KESÝLMÝÞ olduðu
    /// açýkça raporlanýyor.
    /// ===============================================
    /// </summary>
    public static class JsonRepair
    {
        /// <summary>Onarým sýrasýnda yapýlan azami kapatma sayýsý (sonsuz döngü korumasý).</summary>
        private const int MaxClosings = 64;

        // =====================================
        // EXTRACTION
        // =====================================

        /// <summary>
        /// Metindeki ilk dengeli JSON objesini çýkarýr.
        ///
        /// Eski sürüm ilk '{' ile son '}' arasýný alýyordu. Bu, execute_code
        /// yüklerinde kýrýlýyor: gönderilen C# kodunun içinde de süslü parantez var
        /// ve model kod bloðundan sonra bir þey yazdýysa son '}' yanlýþ yeri
        /// gösteriyor. Artýk derinlik sayýmý ile, string içi parantezler yok
        /// sayýlarak bulunuyor.
        /// </summary>
        public static string ExtractJson(string response)
        {
            return ExtractJson(response, out _);
        }

        /// <summary>
        /// <paramref name="looksTruncated"/> true dönerse JSON açýk kalmýþ demektir -
        /// yani model cevabý ortasýnda kesilmiþ. Bu bilgi kritik: kesilmiþ bir cevap
        /// "görev bitti" ile karýþtýrýlmamalý.
        /// </summary>
        public static string ExtractJson(string response, out bool looksTruncated)
        {
            looksTruncated = false;

            if (string.IsNullOrWhiteSpace(response))
                return null;

            string cleaned = StripCodeFences(response);

            int firstOpen = cleaned.IndexOf('{');
            if (firstOpen < 0)
                return null;

            int depth = 0;
            bool insideString = false;
            bool isEscaped = false;

            for (int i = firstOpen; i < cleaned.Length; i++)
            {
                char c = cleaned[i];

                if (insideString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        isEscaped = true;
                        continue;
                    }

                    if (c == '"')
                        insideString = false;

                    continue;
                }

                if (c == '"')
                {
                    insideString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;

                    if (depth == 0)
                        return cleaned.Substring(firstOpen, i - firstOpen + 1);
                }
            }

            // Buraya düþmek, JSON'un kapanmadan bittiði anlamýna gelir.
            looksTruncated = true;
            return cleaned.Substring(firstOpen);
        }

        /// <summary>
        /// Metindeki ilk dengeli JSON dizisini çýkarýr. Obje çýkarýmýyla ayný
        /// derinlik mantýðý - naif ilk '[' / son ']' aramasý kod içeren yüklerde
        /// yanlýþ sonuç veriyordu.
        /// </summary>
        public static string ExtractJsonArray(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            string cleaned = StripCodeFences(response);

            int firstOpen = cleaned.IndexOf('[');
            if (firstOpen < 0)
                return null;

            int depth = 0;
            bool insideString = false;
            bool isEscaped = false;

            for (int i = firstOpen; i < cleaned.Length; i++)
            {
                char c = cleaned[i];

                if (insideString)
                {
                    if (isEscaped) { isEscaped = false; continue; }
                    if (c == '\\') { isEscaped = true; continue; }
                    if (c == '"') insideString = false;
                    continue;
                }

                if (c == '"') { insideString = true; continue; }

                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return cleaned.Substring(firstOpen, i - firstOpen + 1);
                }
            }

            return null;
        }

        /// <summary>
        /// Markdown kod çitlerini metnin HERHANGÝ bir yerinden temizler.
        ///
        /// Eski sürüm yalnýzca metin '```' ile BAÞLIYORSA temizlik yapýyordu. Model
        /// sýk sýk önce bir cümle yazýp sonra blok açýyor ("Ýþte çaðrý:\n```json\n...")
        /// - o durumda çitler olduðu gibi kalýyordu.
        /// </summary>
        public static string StripCodeFences(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("```", StringComparison.Ordinal) < 0)
                return text;

            return text
                .Replace("```json", "")
                .Replace("```JSON", "")
                .Replace("```csharp", "")
                .Replace("```cs", "")
                .Replace("```", "")
                .Trim();
        }

        // =====================================
        // VALIDATION
        // =====================================

        public static bool IsValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                JObject.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // =====================================
        // REPAIR
        // =====================================

        /// <summary>
        /// Çýkar, onar, ayrýþtýr. Baþarýlýysa true.
        /// </summary>
        public static bool TryRepair(string response, out JObject result)
        {
            return TryRepair(response, out result, out _);
        }

        /// <summary>
        /// <paramref name="wasRepaired"/> true dönerse ham metin geçerli deðildi ve
        /// onarým gerekti. Çaðýran bunu loglayabilir - sýk tekrarlýyorsa model
        /// ayarlarýnda (num_predict, sýcaklýk) bir sorun var demektir.
        /// </summary>
        public static bool TryRepair(string response, out JObject result, out bool wasRepaired)
        {
            result = null;
            wasRepaired = false;

            string json = ExtractJson(response, out bool truncated);

            if (string.IsNullOrEmpty(json))
                return false;

            // 1. Ham hâli zaten geçerli mi?
            if (TryParse(json, out result))
                return true;

            // 2. Onarým denemesi.
            wasRepaired = true;
            string repaired = Repair(json, truncated);

            if (!string.IsNullOrEmpty(repaired) && TryParse(repaired, out result))
                return true;

            result = null;
            return false;
        }

        private static bool TryParse(string json, out JObject result)
        {
            result = null;

            try
            {
                // DateParseHandling.None: "05:00" gibi saat benzeri metinleri
                // Newtonsoft tarihe çevirmeye çalýþýyor ve deðeri bozuyor. Bir saat
                // arayüzünde bu doðrudan yanlýþ ekran metni demek.
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    result = JObject.Load(reader);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Geriye dönük uyumlu imza.
        /// </summary>
        public static string Repair(string response)
        {
            string json = ExtractJson(response, out bool truncated);
            return string.IsNullOrEmpty(json) ? null : Repair(json, truncated);
        }

        /// <summary>
        /// LLM çýktýlarýnda gerçekten görülen bozukluklarý sýrayla düzeltir.
        /// Sýra önemlidir: önce karakter düzeyi, sonra sözdizimi, en son yapý.
        /// </summary>
        public static string Repair(string json, bool truncated)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            string work = json;

            work = NormalizeSmartQuotes(work);
            work = NormalizePythonLiterals(work);
            work = RemoveTrailingCommas(work);

            if (truncated)
            {
                work = CloseUnbalanced(work);
            }
            else
            {
                // Kesilmemiþ görünse bile dengesiz kalmýþ olabilir (model bir
                // parantezi atlamýþ olabilir).
                work = CloseUnbalanced(work);
            }

            return work;
        }

        /// <summary>
        /// Akýllý týrnaklarý ASCII'ye çevirir. Model Türkçe yazarken sýk sýk tipografik
        /// týrnak üretiyor ve JSON ayrýþtýrýcýsý bunlarý tanýmýyor.
        /// </summary>
        private static string NormalizeSmartQuotes(string json)
        {
            return json
                .Replace('\u201C', '"')   // "
                .Replace('\u201D', '"')   // "
                .Replace('\u201E', '"')   // „
                .Replace('\u2018', '\'')  // '
                .Replace('\u2019', '\''); // '
        }

        /// <summary>
        /// Python/Ollama alýþkanlýðý: True/False/None. JSON'da true/false/null olmalý.
        /// Yalnýzca string DIÞINDA deðiþtirilir - bir metin içindeki "None" bozulmamalý.
        /// </summary>
        private static string NormalizePythonLiterals(string json)
        {
            var sb = new StringBuilder(json.Length);
            bool insideString = false;
            bool isEscaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (insideString)
                {
                    sb.Append(c);

                    if (isEscaped) { isEscaped = false; continue; }
                    if (c == '\\') { isEscaped = true; continue; }
                    if (c == '"') insideString = false;

                    continue;
                }

                if (c == '"')
                {
                    insideString = true;
                    sb.Append(c);
                    continue;
                }

                if (TryReplaceLiteral(json, i, "True", "true", sb, ref i)) continue;
                if (TryReplaceLiteral(json, i, "False", "false", sb, ref i)) continue;
                if (TryReplaceLiteral(json, i, "None", "null", sb, ref i)) continue;

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static bool TryReplaceLiteral(
            string json, int index, string from, string to, StringBuilder sb, ref int i)
        {
            if (index + from.Length > json.Length)
                return false;

            if (string.CompareOrdinal(json, index, from, 0, from.Length) != 0)
                return false;

            // Kelime sýnýrý kontrolü: "TrueValue" gibi bir anahtarý bozmamak için.
            int after = index + from.Length;
            if (after < json.Length && (char.IsLetterOrDigit(json[after]) || json[after] == '_'))
                return false;

            sb.Append(to);
            i = after - 1;
            return true;
        }

        /// <summary>
        /// Sondaki fazla virgülleri siler: {"a":1,}  ->  {"a":1}
        /// String içindeki virgüllere dokunmaz.
        /// </summary>
        private static string RemoveTrailingCommas(string json)
        {
            var sb = new StringBuilder(json.Length);
            bool insideString = false;
            bool isEscaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (insideString)
                {
                    sb.Append(c);

                    if (isEscaped) { isEscaped = false; continue; }
                    if (c == '\\') { isEscaped = true; continue; }
                    if (c == '"') insideString = false;

                    continue;
                }

                if (c == '"')
                {
                    insideString = true;
                    sb.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    // Sonraki anlamlý karakter kapanýþ ise virgülü at.
                    int j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;

                    if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                        continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Açýk kalmýþ string ve parantezleri kapatýr.
        ///
        /// Bu, kesilmiþ cevaplar için tek kurtarma yolu. Model
        /// {"type":"create_ui_panel","params":{"name":"Sideb
        /// diye kesildiyse, buradan
        /// {"type":"create_ui_panel","params":{"name":"Sideb"}}
        /// çýkýyor. Ýsim eksik kalýyor ama çaðrý ayrýþtýrýlabilir hâle geliyor ve
        /// ajan bunu bir HATA olarak modele bildirebiliyor - "görev bitti" sanmak
        /// yerine.
        /// </summary>
        private static string CloseUnbalanced(string json)
        {
            var stack = new System.Collections.Generic.Stack<char>();
            bool insideString = false;
            bool isEscaped = false;

            foreach (char c in json)
            {
                if (insideString)
                {
                    if (isEscaped) { isEscaped = false; continue; }
                    if (c == '\\') { isEscaped = true; continue; }
                    if (c == '"') insideString = false;
                    continue;
                }

                if (c == '"') { insideString = true; continue; }

                if (c == '{' || c == '[') stack.Push(c);
                else if (c == '}' || c == ']')
                {
                    if (stack.Count > 0) stack.Pop();
                }
            }

            var sb = new StringBuilder(json.TrimEnd());

            // Yarým kalmýþ kaçýþ karakteri kapatmayý bozar.
            if (isEscaped && sb.Length > 0)
                sb.Length--;

            if (insideString)
                sb.Append('"');

            // Deðer beklenen yerde kesilmiþse ("name": ) geçerli bir deðer koy.
            string trimmed = sb.ToString().TrimEnd();
            if (trimmed.EndsWith(":", StringComparison.Ordinal))
                sb.Append("null");
            else if (trimmed.EndsWith(",", StringComparison.Ordinal))
                sb.Length = sb.ToString().TrimEnd().Length - 1;

            int closings = 0;
            while (stack.Count > 0 && closings < MaxClosings)
            {
                char open = stack.Pop();
                sb.Append(open == '{' ? '}' : ']');
                closings++;
            }

            return sb.ToString();
        }
    }
}
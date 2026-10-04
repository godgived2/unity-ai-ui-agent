using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Client
{
    /// <summary>
    /// Ollama'nın yerel HTTP API'siyle konuşan ince istemci katmanı.
    ///
    /// SORUMLULUK SINIRI: bu sınıf ne sorulacağına karar vermez - prompt'u alır,
    /// gönderir, yanıtı temizleyip döndürür. Karar verme işi OllamaToolAgent'ın.
    ///
    /// ---------------------------------------------------------------------------
    /// BU SÜRÜMDE DÜZELTİLENLER
    ///
    /// A. TOKEN TAHMİNİ DİĞER KATMANLA ÇELİŞİYORDU.
    ///    Burada sabit "4 karakter = 1 token" kullanılıyordu; PromptBuilder ise
    ///    ASCII ve ASCII olmayan karakterleri AYRI oranlarla sayıyor. Türkçe ağırlıklı
    ///    bir promptta iki katman farklı sayı söylüyordu: PromptBuilder "sığdı" derken
    ///    bu sınıf "sınıra yakın" diye uyarıyor ya da tersi oluyordu.
    ///    Artık ikisi aynı yöntemi kullanıyor.
    ///
    /// B. num_predict VARSAYILANLARI HİÇBİR YERLE UYUŞMUYORDU.
    ///    Burada 2048, OllamaToolAgent'ta TOOL_STEP_NUM_PREDICT = 1024,
    ///    PromptBuilder'ın ayırdığı pay 1280. Üç sayı üç farklı yerde.
    ///    Varsayılan 1024'e çekildi; agent zaten değerini açıkça geçiyor, ama
    ///    varsayılanın da doğru olması, yeni bir çağrı noktası eklendiğinde sessiz
    ///    bir uyumsuzluk doğmasını engelliyor.
    ///
    /// C. İPTAL TOKEN'I İŞE YARAMIYORDU.
    ///    SendAsync 'cancellationToken' alıyordu ama yalnızca çağrının BAŞINDA bir kez
    ///    bakıyor, HttpClient'a hiç geçirmiyordu. İstek başladıktan sonra iptal etmek
    ///    imkânsızdı - 10 dakikalık bir isteği durdurmanın yolu yoktu.
    ///    Token artık PostAsync'e kadar taşınıyor.
    ///
    /// D. ZAMAN AŞIMI YETERSİZ KALDI.
    ///    num_ctx 32768'e çıkarıldıktan sonra 14B bir modelin bir kısmı CPU'ya taşıyor
    ///    ve tek bir adım 10 dakikayı aşabiliyor. 20 dakikaya çıkarıldı.
    ///
    /// E. NUM_CTX YORUMU KENDİYLE ÇELİŞİYORDU.
    ///    "Yükseltmek yerine MAX_MEMORY_MESSAGES'i düşük tutmak daha güvenli" diyordu,
    ///    oysa değer zaten yükseltilmişti.
    ///
    /// F. İKİ AYRI TAŞMA UYARISI ÇIKIYORDU.
    ///    PromptBuilder zaten bloklarını bütçeye göre düşürüyor ve ayrıntılı rapor
    ///    veriyor. Buradaki ikinci uyarı aynı adımda tekrar basılıyordu ve üstelik
    ///    yanlış tahmine dayanıyordu. Artık yalnızca GERÇEK sınır aşıldığında ve
    ///    PromptBuilder'a atıf yaparak uyarıyor.
    /// </summary>
    public class OllamaClient : IDisposable
    {
        /// <summary>
        /// Varsayılan uç nokta.
        ///
        /// KRİTİK - 'localhost' DEĞİL, '127.0.0.1':
        /// Windows'ta 'localhost' önce IPv6'ya (::1) çözülüyor. Ollama ise varsayılan
        /// kurulumda yalnızca IPv4 üzerinde, 127.0.0.1:11434 adresinde dinliyor.
        /// Sonuç: .NET IPv6'ya bağlanmayı deniyor, bağlantı reddediliyor ve
        /// "An error occurred while sending the request" hatası alınıyor - Ollama
        /// sapasağlam çalışırken.
        ///
        /// Gerçek testte tam olarak bu yaşandı: 'ollama serve' çalışıyordu, tarayıcıdan
        /// erişiliyordu, ama Unity bağlanamıyordu. 127.0.0.1 yazınca çözüm anlık oldu.
        /// Ollama'yı ağdaki başka bir makinede çalıştırıyorsanız burayı o makinenin IP
        /// adresiyle değiştirin.
        /// </summary>
        public const string DefaultUrl = "http://127.0.0.1:11434/api/generate";

        private readonly string _url;
        private readonly string _model;
        private readonly HttpClient _client;
        private bool _disposed;

        /// <summary>
        /// Log durumunu dinamik olarak değiştirebilmek veya Agent'tan yönetebilmek için.
        /// </summary>
        public bool EnableVerboseLogging { get; set; }

        // 0'dan 1'e çıkarıldı: uzun oturumlarda (30+ dakika süren UI görevleri) Ollama
        // bağlantısının koptuğunu gerçek testte gördük ("OLLAMA CONNECTION ERROR").
        // Tek bir yeniden deneme, saatlerce süren bir işin tek bir ağ hıçkırığı yüzünden
        // çöpe gitmesini engelliyor. Daha fazlası riskli: agent katmanı da kendi
        // yeniden denemelerini yapıyor, üst üste binerse yanıt süreleri katlanıyor.
        private const int MAX_RETRY = 1;

        /// <summary>
        /// Bağlam penceresi.
        ///
        /// ============ PromptBuilder.ContextWindow İLE AYNI OLMALI ============
        /// İki dosya birbirinden habersiz ve eşleşmediklerinde HİÇBİR DERLEME HATASI
        /// ÇIKMAZ. PromptBuilder'ın bütçe hesabı oradaki sayıya dayanıyor: buradaki
        /// değer daha KÜÇÜKSE prompt sessizce kesilir ve model çekirdek kuralları
        /// kaybeder; daha BÜYÜKSE boş yere kural kısılır.
        ///
        /// 20480'den 32768'e çıkarıldı. Gerekçe: zorunlu kural blokları + araç şeması
        /// + büyüyen geçmiş, 20480'de uzun görevlerin ortasında sürekli taşıyordu ve
        /// PromptBuilder bütün tasarım rehberini feda etmesine rağmen yetmiyordu.
        ///
        /// BEDELİ: ek KV cache bellek istiyor. 8 GB VRAM'de 14B bir model zaten tam
        /// sığmıyor, bu yüzden daha fazla katman CPU'ya taşıyor ve her adım yavaşlıyor.
        /// Bilinçli bir takas - doğru sonuç, daha uzun süre.
        ///
        /// 'ollama ps' ile modelin GPU'da mı CPU'da mı çalıştığını görebilirsiniz.
        /// ====================================================================
        /// </summary>
        private const int NUM_CTX = 32768;

        /// <summary>
        /// Varsayılan yanıt uzunluğu.
        ///
        /// OllamaToolAgent.TOOL_STEP_NUM_PREDICT ile AYNI: agent her araç adımını
        /// açıkça o değerle çağırıyor, ama varsayılanın da eşleşmesi gerekiyor -
        /// yoksa yeni bir çağrı noktası eklendiğinde PromptBuilder'ın ayırdığı paydan
        /// fazlasını üretmeye çalışır ve cevap ortasında kesilir.
        /// </summary>
        private const int DEFAULT_NUM_PREDICT = 1024;

        /// <summary>
        /// İlk çağrı modeli belleğe yüklediği için uzun sürebilir; 14B bir model yavaş
        /// diskte daha da uzun sürer. Sonraki çağrılar çok daha hızlı.
        ///
        /// 10 dakikadan 20'ye çıkarıldı: num_ctx 32768 ile modelin bir kısmı CPU'ya
        /// taştığında tek bir adım 10 dakikayı aşabiliyor ve görev, aslında çalışan bir
        /// modelin ortasında zaman aşımına uğruyordu.
        /// </summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(20);

        public OllamaClient(
            string model = "qwen3:14b",
            string url = DefaultUrl,
            bool enableVerboseLogging = false)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "qwen3:14b" : model.Trim();
            _url = NormalizeUrl(url);
            EnableVerboseLogging = enableVerboseLogging;

            _client = new HttpClient
            {
                Timeout = RequestTimeout
            };
        }

        /// <summary>
        /// Kullanıcının girdiği adresi kabul edilebilir bir uç noktaya çevirir.
        ///
        /// Ayar alanına elle adres yazılırken sürekli aynı üç hata yapılıyor ve üçü de
        /// aynı anlamsız bağlantı hatasını veriyor. Burada sessizce düzeltmek, kullanıcıyı
        /// saatlerce yanlış yerde hata aramaktan kurtarıyor:
        ///   1. Sadece "localhost:11434" yazılıyor  -> şema eksik
        ///   2. Sadece kök adres yazılıyor           -> /api/generate yolu eksik
        ///   3. Sonda eğik çizgi kalıyor             -> "//api/generate" oluşuyor
        /// Ayrıca 'localhost' -> '127.0.0.1' çevriliyor (bkz. DefaultUrl açıklaması).
        /// </summary>
        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return DefaultUrl;

            string normalized = url.Trim();

            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "http://" + normalized;
            }

            normalized = normalized
                .Replace("://localhost", "://127.0.0.1")
                .Replace("://LOCALHOST", "://127.0.0.1");

            normalized = normalized.TrimEnd('/');

            if (!normalized.Contains("/api/"))
            {
                normalized += "/api/generate";
            }

            return normalized;
        }

        // =====================================
        // CURRENT MODEL INFO
        // =====================================

        public string CurrentModel => _model;

        /// <summary>Kullanılan uç nokta - hata ayıklama ve arayüzde gösterim için.</summary>
        public string Endpoint => _url;

        /// <summary>
        /// Yapılandırılmış bağlam penceresi. PromptBuilder'ın kendi sabitiyle
        /// karşılaştırmak isteyen bir doğrulama katmanı için açık tutuluyor.
        /// </summary>
        public static int ContextWindow => NUM_CTX;

        // =====================================
        // TOKEN TAHMİNİ
        // =====================================

        /// <summary>
        /// Token sayısını tahmin eder - PromptBuilder.EstimateTokens ile AYNI yöntem.
        ///
        /// ============ NEDEN SABİT ORAN YETMİYOR ============
        /// Burada eskiden "4 karakter = 1 token" vardı. Bu oran İngilizce kural metni
        /// için makul ama prompt saf İngilizce değil: kullanıcı isteği Türkçe, GameObject
        /// adları Türkçe, araç sonuçları Türkçe metin taşıyor. Türkçeye özgü karakterler
        /// (ş, ğ, ı, ö, ü, ç) çoğu BPE tokenizer'ında ayrı token oluyor.
        ///
        /// Sabit 4 kullanmak taşmayı OLDUĞUNDAN KÜÇÜK gösteriyordu - ve PromptBuilder
        /// daha doğru bir tahminle çalıştığı için iki katman aynı prompt hakkında farklı
        /// şeyler söylüyordu. İki uyarı, iki farklı sayı, hangisinin doğru olduğu belirsiz.
        ///
        /// İki dosyada aynı formül duruyor. Biri değişirse diğeri de değişmeli -
        /// ortak bir yardımcıya taşımak daha temiz olurdu ama bu iki assembly birbirini
        /// tanımıyor.
        /// ==================================================
        /// </summary>
        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int ascii = 0;
            int wide = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < 128)
                    ascii++;
                else
                    wide++;
            }

            return (ascii / 4) + (wide / 2) + 1;
        }

        // =====================================
        // HEALTH CHECK
        // =====================================

        /// <summary>
        /// Ollama'ya ulaşılabiliyor mu, kısa bir istekle kontrol eder.
        ///
        /// Uzun bir görev başlatmadan önce çağrılırsa kullanıcı 20 dakika bekleyip
        /// zaman aşımı almak yerine saniyeler içinde net bir hata görür.
        /// </summary>
        public async Task<bool> IsReachableAsync()
        {
            string root = _url;

            int apiIndex = root.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (apiIndex > 0)
            {
                root = root.Substring(0, apiIndex);
            }

            try
            {
                using (var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    HttpResponseMessage response = await probe.GetAsync(root);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaClient] Ollama is not reachable at {root}: {Describe(ex)}");
                return false;
            }
        }

        // =====================================
        // ASK (PLAIN TEXT / FINAL SUMMARY OVERLOAD)
        // =====================================

        /// <summary>
        /// Bu overload SADECE düz metin cevaplar için: görev bitiminde
        /// PromptBuilder.BuildFinalResponsePrompt ile çağrılan final özet.
        ///
        /// DÜZELTİLDİ: eskiden numPredict:512 ve forceJson:true idi. İkisi de yanlıştı -
        /// final cevap JSON değil düz metin olmalı (forceJson:true onu JSON'a zorluyor ve
        /// CleanResponse ilk '{' ile son '}' arasını kesiyordu), ve 512 token bir özet
        /// için fazla kısıtlıydı. Sıcaklık da biraz yükseltildi: doğal dil özet için
        /// 0.1 fazla mekanik.
        /// </summary>
        public async Task<string> Ask(string prompt)
        {
            return await Ask(prompt, numPredict: DEFAULT_NUM_PREDICT, forceJson: false, temperature: 0.3f);
        }

        // =====================================
        // ASK (CONFIGURABLE OVERLOAD)
        // =====================================

        public async Task<string> Ask(
            string prompt,
            int numPredict = DEFAULT_NUM_PREDICT,
            bool forceJson = true,
            float temperature = 0.1f,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(prompt))
                return "";

            int attempt = 0;

            while (attempt <= MAX_RETRY)
            {
                attempt++;

                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.Log("[OllamaClient] Request cancelled before it was sent.");
                    return "";
                }

                try
                {
                    var optionsObj = new
                    {
                        temperature = temperature,
                        top_p = 0.9,
                        top_k = 40,
                        num_predict = numPredict,
                        num_ctx = NUM_CTX,
                        repeat_penalty = 1.05
                    };

                    object bodyObj;

                    if (forceJson)
                    {
                        bodyObj = new
                        {
                            model = _model,
                            prompt = prompt,
                            stream = false,
                            think = false,
                            format = "json", // Sadece tool çağrılarında JSON zorla
                            options = optionsObj
                        };
                    }
                    else
                    {
                        bodyObj = new
                        {
                            model = _model,
                            prompt = prompt,
                            stream = false,
                            think = false,
                            options = optionsObj
                        };
                    }

                    string json = JsonConvert.SerializeObject(bodyObj);

                    int estimatedTokens = EstimateTokens(prompt);

                    // DÜZELTME: 'Debug.isDebugBuild' koşulu KALDIRILDI.
                    //
                    // O bayrak Editor'de her zaman true olduğu için zararsız görünüyordu,
                    // ama anlamı yanlıştı: EnableVerboseLogging kullanıcının açıkça
                    // verdiği bir tercih, build tipiyle koşullanmamalı.
                    if (EnableVerboseLogging)
                    {
                        Debug.Log("========== OLLAMA REQUEST ==========");
                        Debug.Log("MODEL : " + _model);
                        Debug.Log("URL : " + _url);
                        Debug.Log("JSON MODE : " + forceJson);
                        Debug.Log("MAX TOKENS : " + numPredict);
                        Debug.Log("PROMPT : " + prompt.Length + " chars (~" + estimatedTokens + " tokens) of " + NUM_CTX);
                        Debug.Log(prompt);
                    }

                    // ============ SON SAVUNMA HATTI ============
                    // PromptBuilder promptu zaten bütçeye göre kuruyor ve taşarsa
                    // ayrıntılı rapor veriyor. Buradaki kontrol yalnızca o katmanın
                    // atladığı ya da devre dışı kaldığı durumlar için: cevap payı da
                    // hesaba katıldığında gerçekten sınırı aşıyor muyuz.
                    //
                    // Eşik artık "%85" gibi keyfi bir oran değil, GERÇEK hesap:
                    // prompt + üretilecek cevap, pencereye sığmalı.
                    if (estimatedTokens + numPredict > NUM_CTX)
                    {
                        Debug.LogError(
                            $"[OllamaClient] Prompt (~{estimatedTokens} tokens) plus the reply budget ({numPredict}) exceeds num_ctx ({NUM_CTX}). " +
                            "Ollama silently discards the BEGINNING of an oversized prompt, so the model will not see its core rules and will start looping.\n" +
                            "PromptBuilder is supposed to prevent this - check that PromptBuilder.ContextWindow is also set to " + NUM_CTX + ".");
                    }

                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        if (EnableVerboseLogging)
                        {
                            Debug.Log("[OllamaClient] Posting to " + _url);
                        }

                        // İPTAL ARTIK GERÇEKTEN ÇALIŞIYOR: token PostAsync'e kadar
                        // taşınıyor. Eskiden yalnızca çağrının başında bir kez
                        // bakılıyordu, yani başlamış bir isteği durdurmanın yolu yoktu.
                        HttpResponseMessage response = await _client.PostAsync(_url, content, cancellationToken);
                        string result = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            LogHttpFailure(response, result, attempt);

                            if (attempt <= MAX_RETRY)
                            {
                                await Task.Delay(1000, cancellationToken);
                                continue;
                            }

                            return "";
                        }

                        string answer = ExtractAnswer(result, forceJson, out bool parseFailed);

                        if (parseFailed)
                        {
                            // Ayrıştırma hatası genelde kalıcıdır (bozuk yanıt biçimi,
                            // yanlış model adı), tekrar denemenin faydası yok.
                            return "";
                        }

                        answer = CleanResponse(answer, forceJson);

                        if (string.IsNullOrWhiteSpace(answer))
                        {
                            Debug.LogWarning($"[OllamaClient] Ollama returned an empty response (Attempt {attempt}/{MAX_RETRY + 1}).");

                            // Boş yanıt genelde geçici bir sorundur (model yükleniyor,
                            // bağlam sınırı, anlık kaynak sıkışması). Yeniden deneme
                            // hakkı varsa boş dönmek yerine tekrar deniyoruz.
                            if (attempt <= MAX_RETRY)
                            {
                                await Task.Delay(1000, cancellationToken);
                                continue;
                            }
                        }
                        else if (EnableVerboseLogging)
                        {
                            Debug.Log("========== OLLAMA RESPONSE ==========");
                            Debug.Log("MODEL : " + _model);
                            Debug.Log(answer);
                            Debug.Log("=====================================");
                        }

                        return answer.Trim();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Kullanıcı iptal etti - bu bir hata değil, tekrar denenmemeli.
                    // NOT: bu blok TaskCanceledException'dan ÖNCE gelmeli, çünkü o
                    // ondan türüyor ve zaman aşımı ile iptal aynı tipi paylaşıyor.
                    Debug.Log("[OllamaClient] Request cancelled.");
                    return "";
                }
                catch (TaskCanceledException)
                {
                    Debug.LogError(
                        $"[OllamaClient] Request timed out after {RequestTimeout.TotalMinutes} minutes (Attempt {attempt}/{MAX_RETRY + 1}).\n" +
                        "The model may still be loading into memory, or the machine may be under heavy load.\n" +
                        $"Run 'ollama ps' to see whether '{_model}' is on GPU or CPU - with num_ctx {NUM_CTX} a 14B model often spills to CPU, and CPU inference is many times slower.");
                }
                catch (Exception ex)
                {
                    LogConnectionFailure(ex, attempt);
                }

                if (attempt <= MAX_RETRY)
                {
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return "";
                    }
                }
            }

            return "";
        }

        // =====================================
        // RESPONSE PARSING
        // =====================================

        /// <summary>
        /// Ollama yanıt gövdesinden asıl metni çıkarır.
        ///
        /// Ollama bir hata durumunda 200 OK ile birlikte {"error": "..."} da dönebiliyor.
        /// Önceki sürüm bunu yakalamıyordu: 'response' alanı boş olduğu için sonuç boş
        /// string oluyor ve kullanıcı "model neden hiçbir şey demiyor" diye kalıyordu.
        /// Artık hata mesajı Console'a yazılıyor - en sık görüleni "model not found",
        /// yani ayar alanındaki model adının yanlış yazılması.
        /// </summary>
        private string ExtractAnswer(string rawBody, bool forceJson, out bool parseFailed)
        {
            parseFailed = false;

            if (string.IsNullOrWhiteSpace(rawBody))
                return "";

            try
            {
                JObject data = JObject.Parse(rawBody);

                string error = data["error"]?.ToString();
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.LogError(
                        $"[OllamaClient] Ollama reported an error: {error}\n" +
                        $"Model requested: '{_model}'. Run 'ollama list' and make sure the name matches exactly.");

                    parseFailed = true;
                    return "";
                }

                return data["response"]?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                if (forceJson)
                {
                    Debug.LogError($"[OllamaClient] Invalid Ollama JSON response: {ex.Message}\nBody: {Truncate(rawBody, 400)}");
                    parseFailed = true;
                    return "";
                }

                // Düz metin modunda gövdenin JSON olmaması beklenebilir bir durum.
                return rawBody;
            }
        }

        // =====================================
        // ERROR REPORTING
        // =====================================

        private void LogHttpFailure(HttpResponseMessage response, string body, int attempt)
        {
            string hint = "";

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                hint = $"\nA 404 usually means the model '{_model}' is not installed. Run: ollama pull {_model}";
            }

            Debug.LogError(
                $"[OllamaClient] HTTP {(int)response.StatusCode} {response.StatusCode} (Attempt {attempt}/{MAX_RETRY + 1})\n" +
                $"URL: {_url}{hint}\n{Truncate(body, 500)}");
        }

        /// <summary>
        /// Bağlantı hatasını KULLANILABİLİR biçimde raporlar.
        ///
        /// Önceki sürüm yalnızca ex.Message yazıyordu ve HttpRequestException'ın mesajı
        /// her zaman aynı, hiçbir şey söylemeyen cümle: "An error occurred while sending
        /// the request." Asıl sebep InnerException'da duruyor ("No connection could be
        /// made because the target machine actively refused it" gibi) ve o olmadan
        /// kullanıcı nereye bakacağını bilmiyor.
        /// </summary>
        private void LogConnectionFailure(Exception ex, int attempt)
        {
            Debug.LogError(
                $"[OllamaClient] Connection failed (Attempt {attempt}/{MAX_RETRY + 1}): {Describe(ex)}\n" +
                $"URL: {_url}\n" +
                "Checklist:\n" +
                "  1. Is Ollama running? Open http://127.0.0.1:11434 in a browser - it should say 'Ollama is running'.\n" +
                "  2. If not, run 'ollama serve' in a terminal and leave it open.\n" +
                "  3. Use 127.0.0.1 instead of localhost - on Windows 'localhost' resolves to IPv6 first, " +
                "but Ollama listens on IPv4 only, so the connection is refused.");
        }

        /// <summary>
        /// İstisna zincirinin tamamını okunabilir tek satıra indirir. En anlamlı bilgi
        /// genelde en içteki istisnadadır.
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex == null)
                return "unknown error";

            var sb = new StringBuilder(ex.Message);
            Exception inner = ex.InnerException;
            int depth = 0;

            while (inner != null && depth < 4)
            {
                sb.Append(" -> ").Append(inner.Message);
                inner = inner.InnerException;
                depth++;
            }

            return sb.ToString();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        // =====================================
        // SEND ASYNC (AGENT COMPATIBILITY)
        // =====================================

        /// <summary>
        /// Geriye dönük uyumluluk sarmalayıcısı.
        ///
        /// DÜZELTİLDİ: 'cancellationToken' yalnızca çağrının başında bir kez kontrol
        /// ediliyor, sonra HİÇ kullanılmıyordu - yani başlamış bir isteği iptal etmenin
        /// yolu yoktu ve 20 dakikalık bir çağrıyı durdurmak imkânsızdı. Token artık
        /// Ask'a geçiriliyor ve oradan PostAsync'e kadar taşınıyor.
        ///
        /// numPredict de 2048'den DEFAULT_NUM_PREDICT'e çekildi: eskisi
        /// OllamaToolAgent.TOOL_STEP_NUM_PREDICT ile de, PromptBuilder'ın ayırdığı
        /// payla da uyuşmuyordu.
        /// </summary>
        public async Task<string> SendAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return await Ask(
                prompt,
                numPredict: DEFAULT_NUM_PREDICT,
                forceJson: true,
                temperature: 0.1f,
                cancellationToken: cancellationToken);
        }

        // =====================================
        // RESPONSE CLEANER
        // =====================================

        private string CleanResponse(string response, bool isJsonFormat)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "";

            response = response.Trim();

            // Code block temizliği.
            //
            // DÜZELTME: eskiden yalnızca metin '```' ile BAŞLIYORSA temizlik yapılıyordu.
            // Model sık sık kısa bir açıklama yazıp ardından kod bloğu açıyor
            // ("Here is the call:\n```json\n{...}```") - o durumda backtick'ler olduğu
            // gibi kalıyordu. JSON modunda süslü parantez kesme bunu kurtarıyordu ama
            // düz metin modunda backtick'ler kullanıcıya kadar gidiyordu.
            if (response.Contains("```"))
            {
                response = response
                    .Replace("```json", "")
                    .Replace("```JSON", "")
                    .Replace("```csharp", "")
                    .Replace("```cs", "")
                    .Replace("```", "");
            }

            // JSON modunda ilk '{' ile son '}' arasını al, değilse metni olduğu gibi
            // koru. Bu yüzden final özet çağrısının forceJson:false olması kritik -
            // aksi halde düz metin cevap parantez arayıp kesiliyor.
            if (isJsonFormat)
            {
                int start = response.IndexOf('{');
                int end = response.LastIndexOf('}');

                if (start >= 0 && end >= 0 && end > start)
                {
                    response = response.Substring(start, end - start + 1);
                }
            }

            return response.Trim();
        }

        // =====================================
        // DISPOSE PATTERN
        // =====================================

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaClient));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
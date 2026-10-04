using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AI.Context;
using UnityEngine;

namespace MCPForUnity.Editor.AI
{
    /// <summary>
    /// Builds the system context sent alongside the MCP tool schema and the user's
    /// request. Rules are dynamically derived from the provided MCP Tool Schema.
    ///
    /// This class only orchestrates: the actual rule text lives in
    /// PromptRulesCore, PromptRulesUnity and PromptRulesUI.
    ///
    /// TARGET UI SYSTEM: UGUI (Canvas-based), built with the UI macros and
    /// manage_gameobject + manage_components. NOT UI Toolkit / manage_ui.
    ///
    ///
    /// ============ ANA TASARIM: BÜTÇE FARKINDALIKLI PROMPT ============
    ///
    /// Bir önceki sürüm taşmayı ÖLÇÜYOR ama ENGELLEMİYORDU.
    ///
    /// Gözlemlenen: "[PromptBuilder] System prompt is 76571 chars (~19142 tokens)
    /// in the planning phase ... about 710 tokens too many." Uyarı basıldı ve prompt
    /// AYNEN gönderildi. Ollama başını kesti. Model kimliğini, döngü kurallarını ve
    /// isteğin ilk maddelerini hiç görmedi. Sonuç zinciri:
    ///
    ///     çekirdek kurallar görülmedi
    ///       -> create_ui_row'a control='slider' verildi
    ///       -> create_ui_slider "already exists" döndü
    ///       -> aynı adım 4 kez tekrarlandı
    ///       -> adım bütçesi bitti, Ses paneli hiç kurulmadı
    ///
    /// Yani ekrandaki bozuk sonuç semptomdu; hastalık buradaydı.
    ///
    /// ÇÖZÜM:
    ///   1. Her kural bloğu AYRI derleniyor ve token maliyeti ölçülüyor.
    ///   2. Bloklar ZORUNLU ve OPSİYONEL diye ayrılıyor; opsiyonellerin bir
    ///      önceliği var.
    ///   3. Toplam bütçeyi aşarsa en düşük öncelikli opsiyonel bloklar sırayla
    ///      DÜŞÜRÜLÜYOR - sığana kadar.
    ///   4. Sonuç her zaman pencereye sığıyor. Ollama'nın kesecek bir şeyi kalmıyor.
    ///
    /// ZORUNLU bloklar asla düşmez: kimlik, ajan döngüsü, durma koşulu, JSON biçimi,
    /// şema otoritesi, Canvas/anchor/çakışma kuralları, macro envanteri, hata
    /// yönetimi, final UI özeti. Bunlardan biri eksik olduğunda model ya JSON
    /// biçimini kaybediyor ya da hiç durmuyor.
    ///
    /// DÜŞÜRÜLEBİLİR bloklar tasarım rehberidir: felsefe, öz-inceleme, kalite,
    /// mimari, görsel referans. Bunlar sonucu güzelleştirir; yokluğu sonucu
    /// BOZMAZ. Taşan bir prompt ise sonucu HER ZAMAN bozar. Takas açık.
    ///
    ///
    /// ============ DİĞER DÜZELTMELER ============
    ///
    /// A. TOKEN TAHMİNİ TÜRKÇEDE YANLIŞTI.
    ///    Sabit "4 karakter = 1 token" oranı İngilizce kural metni için doğru, ama
    ///    Türkçe istek metni ve Türkçe hata mesajları için ciddi şekilde iyimser.
    ///    'ş', 'ğ', 'ü' gibi karakterler çoğu tokenizer'da tek başına token oluyor.
    ///    Artık ASCII ve ASCII olmayan karakterler AYRI oranlarla sayılıyor.
    ///
    /// B. GEÇMİŞ BÜTÇEYE HİÇ DAHİL DEĞİLDİ.
    ///    BuildContext geçmişin ne kadar yer kapladığını bilmiyordu. Artık kuyruk
    ///    blokları ÖNCE kuruluyor, gerçek maliyeti rezerve ediliyor, kural bütçesi
    ///    kalandan hesaplanıyor.
    ///
    /// C. GEÇMİŞİN KENDİSİ SINIRSIZ BÜYÜYEBİLİYORDU.
    ///    Artık kendi payını (bütçenin ~%35'i) aşarsa BAŞI VE SONU korunarak
    ///    kırpılıyor. Düz baştan kırpma modelin ilk adımlarda ürettiği nesneleri
    ///    unutmasına ve onları TEKRAR kurmasına yol açardı.
    ///
    /// D. "ZATEN VAR" SONUCU BAŞARISIZLIK SAYILIYORDU. Referans bloğuna açık kural
    ///    eklendi: bir araç "zaten var" diyorsa o adım TAMAMDIR.
    ///
    /// E. İKİ GİRİŞ NOKTASINDAN BİRİ BÜTÇEYİ HİÇ DENETLEMİYORDU. İkisi tek koda
    ///    indirildi.
    ///
    /// F. BİR KURAL BLOĞU EXCEPTION ATARSA TÜM PROMPT ÖLÜYORDU. Artık o blok
    ///    atlanıp loglanıyor.
    ///
    /// G. SAHNE ÖZETİ SINIRSIZDI. 2000 karakterle sınırlandı.
    ///
    ///
    /// ============ BU SÜRÜMDE TEMİZLENENLER ============
    ///
    /// H. CS0162 "UNREACHABLE CODE DETECTED".
    ///    VerboseBudgetLogging bir 'const bool = false' idi. Derleyici sabitleri
    ///    derleme anında katlıyor, yani 'if (VerboseBudgetLogging)' ifadesi
    ///    'if (false)' hâline geliyor ve gövdesi ERİŞİLEMEZ koda dönüşüyordu -
    ///    her derlemede bir uyarı. 'static readonly' ile katlama olmuyor, uyarı
    ///    kayboluyor ve bayrak hâlâ tek satırda açılabiliyor.
    ///
    /// I. ÖLÜ KOD SİLİNDİ. Altı [Obsolete] prompt adaptörü (BuildPlanningPrompt,
    ///    BuildVerificationPrompt, iki BuildExecutionDecisionPrompt, iki
    ///    BuildOutputAnalysisPrompt) bağlı oldukları PlanningAgent/ExecutionAgent
    ///    zinciriyle birlikte işlevsiz kalmıştı. MAX_AGENT_STEPS sabiti de
    ///    kullanılmıyordu - gerçek sınır OllamaToolAgent.MAX_AGENT_STEPS.
    ///
    /// J. PromptSection.Order ALANI KULLANILMIYORDU. Yazılıyor ama hiçbir yerde
    ///    okunmuyordu; bloklar zaten listeye ekleme sırasında duruyor.
    ///
    ///
    /// KORUNAN TASARIM: iki fazlı yükleme. Tasarım rehberi yalnızca PLAN aşamasında
    /// gönderiliyor; model planını <conversation_history> içinde taşıdığı için 15.
    /// adımda "ekranı nasıl kurgularsın" anlatmanın karşılığı yok. Aşama bilgisi
    /// formattedHistory'den türetiliyor - OllamaToolAgent'a dokunulmuyor.
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>
        /// Bağlam penceresi - OllamaClient'ın num_ctx değeriyle AYNI olmalı.
        ///
        /// ============ DİKKAT: ELLE EŞLEŞTİRİLİYOR ============
        /// İdeali bu değeri OllamaClient'tan okumak olurdu, ama orada public bir
        /// alan yok. Yani iki dosya birbirinden habersiz.
        ///
        /// Biri değişip diğeri unutulursa HİÇBİR DERLEME HATASI ÇIKMAZ. Eskiden
        /// sonuç "uyarı eşiği yanlış olur" idi; ARTIK DAHA CİDDİ: bütçe hesabı bu
        /// sayıya dayanıyor, yani buradaki değer gerçeğin üstündeyse prompt yine
        /// taşar ve yine sessizce kesilir.
        ///
        /// OllamaClient'ta num_ctx'i değiştirirseniz BURAYI DA değiştirin.
        ///
        /// 32768'e çıkarıldı: 20480 ile zorunlu bloklar + şema + büyüyen geçmiş
        /// uzun görevlerde sürekli taşıyordu. 14b modelde bu ek KV cache VRAM'i
        /// zorluyor ve her adım yavaşlıyor - bilinçli bir takas.
        /// =====================================================
        /// </summary>
        private const int ContextWindow = 32768;

        /// <summary>
        /// Modelin cevabı için ayrılan token payı.
        ///
        /// ============ NEDEN 2048 DEĞİL ============
        /// GERÇEK ÖLÇÜM: uygulama aşamasında prompt 199, sonra 215, sonra 77 token
        /// taştı. Küçük sayılar ama sonucu büyük: Ollama promptun BAŞINI kesiyor,
        /// model "bunu zaten yaptın" bilgisini kaybediyor ve aynı satırı tekrar
        /// kurmaya çalışıyor - konsolda "Duplicate macro call blocked: create_ui_row"
        /// iki kez, ardından görev erken bitti.
        ///
        /// Sebep bu sabitin GERÇEKTEN AYRILAN yerden büyük olmasıydı:
        /// OllamaToolAgent her araç adımını TOOL_STEP_NUM_PREDICT = 1024 ile
        /// çağırıyor. Yani model bir adımda en fazla 1024 token üretebiliyor, ama
        /// burada 2048 rezerve ediliyordu - 1024 token boş yere kurallardan
        /// kısılıyordu.
        ///
        /// 1280 = 1024 (gerçek üst sınır) + 256 (tokenizer şaşma payı).
        ///
        /// DİKKAT: OllamaToolAgent'ta TOOL_STEP_NUM_PREDICT değişirse BURASI DA
        /// değişmeli. Buradaki değer oradakinden KÜÇÜK olursa cevap ortasında
        /// kesilir; çok BÜYÜK olursa kurallar boş yere kısılır.
        /// ==========================================
        /// </summary>
        private const int ReplyTokenBudget = 1280;

        /// <summary>
        /// Emniyet payı.
        ///
        /// Token tahmini bir TAHMİN. Gerçek prompt_eval_count her zaman biraz farklı
        /// çıkar; tokenizer JSON şemasını, Türkçe özel adları ve uzun sembol
        /// dizilerini beklenenden pahalı sayabilir. Tam sınıra dayanmak, tahminin
        /// %3 şaşması hâlinde promptun yine kesilmesi demektir.
        ///
        /// 512 token, ~18.000 tokenlik bir bütçede yaklaşık %2,8'lik bir emniyet
        /// payı - şaşma toleransını karşılar, gözle görülür bir kayba yol açmaz.
        /// </summary>
        private const int SafetyMarginTokens = 512;

        /// <summary>
        /// Geçmişin kullanabileceği bütçe payı.
        ///
        /// Geçmiş ne kadar büyürse kurallara o kadar az yer kalır. %35 sınırı,
        /// uzun bir oturumda bile çekirdek kuralların ayakta kalmasını garanti
        /// ediyor: geçmiş sonsuza kadar büyüyüp kuralları dışarı itemiyor.
        /// </summary>
        private const double HistoryBudgetShare = 0.35;

        /// <summary>
        /// Geçmiş bu uzunluğu aşınca PLAN AŞAMASI bitmiş sayılıyor.
        ///
        /// İlk adımda geçmiş yok. İkinci adımda bir çağrı + bir sonuç var (~400-800
        /// karakter). 2500 karakter kabaca ÜÇ tamamlanmış adım demek - yani model
        /// planını kurmuş ve uygulamaya geçmiş oluyor.
        ///
        /// Daha düşük bir eşik rehberi model planını bitirmeden keserdi; daha
        /// yüksek olan kazancı geciktirirdi.
        /// </summary>
        private const int PlanningPhaseHistoryLimit = 2500;

        /// <summary>
        /// Sahne özetinin üst sınırı. Kalabalık bir sahnede GetSummary() tek başına
        /// binlerce karakter dönebiliyor ve bu blok ZORUNLU olduğu için düşürülemez
        /// - o yüzden kaynağında sınırlanıyor.
        /// </summary>
        private const int MaxSceneSummaryChars = 2000;

        /// <summary>
        /// true yapılırsa her adımda token kullanımı loglanır. Normalde kapalı:
        /// taşma yokken konsolu kirletmenin anlamı yok.
        ///
        /// ============ NEDEN const DEĞİL, static readonly ============
        /// 'const bool = false' derleme anında katlanıyor: 'if (VerboseBudgetLogging)'
        /// ifadesi 'if (false)' hâline geliyor ve gövdesi ERİŞİLEMEZ kod sayılıyor.
        /// Derleyici her derlemede CS0162 "Unreachable code detected" uyarısı basıyordu.
        ///
        /// 'static readonly' ile değer çalışma anında okunuyor, katlama olmuyor,
        /// uyarı kayboluyor - ve bayrağı açmak için yine tek satır değiştirmek yeterli.
        /// ============================================================
        /// </summary>
        private static readonly bool VerboseBudgetLogging = false;

        /// <summary>
        /// Bu öncelik değerine sahip bloklar ASLA düşürülmez.
        /// </summary>
        private const int Mandatory = int.MaxValue;

        // =====================================================
        // BLOK ÖNCELİKLERİ
        //
        // Küçük sayı = önce düşer.
        //
        // Sıralama ilkesi: bir bloğun yokluğu sonucu ne kadar BOZUYORSA önceliği o
        // kadar yüksek. Görsel referans yoksa ekran daha sade olur; görev
        // ayrıştırma yoksa model 20 adımlık işi planlayamaz. İkisi aynı ağırlıkta
        // değil.
        // =====================================================

        private const int PrioVisualReference = 10;
        private const int PrioSelfReflection = 15;
        private const int PrioPhilosophy = 20;
        private const int PrioQualityGuidance = 25;
        private const int PrioThreeD = 28;
        private const int PrioCapability = 30;
        private const int PrioSelfReview = 35;
        private const int PrioIntent = 40;
        private const int PrioComplexScene = 45;
        private const int PrioIcons = 48;
        private const int PrioUIFrame = 50;
        private const int PrioArchitecture = 55;
        private const int PrioModernDesign = 60;
        private const int PrioDecomposition = 65;
        private const int PrioUIPlanning = 70;
        private const int PrioScript = 75;
        private const int PrioWiring = 80;


        public enum AgentMode
        {
            Strict,
            Creative
        }

        // =====================================================
        // PROMPT SCOPE
        // =====================================================

        /// <summary>
        /// İsteğin hangi kural ailelerine ihtiyaç duyduğunu belirler.
        ///
        /// TEK KAYNAK: "bu bir UI görevi mi" sorusunun cevabı yalnızca burada
        /// veriliyor. Eskiden ayrı bir IsUiTask metodu daha vardı ve farklı bir
        /// mantık kullanıyordu - ikisi çelişebiliyordu.
        /// </summary>
        private struct PromptScope
        {
            public bool WantsUi;
            public bool WantsScript;
            public bool WantsWiring;
            public bool WantsIcons;
            public bool Wants3D;
            public bool IsExplicitStepList;

            /// <summary>
            /// İstek SEKMELİ bir ekran mı - yani aynı dikdörtgeni paylaşan,
            /// biri görünür diğerleri gizli paneller mi istiyor.
            ///
            /// WantsWiring'den AYRI tutuluyor: "bağla" kelimesi geçen her istek sekme
            /// yapısı istemiyor, ve sekmeli her istek aynı komutta bağlama da
            /// istemiyor. Kullanıcı bugün tam olarak böyle çalışıyor: önce ekranı kurup
            /// sonra ayrı bir komutla bağlıyor.
            /// </summary>
            public bool WantsTabs;

            public static PromptScope From(string prompt, string toolSchema)
            {
                var scope = new PromptScope();

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    // İstek okunamıyorsa hiçbir şey atlanmıyor - güvenli taraf.
                    // Bütçe yönetimi artık fazlalığı kendi hallediyor, yani cömert
                    // davranmanın maliyeti eskisi gibi "taşma" değil, sadece düşük
                    // öncelikli blokların düşmesi.
                    scope.WantsUi = true;
                    scope.WantsScript = true;
                    scope.WantsWiring = true;
                    scope.WantsTabs = true;
                    scope.Wants3D = true;
                    return scope;
                }

                scope.WantsUi = ContainsAny(prompt,
                    "ui", "arayüz", "arayuz", "panel", "buton", "button", "menü", "menu",
                    "ekran", "screen", "sekme", "tab", "toggle", "label", "etiket",
                    "kart", "card", "canvas", "hud", "dashboard", "form", "login",
                    "giriş", "giris", "sidebar", "satır", "row", "create_ui_",
                    "interface", "input", "gösterge", "gosterge", "telemetri",
                    "telemetry", "konsol", "console", "istasyon", "ayarlar", "settings",
                    "kaydırıcı", "kaydirici", "slider");

                scope.WantsScript = ContainsAny(prompt,
                    "script", "kod", "code", ".cs", "monobehaviour", "write_script",
                    "davranış", "davranis", "sınıf", "sinif", "class");

                // Tek başına "tab" KULLANILMIYOR: kelime sınırı denetimi ek toleransı
                // yüzünden "table" ve "tablet" içinde de eşleşiyordu. Onun yerine
                // sekmeli bir istekte neredeyse her zaman geçen, belirsiz olmayan
                // ifadeler aranıyor.
                scope.WantsTabs = ContainsAny(prompt,
                    "tabs", "tabbed", "tab panel", "tab bar",
                    "sekme", "sekmeli", "switchable", "tabcontroller");

                scope.WantsWiring = ContainsAny(prompt,
                    "bağla", "bagla", "wire", "tabcontroller", "sekme", "tab",
                    "onclick", "tıklan", "tiklan", "click", "geçiş", "gecis",
                    "execute_code", "attach");

                // 2.545 karakterlik bir doku üretme kod listesi içeriyor. Eskiden
                // HİÇ çağrılmıyordu (yani ikon hedefi fiilen yoktu); şimdi çağrılıyor
                // ama yalnızca gerçekten istendiğinde.
                scope.WantsIcons = ContainsAny(prompt,
                    "ikon", "icon", "simge", "logo", "sprite", "amblem", "görsel", "gorsel");

                scope.Wants3D = ContainsAny(prompt,
                    "küp", "kup", "cube", "sphere", "küre", "kure", "capsule",
                    "cylinder", "silindir", "primitive", "mesh", "material", "malzeme",
                    "shader", "ışık", "isik", "light", "kamera", "camera", "prefab",
                    "rigidbody", "collider", "3d", "model");

                // Kullanıcı adım adım, macro adlarıyla yazdıysa modele "nasıl ekran
                // tasarlanır" anlatmanın anlamı yok - ne yapacağı zaten söylenmiş.
                scope.IsExplicitStepList = LooksLikeExplicitStepList(prompt);

                // Şema execute_code sunmuyorsa script/wiring/ikon kuralları boşa gider.
                if (!string.IsNullOrWhiteSpace(toolSchema) &&
                    toolSchema.IndexOf("execute_code", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    scope.WantsWiring = false;
                    scope.WantsIcons = false;
                }

                // Hiçbir aile eşleşmediyse istek muğlak demektir; UI kurallarını
                // yüklüyoruz çünkü bu projede isteklerin ezici çoğunluğu UI.
                if (!scope.WantsUi && !scope.WantsScript && !scope.WantsWiring && !scope.Wants3D)
                {
                    scope.WantsUi = true;
                    scope.Wants3D = true;
                }

                return scope;
            }

            /// <summary>
            /// Anahtar kelime eşleşmesi - KELİME SINIRINA saygılı.
            ///
            /// Düz alt dize araması gereksiz kural blokları yüklüyordu:
            ///   "BAĞLANTI AKTİF" içinde "bağla" bulunuyordu   -> +5.420 karakter
            ///   "kamera görüntüsü" içinde "kamera" bulunuyordu -> +2.000 karakter
            ///
            /// Artık eşleşme yalnızca kelime sınırında sayılıyor: aranan parça ya
            /// kelimenin tamamı ya da başlangıcı olmalı, ve arkasından en fazla bir
            /// Türkçe ek kadar harf gelebilir. "buton" -> "butonlar" eşleşir,
            /// "bağla" -> "bağlantı" eşleşmez.
            /// </summary>
            private static bool ContainsAny(string text, params string[] needles)
            {
                if (string.IsNullOrEmpty(text))
                    return false;

                foreach (string n in needles)
                {
                    if (string.IsNullOrEmpty(n))
                        continue;

                    int from = 0;

                    while (from < text.Length)
                    {
                        int index = text.IndexOf(n, from, StringComparison.OrdinalIgnoreCase);

                        if (index < 0)
                            break;

                        // Öncesi kelime sınırı olmalı: eşleşme bir kelimenin
                        // ORTASINDA başlıyorsa saymıyoruz.
                        bool startsWord = index == 0 || !IsWordChar(text[index - 1]);

                        if (startsWord)
                        {
                            // Sonrasında en fazla bir Türkçe ek kadar harf olabilir.
                            int after = index + n.Length;
                            int extra = 0;

                            while (after + extra < text.Length && IsWordChar(text[after + extra]))
                            {
                                extra++;
                            }

                            if (extra <= 4)
                                return true;
                        }

                        from = index + 1;
                    }
                }

                return false;
            }

            private static bool IsWordChar(char c)
            {
                return char.IsLetterOrDigit(c) || c == '_';
            }

            /// <summary>
            /// İstek numaralı bir adım listesi mi. Beş veya daha fazla "1." / "2." gibi
            /// satır başı ya da üçten fazla açık macro adı varsa öyle sayıyoruz.
            /// </summary>
            private static bool LooksLikeExplicitStepList(string prompt)
            {
                int numbered = Regex.Matches(prompt, @"(?m)^\s*\d{1,2}[\.\)]\s").Count;
                if (numbered >= 5)
                    return true;

                int macroMentions = Regex.Matches(prompt, @"create_ui_[a-z_]+", RegexOptions.IgnoreCase).Count;
                return macroMentions >= 3;
            }
        }

        // =====================================================
        // TOKEN TAHMİNİ
        // =====================================================

        /// <summary>
        /// Token sayısını tahmin eder.
        ///
        /// ============ NEDEN SABİT BİR ORAN YETMİYOR ============
        /// Eski kod "4 karakter = 1 token" diyordu. Bu oran İngilizce kural metni
        /// için makul, ama prompt saf İngilizce DEĞİL:
        ///
        ///   - kullanıcı isteği Türkçe
        ///   - GameObject adları Türkçe ("ParlaklikKaydiriciHandleArea")
        ///   - araç sonuçları ve hata mesajları Türkçe metin taşıyor
        ///
        /// Türkçeye özgü karakterler (ş, ğ, ı, ö, ü, ç) çoğu BPE tokenizer'ında
        /// ayrı token oluyor, yani o bölgede oran 4'e değil 2'ye yakın. Sabit 4
        /// kullanmak taşmayı OLDUĞUNDAN KÜÇÜK gösteriyordu - ve bütçe hesabı artık
        /// bu sayıya dayandığı için iyimser tahmin doğrudan kesilen prompt demek.
        ///
        /// Çözüm basit ve ucuz: ASCII ve ASCII olmayan karakterler ayrı sayılıyor.
        /// Tam değil - tam olması için gerçek tokenizer gerekir - ama eskisi gibi
        /// SİSTEMATİK OLARAK YANLI değil.
        /// =======================================================
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

            // +1: her parça en az bir token üretir, ayrıca tam bölmede aşağı
            // yuvarlamanın biriktirdiği hatayı kapatır.
            return (ascii / 4) + (wide / 2) + 1;
        }

        // =====================================================
        // BLOK TOPLAMA
        // =====================================================

        /// <summary>
        /// Tek bir kural bloğu: adı, metni, maliyeti ve düşürülebilirliği.
        ///
        /// NOT: burada bir 'Order' alanı vardı ve hiçbir yerde OKUNMUYORDU - bloklar
        /// zaten listeye ekleme sırasında duruyor ve düşürme mantığı sırayı bozmuyor
        /// (bir blok ya tamamen kalıyor ya tamamen gidiyor). Ölü alan kaldırıldı.
        /// </summary>
        private struct PromptSection
        {
            public int Priority;
            public string Name;
            public string Text;
            public int Tokens;
        }

        /// <summary>
        /// Kural bloklarını tek tek derleyip maliyetleriyle birlikte biriktirir.
        ///
        /// Neden doğrudan tek StringBuilder'a yazmıyoruz: bir bloğun maliyetini
        /// bilmeden onu düşürüp düşüremeyeceğimize karar veremeyiz. Blokları ayrı
        /// derlemek, hangi 700 tokenın feda edileceğini SEÇEBİLMEMİZİ sağlıyor -
        /// bu seçim daha önce Ollama'nın eline bırakılmıştı ve Ollama her zaman
        /// EN BAŞTAKİLERİ, yani en kritik olanları atıyordu.
        /// </summary>
        private sealed class SectionSet
        {
            private readonly List<PromptSection> _sections = new List<PromptSection>(64);
            private readonly StringBuilder _scratch = new StringBuilder(8192);

            public List<PromptSection> Sections
            {
                get { return _sections; }
            }

            public void Add(int priority, string name, Action<StringBuilder> writer)
            {
                if (writer == null)
                    return;

                _scratch.Length = 0;

                try
                {
                    writer(_scratch);
                }
                catch (Exception ex)
                {
                    // Bir kural bloğunun içindeki hata TÜM promptu öldürmemeli.
                    // Eskiden BuildContext tek try-catch'siz zincir olduğu için
                    // tek bir NullReference bütün ajanı durduruyordu.
                    Debug.LogError(
                        "[PromptBuilder] Rule block '" + name + "' threw and was skipped: " + ex.Message);
                    return;
                }

                if (_scratch.Length == 0)
                    return;

                string text = _scratch.ToString();

                _sections.Add(new PromptSection
                {
                    Priority = priority,
                    Name = name,
                    Text = text,
                    Tokens = EstimateTokens(text)
                });
            }
        }

        /// <summary>
        /// Blokları bütçeye sığdırıp birleştirir.
        ///
        /// Düşürme sırası: önce en düşük öncelik. Aynı öncelikte birden fazla blok
        /// varsa önce BÜYÜK olan düşer - böylece hedefe daha az blok feda ederek
        /// ulaşılır.
        /// </summary>
        private static string Assemble(SectionSet set, int availableTokens, bool isPlanningPhase)
        {
            List<PromptSection> all = set.Sections;

            int totalTokens = 0;
            for (int i = 0; i < all.Count; i++)
                totalTokens += all[i].Tokens;

            var dropped = new List<string>();
            var removed = new HashSet<int>();

            if (totalTokens > availableTokens)
            {
                var optional = new List<int>();

                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Priority != Mandatory)
                        optional.Add(i);
                }

                optional.Sort(delegate (int a, int b)
                {
                    int byPriority = all[a].Priority.CompareTo(all[b].Priority);
                    if (byPriority != 0)
                        return byPriority;

                    // Eşit öncelikte büyük olan önce gitsin.
                    return all[b].Tokens.CompareTo(all[a].Tokens);
                });

                for (int i = 0; i < optional.Count; i++)
                {
                    if (totalTokens <= availableTokens)
                        break;

                    int idx = optional[i];
                    removed.Add(idx);
                    totalTokens -= all[idx].Tokens;
                    dropped.Add(all[idx].Name + " (-" + all[idx].Tokens + "t)");
                }
            }

            var sb = new StringBuilder(Math.Max(4096, totalTokens * 4));

            for (int i = 0; i < all.Count; i++)
            {
                if (removed.Contains(i))
                    continue;

                sb.Append(all[i].Text);
            }

            ReportBudget(totalTokens, availableTokens, dropped, isPlanningPhase);

            return sb.ToString();
        }

        /// <summary>
        /// Bütçe durumunu raporlar.
        ///
        /// ÜÇ DURUM, ÜÇ FARKLI MESAJ - eski tek uyarı her adımda aynı şeyi basıp
        /// gürültüye dönüşmüştü:
        ///
        ///   1. Hâlâ taşıyor  -> LogError. Bütün opsiyoneller zaten düştü, demek ki
        ///      fazlalık ZORUNLU bloklarda veya araç şemasında. Bu gerçek bir
        ///      yapılandırma sorunu ve müdahale gerektiriyor.
        ///   2. Sığdı ama blok düştü -> LogWarning + HANGİ bloklar. Sonuç çalışır
        ///      ama tasarım kalitesi düşebilir; hangi rehberin gitmediğini bilmek
        ///      hatayı teşhis etmeyi kolaylaştırıyor.
        ///   3. Sorunsuz -> sessizlik (VerboseBudgetLogging kapalıysa).
        /// </summary>
        private static void ReportBudget(
            int usedTokens, int availableTokens, List<string> dropped, bool isPlanningPhase)
        {
            string phase = isPlanningPhase ? "planning" : "execution";

            if (usedTokens > availableTokens)
            {
                int overflow = usedTokens - availableTokens;

                Debug.LogError(
                    "[PromptBuilder] Prompt STILL overflows in the " + phase + " phase: ~" + usedTokens +
                    " tokens against ~" + availableTokens + " usable (num_ctx " + ContextWindow +
                    ", reply " + ReplyTokenBudget + ", safety " + SafetyMarginTokens + ") - " + overflow +
                    " tokens too many.\n" +
                    "Every optional rule block has ALREADY been dropped, so the excess is in the mandatory " +
                    "blocks, the tool schema or the user request itself.\n" +
                    "Ollama silently discards the BEGINNING of an oversized prompt: the model will not see its " +
                    "identity, the execution loop or the first items of the request.\n" +
                    "Fix one of: split the request into two smaller commands, shorten the request, expose fewer " +
                    "MCP tools, or raise num_ctx in OllamaClient AND in PromptBuilder.ContextWindow.");
                return;
            }

            if (dropped.Count > 0)
            {
                Debug.LogWarning(
                    "[PromptBuilder] Prompt fits (~" + usedTokens + "/" + availableTokens + " tokens, " + phase +
                    " phase), but " + dropped.Count + " optional rule block(s) were dropped to make room: " +
                    string.Join(", ", dropped.ToArray()) + ".\n" +
                    "The result stays functionally correct; only design guidance was sacrificed. " +
                    "If quality suffers, shorten the request instead of raising num_ctx.");
                return;
            }

            if (VerboseBudgetLogging)
            {
                Debug.Log("[PromptBuilder] " + phase + " prompt ~" + usedTokens + "/" + availableTokens + " tokens.");
            }
        }

        // =====================================================
        // BUILD METHODS
        // =====================================================

        public static string BuildFromSchema(
            string toolSchema,
            string userPrompt,
            AgentMode mode = AgentMode.Strict)
        {
            // TEK KOD YOLU.
            //
            // Eskiden bu metot kendi kuyruğunu (<user_request>) kuruyor ve bütçe
            // denetimini HİÇ çağırmıyordu - yani iki giriş noktasından biri
            // korumasızdı. Artık ikisi de aynı yerden geçiyor.
            return BuildFromSchemaWithHistory(toolSchema, string.Empty, userPrompt, mode);
        }

        public static string BuildFromSchema(
            string toolSchema,
            string userPrompt,
            string mode)
        {
            return BuildFromSchema(toolSchema, userPrompt, ParseAgentMode(mode));
        }

        /// <summary>
        /// Conversation History ve aktif kullanıcı isteğini içeren sistem prompt'unu
        /// oluşturur. OllamaToolAgent bu metodu her adımda çağırır.
        ///
        /// KRİTİK TASARIM NOKTASI: currentTask her adımda DEĞİŞMEDEN tekrar sunuluyor.
        /// Eski versiyonda bu blok "Current Task / Request to fulfill right now"
        /// başlığı taşıyordu ve model 30. adımda bile onu TAZE bir emir gibi okuyup
        /// çalışmaya devam ediyordu - UI çoktan bitmiş olsa bile.
        ///
        /// KURULUM SIRASI ÖNEMLİ: kuyruk blokları (geçmiş + istek) ÖNCE kuruluyor.
        /// Sebebi, kural bütçesinin onlardan ARTAN yere göre hesaplanması. Eski kod
        /// kuralları önce bitirip geçmişi sonradan eklediği için geçmişin maliyetini
        /// hiç hesaba katmıyordu.
        ///
        /// İMZA DEĞİŞMEDİ. Aşama bilgisi formattedHistory'den türetiliyor, yani
        /// OllamaToolAgent'a dokunmaya gerek yok.
        /// </summary>
        public static string BuildFromSchemaWithHistory(
            string toolSchema,
            string formattedHistory,
            string currentTask = "",
            AgentMode mode = AgentMode.Strict)
        {
            bool hasHistory = !string.IsNullOrWhiteSpace(formattedHistory);

            // ============ AŞAMA TESPİTİ ============
            // Geçmiş yoksa ya da çok kısaysa model hâlâ planını kuruyor: tasarım
            // rehberi gerekli. Geçmiş büyüdüyse plan kurulmuş ve
            // <conversation_history> içinde taşınıyor; rehberi tekrar göndermek
            // ~16.000 karakter israf.
            bool isPlanningPhase =
                !hasHistory || formattedHistory.Length < PlanningPhaseHistoryLimit;

            int usableTokens = ContextWindow - ReplyTokenBudget - SafetyMarginTokens;

            string history = hasHistory
                ? FitHistory(formattedHistory, (int)(usableTokens * HistoryBudgetShare))
                : string.Empty;

            // Kuyruk önce kuruluyor ki gerçek maliyeti ölçülebilsin.
            StringBuilder tail = BuildTail(history, currentTask, hasHistory);

            int reservedTokens = EstimateTokens(tail.ToString());
            int ruleBudget = usableTokens - reservedTokens;

            // Kuyruk tek başına bütçeyi yerse bile çekirdek kurallar gitmesin: en az
            // bir taban pay bırakılıyor. Bu durumda ReportBudget zaten LogError
            // basacak, yani sorun görünür kalıyor.
            if (ruleBudget < 1024)
                ruleBudget = 1024;

            StringBuilder sb = new StringBuilder(65536);

            sb.AppendLine(BuildContext(toolSchema, mode, currentTask, isPlanningPhase, ruleBudget));
            sb.Append(tail);

            return sb.ToString();
        }

        /// <summary>
        /// Geçmiş ve istek bloklarını kurar.
        /// </summary>
        private static StringBuilder BuildTail(string history, string currentTask, bool hasHistory)
        {
            var sb = new StringBuilder(8192);

            if (hasHistory && !string.IsNullOrEmpty(history))
            {
                sb.AppendLine("<conversation_history>");
                sb.AppendLine(history);
                sb.AppendLine("</conversation_history>\n");
            }

            if (string.IsNullOrWhiteSpace(currentTask))
                return sb;

            if (hasHistory)
            {
                sb.AppendLine("<original_request_reference>");
                sb.AppendLine("This is the request you have been working on. It is shown for REFERENCE ONLY - it is NOT a fresh instruction to start over:");
                sb.AppendLine(currentTask);
                sb.AppendLine();
                sb.AppendLine("HOW TO USE THIS:");
                sb.AppendLine("1. Read <conversation_history> above and identify which parts of this request are ALREADY DONE.");
                sb.AppendLine("2. Find the FIRST part that is NOT yet done.");
                sb.AppendLine("3. If such a part exists, emit ONE tool call for it.");
                sb.AppendLine("4. If EVERY part is done, finish with exactly one call: {\"type\":\"task_complete\",\"params\":{\"summary\":\"...\"}} - the summary in the user's language. This is the correct, expected way to end - it is not giving up. Never end by repeating a call that already succeeded.");
                sb.AppendLine();
                sb.AppendLine("If the request is a NUMBERED LIST, work through it strictly in order and never skip ahead. Step N+1 only starts once step N has succeeded.");
                sb.AppendLine("Never re-create a GameObject whose creation already succeeded in the history - creating it twice leaves a duplicate orphan in the scene.");
                sb.AppendLine("Never re-configure an element you already configured. Adjusting the same object again with slightly different values accomplishes nothing and wastes the task.");
                sb.AppendLine();

                // ============ DÖRT ARDIŞIK TEKRARIN DOĞRUDAN KARŞILIĞI ============
                // Konsol kaydı: Step 16 ve 17'de create_ui_slider "already had ..."
                // döndü, model bunu BAŞARISIZLIK okuyup aynı çağrıyı tekrarladı,
                // ajan "Stuck re-creating elements that already exist (4 consecutive
                // attempts)" diyerek durdu ve Ses paneli hiç kurulmadı.
                //
                // "Zaten var" bir hata değil, İSTENEN DURUM. Bunu açıkça söylemek
                // gerekiyor çünkü araç cevabı bazen success:false taşıyor ve model
                // haklı olarak onu hata sanıyor.
                sb.AppendLine("IF A TOOL REPORTS THAT SOMETHING ALREADY EXISTS, THAT STEP IS DONE.");
                sb.AppendLine("An 'already exists' / 'already existed and was reused' result means the desired state is reached. Treat it as SUCCESS, move to the NEXT step, and never repeat that call.");
                sb.AppendLine("Repeating a call that failed the same way twice will never start working on the third try. If two attempts at one step both fail, skip that step, continue with the rest, and mention it in your final summary.");
                sb.AppendLine();
                sb.AppendLine("Finishing when the work is complete is REQUIRED behaviour, not optional. Continuing past that point damages the result.");
                sb.AppendLine("</original_request_reference>\n");
            }
            else
            {
                // İlk adım: henüz geçmiş yok, bu gerçekten taze bir emir.
                sb.AppendLine("<current_user_intent>");
                sb.AppendLine("Current Task / Request to fulfill right now:");
                sb.AppendLine(currentTask);
                sb.AppendLine();
                sb.AppendLine("If this is a numbered list, start with step 1 and follow the order exactly.");
                sb.AppendLine("Plan the whole thing first, then emit the FIRST tool call.");
                sb.AppendLine("</current_user_intent>\n");
            }

            return sb;
        }

        /// <summary>
        /// Geçmişi kendi token payına sığdırır.
        ///
        /// ============ NEDEN BAŞ VE KUYRUK KORUNUYOR ============
        /// En kolay yol geçmişin başını atmaktır. Burada YANLIŞ olur: geçmişin başı
        /// "hangi nesneler kuruldu" bilgisini taşır. O kısım gidince model daha önce
        /// ürettiği GameObject'leri unutur ve TEKRAR kurmaya çalışır - "Stuck
        /// re-creating elements that already exist" hatasının tam mekanizması.
        ///
        /// Onun yerine ORTA kısım atılıyor:
        ///   - BAŞ (~%25): ilk adımlar, yani iskeletin kurulduğu yer
        ///   - KUYRUK (~%75): son adımlar, yani şu an nerede kalındığı
        ///
        /// Kuyruğa daha çok pay veriliyor çünkü "sırada ne var" kararı için en
        /// yakın adımlar belirleyici.
        ///
        /// Atlanan kısmın yerine açık bir not bırakılıyor: orada üretilenler ZATEN
        /// VAR, emin değilsen sahneyi incele - tekrar kurma.
        /// =======================================================
        /// </summary>
        private static string FitHistory(string history, int maxTokens)
        {
            if (string.IsNullOrEmpty(history) || maxTokens <= 0)
                return history;

            int estimated = EstimateTokens(history);

            if (estimated <= maxTokens)
                return history;

            // Metnin KENDİ yoğunluğundan karakter bütçesi türetiliyor. Sabit bir
            // oran kullanmak Türkçe ağırlıklı bir geçmişte fazla cömert olurdu.
            int maxChars = (int)((long)history.Length * maxTokens / Math.Max(1, estimated));

            if (maxChars >= history.Length)
                return history;

            const string Marker =
                "\n\n[... MIDDLE OF THE HISTORY OMITTED to fit the context window. " +
                "Everything created during the omitted steps ALREADY EXISTS in the scene - do NOT create it again. " +
                "If you are unsure whether an object exists, inspect the scene instead of re-creating it ...]\n\n";

            // Çok dar bir bütçede baş+kuyruk bölmenin anlamı kalmıyor; en taze
            // kısım her zaman kuyruktur.
            if (maxChars < Marker.Length + 600)
            {
                int take = Math.Min(history.Length, Math.Max(200, maxChars));
                return history.Substring(history.Length - take);
            }

            int room = maxChars - Marker.Length;
            int headChars = room / 4;
            int tailChars = room - headChars;

            return history.Substring(0, headChars) + Marker + history.Substring(history.Length - tailChars);
        }

        /// <summary>
        /// Maksimum adım sayısına ulaşıldığında ya da görev erken bitirildiğinde nihai
        /// özet cevabını istemek için kullanılır.
        ///
        /// İKİ DÜZELTME KORUNDU:
        /// 1. DİL. Bu prompt bütün kural bloklarını ATLIYOR - bağımsız bir istek.
        ///    "Kullanıcının dilinde cevap ver" talimatı yalnızca UI kurallarında
        ///    olduğu için, görev erken bittiğinde kullanıcı Türkçe yazmasına rağmen
        ///    İngilizce özet alıyordu.
        /// 2. BOYUT. lastResult ham gömülüyordu ve bir tool sonucu binlerce
        ///    karakterlik JSON olabiliyor.
        /// </summary>
        public static string BuildFinalResponsePrompt(string userPrompt, string reason, string lastResult)
        {
            StringBuilder sb = new StringBuilder(2048);

            sb.AppendLine("[SYSTEM INSTRUCTION]");
            sb.AppendLine("The tool execution loop ended. Reason: " + reason);
            sb.AppendLine();
            sb.AppendLine("Original user request:");
            sb.AppendLine(Truncate(userPrompt, 1200));
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(lastResult))
            {
                sb.AppendLine("Last tool execution result:");
                sb.AppendLine(Truncate(lastResult, 600));
                sb.AppendLine();
            }

            sb.AppendLine("Write a short final summary for the user. State plainly:");
            sb.AppendLine("- what was actually built (the top-level structure, not every object)");
            sb.AppendLine("- what is still missing or was only approximated");
            sb.AppendLine("- any file created under Assets/UI/Generated/ or Assets/UI/Sprites/");
            sb.AppendLine();
            sb.AppendLine("Do not claim success that did not happen. If the work stopped early, say so and say why in one sentence.");
            sb.AppendLine("Plain prose only - no JSON, no tool calls, no markdown fences.");
            sb.AppendLine("WRITE IT IN THE SAME LANGUAGE THE USER USED IN THEIR REQUEST ABOVE.");

            return sb.ToString();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "... [truncated]";
        }

        /// <summary>Legacy/Shortcut overload mapping to BuildFromSchema.</summary>
        public static string BuildToolPrompt(
            string toolSchema,
            string request,
            AgentMode mode = AgentMode.Strict)
        {
            return BuildFromSchema(toolSchema, request, mode);
        }

        private static AgentMode ParseAgentMode(string mode)
        {
            if (!string.IsNullOrWhiteSpace(mode) &&
                mode.Equals("creative", StringComparison.OrdinalIgnoreCase))
            {
                return AgentMode.Creative;
            }

            return AgentMode.Strict;
        }

        // =====================================================
        // CONTEXT ASSEMBLY
        // =====================================================

        /// <summary>
        /// Kural bloklarını kapsam ve aşamaya göre toplar, sonra bütçeye sığdırır.
        ///
        /// BLOK SIRASI KASITLI VE DEĞİŞTİRİLMEMELİ. Model çelişki durumunda EN SON
        /// okuduğuna uyuyor, o yüzden özet niteliğindeki bloklar en sonda duruyor.
        /// Düşürme mantığı sırayı BOZMUYOR: bir blok ya tamamen kalıyor ya tamamen
        /// gidiyor, kalanların göreli sırası aynı.
        /// </summary>
        private static string BuildContext(
            string toolSchema, AgentMode mode, string userPrompt, bool isPlanningPhase, int ruleBudget)
        {
            PromptScope scope = PromptScope.From(userPrompt, toolSchema);

            // ============ MOD KAPSAMDAN TÜRETİLİYOR ============
            // Eskiden ayrı bir IsUiTask metodu vardı ve ham alt dize araması
            // yapıyordu: IndexOf("UI") "build", "quick", "guide", "fluid" gibi
            // kelimelerin İÇİNDE eşleşiyor, yani "build a cube" Creative moda
            // geçiyordu. Daha kötüsü PromptScope ile ÇELİŞEBİLİYORDU.
            if (scope.WantsUi && !scope.IsExplicitStepList)
            {
                mode = AgentMode.Creative;
            }

            // Tasarım rehberi: hem plan aşamasında olmalı hem model kendi kurgusunu
            // yapıyor olmalı. Kullanıcı numaralı liste verdiyse zaten gerekmiyor.
            bool includeDesignGuidance = isPlanningPhase && !scope.IsExplicitStepList;

            string sceneInfo;

            try
            {
                SceneContext scene = SceneContextBuilder.Build();
                sceneInfo = Truncate(scene != null ? scene.GetSummary() : null, MaxSceneSummaryChars);

                if (string.IsNullOrEmpty(sceneInfo))
                    sceneInfo = "No active scene loaded.";
            }
            catch (Exception ex)
            {
                // Sahne okunamıyorsa prompt yine de kurulmalı - ajanı sahne
                // taramasındaki bir hata yüzünden durdurmanın anlamı yok.
                Debug.LogWarning("[PromptBuilder] Scene context unavailable: " + ex.Message);
                sceneInfo = "Scene context unavailable.";
            }

            var set = new SectionSet();

            // --- ÇEKİRDEK: her adımda gerekli. Bunlar KISIT, tavsiye değil. ---
            //
            // Modelin KİM olduğunu, döngünün nasıl işlediğini ve ne zaman duracağını
            // anlatıyorlar. Biri eksik olduğunda model ya JSON biçimini kaybediyor ya
            // da hiç durmuyor. ASLA DÜŞÜRÜLMEZ.
            set.Add(Mandatory, "identity", PromptRulesCore.AppendIdentity);
            set.Add(Mandatory, "agent_execution_loop", PromptRulesCore.AppendAgentExecutionLoop);
            set.Add(Mandatory, "goal_rules", PromptRulesCore.AppendGoalRules);
            set.Add(Mandatory, "completion_rules", PromptRulesCore.AppendCompletionRules);
            set.Add(Mandatory, "verification_rules", PromptRulesCore.AppendVerificationRules);
            set.Add(Mandatory, "placeholder_rules", PromptRulesCore.AppendPlaceholderRules);
            set.Add(Mandatory, "production_rules", PromptRulesCore.AppendProductionRules);
            set.Add(Mandatory, "reuse_rules", PromptRulesCore.AppendReuseRules);
            set.Add(Mandatory, "continuation_rules", PromptRulesCore.AppendContinuationRules);
            set.Add(Mandatory, "stop_rules", PromptRulesCore.AppendStopRules);

            // --- TASARIM REHBERİ: yalnızca PLAN AŞAMASINDA, ve düşürülebilir ---
            //
            // Model planını <conversation_history> içinde taşıyor; 15. adımda
            // "ekranı nasıl kurgularsın" anlatmanın karşılığı yok.
            if (includeDesignGuidance)
            {
                set.Add(PrioPhilosophy, "mcp_philosophy", PromptRulesCore.AppendMcpPhilosophy);
                set.Add(PrioIntent, "intent_classification", PromptRulesCore.AppendIntentClassification);
                set.Add(PrioSelfReview, "self_review", PromptRulesCore.AppendSelfReviewRules);
                set.Add(PrioArchitecture, "architecture", PromptRulesCore.AppendArchitectureRules);
                set.Add(PrioComplexScene, "complex_scene", PromptRulesCore.AppendComplexSceneRules);
                set.Add(PrioQualityGuidance, "professional_quality", PromptRulesCore.AppendProfessionalQualityRules);
                set.Add(PrioCapability, "capability_reasoning", PromptRulesCore.AppendCapabilityReasoning);
                set.Add(PrioDecomposition, "task_decomposition", PromptRulesCore.AppendTaskDecomposition);
            }

            // --- UNITY: araç seçimi ve JSON biçimi. Her adımda gerekli. ---
            set.Add(Mandatory, "tool_selection", PromptRulesUnity.AppendToolSelectionRules);
            set.Add(Mandatory, "tool_priority", PromptRulesUnity.AppendToolPriority);
            set.Add(Mandatory, "schema_authority", PromptRulesUnity.AppendSchemaAuthority);
            set.Add(Mandatory, "unity_api_prohibition", PromptRulesUnity.AppendUnityApiProhibition);
            set.Add(Mandatory, "scene", delegate (StringBuilder s) { PromptRulesUnity.AppendScene(s, sceneInfo); });
            set.Add(Mandatory, "schema_tools", delegate (StringBuilder s) { PromptRulesUnity.AppendSchemaTools(s, toolSchema); });
            set.Add(Mandatory, "format_rules", PromptRulesUnity.AppendFormatRules);
            set.Add(Mandatory, "validation_rules", PromptRulesUnity.AppendValidationRules);
            set.Add(Mandatory, "invalid_outputs", PromptRulesUnity.AppendInvalidOutputs);

            // Tool sonucunu yorumlama: PLAN aşamasında henüz sonuç YOK. İlk adımda
            // göndermek boşuna; uygulama aşamasında ise şart - "already exists"
            // gibi sonuçları doğru okumak buna bağlı.
            if (!isPlanningPhase)
            {
                set.Add(Mandatory, "tool_result_reasoning", PromptRulesUnity.AppendToolResultReasoning);
            }

            // --- UNITY: yalnızca 3D/sahne işlerinde ---
            //
            // Primitive ve malzeme kuralları saf bir UI görevinde ölü ağırlık. Kapsam
            // yanlış eşleşirse diye ayrıca düşürülebilir bırakıldı.
            if (scope.Wants3D)
            {
                set.Add(PrioThreeD, "primitive_rules", PromptRulesUnity.AppendPrimitiveRules);
                set.Add(PrioThreeD, "primitive_parameters", PromptRulesUnity.AppendPrimitiveParameterRules);
                set.Add(PrioThreeD, "material_capability", PromptRulesUnity.AppendMaterialCapabilityRules);
                set.Add(PrioThreeD, "vector_rules", delegate (StringBuilder s) { PromptRulesUnity.AppendVectorRulesIfRelevant(s, toolSchema); });
            }

            if (includeDesignGuidance)
            {
                set.Add(PrioUIFrame, "production_ui_frame", PromptRulesUnity.AppendProductionAndUIFrame);
            }

            // NOT: Burada elle yazılmış bir <ui_rules> bloğu vardı ve UI TOOLKIT
            // diyordu - PromptRulesUI'nin TAM TERSİ. Model her prompt'ta birbirini
            // iptal eden iki talimat görüyordu. UI kuralları artık TEK yerde.

            if (scope.WantsUi)
            {
                // --- UI'NİN FİZİĞİ: her adımda gerekli, ASLA DÜŞMEZ ---
                //
                // Hangi macro ne yapar, anchor nasıl çalışır, otomatik dikey akış ne
                // zaman devreye girer, Canvas neden zorunlu, kardeşler neden çakışamaz.
                // Model bunlar olmadan görünmeyen eleman üretiyor.
                //
                // SIRA: auto_layout_flow, sibling_overlap'tan ÖNCE. Model bant
                // aritmetiğini okumadan önce onu çoğu durumda hiç yapması gerekmediğini
                // bilmeli; aksi hâlde hazır tabloları görüp elle hesaba devam eder.
                set.Add(Mandatory, "manage_ui_hard_rules", PromptRulesUI.AppendManageUIHardRules);
                set.Add(Mandatory, "ui_generation_pipeline", PromptRulesUI.AppendUiGenerationPipeline);

                // ============ EKRAN BÖLGELERİ: ZORUNLU ============
                // GERÇEK ARIZA: bir drone kontrol istasyonu isteğinde model harita
                // alanını x 0-1 yaptı - ekranın TAMAMI - ve sağdaki telemetri paneline
                // yer bırakmadı. Konsol:
                //
                //   'TelemetryPanel' at [x 0.78-1] would overlap 'MapArea' at [x 0-1] by 100%
                //
                // Reddedildi; model paneli bu sefer tüm ekrana koymayı denedi, o da
                // reddedildi, görev tıkandı. Ekranda durum çubuğu ve haritadan başka
                // bir şey kalmadı.
                //
                // Bunu düzeltecek bilgi <ui_layout_rules> içinde HAZIR DURUYORDU ama o
                // blok hiç çağrılmıyor - elenmişti, çünkü anchor kuralları başka
                // bloklarda tekrarlanıyordu. Bölge haritalarının ise başka hiçbir
                // yerde karşılığı yoktu; elenen bloğun tek benzersiz parçası oydu.
                //
                // ZORUNLU, çünkü yanlış bölünmüş bir ekran sonradan DÜZELTİLEMİYOR:
                // ilk panel kaydedildikten sonra diğer her çağrı onunla çakışıyor ve
                // reddediliyor. Kart yerleşimi gibi sonradan telafi edilebilir bir şey
                // değil.
                set.Add(Mandatory, "screen_regions", PromptRulesUI.AppendScreenRegionRules);

                set.Add(Mandatory, "auto_layout_flow", PromptRulesUI.AppendAutoLayoutFlowRules);
                set.Add(Mandatory, "sibling_overlap", PromptRulesUI.AppendSiblingOverlapRules);

                // ============ SEKME YAPISI: İSTEK SEKME İÇERİYORSA ZORUNLU ============
                // GERÇEK ÖLÇÜM - TEKRARLANABİLİRLİK TESTİ: aynı sekmeli ekran isteği iki
                // kez çalıştırıldı ve İKİ FARKLI YAPI üretti.
                //
                //   Tur 1:  ContentArea > EnginePanel > EngineCard      (doğru)
                //   Tur 2:  ContentArea > EngineCard                    (panel katmanı yok)
                //
                // İkinci turda üç kart aynı anda görünür kaldı ve sekme geçişinin
                // bağlanacağı bir panel hiç oluşmadı. Kullanıcı Hierarchy'de kartları ve
                // satırları eksiksiz gördüğü için hatayı fark etmek bile zordu.
                //
                // Sebep: bu blok yalnızca PLAN aşamasında yükleniyor ve DÜŞÜRÜLEBİLİRDİ.
                // Bu, num_ctx 20480 iken zorunlu bir tasarruftu. num_ctx 32768'e
                // çıkarıldıktan sonra o kısıt ortadan kalktı ama ayar yerinde kaldı.
                //
                // Şimdi iki koşul birlikte:
                //   - istek sekme içeriyorsa  -> ZORUNLU, her aşamada
                //   - içermiyorsa             -> hiç yüklenmiyor
                //
                // Her aşamada, çünkü sekme kararı planlama biter bitmez verilmiyor:
                // model panelleri genellikle üçüncü-dördüncü adımda kuruyor, yani
                // PlanningPhaseHistoryLimit aşılmış oluyor ve blok eskiden tam o anda
                // düşüyordu.
                //
                // Maliyet ~1200 token; 32768'lik pencerede ~13.000 token boşluk var.
                // =====================================================================
                if (scope.WantsTabs)
                {
                    set.Add(Mandatory, "tabbed_screens", PromptRulesUI.AppendTabbedScreenRules);
                }

                set.Add(Mandatory, "ui_agent_rules", PromptRulesUI.AppendUIAgentRules);
                set.Add(Mandatory, "ui_execution_requirement", PromptRulesUI.AppendUiExecutionRequirement);

                // --- TASARIM REHBERİ: yalnızca plan aşamasında, düşürülebilir ---
                //
                // ÇAĞRILMAYAN DÖRT BLOK ve gerekçeleri - hepsinin içeriği başka bir
                // blokta zaten var, yani tekrar dışında bir katkıları yok:
                //
                //   AppendUILayoutRules
                //       Anchor ve boşluk kuralları <auto_layout_flow>,
                //       <sibling_overlap> ve <modern_design_rules> içinde daha somut
                //       anlatılıyor; kart boyutlandırma maddesi de
                //       <modern_design_rules>'a taşındı.
                //
                //   AppendProfessionalUIArchitecture
                //       Hiyerarşi derinliği ve gruplama <ui_planning>'de var.
                //
                //   AppendScreenPatterns
                //       İsimlendirme kuralları <ui_generation_pipeline>'da var.
                //
                //   AppendDesignSystemRules
                //       Tutarlılık kuralları <modern_design_rules>'un son bölümü.
                //
                //   AppendRoundedCornerRules
                //       Tek gerçek kuralı olan 'sharpCorners' ve "cornerRadius diye
                //       bir parametre yok" uyarısı <final_ui_reminder>'da zaten var.
                //
                // Toplam ~6.000 karakter, sıfır bilgi kaybı.
                if (includeDesignGuidance)
                {
                    set.Add(PrioUIPlanning, "ui_planning", PromptRulesUI.AppendUIPlanning);
                    set.Add(PrioModernDesign, "modern_design", PromptRulesUI.AppendModernDesignRules);
                    set.Add(PrioVisualReference, "ui_visual_reference", PromptRulesUI.AppendUiVisualReference);
                }
            }

            // --- Davranış bağlama: yalnızca istendiğinde ---
            //
            // TabController'ın alan alan nasıl doldurulacağını anlatan uzun kod örneği
            // burada (5.420 karakter). Sekmeli bir ekranda VAZGEÇİLMEZ, o yüzden
            // önceliği yüksek: sekmeler çalışmazsa ekran işlevsiz.
            if (scope.WantsWiring)
            {
                set.Add(PrioWiring, "behaviour_wiring", PromptRulesUI.AppendBehaviourWiringRules);
            }

            // --- Script yazma: yalnızca istendiğinde ---
            if (scope.WantsScript)
            {
                set.Add(PrioScript, "script_authoring", PromptRulesUI.AppendScriptAuthoringRules);
            }

            // --- İkon üretimi: yalnızca istendiğinde ---
            //
            // BU BLOK ESKİDEN HİÇ ÇAĞRILMIYORDU. Yazılmıştı ama BuildContext onu
            // atlıyordu, yani model ikon üretmeyi hiç öğrenmiyordu - ve ikon üretimi
            // bu projenin açık hedeflerinden biri.
            if (scope.WantsIcons)
            {
                set.Add(PrioIcons, "icon_authoring", PromptRulesUI.AppendIconAuthoringRules);
            }

            // --- ÇEKİRDEK: hata yönetimi ve bitiş ---
            bool explicitList = scope.IsExplicitStepList;
            AgentMode resolvedMode = mode;

            set.Add(Mandatory, "quality_rules",
                delegate (StringBuilder s) { AppendQualityRules(s, resolvedMode, explicitList); });
            set.Add(Mandatory, "error_handling", PromptRulesCore.AppendErrorHandling);
            set.Add(Mandatory, "final_response_rules", PromptRulesCore.AppendFinalResponseRules);
            set.Add(Mandatory, "tool_usage_rule", PromptRulesCore.AppendToolUsageRule);

            if (includeDesignGuidance)
            {
                set.Add(PrioSelfReflection, "self_reflection", PromptRulesCore.AppendSelfReflection);
            }

            // EN SON: modelin JSON üretmeden hemen önce okuduğu son talimat.
            // Çelişki durumunda model SON okuduğuna uyduğu için bu blok her zaman en
            // sonda kalmalı ve önceki blokların ÖZETİ olmalı. Bu yüzden ZORUNLU.
            if (scope.WantsUi)
            {
                set.Add(Mandatory, "final_ui_reminder", PromptRulesUI.AppendFinalUIReminder);
            }

            return Assemble(set, ruleBudget, isPlanningPhase);
        }

        private static void AppendQualityRules(StringBuilder sb, AgentMode mode, bool isExplicitStepList)
        {
            sb.AppendLine("<quality_rules>");
            sb.AppendLine("Operating Mode: " + mode);

            if (isExplicitStepList)
            {
                // Kullanıcı tam olarak ne istediğini yazmışsa "zenginleştir" mesajı
                // zararlı: gerçek testte model 3 satır istenen bir karta 11 satır
                // ekledi. Burada tersini söylüyoruz.
                sb.AppendLine("The user gave an explicit, numbered list. Your job is to execute it EXACTLY - not to improve it.");
                sb.AppendLine("Do not add elements that are not on the list. Do not change names, colors or anchors the user specified.");
                sb.AppendLine("Do not repeat an entry because it looks similar to the previous one - each numbered step happens ONCE.");
                sb.AppendLine("When the last numbered step succeeds, finish with {\"type\":\"task_complete\",\"params\":{\"summary\":\"...\"}}.");
            }
            else if (mode == AgentMode.Creative)
            {
                sb.AppendLine("Focus on rich aesthetics, complete visual structures, and polished layouts.");

                // Creative mod tek başına "daha fazla, daha zengin" mesajı veriyordu
                // ve bu, durma kurallarıyla doğrudan çelişiyordu: model "cilalı olsun"
                // talimatını "bitmedi, devam et" diye okuyabiliyordu.
                sb.AppendLine("Polish means getting each element right the FIRST time - correct colors, sizes and placement in its initial call.");
                sb.AppendLine("Polish does NOT mean adding more elements than the user asked for, and it does NOT mean revisiting elements that are already finished.");
                sb.AppendLine("An unfinished section of the screen is a far worse outcome than a plain one. Build every requested section before refining any of them.");
            }
            else
            {
                sb.AppendLine("Focus on precise execution, exact schema parameter matching, and strict compliance.");
            }

            sb.AppendLine("</quality_rules>\n");
        }
    }
}
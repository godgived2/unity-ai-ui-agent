using UnityEngine;

namespace AI.Core
{
    /// <summary>
    /// AI sistemi için merkezi sabit deðerler.
    /// Tool isimleri, klasör yollarý, servis isimleri ve
    /// genel AI ayarlarý burada tutulur.
    /// </summary>
    public static class AIConstants
    {

        // ==============================
        // AI VERSION
        // ==============================

        public const string AI_VERSION = "1.0.0";


        // ==============================
        // OLLAMA
        // ==============================

        /// <summary>
        /// Varsayýlan model.
        ///
        /// ============ 14b'DEN 8b'YE ============
        /// Ölçüm: qwen3:14b bu makinede 14 GB yer kaplýyor ve 'ollama ps'
        /// %58/%42 CPU/GPU gösteriyor. RTX 4060 Laptop'ta 8 GB VRAM var; 14B
        /// modelin aðýrlýklarý tek baþýna ~9 GB, üstüne 32768 token'lýk KV cache
        /// ~2,5 GB daha istiyor. Model hiçbir zaman GPU'ya sýðmadý.
        ///
        /// Sonuç 1,3 token/saniye - yani tek bir adým dakikalarca sürüyor ve
        /// 150 adýmlýk bir görev pratikte bitmiyor.
        ///
        /// qwen3:14b Q4'te ~5,2 GB: KV cache ile birlikte 8 GB'a rahat sýðýyor ve
        /// tamamen GPU'da çalýþýyor. Beklenen hýz 30-50 tok/s, yani 25-40 kat.
        ///
        /// Kalite kaybý endiþesi burada yersiz: modelin iþi akýl yürütmek deðil,
        /// þema takip edip geçerli JSON üretmek. Zor iþi (anchor aritmetiði,
        /// çakýþma denetimi, component sýrasý, reFerans baðlama) UiMacroExpander
        /// yapýyor. Ayrýca çalýþtýramadýðýn bir modelin kalitesi hiçbir iþe
        /// yaramýyor.
        ///
        /// DOÐRULAMA: bir görev çalýþýrken terminalde 'ollama ps'. PROCESSOR
        /// sütununda '100% GPU' görmelisiniz. CPU yüzdesi çýkýyorsa
        /// OllamaClient.ContextWindow'u 16384'e düþürün.
        /// =======================================
        /// </summary>
        public const string DEFAULT_MODEL = "qwen3:14b";

        /// <summary>
        /// Ollama uç noktasý.
        ///
        /// ============ 'localhost' DEÐÝL, '127.0.0.1' ============
        /// Windows'ta 'localhost' önce IPv6'ya (::1) çözülüyor. Ollama ise
        /// varsayýlan kurulumda yalnýzca IPv4 üzerinde dinliyor. Sonuç: .NET
        /// IPv6'ya baðlanmayý deniyor, baðlantý reddediliyor ve
        /// "An error occurred while sending the request" hatasý alýnýyor -
        /// Ollama sapasaðlam çalýþýrken.
        ///
        /// Gerçek testte tam olarak bu yaþandý: 'ollama serve' çalýþýyordu,
        /// tarayýcýdan eriþiliyordu, ama Unity baðlanamýyordu.
        ///
        /// OllamaClient.NormalizeUrl bunu zaten düzeltiyor, ama yalnýzca adres
        /// oradan geçtiðinde. Kaynaðý doðru tutmak, düzeltmeye güvenmekten iyi.
        ///
        /// Ollama'yý aðdaki baþka bir makinede çalýþtýrýyorsanýz burayý o
        /// makinenin IP adresiyle deðiþtirin.
        /// ========================================================
        /// </summary>
        public const string OLLAMA_URL =
            "http://127.0.0.1:11434/api/generate";



        // ==============================
        // MCP
        // ==============================

        public const string MCP_SERVER_NAME =
            "Unity MCP Server";


        public const string MCP_PROTOCOL =
            "mcp";



        // ==============================
        // PROJECT PATHS
        // ==============================

        public const string AI_ROOT =
            "Assets/AI/";


        public const string CONTEXT_PATH =
            "Assets/AI/Context/";


        public const string MODEL_PATH =
            "Assets/AI/Models/";


        public const string TOOL_PATH =
            "Assets/AI/Discovery/";


        /// <summary>
        /// Modelin ürettiði script'lerin yazýldýðý TEK klasör.
        ///
        /// UiMacroExpander.GeneratedScriptFolder, OllamaToolAgent'ýn güvenlik
        /// denetimi ve PromptRulesUI'deki kural metni bu yolla eþleþmeli. Üçü
        /// ayrýþýrsa model bir yere yazar, güvenlik baþka yere bakar ve dosya
        /// hiç doðrulanmaz. Sabiti burada tutmak o üçünün sessizce ayrýþmasýný
        /// zorlaþtýrýyor.
        /// </summary>
        public const string GENERATED_SCRIPT_PATH =
            "Assets/UI/Generated";


        /// <summary>Üretilen sprite'larýn yazýldýðý tek klasör.</summary>
        public const string GENERATED_SPRITE_PATH =
            "Assets/UI/Sprites";



        // ==============================
        // AGENT STATES
        // ==============================

        public const string STATE_IDLE =
            "Idle";


        public const string STATE_PLANNING =
            "Planning";


        public const string STATE_EXECUTING =
            "Executing";


        public const string STATE_VERIFYING =
            "Verifying";



        // ==============================
        // TOOL SYSTEM
        // ==============================

        public const string TOOL_CREATE =
            "create";


        public const string TOOL_MODIFY =
            "modify";


        public const string TOOL_DELETE =
            "delete";


        public const string TOOL_QUERY =
            "query";



        // ==============================
        // CONTEXT
        // ==============================

        /// <summary>
        /// Baðlamýn karakter sýnýrý.
        ///
        /// DÝKKAT - BU BÝR TOKEN SINIRI DEÐÝL. 50.000 karakter, Türkçe metinde
        /// kabaca 16.000-17.000 token demek. OllamaClient.ContextWindow ise
        /// 32768 token. Yani bu sabit tek baþýna baðlam taþmasýný engellemez;
        /// gerçek denetim OllamaClient'ýn prompt_eval_count ölçümünde.
        ///
        /// Ölçülen sistem promptu 63.563 karakterdi, yani bu sýnýrýn üstünde -
        /// eðer bir yerde bu deðere göre kýrpma yapýlýyorsa promptun sonu
        /// sessizce kesiliyor olabilir. ContextBuilder'ý incelerken bunu
        /// doðrulayýn.
        /// </summary>
        public const int MAX_CONTEXT_LENGTH =
            50000;


        public const int MAX_HISTORY_MESSAGES =
            50;



        // ==============================
        // LOGGING
        // ==============================

        public const string LOG_PREFIX =
            "[LOCAL AI]";


        public static void Log(string message)
        {
            Debug.Log(
                $"{LOG_PREFIX} {message}"
            );
        }


        public static void LogWarning(string message)
        {
            Debug.LogWarning(
                $"{LOG_PREFIX} {message}"
            );
        }


        public static void LogError(string message)
        {
            Debug.LogError(
                $"{LOG_PREFIX} {message}"
            );
        }

    }
}
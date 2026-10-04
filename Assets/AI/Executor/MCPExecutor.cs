using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Executor
{
    /// <summary>
    /// MCP Execution Layer.
    ///
    /// ExecutionAgent -> MCPAgent -> MCPExecutor -> MCPUnityClient
    ///   -> TransportCommandDispatcher -> CommandRegistry -> MCP Tools
    ///
    /// Responsibilities:
    /// - Receive validated MCP JSON commands ({"type": "...", "params": {...}}).
    /// - Forward them RAW to the dispatcher (no extra envelope).
    /// - Return raw MCP responses.
    ///
    /// ============ BU SÜRÜMDE DÜZELTÝLENLER ============
    ///
    /// 1. ÖLÜ ÝSTEMCÝ: Initialize, _instance doluysa KOÞULSUZ return ediyordu.
    ///    OllamaToolAgent her görevde Initialize(_client) çaðýrýyor; MCP baðlantýsý
    ///    kopup yeniden kurulduðunda ajan YENÝ bir istemciyle geliyor ama executor
    ///    ESKÝ, ölü istemciyi tutmaya devam ediyordu. Sonraki her çaðrý baþarýsýz
    ///    oluyor ve hata mesajý sebebi hiç göstermiyordu.
    ///
    /// 2. ÝSTEMCÝ KAYNAÐI TUTARSIZLIÐI: EnsureInitialized MCPUnityClient.Instance
    ///    kullanýyordu, ajan ise GetClient(). Ýkisi farklý örneðe iþaret ediyorsa
    ///    otomatik baþlatma baðlanmamýþ bir istemci yakalýyordu. Artýk ikisi de
    ///    GetClient().
    ///
    /// 3. ZAMAN AÞIMI YOKTU: ExecuteRawAsync varsayýlan CancellationToken.None ile
    ///    çaðrýlýyordu. MCP sunucusu takýlýrsa ajan SONSUZA KADAR bekliyordu - ne bir
    ///    hata, ne bir log, ne de bir çýkýþ yolu. Artýk her çaðrýnýn kendi zaman aþýmý
    ///    var ve aþýldýðýnda modele okunabilir bir hata dönüyor.
    ///
    /// 4. ÇAÐIRANIN NESNESÝ DEÐÝÞTÝRÝLÝYORDU: command["params"] doðrudan gelen
    ///    JObject üzerine yazýlýyordu. Ajan ayný nesneden çaðrý imzasý üretiyor, yani
    ///    yürütme öncesi ve sonrasý imza farklý olabiliyordu - duplicate korumasý
    ///    sessizce delinebiliyordu. Artýk kopya üzerinde çalýþýlýyor.
    ///
    /// 5. HATA ZARFI TUTARSIZDI: CreateError {"status","success","error"} döndürüyordu
    ///    ama macro katmaný ve ajan mesajý result.message içinde arýyor. Model hata
    ///    metnini bazen bulup bazen bulamýyordu. Artýk her iki yerde de var.
    ///
    /// 6. LOG SELÝ: EnableLogging varsayýlan TRUE ve her çaðrýnýn TAM JSON'u
    ///    yazýlýyordu. Tek bir macro 7+ çaðrý yapýyor, execute_code yükleri kilobaytlar
    ///    tutuyor - Console kullanýlamaz hale geliyordu. Artýk varsayýlan kapalý ve
    ///    açýkken bile kýrpýlýyor.
    /// ==================================================
    /// </summary>
    public sealed class MCPExecutor
    {
        /// <summary>
        /// Tek bir MCP çaðrýsýnýn azami süresi.
        ///
        /// MCP çaðrýlarý normalde milisaniyeler sürüyor. 90 saniye, sunucunun gerçekten
        /// takýldýðýný anlamak için fazlasýyla yeterli ve kullanýcýyý süresiz
        /// bekletmiyor. execute_code içinde aðýr bir döngü varsa bu sýnýr aþýlabilir -
        /// o durumda kodu bölmek doðru çözüm.
        /// </summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>Loglanan JSON'un azami uzunluðu.</summary>
        private const int MaxLoggedJsonLength = 600;

        private readonly MCPUnityClient _client;
        private static MCPExecutor _instance;

        public static MCPExecutor Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("MCPExecutor is not initialized.");

                return _instance;
            }
        }

        public static bool IsInitialized => _instance != null;

        /// <summary>
        /// Ayrýntýlý log. VARSAYILAN KAPALI.
        ///
        /// Eskiden true idi ve her çaðrýnýn tam JSON'unu yazýyordu. Tek bir
        /// create_ui_card 10 MCP çaðrýsý yapýyor; 30 elemanlý bir ekranda Console
        /// yüzlerce satýr JSON'la doluyor ve gerçek hatalar arasýnda kayboluyordu.
        /// OllamaToolAgent zaten kendi EnableVerboseLogging bayraðýyla anlamlý olaný
        /// yazýyor.
        /// </summary>
        public bool EnableLogging { get; set; }

        /// <summary>Bu executor'ýn baðlý olduðu istemci - teþhis için.</summary>
        public MCPUnityClient Client => _client;

        private MCPExecutor(MCPUnityClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        // =====================================================
        // INITIALIZATION
        // =====================================================

        /// <summary>
        /// Executor'ý verilen istemciye baðlar.
        ///
        /// KRÝTÝK DÜZELTME: eskiden _instance doluysa koþulsuz return ediliyordu.
        /// Artýk istemci DEÐÝÞTÝYSE yeniden baðlanýyor. Aksi hâlde MCP kopup yeniden
        /// baðlandýðýnda executor ölü istemciyi tutmaya devam ediyor ve her çaðrý
        /// sessizce baþarýsýz oluyordu.
        /// </summary>
        public static void Initialize(MCPUnityClient client)
        {
            if (client == null)
            {
                Debug.LogError("[MCP Executor] Cannot initialize. Client is null.");
                return;
            }

            if (_instance != null)
            {
                if (ReferenceEquals(_instance._client, client))
                    return;

                Debug.Log("[MCP Executor] A different MCP client was supplied - rebinding. " +
                          "(The previous connection was probably dropped and re-established.)");
            }

            _instance = new MCPExecutor(client);
            Debug.Log("[MCP Executor] Initialized.");
        }

        /// <summary>
        /// Varsayýlan istemciyle baþlatýr. Birden fazla kez çaðrýlmasý güvenli.
        ///
        /// GetClient() kullanýlýyor - OllamaToolAgent da onu kullanýyor. Eskiden burada
        /// Instance vardý; ikisi farklý örnek döndürüyorsa otomatik baþlatma
        /// baðlanmamýþ bir istemciye tutunuyordu.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_instance != null)
                return;

            try
            {
                Initialize(MCPUnityClient.GetClient());
            }
            catch (Exception ex)
            {
                Debug.LogError("[MCP Executor] Auto-initialization failed: " + ex.Message);
            }
        }

        // =====================================================
        // EXECUTION ENTRY
        // =====================================================

        public static async Task<string> Execute(
            JObject command,
            CancellationToken cancellationToken = default)
        {
            // Her çaðrýda sessizce baþarýsýz olmak yerine otomatik baþlat.
            EnsureInitialized();

            if (_instance == null)
            {
                return CreateError(
                    "MCPExecutor is not initialized and no MCP client is available. " +
                    "The MCP connection is probably down - check the Unity Console and the MCP server.");
            }

            return await _instance.ExecuteAsync(command, cancellationToken);
        }

        // =====================================================
        // MCP REQUEST PIPELINE
        // =====================================================

        public async Task<string> ExecuteAsync(
            JObject command,
            CancellationToken cancellationToken = default)
        {
            if (command == null)
            {
                Debug.LogError("[MCP Executor] Command is NULL.");
                return CreateError("MCP command is null.");
            }

            // ---- Dispatcher'ýn beklediði þekli doðrula ----
            string toolType = command["type"]?.ToString();

            if (string.IsNullOrWhiteSpace(toolType))
            {
                Debug.LogError("[MCP Executor] Command missing 'type' field.");
                return CreateError("MCP command is missing its 'type' field. Every call must be {\"type\":\"<tool>\",\"params\":{...}}.");
            }

            // ÇAÐIRANIN NESNESÝNÝ DEÐÝÞTÝRME.
            //
            // Eskiden eksik 'params' doðrudan gelen JObject'e yazýlýyordu. Ajan ayný
            // nesneden çaðrý imzasý üretiyor; yürütme öncesi üretilen imza ile sonrasý
            // farklý olabiliyor ve duplicate korumasý sessizce delinebiliyordu.
            JObject payload = (JObject)command.DeepClone();

            if (payload["params"] == null || !(payload["params"] is JObject))
            {
                payload["params"] = new JObject();
            }

            string json = payload.ToString(Formatting.None);

            Log("EXECUTE START");
            Log($"Tool: {toolType}");
            Log($"JSON: {Truncate(json, MaxLoggedJsonLength)}");

            string response;

            // Zaman aþýmý: MCP takýlýrsa ajan sonsuza kadar beklemesin.
            using (var timeoutSource = new CancellationTokenSource(DefaultTimeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                try
                {
                    // KRÝTÝK: _client.SendRequest(json, ct) ÇAÐRILMAMALI. O çaðrý
                    // (string toolName, CancellationToken) aþýrý yüklemesine çözülüyor;
                    // tüm JSON tool ADI olarak geçiyor ve params düþüyordu. Ardýndan
                    // {"command":..., "params":...} zarfýna sarýlýyor, dispatcher ise
                    // "type"/"params" bekliyor.
                    //
                    // ExecuteRawAsync JSON'u olduðu gibi
                    // TransportCommandDispatcher.ExecuteCommandJsonAsync'e iletiyor -
                    // zaten elimizde olan þekil bu.
                    response = await _client.ExecuteRawAsync(json, linked.Token);
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
                {
                    string message =
                        $"The MCP call '{toolType}' did not respond within {DefaultTimeout.TotalSeconds:0} seconds and was aborted. " +
                        "The MCP server may be stuck or the operation may be too heavy. " +
                        "If this was execute_code, split the code into smaller pieces.";

                    Debug.LogError("[MCP Executor] TIMEOUT: " + message);
                    return CreateError(message);
                }
                catch (OperationCanceledException)
                {
                    Debug.LogWarning("[MCP Executor] Execution cancelled by the caller.");
                    return CreateError("MCP execution cancelled.");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[MCP Executor] ExecuteRawAsync EXCEPTION:");
                    Debug.LogException(ex);
                    return CreateError(Describe(ex));
                }
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                Debug.LogError("[MCP Executor] EMPTY RESPONSE FROM MCP.");
                return CreateError(
                    $"The MCP call '{toolType}' returned an empty response. The connection may have dropped mid-call.");
            }

            Log($"RESPONSE: {Truncate(response, MaxLoggedJsonLength)}");
            return response;
        }

        // =====================================================
        // COMPATIBILITY
        // =====================================================

        public async Task<string> ExecuteTool(
            JObject command,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(command, cancellationToken);
        }

        // =====================================================
        // STATE
        // =====================================================

        public static void Reset()
        {
            _instance = null;
            Debug.Log("[MCP Executor] Reset.");
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private void Log(string message)
        {
            if (!EnableLogging)
                return;

            Debug.Log("[MCP Executor] " + message);
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? string.Empty;

            return text.Substring(0, maxLength) + $"... [+{text.Length - maxLength} chars]";
        }

        /// <summary>
        /// Ýstisna zincirini tek okunabilir satýra indirir. En anlamlý bilgi genelde
        /// en içteki istisnadadýr; sadece ex.Message yazmak çoðu zaman hiçbir þey
        /// söylemeyen bir cümle veriyor.
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex == null)
                return "unknown error";

            var sb = new System.Text.StringBuilder(ex.Message);
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

        /// <summary>
        /// Hata zarfý.
        ///
        /// Mesaj ÝKÝ yerde birden duruyor:
        ///   - result.message : macro katmanýnýn ve modelin baktýðý yer
        ///   - error          : eski okuyucularýn baktýðý yer
        ///
        /// Eskiden yalnýzca 'error' vardý ve model hata metnini bazen bulup bazen
        /// bulamýyordu - bulamadýðýnda ayný hatalý çaðrýyý tekrar deniyordu.
        ///
        /// status="error" ve result.success=false birlikte: hem OllamaToolAgent'ýn
        /// WasExecutionSuccessful'ý hem UiMacroExpander'ýn WasStepSuccessful'ý bunu
        /// doðru okuyor.
        /// </summary>
        private static string CreateError(string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? "Unknown MCP error." : message;

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
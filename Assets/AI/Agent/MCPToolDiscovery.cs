using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AI.Client;
using AI.Discovery;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using AIToolMetadata = AI.Discovery.ToolMetadata;
using AIToolRegistry = AI.Discovery.ToolRegistry;
using ServiceToolMetadata = MCPForUnity.Editor.Services.ToolMetadata;

namespace MCPForUnity.Editor.AI
{
    /// <summary>
    /// Agent þemasýnýn ayrýntý düzeyi. Ollama baðlam penceresi bu projedeki asýl darboðaz
    /// olduðu için varsayýlan <see cref="Compact"/>.
    /// </summary>
    public enum AgentSchemaVerbosity
    {
        /// <summary>Eski davranýþ: description + capability + allowed_actions + required + schema.
        /// Alanlar birbirini tekrar eder; sadece geriye dönük uyumluluk için.</summary>
        Full = 0,

        /// <summary>Tekrarlar temizlenmiþ, açýklamalarý kýrpýlmýþ, gereksiz JSON Schema
        /// anahtarlarý atýlmýþ hâli. Tool baþýna kabaca %40-60 daha az token.</summary>
        Compact = 1,

        /// <summary>Sadece imza: <c>manage_gameobject(action!:string, name:string)</c>.
        /// Þema hiç gönderilmez; çok uzun görevlerde ilk tarama için.</summary>
        Signature = 2
    }

    /// <summary>
    /// MCP tool'larýný keþfeder, indeksler ve LLM'e sunulacak agent þemasýný üretir.
    ///
    /// Eþzamanlýlýk modeli: keþif <see cref="_lock"/> altýnda yapýlýr ve sonuç
    /// deðiþmez (immutable) bir <see cref="ToolIndex"/> olarak tek atomik atamayla
    /// yayýnlanýr. Okuyan tüm metotlar kilit almadan bu anlýk görüntü üzerinden
    /// çalýþýr, dolayýsýyla "Collection was modified" yarýþý ve main thread
    /// bloklanmasý yoktur.
    /// </summary>
    public sealed class MCPToolDiscovery : IDisposable
    {
        // =====================================================
        // CONFIGURATION
        // =====================================================

        /// <summary>
        /// Alaka skorlamasýndan baðýmsýz olarak HER ZAMAN agent þemasýna dahil edilecek
        /// tool'lar.
        ///
        /// Neden gerekli: FindRelevantToolsAsync sadece en alakalý N tool'u seçiyor.
        /// execute_code, UI'ýn davranýþ tarafýný baðlamak için þart (sekme geçiþi,
        /// Toggle.targetGraphic, Slider.fillRect gibi sahne referanslarý) - ama tipik bir
        /// UI promptunda "code" / "execute" kelimesi geçmez. Skorlamaya býrakýlamaz.
        ///
        /// Çalýþma zamanýnda deðiþtirilebilir: discovery.PinnedTools.Add("manage_scene");
        /// </summary>
        public HashSet<string> PinnedTools { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "execute_code" };

        /// <summary>Agent þemasýna hiçbir zaman girmeyecek tool'lar (meta/self-referans).</summary>
        public HashSet<string> ExcludedFromSchema { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "get_mcp_tools" };

        public AgentSchemaVerbosity SchemaVerbosity { get; set; } = AgentSchemaVerbosity.Compact;

        /// <summary>Compact modda tool açýklamasýnýn kýrpýlacaðý karakter sayýsý. 0 = kýrpma.</summary>
        public int MaxToolDescriptionLength { get; set; } = 220;

        /// <summary>Compact modda parametre açýklamasýnýn kýrpýlacaðý karakter sayýsý. 0 = kýrpma.</summary>
        public int MaxParamDescriptionLength { get; set; } = 110;

        /// <summary>Þema JSON'unun kaç karakter olduðu (son üretimde). Baðlam bütçesi teþhisi için.</summary>
        public int LastSchemaCharCount { get; private set; }

        public bool EnableVerboseLogging { get; set; } = true;

        // =====================================================
        // STATE
        // =====================================================

        private readonly MCPUnityClient _client;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private volatile ToolIndex _index = ToolIndex.Empty;
        private volatile bool _isLoaded;
        private volatile string _lastError;
        private int _disposed;

        public MCPUnityClient Client => _client;
        public bool IsLoaded => _isLoaded;
        public string LastError => _lastError;

        public event Action<IReadOnlyList<MCPToolInfo>> OnToolsDiscovered;
        public event Action<string> OnDiscoveryFailed;

        public MCPToolDiscovery(MCPUnityClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        // =====================================================
        // DISCOVERY
        // =====================================================

        public async Task<IReadOnlyList<MCPToolInfo>> DiscoverToolsAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ToolIndex published = null;
            string failure = null;

            try
            {
                await _lock.WaitAsync(cancellationToken).ConfigureAwait(true);

                try
                {
                    if (_isLoaded && !forceRefresh)
                        return _index.Tools;

                    ToolIndex built = BuildIndex(cancellationToken, out failure);

                    if (built != null)
                    {
                        // Registry'yi yeniden doldurmadan önce temizle: forceRefresh her
                        // çaðrýldýðýnda Register() tekrar çalýþýyordu ve registry isimden
                        // dedupe etmiyorsa duplicate birikiyordu.
                        TryResetRegistry();

                        foreach (MCPToolInfo tool in built.Tools)
                            RegisterTool(tool);

                        // Tek atomik yayýn - okuyucular ya eski ya yeni indeksi görür,
                        // asla yarý kurulmuþ bir tanesini görmez.
                        _index = built;
                        _isLoaded = true;
                        _lastError = null;
                        published = built;
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Log("Discovery iptal edildi.");
                return _index.Tools;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MCP Discovery] " + ex);
                failure = ex.Message;
            }

            // Event'ler KÝLÝT DIÞINDA tetiklenir. Aksi hâlde bir handler tekrar
            // DiscoverToolsAsync çaðýrdýðýnda SemaphoreSlim reentrant olmadýðý için
            // Editor kilitleniyordu.
            if (failure != null)
            {
                _lastError = failure;
                SafeInvoke(OnDiscoveryFailed, failure);
            }

            if (published != null)
                SafeInvoke(OnToolsDiscovered, published.Tools);

            return _index.Tools;
        }

        /// <summary>Kilit altýnda çaðrýlýr. Hiçbir event tetiklemez, hiçbir alaný yayýnlamaz.</summary>
        private ToolIndex BuildIndex(CancellationToken cancellationToken, out string failure)
        {
            failure = null;

            var service = new ToolDiscoveryService();
            IReadOnlyList<ServiceToolMetadata> discovered = service.DiscoverAllTools() as IReadOnlyList<ServiceToolMetadata>
                                                            ?? service.DiscoverAllTools()?.ToList();

            if (discovered == null || discovered.Count == 0)
            {
                failure = "Hiç MCP tool'u keþfedilemedi (ToolDiscoveryService boþ döndü).";
                Debug.LogError("[MCP] " + failure);
                return null;
            }

            var tools = new List<MCPToolInfo>(discovered.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keywordIndex = NewIndex();
            var groupIndex = NewIndex();
            var actionIndex = NewIndex();

            foreach (ServiceToolMetadata tool in discovered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                    continue;

                if (!names.Add(tool.Name))
                {
                    Log($"Yinelenen tool adý atlandý: {tool.Name}");
                    continue;
                }

                MCPToolInfo info = CreateToolInfo(tool);
                tools.Add(info);

                IndexTool(info, keywordIndex, groupIndex, actionIndex);
                Log("Registered : " + info.Name);
            }

            if (tools.Count == 0)
            {
                failure = "Keþfedilen tool'larýn hiçbiri geçerli deðil (adsýz / yinelenen).";
                Debug.LogError("[MCP] " + failure);
                return null;
            }

            return new ToolIndex(tools, names, keywordIndex, groupIndex, actionIndex);
        }

        private MCPToolInfo CreateToolInfo(ServiceToolMetadata tool)
        {
            // Önce elle yazýlmýþ özel þema var mý sor (manage_ui, execute_code gibi -
            // [ToolParameter] Parameters sýnýfý desenini kullanmayan tool'lar). Varsa
            // ManageUI.GetToolSchema() gibi otorite kaynaktan gelen TAM þemayý kullan.
            // Yoksa reflection tabanlý BuildInputSchema'ya düþ.
            JObject specialSchema = null;

            try
            {
                specialSchema = MCPToolDiscoveryCommand.GetSpecialCaseSchema(tool.Name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Discovery] '{tool.Name}' için özel þema alýnamadý, " +
                                 $"reflection þemasýna düþülüyor: {ex.Message}");
            }

            JObject inputSchema;
            List<string> requiredParams;
            List<string> actions;

            if (specialSchema != null)
            {
                inputSchema = specialSchema;
                requiredParams = ExtractTopLevelRequired(specialSchema);
                actions = ExtractEnumActions(specialSchema);
            }
            else
            {
                inputSchema = BuildInputSchema(tool, out requiredParams, out actions);
            }

            var info = new MCPToolInfo(tool.Name)
            {
                Description = tool.Description ?? string.Empty,
                Group = string.IsNullOrWhiteSpace(tool.Group) ? "core" : tool.Group,
                InputSchema = inputSchema ?? new JObject(),
                RequiredParams = requiredParams ?? new List<string>()
            };

            // Actions, IndexTool'dan ÖNCE set edilmeli: aksi hâlde _actionIndex (en güçlü
            // skorlama sinyali) boþ kalýr.
            info.SetActions(actions);
            return info;
        }

        // =====================================================
        // PUBLIC API (geriye dönük uyumlu sarmalayýcýlar)
        // =====================================================

        public Task<IReadOnlyList<MCPToolInfo>> DiscoverTools(bool forceRefresh = false)
        {
            return DiscoverToolsAsync(forceRefresh, CancellationToken.None);
        }

        public Task<string> BuildAgentSchema()
        {
            return BuildAgentSchemaAsync(userPrompt: null);
        }

        /// <summary>
        /// Belirli bir tool listesinden þema üretir. Yeni kodda bunun yerine
        /// <see cref="BuildAgentSchemaForToolsAsync"/> tercih edin: iki aþýrý yükleme
        /// birlikte bulunduðu için <c>BuildAgentSchemaAsync(null)</c> çaðrýsý derleme
        /// hatasý (belirsiz aþýrý yükleme) verir.
        /// </summary>
        public Task<string> BuildAgentSchemaAsync(IEnumerable<MCPToolInfo> tools)
        {
            return Task.FromResult(BuildAgentSchemaFromList(tools));
        }

        public Task<string> BuildAgentSchemaForToolsAsync(IEnumerable<MCPToolInfo> tools)
        {
            return Task.FromResult(BuildAgentSchemaFromList(tools));
        }

        public async Task<string> BuildAgentSchemaAsync(string userPrompt = null, int topCount = 8)
        {
            IEnumerable<MCPToolInfo> targetTools = string.IsNullOrWhiteSpace(userPrompt)
                ? await DiscoverToolsAsync().ConfigureAwait(true)
                : await FindRelevantToolsAsync(userPrompt, topCount).ConfigureAwait(true);

            return BuildAgentSchemaFromList(targetTools);
        }

        // =====================================================
        // AGENT SCHEMA
        // =====================================================

        public string BuildAgentSchemaFromList(IEnumerable<MCPToolInfo> tools)
        {
            var array = new JArray();

            if (tools != null)
            {
                foreach (MCPToolInfo tool in tools)
                {
                    if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                        continue;

                    if (ExcludedFromSchema.Contains(tool.Name))
                        continue;

                    array.Add(BuildToolNode(tool));
                }
            }

            string json = array.ToString(Formatting.None);
            LastSchemaCharCount = json.Length;

            Log($"Agent þemasý: {array.Count} tool, {json.Length} karakter " +
                $"(~{json.Length / 4} token, mod={SchemaVerbosity}).");

            return json;
        }

        private JObject BuildToolNode(MCPToolInfo tool)
        {
            string[] actions = tool.Actions.ToArray();

            switch (SchemaVerbosity)
            {
                case AgentSchemaVerbosity.Signature:
                    {
                        var node = new JObject
                        {
                            ["name"] = tool.Name,
                            ["signature"] = BuildSignature(tool),
                            ["description"] = Trim(tool.Description, MaxToolDescriptionLength)
                        };
                        if (actions.Length > 0)
                            node["actions"] = new JArray(actions);
                        return node;
                    }

                case AgentSchemaVerbosity.Full:
                    {
                        // Eski (tekrarlý) biçim. Sadece geriye dönük uyumluluk için.
                        return new JObject
                        {
                            ["name"] = tool.Name,
                            ["description"] = tool.Description,
                            ["capability"] = $"{tool.Description} Actions: {string.Join(",", actions)}",
                            ["group"] = tool.Group,
                            ["allowed_actions"] = new JArray(actions),
                            ["required"] = new JArray(tool.RequiredParams ?? new List<string>()),
                            ["schema"] = tool.InputSchema
                        };
                    }

                default:
                    {
                        // Compact: 'capability' kaldýrýldý (description'ý tekrar ediyordu),
                        // 'required' kaldýrýldý (schema.required içinde zaten var),
                        // 'allowed_actions' sadece þemada enum yoksa yazýlýr.
                        var node = new JObject
                        {
                            ["name"] = tool.Name,
                            ["description"] = Trim(tool.Description, MaxToolDescriptionLength),
                            ["group"] = tool.Group
                        };

                        bool actionsInSchema = tool.InputSchema?["properties"]?["action"]?["enum"] is JArray;
                        if (actions.Length > 0 && !actionsInSchema)
                            node["actions"] = new JArray(actions);

                        node["schema"] = CompactSchema(tool.InputSchema);
                        return node;
                    }
            }
        }

        private string BuildSignature(MCPToolInfo tool)
        {
            var sb = new StringBuilder(tool.Name).Append('(');

            if (tool.InputSchema?["properties"] is JObject props)
            {
                var required = new HashSet<string>(
                    tool.RequiredParams ?? new List<string>(), StringComparer.Ordinal);

                bool first = true;
                foreach (JProperty prop in props.Properties())
                {
                    if (!first) sb.Append(", ");
                    first = false;

                    sb.Append(prop.Name);
                    if (required.Contains(prop.Name)) sb.Append('!');

                    string type = (prop.Value as JObject)?["type"]?.ToString();
                    if (!string.IsNullOrEmpty(type)) sb.Append(':').Append(type);
                }
            }

            return sb.Append(')').ToString();
        }

        /// <summary>
        /// Þemayý KLONLAYIP token yiyen anahtarlarý temizler. Klonlama þart: aksi hâlde
        /// önbellekteki (ve özel-þema saðlayýcýsýnýn sahip olduðu) JObject bozulur.
        /// </summary>
        private JToken CompactSchema(JToken schema)
        {
            if (schema == null)
                return new JObject();

            JToken clone = schema.DeepClone();
            PruneSchema(clone, 0);
            return clone;
        }

        private static readonly string[] PrunableKeys =
        {
            "title", "examples", "example", "default", "additionalProperties",
            "$schema", "$id", "readOnly", "writeOnly", "deprecated"
        };

        private void PruneSchema(JToken token, int depth)
        {
            if (depth > MaxSchemaDepth) return;

            if (token is JObject obj)
            {
                foreach (string key in PrunableKeys)
                    obj.Remove(key);

                if (MaxParamDescriptionLength > 0 &&
                    obj["description"] is JValue desc &&
                    desc.Type == JTokenType.String)
                {
                    obj["description"] = Trim(desc.ToString(), MaxParamDescriptionLength);
                }

                foreach (JProperty prop in obj.Properties().ToList())
                    PruneSchema(prop.Value, depth + 1);
            }
            else if (token is JArray arr)
            {
                foreach (JToken item in arr)
                    PruneSchema(item, depth + 1);
            }
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || max <= 0 || text.Length <= max)
                return text ?? string.Empty;

            int cut = text.LastIndexOf(' ', Math.Min(max - 1, text.Length - 1));
            if (cut < max / 2) cut = max - 1;

            return text.Substring(0, cut).TrimEnd() + "…";
        }

        // =====================================================
        // SEARCH ENGINE & TOOL RELEVANCE
        // =====================================================

        private const int ScoreExactName = 40;
        private const int ScoreNameToken = 20;
        private const int ScoreNameTokenCap = 40;
        private const int ScoreAction = 15;
        private const int ScoreActionCap = 30;
        private const int ScoreGroup = 8;
        private const int ScoreKeyword = 4;
        private const int ScoreKeywordCap = 20;

        public async Task<IReadOnlyList<MCPToolInfo>> FindRelevantToolsAsync(string prompt, int topCount = 8)
        {
            ThrowIfDisposed();

            if (!_isLoaded)
                await DiscoverToolsAsync().ConfigureAwait(true);

            ToolIndex index = _index;

            if (index.Tools.Count == 0)
                return EmptyTools;

            if (topCount <= 0)
                topCount = 8;

            if (string.IsNullOrWhiteSpace(prompt))
                return WithPinned(index, index.Tools.Take(topCount).ToList());

            // Tokenize lazy bir IEnumerable döndürüyordu; tool × token döngüsünde regex
            // zinciri yüzlerce kez yeniden çalýþýyordu. Tek seferde materyalize ediyoruz.
            HashSet<string> promptTokens = ExpandWithSynonyms(Tokenize(prompt));
            string normalizedPrompt = NormalizeForCompare(prompt);

            var scored = new List<KeyValuePair<MCPToolInfo, int>>(index.Tools.Count);

            foreach (MCPToolInfo tool in index.Tools)
            {
                int score = 0;

                // 1) Tam ad geçiyor mu ("execute_code" / "execute code")
                if (normalizedPrompt.Contains(NormalizeForCompare(tool.Name)))
                    score += ScoreExactName;

                // 2) Ad token'larý
                int nameScore = 0;
                foreach (string nameToken in Tokenize(tool.Name))
                {
                    if (promptTokens.Contains(nameToken))
                        nameScore += ScoreNameToken;
                }
                score += Math.Min(nameScore, ScoreNameTokenCap);

                // 3) Grup
                if (!string.IsNullOrWhiteSpace(tool.Group) &&
                    promptTokens.Contains(NormalizeForCompare(tool.Group)))
                {
                    score += ScoreGroup;
                }

                // 4) Action ve keyword indeksleri (üst sýnýrlý: uzun açýklamalý bir tool,
                //    adý birebir eþleþen tool'u geçemesin)
                int actionScore = 0;
                int keywordScore = 0;

                foreach (string token in promptTokens)
                {
                    if (index.Actions.TryGetValue(token, out HashSet<MCPToolInfo> actionMatches) &&
                        actionMatches.Contains(tool))
                    {
                        actionScore += ScoreAction;
                    }

                    if (index.Keywords.TryGetValue(token, out HashSet<MCPToolInfo> keywordMatches) &&
                        keywordMatches.Contains(tool))
                    {
                        keywordScore += ScoreKeyword;
                    }
                }

                score += Math.Min(actionScore, ScoreActionCap);
                score += Math.Min(keywordScore, ScoreKeywordCap);

                if (score > 0)
                    scored.Add(new KeyValuePair<MCPToolInfo, int>(tool, score));
            }

            List<MCPToolInfo> relevant = scored
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key.Name, StringComparer.Ordinal) // deterministik eþitlik bozumu
                .Select(x => x.Key)
                .Take(topCount)
                .ToList();

            if (relevant.Count == 0)
            {
                Log($"'{prompt}' için hiçbir tool skor alamadý; ilk {topCount} tool'a düþülüyor. " +
                    "Bu genelde prompt dilinin tool adlarýyla örtüþmediðini gösterir.");
                relevant = index.Tools.Take(topCount).ToList();
            }

            return WithPinned(index, relevant);
        }

        /// <summary>
        /// PinnedTools'daki her tool'u alaka skorundan baðýmsýz olarak sonuca ekler.
        ///
        /// Listenin SONUNA ekliyoruz, en düþük skorluyu atmýyoruz: fazladan bir-iki tool
        /// birkaç yüz token, eksik bir tool ise o yeteneðin tamamen kaybý demek. Sona
        /// eklemenin ikinci faydasý: Ollama baðlamý taþýnca prompt'un BAÞI kýrpýldýðý için
        /// kritik tool'lar hayatta kalýr.
        /// </summary>
        private IReadOnlyList<MCPToolInfo> WithPinned(ToolIndex index, List<MCPToolInfo> tools)
        {
            foreach (string toolName in PinnedTools)
            {
                if (tools.Any(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                MCPToolInfo tool = index.Tools.FirstOrDefault(t =>
                    t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

                if (tool != null)
                {
                    tools.Add(tool);
                    Log($"'{toolName}' þemaya zorla eklendi (skorlama elemiþti).");
                }
                else
                {
                    Debug.LogWarning($"[MCP Discovery] '{toolName}' PinnedTools içinde ama hiç " +
                                     "keþfedilmedi - model bu yeteneði kullanamayacak.");
                }
            }

            return new ReadOnlyCollection<MCPToolInfo>(tools);
        }

        // =====================================================
        // INDEXING ENGINE
        // =====================================================

        private const int MaxSchemaDepth = 8;

        private static Dictionary<string, HashSet<MCPToolInfo>> NewIndex()
        {
            return new Dictionary<string, HashSet<MCPToolInfo>>(StringComparer.OrdinalIgnoreCase);
        }

        private void IndexTool(
            MCPToolInfo tool,
            Dictionary<string, HashSet<MCPToolInfo>> keywordIndex,
            Dictionary<string, HashSet<MCPToolInfo>> groupIndex,
            Dictionary<string, HashSet<MCPToolInfo>> actionIndex)
        {
            if (!string.IsNullOrWhiteSpace(tool.Group))
                AddToIndex(groupIndex, NormalizeForCompare(tool.Group), tool);

            foreach (string action in tool.Actions)
            {
                foreach (string token in Tokenize(action))
                    AddToIndex(actionIndex, token, tool);
            }

            foreach (string token in Tokenize(tool.Name))
                AddToIndex(keywordIndex, token, tool);

            foreach (string token in Tokenize(tool.Description))
            {
                if (!Stopwords.Contains(token))
                    AddToIndex(keywordIndex, token, tool);
            }

            ExtractAllProperties(tool.InputSchema, tool, keywordIndex, 0);
        }

        private void ExtractAllProperties(
            JToken token,
            MCPToolInfo tool,
            Dictionary<string, HashSet<MCPToolInfo>> keywordIndex,
            int depth)
        {
            // Derinlik korumasý: $ref'li veya kendine referans veren þemalarda özyineleme
            // Editor'ü StackOverflow ile çökertiyordu.
            if (token == null || depth > MaxSchemaDepth)
                return;

            if (token is JObject obj)
            {
                if (obj["properties"] is JObject properties)
                {
                    foreach (JProperty prop in properties.Properties())
                    {
                        foreach (string nameToken in Tokenize(prop.Name))
                            AddToIndex(keywordIndex, nameToken, tool);

                        if (prop.Value is JObject propObj && propObj["enum"] is JArray enumArray)
                        {
                            foreach (JToken enumVal in enumArray)
                            {
                                foreach (string enumToken in Tokenize(enumVal.ToString()))
                                    AddToIndex(keywordIndex, enumToken, tool);
                            }
                        }

                        ExtractAllProperties(prop.Value, tool, keywordIndex, depth + 1);
                    }
                }
                else
                {
                    foreach (JProperty prop in obj.Properties())
                    {
                        if (!string.Equals(prop.Name, "properties", StringComparison.Ordinal))
                            ExtractAllProperties(prop.Value, tool, keywordIndex, depth + 1);
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                    ExtractAllProperties(item, tool, keywordIndex, depth + 1);
            }
        }

        private static void AddToIndex(
            Dictionary<string, HashSet<MCPToolInfo>> index,
            string key,
            MCPToolInfo tool)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!index.TryGetValue(key, out HashSet<MCPToolInfo> set))
            {
                set = new HashSet<MCPToolInfo>();
                index[key] = set;
            }

            set.Add(tool);
        }

        // =====================================================
        // TOKENIZER (Türkçe duyarlý)
        // =====================================================

        private static readonly Regex CamelBoundary =
            new Regex(@"([a-z0-9])([A-Z])", RegexOptions.Compiled);

        private static readonly Regex NonAlphanumeric =
            new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);

        /// <summary>
        /// Türkçe karakterleri ASCII'ye katlar. Eski kod bunlarý doðrudan siliyordu:
        /// "oluþtur" -> "olu" + "tur", "kaydýrýcý" -> "kayd" + "r" + "c". Türkçe prompt'lar
        /// hiçbir zaman skor alamýyor, sistem her seferinde "ilk N tool" fallback'ine
        /// düþüyordu.
        /// </summary>
        private static string FoldToAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                switch (c)
                {
                    case 'ý': case 'Ý': case 'î': case 'Î': sb.Append('i'); break;
                    case 'þ': case 'Þ': sb.Append('s'); break;
                    case 'ð': case 'Ð': sb.Append('g'); break;
                    case 'ü': case 'Ü': sb.Append('u'); break;
                    case 'ö': case 'Ö': sb.Append('o'); break;
                    case 'ç': case 'Ç': sb.Append('c'); break;
                    case 'â': case 'Â': sb.Append('a'); break;
                    case 'û': case 'Û': sb.Append('u'); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }

        private static string NormalizeForCompare(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string folded = FoldToAscii(text).Replace('_', ' ').Replace('-', ' ');
            folded = CamelBoundary.Replace(folded, "$1 $2");

            // ToLowerInvariant kasýtlý: Türkçe kültürde ToLower('I') -> 'ý' olur ve
            // eþleþtirme bozulur. Türkçe karakterler zaten yukarýda katlandý.
            return NonAlphanumeric.Replace(folded.ToLowerInvariant(), " ").Trim();
        }

        private static List<string> Tokenize(string text)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            string normalized = NormalizeForCompare(text);

            foreach (string part in normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length > 1)
                    result.Add(part);
            }

            return result;
        }

        /// <summary>
        /// Türkçe prompt token'larýný, tool adlarýnda/açýklamalarýnda geçen Ýngilizce
        /// karþýlýklarýyla geniþletir. Tool adlarý Ýngilizce, kullanýcý Türkçe yazýyor;
        /// bu köprü olmadan ad/action indeksleri neredeyse hiç isabet etmiyor.
        /// </summary>
        private static HashSet<string> ExpandWithSynonyms(IEnumerable<string> tokens)
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string token in tokens)
            {
                expanded.Add(token);

                if (Synonyms.TryGetValue(token, out string[] mapped))
                {
                    foreach (string m in mapped)
                        expanded.Add(m);
                }
            }

            return expanded;
        }

        private static readonly Dictionary<string, string[]> Synonyms =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // --- UI elemanlarý ---
                { "buton",      new[] { "button", "ui" } },
                { "butonu",     new[] { "button", "ui" } },
                { "butonlar",   new[] { "button", "ui" } },
                { "dugme",      new[] { "button", "ui" } },
                { "dugmesi",    new[] { "button", "ui" } },
                { "panel",      new[] { "panel", "ui" } },
                { "paneli",     new[] { "panel", "ui" } },
                { "sekme",      new[] { "tab", "ui" } },
                { "sekmeler",   new[] { "tab", "ui" } },
                { "sekmeli",    new[] { "tab", "ui" } },
                { "kaydirici",  new[] { "slider", "ui" } },
                { "surgu",      new[] { "slider", "ui" } },
                { "anahtar",    new[] { "toggle", "ui" } },
                { "acmakapama", new[] { "toggle", "ui" } },
                { "kutucuk",    new[] { "toggle", "checkbox", "ui" } },
                { "etiket",     new[] { "label", "text", "ui" } },
                { "yazi",       new[] { "text", "label", "ui" } },
                { "metin",      new[] { "text", "label", "ui" } },
                { "giris",      new[] { "input", "field", "ui" } },
                { "alan",       new[] { "field", "input" } },
                { "kart",       new[] { "card", "ui" } },
                { "satir",      new[] { "row", "layout" } },
                { "sutun",      new[] { "column", "layout" } },
                { "menu",       new[] { "menu", "nav", "ui" } },
                { "gezinti",    new[] { "nav", "navigation", "ui" } },
                { "arayuz",     new[] { "ui", "canvas" } },
                { "arayuzu",    new[] { "ui", "canvas" } },
                { "tuval",      new[] { "canvas", "ui" } },
                { "duzen",      new[] { "layout", "rect" } },
                { "yerlesim",   new[] { "layout", "rect" } },
                { "boyut",      new[] { "size", "rect", "scale" } },
                { "renk",       new[] { "color" } },
                { "yazitipi",   new[] { "font", "text" } },

                // --- Unity kavramlarý ---
                { "nesne",      new[] { "gameobject", "object" } },
                { "nesnesi",    new[] { "gameobject", "object" } },
                { "obje",       new[] { "gameobject", "object" } },
                { "objesi",     new[] { "gameobject", "object" } },
                { "bilesen",    new[] { "component" } },
                { "bileseni",   new[] { "component" } },
                { "sahne",      new[] { "scene" } },
                { "sahneyi",    new[] { "scene" } },
                { "hiyerarsi",  new[] { "hierarchy", "gameobject" } },
                { "varlik",     new[] { "asset" } },
                { "onyapim",    new[] { "prefab", "asset" } },
                { "materyal",   new[] { "material", "asset" } },
                { "malzeme",    new[] { "material", "asset" } },
                { "kamera",     new[] { "camera", "gameobject" } },
                { "isik",       new[] { "light", "gameobject" } },
                { "golgelendirici", new[] { "shader", "material" } },
                { "konsol",     new[] { "console", "log", "read" } },
                { "hata",       new[] { "error", "console", "log" } },
                { "derleme",    new[] { "compile", "refresh", "console" } },
                { "paket",      new[] { "package" } },
                { "duzenleyici", new[] { "editor" } },

                // --- Eylemler ---
                { "olustur",    new[] { "create", "add", "manage" } },
                { "olusturma",  new[] { "create", "add", "manage" } },
                { "yarat",      new[] { "create", "manage" } },
                { "ekle",       new[] { "add", "create", "manage" } },
                { "yap",        new[] { "create", "manage" } },
                { "sil",        new[] { "delete", "remove", "manage" } },
                { "kaldir",     new[] { "delete", "remove", "manage" } },
                { "guncelle",   new[] { "update", "modify", "manage" } },
                { "degistir",   new[] { "update", "modify", "set", "manage" } },
                { "duzenle",    new[] { "update", "modify", "manage" } },
                { "ayarla",     new[] { "set", "update", "modify" } },
                { "tasi",       new[] { "move", "transform", "position" } },
                { "konumlandir", new[] { "position", "transform", "rect" } },
                { "bul",        new[] { "find", "search", "get" } },
                { "listele",    new[] { "list", "get", "read" } },
                { "oku",        new[] { "read", "get" } },
                { "kaydet",     new[] { "save", "write" } },

                // --- execute_code köprüsü (kritik) ---
                { "kod",        new[] { "code", "execute", "script" } },
                { "kodu",       new[] { "code", "execute", "script" } },
                { "betik",      new[] { "script", "code", "write" } },
                { "betigi",     new[] { "script", "code", "write" } },
                { "script",     new[] { "script", "code", "write" } },
                { "calistir",   new[] { "execute", "run", "code" } },
                { "yurut",      new[] { "execute", "run", "code" } },
                { "bagla",      new[] { "execute", "code", "reference" } },
                { "baglan",     new[] { "execute", "code", "reference" } },
                { "baglanti",   new[] { "execute", "code", "reference" } },
                { "referans",   new[] { "reference", "execute", "code" } },
                { "ata",        new[] { "assign", "set", "execute", "code" } },
                { "tikla",      new[] { "click", "onclick", "execute", "code", "button" } },
                { "tiklama",    new[] { "click", "onclick", "execute", "code", "button" } },
                { "olay",       new[] { "event", "execute", "code" } },
                { "davranis",   new[] { "behaviour", "script", "code", "execute" } }
            };

        /// <summary>Açýklama indekslemesinde gürültü yapan kelimeler. Kasýtlý olarak dar
        /// tutuldu; "get"/"set"/"read" gibi tool adlarýnda geçen kelimeler DIÞARIDA
        /// býrakýlmadý.</summary>
        private static readonly HashSet<string> Stopwords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "for", "with", "from", "this", "that", "into", "will",
                "are", "was", "has", "have", "not", "its", "you", "your", "can",
                "which", "when", "then", "than", "also", "any", "each", "such",
                "unity", "tool", "tools", "parameter", "parameters", "value", "values",
                "ile", "ve", "bir", "bu", "su", "icin", "olan", "gibi", "veya",
                "daha", "cok", "var", "yok", "ise", "ama", "kadar"
            };

        // =====================================================
        // SCHEMA RESOLUTION HELPERS
        // =====================================================

        /// <summary>
        /// Reflection tabanlý, genel amaçlý þema üretici. ParameterMetadata listesinden
        /// JSON Schema üretir; C# tip adlarýný JSON Schema tiplerine çevirir ve varsa
        /// enum/allowed-value bilgisini de yakalar.
        /// </summary>
        private JObject BuildInputSchema(
            ServiceToolMetadata tool,
            out List<string> requiredParams,
            out List<string> actions)
        {
            var properties = new JObject();
            requiredParams = new List<string>();
            actions = new List<string>();

            if (tool.Parameters != null)
            {
                foreach (var param in tool.Parameters)
                {
                    if (param == null || string.IsNullOrWhiteSpace(param.Name))
                        continue;

                    if (properties[param.Name] != null)
                    {
                        Debug.LogWarning($"[MCP Discovery] '{tool.Name}' tool'unda yinelenen " +
                                         $"parametre adý '{param.Name}' - ilki korunuyor.");
                        continue;
                    }

                    var propNode = new JObject
                    {
                        // param.Type "String"/"Int32"/"System.Boolean" gelebiliyor; JSON
                        // Schema'nýn beklediði "string"/"integer"/"boolean"a çeviriyoruz.
                        // Aksi hâlde model yanlýþ tipte argüman üretiyor.
                        ["type"] = MapToJsonSchemaType(param.Type),
                        ["description"] = param.Description ?? string.Empty
                    };

                    IReadOnlyList<string> enumValues = TryExtractEnumValues(param);
                    if (enumValues != null && enumValues.Count > 0)
                    {
                        propNode["enum"] = new JArray(enumValues);

                        if (string.Equals(param.Name, "action", StringComparison.OrdinalIgnoreCase))
                            actions.AddRange(enumValues);
                    }

                    properties[param.Name] = propNode;

                    if (param.Required)
                        requiredParams.Add(param.Name);
                }
            }

            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(requiredParams)
            };
        }

        private static string MapToJsonSchemaType(string clrType)
        {
            if (string.IsNullOrWhiteSpace(clrType))
                return "string";

            string t = clrType.Trim();

            if (t.EndsWith("[]", StringComparison.Ordinal) ||
                t.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("Array", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("IEnumerable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "array";
            }

            int lastDot = t.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < t.Length - 1)
                t = t.Substring(lastDot + 1);

            t = t.TrimEnd('?');

            switch (t.ToLowerInvariant())
            {
                case "string":
                case "char":
                case "guid":
                    return "string";

                case "int":
                case "int16":
                case "int32":
                case "int64":
                case "long":
                case "short":
                case "byte":
                case "uint":
                case "integer":
                    return "integer";

                case "float":
                case "single":
                case "double":
                case "decimal":
                case "number":
                    return "number";

                case "bool":
                case "boolean":
                    return "boolean";

                case "jarray":
                    return "array";

                case "object":
                case "jobject":
                case "jtoken":
                case "dictionary`2":
                    return "object";

                default:
                    return "string";
            }
        }

        private static readonly string[] EnumMemberCandidates =
        {
            "EnumValues", "AllowedValues", "Enum", "Options", "Choices", "ValidValues"
        };

        /// <summary>
        /// ParameterMetadata'da enum/allowed-values taþýyan bir üye varsa reflection ile
        /// okur. Böyle bir üye yoksa sessizce null döner - ToolParameterAttribute'a enum
        /// desteði eklendiði gün bu metot otomatik olarak çalýþmaya baþlar, çaðýran
        /// tarafta deðiþiklik gerekmez.
        /// </summary>
        private static IReadOnlyList<string> TryExtractEnumValues(object parameter)
        {
            if (parameter == null)
                return null;

            try
            {
                Type type = parameter.GetType();

                foreach (string memberName in EnumMemberCandidates)
                {
                    object raw = null;

                    PropertyInfo prop = type.GetProperty(memberName,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (prop != null && prop.CanRead)
                    {
                        raw = prop.GetValue(parameter);
                    }
                    else
                    {
                        FieldInfo field = type.GetField(memberName,
                            BindingFlags.Public | BindingFlags.Instance);

                        if (field != null)
                            raw = field.GetValue(parameter);
                    }

                    if (raw == null || raw is string)
                        continue;

                    if (raw is System.Collections.IEnumerable sequence)
                    {
                        var values = new List<string>();

                        foreach (object item in sequence)
                        {
                            string s = item?.ToString();
                            if (!string.IsNullOrWhiteSpace(s))
                                values.Add(s);
                        }

                        if (values.Count > 0)
                            return values;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MCP Discovery] Enum deðerleri okunamadý: " + ex.Message);
            }

            return null;
        }

        private static List<string> ExtractTopLevelRequired(JObject schema)
        {
            var result = new List<string>();

            if (schema?["required"] is JArray requiredArr)
            {
                foreach (JToken r in requiredArr)
                {
                    string value = r?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value))
                        result.Add(value);
                }
            }

            return result;
        }

        private static List<string> ExtractEnumActions(JObject schema)
        {
            var result = new List<string>();
            JObject actionNode = schema?["properties"]?["action"] as JObject;

            if (actionNode == null)
                return result;

            // Doðrudan enum
            if (actionNode["enum"] is JArray direct)
                AddEnumValues(direct, result);

            // oneOf / anyOf içine gömülmüþ enum
            foreach (string combinator in new[] { "oneOf", "anyOf", "allOf" })
            {
                if (actionNode[combinator] is JArray branches)
                {
                    foreach (JToken branch in branches)
                    {
                        if (branch?["enum"] is JArray nested)
                            AddEnumValues(nested, result);
                    }
                }
            }

            return result;
        }

        private static void AddEnumValues(JArray source, List<string> target)
        {
            foreach (JToken item in source)
            {
                string value = item?.ToString();

                if (!string.IsNullOrWhiteSpace(value) &&
                    !target.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(value);
                }
            }
        }

        // =====================================================
        // TOOL REGISTRATION
        // =====================================================

        private void RegisterTool(MCPToolInfo tool)
        {
            try
            {
                var metadata = new AIToolMetadata
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Category = tool.Group,
                    Actions = tool.Actions.ToList(),
                    InputSchema = tool.InputSchema,
                    Parameters = tool.RequiredParams ?? new List<string>(),
                    Enabled = true
                };

                AIToolRegistry.Register(metadata);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Discovery] '{tool.Name}' registry'ye eklenemedi: {ex.Message}");
            }
        }

        /// <summary>
        /// Yeniden keþiften önce registry'yi temizlemeye çalýþýr. ToolRegistry'de böyle bir
        /// metot yoksa sessizce geçer - bu durumda Register()'ýn isim üzerinden overwrite
        /// ettiðini varsayarýz.
        /// </summary>
        private static void TryResetRegistry()
        {
            try
            {
                Type registryType = typeof(AIToolRegistry);

                foreach (string methodName in new[] { "Clear", "Reset", "UnregisterAll", "RemoveAll" })
                {
                    MethodInfo method = registryType.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (method != null)
                    {
                        method.Invoke(null, null);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MCP Discovery] Registry sýfýrlanamadý: " + ex.Message);
            }
        }

        // =====================================================
        // CACHE
        // =====================================================

        public IReadOnlyList<MCPToolInfo> GetCachedTools()
        {
            return _index.Tools;
        }

        public bool TryGetTool(string name, out MCPToolInfo tool)
        {
            tool = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            tool = _index.Tools.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            return tool != null;
        }

        /// <summary>
        /// Önbelleði boþaltýr. Eskiden _lock.Wait() ile main thread'i blokluyordu; devam
        /// eden bir discovery varken çaðrýldýðýnda Editor kilitleniyordu. Artýk kilitsiz:
        /// boþ indeksi atomik olarak yayýnlar.
        /// </summary>
        public void Clear()
        {
            _index = ToolIndex.Empty;
            _isLoaded = false;
            _lastError = null;
            LastSchemaCharCount = 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Clear();
            OnToolsDiscovered = null;
            OnDiscoveryFailed = null;
            _lock.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(MCPToolDiscovery));
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private static readonly IReadOnlyList<MCPToolInfo> EmptyTools =
            new ReadOnlyCollection<MCPToolInfo>(new List<MCPToolInfo>());

        private static void SafeInvoke<T>(Action<T> handler, T arg)
        {
            if (handler == null)
                return;

            try
            {
                handler(arg);
            }
            catch (Exception ex)
            {
                // Bir abonenin patlamasý discovery'yi bozmasýn.
                Debug.LogError("[MCP Discovery] Event handler hatasý: " + ex);
            }
        }

        private void Log(string message)
        {
            if (EnableVerboseLogging)
                Debug.Log("[MCP Discovery] " + message);
        }

        // =====================================================
        // IMMUTABLE INDEX SNAPSHOT
        // =====================================================

        /// <summary>
        /// Keþif sonucunun deðiþmez anlýk görüntüsü. Tek referans atamasý ile yayýnlandýðý
        /// için okuyucular kilit almadan güvenle dolaþabilir.
        /// </summary>
        private sealed class ToolIndex
        {
            public static readonly ToolIndex Empty = new ToolIndex(
                new List<MCPToolInfo>(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                NewIndex(), NewIndex(), NewIndex());

            public readonly ReadOnlyCollection<MCPToolInfo> Tools;
            public readonly HashSet<string> Names;
            public readonly Dictionary<string, HashSet<MCPToolInfo>> Keywords;
            public readonly Dictionary<string, HashSet<MCPToolInfo>> Groups;
            public readonly Dictionary<string, HashSet<MCPToolInfo>> Actions;

            public ToolIndex(
                List<MCPToolInfo> tools,
                HashSet<string> names,
                Dictionary<string, HashSet<MCPToolInfo>> keywords,
                Dictionary<string, HashSet<MCPToolInfo>> groups,
                Dictionary<string, HashSet<MCPToolInfo>> actions)
            {
                Tools = new ReadOnlyCollection<MCPToolInfo>(tools);
                Names = names;
                Keywords = keywords;
                Groups = groups;
                Actions = actions;
            }
        }
    }

    // =====================================================
    // MCP TOOL MODEL
    // =====================================================

    [Serializable]
    public class MCPToolInfo
    {
        public string Name { get; private set; }
        public string Description = string.Empty;
        public string Group = "core";

        public JObject InputSchema = new JObject();
        public List<string> RequiredParams = new List<string>();

        private readonly HashSet<string> _actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> Actions => _actions;

        public MCPToolInfo(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tool adý boþ olamaz.", nameof(name));

            Name = name;
        }

        public void SetActions(IEnumerable<string> actions)
        {
            _actions.Clear();

            if (actions == null)
                return;

            foreach (string action in actions)
            {
                if (!string.IsNullOrWhiteSpace(action))
                    _actions.Add(action.Trim());
            }
        }

        public bool HasAction(string actionName)
        {
            return !string.IsNullOrWhiteSpace(actionName) && _actions.Contains(actionName);
        }

        public override string ToString()
        {
            return $"{Name} [{Group}] ({_actions.Count} actions)";
        }
    }
}
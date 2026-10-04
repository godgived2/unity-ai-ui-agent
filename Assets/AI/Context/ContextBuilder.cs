using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using AI.Services;
using AI.Core;

namespace AI.Context
{
    /// <summary>
    /// Bir context bölümünün önceliði.
    ///
    /// Ollama baðlamý taþtýðýnda prompt'un BAÞI kýrpýldýðý için, çýktý sýrasý önceliðin
    /// TERSÝDÝR: en kritik bölüm en SONA yazýlýr ve kýrpmadan sað çýkar.
    /// </summary>
    public enum ContextPriority
    {
        /// <summary>Görev boyunca deðiþmeyen arka plan bilgisi. Ýlk feda edilecek.</summary>
        Background = 0,

        /// <summary>Faydalý ama görev kritik deðil.</summary>
        Normal = 1,

        /// <summary>Model bu olmadan doðru karar veremez.</summary>
        High = 2,

        /// <summary>Asla kýrpýlmamalý (aktif derleme hatasý gibi).</summary>
        Critical = 3
    }

    [Flags]
    public enum ContextScope
    {
        None = 0,
        Unity = 1 << 0,
        Project = 1 << 1,
        Tools = 1 << 2,
        Hierarchy = 1 << 3,
        Console = 1 << 4,
        Scene = 1 << 5,
        Selection = 1 << 6,
        Package = 1 << 7,
        Assets = 1 << 8,

        /// <summary>
        /// Bir ReAct adýmý için varsayýlan.
        ///
        /// Tools ve Assets bilerek DIÞARIDA: resmî tool þemasýný MCPToolDiscovery
        /// üretiyor, varlýk envanterini ProjectContext özetliyor. Ýkisini de buraya
        /// eklemek ayný bilgiyi prompt'a iki kez sokar.
        /// </summary>
        AgentStep = Unity | Project | Package | Scene | Selection | Hierarchy | Console,

        All = ~0
    }

    /// <summary>
    /// Tek bir context üretim isteði.
    /// </summary>
    public class ContextRequest
    {
        /// <summary>Kullanýcýnýn orijinal komutu. Alaka filtrelerinde kullanýlýr.</summary>
        public string UserPrompt = string.Empty;

        /// <summary>ReAct döngüsündeki adým sýrasý. 0 = ilk adým (tam context gönderilir).</summary>
        public int StepIndex;

        public ContextScope Scope = ContextScope.AgentStep;

        /// <summary>
        /// Toplam karakter bütçesi. num_ctx'ten türetmek için <see cref="FromNumCtx"/>.
        /// </summary>
        public int CharBudget = 6000;

        /// <summary>Statik verilerin (proje taramasý, paketler) önbelleðini yok sayar.</summary>
        public bool ForceRefresh;

        public HierarchyCaptureOptions HierarchyOptions;
        public SceneCaptureOptions SceneOptions;
        public SelectionCaptureOptions SelectionOptions;

        /// <summary>
        /// Baðlam penceresinin bir kýsmýný context'e ayýrýr. Geri kalaný sistem prompt'u,
        /// tool þemasý, konuþma geçmiþi ve modelin cevabý için kalýr.
        ///
        /// charsPerToken = 3.5: Türkçe metin Ýngilizceden daha fazla token'a bölünür,
        /// bu yüzden 4 yerine ihtiyatlý bir deðer.
        /// </summary>
        public static ContextRequest FromNumCtx(
            int numCtx,
            string userPrompt = null,
            int stepIndex = 0,
            float contextShare = 0.30f)
        {
            int budget = Mathf.Max(1200, Mathf.RoundToInt(numCtx * contextShare * 3.5f));

            return new ContextRequest
            {
                UserPrompt = userPrompt ?? string.Empty,
                StepIndex = stepIndex,
                CharBudget = budget
            };
        }
    }

    /// <summary>Bütçeye göre yerleþtirilmiþ tek bir bölüm.</summary>
    public class ContextSection
    {
        public string Id;
        public ContextPriority Priority;
        public string Body;

        public int Length => Body?.Length ?? 0;
    }

    /// <summary>
    /// Tüm AI context verilerini toplar ve baðlam bütçesine sýðan tek bir metin üretir.
    ///
    /// Eski hâlindeki üç temel sorun giderildi:
    /// 1. Build() dokuz context alanýndan yalnýzca üçünü dolduruyordu; Hierarchy, Console,
    ///    Scene, Selection ve Package hiç doldurulmuyor, hiç render edilmiyordu.
    /// 2. Her çaðrýda tam proje taramasý yapýlýyordu (adým baþýna Editor donmasý).
    /// 3. Hiçbir yerde bütçe yoktu; context ne kadar büyürse o kadar gidiyordu.
    /// </summary>
    public class ContextBuilder
    {
        // =====================================
        // SERVICES (tembel çözümleme)
        // =====================================

        private UnityContextService unityContextService;
        private UnityProjectScanner projectScanner;
        private UnityToolDiscovery toolDiscovery;

        // =====================================
        // CACHE
        // =====================================

        /// <summary>Statik verilerin yeniden taranma aralýðý, saniye.</summary>
        public float StaticCacheSeconds { get; set; } = 120f;

        private UnityContext _cachedUnity;
        private ProjectContext _cachedProject;
        private PackageContext _cachedPackages;
        private double _staticCacheTime = double.NegativeInfinity;

        private HierarchyContext _previousHierarchy;
        private string _previousSceneSignature;

        public bool EnableVerboseLogging { get; set; }

        /// <summary>Son üretilen metnin karakter sayýsý. Bütçe teþhisi için.</summary>
        public int LastRenderedLength { get; private set; }

        public ContextBuilder()
        {
            ResolveServices();
        }

        /// <summary>
        /// Servisleri her çaðrýda yeniden çözmeyi dener. Eski kod bunlarý yalnýzca
        /// constructor'da çözüyordu: AIServiceLocator domain reload sonrasý henüz
        /// dolmamýþsa üçü de kalýcý olarak null kalýyor ve Build() sessizce boþ context
        /// döndürüyordu - hata bile vermeden.
        /// </summary>
        private void ResolveServices()
        {
            try
            {
                if (unityContextService == null)
                    unityContextService = AIServiceLocator.Get<UnityContextService>();

                if (projectScanner == null)
                    projectScanner = AIServiceLocator.Get<UnityProjectScanner>();

                if (toolDiscovery == null)
                    toolDiscovery = AIServiceLocator.Get<UnityToolDiscovery>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Servis cozumleme hatasi: " + ex.Message);
            }
        }

        // =====================================
        // BUILD
        // =====================================

        /// <summary>Geriye dönük uyumlu tam yapý.</summary>
        public AIContext Build()
        {
            return Build(new ContextRequest { Scope = ContextScope.All });
        }

        public AIContext Build(ContextRequest request)
        {
            request = request ?? new ContextRequest();
            ResolveServices();

            var context = new AIContext();

            bool staticExpired = request.ForceRefresh ||
                                 Time.realtimeSinceStartupAsDouble - _staticCacheTime > StaticCacheSeconds;

            // ---------- UNITY ----------
            if (Has(request.Scope, ContextScope.Unity))
            {
                if (staticExpired || _cachedUnity == null)
                    _cachedUnity = BuildUnityContext() ?? new UnityContext();

                // Derleme / Play durumu HER adimda taze olmali: onbellege alinamaz,
                // cunku agent'in "simdi tool cagirmali miyim" karari buna bagli.
                _cachedUnity.EnrichEditorState();
                context.Unity = _cachedUnity;
            }

            // ---------- PROJECT ----------
            if (Has(request.Scope, ContextScope.Project))
            {
                if (staticExpired || _cachedProject == null)
                    _cachedProject = BuildProjectContext();

                context.Project = _cachedProject ?? new ProjectContext();
            }

            // ---------- PACKAGES ----------
            if (Has(request.Scope, ContextScope.Package))
            {
                if (staticExpired || _cachedPackages == null)
                    _cachedPackages = PackageContext.Capture();

                context.Package = _cachedPackages ?? new PackageContext();
            }

            if (staticExpired)
                _staticCacheTime = Time.realtimeSinceStartupAsDouble;

            // ---------- TOOLS ----------
            if (Has(request.Scope, ContextScope.Tools))
                BuildToolContext(context);

            // ---------- SCENE ----------
            if (Has(request.Scope, ContextScope.Scene))
            {
                try
                {
                    context.Scene = SceneContextBuilder.Build(
                        request.SceneOptions ?? SceneCaptureOptions.Default);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AI Context] Sahne okunamadi: " + ex.Message);
                }
            }

            // ---------- SELECTION ----------
#if UNITY_EDITOR
            if (Has(request.Scope, ContextScope.Selection))
            {
                try
                {
                    context.Selection = SelectionContext.Capture(
                        request.SelectionOptions ?? SelectionCaptureOptions.Default);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AI Context] Secim okunamadi: " + ex.Message);
                }
            }
#endif

            // ---------- HIERARCHY ----------
            if (Has(request.Scope, ContextScope.Hierarchy))
            {
                HierarchyCaptureOptions options = request.HierarchyOptions
                    ?? (LooksLikeUiTask(request.UserPrompt)
                        ? HierarchyCaptureOptions.UiFocused()
                        : HierarchyCaptureOptions.Default);

                try
                {
                    context.Hierarchy = HierarchyContext.CaptureActiveScene(options);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AI Context] Hiyerarsi yakalanamadi: " + ex.Message);
                }
            }

            // ---------- CONSOLE ----------
#if UNITY_EDITOR
            if (Has(request.Scope, ContextScope.Console))
            {
                try
                {
                    context.Console = ConsoleContextRecorder.Snapshot();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AI Context] Konsol okunamadi: " + ex.Message);
                }
            }
#endif

            // ---------- ASSETS ----------
            // AssetContext bilerek doldurulmuyor: envanteri ProjectContext ozetliyor.
            // Ikisi birden prompt'a girerse ayni liste iki kez gider. AssetContext
            // arama/lookup katmani olarak kullanilir, disaridan doldurulur.

            return context;
        }

        private UnityContext BuildUnityContext()
        {
            if (unityContextService == null)
                return null;

            try
            {
                var unity = unityContextService.GetContext();

                if (unity == null)
                    return null;

                return new UnityContext
                {
                    ProjectName = unity.ProductName,
                    UnityVersion = unity.UnityVersion,
                    Platform = unity.Platform,
                    IsEditor = unity.IsEditor,
                    Time = unity.Time
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Unity context alinamadi: " + ex.Message);
                return null;
            }
        }

        private ProjectContext BuildProjectContext()
        {
            if (projectScanner == null)
                return null;

            try
            {
                var project = projectScanner.ScanProject();

                if (project == null)
                    return null;

                var result = new ProjectContext
                {
                    Assets = project.Assets,
                    Scripts = project.Scripts,
                    Scenes = project.Scenes,
                    Prefabs = project.Prefabs,
                    Folders = project.Folders
                };

                result.UpdateCounts();
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Proje taranamadi: " + ex.Message);
                return null;
            }
        }

        private void BuildToolContext(AIContext context)
        {
            if (toolDiscovery == null)
                return;

            try
            {
                var tools = toolDiscovery.DiscoverTools();

                if (tools == null)
                    return;

                foreach (var tool in tools)
                {
                    if (tool == null)
                        continue;

                    context.Tools.AddTool(new UnityToolContextInfo
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = tool.Parameters
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Tool kesfi basarisiz: " + ex.Message);
            }
        }

        // =====================================
        // BUDGETED PROMPT RENDERING
        // =====================================

        /// <summary>
        /// Prompt'a gömülecek nihai context metnini üretir.
        ///
        /// Bütçe daðýtýmý öncelik sýrasýna göre yapýlýr ve bölümler ÖNCELÝÐÝN TERSÝ
        /// sýrayla yazýlýr: Ollama baðlamý taþarsa prompt'un baþý kýrpýldýðý için,
        /// kritik bölümler sonda durur ve hayatta kalýr.
        /// </summary>
        public string BuildPromptContext(ContextRequest request)
        {
            request = request ?? new ContextRequest();

            AIContext context = Build(request);
            var sections = new List<ContextSection>();

            // ---------- Background: gorev boyunca degismez ----------
            if (Has(request.Scope, ContextScope.Unity) && context.Unity != null)
            {
                sections.Add(new ContextSection
                {
                    Id = "environment",
                    Priority = ContextPriority.Background,
                    Body = Safe(() => context.Unity.Render())
                });
            }

            if (Has(request.Scope, ContextScope.Package) && context.Package != null &&
                context.Package.PackageCount > 0)
            {
                sections.Add(new ContextSection
                {
                    Id = "packages",
                    Priority = ContextPriority.Background,
                    Body = Safe(() => context.Package.Render())
                });
            }

            if (Has(request.Scope, ContextScope.Project) && context.Project != null)
            {
                sections.Add(new ContextSection
                {
                    Id = "project",
                    Priority = ContextPriority.Background,

                    // Kullanicinin komutu odak anahtari olarak veriliyor: eslesen script
                    // yollari listeleniyor, boylece model var olan bir script'i yeniden
                    // yazmak yerine ona referans veriyor.
                    Body = Safe(() => context.Project.Render(0, request.UserPrompt))
                });
            }

            // ---------- Normal ----------
            if (Has(request.Scope, ContextScope.Tools) && context.Tools != null &&
                context.Tools.ToolCount > 0)
            {
                sections.Add(new ContextSection
                {
                    Id = "tools",
                    Priority = ContextPriority.Normal,
                    Body = Safe(() => context.Tools.GetSummary())
                });
            }

            // ---------- High: sahne durumu ----------
            if (Has(request.Scope, ContextScope.Scene) && context.Scene != null)
            {
                // Sahne basligi degismediyse tekrar gondermeye gerek yok.
                string signature = Safe(() => context.Scene.ComputeSignature());
                bool changed = request.StepIndex == 0 ||
                               !string.Equals(signature, _previousSceneSignature, StringComparison.Ordinal);

                if (changed)
                {
                    sections.Add(new ContextSection
                    {
                        Id = "scene",
                        Priority = ContextPriority.High,
                        Body = Safe(() => context.Scene.Render())
                    });
                }

                _previousSceneSignature = signature;
            }

            if (Has(request.Scope, ContextScope.Selection) && context.Selection != null &&
                context.Selection.HasSelection)
            {
                // Token basina en yuksek sinyal: kullanici bir obje sectiyse "buna buton
                // ekle" komutu belirsizlikten cikar.
                sections.Add(new ContextSection
                {
                    Id = "selection",
                    Priority = ContextPriority.High,
                    Body = Safe(() => context.Selection.Render())
                });
            }

            if (Has(request.Scope, ContextScope.Hierarchy) && context.Hierarchy != null)
            {
                // Delta: ilk adimda tam agac, sonraki adimlarda yalnizca degisenler.
                // 150 adimlik donguce en buyuk tasarruf kalemi budur.
                bool useDelta = request.StepIndex > 0 && _previousHierarchy != null;

                sections.Add(new ContextSection
                {
                    Id = "hierarchy",
                    Priority = ContextPriority.High,
                    Body = Safe(() => useDelta
                        ? context.Hierarchy.RenderDelta(_previousHierarchy)
                        : context.Hierarchy.Render())
                });

                _previousHierarchy = context.Hierarchy;
            }

            // ---------- Critical: aktif hatalar ----------
            if (Has(request.Scope, ContextScope.Console) && context.Console != null &&
                context.Console.Messages.Count > 0)
            {
                bool hasErrors = context.Console.HasErrors;

                sections.Add(new ContextSection
                {
                    Id = "console",
                    Priority = hasErrors ? ContextPriority.Critical : ContextPriority.Normal,
                    Body = Safe(() => context.Console.Render(
                        maxErrors: 5,
                        maxWarnings: hasErrors ? 0 : 2))
                });
            }

            return Compose(sections, request.CharBudget);
        }

        /// <summary>
        /// Bölümleri bütçeye sýðdýrýr ve birleþtirir.
        ///
        /// Daðýtým: kritikten baþlayarak yer ayrýlýr, kullanýlmayan pay düþük
        /// önceliklilere kalýr. Böylece konsol temizken hiyerarþi daha çok yer alýr,
        /// hata çýktýðýnda hata öne geçer.
        /// </summary>
        private string Compose(List<ContextSection> sections, int totalBudget)
        {
            if (sections.Count == 0)
                return string.Empty;

            if (totalBudget <= 0)
                totalBudget = int.MaxValue;

            sections.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            int remaining = totalBudget;
            var kept = new List<ContextSection>();

            foreach (ContextSection section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Body))
                    continue;

                if (remaining <= 0)
                {
                    LogVerbose($"'{section.Id}' butce doldugu icin atlandi.");
                    continue;
                }

                int share = section.Priority == ContextPriority.Critical
                    ? remaining
                    : Mathf.Max(200, Mathf.RoundToInt(remaining * PriorityShare(section.Priority)));

                string body = ContextFormat.Truncate(section.Body, share);

                if (body.Length < section.Body.Length)
                    LogVerbose($"'{section.Id}' {section.Body.Length} -> {body.Length} karakter kirpildi.");

                section.Body = body;
                remaining -= body.Length;
                kept.Add(section);
            }

            // Yazim sirasi: onceligin TERSI. En kritik bolum en sonda kalsin ki baglam
            // tasip prompt'un basi kirpildiginda hayatta kalsin.
            kept.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            var sb = new StringBuilder();

            foreach (ContextSection section in kept)
            {
                sb.Append("### ").AppendLine(section.Id.ToUpperInvariant());
                sb.AppendLine(section.Body.TrimEnd());
                sb.AppendLine();
            }

            string result = sb.ToString().TrimEnd();
            LastRenderedLength = result.Length;

            LogVerbose($"Context: {kept.Count} bolum, {result.Length} karakter " +
                       $"(~{result.Length / 4} token, butce {totalBudget}).");

            return result;
        }

        private static float PriorityShare(ContextPriority priority)
        {
            switch (priority)
            {
                case ContextPriority.High: return 0.55f;
                case ContextPriority.Normal: return 0.40f;
                default: return 0.30f;
            }
        }

        // =====================================
        // HELPERS
        // =====================================

        /// <summary>
        /// Yeni bir göreve baþlarken çaðýrýn: delta karþýlaþtýrmasý ve konsol tamponu
        /// sýfýrlanýr, önceki görevin hatalarý modele karýþmaz.
        /// </summary>
        public void ResetSession()
        {
            _previousHierarchy = null;
            _previousSceneSignature = null;
            _staticCacheTime = double.NegativeInfinity;

#if UNITY_EDITOR
            ConsoleContextRecorder.Reset();
#endif
        }

        private static bool Has(ContextScope scope, ContextScope flag)
        {
            return (scope & flag) == flag;
        }

        private static readonly string[] UiKeywords =
        {
            "ui", "canvas", "panel", "button", "buton", "dugme", "düðme",
            "tab", "sekme", "toggle", "slider", "kaydirici", "kaydýrýcý",
            "input", "giris", "giriþ", "label", "etiket", "menu", "menü",
            "nav", "card", "kart", "layout", "duzen", "düzen", "arayuz", "arayüz"
        };

        private static bool LooksLikeUiTask(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            string lower = ContextFormat.LowerInvariant(prompt);

            foreach (string keyword in UiKeywords)
            {
                if (lower.Contains(keyword))
                    return true;
            }

            return false;
        }

        private static string Safe(Func<string> producer)
        {
            try
            {
                return producer() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Bolum uretilemedi: " + ex.Message);
                return string.Empty;
            }
        }

        private void LogVerbose(string message)
        {
            if (EnableVerboseLogging)
                Debug.Log("[AI Context] " + message);
        }
    }

    // =====================================
    // FULL AI CONTEXT
    // =====================================

    [Serializable]
    public class AIContext
    {
        public UnityContext Unity = new UnityContext();
        public ProjectContext Project = new ProjectContext();
        public HierarchyContext Hierarchy = new HierarchyContext();
        public SceneContext Scene = new SceneContext();
        public SelectionContext Selection = new SelectionContext();
        public ConsoleContext Console = new ConsoleContext();
        public PackageContext Package = new PackageContext();
        public AssetContext Assets = new AssetContext();
        public ToolContext Tools = new ToolContext();

        /// <summary>
        /// Geriye dönük uyumlu özet. Eskiden yalnýzca Unity + Project + Tools yazýyordu;
        /// hiyerarþi, sahne, seçim ve konsol - yani modelin gerçekten ihtiyaç duyduðu
        /// bilgiler - hiç görünmüyordu.
        /// </summary>
        public string GetSummary()
        {
            var sb = new StringBuilder();

            Append(sb, "ENVIRONMENT", Unity?.Render());
            Append(sb, "PACKAGES", Package != null && Package.PackageCount > 0 ? Package.Render() : null);
            Append(sb, "PROJECT", Project?.Render());
            Append(sb, "TOOLS", Tools != null && Tools.ToolCount > 0 ? Tools.GetSummary() : null);
            Append(sb, "SCENE", Scene?.Render());
            Append(sb, "SELECTION",
                Selection != null && Selection.HasSelection ? Selection.Render() : null);
            Append(sb, "HIERARCHY", Hierarchy?.Render());
            Append(sb, "CONSOLE",
                Console != null && Console.Messages.Count > 0 ? Console.GetSummary() : null);

            return sb.ToString().TrimEnd();
        }

        private static void Append(StringBuilder sb, string header, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return;

            sb.Append("### ").AppendLine(header);
            sb.AppendLine(body.TrimEnd());
            sb.AppendLine();
        }
    }
}
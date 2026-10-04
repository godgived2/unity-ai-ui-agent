using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using AI.Client;
using AI.Executor;
using MCPForUnity.Editor.AI;
using AI.Parser;

using Newtonsoft.Json.Linq;

using UnityEditor;
using UnityEngine;

namespace AI.Agent
{
    /// <summary>
    /// Model tarafýndan üretilen script dosyalarýnýn yaþam döngüsünü yöneten watchdog.
    ///
    /// ================== NEDEN AYRI BÝR SINIF ==================
    /// Bir .cs dosyasý import edildiði anda Unity derleme baþlatýr ve DOMAIN RELOAD
    /// yapar. Domain reload, çalýþan tüm async durumunu ve tüm örnek (instance)
    /// alanlarýný yok eder - OllamaToolAgent örneði o anda ölür.
    ///
    /// Bunun iki sonucu var:
    ///
    /// 1) Promptdaki "yaz -> derlemeyi bekle -> baðla" protokolü, import görev
    ///    sýrasýnda yapýlýrsa ASLA çalýþamaz. Ajan ikinci adýma gelemeden ölür.
    ///    Bu yüzden import GÖREV BÝTENE KADAR ERTELENÝR.
    ///
    /// 2) Rollback bilgisi bir örnek alanýnda (List&lt;string&gt;) tutulamaz - reload onu
    ///    siler. Bu yüzden bekleyen dosyalar EditorPrefs'e yazýlýr ve reload sonrasý
    ///    [InitializeOnLoad] ile geri okunur.
    ///
    /// AKIÞ:
    ///   Görev sýrasýnda : model dosyayý diske yazar, import ETMEZ (engellenir)
    ///   Görev bitiminde : ScheduleImportAndVerify() -> ImportAsset -> derleme + reload
    ///   Reload sonrasý  : statik kurucu -> VerifyAfterReload()
    ///                     derleme baþarýlý  -> bekleyen liste temizlenir
    ///                     derleme baþarýsýz -> dosyalar SÝLÝNÝR, proje kurtarýlýr
    /// ==========================================================
    /// </summary>
    [InitializeOnLoad]
    internal static class GeneratedScriptWatchdog
    {
        private const string PendingKey = "AI.Agent.OllamaToolAgent.PendingGeneratedScripts";
        private const string Separator = "|";

        static GeneratedScriptWatchdog()
        {
            // Statik kurucu her domain reload'da çalýþýr. delayCall, editor'ün
            // kendini toparlamasýný bekler; isCompiling burada hâlâ true olabilir.
            EditorApplication.delayCall += VerifyAfterReload;
        }

        internal static bool HasPending => GetPending().Count > 0;

        internal static List<string> GetPending()
        {
            string raw = EditorPrefs.GetString(PendingKey, string.Empty);

            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static void SetPending(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                EditorPrefs.DeleteKey(PendingKey);
                return;
            }

            EditorPrefs.SetString(PendingKey, string.Join(Separator, paths));
        }

        /// <summary>
        /// Bir dosyayý "yazýldý, henüz doðrulanmadý" listesine ekler. Domain reload'a
        /// dayanýklý olmasý için EditorPrefs'e yazýlýyor.
        /// </summary>
        internal static void Track(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var pending = GetPending();

            if (pending.Contains(path))
                return;

            pending.Add(path);
            SetPending(pending);

            Debug.Log($"[GeneratedScriptWatchdog] Tracking '{path}' - it will be imported and verified after the task finishes.");
        }

        /// <summary>
        /// Görev bittikten SONRA çaðrýlýr. Bekleyen dosyalarý import eder; bu, derlemeyi
        /// ve domain reload'u tetikler. Reload sonrasý doðrulama statik kurucudan devam
        /// eder.
        ///
        /// delayCall kullanýlmasý þart: bu metot ajanýn async akýþýndan çaðrýlýyor ve
        /// import doðrudan burada yapýlýrsa reload çaðýraný ortasýnda öldürür.
        /// </summary>
        internal static void ScheduleImportAndVerify()
        {
            var pending = GetPending();

            if (pending.Count == 0)
                return;

            EditorApplication.delayCall += () =>
            {
                Debug.Log($"[GeneratedScriptWatchdog] Importing {pending.Count} generated script(s). Unity will now compile - the editor may freeze briefly.");

                foreach (string path in pending)
                {
                    if (System.IO.File.Exists(path))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            };
        }

        /// <summary>
        /// Domain reload sonrasý derleme sonucunu denetler. Derleme kýrýldýysa üretilen
        /// dosyalarý siler - aksi halde proje kilitli kalýr ve kullanýcýnýn dosyayý
        /// iþletim sisteminden elle silmesi gerekir.
        /// </summary>
        private static void VerifyAfterReload()
        {
            var pending = GetPending();

            if (pending.Count == 0)
                return;

            if (EditorApplication.isCompiling)
            {
                // Derleme sürüyor: bir sonraki tick'te tekrar bak.
                EditorApplication.delayCall += VerifyAfterReload;
                return;
            }

            if (!EditorUtility.scriptCompilationFailed)
            {
                Debug.Log($"[GeneratedScriptWatchdog] {pending.Count} generated script(s) compiled cleanly:\n  " + string.Join("\n  ", pending));
                SetPending(null);
                return;
            }

            Debug.LogError(
                $"[GeneratedScriptWatchdog] The project has compile errors after generating {pending.Count} script(s). " +
                "Rolling them back automatically so the editor stays usable.");

            RollbackAll("compile errors detected after import");
        }

        /// <summary>
        /// Bekleyen tüm üretilmiþ scriptleri siler. Proje kilitlendiðinde chat penceresi
        /// açýlmayabilir; statik olduðu için bu yol her zaman çalýþýr.
        /// </summary>
        internal static void RollbackAll(string reason)
        {
            var pending = GetPending();

            if (pending.Count == 0)
            {
                Debug.Log("[GeneratedScriptWatchdog] Nothing to roll back.");
                return;
            }

            Debug.LogWarning($"[GeneratedScriptWatchdog] Rolling back {pending.Count} script(s): {reason}");

            bool anyDeleted = false;

            foreach (string path in pending)
            {
                try
                {
                    // AssetDatabase.DeleteAsset .meta dosyasýný da temizler ve import
                    // kayýtlarýný tutarlý býrakýr. Dosya henüz import edilmemiþse
                    // (AssetDatabase tanýmýyorsa) diskten doðrudan siliniyor.
                    if (AssetDatabase.DeleteAsset(path))
                    {
                        anyDeleted = true;
                        Debug.LogWarning($"[GeneratedScriptWatchdog] Deleted '{path}'.");
                        continue;
                    }

                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);

                        string meta = path + ".meta";
                        if (System.IO.File.Exists(meta))
                        {
                            System.IO.File.Delete(meta);
                        }

                        anyDeleted = true;
                        Debug.LogWarning($"[GeneratedScriptWatchdog] Deleted '{path}' from disk.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[GeneratedScriptWatchdog] Could not delete '{path}': {ex.Message}. " +
                        "If Unity is stuck in safe mode, delete this file manually from Explorer/Finder and reopen the project.");
                }
            }

            SetPending(null);

            if (!anyDeleted)
                return;

            // Refresh gerekli ama çaðýranýn yýðýnýnda DEÐÝL - reload bu metodu çaðýran
            // akýþý öldürebilir.
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                Debug.Log("[GeneratedScriptWatchdog] Rollback complete - project refreshed.");
            };
        }

        /// <summary>
        /// Editor menüsünden elle kurtarma. Proje derlenmiyorsa chat penceresi
        /// açýlmayabilir; bu menü öðesi statik olduðu için yine de çalýþýr.
        /// </summary>
        [MenuItem("Tools/AI Agent/Rollback Generated Scripts")]
        private static void RollbackFromMenu()
        {
            if (!HasPending)
            {
                EditorUtility.DisplayDialog("AI Agent", "There are no generated scripts waiting to be rolled back.", "OK");
                return;
            }

            var pending = GetPending();

            bool confirmed = EditorUtility.DisplayDialog(
                "Rollback generated scripts",
                $"Delete {pending.Count} script(s) generated by the AI agent?\n\n" + string.Join("\n", pending),
                "Delete", "Cancel");

            if (confirmed)
            {
                RollbackAll("manual rollback from the Tools menu");
            }
        }
    }

    /// <summary>
    /// Yerel LLM'i (Ollama) MCP araçlarýna baðlayan ReAct ajaný.
    ///
    /// SORUMLULUK SINIRI: bu sýnýf modelin NE YAPACAÐINA karar vermez - o iþ prompt
    /// katmanýnýn (PromptBuilder + PromptRules*). Bu sýnýf modelin çýktýsýný
    /// çalýþtýrýlabilir hale getirir, çalýþtýrýr, sonucu geri besler ve modelin
    /// kendini ya da projeyi bozmasýný engeller.
    ///
    /// ÜÇ SAVUNMA KATMANI:
    ///   1. Sanitizasyon  - düzeltilebilir hatalarý sessizce düzeltir (eksik 'action',
    ///                      legacy alignment, ters anchor, UI objesinde scale).
    ///   2. Doðrulama     - düzeltilemeyen hatalarý çalýþtýrmadan reddeder ve modele
    ///                      neyin yanlýþ olduðunu söyler.
    ///   3. Döngü kesici  - model týkandýðýnda (tekrar, hata, boþ nudge) görevi erken
    ///                      bitirir. Sýnýrsýz hiçbir "devam et" yolu OLMAMALI - her
    ///                      geri itmenin bir sayacý var.
    ///
    ///
    /// ============ BU SÜRÜMDE DÜZELTÝLEN ÜÇ AYRIÞMA ============
    /// Bu dosya, UiMacroExpander ve PromptRulesUI ile üç noktada ayrýþmýþtý. Hepsi
    /// SESSÝZ kayýptý - derleme hatasý vermiyor, sadece yetenek kayboluyordu.
    ///
    /// A. ÞEMA BEÞ MACRO EKSÝKTÝ - EN BÜYÜÐÜ.
    ///    create_ui_image, create_ui_button_bar, create_ui_slider,
    ///    create_ui_progress_bar ve create_ui_input kodda vardý ve PromptRulesUI
    ///    onlarý tanýtýyordu, ama BuildMacroToolSchemaJson hiç üretmiyordu.
    ///    PromptRulesUnity.AppendSchemaAuthority modele "þema otoritedir" dediði
    ///    için model bu beþini güvenle kullanamýyordu. Gerçek testte ses kaydýrýcýsý
    ///    bu yüzden kurulamadý: model create_ui_row'a control='slider' verdi, çünkü
    ///    create_ui_slider'ýn gerçekten var olduðuna dair þemada bir kanýt yoktu.
    ///
    /// B. VisualMacros LÝSTESÝ AYNI BEÞÝNÝ SAYMIYORDU.
    ///    TrackMacroProgress yalnýzca listedeki macro'larda _sawVisualComponentThisTask
    ///    iþaretliyor. Sadece slider ve input'tan oluþan bir ekran kurulduðunda ajan
    ///    "hiç görsel eleman eklemedin" diye modeli geri itiyordu - üstelik ekran
    ///    doluydu.
    ///
    /// C. SyncFromSceneAsync ÖLÜ KODDU.
    ///    UiMacroExpander'a eklendi ama hiçbir yerden çaðrýlmýyordu. Yerleþim kaydý
    ///    sahneyle senkron kalmadýðý için ikinci çalýþtýrmada her çaðrý "çakýþýyor"
    ///    cevabý alýyor, model kaçýþ yolu olarak allowOverlap kullanýyor ve ekran üst
    ///    üste biniyordu. Artýk her görev baþýnda çaðrýlýyor.
    ///
    /// Ayrýca: ValidateToolCommand'ýn hata mesajý elle yazýlmýþ eski bir macro
    /// listesi taþýyordu; artýk UiMacroExpander.MacroToolNames'ten üretiliyor.
    /// ==========================================================
    /// </summary>
    public class OllamaToolAgent : IDisposable
    {
        /// <summary>
        /// Varsayýlan model. Donaným 8 GB VRAM ise qwen3:8b, daha fazlaysa qwen3:14b
        /// uygun. Çaðýran istediðini geçebilir - bu yalnýzca varsayýlan.
        /// </summary>
        private const string DEFAULT_MODEL = "qwen3:14b";

        // 80'den yükseltildi: gerçek testte 40+ elemanlý bir ayar ekraný 80 adýmý
        // doldurup yarým kaldý ("Max steps reached"). Otomatik Canvas/EventSystem
        // kurulumu bu bütçeden düþmüyor, ama modelin hatalý denemeleri düþüyor.
        private const int MAX_AGENT_STEPS = 150;

        // Geçmiþte tutulan mesaj sayýsý.
        //
        // ============ 40'TAN 80'E - GERÇEK ARIZA ============
        // Her adým geçmiþe ÝKÝ mesaj ekliyor (çaðrý + sonuç), yani 40 mesaj yalnýzca
        // 20 adým demekti. Sekmeli bir ekran ~28 adým sürüyor. 20. adýmdan sonra
        // AddMessageToHistory EN ESKÝ mesajlarý siliyordu - yani ilk kartýn satýrlarýný
        // kurduðu adýmlar geçmiþten tamamen düþüyordu.
        //
        // Sonuç konsolda görüldü: iþ bittikten sonra model ilk satýrlarý "eksik" sanýp
        // yeniden kurmaya çalýþtý, koruma dört kez engelledi, görev "Stuck" diye
        // kesildi ve ~3 dakika boþa gitti.
        //
        // PromptBuilder.FitHistory tam da bunu önlemek için geçmiþin BAÞINI koruyor -
        // ama baþ, PromptBuilder'a ulaþmadan önce burada siliniyordu.
        //
        // 80 güvenli: num_ctx artýk 32768, FormatHistory eski mesajlarý tek satýra
        // indiriyor ve PromptBuilder geçmiþe ayrýlan payý ayrýca sýnýrlýyor.
        //
        // ASIL KORUMA bu sayý deðil, _buildManifest: geçmiþ ne kadar kýrpýlýrsa
        // kýrpýlsýn, kurulan her eleman orada kalýyor ve her adýmda modele gösteriliyor.
        // ====================================================
        private const int MAX_MEMORY_MESSAGES = 80;

        private const int TOOL_STEP_NUM_PREDICT = 1024;

        // MCP keþfinden kaç GERÇEK araç þemaya alýnacak (macro'lar bu sayýya dahil
        // deðil - onlar her zaman ekleniyor).
        //
        // ============ NEDEN 8'DEN 6'YA ÝNDÝRÝLDÝ ============
        // GERÇEK ÖLÇÜM: macro þemasý geniþledikten sonra prompt 18966 token'a çýktý
        // ve 17105'lik bütçeyi 1861 token aþtý. Þema ZORUNLU bir blok olduðu için
        // PromptBuilder onu düþüremiyor; bütün tasarým rehberini feda etti, yine
        // sýðmadý.
        //
        // Bu projede bir UI görevi pratikte ÜÇ gerçek araç kullanýyor:
        // manage_gameobject, manage_components ve execute_code. Sekiz araç sunmak,
        // model hiç çaðýrmayacak olsa bile her adýmda þema maliyeti ödemek demek.
        // Altý, o üçü ve keþfin öne çýkardýðý iki yedeði kapsayacak kadar geniþ.
        //
        // Model gerçekten eksik bir araç yüzünden týkanýrsa bu sayý yükseltilebilir -
        // ama o zaman num_ctx de yükseltilmeli, yoksa taþma geri gelir.
        // ====================================================
        private const int MAX_DISCOVERED_TOOLS = 6;

        // Art arda kaç "bu zaten yapýldý" durumundan sonra görevin bittiðini kabul
        // edeceðimiz. Hem raw çaðrýlardaki engellemeleri hem macro'larýn "already exists"
        // sonuçlarýný kapsýyor.
        private const int MAX_CONSECUTIVE_DUPLICATE_BLOCKS = 4;

        // Model AYNI hedef+component'i art arda tekrar tekrar "iyileþtirmeye" çalýþabilir -
        // her seferinde deðerleri hafifçe deðiþtirerek (anchorMin'i 0.001 kaydýrmak gibi).
        // Bu, MAX_CONSECUTIVE_DUPLICATE_BLOCKS'u hiç tetiklemez çünkü JSON'lar birebir
        // ayný deðil - ama gerçek testte 40. adýmdan 83. adýma kadar SettingsWindow'un
        // RectTransform/Image'ý üst üste "düzeltilmeye" çalýþýldý ve hiçbir yeni eleman
        // eklenmedi, ekranda hiçbir deðiþiklik olmadý.
        private const int MAX_CONSECUTIVE_SAME_TARGET_EDITS = 6;

        // Art arda kaç BAÞARISIZ adýmdan sonra pes edeceðimiz.
        //
        // NEDEN EKLENDÝ: doðrulama hatalarý, güvenlik engellemeleri ve macro
        // baþarýsýzlýklarý hiçbir sayaca dokunmuyordu - sadece 'continue' ediliyordu.
        // Model geçersiz bir tool adýnda ýsrar ederse ya da bir macro'nun parent'ý
        // bulunamýyorsa, sistem 150 adým boyunca ayný hatayý tekrarlayýp hiçbir þey
        // üretmeden bitiyordu. Duplicate korumasý vardý, HATA korumasý yoktu.
        private const int MAX_CONSECUTIVE_ERRORS = 5;

        // Model bitirmeye çalýþtýðýnda boþ konteyner uyarýsý en fazla bu kadar kez
        // verilir. Sýnýrsýz olsaydý, model gerçekten yapamadýðý bir durumda sonsuz
        // döngüye girebilirdi.
        private const int MAX_EMPTY_CONTAINER_NUDGES = 3;

        /// <summary>
        /// Ýstenip de ekrana konmamýþ metinler için en fazla kaç kez geri itilir.
        ///
        /// 2 bilinçli olarak düþük: bu denetim isteðin kendisinden okunduðu için nadiren
        /// yanýlýyor ama yanýldýðýnda her itiþ bir model turu (~40 saniye) harcýyor.
        /// </summary>
        private const int MAX_MISSING_TEXT_NUDGES = 2;

        // "UI eksik" geri itmesinin sýnýrý.
        //
        // NEDEN EKLENDÝ: boþ konteyner nudge'ýnýn sayacý vardý, bunun yoktu.
        // _standaloneInputModuleConfirmed bir kez false kalýrsa (EventSystem kurulumu
        // baþarýsýz olduysa) GetIncompleteUiReason() her seferinde ayný gerekçeyi
        // döndürüyor ve model hiçbir zaman bitiremiyordu - kalan adým bütçesi, modelin
        // düzeltemeyeceði bir sorun yüzünden yanýyordu.
        private const int MAX_INCOMPLETE_UI_NUDGES = 3;

        // Bir görevde kaç script dosyasý yazýlabileceði. Her dosya derleme riski
        // demek; bir istek için birden fazlasýna ihtiyaç duyulmasý neredeyse her
        // zaman modelin daðýldýðýný gösterir.
        private const int MAX_SCRIPTS_PER_TASK = 2;

        // Tek bir konteynerin altýna kaç doðrudan çocuk eklenebileceði.
        //
        // NEDEN EKLENDÝ - GERÇEK TEST BULGUSU: model "her kartta üç satýr olsun" diye
        // açýkça istenmiþ bir ekranda 'AyarlarRow1'den 'AyarlarRow11'e kadar satýr
        // üretmeye devam etti ve durmuyordu. Üretilen satýrlarýn içeriði de birbirinin
        // kopyasýydý ("Gizli Mod" üç kez arka arkaya).
        //
        // Mevcut korumalarýn HÝÇBÝRÝ bunu yakalayamýyordu:
        //   - Duplicate imza korumasý: her satýrýn adý FARKLI (Row7 != Row8), yani JSON
        //     birebir ayný deðil.
        //   - Ayný-hedef korumasý: her seferinde farklý bir hedef, zincir hiç kurulmuyor.
        //   - Hata sayacý: çaðrýlar BAÞARILI, teknik olarak bir hata yok.
        // Yani model teknik olarak "yeni eleman üretiyor", anlamsal olarak döngüde.
        //
        // Bu sýnýr o boþluðu kapatýyor: gerçek bir ayar kartýnda 8'den fazla satýr
        // olmasý pratikte görülmüyor; bu sayýyý aþmak modelin planý kaybettiðinin
        // güvenilir iþareti.
        private const int MAX_CHILDREN_PER_CONTAINER = 8;

        private const string AUTO_CANVAS_NAME = "MainCanvas";
        private const string AUTO_EVENT_SYSTEM_NAME = "EventSystem";

        // Hem Unity'nin hem UiMacroExpander'ýn "bu zaten mevcut" durumunda kullandýðý
        // ifade. Tek yerde tanýmlý olmasý önemli - üç ayrý yerde bu metne bakýyoruz.
        private const string ALREADY_EXISTS_MARKER = "already exists";

        // Modelin script yazmasýna izin verilen TEK klasör. PromptRulesUI'deki
        // <script_authoring> kurallarý ve UiMacroExpander.GeneratedScriptFolder da bu
        // yolu söylüyor - üçü eþleþmeli.
        private const string GENERATED_SCRIPT_FOLDER = "Assets/UI/Generated";

        // Modelin sprite/görsel üretmesine izin verilen klasör. <icon_authoring>
        // kurallarý da bu yolu söylüyor.
        private const string GENERATED_SPRITE_FOLDER = "Assets/UI/Sprites";

        private static readonly string[] ReferenceFieldNames = { "parent", "target", "path" };

        private static readonly string[] UiVisualComponents =
            { "Image", "RawImage", "Text", "TextMeshProUGUI", "Button", "Toggle", "Slider", "InputField", "Dropdown", "Scrollbar" };

        private static readonly string[] CanvasComponents =
            { "Canvas", "CanvasScaler", "GraphicRaycaster" };

        // 'alignment' parametresi alan macro'lar. Legacy alignment düzeltmesi bunlara da
        // uygulanmalý - eskiden sadece manage_components'a uygulanýyordu ve macro
        // yolundan gelen "UpperLeft" sessizce geçiyordu.
        //
        // create_ui_button_bar EKLENDÝ: bar seviyesindeki alignment her butona miras
        // geçiyor, yani oradaki tek bir legacy deðer bütün barý bozuyordu.
        private static readonly string[] AlignmentBearingMacros =
            { "create_ui_button", "create_ui_label", "create_ui_button_bar", "create_ui_image" };

        // Konteyner sayýlan macro'lar: bunlarýn içi doldurulmazsa UI yarým kalmýþ olur.
        //
        // create_ui_button_bar BÝLÝNÇLÝ OLARAK YOK: kendi butonlarýný ayný çaðrýda
        // kuruyor, yani boþ kalmasý mümkün deðil.
        private static readonly string[] ContainerMacros =
            { "create_ui_panel", "create_ui_card" };

        // Görsel eleman üreten TÜM macro'lar.
        //
        // ============ DÜZELTÝLDÝ - BEÞ MACRO EKSÝKTÝ ============
        // Bu liste TrackMacroProgress'te _sawVisualComponentThisTask ve
        // _sawChildUnderCanvasThisTask bayraklarýný belirliyor; o bayraklar da
        // GetIncompleteUiReason'ýn modeli geri itip itmeyeceðini.
        //
        // create_ui_image, create_ui_button_bar, create_ui_slider,
        // create_ui_progress_bar ve create_ui_input listede YOKTU. Sonuç: yalnýzca
        // bu macro'lardan oluþan bir ekran kurulduðunda ajan "A Canvas exists but you
        // never added any visual element" diyerek modeli geri itiyordu - ekran
        // doluyken. Üç nudge boþa gidiyor, sonra görev eksik bitiriliyordu.
        // =======================================================
        private static readonly string[] VisualMacros =
        {
            "create_ui_panel", "create_ui_card", "create_ui_button", "create_ui_button_bar",
            "create_ui_nav_item", "create_ui_label", "create_ui_image", "create_ui_toggle",
            "create_ui_row", "create_ui_slider", "create_ui_progress_bar", "create_ui_input"
        };

        // execute_code'un þemasýnda OLMAYAN ama modelin diðer tool'lardan alýþkanlýkla
        // eklediði parametreler. Gerçek testte 'target':'MainCanvas' gönderdi - o tool
        // böyle bir alan tanýmýyor, çaðrý boþa gitti.
        private static readonly string[] ExecuteCodeUnknownFields =
            { "target", "parent", "name", "componentType", "component_type", "properties", "anchorMin", "anchorMax" };

        // Bir execute_code kodunun dosya YAZDIÐINI gösteren ifadeler.
        private static readonly string[] FileWriteMarkers =
            { "WriteAllText", "WriteAllLines", "WriteAllBytes", "StreamWriter", "File.Create" };

        // Bir execute_code kodunda GÖRÜLMEMESÝ gereken ifadeler ve modele verilecek
        // gerekçe. Gerekçenin anlamlý olmasý önemli: model neden engellendiðini
        // bilmezse ayný þeyi baþka bir yazýmla tekrar dener.
        private static readonly Dictionary<string, string> ForbiddenCodePatterns =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["AssetDatabase.DeleteAsset"] = "deleting assets is never permitted",
                ["AssetDatabase.MoveAsset"] = "moving assets is never permitted",
                ["Directory.Delete"] = "deleting folders is never permitted",
                ["File.Delete"] = "deleting files is never permitted - failed scripts are rolled back automatically",
                ["EditorApplication.Exit"] = "closing the editor is never permitted",
                ["Application.Quit"] = "quitting the application is never permitted",

                // KRÝTÝK: Refresh ve ImportAsset domain reload tetikler. Domain reload
                // bu ajanýn kendisini öldürür - async döngü ortada kesilir, chat
                // penceresi kapanýr ve yazýlmýþ script derlenmemiþ halde diskte kalýr.
                //
                // Eski sürümde bu ikisi hiç engellenmiyordu; prompt "çaðýrma" diyordu
                // ama kural sadece tavsiyeydi ve model çaðýrdýðýnda sistem ölüyordu.
                ["AssetDatabase.Refresh"] = "it triggers a domain reload that kills this agent mid-task - the agent imports your file automatically after the task finishes, so you never need to refresh",
                ["AssetDatabase.ImportAsset"] = "it triggers a compile and domain reload that kills this agent mid-task - just write the file, the agent imports it for you after the task finishes",
                ["RequestScriptCompilation"] = "forcing a recompile kills this agent mid-task",
                ["RequestScriptReload"] = "forcing a script reload kills this agent mid-task",

                ["PlayerSettings."] = "changing project settings is never permitted",
                ["EditorBuildSettings"] = "changing build settings is never permitted",
                ["EditorSettings."] = "changing editor settings is never permitted",
                ["EditorApplication.isPlaying"] = "entering or exiting play mode from a tool call is never permitted",
            };

        // Model, TMP'nin TextAlignmentOptions enum'u yerine legacy Text'in TextAnchor
        // isimlerini kullanýyor. Gerçek testte "UpperLeft" gönderdi ve Unity reddetti:
        // component eklendi ama hizalama varsayýlanda kaldý (sessiz kalite kaybý).
        private static readonly Dictionary<string, string> LegacyAlignmentMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UpperLeft"] = "TopLeft",
                ["UpperCenter"] = "Top",
                ["UpperRight"] = "TopRight",
                ["MiddleLeft"] = "Left",
                ["MiddleCenter"] = "Center",
                ["MiddleRight"] = "Right",
                ["LowerLeft"] = "BottomLeft",
                ["LowerCenter"] = "Bottom",
                ["LowerRight"] = "BottomRight",
            };

        private readonly MCPUnityClient _client;
        private readonly MCPToolDiscovery _discovery;
        private readonly OllamaClient _ollama;
        private readonly ResponseParser _parser;

        private readonly List<OllamaMessage> _conversationHistory = new List<OllamaMessage>();

        private readonly HashSet<string> _executedCallSignatures = new HashSet<string>();

        private readonly HashSet<string> _createdObjectNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Konteyner olabilecek objeler (panel/kart içerik alaný) ve bunlardan hangilerinin
        // gerçekten çocuk aldýðý. Gerçek testte model 4 içerik panelinden 2'sini doldurup
        // diðer 2'sini boþ býrakýp durdu - hangi panelin boþ kaldýðýný takip edemiyordu.
        private readonly HashSet<string> _containerCandidates =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Her konteynerin altýna bu görevde kaç doðrudan çocuk eklendiði.
        // MAX_CHILDREN_PER_CONTAINER korumasýnýn veri kaynaðý.
        private readonly Dictionary<string, int> _childCountPerParent =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _containersWithChildren =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Bu GÖREVDE yazýlan script dosyalarý. Kalýcý takip GeneratedScriptWatchdog'da
        // (EditorPrefs) - burasý sadece görev içi sayým ve kullanýcý mesajý için.
        private readonly List<string> _scriptsWrittenThisTask = new List<string>();

        /// <summary>
        /// Bu görevde kurulan her elemanýn kaydý: (ebeveyn, ad). Oluþturulma sýrasýyla.
        ///
        /// ============ NEDEN AYRI BÝR LÝSTE - GEÇMÝÞ YETMÝYOR ============
        /// Konuþma geçmiþi KIRPILIYOR - önce burada MAX_MEMORY_MESSAGES ile, sonra
        /// PromptBuilder'da token payýyla. Uzun bir görevde ilk adýmlar er ya da geç
        /// düþüyor ve model onlarý kurduðunu unutuyor.
        ///
        /// Bu liste hiç kýrpýlmýyor. Her adýmda FormatHistory'nin SONUNA kompakt bir
        /// aðaç olarak ekleniyor: modelin "neyi kurdum, ne eksik" sorusunun cevabý,
        /// geçmiþin hangi kýsmýnýn hayatta kaldýðýndan baðýmsýz olarak hep orada.
        ///
        /// Boyutu küçük: ebeveyn baþýna tek satýr. 30 elemanlý bir ekran ~1.500
        /// karakter, yani ~400 token.
        /// =================================================================
        /// </summary>
        private readonly List<KeyValuePair<string, string>> _buildManifest =
            new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Bu görevde kurulan kartlar. Bir satýrýn ebeveyni olarak kart adý verildiðinde
        /// UiMacroExpander onu içerik alanýna yönlendiriyor; inþa listesi de satýrý
        /// gerçekte durduðu yerde, '&lt;Kart&gt;Content' altýnda göstermeli.
        /// </summary>
        private readonly HashSet<string> _cardNamesThisTask =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ýnþa listesinin üst sýnýrý - bozuk bir döngü listeyi sýnýrsýz büyütmesin.</summary>
        private const int MAX_MANIFEST_ENTRIES = 250;

        private int _emptyContainerNudges;

        /// <summary>Bu görevin isteði - istenen metinleri çýkarmak için saklanýyor.</summary>
        private string _currentUserPrompt;

        /// <summary>
        /// Bu görevde EKRANA konmuþ her metin: etiketler, baþlýklar, deðerler, buton
        /// yazýlarý, ipuçlarý, açýklamalar.
        ///
        /// ============ NEDEN VAR - UNUTULAN ELEMANLAR ============
        /// GERÇEK TEST, ÜÇ TUR ÜST ÜSTE: operatör konsolunda "Silent tracking" satýrý,
        /// "OPERATIONAL" ve "BATTERY-04 / SECTOR NORTH" etiketleri hiç kurulmadý. Tek
        /// bir hata yoktu - ne çakýþma, ne reddedilen çaðrý, ne kýrmýzý satýr. Model
        /// iþ listesinin bir kýsmýný atladý ve görevi bitmiþ saydý.
        ///
        /// Boþ konteyner korumasý bunu yakalayamýyor, çünkü konteynerler doluydu;
        /// eksik olan onlarýn ÝÇÝNDEKÝ bir satýrdý.
        ///
        /// Kullanýcý istediði her metni zaten týrnak içinde yazýyor. Görev bitmeden
        /// önce o týrnaklarý isteðin içinden çýkarýp burada biriken listeyle
        /// karþýlaþtýrmak, "ne eksik" sorusunu modele sormadan, deterministik olarak
        /// cevaplýyor.
        /// =======================================================
        /// </summary>
        private readonly HashSet<string> _producedTexts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _missingTextNudges;
        private int _incompleteUiNudges;
        private int _consecutiveErrors;

        private bool _canvasReady;
        private bool _eventSystemReady;
        private bool _eventSystemSetupAttempted;
        private bool _standaloneInputModuleConfirmed;
        private bool _sawVisualComponentThisTask;
        private bool _sawRectTransformSetThisTask;
        private bool _sawChildUnderCanvasThisTask;

        // RunSetupCallAsync'in son sonucu - EnsureEventSystemExistsAsync'in "zaten var"
        // hatasýný gerçek bir baþarýsýzlýktan ayýrt edebilmesi için tutuluyor.
        private string _lastSetupCallResult;

        // Art arda kaç adýmýn "zaten yapýldý" olarak engellendiðini sayar.
        private int _consecutiveDuplicateBlocks;

        // Son dokunulan (target, componentType) çifti ve art arda kaçýncý kez
        // dokunulduðu. Farklý bir hedefe geçilirse sýfýrlanýr.
        private string _lastEditTargetKey;
        private int _consecutiveSameTargetEdits;

        private string _toolSchema = "";
        private bool _enableVerboseLogging = true;
        private bool _disposed;

        public bool EnableVerboseLogging
        {
            get => _enableVerboseLogging;
            set
            {
                _enableVerboseLogging = value;
                if (_ollama != null)
                {
                    _ollama.EnableVerboseLogging = value;
                }
            }
        }

        /// <summary>
        /// Doðrulanmayý bekleyen, model tarafýndan üretilmiþ script dosyalarý.
        /// Domain reload'a dayanýklý kaynaktan okunuyor.
        /// </summary>
        public IReadOnlyList<string> GeneratedScriptFiles => GeneratedScriptWatchdog.GetPending();

        public bool HasGeneratedScripts => GeneratedScriptWatchdog.HasPending;

        // =====================================
        // CONSTRUCTOR
        // =====================================

        public OllamaToolAgent(string modelName = DEFAULT_MODEL)
        {
            _client = MCPUnityClient.GetClient();
            _discovery = new MCPToolDiscovery(_client);

            _ollama = new OllamaClient(
                string.IsNullOrWhiteSpace(modelName) ? DEFAULT_MODEL : modelName,
                enableVerboseLogging: _enableVerboseLogging
            );

            _parser = new ResponseParser();
        }

        // =====================================
        // INITIALIZE
        // =====================================

        public async Task<bool> Initialize()
        {
            ThrowIfDisposed();

            bool connected = await _client.Connect();

            if (!connected)
            {
                Debug.LogError("[OllamaToolAgent] MCP connection failed.");
                return false;
            }

            MCPExecutor.Initialize(_client);

            _toolSchema = MergeMacroSchema(await _discovery.BuildAgentSchemaAsync("general Unity task", MAX_DISCOVERED_TOOLS));

            if (string.IsNullOrWhiteSpace(_toolSchema))
            {
                Debug.LogWarning("[OllamaToolAgent] WARNING: Tool Schema is empty! Check MCP Tool Discovery.");
            }
            else if (EnableVerboseLogging)
            {
                Debug.Log("=================================");
                Debug.Log("    MCP DYNAMIC TOOLS READY       ");
                LogSafe(_toolSchema);
                Debug.Log("=================================");
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogWarning(
                    "[OllamaToolAgent] The project currently has compile errors. Tool calls will still run, but results may be unreliable. " +
                    $"If a generated script is the cause, check '{GENERATED_SCRIPT_FOLDER}' or use Tools > AI Agent > Rollback Generated Scripts.");
            }

            return true;
        }

        // =====================================
        // ASK (MULTI-STEP REACT LOOP WITH MEMORY)
        // =====================================

        public async Task<string> Ask(string userPrompt)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(userPrompt))
                return string.Empty;

            ResetTaskState();

            // Ýstenen metinleri çýkarmak için saklanýyor - bkz. _producedTexts.
            _currentUserPrompt = userPrompt;

            AddMessageToHistory("user", userPrompt);

            // TÜM DÖNGÜ KORUMA ALTINDA.
            //
            // NEDEN: Ollama zaman aþýmý, MCP kopmasý ya da beklenmedik bir JSON
            // istisnasý görevi ortada öldürüyordu. Kullanýcý hiçbir özet görmüyordu ve
            // - çok daha kötüsü - o adýmda yazýlmýþ bir .cs dosyasý import edilmeden
            // diskte kalýyordu. Artýk her çýkýþ yolu finally'den geçiyor.
            try
            {
                string answer = await RunAgentLoopAsync(userPrompt);

                // KARTLARI ÝÇERÝÐE KÜÇÜLT. Görev nasýl biterse bitsin (task_complete,
                // stuck, adým sýnýrý) burasý tek ortak çýkýþ noktasý. Kendi hatasýný
                // kendisi yutuyor: bir rötuþun baþarýsýzlýðý kullanýcýnýn cevabýný
                // "internal error" mesajýna çevirmemeli.
                try
                {
                    await UiMacroExpander.CompactCardsAsync();
                }
                catch (Exception compactEx)
                {
                    Debug.LogWarning($"[OllamaToolAgent] Card compaction skipped: {compactEx.Message}");
                }

                return AppendScriptNoticeIfNeeded(answer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OllamaToolAgent] Task failed with an exception: {ex}");

                return "The task stopped because of an internal error: " + ex.Message +
                       "\n\nAnything already created in the scene is still there. " +
                       "Check the Console for details, then try again with a simpler request.";
            }
            finally
            {
                // Görev nasýl biterse bitsin: yazýlan scriptler þimdi import edilir.
                // Derleme ve domain reload bundan SONRA olur, yani bu akýþý öldürmez.
                if (_scriptsWrittenThisTask.Count > 0)
                {
                    GeneratedScriptWatchdog.ScheduleImportAndVerify();
                }
            }
        }

        /// <summary>
        /// Script yazýldýysa kullanýcýya ne olacaðýný açýkça söyler. Bu bilgi olmadan
        /// kullanýcý, editörün neden bir anda derlemeye baþladýðýný ve component'in
        /// neden henüz eklenmediðini anlayamaz.
        /// </summary>
        private string AppendScriptNoticeIfNeeded(string answer)
        {
            if (_scriptsWrittenThisTask.Count == 0)
                return answer;

            var sb = new StringBuilder(answer);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(_scriptsWrittenThisTask.Count == 1
                ? "A behaviour script was generated:"
                : $"{_scriptsWrittenThisTask.Count} behaviour scripts were generated:");

            foreach (string path in _scriptsWrittenThisTask)
            {
                sb.AppendLine($"  - {path}");
            }

            sb.AppendLine();
            sb.AppendLine("Unity is compiling it now. If compilation fails, the file is deleted automatically and the project stays usable.");
            sb.AppendLine("Once compilation finishes, ask me to attach it - the component cannot be added until the type exists.");

            return sb.ToString();
        }

        private async Task<string> RunAgentLoopAsync(string userPrompt)
        {
            // ============ YERLEÞÝM KAYDINI SAHNEYLE EÞÝTLE ============
            // UiMacroExpander'ýn LayoutRegistry'si STATÝK: Unity oturumu boyunca
            // yaþýyor ve sahneyle kendiliðinden senkron kalmýyor. Ýki yönde de
            // bozuluyordu:
            //
            //   - Kullanýcý Hierarchy'den bir paneli elle sildi  -> kayýt o bandý hâlâ
            //     dolu sanýyor ve oraya yerleþmek isteyen her çaðrýyý reddediyor.
            //   - Ajan ikinci kez çalýþtýrýldý  -> kayýtta birinci turun dikdörtgenleri
            //     duruyor, model her denemede "çakýþýyor" cevabý alýyor, sonunda kaçýþ
            //     yolu olarak allowOverlap kullanýyor ve ekran üst üste biniyor.
            //
            // Bu çaðrý kaydý SÝLMÝYOR, sahnedeki gerçek anchor deðerlerine göre
            // YENÝDEN KURUYOR. Tek bir execute_code turu, görev baþýna bir kez.
            //
            // Baþarýsýz olursa görev yine devam ediyor - kayýt elindekini koruyor.
            await UiMacroExpander.SyncFromSceneAsync(_createdObjectNames);

            // Accent rengi kullanýcýnýn isteðinden okunuyor ("amber accent",
            // "turkuaz vurgu"). SyncFromSceneAsync onu temizlediði için SONRA çaðrýlmalý.
            UiMacroExpander.SeedAccentFromRequest(userPrompt);

            // Sahnede önceki komutlardan kalan UI varsa, bu görev onlarýn üstüne
            // inþa etmeli - yeniden yaratmaya çalýþmamalý.
            await ImportExistingSceneObjectsAsync();

            string realToolSchema = await _discovery.BuildAgentSchemaAsync(userPrompt, MAX_DISCOVERED_TOOLS);
            _toolSchema = MergeMacroSchema(realToolSchema);

            if (string.IsNullOrWhiteSpace(_toolSchema))
            {
                Debug.LogWarning("[OllamaToolAgent] WARNING: Tool Schema is empty for current prompt!");
            }

            int currentStep = 0;
            string lastExecutionResult = string.Empty;

            while (currentStep < MAX_AGENT_STEPS)
            {
                currentStep++;

                if (EnableVerboseLogging)
                {
                    Debug.Log($"========== AGENT STEP {currentStep}/{MAX_AGENT_STEPS} ==========");
                }

                string systemPrompt = PromptBuilder.BuildFromSchemaWithHistory(
                    _toolSchema,
                    FormatHistory(),
                    userPrompt,
                    PromptBuilder.AgentMode.Strict
                );

                if (EnableVerboseLogging)
                {
                    LogSafe(systemPrompt);
                }

                string rawResponse = await _ollama.Ask(
                    systemPrompt,
                    numPredict: TOOL_STEP_NUM_PREDICT,
                    forceJson: true,
                    temperature: 0.1f);

                if (EnableVerboseLogging)
                {
                    Debug.Log($"========== QWEN RAW RESPONSE (STEP {currentStep}) ==========");
                    LogSafe(rawResponse);
                }

                string cleanedResponse = CleanResponse(rawResponse);
                JObject command = _parser.ParseToolCommand(cleanedResponse);

                // ============ MODEL BÝTÝRMEK ÝSTÝYOR ============
                // GERÇEK ARIZA - HER SEKMELÝ TESTÝN "STUCK" ÝLE BÝTMESÝNÝN SEBEBÝ:
                // Bitiþ eskiden yalnýzca 'command == null' ile algýlanýyordu, yani
                // modelin DÜZ METÝN yazmasýyla. Ama her adým forceJson:true ile
                // çaðrýlýyor ve Ollama bu durumda çýktýyý dilbilgisi seviyesinde JSON'a
                // KÝLÝTLÝYOR. Model düz metin yazamaz - fiziksel olarak mümkün deðil.
                //
                // Prompt'taki "iþ bitince düz metinle cevap ver" talimatý bu yüzden
                // uygulanamazdý. Bitirmek isteyen model elindeki tek hamleyi yapýyordu:
                // en tanýdýk JSON'u, son araç çaðrýsýný tekrarlamak. Koruma dört kez
                // engelliyor, görev "Stuck" diye kesiliyor, ~3 dakika boþa gidiyordu.
                //
                // Artýk JSON içinde açýk bir bitiþ yolu var: {"type":"task_complete"}.
                // Eski yol da (düz metin, tip alaný olmayan JSON) hâlâ kabul ediliyor.
                // ================================================
                bool completionSignal = IsCompletionSignal(command, cleanedResponse, out string completionSummary);

                if (command == null || completionSignal)
                {
                    string finishGuard = await BuildFinishGuardNoticeAsync();

                    if (finishGuard != null)
                    {
                        AddMessageToHistory("assistant", cleanedResponse);
                        AddMessageToHistory("user", $"[System Notice]: {finishGuard}");
                        continue;
                    }

                    AddMessageToHistory("assistant", cleanedResponse);

                    return completionSignal
                        ? await BuildCompletionAnswerAsync(userPrompt, completionSummary, lastExecutionResult)
                        : cleanedResponse;
                }

                string toolName = command["type"]?.ToString();
                bool isMacro = UiMacroExpander.IsMacroTool(toolName);

                bool macroNeedsCanvasCheck = isMacro &&
                    !string.Equals(toolName, "ensure_canvas", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(toolName, "ensure_event_system", StringComparison.OrdinalIgnoreCase) &&
                    // write_script sahneye hiç dokunmuyor - sýrf script yazmak için
                    // Canvas kurmak gereksiz ve sahneyi kirletir.
                    !string.Equals(toolName, "write_script", StringComparison.OrdinalIgnoreCase);

                if ((macroNeedsCanvasCheck || (!isMacro && NeedsCanvas(command))) && !_canvasReady)
                {
                    await EnsureCanvasExistsAsync();

                    // Canvas kurulamadýysa devam etmenin anlamý yok: her UI çaðrýsý
                    // görünmeyen bir objeyle sonuçlanýr ve kullanýcý boþ bir Game view
                    // görür.
                    if (!_canvasReady)
                    {
                        return await FinishStuckTask(userPrompt, _lastSetupCallResult,
                            "The Canvas could not be created, so no UI element can be rendered. Check the MCP connection and the Unity Console.");
                    }
                }

                StepOutcome outcome = isMacro
                    ? await ExecuteMacroStepAsync(command, toolName, cleanedResponse, currentStep, userPrompt)
                    : await ExecuteRawStepAsync(command, toolName, cleanedResponse, currentStep, userPrompt, lastExecutionResult);

                if (outcome.TaskFinished)
                {
                    return outcome.FinalAnswer;
                }

                if (outcome.SkipStep)
                {
                    // Art arda hata korumasý. Doðrulama hatalarý, güvenlik engellemeleri
                    // ve macro baþarýsýzlýklarý buraya düþüyor.
                    if (outcome.CountsAsError)
                    {
                        _consecutiveErrors++;

                        if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            return await FinishStuckTask(userPrompt, lastExecutionResult,
                                $"Stopped after {_consecutiveErrors} consecutive failed steps - the model kept producing calls that could not be executed. Last problem: {outcome.ErrorSummary}");
                        }
                    }

                    continue;
                }

                _consecutiveErrors = 0;
                lastExecutionResult = outcome.ExecutionResult;

                // KRÝTÝK: Canvas'ý MODEL kendisi kurduysa (MarkCanvasIfCreated
                // _canvasReady'yi true yapar) EnsureCanvasExistsAsync hiç çalýþmýyordu -
                // dolayýsýyla EventSystem hiç oluþmuyordu ve butonlar tamamen saðýr
                // kalýyordu. Artýk Canvas hangi yoldan hazýr olursa olsun, EventSystem'i
                // burada garantiye alýyoruz.
                if (_canvasReady && !_eventSystemSetupAttempted)
                {
                    await EnsureEventSystemExistsAsync();
                }

                if (EnableVerboseLogging)
                {
                    Debug.Log($"========== RESULT (STEP {currentStep}) ==========");
                    LogSafe(outcome.ExecutionResult);
                }

                AddMessageToHistory("assistant", command.ToString(Newtonsoft.Json.Formatting.None));
                AddMessageToHistory("user", $"[Tool Execution Result]: {outcome.ExecutionResult}");

                if (outcome.WroteScript)
                {
                    // Modelin ayný görevde AddComponent denemesini engelle. Tip
                    // derlenmeden var olmaz ve derleme bu görev bittikten sonra olur.
                    AddMessageToHistory("user",
                        "[System Notice]: The script file was written to disk. Unity has NOT compiled it yet - the type does not exist, " +
                        "so you CANNOT add it as a component in this task. Do not call execute_code to attach it, do not check whether the type exists, " +
                        "and never call AssetDatabase.Refresh or AssetDatabase.ImportAsset. " +
                        "Compilation is handled automatically after this task ends. " +
                        "Finish the remaining visual work, then respond with plain text and mention that the script was created.");
                }
            }

            string summarizePrompt = PromptBuilder.BuildFinalResponsePrompt(userPrompt, "Max steps reached", lastExecutionResult);
            string finalAnswer = CleanResponse(await _ollama.Ask(summarizePrompt));

            AddMessageToHistory("assistant", finalAnswer);
            return finalAnswer;
        }

        /// <summary>
        /// Bir adýmýn sonucu. Eskiden bu bilgi bool'lar ve erken return'lerle döngünün
        /// içine daðýlmýþtý; macro ve raw yollarý farklý sayaçlara dokunuyordu ve
        /// birinde olan koruma diðerinde yoktu (macro yolunda hata sayacý hiç yoktu).
        /// Tek tipe indirilince iki yol da ayný korumalardan geçiyor.
        /// </summary>
        private struct StepOutcome
        {
            public bool SkipStep;
            public bool CountsAsError;
            public bool TaskFinished;
            public bool WroteScript;
            public string ExecutionResult;
            public string FinalAnswer;
            public string ErrorSummary;

            public static StepOutcome Error(string summary) =>
                new StepOutcome { SkipStep = true, CountsAsError = true, ErrorSummary = summary };

            /// <summary>
            /// Adým atlandý ama bu bir HATA deðil (tamamlanmýþ iþin tekrarý gibi).
            /// Hata sayacýna dokunmaz - kendi sayacý vardýr.
            /// </summary>
            public static StepOutcome Skip() =>
                new StepOutcome { SkipStep = true, CountsAsError = false };

            public static StepOutcome Finished(string answer) =>
                new StepOutcome { TaskFinished = true, FinalAnswer = answer };

            public static StepOutcome Ran(string result, bool wroteScript = false) =>
                new StepOutcome { ExecutionResult = result, WroteScript = wroteScript };
        }

        // =====================================
        // STEP EXECUTION - MACRO PATH
        // =====================================

        private async Task<StepOutcome> ExecuteMacroStepAsync(
            JObject command, string toolName, string cleanedResponse, int currentStep, string userPrompt)
        {
            // Macro parametrelerindeki legacy alignment deðeri de düzeltilmeli.
            // Eskiden sadece manage_components'a bakýlýyordu; create_ui_button /
            // create_ui_label üzerinden gelen "UpperLeft" sessizce geçiyor ve etiket
            // varsayýlan hizalamada kalýyordu.
            FixLegacyAlignmentValue(command);

            // Görev baþýna script sýnýrý write_script için de geçerli. Bir istek için
            // ikiden fazla dosyaya ihtiyaç duyulmasý neredeyse her zaman modelin
            // daðýldýðýný gösterir ve her dosya ayrý bir derleme riski demek.
            if (string.Equals(toolName, "write_script", StringComparison.OrdinalIgnoreCase) &&
                _scriptsWrittenThisTask.Count >= MAX_SCRIPTS_PER_TASK)
            {
                string limitMessage =
                    $"This task has already generated {_scriptsWrittenThisTask.Count} script(s); the limit is {MAX_SCRIPTS_PER_TASK}. " +
                    "Finish the current work instead of writing more files.";

                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: {limitMessage}");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: {limitMessage}");

                return StepOutcome.Error("script limit reached");
            }

            // TAÞMA KORUMASI - çaðrý çalýþtýrýlmadan ÖNCE.
            //
            // Gerçek testte model tek bir karta 11 satýr ekledi ve durmadý; istenen 3'tü.
            // Diðer korumalarýn hiçbiri devreye girmiyordu çünkü her satýrýn adý farklý
            // olduðu için teknik olarak "yeni eleman" sayýlýyordu.
            if (IsContainerOverflowing(command, out string overflowMessage))
            {
                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Container overflow blocked - {overflowMessage}");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: {overflowMessage}");

                return StepOutcome.Error($"container overflow on '{command["params"]?["parent"]}'");
            }

            string macroSignature = BuildCallSignature(command);

            // Macro'lar da imza takibine dahil. Eskiden deðildi: birebir ayný macro
            // çaðrýsý tekrar çalýþtýrýlýyor, UiMacroExpander "already exists" dönüyor
            // ve bir adým boþa gidiyordu. Þimdi hiç çalýþtýrýlmýyor.
            if (_executedCallSignatures.Contains(macroSignature))
            {
                _consecutiveDuplicateBlocks++;

                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Duplicate macro call blocked: {toolName}");

                if (_consecutiveDuplicateBlocks >= MAX_CONSECUTIVE_DUPLICATE_BLOCKS)
                {
                    return StepOutcome.Finished(await FinishStuckTask(userPrompt, null,
                        $"Stuck repeating an already-completed macro call ({_consecutiveDuplicateBlocks} consecutive blocked attempts)."));
                }

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", "[System Notice]: This exact macro call was already executed successfully. That element is DONE. Check the [BUILD PROGRESS] list at the end of the history - it shows everything already built. Build only what the request still needs; if nothing is missing, finish with {\"type\":\"task_complete\",\"params\":{\"summary\":\"...\"}}.");
                return StepOutcome.Skip();
            }

            if (EnableVerboseLogging)
            {
                Debug.Log($"========== UI MACRO (STEP {currentStep}) ==========");
                Debug.Log(command.ToString(Newtonsoft.Json.Formatting.Indented));
            }

            string executionResult = await UiMacroExpander.Execute(command, _createdObjectNames);

            if (WasExecutionSuccessful(executionResult))
            {
                _executedCallSignatures.Add(macroSignature);
                RecordBuiltElement(toolName, command);
                RecordProducedTexts(command);
                TrackMacroProgress(toolName, command, executionResult);
                _consecutiveDuplicateBlocks = 0;
                _lastEditTargetKey = null;
                _consecutiveSameTargetEdits = 0;

                // write_script bir dosya ürettiyse rollback takibine girmeli: derleme
                // kýrýlýrsa watchdog onu otomatik silecek.
                bool macroWroteScript = TrackScriptFromMacroResult(toolName, executionResult);

                return StepOutcome.Ran(executionResult, macroWroteScript);
            }

            if (IsAlreadyExistsFailure(executionResult))
            {
                // UiMacroExpander "bu zaten var" durumunu Error() ile, yani
                // success:false olarak döndürüyor. Raw çaðrýlarda bu durumu
                // _consecutiveDuplicateBlocks yakalýyordu, ama MACRO yolunda hiçbir
                // sayaç yoktu - model ayný macro'yu sýnýrsýz kez tekrar edebiliyordu.
                //
                // NOT: macro'nun ÝÇÝNDE bir obje "zaten var" çýkarsa artýk sahipleniliyor
                // ve sonuç BAÞARI dönüyor (bkz. UiMacroExpander.RunSequential). Buraya
                // yalnýzca PreflightElement'in en baþtan reddettiði durum düþüyor -
                // yani modelin bu görevde zaten kurduðu bir elemaný tekrar istemesi.
                _consecutiveDuplicateBlocks++;

                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Macro '{toolName}' targeted something that already exists ({_consecutiveDuplicateBlocks}/{MAX_CONSECUTIVE_DUPLICATE_BLOCKS} consecutive).");

                if (_consecutiveDuplicateBlocks >= MAX_CONSECUTIVE_DUPLICATE_BLOCKS)
                {
                    return StepOutcome.Finished(await FinishStuckTask(userPrompt, executionResult,
                        $"Stuck re-creating elements that already exist ({_consecutiveDuplicateBlocks} consecutive attempts). The UI appears to be complete."));
                }

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[Tool Execution Result]: {executionResult}\n[System Notice]: That element already exists - it is DONE. Check the [BUILD PROGRESS] list at the end of the history: build only what the request still needs, and if nothing is missing, finish with {{\"type\":\"task_complete\",\"params\":{{\"summary\":\"...\"}}}}.");
                return StepOutcome.Skip();
            }

            // GERÇEK macro hatasý (parent bulunamadý, geçersiz parametre...).
            //
            // Eskiden bu durum hiçbir sayaca dokunmuyordu: sonuç geçmiþe yazýlýyor ve
            // döngü devam ediyordu. Parent'ý hiç var olmayan bir macro, 150 adým
            // boyunca ayný hatayý verebiliyordu.
            Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Macro '{toolName}' failed: {executionResult}");

            AddMessageToHistory("assistant", cleanedResponse);
            AddMessageToHistory("user", $"[Tool Execution Result]: {executionResult}\n[System Error]: This macro call FAILED. Read the error, fix that specific problem, and try a corrected call. Two frequent causes: 'parent' names a GameObject that does not exist yet, or the anchors you gave overlap a sibling - in the second case the simplest fix is to drop anchorMin/anchorMax entirely and let the agent place the element. Do not repeat the identical call.");

            return StepOutcome.Error($"macro '{toolName}' failed - {Truncate(executionResult, 160)}");
        }

        // =====================================
        // STEP EXECUTION - RAW PATH
        // =====================================

        private async Task<StepOutcome> ExecuteRawStepAsync(
            JObject command, string toolName, string cleanedResponse, int currentStep,
            string userPrompt, string lastExecutionResult)
        {
            // KRÝTÝK SIRA: execute_code sanitizasyonu VALÝDASYONDAN ÖNCE çalýþmalý.
            // Model 'action' parametresini atlýyor ve execute_code þemasýnda bu
            // zorunlu - validasyon sanitize'dan önce çalýþsaydý çaðrý daha
            // düzeltilmeden reddedilir, bir adým boþa giderdi.
            SanitizeExecuteCodeCall(command);

            if (!ValidateToolCommand(command, out string validationError))
            {
                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: {validationError}");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: {validationError} Please review the schema and correct your JSON.");
                return StepOutcome.Error(validationError);
            }

            // Script/dosya yazma güvenlik kontrolü - kodu ÇALIÞTIRMADAN önce.
            if (!ValidateExecuteCodeSafety(command, out string safetyError))
            {
                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: BLOCKED unsafe execute_code - {safetyError}");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: This execute_code call was BLOCKED and not executed. {safetyError} Fix the code and try again.");
                return StepOutcome.Error(safetyError);
            }

            SanitizeEmptyReferenceFields(command);
            SanitizeRectTransformProperties(command);
            FixLegacyAlignmentValue(command);
            StripScaleFromUiObjects(command);

            if (_canvasReady && IsAddingCanvasComponent(command))
            {
                Debug.LogWarning("[OllamaToolAgent] Blocked: attempted to add a second Canvas.");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: BLOCKED - a Canvas already exists ('{AUTO_CANVAS_NAME}') with Canvas, CanvasScaler and GraphicRaycaster. Never add a second Canvas - nested Canvases break UGUI layout. Make your UI elements children of '{AUTO_CANVAS_NAME}' and continue with Image/Text/Button components instead.");
                return StepOutcome.Error("attempted to add a second Canvas");
            }

            // Macro yolundaki taþma korumasý ham çaðrýlar için de geçerli - aksi halde
            // model macro'da engellenince ayný iþi manage_gameobject ile yapmaya
            // devam edebilir ve koruma delinmiþ olur.
            if (string.Equals(toolName, "manage_gameobject", StringComparison.OrdinalIgnoreCase) &&
                IsContainerOverflowing(command, out string rawOverflowMessage))
            {
                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Container overflow blocked - {rawOverflowMessage}");

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Error]: {rawOverflowMessage}");

                return StepOutcome.Error($"container overflow on '{command["params"]?["parent"]}'");
            }

            if (IsDuplicateObjectCreation(command, out string duplicateName))
            {
                Debug.LogWarning($"[OllamaToolAgent] Blocked duplicate GameObject creation: '{duplicateName}'");

                _consecutiveDuplicateBlocks++;

                if (_consecutiveDuplicateBlocks >= MAX_CONSECUTIVE_DUPLICATE_BLOCKS)
                {
                    return StepOutcome.Finished(await FinishStuckTask(userPrompt, lastExecutionResult,
                        $"Stuck repeating already-completed work ({_consecutiveDuplicateBlocks} consecutive blocked attempts, last one: creating '{duplicateName}' again)."));
                }

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Notice]: A GameObject named '{duplicateName}' was already created in this task, so this call was NOT executed - it would leave a duplicate in the scene. To move it under a different parent, use manage_gameobject action=modify with 'target':'{duplicateName}' and the new 'parent'. Otherwise continue with the next element.");
                return StepOutcome.Skip();
            }

            // Ayný (target, componentType) çiftine art arda kaç kez dokunulduðunu
            // takip et - deðerler her seferinde farklý olsa bile.
            if (string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
            {
                string editTarget = command["params"]?["target"]?.ToString();
                string editComponentType = GetComponentType(command);

                if (!string.IsNullOrWhiteSpace(editTarget) && !string.IsNullOrWhiteSpace(editComponentType))
                {
                    string editKey = editTarget.ToLowerInvariant() + "|" + editComponentType.ToLowerInvariant();

                    if (editKey == _lastEditTargetKey)
                    {
                        _consecutiveSameTargetEdits++;
                    }
                    else
                    {
                        _lastEditTargetKey = editKey;
                        _consecutiveSameTargetEdits = 1;
                    }

                    if (_consecutiveSameTargetEdits >= MAX_CONSECUTIVE_SAME_TARGET_EDITS)
                    {
                        return StepOutcome.Finished(await FinishStuckTask(userPrompt, lastExecutionResult,
                            $"Stuck fine-tuning the same element repeatedly ({_consecutiveSameTargetEdits} consecutive edits to '{editTarget}' / {editComponentType}) instead of moving on to other elements."));
                    }
                }
            }
            else
            {
                // Baþka bir tool'a geçildiyse "ayný hedefi kurcalama" zinciri kýrýlmýþtýr.
                _lastEditTargetKey = null;
                _consecutiveSameTargetEdits = 0;
            }

            string callSignature = BuildCallSignature(command);

            if (_executedCallSignatures.Contains(callSignature))
            {
                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Duplicate call blocked at code level: {callSignature}");

                _consecutiveDuplicateBlocks++;

                if (_consecutiveDuplicateBlocks >= MAX_CONSECUTIVE_DUPLICATE_BLOCKS)
                {
                    return StepOutcome.Finished(await FinishStuckTask(userPrompt, lastExecutionResult,
                        $"Stuck repeating an already-completed action ({_consecutiveDuplicateBlocks} consecutive blocked attempts)."));
                }

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[System Notice]: This exact tool call was already executed successfully earlier in this task ({_consecutiveDuplicateBlocks} time(s) now). This part of the UI is DONE - move on to a different, not-yet-built element. If the ENTIRE task is complete, respond with plain text now (no JSON).");
                return StepOutcome.Skip();
            }

            if (EnableVerboseLogging)
            {
                Debug.Log($"========== MCP EXECUTION (STEP {currentStep}) ==========");
                Debug.Log(command.ToString(Newtonsoft.Json.Formatting.Indented));
            }

            string executionResult = await MCPExecutor.Execute(command);

            if (WasExecutionSuccessful(executionResult))
            {
                _executedCallSignatures.Add(callSignature);
                TrackCreatedObject(command);
                MarkCanvasIfCreated(command);
                TrackRawUiProgress(command);

                bool wroteScript = TrackWrittenScriptFiles(command);

                await EnsureStandaloneInputModuleAsync(command);
                _consecutiveDuplicateBlocks = 0;

                return StepOutcome.Ran(executionResult, wroteScript);
            }

            if (IsAlreadyExistsFailure(executionResult))
            {
                // Raw çaðrý da "zaten var" alabilir (Unity'nin kendi mesajý). Macro
                // yoluyla ayný mantýk: bu bir hata deðil, tamamlanmýþ iþin tekrarý.
                _consecutiveDuplicateBlocks++;

                Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Raw call targeted something that already exists ({_consecutiveDuplicateBlocks}/{MAX_CONSECUTIVE_DUPLICATE_BLOCKS} consecutive).");

                if (_consecutiveDuplicateBlocks >= MAX_CONSECUTIVE_DUPLICATE_BLOCKS)
                {
                    return StepOutcome.Finished(await FinishStuckTask(userPrompt, executionResult,
                        $"Stuck re-creating elements that already exist ({_consecutiveDuplicateBlocks} consecutive attempts). The UI appears to be complete."));
                }

                AddMessageToHistory("assistant", cleanedResponse);
                AddMessageToHistory("user", $"[Tool Execution Result]: {executionResult}\n[System Notice]: That already exists - move on to the next element.");
                return StepOutcome.Skip();
            }

            Debug.LogWarning($"[OllamaToolAgent] Step {currentStep}: Call failed: {executionResult}");

            AddMessageToHistory("assistant", cleanedResponse);
            AddMessageToHistory("user", $"[Tool Execution Result]: {executionResult}\n[System Error]: This call FAILED. Read the error, fix that one specific problem, and try a corrected call. Do not repeat the identical call.");

            return StepOutcome.Error($"'{toolName}' failed - {Truncate(executionResult, 160)}");
        }

        // =====================================
        // FINISH GUARDS
        // =====================================

        /// <summary>
        /// Model bitirmek istediðinde son kontrol. Bir gerekçe döndürürse model geri
        /// itilir; null döndürürse görev biter.
        ///
        /// HER GERÝ ÝTMENÝN BÝR SAYACI VAR. Eskiden boþ konteyner nudge'ýnýn sayacý
        /// vardý ama "UI eksik" gerekçesinin yoktu - EventSystem kurulumu bir kez
        /// baþarýsýz olduðunda model hiçbir zaman bitiremiyordu ve kalan adým bütçesi,
        /// modelin düzeltemeyeceði bir sorun yüzünden yanýyordu.
        /// </summary>
        private async Task<string> BuildFinishGuardNoticeAsync()
        {
            // Bitirmeden ÖNCE eksik altyapýyý tamamla: en sýk kaçan þey EventSystem -
            // onsuz butonlar týklanamaz.
            if (_canvasReady && !_eventSystemSetupAttempted)
            {
                await EnsureEventSystemExistsAsync();
            }

            string incompleteReason = GetIncompleteUiReason();

            if (incompleteReason != null && _incompleteUiNudges < MAX_INCOMPLETE_UI_NUDGES)
            {
                _incompleteUiNudges++;

                Debug.LogWarning($"[OllamaToolAgent] Incomplete UI detected - forcing continuation (nudge {_incompleteUiNudges}/{MAX_INCOMPLETE_UI_NUDGES}): {incompleteReason}");

                return $"{incompleteReason} The UI is NOT finished - do not stop here. Continue emitting tool calls " +
                       "(macros like create_ui_panel/create_ui_card/create_ui_button/create_ui_nav_item/create_ui_label/create_ui_row/create_ui_toggle/create_ui_slider/create_ui_input are preferred) " +
                       "until this is resolved and the UI is actually visible and usable in Game view.";
            }

            if (incompleteReason != null)
            {
                // Sayaç doldu: model bunu çözemiyor. Sonsuz döngüye girmektense eksik
                // ama teslim edilmiþ bir UI daha iyi - kullanýcý Console'da uyarýyý
                // görüyor.
                Debug.LogWarning($"[OllamaToolAgent] UI still incomplete after {MAX_INCOMPLETE_UI_NUDGES} nudges - finishing anyway: {incompleteReason}");
            }

            string emptyContainerNudge = GetEmptyContainerNudge();

            if (emptyContainerNudge != null)
            {
                _emptyContainerNudges++;
                Debug.LogWarning($"[OllamaToolAgent] Empty containers detected (nudge {_emptyContainerNudges}/{MAX_EMPTY_CONTAINER_NUDGES}): {emptyContainerNudge}");
                return emptyContainerNudge;
            }

            // Ýstenip de ekrana hiç konmamýþ metinler - boþ konteyner denetiminin
            // yakalayamadýðý eksikler buradan çýkýyor.
            string missingTextNudge = GetMissingRequestedTextsNudge();

            if (missingTextNudge != null)
            {
                _missingTextNudges++;
                Debug.LogWarning($"[OllamaToolAgent] Requested texts are missing (nudge {_missingTextNudges}/{MAX_MISSING_TEXT_NUDGES}): {missingTextNudge}");
                return missingTextNudge;
            }

            return null;
        }

        /// <summary>
        /// Sahnede zaten duran UI objelerinin adlarýný okuyup görev hafýzasýna alýr.
        ///
        /// ÇOK KOMUTLU AKIÞ ÝÇÝN ÞART: kullanýcý iþi birden fazla komuta bölüyor
        /// ("önce iskelet, sonra içerik"). Ýkinci komutta ajan önceki komutun ürettiði
        /// hiçbir objeyi bilmiyordu; bunun iki kötü sonucu vardý:
        ///
        ///   1. IsInsideCanvas() önceki komutta yapýlmýþ bir paneli tanýmýyor, o panele
        ///      eklenen eleman "Canvas dýþýnda" sanýlýyor ve GetIncompleteUiReason
        ///      gereksiz yere modeli geri itiyordu.
        ///   2. Duplicate korumasý boþ bir listeyle baþlýyor; model önceki komutta
        ///      oluþturulmuþ bir objeyi yeniden yaratmaya çalýþsa engellenmiyordu.
        ///
        /// Yalnýzca Canvas'ýn ALTINDAKÝ objeler alýnýyor - sahnedeki kamera, ýþýk gibi
        /// UI olmayan objelerin bu listede iþi yok.
        ///
        /// NOT: UiMacroExpander.SyncFromSceneAsync bundan ÖNCE çalýþýyor ve ayný setin
        /// bir kýsmýný zaten dolduruyor. Ýkisi çakýþmýyor: Sync yerleþim kaydýný kurar
        /// ve RectTransform'u olan her objeyi ekler, bu metot ise modele gönderilecek
        /// insan okunur listeyi üretir.
        /// </summary>
        private async Task ImportExistingSceneObjectsAsync()
        {
            string code =
                "var canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);\n" +
                "if (canvas == null) return \"\";\n" +
                "var names = new System.Collections.Generic.List<string>();\n" +
                "foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) {\n" +
                "  if (t == canvas.transform) continue;\n" +
                "  names.Add(t.gameObject.name);\n" +
                "}\n" +
                "return string.Join(\"|\", names);";

            JObject call = BuildCall("execute_code", new JObject
            {
                ["action"] = "execute",
                ["code"] = code
            });

            string result;

            try
            {
                result = await MCPExecutor.Execute(call);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaToolAgent] Could not read existing scene objects: {ex.Message}. Continuing without them.");
                return;
            }

            if (string.IsNullOrWhiteSpace(result))
                return;

            // Sonuç bir JSON zarfýnýn içinde geliyor; isim listesini oradan çýkarýyoruz.
            string payload = result;

            try
            {
                JObject parsed = JObject.Parse(result);
                payload = parsed["result"]?["message"]?.ToString()
                          ?? parsed["result"]?.ToString()
                          ?? result;
            }
            catch
            {
                // Düz metin dönmüþse olduðu gibi kullanýyoruz.
            }

            var found = new List<string>();

            foreach (string raw in payload.Split('|'))
            {
                string name = raw.Trim().Trim('"', '{', '}', '[', ']');

                // Gürültü filtresi: JSON kalýntýlarý ve TMP'nin kendi alt objeleri.
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Length > 64 ||
                    name.Contains(":") ||
                    name.StartsWith("TMP SubMesh", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_createdObjectNames.Add(UiMacroExpander.NameKey("gameobject", name)))
                {
                    found.Add(name);
                }
            }

            if (found.Count == 0)
                return;

            Debug.Log($"[OllamaToolAgent] Imported {found.Count} existing UI object(s) from the scene so this task can build on top of them.");

            // Model de bilmeli: aksi halde var olan bir konteynere ekleme yapmak yerine
            // onu yeniden oluþturmaya çalýþýr.
            AddMessageToHistory("user",
                "[System Notice]: These UI objects ALREADY EXIST in the scene from earlier commands: " +
                string.Join(", ", found.Take(40)) +
                (found.Count > 40 ? $" (and {found.Count - 40} more)" : "") +
                ". Do NOT create any of them again. Use them as 'parent' when the request asks you to add something inside them.");
        }

        private void ResetTaskState()
        {
            _executedCallSignatures.Clear();
            _createdObjectNames.Clear();
            _containerCandidates.Clear();
            _containersWithChildren.Clear();
            _childCountPerParent.Clear();
            _scriptsWrittenThisTask.Clear();
            _buildManifest.Clear();
            _cardNamesThisTask.Clear();
            _producedTexts.Clear();
            _missingTextNudges = 0;
            _currentUserPrompt = null;
            _emptyContainerNudges = 0;
            _incompleteUiNudges = 0;
            _consecutiveErrors = 0;
            _canvasReady = false;
            _eventSystemReady = false;
            _eventSystemSetupAttempted = false;
            _standaloneInputModuleConfirmed = false;
            _sawVisualComponentThisTask = false;
            _sawRectTransformSetThisTask = false;
            _sawChildUnderCanvasThisTask = false;
            _lastSetupCallResult = null;
            _consecutiveDuplicateBlocks = 0;
            _lastEditTargetKey = null;
            _consecutiveSameTargetEdits = 0;

            // GeneratedScriptWatchdog'un bekleyen listesi BÝLÝNÇLÝ olarak
            // temizlenmiyor: önceki görevden kalan doðrulanmamýþ dosyalar hâlâ
            // diskte ve hâlâ geri alýnabilir olmalý.
            //
            // UiMacroExpander.ResetSession() de BÝLÝNÇLÝ olarak çaðrýlmýyor: yerleþim
            // kaydý sahneyle birlikte yaþamalý. Onun yerine RunAgentLoopAsync baþýnda
            // SyncFromSceneAsync çaðrýlýyor - kaydý silmek yerine sahnedeki gerçeðe
            // göre yeniden kuruyor. Kaydý burada silmek, ikinci komutta birinci
            // komutun panellerini görünmez yapar ve çakýþma korumasý devre dýþý kalýr.
        }

        /// <summary>
        /// Model gerçekten sýkýþtýðýnda (ayný zaten-tamamlanmýþ iþi ya da ayný objeyi
        /// art arda tekrar tekrar denediðinde) buraya düþülür. Devam etmenin faydasý yok:
        /// engellenen çaðrýlar zaten daha önce BAÞARIYLA çalýþtýðý için UI muhtemelen
        /// tamam, model sadece fark edemiyor.
        /// </summary>
        private async Task<string> FinishStuckTask(string userPrompt, string lastExecutionResult, string reason)
        {
            Debug.LogWarning($"[OllamaToolAgent] {reason} Ending task early instead of burning the remaining step budget.");

            string summarizePrompt = PromptBuilder.BuildFinalResponsePrompt(userPrompt, reason, lastExecutionResult);
            string finalAnswer = CleanResponse(await _ollama.Ask(summarizePrompt));

            AddMessageToHistory("assistant", finalAnswer);
            return finalAnswer;
        }

        // =====================================
        // GENERATED SCRIPT SAFETY
        // =====================================

        /// <summary>
        /// execute_code çaðrýsýný ÇALIÞTIRMADAN ÖNCE güvenlik açýsýndan denetler.
        ///
        /// Beþ þeyi engelliyor:
        /// (1) Yýkýcý ve ajaný öldüren çaðrýlar (silme, çýkýþ, Refresh, ImportAsset).
        /// (2) Ýzinli klasörlerin dýþýna dosya yazmak.
        /// (3) Yolu çalýþma anýnda kuran, yani denetlenemeyen dosya yazma giriþimleri.
        /// (4) Dosya adý ile sýnýf adýnýn uyuþmadýðý scriptler.
        /// (5) Tek görevde çok fazla script üretmek.
        ///
        /// Engellenen çaðrý ÇALIÞTIRILMIYOR; modele neyin yanlýþ olduðu söyleniyor ve
        /// düzeltmesi isteniyor.
        /// </summary>
        private bool ValidateExecuteCodeSafety(JObject command, out string error)
        {
            error = null;

            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "execute_code", StringComparison.OrdinalIgnoreCase))
                return true;

            string code = command["params"]?["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code))
                return true;

            foreach (var forbidden in ForbiddenCodePatterns)
            {
                if (code.IndexOf(forbidden.Key, StringComparison.Ordinal) >= 0)
                {
                    error = $"The code contains '{forbidden.Key}' - {forbidden.Value}.";
                    return false;
                }
            }

            if (!WritesFiles(code))
                return true;

            var writtenPaths = ExtractWrittenAssetPaths(code);

            // ÖNEMLÝ DÜZELTME: eskiden yol bulunamadýðýnda 'return true' deniyordu,
            // yani denetim tamamen atlanýyordu. Model yolu bir deðiþkenle ya da string
            // birleþtirmeyle kurduðunda ("Assets/" + klasor + ".cs", Path.Combine...)
            // klasör kýsýtý fiilen devre dýþý kalýyordu.
            //
            // Artýk: dosya yazan ama denetlenebilir bir yol içermeyen kod reddediliyor.
            // Model literal yol yazmaya zorlanýyor - bu hem denetlenebilir hem de
            // rollback listesine girebilir olmasýný saðlýyor.
            if (writtenPaths.Count == 0)
            {
                error = "The code writes a file but the destination path could not be verified. " +
                        $"Write the path as a plain string literal (for example \"{GENERATED_SCRIPT_FOLDER}/MyBehaviour.cs\") " +
                        "instead of building it with variables or Path.Combine, so it can be checked and rolled back if it fails to compile.";
                return false;
            }

            int newScriptCount = 0;

            foreach (string path in writtenPaths)
            {
                string normalized = path.Replace('\\', '/');
                bool isScript = normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

                bool inScriptFolder = normalized.StartsWith(GENERATED_SCRIPT_FOLDER + "/", StringComparison.OrdinalIgnoreCase);
                bool inSpriteFolder = normalized.StartsWith(GENERATED_SPRITE_FOLDER + "/", StringComparison.OrdinalIgnoreCase);

                if (!inScriptFolder && !inSpriteFolder)
                {
                    error = $"The code writes '{path}', which is outside the two permitted folders. " +
                            $"Scripts may ONLY be written into '{GENERATED_SCRIPT_FOLDER}/' and generated images ONLY into '{GENERATED_SPRITE_FOLDER}/'.";
                    return false;
                }

                if (isScript && !inScriptFolder)
                {
                    error = $"The code writes the script '{path}' outside '{GENERATED_SCRIPT_FOLDER}/'. Scripts may only go there.";
                    return false;
                }

                if (!isScript && inScriptFolder)
                {
                    error = $"'{path}' is not a .cs file, so it must not go into '{GENERATED_SCRIPT_FOLDER}/'. " +
                            $"Generated images belong in '{GENERATED_SPRITE_FOLDER}/'.";
                    return false;
                }

                if (isScript)
                {
                    // NOT: script yazmanýn DOÐRU yolu write_script macro'su. Bu dal
                    // yalnýzca model yine de execute_code'a kalkýþtýðýnda devreye
                    // giriyor ve en azýndan dosyanýn bozuk olmamasýný saðlýyor.
                    //
                    // Sýnýf adý ile dosya adý eþleþmiyorsa Unity MonoBehaviour'u
                    // yükleyemez ve anlaþýlýr bir hata da vermez. Yazmadan önce
                    // yakalamak, derleme kýrýlmasýný tamamen önlüyor.
                    if (!ScriptFileNameMatchesClass(code, normalized, out string mismatch))
                    {
                        error = mismatch;
                        return false;
                    }

                    newScriptCount++;
                }
            }

            if (_scriptsWrittenThisTask.Count + newScriptCount > MAX_SCRIPTS_PER_TASK)
            {
                error = $"This task has already generated {_scriptsWrittenThisTask.Count} script(s); the limit is {MAX_SCRIPTS_PER_TASK}. " +
                        "Finish the current work instead of writing more files.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Yazýlan .cs dosyasýnýn adý ile içindeki public sýnýf adý eþleþiyor mu.
        ///
        /// Unity, dosya adý ile sýnýf adý farklý olan bir MonoBehaviour'u yükleyemez ve
        /// verdiði hata mesajý sorunun kaynaðýný göstermez. Bu, üretilen scriptlerde en
        /// sýk görülen kýrýlma sebebi - dosya yazýlmadan önce yakalamak, derlemenin hiç
        /// kýrýlmamasýný saðlýyor.
        /// </summary>
        private bool ScriptFileNameMatchesClass(string code, string normalizedPath, out string error)
        {
            error = null;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return true;

            // Kod, yazýlacak kaynaðý bir string literal içinde taþýyor. Sýnýf
            // bildirimini kaçýþ karakterleriyle birlikte arýyoruz.
            var matches = Regex.Matches(code, @"public\s+class\s+([A-Za-z_][A-Za-z0-9_]*)");

            if (matches.Count == 0)
            {
                // Sýnýf bildirimi bulunamadý - MonoBehaviour olmayabilir, engellemiyoruz.
                return true;
            }

            foreach (Match m in matches)
            {
                if (string.Equals(m.Groups[1].Value, fileName, StringComparison.Ordinal))
                    return true;
            }

            string found = string.Join(", ", matches.Cast<Match>().Select(m => m.Groups[1].Value).Distinct());

            error = $"The file is named '{fileName}.cs' but the class inside is '{found}'. " +
                    "Unity cannot load a MonoBehaviour whose file name and class name differ, and the error it gives is not helpful. " +
                    $"Rename one of them so they match exactly - either write to '{GENERATED_SCRIPT_FOLDER}/{found}.cs' or rename the class to '{fileName}'. " +
                    "Better still, use the write_script macro instead of execute_code - it checks this for you and needs no escaping.";

            return false;
        }

        private bool WritesFiles(string code)
        {
            foreach (string marker in FileWriteMarkers)
            {
                if (code.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Koddaki týrnak içinde geçen, Assets/ ile baþlayan dosya yollarýný çýkarýr.
        ///
        /// Tam bir C# ayrýþtýrýcý deðil - amacý da o deðil. Tek sorusu var: "bu kod
        /// hangi dosyalara dokunuyor?" Yanlýþ pozitif zararsýz (fazladan bir yolu
        /// denetleriz), yanlýþ negatif ise ValidateExecuteCodeSafety tarafýndan
        /// reddedilmeye yol açar - yani güvenli tarafa düþer.
        /// </summary>
        private List<string> ExtractWrittenAssetPaths(string code)
        {
            var paths = new List<string>();

            foreach (Match match in Regex.Matches(code, "\"(Assets[/\\\\][^\"]+\\.[A-Za-z0-9]{1,6})\""))
            {
                string path = match.Groups[1].Value.Replace('\\', '/');

                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>
        /// write_script macro'sunun sonucundan dosya yolunu okuyup rollback takibine
        /// alýr. Böylece macro ile yazýlan scriptler de execute_code ile yazýlanlarla
        /// ayný güvenlik aðýndan geçiyor: görev sonunda import ediliyor, derleme
        /// kýrýlýrsa otomatik siliniyor.
        /// </summary>
        private bool TrackScriptFromMacroResult(string toolName, string executionResult)
        {
            if (!string.Equals(toolName, "write_script", StringComparison.OrdinalIgnoreCase))
                return false;

            string path = null;

            try
            {
                JObject parsed = JObject.Parse(executionResult);
                path = parsed["result"]?["scriptPath"]?.ToString();
            }
            catch
            {
                // Sonuç ayrýþtýrýlamadýysa takip edemeyiz; dosya yine de yazýlmýþ
                // durumda ve menüden elle geri alýnabilir.
            }

            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!_scriptsWrittenThisTask.Contains(path))
            {
                _scriptsWrittenThisTask.Add(path);
            }

            GeneratedScriptWatchdog.Track(path);
            return true;
        }

        /// <summary>
        /// Baþarýyla çalýþan bir execute_code çaðrýsýnýn yazdýðý .cs dosyalarýný
        /// watchdog'a bildirir. Script yazýldýysa true döner.
        /// </summary>
        private bool TrackWrittenScriptFiles(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "execute_code", StringComparison.OrdinalIgnoreCase))
                return false;

            string code = command["params"]?["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code) || !WritesFiles(code))
                return false;

            bool wroteScript = false;

            foreach (string path in ExtractWrittenAssetPaths(code))
            {
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                wroteScript = true;

                if (!_scriptsWrittenThisTask.Contains(path))
                {
                    _scriptsWrittenThisTask.Add(path);
                }

                GeneratedScriptWatchdog.Track(path);
            }

            return wroteScript;
        }

        /// <summary>
        /// Üretilmiþ scriptleri elle geri alma. Watchdog'a devrediyor - kalýcý liste
        /// orada tutuluyor.
        /// </summary>
        public void RollbackGeneratedScripts(string reason)
        {
            GeneratedScriptWatchdog.RollbackAll(reason);
            _scriptsWrittenThisTask.Clear();
        }

        // =====================================
        // UI MACRO SCHEMA INJECTION
        // =====================================

        private static string MergeMacroSchema(string realToolSchemaJson)
        {
            try
            {
                JArray real = string.IsNullOrWhiteSpace(realToolSchemaJson)
                    ? new JArray()
                    : JArray.Parse(realToolSchemaJson);

                JArray macros = JArray.Parse(MacroToolSchemaJson);

                JArray combined = new JArray();
                foreach (var m in macros) combined.Add(m);
                foreach (var r in real) combined.Add(r);

                return combined.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaToolAgent] Failed to merge macro schema: {ex.Message}");
                return realToolSchemaJson;
            }
        }

        private static readonly string MacroToolSchemaJson = BuildMacroToolSchemaJson();

        /// <summary>
        /// Modelin gördüðü macro þemasý.
        ///
        /// ============ BU LÝSTE ÜÇ YERLE EÞLEÞMELÝ ============
        ///   UiMacroExpander.MacroToolNames   (gerçek uygulama)
        ///   PromptRulesUI                     (kural metni)
        ///   burasý                            (þema)
        ///
        /// Ayrýþmasý SESSÝZ kayýp üretir ve bu gerçekten yaþandý: create_ui_image,
        /// create_ui_button_bar, create_ui_slider, create_ui_progress_bar ve
        /// create_ui_input kodda vardý, PromptRulesUI onlarý tanýtýyordu, ama burada
        /// YOKTU. PromptRulesUnity.AppendSchemaAuthority modele "þema otoritedir"
        /// dediði için model bu beþini güvenle kullanamadý.
        ///
        ///
        /// ============ NEDEN BU KADAR KISA YAZILIYOR ============
        /// GERÇEK ÖLÇÜM: beþ macro eklendikten sonra prompt 18966 token'a çýktý ve
        /// 17105'lik bütçeyi 1861 token AÞTI. PromptBuilder bütün opsiyonel kural
        /// bloklarýný düþürdü, yine sýðmadý - çünkü þema ZORUNLU ve düþürülemez.
        ///
        /// Sebep þemanýn kendisiydi: her macro'nun açýklamasý bir paragraftý, her
        /// parametrenin açýklamasý bir cümleydi, ve ayný anchor metni ON macro'da
        /// tekrar ediyordu. create_ui_row tek baþýna 28 parametre taþýyor.
        ///
        /// ÞEMANIN ÝÞÝ NE OLDUÐUNU DEÐÝL, NE ALDIÐINI SÖYLEMEK. Bir parametrenin
        /// neden var olduðu, ne zaman kullanýlacaðý ve hangi tuzaklarý taþýdýðý
        /// PromptRulesUI'de zaten ayrýntýlý anlatýlýyor - þemada tekrarlamak ayný
        /// bilgiyi iki kez ödemek demek, ve bedelini çekirdek kurallar ödüyor.
        ///
        /// KURAL: description tek cümle, parametre açýklamasý tek satýr. Bir
        /// parametrenin davranýþýný uzun uzun anlatman gerekiyorsa o metin
        /// PromptRulesUI'ye ait, buraya deðil.
        /// ======================================================
        /// </summary>
        private static string BuildMacroToolSchemaJson()
        {
            // Tekrar eden açýklamalar. Tek yerde tanýmlý olmalarý hem tutarlýlýk
            // saðlýyor hem de uzunluklarýný tek hamlede ayarlamayý mümkün kýlýyor -
            // A0 on macro'da geçtiði için her fazladan kelime on kez ödeniyor.
            const string A0 = "{x,y} 0-1. OMIT BOTH anchors to stack automatically under the previous sibling.";
            const string A1 = "{x,y} 0-1. Give with anchorMin, or omit both.";
            const string H = "0-1 share of parent height. Only used when anchors are omitted.";
            const string C = "{r,g,b,a} 0-1.";

            var array = new JArray
            {
                new JObject
                {
                    ["name"] = "ensure_canvas",
                    ["description"] = "Creates the root Canvas if missing. Rarely needed - the agent does this automatically before your first UI element.",
                    ["capability"] = "UI setup",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray(),
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Defaults to 'MainCanvas'." }
                        }
                    }
                },
                new JObject
                {
                    ["name"] = "ensure_event_system",
                    ["description"] = "Creates an EventSystem if missing. Rarely needed - the agent does this automatically.",
                    ["capability"] = "UI setup",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray(),
                    ["schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() }
                },
                new JObject
                {
                    ["name"] = "create_ui_panel",
                    ["description"] = "A container rectangle: window, sidebar, top/bottom bar, or a tab content panel. It divides the screen, so it needs explicit anchors and does NOT stack automatically. Must be filled with children.",
                    ["capability"] = "UI panel",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas, or 'MainCanvas'." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C + " Fully transparent for an invisible frame such as a tab panel." },
                            ["outlineColor"] = new JObject { ["type"] = "object", ["description"] = C + " Subtle border; use alpha 0.3-0.5." },
                            ["sharpCorners"] = new JObject { ["type"] = "boolean", ["description"] = "true for square corners (backgrounds, edge-anchored bars)." },
                            ["allowOverlap"] = new JObject { ["type"] = "boolean", ["description"] = "ONLY for tab panels sharing the SAME rectangle. Ignored on a partial overlap." },
                            ["columns"] = new JObject { ["type"] = "integer", ["description"] = "2-4. Splits this panel into equal columns: elements created inside it WITHOUT anchors go side by side, one per column. Use it for a row of cards or two labels at opposite ends." },
                            ["region"] = new JObject { ["type"] = "string", ["description"] = "top | bottom | left | right | center. The agent computes the anchors from the parent's FREE area, so pass no anchors with it. Create bars and side columns first, then center - it takes whatever is left. This is the reliable way to split a screen." },
                            ["size"] = new JObject { ["type"] = "number", ["description"] = "With region: thickness as 0-1 of the parent. Defaults 0.08 for top/bottom, 0.22 for left/right. Ignored for center." },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1. Defaults to filling the parent." },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1." }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_card",
                    ["description"] = "A titled card plus an inner content container named '<name>Content' - add rows to THAT. Preferred for settings groups. Stacks automatically when anchors are omitted.",
                    ["capability"] = "UI card",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "Usually a tab panel or a content panel." },
                            ["title"] = new JObject { ["type"] = "string", ["description"] = "Card header text, e.g. 'GENERAL'." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["titleColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["titleFontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults to 18." },
                            ["outlineColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H + " Size the card to its row count." },
                            ["centered"] = new JObject { ["type"] = "boolean", ["description"] = "true for a dialog or login card: placed in the middle of its parent and shrunk to its content at the end. Pass no anchors with it." },
                            ["columns"] = new JObject { ["type"] = "integer", ["description"] = "2-4. Splits this card's content area into equal columns: elements created inside it WITHOUT anchors go side by side, one per column." },
                            ["width"] = new JObject { ["type"] = "number", ["description"] = "With centered: 0-1 share of parent width. Defaults 0.36." },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_button",
                    ["description"] = "One working button: Image + Button + a TMP label child, with hover and pressed tinting already wired. For several side by side use create_ui_button_bar.",
                    ["capability"] = "UI button",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent", "text" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["text"] = new JObject { ["type"] = "string", ["description"] = "Label shown on the button." },
                            ["buttonColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["textColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults to 16." },
                            ["alignment"] = new JObject { ["type"] = "string", ["description"] = "TMP value: Center, Left, Right. Not 'MiddleCenter'." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent", "text" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_button_bar",
                    ["description"] = "Several equal-width buttons in ONE call. Use for any toolbar or Apply/Cancel pair - the macro computes the widths and gaps, so they cannot come out uneven or overlapping. Max 8.",
                    ["capability"] = "UI button bar",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent", "buttons" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique name for the bar." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["buttons"] = new JObject { ["type"] = "array", ["description"] = "1-8 entries, each a label string or an object {name,text,buttonColor}." },
                            ["buttonColor"] = new JObject { ["type"] = "object", ["description"] = C + " Inherited by every button." },
                            ["textColor"] = new JObject { ["type"] = "object", ["description"] = C + " Inherited." },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Inherited." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C + " Bar background; defaults to transparent." },
                            ["gap"] = new JObject { ["type"] = "number", ["description"] = "Gap between buttons, 0-0.1. Defaults 0.02." },
                            ["margin"] = new JObject { ["type"] = "number", ["description"] = "Inset at both ends, 0-0.2. Defaults 0.02." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent", "buttons" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_nav_item",
                    ["description"] = "A sidebar menu entry with selected/unselected state and an accent indicator. Preferred over create_ui_button for navigation and tab headers. Stacks automatically when anchors are omitted.",
                    ["capability"] = "UI navigation item",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent", "text" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Use the pair 'NavX' / 'XPanel' for tabbed screens." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "The sidebar panel." },
                            ["text"] = new JObject { ["type"] = "string", ["description"] = "Navigation label." },
                            ["isSelected"] = new JObject { ["type"] = "boolean", ["description"] = "Exactly ONE item should be true." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["textColor"] = new JObject { ["type"] = "object", ["description"] = C + " Keep unselected labels dim but readable." },
                            ["accentColor"] = new JObject { ["type"] = "object", ["description"] = C + " Left indicator bar." },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults to 15." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent", "text" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_label",
                    ["description"] = "A text label: titles, section headers, captions, large numeric readouts.",
                    ["capability"] = "UI text",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent", "text" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["text"] = new JObject { ["type"] = "string", ["description"] = "The text to display." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults 18; a main readout belongs in 48-96." },
                            ["alignment"] = new JObject { ["type"] = "string", ["description"] = "TMP value: Center, Left, Right, TopLeft. Not 'UpperLeft'." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent", "text" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_image",
                    ["description"] = "A visual rectangle: icon slot, map or camera placeholder, divider, colour block. 'caption' draws centred text inside it; 'sprite' shows an already-imported PNG.",
                    ["capability"] = "UI image area",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["caption"] = new JObject { ["type"] = "string", ["description"] = "Centred text for placeholder areas, e.g. 'MAP / CAMERA'." },
                            ["captionFontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults to 14." },
                            ["captionColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["sprite"] = new JObject { ["type"] = "string", ["description"] = "Path to an existing PNG under Assets/UI/Sprites/. A file generated this task cannot be used until the next command." },
                            ["preserveAspect"] = new JObject { ["type"] = "boolean", ["description"] = "Defaults true; only with 'sprite'." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C + " Fill, or the tint applied to 'sprite'." },
                            ["outlineColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["sharpCorners"] = new JObject { ["type"] = "boolean", ["description"] = "true for square corners." },
                            ["blockClicks"] = new JObject { ["type"] = "boolean", ["description"] = "Defaults false; true only for a modal backdrop." },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1." },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1." }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_toggle",
                    ["description"] = "A standalone clickable on/off switch with an accent fill that shows its state. For a toggle inside a settings row use create_ui_row with control='toggle'.",
                    ["capability"] = "UI toggle",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["isOn"] = new JObject { ["type"] = "boolean", ["description"] = "Initial state." },
                            ["trackColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["onColor"] = new JObject { ["type"] = "object", ["description"] = C + " Fill shown when ON." },
                            ["knobColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1." },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = "{x,y} 0-1." }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_row",
                    ["description"] = "A settings row: label, optional description line, optional value text, and an optional right-side control the macro BUILDS FOR YOU - a real toggle, slider, progress bar or input field. The most efficient way to build settings lists. Stacks automatically when anchors are omitted. NOT for buttons - a row of buttons is create_ui_button_bar.",
                    ["capability"] = "UI settings row",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent", "label" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "The card content container '<CardName>Content', or a panel." },
                            ["label"] = new JObject { ["type"] = "string", ["description"] = "Setting title, shown on the left." },
                            ["description"] = new JObject { ["type"] = "string", ["description"] = "Short muted line under the label." },
                            ["control"] = new JObject { ["type"] = "string", ["description"] = "'toggle', 'slider', 'progress' or 'input'. The macro builds it on the right. Omit for none; any other value is rejected." },
                            ["isOn"] = new JObject { ["type"] = "boolean", ["description"] = "Toggle state." },
                            ["minValue"] = new JObject { ["type"] = "number", ["description"] = "For slider/progress. Defaults 0." },
                            ["maxValue"] = new JObject { ["type"] = "number", ["description"] = "For slider/progress. Must exceed minValue." },
                            ["value"] = new JObject { ["type"] = "string", ["description"] = "With slider/progress: the numeric starting value. Otherwise: a right-aligned readout text such as 'English'. Can be combined with a control." },
                            ["wholeNumbers"] = new JObject { ["type"] = "boolean", ["description"] = "For slider/progress; snaps to integers." },
                            ["placeholder"] = new JObject { ["type"] = "string", ["description"] = "For input; the empty-field hint." },
                            ["text"] = new JObject { ["type"] = "string", ["description"] = "For input; initial content." },
                            ["isPassword"] = new JObject { ["type"] = "boolean", ["description"] = "For input; masks characters." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C + " Row background; defaults transparent." },
                            ["labelColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["descriptionColor"] = new JObject { ["type"] = "object", ["description"] = C + " More muted than labelColor, still readable." },
                            ["valueColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["trackColor"] = new JObject { ["type"] = "object", ["description"] = C + " Toggle, slider or bar track." },
                            ["onColor"] = new JObject { ["type"] = "object", ["description"] = C + " Toggle fill when ON." },
                            ["knobColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fillColor"] = new JObject { ["type"] = "object", ["description"] = C + " Filled portion of a slider or bar." },
                            ["handleColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Label size. Defaults 15." },
                            ["descriptionFontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults 12." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent", "label" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_slider",
                    ["description"] = "A REAL draggable range control with Slider.fillRect and handleRect wired. THE ONLY WAY to get a working slider - manage_components cannot assign those fields, so a hand-built one cannot be dragged. Inside a row, prefer create_ui_row with control='slider'.",
                    ["capability"] = "UI slider",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "A row, a card content container or a panel." },
                            ["minValue"] = new JObject { ["type"] = "number", ["description"] = "Defaults 0." },
                            ["maxValue"] = new JObject { ["type"] = "number", ["description"] = "Defaults 1. Must exceed minValue." },
                            ["value"] = new JObject { ["type"] = "number", ["description"] = "Starting value, clamped into the range." },
                            ["wholeNumbers"] = new JObject { ["type"] = "boolean", ["description"] = "Snaps to integers." },
                            ["trackColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fillColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["handleColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_progress_bar",
                    ["description"] = "A READ-ONLY fill bar for battery, signal, health or loading. Cannot be dragged and its handle is hidden. Use this, never a slider, for anything the user only reads.",
                    ["capability"] = "UI progress bar",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["minValue"] = new JObject { ["type"] = "number", ["description"] = "Defaults 0." },
                            ["maxValue"] = new JObject { ["type"] = "number", ["description"] = "Defaults 100. Must exceed minValue." },
                            ["value"] = new JObject { ["type"] = "number", ["description"] = "The value to display." },
                            ["trackColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["fillColor"] = new JObject { ["type"] = "object", ["description"] = C + " Green for healthy, red-orange for a warning." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "create_ui_input",
                    ["description"] = "A REAL typeable text field with TMP_InputField.textComponent and placeholder wired. THE ONLY WAY to get a working input - built from raw calls, nothing the user types ever appears. Inside a row, prefer create_ui_row with control='input'.",
                    ["capability"] = "UI input field",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "parent" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Unique GameObject name." },
                            ["parent"] = new JObject { ["type"] = "string", ["description"] = "An existing object inside the Canvas." },
                            ["placeholder"] = new JObject { ["type"] = "string", ["description"] = "Empty-field hint, e.g. 'Username'." },
                            ["text"] = new JObject { ["type"] = "string", ["description"] = "Initial content." },
                            ["isPassword"] = new JObject { ["type"] = "boolean", ["description"] = "Masks characters." },
                            ["color"] = new JObject { ["type"] = "object", ["description"] = C + " Field background." },
                            ["textColor"] = new JObject { ["type"] = "object", ["description"] = C },
                            ["placeholderColor"] = new JObject { ["type"] = "object", ["description"] = C + " Clearly dimmer than textColor." },
                            ["fontSize"] = new JObject { ["type"] = "integer", ["description"] = "Defaults 15." },
                            ["height"] = new JObject { ["type"] = "number", ["description"] = H },
                            ["anchorMin"] = new JObject { ["type"] = "object", ["description"] = A0 },
                            ["anchorMax"] = new JObject { ["type"] = "object", ["description"] = A1 }
                        },
                        ["required"] = new JArray { "name", "parent" }
                    }
                },
                new JObject
                {
                    ["name"] = "write_script",
                    ["description"] = "Writes a C# behaviour script to Assets/UI/Generated/. USE THIS, never execute_code, for authoring scripts - execute_code needs double escaping and corrupts the file. The file is NOT compiled during this task, so the type does not exist yet and cannot be attached until the next command.",
                    ["capability"] = "script authoring",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "name", "content" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["name"] = new JObject { ["type"] = "string", ["description"] = "Class name only, no .cs and no folder path. The class inside must match exactly." },
                            ["content"] = new JObject { ["type"] = "string", ["description"] = "Complete C# source as plain text. A MonoBehaviour with NO namespace. Include every using. Never reference UnityEditor or AssetDatabase." }
                        },
                        ["required"] = new JArray { "name", "content" }
                    }
                },
                // ============ BÝTÝÞ SÝNYALÝ ============
                // Model her adýmda JSON'a kilitli (forceJson). "Düz metinle bitir"
                // talimatýný uygulayamýyor, bu yüzden bitirmenin JSON içinde bir yolu
                // olmalý - ve "þema otoritedir" kuralý yüzünden o yol ÞEMADA görünmeli,
                // yoksa model onu kullanmaya cesaret edemez.
                new JObject
                {
                    ["name"] = "task_complete",
                    ["description"] = "Call this ONCE when every requested element exists and any requested wiring is done. It ends the task. Never end a task by repeating a call that already succeeded.",
                    ["capability"] = "finish task",
                    ["group"] = "ui-macro",
                    ["allowed_actions"] = new JArray(),
                    ["required"] = new JArray { "summary" },
                    ["schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["summary"] = new JObject { ["type"] = "string", ["description"] = "2-3 sentences in the user's language: what was built, whether behaviour was wired, anything missing." }
                        },
                        ["required"] = new JArray { "summary" }
                    }
                }
            };

            return array.ToString(Newtonsoft.Json.Formatting.None);
        }

        // =====================================
        // AUTOMATIC CANVAS / EVENT SYSTEM SETUP
        // =====================================

        private bool NeedsCanvas(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return false;

            string componentType = GetComponentType(command);
            if (string.IsNullOrWhiteSpace(componentType))
                return false;

            if (CanvasComponents.Any(c => c.Equals(componentType, StringComparison.OrdinalIgnoreCase)))
                return false;

            return UiVisualComponents.Any(c => c.Equals(componentType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Sahnede kullanýlabilir bir Canvas ZATEN var mý, execute_code ile sorar.
        ///
        /// ================ NEDEN GEREKLÝ ================
        /// GERÇEK TEST BULGUSU - ÇOK KOMUTLU AKIÞ BOZUKTU:
        /// Her yeni komut yeni bir görevdir ve ResetTaskState() '_canvasReady = false'
        /// yapar. Ajan yalnýzca KENDÝ görevinde oluþturduklarýný hatýrladýðý için,
        /// ikinci komutta sahnedeki Canvas'ý göremiyor ve yeniden kurmaya çalýþýyordu.
        ///
        /// Unity ayný isimde ikinci bir GameObject oluþturmaya izin verdiði için sahnede
        /// üç tane 'MainCanvas' birikti; ardýndan GameObject.Find ilk bulduðunu
        /// döndürdüðü için component eklemeleri yanlýþ objeye gitti ve kurulum
        /// "Canvas setup FAILED" ile çöktü. Kullanýcýnýn ikinci komutu hiçbir þey
        /// üretmedi.
        ///
        /// Çözüm: hafýzaya deðil SAHNEYE sormak. Canvas oradaysa yeniden kurulmuyor,
        /// sadece hazýr iþaretleniyor.
        ///
        /// Bu kontrol MCP üzerinden deðil execute_code ile yapýlýyor: manage_gameobject
        /// bir objenin VARLIÐINI sorgulayan bir action sunmuyor, sadece oluþturuyor.
        /// ===============================================
        /// </summary>
        private async Task<bool> SceneAlreadyHasCanvasAsync()
        {
            string code =
                "var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);\n" +
                "if (canvases == null || canvases.Length == 0) return \"NONE\";\n" +
                "foreach (var c in canvases) {\n" +
                "  if (c.gameObject.name == \"" + AUTO_CANVAS_NAME + "\") return \"FOUND:\" + c.gameObject.name;\n" +
                "}\n" +
                "return \"FOUND:\" + canvases[0].gameObject.name;";

            JObject call = BuildCall("execute_code", new JObject
            {
                ["action"] = "execute",
                ["code"] = code
            });

            string result;

            try
            {
                result = await MCPExecutor.Execute(call);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaToolAgent] Could not probe the scene for an existing Canvas: {ex.Message}. Falling back to creating one.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(result))
                return false;

            bool found = result.IndexOf("FOUND:", StringComparison.Ordinal) >= 0;

            if (found)
            {
                Debug.Log("[OllamaToolAgent] An existing Canvas was found in the scene - reusing it instead of creating a second one.");
            }

            return found;
        }

        /// <summary>
        /// Sahnede bir EventSystem zaten var mý. Canvas ile ayný gerekçe: ikinci komutta
        /// yeniden kurmaya çalýþmak sahneye yinelenmiþ EventSystem býrakýyor ve Unity
        /// "multiple EventSystems in scene" uyarýsý veriyor.
        /// </summary>
        private async Task<bool> SceneAlreadyHasEventSystemAsync()
        {
            string code =
                "var es = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>(true);\n" +
                "if (es == null) return \"NONE\";\n" +
                "var module = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();\n" +
                "return module != null ? \"FOUND_WITH_MODULE\" : \"FOUND_NO_MODULE\";";

            JObject call = BuildCall("execute_code", new JObject
            {
                ["action"] = "execute",
                ["code"] = code
            });

            string result;

            try
            {
                result = await MCPExecutor.Execute(call);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaToolAgent] Could not probe the scene for an existing EventSystem: {ex.Message}.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(result))
                return false;

            // Modülü eksik bir EventSystem "yok" sayýlýyor: kurulum akýþý devam edip
            // StandaloneInputModule'ü ekleyecek, aksi halde butonlar saðýr kalýr.
            return result.IndexOf("FOUND_WITH_MODULE", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Canvas + CanvasScaler + GraphicRaycaster kurar, ardýndan EventSystem'i
        /// garantiye alýr. Bu adýmlar dýþ döngünün adým sayacýna dahil DEÐÝL.
        ///
        /// KRÝTÝK: _canvasReady KOÞULLU atanýyor. Önceki bir sürüm dört setup çaðrýsýnýn
        /// hepsi baþarýsýz olsa bile Canvas'ý hazýr sayýyordu; sonraki her UI çaðrýsý
        /// görünmeyen objeler üretiyor ve kullanýcý boþ bir Game view görüyordu.
        /// </summary>
        private async Task EnsureCanvasExistsAsync()
        {
            // ÖNCE SAHNEYE SOR. Önceki bir komuttan kalma Canvas varsa ikinci bir tane
            // oluþturmak sahneyi bozar - bkz. SceneAlreadyHasCanvasAsync açýklamasý.
            if (await SceneAlreadyHasCanvasAsync())
            {
                _canvasReady = true;
                _createdObjectNames.Add(UiMacroExpander.NameKey("gameobject", AUTO_CANVAS_NAME));

                AddMessageToHistory("user",
                    $"[System Notice]: A Canvas ('{AUTO_CANVAS_NAME}') already exists in the scene from earlier work and is being reused. " +
                    "Do NOT create another Canvas. Elements built in previous commands are still there - " +
                    "do not rebuild them, only add what this request asks for.");

                await EnsureEventSystemExistsAsync();
                return;
            }

            Debug.Log("[OllamaToolAgent] Ensuring Canvas exists before this UI call.");

            var setupCalls = new List<JObject>
            {
                BuildCall("manage_gameobject", new JObject { ["action"] = "create", ["name"] = AUTO_CANVAS_NAME }),
                BuildCall("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = AUTO_CANVAS_NAME, ["componentType"] = "Canvas",
                    ["properties"] = new JObject { ["renderMode"] = "ScreenSpaceOverlay" }
                }),
                BuildCall("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = AUTO_CANVAS_NAME, ["componentType"] = "CanvasScaler",
                    ["properties"] = new JObject
                    {
                        ["uiScaleMode"] = "ScaleWithScreenSize",
                        ["referenceResolution"] = new JObject { ["x"] = 1920, ["y"] = 1080 },
                        ["screenMatchMode"] = "MatchWidthOrHeight",
                        ["matchWidthOrHeight"] = 0.5
                    }
                }),
                BuildCall("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = AUTO_CANVAS_NAME, ["componentType"] = "GraphicRaycaster"
                })
            };

            // Canvas GameObject'i ve Canvas component'i zorunlu; Scaler/Raycaster
            // olmadan da UI görünür (daha kötü ölçeklenir ama görünür), o yüzden
            // bunlarýn baþarýsýzlýðý görevi durdurmuyor.
            bool coreOk = true;

            for (int i = 0; i < setupCalls.Count; i++)
            {
                bool ok = await RunSetupCallAsync(setupCalls[i]);

                if (!ok && IsAlreadyExistsFailure(_lastSetupCallResult))
                {
                    ok = true;
                }

                if (i <= 1 && !ok)
                {
                    coreOk = false;
                }
            }

            if (!coreOk)
            {
                Debug.LogError("[OllamaToolAgent] Canvas setup FAILED - UI elements would be created but never rendered.");
                return;
            }

            _canvasReady = true;

            AddMessageToHistory("user",
                $"[System Notice]: A Canvas was set up automatically: '{AUTO_CANVAS_NAME}' with Canvas + CanvasScaler + GraphicRaycaster. " +
                "Do NOT create another Canvas and do NOT add Canvas/CanvasScaler/GraphicRaycaster to anything - that is already done. " +
                $"Use \"parent\":\"{AUTO_CANVAS_NAME}\" (directly or indirectly) for every UI element from now on.");

            await EnsureEventSystemExistsAsync();

            Debug.Log("[OllamaToolAgent] Canvas auto-setup complete.");
        }

        /// <summary>
        /// EventSystem + StandaloneInputModule'ü garantiye alýr. Canvas kurulumundan
        /// AYRI bir metot olmasý kritik: Canvas'ý model kendisi kurduðunda
        /// EnsureCanvasExistsAsync hiç çalýþmýyor, ama butonlarýn/toggle'larýn
        /// týklanabilmesi için EventSystem yine de þart.
        ///
        /// DÜZELTME: "denendi" ve "hazýr" ayrý bayraklar. Önceki sürümde tek bayrak
        /// vardý ve baþarýsýzlýkta da true'ya çekiliyordu: bir daha hiç denenmiyor,
        /// üstelik _standaloneInputModuleConfirmed false kaldýðý için model her
        /// bitirmek istediðinde geri itiliyordu.
        /// </summary>
        private async Task EnsureEventSystemExistsAsync()
        {
            if (_eventSystemReady)
                return;

            // Canvas ile ayný gerekçe: ikinci komutta sahnedeki EventSystem görülmezse
            // yenisi kuruluyor ve Unity "multiple EventSystems" uyarýsý veriyor.
            if (await SceneAlreadyHasEventSystemAsync())
            {
                _eventSystemReady = true;
                _eventSystemSetupAttempted = true;
                _standaloneInputModuleConfirmed = true;
                _createdObjectNames.Add(UiMacroExpander.NameKey("gameobject", AUTO_EVENT_SYSTEM_NAME));

                Debug.Log("[OllamaToolAgent] An existing EventSystem with StandaloneInputModule was found - reusing it.");
                return;
            }

            _eventSystemSetupAttempted = true;

            Debug.Log("[OllamaToolAgent] Ensuring EventSystem exists - without it buttons and toggles cannot receive clicks.");

            var setupCalls = new List<JObject>
            {
                BuildCall("manage_gameobject", new JObject { ["action"] = "create", ["name"] = AUTO_EVENT_SYSTEM_NAME }),
                BuildCall("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = AUTO_EVENT_SYSTEM_NAME, ["componentType"] = "EventSystem"
                }),
                BuildCall("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = AUTO_EVENT_SYSTEM_NAME, ["componentType"] = "StandaloneInputModule"
                })
            };

            bool allOk = true;

            foreach (JObject call in setupCalls)
            {
                if (await RunSetupCallAsync(call))
                    continue;

                // "Zaten var" bir hata DEÐÝL - sahnede önceki görevden kalma bir
                // EventSystem varsa Unity "already exists and does not allow multiple
                // instances" diyor.
                if (IsAlreadyExistsFailure(_lastSetupCallResult))
                {
                    Debug.Log("[OllamaToolAgent] Component already present - treating as success.");
                    continue;
                }

                allOk = false;
            }

            _eventSystemReady = allOk;
            _standaloneInputModuleConfirmed = allOk;

            AddMessageToHistory("user",
                "[System Notice]: An EventSystem with StandaloneInputModule was set up automatically. " +
                "Do NOT create another EventSystem and do NOT add EventSystem/StandaloneInputModule to anything - that is already done.");

            if (allOk)
            {
                Debug.Log("[OllamaToolAgent] EventSystem ready - buttons and toggles will receive clicks.");
            }
            else
            {
                Debug.LogWarning("[OllamaToolAgent] EventSystem setup had failures - buttons may not respond to clicks. It will be retried on the next UI step.");

                // Bir sonraki adýmda tekrar denenebilsin.
                _eventSystemSetupAttempted = false;
            }
        }

        private async Task<bool> RunSetupCallAsync(JObject call)
        {
            string result = await MCPExecutor.Execute(call);
            _lastSetupCallResult = result;

            if (EnableVerboseLogging)
            {
                Debug.Log($"[OllamaToolAgent] Auto-setup: {call.ToString(Newtonsoft.Json.Formatting.None)}");
                LogSafe(result, 300);
            }

            if (WasExecutionSuccessful(result))
            {
                _executedCallSignatures.Add(BuildCallSignature(call));
                TrackCreatedObject(call);
                return true;
            }

            Debug.LogWarning($"[OllamaToolAgent] Auto-setup call failed: {result}");
            return false;
        }

        private JObject BuildCall(string toolName, JObject parameters)
        {
            return new JObject { ["type"] = toolName, ["params"] = parameters };
        }

        private bool IsAddingCanvasComponent(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return false;

            string action = command["params"]?["action"]?.ToString();
            if (!string.Equals(action, "add", StringComparison.OrdinalIgnoreCase))
                return false;

            string componentType = GetComponentType(command);
            if (string.IsNullOrWhiteSpace(componentType))
                return false;

            return CanvasComponents.Any(c => c.Equals(componentType, StringComparison.OrdinalIgnoreCase));
        }

        private void MarkCanvasIfCreated(JObject command)
        {
            if (_canvasReady)
                return;

            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return;

            string componentType = GetComponentType(command);

            if (!string.IsNullOrWhiteSpace(componentType) &&
                componentType.Equals("Canvas", StringComparison.OrdinalIgnoreCase))
            {
                _canvasReady = true;
                Debug.Log("[OllamaToolAgent] Canvas created by the model directly - EventSystem will be ensured next.");
            }
        }

        private async Task EnsureStandaloneInputModuleAsync(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return;

            string componentType = GetComponentType(command);
            if (!string.Equals(componentType, "EventSystem", StringComparison.OrdinalIgnoreCase))
                return;

            string target = command["params"]?["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(target))
                return;

            _eventSystemSetupAttempted = true;

            JObject addInputModule = BuildCall("manage_components", new JObject
            {
                ["action"] = "add",
                ["target"] = target,
                ["componentType"] = "StandaloneInputModule"
            });

            string inputModuleSignature = BuildCallSignature(addInputModule);

            if (_executedCallSignatures.Contains(inputModuleSignature))
            {
                _standaloneInputModuleConfirmed = true;
                _eventSystemReady = true;
                return;
            }

            Debug.Log($"[OllamaToolAgent] EventSystem added to '{target}' - ensuring StandaloneInputModule is present.");

            string result = await MCPExecutor.Execute(addInputModule);

            if (WasExecutionSuccessful(result) || IsAlreadyExistsFailure(result))
            {
                _executedCallSignatures.Add(inputModuleSignature);
                _standaloneInputModuleConfirmed = true;
                _eventSystemReady = true;
                Debug.Log("[OllamaToolAgent] StandaloneInputModule confirmed.");
            }
            else
            {
                Debug.LogWarning($"[OllamaToolAgent] Failed to auto-add StandaloneInputModule: {result}");
            }
        }

        private string GetComponentType(JObject command)
        {
            return command["params"]?["componentType"]?.ToString()
                   ?? command["params"]?["component_type"]?.ToString();
        }

        // =====================================
        // DUPLICATE OBJECT TRACKING
        // =====================================

        private bool IsDuplicateObjectCreation(JObject command, out string duplicateName)
        {
            duplicateName = null;

            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_gameobject", StringComparison.OrdinalIgnoreCase))
                return false;

            string action = command["params"]?["action"]?.ToString();
            if (!string.Equals(action, "create", StringComparison.OrdinalIgnoreCase))
                return false;

            string name = command["params"]?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (_createdObjectNames.Contains(UiMacroExpander.NameKey("gameobject", name)))
            {
                duplicateName = name;
                return true;
            }

            return false;
        }

        private void TrackCreatedObject(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_gameobject", StringComparison.OrdinalIgnoreCase))
                return;

            string action = command["params"]?["action"]?.ToString();
            if (!string.Equals(action, "create", StringComparison.OrdinalIgnoreCase))
                return;

            string name = command["params"]?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return;

            _createdObjectNames.Add(UiMacroExpander.NameKey("gameobject", name));

            string parent = command["params"]?["parent"]?.ToString();
            if (string.IsNullOrWhiteSpace(parent))
                return;

            _containersWithChildren.Add(parent);
            CountChild(parent);

            // Sadece Canvas'ýn ALTINDAKÝ bir parent, "Canvas'a baðlandý" sayýlmalý.
            // Önceki sürüm herhangi bir parent'ý yeterli görüyordu; sahne kökündeki
            // bir objenin altýna konan eleman da "Canvas'ýn içinde" sanýlýyordu ve
            // GetIncompleteUiReason bu yüzden hiç uyarmýyordu.
            if (IsInsideCanvas(parent))
            {
                _sawChildUnderCanvasThisTask = true;
            }
        }

        /// <summary>
        /// Verilen isim Canvas'ýn kendisi mi ya da bu görevde Canvas'ýn altýna konmuþ
        /// bir obje mi. Tam bir hiyerarþi taramasý deðil - ajan sadece kendi oluþturduðu
        /// ve sahneden içe aktardýðý objeleri biliyor - ama pratikte yeterli.
        /// </summary>
        private bool IsInsideCanvas(string parentName)
        {
            if (string.Equals(parentName, AUTO_CANVAS_NAME, StringComparison.OrdinalIgnoreCase))
                return true;

            return _containerCandidates.Contains(parentName) ||
                   _createdObjectNames.Contains(UiMacroExpander.NameKey("gameobject", parentName));
        }

        // =====================================
        // EMPTY CONTAINER TRACKING
        // =====================================

        /// <summary>
        /// Model bitirmeye çalýþtýðýnda, oluþturduðu ama içine hiçbir þey koymadýðý
        /// konteynerleri bulur ve isim isim listeler.
        ///
        /// Gerçek testte model 4 içerik panelinden 2'sini doldurup diðer 2'sini boþ
        /// býraktý ve durdu. Baðlam sorunu deðildi - model 4 benzer görevi sýrayla
        /// takip edemiyordu ve hangi konteynerin boþ kaldýðýný göremiyordu.
        /// </summary>
        /// <summary>Macro ya da ham çaðrýnýn ekrana koyduðu metinleri kaydeder.</summary>
        private void RecordProducedTexts(JObject command)
        {
            JObject p = command?["params"] as JObject;

            if (p == null)
                return;

            foreach (string field in TextBearingFields)
            {
                string value = p[field]?.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                    _producedTexts.Add(value);
            }

            // Buton çubuðu: her giriþ ya düz bir etiket ya da 'text' alaný olan bir nesne.
            if (p["buttons"] is JArray buttons)
            {
                foreach (JToken entry in buttons)
                {
                    if (entry.Type == JTokenType.String)
                    {
                        _producedTexts.Add(entry.ToString());
                    }
                    else if (entry is JObject button)
                    {
                        string label = button["text"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(label))
                            _producedTexts.Add(label);
                    }
                }
            }

            // Ham manage_components çaðrýsý: metin 'properties' içinde.
            if (p["properties"] is JObject properties)
            {
                string raw = properties["text"]?.ToString();

                if (!string.IsNullOrWhiteSpace(raw))
                    _producedTexts.Add(raw);
            }
        }

        /// <summary>Macro parametrelerinde ekrana yazý taþýyan alanlar.</summary>
        private static readonly string[] TextBearingFields =
        {
            "text", "title", "label", "value", "caption", "placeholder", "description"
        };

        /// <summary>
        /// Ýsteðin içindeki TIRNAKLI metinleri çýkarýr - kullanýcý ekranda görmek
        /// istediði her yazýyý zaten týrnak içinde yazýyor.
        ///
        /// Elenen durumlar:
        ///   - Harf ya da rakam içermeyenler (ayraçlar, semboller).
        ///   - "for example", "e.g.", "such as", "örneðin" ifadelerinden hemen SONRA
        ///     gelenler: bunlar ÖRNEK metinler, zorunlu içerik deðil. R5 promptundaki
        ///     "Receive a message when a job fails" tam olarak böyle ve onu eksik
        ///     saymak boþa bir model turu harcardý.
        ///   - Ayný metnin tekrarlarý.
        /// </summary>
        private static List<string> ExtractQuotedTexts(string prompt)
        {
            var found = new List<string>();

            if (string.IsNullOrWhiteSpace(prompt))
                return found;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(prompt, "[\"\u201C\u201D]([^\"\u201C\u201D\r\n]{2,60})[\"\u201C\u201D]"))
            {
                string text = match.Groups[1].Value.Trim();

                if (text.Length < 2 || !text.Any(char.IsLetterOrDigit))
                    continue;

                int lookBehind = Math.Min(40, match.Index);
                string before = prompt.Substring(match.Index - lookBehind, lookBehind).ToLowerInvariant();

                if (before.Contains("for example") || before.Contains("e.g.") ||
                    before.Contains("such as") || before.Contains("örneðin") || before.Contains("ornegin"))
                {
                    continue;
                }

                string key = NormalizeTextForCompare(text);

                if (key.Length == 0 || !seen.Add(key))
                    continue;

                found.Add(text);
            }

            return found;
        }

        /// <summary>Karþýlaþtýrma için: küçük harf, baþtaki/sondaki boþluk yok, iç boþluklar tek.</summary>
        private static string NormalizeTextForCompare(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
        }

        /// <summary>
        /// Ýstenip de ekranda hiç olmayan metinleri bulur ve modele liste olarak verir.
        ///
        /// Karþýlaþtýrma ÝÇEREN eþleþmeyle yapýlýyor: model istenen metni daha uzun bir
        /// yazýnýn içine koymuþ olabilir ("Power 94%" gibi). Bu yönde esnek olmak yanlýþ
        /// alarmý azaltýyor; ters yönde katý olmak ise gereksiz.
        /// </summary>
        private string GetMissingRequestedTextsNudge()
        {
            if (_missingTextNudges >= MAX_MISSING_TEXT_NUDGES)
                return null;

            List<string> requested = ExtractQuotedTexts(_currentUserPrompt);

            if (requested.Count == 0)
                return null;

            var produced = _producedTexts
                .Select(NormalizeTextForCompare)
                .Where(t => t.Length > 0)
                .ToList();

            // Hiç metin kurulmadýysa ekran zaten yarým demektir ve bunu baþka korumalar
            // söylüyor; burada ikinci bir uyarý eklemek gürültü olurdu.
            if (produced.Count == 0)
                return null;

            var missing = new List<string>();

            foreach (string text in requested)
            {
                string needle = NormalizeTextForCompare(text);

                if (!produced.Any(p => p.Contains(needle)))
                    missing.Add(text);
            }

            if (missing.Count == 0)
                return null;

            string list = string.Join(", ", missing.Take(8).Select(t => "\"" + t + "\""));
            string more = missing.Count > 8 ? $" (and {missing.Count - 8} more)" : "";

            return $"These texts were asked for in the request but are NOT on screen anywhere yet: {list}{more}. " +
                   "Each one needs the element that carries it - a row, a label, a button or a card title, whichever the request describes - " +
                   "created under the parent it belongs to and WITHOUT anchors so it stacks automatically. " +
                   "If one of them was never meant to be visible text (a colour name, a file name, a component name), ignore that one. " +
                   "Do not finish until the rest exist.";
        }

        private string GetEmptyContainerNudge()
        {
            if (_emptyContainerNudges >= MAX_EMPTY_CONTAINER_NUDGES)
                return null;

            var empty = _containerCandidates
                .Where(c => !_containersWithChildren.Contains(c))
                .ToList();

            if (empty.Count == 0)
                return null;

            string names = string.Join(", ", empty.Select(n => $"'{n}'"));

            return $"These containers were created but are still EMPTY - nothing was ever added inside them: {names}. " +
                   "An empty container renders as a blank rectangle, which means the UI is unfinished. " +
                   "Fill each one now using create_ui_row / create_ui_card / create_ui_label / create_ui_button with parent set to that container's name, and WITHOUT anchors so they stack automatically. " +
                   "Work through them one at a time. Only respond with plain text once every container has content.";
        }

        /// <summary>
        /// Bir macro çaðrýsý sonrasý hangi objelerin "doldurulmasý beklenen konteyner"
        /// olduðunu kaydeder. Sadece konteyner niteliðindeki macro'lar sayýlýyor -
        /// bir label ya da toggle'ýn içine bir þey konmaz, onlarýn boþ olmasý normal.
        /// </summary>
        private void TrackContainerCandidate(string toolName, JObject command, string executionResult)
        {
            if (!ContainerMacros.Any(m => m.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                return;

            // create_ui_card 'createdName' olarak ÝÇERÝK konteynerini döndürüyor
            // ('<name>Content'), asýl doldurulmasý gereken yer orasý.
            string createdName = null;

            try
            {
                JObject parsed = JObject.Parse(executionResult);
                createdName = parsed["result"]?["createdName"]?.ToString();
            }
            catch
            {
                // Sonuç ayrýþtýrýlamadýysa isim parametresine düþüyoruz.
            }

            if (string.IsNullOrWhiteSpace(createdName))
            {
                createdName = command["params"]?["name"]?.ToString();
            }

            if (!string.IsNullOrWhiteSpace(createdName))
            {
                _containerCandidates.Add(createdName);
            }
        }

        /// <summary>
        /// Macro'nun 'parent' parametresi, o parent'ýn artýk çocuðu olduðunu gösterir.
        /// Böylece boþ konteyner listesi kendiliðinden temizleniyor.
        /// </summary>
        private void TrackContainerFilled(JObject command)
        {
            string parent = command["params"]?["parent"]?.ToString();
            if (!string.IsNullOrWhiteSpace(parent))
            {
                _containersWithChildren.Add(parent);
                CountChild(parent);
            }
        }

        /// <summary>
        /// Bir konteynere eklenen doðrudan çocuk sayýsýný artýrýr.
        /// </summary>
        private void CountChild(string parentName)
        {
            if (string.IsNullOrWhiteSpace(parentName))
                return;

            _childCountPerParent.TryGetValue(parentName, out int current);
            _childCountPerParent[parentName] = current + 1;
        }

        private int GetChildCount(string parentName)
        {
            if (string.IsNullOrWhiteSpace(parentName))
                return 0;

            _childCountPerParent.TryGetValue(parentName, out int count);
            return count;
        }

        /// <summary>
        /// Bir konteynerin dolup taþýp taþmadýðýný kontrol eder.
        ///
        /// Model planý kaybettiðinde ayný konteynere sýnýrsýz benzer eleman eklemeye
        /// baþlýyor. Bunu çaðrýyý ÇALIÞTIRMADAN önce kesmek þart: her taþan satýr
        /// sahneye kalýcý çöp býrakýyor ve kullanýcýnýn elle temizlemesi gerekiyor.
        /// </summary>
        private bool IsContainerOverflowing(JObject command, out string overflowMessage)
        {
            overflowMessage = null;

            string parent = command["params"]?["parent"]?.ToString();
            if (string.IsNullOrWhiteSpace(parent))
                return false;

            int count = GetChildCount(parent);

            if (count < MAX_CHILDREN_PER_CONTAINER)
                return false;

            overflowMessage =
                $"'{parent}' already has {count} direct children, which is the limit. " +
                "This call was NOT executed. A container with this many elements means the original request has been " +
                "overshot - re-read the user's request and count how many elements were actually asked for. " +
                "Move on to a DIFFERENT container that is still empty, or finish the task with plain text if everything requested exists.";

            return true;
        }

        // =====================================
        // TOOL VALIDATOR
        // =====================================

        private bool ValidateToolCommand(JObject command, out string validationError)
        {
            validationError = null;

            if (command == null)
            {
                validationError = "Command is null.";
                return false;
            }

            string toolName = command["type"]?.ToString();

            if (string.IsNullOrWhiteSpace(toolName))
            {
                validationError = "'type' field is missing or empty.";
                return false;
            }

            var cachedTools = _discovery.GetCachedTools();
            MCPToolInfo matchedTool = null;

            if (cachedTools != null)
            {
                foreach (var tool in cachedTools)
                {
                    if (tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedTool = tool;
                        break;
                    }
                }
            }

            if (matchedTool == null)
            {
                // ============ LÝSTE ARTIK ÜRETÝLÝYOR ============
                // Burada elle yazýlmýþ bir macro listesi vardý ve eskimiþti: beþ
                // macro'yu (image, button_bar, slider, progress_bar, input) hiç
                // saymýyordu. Model adýný yanlýþ yazdýðýnda, var olan araçlarýn
                // yarýsýndan haberdar edilmiyordu.
                //
                // UiMacroExpander.MacroToolNames tek doðru kaynak; listeyi oradan
                // üretmek bu tür eskimeyi imkânsýz kýlýyor.
                validationError =
                    $"Tool '{toolName}' does not exist. Available UI macros are: " +
                    string.Join(", ", UiMacroExpander.MacroToolNames) +
                    ". For anything else use manage_gameobject, manage_components or execute_code.";
                return false;
            }

            if (command["params"] != null && !(command["params"] is JObject))
            {
                validationError = "'params' must be a JSON object.";
                return false;
            }

            if (matchedTool.RequiredParams != null && matchedTool.RequiredParams.Count > 0)
            {
                JObject paramsObj = command["params"] as JObject ?? new JObject();

                foreach (var required in matchedTool.RequiredParams)
                {
                    if (paramsObj[required] == null)
                    {
                        validationError = $"Tool '{toolName}' is missing required parameter '{required}'.";
                        return false;
                    }
                }
            }

            return true;
        }

        // =====================================
        // PARAMETER SANITIZATION
        // =====================================

        /// <summary>
        /// execute_code çaðrýlarýný düzeltir.
        ///
        /// Gerçek testte model iki hata yaptý:
        /// (1) Zorunlu 'action' parametresini hiç göndermedi - tool "'action' parameter
        ///     is required" ile reddetti, bir adým boþa gitti.
        /// (2) Þemada olmayan bir 'target':'MainCanvas' parametresi ekledi - diðer
        ///     tool'lardan gelen bir alýþkanlýk, execute_code böyle bir alan tanýmýyor.
        ///
        /// ÇAÐRI SIRASI ÖNEMLÝ: bu metot ValidateToolCommand'dan ÖNCE çalýþmalý, aksi
        /// halde eksik 'action' daha düzeltilmeden validasyona takýlýr.
        /// </summary>
        private void SanitizeExecuteCodeCall(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "execute_code", StringComparison.OrdinalIgnoreCase))
                return;

            if (!(command["params"] is JObject p))
            {
                // params hiç yoksa oluþturuyoruz - en azýndan action'ý taþýyabilsin.
                p = new JObject();
                command["params"] = p;
            }

            if (p["action"] == null || string.IsNullOrWhiteSpace(p["action"].ToString()))
            {
                Debug.LogWarning("[OllamaToolAgent] execute_code call was missing the required 'action' parameter - defaulting to 'execute'.");
                p["action"] = "execute";
            }

            foreach (string unknownField in ExecuteCodeUnknownFields)
            {
                if (p[unknownField] != null)
                {
                    Debug.LogWarning($"[OllamaToolAgent] Removing '{unknownField}' from execute_code call - that parameter does not exist on this tool.");
                    p.Remove(unknownField);
                }
            }
        }

        private void SanitizeEmptyReferenceFields(JObject command)
        {
            if (!(command?["params"] is JObject paramsObj))
                return;

            foreach (string field in ReferenceFieldNames)
            {
                JToken value = paramsObj[field];

                if (value != null &&
                    value.Type != JTokenType.Object &&
                    value.Type != JTokenType.Array &&
                    string.IsNullOrWhiteSpace(value.ToString()))
                {
                    Debug.LogWarning($"[OllamaToolAgent] Removing empty '{field}' field before execution.");
                    paramsObj.Remove(field);
                }
            }
        }

        private void SanitizeRectTransformProperties(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return;

            JObject paramsObj = command["params"] as JObject;
            string componentType = paramsObj?["componentType"]?.ToString() ?? paramsObj?["component_type"]?.ToString();
            if (!string.Equals(componentType, "RectTransform", StringComparison.OrdinalIgnoreCase))
                return;

            if (!(paramsObj?["properties"] is JObject props))
                return;

            SwapIfInverted(props, "anchorMin", "anchorMax");

            foreach (string scaleKey in new[] { "scale", "localScale" })
            {
                if (props[scaleKey] != null)
                {
                    Debug.LogWarning($"[OllamaToolAgent] Removing '{scaleKey}' from RectTransform call - resizing must use anchors, not scale.");
                    props.Remove(scaleKey);
                }
            }

            ForceOffsetsForStretchedAnchors(props);
        }

        /// <summary>
        /// Model anchorMin/anchorMax gönderip offsetMin/offsetMax'i atlarsa, objenin
        /// ESKÝ offset deðerleri olduðu gibi kalýyor - gerçek testte LeftMenu
        /// Top:-108 / Bottom:972 gibi eski deðerlerde kaldý ve anchor'lar doðru
        /// olmasýna raðmen panel dikey olarak ezildi.
        ///
        /// KRÝTÝK: bu sýfýrlama SADECE her iki eksende de gerilen (stretch) anchor'lar
        /// için geçerli. Önceki bir sürüm ayrým yapmadan sýfýrlýyordu ve nokta-anchor'lý
        /// bir elemanda (anchorMin == anchorMax) offset'leri sýfýrlamak geniþliði ve
        /// yüksekliði SIFIR yapýyor - eleman tamamen görünmez oluyordu. Yani düzeltmek
        /// için yazýlmýþ kod, kendi baþýna daha kötü bir hata üretiyordu.
        /// </summary>
        private void ForceOffsetsForStretchedAnchors(JObject props)
        {
            if (!(props["anchorMin"] is JObject minObj) || !(props["anchorMax"] is JObject maxObj))
                return;

            float? minX = minObj["x"]?.ToObject<float?>();
            float? minY = minObj["y"]?.ToObject<float?>();
            float? maxX = maxObj["x"]?.ToObject<float?>();
            float? maxY = maxObj["y"]?.ToObject<float?>();

            if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
                return;

            const float epsilon = 0.0001f;
            bool stretchesX = (maxX.Value - minX.Value) > epsilon;
            bool stretchesY = (maxY.Value - minY.Value) > epsilon;

            if (!stretchesX || !stretchesY)
            {
                // Nokta ya da tek eksende sabit anchor. Offset'ler burada konumu ve
                // boyutu taþýyor; sýfýrlamak elemaný yok eder.
                if (props["offsetMin"] == null && props["offsetMax"] == null && props["sizeDelta"] == null)
                {
                    Debug.LogWarning("[OllamaToolAgent] Point-anchored RectTransform without offsets or sizeDelta - the element keeps its previous size. Prefer stretching anchors (anchorMin < anchorMax on both axes).");
                }

                return;
            }

            if (props["offsetMin"] == null)
            {
                Debug.LogWarning("[OllamaToolAgent] Stretching anchors set without offsetMin - forcing to zero so the element fills its anchor rect.");
                props["offsetMin"] = new JObject { ["x"] = 0, ["y"] = 0 };
            }

            if (props["offsetMax"] == null)
            {
                Debug.LogWarning("[OllamaToolAgent] Stretching anchors set without offsetMax - forcing to zero so the element fills its anchor rect.");
                props["offsetMax"] = new JObject { ["x"] = 0, ["y"] = 0 };
            }
        }

        private void SwapIfInverted(JObject props, string minKey, string maxKey)
        {
            if (!(props[minKey] is JObject minObj) || !(props[maxKey] is JObject maxObj))
                return;

            foreach (string axis in new[] { "x", "y" })
            {
                float? minVal = minObj[axis]?.ToObject<float?>();
                float? maxVal = maxObj[axis]?.ToObject<float?>();

                if (minVal.HasValue && maxVal.HasValue && minVal.Value > maxVal.Value)
                {
                    Debug.LogWarning($"[OllamaToolAgent] {minKey}.{axis} ({minVal}) > {maxKey}.{axis} ({maxVal}) - would render inverted/mirrored. Swapping.");
                    minObj[axis] = maxVal.Value;
                    maxObj[axis] = minVal.Value;
                }
            }
        }

        /// <summary>
        /// Model, TMP'nin TextAlignmentOptions enum'u yerine legacy Text'in TextAnchor
        /// isimlerini kullanýyor ("UpperLeft", "MiddleCenter" gibi). Bunlar TMP'de
        /// geçersiz: Unity property'yi set edemiyor, hata logluyor ama component'i yine
        /// de ekliyor - yani hizalama sessizce varsayýlanda kalýyor.
        ///
        /// Macro parametrelerine de uygulanýyor: create_ui_button / create_ui_label /
        /// create_ui_button_bar / create_ui_image üzerinden gelen legacy deðer eskiden
        /// hiç düzeltilmiyordu.
        /// </summary>
        private void FixLegacyAlignmentValue(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (string.IsNullOrWhiteSpace(toolName))
                return;

            if (!(command["params"] is JObject paramsObj))
                return;

            if (string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
            {
                if (paramsObj["properties"] is JObject props)
                {
                    ReplaceLegacyAlignment(props);
                }

                return;
            }

            if (AlignmentBearingMacros.Any(m => m.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
            {
                ReplaceLegacyAlignment(paramsObj);
            }
        }

        private void ReplaceLegacyAlignment(JObject holder)
        {
            string alignment = holder["alignment"]?.ToString();
            if (string.IsNullOrWhiteSpace(alignment))
                return;

            if (LegacyAlignmentMap.TryGetValue(alignment, out string correct))
            {
                Debug.LogWarning($"[OllamaToolAgent] Converting legacy alignment '{alignment}' to TMP value '{correct}'.");
                holder["alignment"] = correct;
            }
        }

        /// <summary>
        /// Model, manage_gameobject üzerinden UI objelerine 'scale' gönderebiliyor.
        /// UGUI'de bu her zaman yanlýþ: boyut anchor/sizeDelta ile ayarlanýr, scale metni
        /// bozar ve elemaný Canvas'ýn dýþýna taþýrýr. Gerçek bir testte TopBar'ýn scale'i
        /// 2.08, LeftMenu'nün 1.99 oldu ve paneller ekranýn dýþýna taþtý.
        ///
        /// NOT: Canvas'ýn KENDÝ scale'i buna dahil deðil - onu Unity "Screen Space -
        /// Overlay" modunda kendisi yönetiyor (0.54 gibi deðerler normaldir).
        /// </summary>
        private void StripScaleFromUiObjects(JObject command)
        {
            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_gameobject", StringComparison.OrdinalIgnoreCase))
                return;

            if (!(command["params"] is JObject p))
                return;

            foreach (string scaleKey in new[] { "scale", "localScale" })
            {
                if (p[scaleKey] != null)
                {
                    Debug.LogWarning($"[OllamaToolAgent] Removing '{scaleKey}' from manage_gameobject - UI objects must keep scale (1,1,1).");
                    p.Remove(scaleKey);
                }
            }
        }

        // =====================================
        // UI PROGRESS TRACKING
        // =====================================

        /// <summary>
        /// Baþarýlý bir macro'nun kurduðu elemaný inþa listesine ekler.
        /// </summary>
        private void RecordBuiltElement(string toolName, JObject command)
        {
            if (!VisualMacros.Any(m => m.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                return;

            if (_buildManifest.Count >= MAX_MANIFEST_ENTRIES)
                return;

            JObject p = command?["params"] as JObject;
            string name = p?["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(name))
                return;

            string parent = p["parent"]?.ToString() ?? "";

            // Kartýn kendisi ebeveyn olarak verildiyse, eleman gerçekte kartýn içerik
            // alanýna yerleþti (UiMacroExpander.RedirectToCardContent). Liste sahnedeki
            // gerçeði göstermeli, modelin yazdýðý adý deðil.
            if (_cardNamesThisTask.Contains(parent))
                parent += "Content";

            if (string.Equals(toolName, "create_ui_card", StringComparison.OrdinalIgnoreCase))
                _cardNamesThisTask.Add(name);

            // Buton çubuðu tek eleman ama içinde birden fazla buton var; kaç tane
            // olduðunu göstermek "butonlar eksik" yanýlgýsýný önlüyor.
            if (string.Equals(toolName, "create_ui_button_bar", StringComparison.OrdinalIgnoreCase) &&
                p["buttons"] is JArray buttons)
            {
                name += $" [{buttons.Count} buttons]";
            }

            // Sahiplenilen (adopted) bir eleman ikinci kez listeye girmesin.
            foreach (var entry in _buildManifest)
            {
                if (string.Equals(entry.Value, name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            _buildManifest.Add(new KeyValuePair<string, string>(parent, name));
        }

        /// <summary>
        /// Ýnþa listesini modele gösterilecek kompakt bir aðaca çevirir.
        ///
        /// Ebeveyn baþýna tek satýr, oluþturulma sýrasýyla. Her satýrýn sonunda
        /// çocuk sayýsý var: "4 satýr istendi, 4 tane var" karþýlaþtýrmasýný model
        /// kendi baþýna yapabilsin.
        /// </summary>
        private string FormatBuildManifest()
        {
            if (_buildManifest.Count == 0 && _scriptsWrittenThisTask.Count == 0)
                return null;

            var order = new List<string>();
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _buildManifest)
            {
                string parent = string.IsNullOrWhiteSpace(entry.Key) ? "(scene root)" : entry.Key;

                if (!groups.TryGetValue(parent, out var children))
                {
                    children = new List<string>();
                    groups[parent] = children;
                    order.Add(parent);
                }

                children.Add(entry.Value);
            }

            var sb = new StringBuilder();
            sb.AppendLine("[BUILD PROGRESS - everything this task has already created. This list is COMPLETE and is never trimmed, even when older steps above were condensed or dropped.]");

            foreach (string parent in order)
            {
                List<string> children = groups[parent];
                sb.AppendLine($"  {parent}: {string.Join(", ", children)} ({children.Count})");
            }

            if (_scriptsWrittenThisTask.Count > 0)
            {
                sb.AppendLine("  scripts written: " +
                              string.Join(", ", _scriptsWrittenThisTask.Select(System.IO.Path.GetFileName)));
            }

            sb.AppendLine("Everything listed above EXISTS - never create any of it again. Compare this list with the request and " +
                          "build only what is still missing. If nothing is missing, finish NOW with exactly one call: " +
                          "{\"type\":\"task_complete\",\"params\":{\"summary\":\"<2-3 sentences in the user's language>\"}}");

            return sb.ToString();
        }

        // =====================================
        // COMPLETION SIGNAL
        // =====================================

        /// <summary>
        /// Modelin bitirmek istediðini gösteren tip adlarý. "task_complete" þemadaki
        /// resmi ad; diðerleri modelin makul varyantlarý - reddetmek hiçbir þey
        /// kazandýrmaz, model zaten bitirmek istediðini açýkça söylemiþ.
        /// </summary>
        private static readonly HashSet<string> CompletionSignalNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "task_complete", "task_completed", "task_done", "complete", "completed",
                "done", "finish", "finished"
            };

        /// <summary>
        /// Cevap bir bitiþ sinyali mi?
        ///
        /// ÜÇ BÝÇÝM kabul ediliyor, çünkü JSON zorlamasý altýnda model bitirmeyi
        /// farklý þekillerde ifade edebiliyor:
        ///   1. {"type":"task_complete","params":{"summary":"..."}}  - resmi biçim
        ///   2. {"type":"done", ...}                                  - varyant
        ///   3. {"summary":"..."} / {"message":"..."} gibi TÝPSÝZ bir JSON nesnesi -
        ///      hiçbir araç çaðrýsý olamaz, dolayýsýyla tek anlamý "söyleyeceðim bu"
        ///
        /// Ayrýþtýrýcýdan BAÐIMSIZ: ResponseParser bilinmeyen bir tipi null olarak
        /// döndürse bile ham cevap burada ayrýca inceleniyor. Aksi hâlde kullanýcý
        /// final cevap olarak ham bir JSON metni görebilirdi.
        /// </summary>
        private static bool IsCompletionSignal(JObject command, string cleanedResponse, out string summary)
        {
            summary = null;

            JObject obj = command;

            if (obj == null && !string.IsNullOrWhiteSpace(cleanedResponse))
            {
                string trimmed = cleanedResponse.Trim();

                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        obj = JObject.Parse(trimmed);
                    }
                    catch
                    {
                        obj = null;
                    }
                }
            }

            if (obj == null)
                return false;

            string type = obj["type"]?.ToString()?.Trim();

            bool typedSignal = !string.IsNullOrEmpty(type) && CompletionSignalNames.Contains(type);
            bool untypedObject = string.IsNullOrEmpty(type) && command == null;

            if (!typedSignal && !untypedObject)
                return false;

            summary = FirstText(obj["params"] as JObject, "summary", "message", "text", "response")
                      ?? FirstText(obj, "summary", "message", "text", "response", "answer");

            return true;
        }

        private static string FirstText(JObject holder, params string[] fields)
        {
            if (holder == null)
                return null;

            foreach (string field in fields)
            {
                string value = holder[field]?.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        /// <summary>
        /// Bitiþ sinyalinden kullanýcýya gösterilecek final cevabý üretir.
        ///
        /// Model bir özet yazdýysa o kullanýlýyor - ekstra bir model çaðrýsý (~30-60
        /// saniye) harcamaya gerek yok. Özet boþ ya da anlamsýz derecede kýsaysa
        /// PromptBuilder.BuildFinalResponsePrompt ile düz metin modunda üretiliyor;
        /// o prompt kullanýcýnýn dilinde yazmayý zaten zorunlu tutuyor.
        /// </summary>
        private async Task<string> BuildCompletionAnswerAsync(string userPrompt, string summary, string lastExecutionResult)
        {
            if (!string.IsNullOrWhiteSpace(summary) && summary.Length >= 20)
                return summary;

            string prompt = PromptBuilder.BuildFinalResponsePrompt(userPrompt, "Task completed", lastExecutionResult);
            string generated = CleanResponse(await _ollama.Ask(prompt));

            return string.IsNullOrWhiteSpace(generated)
                ? "The requested screen was built."
                : generated;
        }

        private void TrackMacroProgress(string toolName, JObject command, string executionResult)
        {
            if (string.Equals(toolName, "ensure_canvas", StringComparison.OrdinalIgnoreCase))
            {
                _canvasReady = true;
                return;
            }

            if (string.Equals(toolName, "ensure_event_system", StringComparison.OrdinalIgnoreCase))
            {
                _eventSystemReady = true;
                _eventSystemSetupAttempted = true;
                _standaloneInputModuleConfirmed = true;
                return;
            }

            if (!VisualMacros.Any(m => m.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                return;

            _sawVisualComponentThisTask = true;
            _sawRectTransformSetThisTask = true;
            _sawChildUnderCanvasThisTask = true;

            // Boþ konteyner takibi: bu macro bir konteyner mi oluþturdu, ve hangi
            // konteynerin içine kondu?
            TrackContainerCandidate(toolName, command, executionResult);
            TrackContainerFilled(command);

            // Macro ile oluþturulan objeler de isim takibine girmeli, yoksa
            // IsInsideCanvas onlarý tanýmaz ve raw bir çaðrý bunlarý parent aldýðýnda
            // "Canvas dýþýnda" sanýlýr.
            string createdName = command["params"]?["name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(createdName))
            {
                _createdObjectNames.Add(UiMacroExpander.NameKey("gameobject", createdName));
            }
        }

        private void TrackRawUiProgress(JObject command)
        {
            // Ham bir çaðrý da ekrana metin koyabilir (properties.text).
            RecordProducedTexts(command);

            string toolName = command["type"]?.ToString();
            if (!string.Equals(toolName, "manage_components", StringComparison.OrdinalIgnoreCase))
                return;

            JObject p = command["params"] as JObject;
            string componentType = p?["componentType"]?.ToString() ?? p?["component_type"]?.ToString();
            string action = p?["action"]?.ToString();

            if (!string.IsNullOrWhiteSpace(componentType) &&
                UiVisualComponents.Any(c => c.Equals(componentType, StringComparison.OrdinalIgnoreCase)))
            {
                _sawVisualComponentThisTask = true;
            }

            if (string.Equals(action, "set_property", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(componentType, "RectTransform", StringComparison.OrdinalIgnoreCase))
            {
                _sawRectTransformSetThisTask = true;

                // ============ KAYIT SENKRONU - DÖNGÜYÜ KIRAN YARISI ============
                // Model bir paneli fazla geniþ kurduðunda tek doðru düzeltme onu
                // KÜÇÜLTMEK ve bunu manage_components ile yapabiliyor. Ama o çaðrý
                // UiMacroExpander'dan geçmiyor, dolayýsýyla LayoutRegistry'ye hiç
                // dokunmuyordu.
                //
                // Sonuç: sahne düzeliyor, kayýt eski geniþ dikdörtgeni tutmaya devam
                // ediyor, bir sonraki çaðrý yine "çakýþýyor" cevabý alýyor. Model
                // DOÐRU þeyi yaptýðý hâlde ilerleyemiyordu.
                //
                // Artýk her baþarýlý RectTransform deðiþikliði kayda yansýyor ve
                // serbest kalan bant gerçekten serbest oluyor.
                // ================================================================
                SyncRectToLayoutRegistry(p);
            }
        }

        /// <summary>
        /// Baþarýlý bir RectTransform set_property çaðrýsýnýn anchor deðerlerini
        /// yerleþim kaydýna yansýtýr.
        ///
        /// Eleman kayýtta yoksa hiçbir þey yapýlmýyor: ebeveynini bilmediðimiz bir
        /// objeyi tahmini bir ebeveynin altýna yazmak çakýþma denetimini büsbütün
        /// bozardý.
        /// </summary>
        private void SyncRectToLayoutRegistry(JObject p)
        {
            string target = p?["target"]?.ToString();

            if (string.IsNullOrWhiteSpace(target))
                return;

            if (!(p["properties"] is JObject props))
                return;

            if (!(props["anchorMin"] is JObject minObj) || !(props["anchorMax"] is JObject maxObj))
                return;

            float? minX = minObj["x"]?.ToObject<float?>();
            float? minY = minObj["y"]?.ToObject<float?>();
            float? maxX = maxObj["x"]?.ToObject<float?>();
            float? maxY = maxObj["y"]?.ToObject<float?>();

            if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
                return;

            UiMacroExpander.UpdateExistingRect(target, minX.Value, minY.Value, maxX.Value, maxY.Value);
        }

        private string GetIncompleteUiReason()
        {
            if (!_canvasReady)
                return null;

            if (_eventSystemSetupAttempted && !_standaloneInputModuleConfirmed)
            {
                return "An EventSystem exists but StandaloneInputModule could not be confirmed on it - buttons and toggles will look correct but will NOT respond to clicks.";
            }

            if (!_sawVisualComponentThisTask)
            {
                return "A Canvas exists but you never added any visual element (create_ui_panel/create_ui_card/create_ui_button/create_ui_label/create_ui_row/create_ui_slider/create_ui_input, or a raw Image/TextMeshProUGUI component), so nothing renders in Game view.";
            }

            if (!_sawRectTransformSetThisTask)
            {
                return "You never set any RectTransform anchors, so elements may still be at their default 100x100 position (0,0) and overlapping.";
            }

            if (!_sawChildUnderCanvasThisTask)
            {
                return "No element was ever nested inside the Canvas, so nothing may actually render. Every UI element needs a 'parent' that leads back to the Canvas.";
            }

            return null;
        }

        // =====================================
        // CALL SIGNATURE / SUCCESS DETECTION
        // =====================================

        private string BuildCallSignature(JObject command)
        {
            string toolName = command["type"]?.ToString() ?? "";
            string paramsJson = command["params"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            return toolName + "|" + paramsJson;
        }

        /// <summary>
        /// Bir tool sonucunun baþarý olup olmadýðýný söyler.
        ///
        /// DÜZELTME: JSON olmayan sonuçlar artýk otomatik baþarýsýz sayýlmýyor.
        /// Önceki bir sürüm parse hatasýnda koþulsuz false dönüyordu; düz metin dönen
        /// bir tool baþarýlý olsa bile imzasý kaydedilmiyor, oluþturduðu obje takip
        /// edilmiyor ve duplicate korumasý o çaðrý için tamamen devre dýþý kalýyordu.
        /// </summary>
        private bool WasExecutionSuccessful(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult))
                return false;

            try
            {
                JObject parsed = JObject.Parse(rawResult);

                string outerStatus = parsed["status"]?.ToString();
                if (string.Equals(outerStatus, "error", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (parsed["result"] is JObject resultObj && resultObj["success"] != null)
                {
                    return resultObj.Value<bool>("success");
                }

                if (string.Equals(outerStatus, "success", StringComparison.OrdinalIgnoreCase))
                    return true;

                // status alaný yok ama JSON geçerli ve hata belirtisi de yok.
                return !LooksLikeError(rawResult);
            }
            catch
            {
                // JSON deðil. Hata belirtisi taþýmýyorsa baþarý sayýyoruz.
                bool looksLikeError = LooksLikeError(rawResult);

                if (looksLikeError && EnableVerboseLogging)
                {
                    Debug.LogWarning($"[OllamaToolAgent] Non-JSON result treated as failure: {Truncate(rawResult, 200)}");
                }

                return !looksLikeError;
            }
        }

        private static bool LooksLikeError(string text)
        {
            return text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Bir sonucun "bu zaten var" anlamýna gelip gelmediðini söyler.
        ///
        /// Hem UiMacroExpander (PreflightElement'in Error() mesajýyla) hem Unity
        /// (component zaten eklenmiþse) bu ifadeyi kullanýyor. Teknik olarak
        /// baþarýsýzlýk, ama anlamý farklý: gerçek bir hata deðil, modelin
        /// tamamlanmýþ iþi tekrar denemesi.
        ///
        /// DÝKKAT - YALNIZCA BAÞARISIZ SONUÇLARDA ÇAÐRILIR. UiMacroExpander'ýn BAÞARI
        /// mesajý da "already existed and were reused" içerebiliyor (sahiplenme
        /// durumu); o sonuç success:true olduðu için buraya hiç gelmiyor ve yanlýþlýkla
        /// tekrar sayýlmýyor. Çaðrý sýrasýný deðiþtirirken bu korunmalý.
        /// </summary>
        private bool IsAlreadyExistsFailure(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult))
                return false;

            return rawResult.IndexOf(ALREADY_EXISTS_MARKER, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // =====================================
        // RESPONSE CLEANER
        // =====================================

        private string CleanResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return string.Empty;

            // Qwen3 düþünme bloðu. Kapanýþ etiketi eksik kalabiliyor (num_predict
            // sýnýrýna takýldýðýnda); o durumda da temizlenmeli, aksi halde ham
            // düþünme metni kullanýcýya cevap olarak gösteriliyor.
            string cleaned = Regex.Replace(rawResponse, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<think>[\s\S]*$", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        // =====================================
        // MEMORY MANAGEMENT
        // =====================================

        /// <summary>
        /// Konuþma geçmiþini prompt'a yazýlacak biçime çevirir.
        ///
        /// ============ NEDEN SIKIÞTIRIYORUZ ============
        /// Önceki bir sürüm 40 mesajýn tamamýný HAM olarak yazýyordu. 25. adýmda bu
        /// 8-10K token demek ve doðrudan sistem promptunun üstüne biniyor. Toplam
        /// num_ctx sýnýrýný aþtýðýnda Ollama prompt'un BAÞINI sessizce kýrpýyor -
        /// yani kullanýcýnýn kendi listesi kayboluyor ve model adýmlarý atlýyor.
        /// Gerçek testte 31 adýmlýk bir komutta model 1. adým olarak 20. maddeyi
        /// çalýþtýrdý; sebebi buydu.
        ///
        /// Sýkýþtýrma stratejisi: modelin bir sonraki adýma karar vermek için
        /// ihtiyacý olan tek þey NELERÝN BÝTTÝÐÝ. Bunun için 15 adým önceki çaðrýnýn
        /// tam JSON'una gerek yok - adý ve hedefi yeter.
        ///
        ///   Son RECENT_VERBATIM_MESSAGES mesaj : tam metin (hata ayrýntýsý burada)
        ///   Daha eskiler                       : tek satýrlýk özet
        ///
        /// Ölçülen kazanç: uzun görevlerde ~3-4K token.
        ///
        /// PromptBuilder ayrýca geçmiþe bir token payý ayýrýyor ve aþarsa baþ ve
        /// kuyruðu koruyarak kýrpýyor; bu iki katman birbirini tamamlýyor.
        /// ==============================================
        /// </summary>
        private const int RECENT_VERBATIM_MESSAGES = 12;

        private string FormatHistory()
        {
            StringBuilder sb = new StringBuilder();

            int total = _conversationHistory.Count;
            int verbatimFrom = Math.Max(0, total - RECENT_VERBATIM_MESSAGES);

            if (verbatimFrom > 0)
            {
                sb.AppendLine("[earlier steps - condensed]");

                for (int i = 0; i < verbatimFrom; i++)
                {
                    string line = CondenseHistoryLine(_conversationHistory[i]);

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        sb.AppendLine(line);
                    }
                }

                sb.AppendLine();
            }

            for (int i = verbatimFrom; i < total; i++)
            {
                var message = _conversationHistory[i];
                sb.AppendLine($"[{message.role}]");
                sb.AppendLine(message.content);
                sb.AppendLine();
            }

            // Ýnþa listesi EN SONA: PromptBuilder.FitHistory kuyruðun %75'ini
            // koruyor, yani liste her koþulda hayatta kalýyor. Ayrýca modelin
            // "sýradaki ne" kararýný verdiði <original_request_reference> bloðunun
            // hemen üstüne düþüyor - karþýlaþtýrma için en doðru yer.
            string manifest = FormatBuildManifest();

            if (!string.IsNullOrEmpty(manifest))
                sb.AppendLine(manifest);

            return sb.ToString();
        }

        /// <summary>
        /// Eski bir geçmiþ satýrýný tek satýra indirir.
        ///
        /// Amaç modelin "bu yapýldý mý" sorusuna cevap verebilmesi. Bir araç
        /// çaðrýsýndan tool adý ve hedef isim yeter; sonuçlardan ise sadece
        /// baþarýlý/baþarýsýz bilgisi.
        /// </summary>
        private string CondenseHistoryLine(OllamaMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.content))
                return null;

            string content = message.content.Trim();

            // Araç çaðrýsý: {"type":"create_ui_row","params":{"name":"X",...}}
            if (content.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    JObject parsed = JObject.Parse(content);

                    string toolName = parsed["type"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        string target = parsed["params"]?["name"]?.ToString()
                                        ?? parsed["params"]?["target"]?.ToString()
                                        ?? "";

                        return string.IsNullOrWhiteSpace(target)
                            ? $"- called {toolName}"
                            : $"- called {toolName} on '{target}'";
                    }
                }
                catch
                {
                    // JSON deðilse aþaðýdaki genel yola düþüyor.
                }
            }

            // Araç sonucu: baþarýlý olanlar tek kelimeye iniyor, hatalar korunuyor
            // çünkü model onlardan ders çýkarabilir.
            if (content.StartsWith("[Tool Execution Result]", StringComparison.Ordinal))
            {
                bool failed = content.IndexOf("\"success\":false", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              content.IndexOf("\"status\":\"error\"", StringComparison.OrdinalIgnoreCase) >= 0;

                return failed
                    ? "  -> FAILED: " + Truncate(content, 180)
                    : "  -> ok";
            }

            // Sistem uyarýlarý kýsaltýlýyor ama tamamen atýlmýyor: modelin neden geri
            // itildiðini hatýrlamasý gerekiyor.
            if (content.StartsWith("[System", StringComparison.Ordinal))
            {
                return "  -> " + Truncate(content, 160);
            }

            // Kullanýcý isteði ve düz metin cevaplar.
            return $"[{message.role}] {Truncate(content, 200)}";
        }

        private void AddMessageToHistory(string role, string content)
        {
            _conversationHistory.Add(new OllamaMessage { role = role, content = content });

            while (_conversationHistory.Count > MAX_MEMORY_MESSAGES)
            {
                _conversationHistory.RemoveAt(0);
            }
        }

        public void ClearMemory()
        {
            _conversationHistory.Clear();
            ResetTaskState();

            // Kullanýcý hafýzayý açýkça temizlediyse yerleþim kaydý da sýfýrlanmalý:
            // bu, "sahneyi de temizleyeceðim" niyetinin en güvenilir iþareti. Sahne
            // hâlâ doluysa bir sonraki görevin baþýndaki SyncFromSceneAsync kaydý
            // yeniden kuracak, yani bilgi kaybý olmuyor.
            UiMacroExpander.ResetSession();

            if (EnableVerboseLogging)
            {
                Debug.Log("[OllamaToolAgent] Conversation memory cleared.");
            }
        }

        // =====================================
        // LOG HELPER
        // =====================================

        private void LogSafe(string text, int maxLength = 1000)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (text.Length > maxLength)
            {
                Debug.Log(text.Substring(0, maxLength) + $"...\n[Truncated {text.Length - maxLength} characters]");
            }
            else
            {
                Debug.Log(text);
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        // =====================================
        // DISPOSE PATTERN
        // =====================================

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaToolAgent));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _ollama?.Dispose();

                // MCP client PAYLAÞILAN bir örnek (MCPUnityClient.GetClient()).
                //
                // Önceki bir sürüm burada Disconnect() çaðýrýyordu: bir ajan dispose
                // edildiðinde ayný baðlantýyý kullanan diðer her þey (baþka bir pencere,
                // baþka bir ajan örneði) sessizce baðlantýsýz kalýyordu. Baðlantýnýn
                // ömrü onu oluþturan tarafa ait - burada kapatýlmaz.
            }

            _disposed = true;
        }
    }

    [Serializable]
    public class OllamaMessage
    {
        public string role;
        public string content;
    }
}
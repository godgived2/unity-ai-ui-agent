using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI.Executor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Agent
{
    /// <summary>
    /// Expands high-level UI "macro" tool calls into the correct, deterministic sequence
    /// of real manage_gameobject / manage_components MCP calls.
    ///
    /// Uses TextMeshProUGUI for all text (matching PromptRulesUI's raw-call examples) -
    /// keep these two in sync if either changes.
    ///
    /// SONUÇ BOYUTU KRÝTÝK: bu sýnýfýn döndürdüðü her string, OllamaToolAgent tarafýndan
    /// konuþma geçmiþine yazýlýyor ve sonraki HER adýmda modele tekrar gönderiliyor.
    /// Bu yüzden BAÞARILI sonuçlar tek satýrlýk özet döner; ayrýntý sadece HATA
    /// durumunda korunur.
    ///
    /// ============ ÝKÝ KATMANLI KURULUM ============
    /// Her eleman iki aþamada tamamlanýyor:
    ///   1. YAPI  - manage_gameobject / manage_components ile (RunSequential)
    ///   2. RÖTUÞ - execute_code ile (RunEditorCodeAsync)
    ///
    /// Ýkinci katman neden var: manage_components bir component alanýna baþka bir SAHNE
    /// OBJESÝ atayamýyor. Converter gelen string'i asset yolu sanýyor:
    /// "Could not load asset at path 'XxxKnob' as type 'Graphic'".
    ///
    /// Rötuþ katmaný TOLERE EDÝLEBÝLÝR: baþarýsýz olursa eleman yine oluþmuþ kalýr.
    /// ==============================================
    ///
    ///
    /// ============ BU SÜRÜMDE DÜZELTÝLEN ANA ARIZA ============
    /// GÖZLEM: sekmeli ayarlar ekranýnda kartýn içindeki satýrlar ÜST ÜSTE bindi ve
    /// konsol "Stuck re-creating elements that already exist (4 consecutive attempts)"
    /// diyerek durdu. Sebep tek bir hata deðil, dört parçalý bir zincirdi:
    ///
    ///   1. LayoutRegistry STATÝK ve sahneyle senkron DEÐÝL. Ýkinci çalýþtýrmada eski
    ///      dikdörtgenler kayýtta duruyor, her yeni çaðrý "çakýþýyor" diye reddediliyor.
    ///
    ///   2. Red mesajý modele bir KAÇIÞ YOLU gösteriyordu: "add allowOverlap:true".
    ///      Model bunu genelleþtirip HER çaðrýya ekledi.
    ///
    ///   3. allowOverlap çakýþma denetimini TAMAMEN kapatýyordu - kýsmi bindirmeyi de.
    ///      Oysa amacý yalnýzca TAM üst üste binen sekme panelleriydi. Satýrlar üst
    ///      üste bindi ve hiçbir þey engellemedi.
    ///
    ///   4. Sahnede kalmýþ bir obje 'known' setinde olmadýðý için manage_gameobject
    ///      create adým 1'de patlýyor, rollback silecek bir þey bulamýyor, model ayný
    ///      çaðrýyý tekrar deniyor. Dört tekrar, sonra ajan duruyor.
    ///
    /// DÜZELTMELER (ayrýntýsý ilgili metotlarýn üstünde):
    ///   A. DÝKEY OTOMATÝK AKIÞ - anchor verilmezse eleman son kardeþin ALTINA
    ///      yerleþiyor. create_ui_button_bar'ýn yatayda yaptýðýný dikeyde yapýyor;
    ///      modelin bant aritmetiði yapma zorunluluðu ortadan kalkýyor. Bindirmenin
    ///      kalýcý çözümü bu.
    ///   B. allowOverlap SIKILAÞTIRILDI - yalnýzca NEREDEYSE AYNI dikdörtgende
    ///      geçerli. Kýsmi bindirme artýk allowOverlap ile de reddediliyor.
    ///   C. "ZATEN VAR" ARTIK BAÞARI - sahnede duran bir obje bulunduðunda macro onu
    ///      SAHÝPLENÝYOR (known'a ekliyor, dikdörtgenini kaydediyor) ve baþarý
    ///      döndürüyor. Tekrar döngüsü kökten kýrýlýyor.
    ///   D. HandleOverlapStackAsync MANTIK HATASI - yorumu "çakýþmayan eleman
    ///      gizlenmemeli" diyordu, kodu tersini yapýyordu.
    ///   E. control='slider'/'input'/'progress' ARTIK ÇALIÞIYOR - reddedilmek yerine
    ///      satýrýn saðýna gerçek kontrolü kuruyor. Model niyetini zaten belirtmiþti.
    ///   F. SAHNE SENKRONU - SyncFromScene() ile kayýt sahnedeki gerçeðe göre
    ///      tazelenebiliyor; OllamaToolAgent görev baþýnda çaðýrabilir.
    /// ========================================================
    ///
    ///
    /// ============ ÖNCEKÝ SÜRÜMLERDE DÜZELTÝLENLER (korundu) ============
    /// A. GameObject.Find PASÝF OBJELERÝ BULAMIYOR - FindHelperCode pasifleri de bulur.
    /// B. ANCHOR DENETÝMÝ - 0-1 dýþý, ters, sýfýr boyutlu dikdörtgenler yakalanýr.
    /// C. KARDEÞ ÇAKIÞMASI - LayoutRegistry izler.
    /// D. OBJE ADI KAÇIÞI - EscapeForCode + ValidateObjectName.
    /// E. create_ui_row KENDÝ ÝÇÝNDE ÇAKIÞIYORDU - etiket geniþliði artýk dinamik.
    /// F. DESTEKLENMEYEN control SESSÝZCE YUTULUYORDU.
    /// G. KISMÝ BAÞARISIZLIKTA SAHNEDE ÇÖP KALIYORDU - RollbackPartialAsync.
    /// H. HOVER/PRESSED RENGÝ - ApplySelectableColorsAsync.
    /// I. METÝNLER TIKLAMAYI YAKALIYORDU - raycastTarget=false.
    /// J. FLOAT'LAR KÜLTÜRE BAÐLIYDI - F() InvariantCulture.
    /// K. write_script NAMESPACE DENETLEMÝYORDU.
    ///
    ///
    /// ============ MACRO ENVANTERÝ ============
    /// ensure_canvas, ensure_event_system
    /// create_ui_panel, create_ui_card
    /// create_ui_button, create_ui_button_bar, create_ui_nav_item
    /// create_ui_label, create_ui_image
    /// create_ui_toggle, create_ui_row
    /// create_ui_slider, create_ui_progress_bar, create_ui_input
    /// write_script
    ///
    /// DÝKKAT: PromptRulesUI bu macro'larý TANITMALI, yoksa model varlýklarýndan
    /// haberdar olmaz. Ýki dosya birlikte güncellenmeli.
    /// =========================================
    /// </summary>
    public static class UiMacroExpander
    {
        // =====================================================
        // SPRITE ASSETS
        // =====================================================

        /// <summary>
        /// Yuvarlatýlmýþ köþe sprite'larý. RoundedSpriteGenerator tarafýndan
        /// (Tools > UI > Generate Rounded Sprites) bir kez üretiliyor.
        ///
        /// NEDEN KENDÝ SPRITE'LARIMIZ: Unity'nin built-in 'UI/Skin/UISprite.psd'
        /// kaynaðýna MCP üzerinden eriþilemiyor - converter AssetDatabase.LoadAssetAtPath
        /// kullanýyor ve built-in kaynaklar diskte o yolda bulunmuyor.
        ///
        /// Sprite YOKSA: atama baþarýsýz olur, Image keskin köþeli kalýr, macro'nun
        /// geri kalaný normal çalýþýr.
        /// </summary>
        private const string SpriteFolder = "Assets/UI/Sprites";

        private const string SpriteRoundedSmall = SpriteFolder + "/UIRounded8.png";   // butonlar, nav item'lar
        private const string SpriteRoundedLarge = SpriteFolder + "/UIRounded16.png";  // paneller, kartlar
        private const string SpritePill = SpriteFolder + "/UIRoundedPill.png";        // toggle track, çubuklar
        private const string SpriteCircle = SpriteFolder + "/UICircle.png";           // toggle knob

        public static readonly HashSet<string> MacroToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ensure_canvas",
            "ensure_event_system",
            "create_ui_panel",
            "create_ui_card",
            "create_ui_button",
            "create_ui_button_bar",
            "create_ui_nav_item",
            "create_ui_label",
            "create_ui_image",
            "create_ui_toggle",
            "create_ui_row",
            "create_ui_slider",
            "create_ui_progress_bar",
            "create_ui_input",
            "write_script"
        };

        public static bool IsMacroTool(string toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName) && MacroToolNames.Contains(toolName);
        }

        public static string NameKey(string kind, string name) => $"{kind}:{name}".ToLowerInvariant();

        /// <summary>
        /// SyncFromSceneAsync'in ürettiði kodun dönüþ deðerine koyduðu sabit önek.
        ///
        /// Amacý "cevap okundu ama sahne boþ" ile "cevap hiç okunamadý" durumlarýný
        /// ayýrmak. Bu önek olmadan boþ bir sahne, baþarýsýz bir okuma gibi görünüyor
        /// ve her temiz sahnede gereksiz bir uyarý basýlýyordu.
        /// </summary>
        private const string SyncMarker = "RECTS:";

        /// <summary>
        /// Yeni bir OTURUM baþlarken çaðrýlýr: yerleþim kaydý, yýðýn kaydý ve sprite
        /// önbelleði sýfýrlanýr.
        ///
        /// DÝKKAT: OllamaToolAgent bunu her GÖREVDE deðil, yalnýzca kullanýcý açýkça
        /// "sýfýrla" dediðinde ya da sahne gerçekten boþaltýldýðýnda çaðýrmalý. Sahne
        /// komutlar arasýnda KALICI: birinci komutta yerleþtirilmiþ bir panel ikinci
        /// komutta hâlâ orada, o yüzden dikdörtgen kaydý da korunmalý.
        ///
        /// Kayýt ile sahneyi TAZELEMEK için ResetSession deðil SyncFromScene kullanýn -
        /// o, kaydý silmek yerine sahnedeki gerçeðe göre yeniden kuruyor.
        /// </summary>
        public static void ResetSession()
        {
            LayoutRegistry.Clear();
            OverlapStacks.Clear();
            BackgroundColors.Clear();
            ColumnContainers.Clear();
            DockRemaining.Clear();
            _sessionAccent = null;
            _sessionAccentFromRequest = false;
            _spritesAvailable = null;
        }

        /// <summary>
        /// Sahnede ZATEN duran bir elemanýn dikdörtgenini kayda ekler.
        ///
        /// Çok komutlu akýþ için: kullanýcý iþi birden fazla komuta böldüðünde,
        /// ikinci komutta model birinci komutun panellerini görmüyor.
        /// </summary>
        public static void SeedExistingRect(string parent, string name,
            float minX, float minY, float maxX, float maxY)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                return;

            RegisterLayout(parent, name, new LayoutRect
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            });
        }

        /// <summary>
        /// Kayýtta ZATEN bulunan bir elemanýn dikdörtgenini günceller - ebeveyni
        /// bilmeye gerek kalmadan, ada göre arayarak.
        ///
        /// ============ NEDEN GEREKLÝ - DÖNGÜNÜN ÝKÝNCÝ YARISI ============
        /// GERÇEK ARIZA: model 'MapArea'yý çok geniþ kurdu, sonraki panel çakýþtý ve
        /// reddedildi. Doðru çözüm MapArea'yý küçültmek - ve model bunu
        /// manage_components ile YAPABÝLÝR. Ama o çaðrý UiMacroExpander'dan deðil,
        /// OllamaToolAgent'ýn ham yolundan geçiyor ve LayoutRegistry'ye HÝÇ
        /// DOKUNMUYORDU.
        ///
        /// Sonuç: model paneli gerçekten küçültüyor, sahne düzeliyor, ama kayýt hâlâ
        /// eski geniþ dikdörtgeni tutuyor. Bir sonraki çaðrý yine "çakýþýyor" cevabý
        /// alýyor. Model doðru þeyi yapsa bile ödüllendirilmiyordu - çýkýþý olmayan
        /// bir döngü.
        ///
        /// OllamaToolAgent baþarýlý her RectTransform set_property çaðrýsýndan sonra
        /// bunu çaðýrýyor. Eleman kayýtta yoksa hiçbir þey yapýlmýyor: ebeveynini
        /// bilmediðimiz bir objeyi yanlýþ bir ebeveynin altýna yazmak, çakýþma
        /// denetimini büsbütün bozardý.
        /// ================================================================
        /// </summary>
        public static bool UpdateExistingRect(string name, float minX, float minY, float maxX, float maxY)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            foreach (var entry in LayoutRegistry)
            {
                var siblings = entry.Value;

                for (int i = 0; i < siblings.Count; i++)
                {
                    if (!string.Equals(siblings[i].Key, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    siblings[i] = new KeyValuePair<string, LayoutRect>(name, new LayoutRect
                    {
                        MinX = minX,
                        MinY = minY,
                        MaxX = maxX,
                        MaxY = maxY
                    });

                    Debug.Log(
                        $"[UiMacroExpander] Layout registry updated for '{name}': it now occupies " +
                        string.Format(CultureInfo.InvariantCulture,
                            "x {0:0.###}-{1:0.###}, y {2:0.###}-{3:0.###}", minX, maxX, minY, maxY) +
                        ". The band it released is free for other elements.");

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yerleþim kaydýný SAHNEDEKÝ GERÇEÐE göre yeniden kurar.
        ///
        /// ============ NEDEN GEREKLÝ - EN BÜYÜK ARIZANIN KÖKÜ ============
        /// LayoutRegistry statik bir sözlük; sahne ise Unity'nin elinde. Ýkisi
        /// ayrýþýnca þu olur:
        ///
        ///   - Kullanýcý Hierarchy'den bir paneli elle siler  -> kayýt onu hâlâ
        ///     "dolu" sanar ve o bandý kullanmak isteyen her çaðrýyý reddeder.
        ///   - Ajan ikinci kez çalýþtýrýlýr  -> kayýtta birinci turun dikdörtgenleri
        ///     durur, model her denemede "çakýþýyor" cevabý alýr, sonunda kaçýþ yolu
        ///     olarak allowOverlap kullanýr ve ekran üst üste biner.
        ///
        /// Bu metot kaydý SÝLMÝYOR, sahneye göre YENÝDEN KURUYOR: sahnedeki her
        /// RectTransform'un gerçek anchor deðerleri okunuyor. Tek bir execute_code
        /// turu, görev baþýna bir kez.
        ///
        /// OllamaToolAgent'ýn her görev BAÞINDA bunu çaðýrmasý önerilir.
        /// ===============================================================
        /// </summary>
        public static async Task<bool> SyncFromSceneAsync(HashSet<string> knownObjectNames)
        {
            string code =
                "var sb = new System.Text.StringBuilder();\n" +
                "foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>()) {\n" +
                "  if (!rt.gameObject.scene.IsValid()) continue;\n" +
                "  var parent = rt.parent as RectTransform;\n" +
                "  if (parent == null) continue;\n" +
                "  sb.Append(parent.gameObject.name).Append('\\u001F')\n" +
                "    .Append(rt.gameObject.name).Append('\\u001F')\n" +
                "    .Append(rt.anchorMin.x.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\\u001F')\n" +
                "    .Append(rt.anchorMin.y.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\\u001F')\n" +
                "    .Append(rt.anchorMax.x.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\\u001F')\n" +
                "    .Append(rt.anchorMax.y.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\\u001E');\n" +
                "}\n" +
                "return \"" + SyncMarker + "\" + sb.ToString();";

            JObject call = Cmd("execute_code", new JObject { ["action"] = "execute", ["code"] = code });

            string raw;

            try
            {
                raw = await MCPExecutor.Execute(call);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UiMacroExpander] Scene sync could not run: {ex.Message}. The layout registry keeps whatever it had.");
                return false;
            }

            string payload = ExtractCodeReturnValue(raw, SyncMarker);

            // ============ BOÞ SAHNE BÝR HATA DEÐÝL ============
            // Eski sürüm sonucu yalnýzca ayraç karakteri taþýyorsa geçerli sayýyordu.
            // Kullanýcý Hierarchy'yi temizlediðinde sahnede hiç RectTransform kalmýyor,
            // dönen metin BOÞ oluyor ve ayraç da bulunmuyordu - yani tamamen normal bir
            // durum "Scene sync returned nothing usable" uyarýsý basýyordu.
            //
            // Çözüm: kodun dönüþ deðerine sabit bir ÖNEK koymak. Önek varsa cevap
            // okundu demektir; arkasýnýn boþ olmasý sahnenin boþ olduðunu söyler,
            // okumanýn baþarýsýz olduðunu deðil.
            // ==================================================
            if (payload == null)
            {
                Debug.LogWarning(
                    "[UiMacroExpander] Scene sync could not read a result from execute_code. " +
                    "The layout registry keeps whatever it had; overlap checks may not reflect the current scene.");
                return false;
            }

            LayoutRegistry.Clear();
            OverlapStacks.Clear();

            // Yeni görev, yeni tema: bir önceki ekranýn accent rengi buraya
            // sýzmamalý. Arka plan renkleri de sahneden okunamadýðý için sýfýrlanýyor;
            // bu görevde kurulan paneller onlarý yeniden kaydedecek.
            BackgroundColors.Clear();
            ColumnContainers.Clear();
            DockRemaining.Clear();
            _sessionAccent = null;
            _sessionAccentFromRequest = false;

            int count = 0;

            foreach (string record in payload.Split('\u001E'))
            {
                if (string.IsNullOrWhiteSpace(record))
                    continue;

                string[] f = record.Split('\u001F');

                if (f.Length < 6)
                    continue;

                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float minX) ||
                    !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float minY) ||
                    !float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float maxX) ||
                    !float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float maxY))
                {
                    continue;
                }

                RegisterLayout(f[0], f[1], new LayoutRect { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY });

                knownObjectNames?.Add(NameKey("gameobject", f[1]));
                count++;
            }

            if (count == 0)
            {
                Debug.Log("[UiMacroExpander] The scene has no UI elements yet - the layout registry starts empty. This is the normal state for a fresh scene.");
            }
            else
            {
                Debug.Log($"[UiMacroExpander] Layout registry rebuilt from the scene: {count} element(s). Overlap checks now reflect what is really there.");
            }

            return true;
        }

        public static async Task<string> Execute(JObject command, HashSet<string> knownObjectNames)
        {
            string macroName = command?["type"]?.ToString();
            JObject p = command?["params"] as JObject ?? new JObject();

            // Çaðýran null geçerse known.Contains NullReferenceException atýyordu ve
            // macro adý loglanmadan ölüyordu.
            HashSet<string> known = knownObjectNames
                                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                switch (macroName?.ToLowerInvariant())
                {
                    case "ensure_canvas": return await EnsureCanvas(p, known);
                    case "ensure_event_system": return await EnsureEventSystem(known);
                    case "create_ui_panel": return await CreatePanel(p, known);
                    case "create_ui_card": return await CreateCard(p, known);
                    case "create_ui_button": return await CreateButton(p, known);
                    case "create_ui_button_bar": return await CreateButtonBar(p, known);
                    case "create_ui_nav_item": return await CreateNavItem(p, known);
                    case "create_ui_label": return await CreateLabel(p, known);
                    case "create_ui_image": return await CreateImage(p, known);
                    case "create_ui_toggle": return await CreateToggle(p, known);
                    case "create_ui_row": return await CreateRow(p, known);
                    case "create_ui_slider": return await CreateSlider(p, known);
                    case "create_ui_progress_bar": return await CreateProgressBar(p, known);
                    case "create_ui_input": return await CreateInputField(p, known);
                    case "write_script": return WriteScript(p);

                    default:
                        // Liste MacroToolNames'ten ÜRETÝLÝYOR. Eskiden elle yazýlmýþtý
                        // ve eskimiþti: write_script hata mesajýnda hiç geçmiyordu.
                        return Error($"Unknown macro tool '{macroName}'. Available: {string.Join(", ", MacroToolNames)}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UiMacroExpander] Macro '{macroName}' threw: {ex}");
                return Error($"Macro '{macroName}' failed internally: {ex.Message}");
            }
        }

        // =====================================================
        // GÜVENLÝ KOD ÜRETÝMÝ
        // =====================================================

        /// <summary>
        /// Bir metni C# string literal'ine güvenle gömülebilir hale getirir.
        ///
        /// Rötuþ katmanýnýn ürettiði her kod parçasý obje adýný doðrudan bir literal'e
        /// gömüyor: GameObject.Find("{name}"). Bu ad MODELDEN geliyor. Ýçinde týrnak,
        /// ters bölü veya satýr sonu olan bir ad üretilen C#'ý bozuyor ve execute_code
        /// derlenmeden patlýyordu - üstelik hata mesajý sorunun adda olduðunu hiç
        /// göstermiyordu.
        /// </summary>
        private static string EscapeForCode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Kültürden BAÐIMSIZ C# float literal'i.
        ///
        /// Türkçe Windows kurulumunda ondalýk ayracý VÝRGÜL. float.ToString()
        /// varsayýlan kültürü kullanýrsa 0.52 deðeri "0,52" olarak yazýlýr ve üretilen
        /// C# kodu DERLENMEZ. Hata mesajý da sorunun bu olduðunu hiç göstermez.
        /// </summary>
        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        /// <summary>
        /// Bir GameObject adýnýn güvenli ve kullanýlabilir olup olmadýðýný denetler.
        ///
        /// '/' özellikle yasak: GameObject.Find onu HÝYERARÞÝ YOLU ayracý sayýyor,
        /// yani "Left/Panel" adlý bir obje sonradan hiçbir zaman bulunamýyor ve
        /// baðlama adýmlarý sessizce baþarýsýz oluyor.
        /// </summary>
        private static bool ValidateObjectName(string name, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "The object name is empty.";
                return false;
            }

            if (name.Length > 64)
            {
                error = $"The name '{Trim(name, 20)}...' is too long ({name.Length} chars). Use a short, descriptive name under 64 characters.";
                return false;
            }

            foreach (char c in name)
            {
                if (c == '"' || c == '\\' || c == '\n' || c == '\r' || c == '\t')
                {
                    error = $"The name '{Trim(name, 40)}' contains a quote, backslash or line break. Use plain letters, digits, spaces and underscores.";
                    return false;
                }

                if (c == '/')
                {
                    error = $"The name '{Trim(name, 40)}' contains '/'. Unity treats that as a hierarchy path separator, so the object could never be found again. Use an underscore instead.";
                    return false;
                }
            }

            return true;
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;

            return text.Substring(0, max);
        }

        // =====================================================
        // ANCHOR DENETÝMÝ
        // =====================================================

        /// <summary>
        /// Anchor deðerlerini denetler ve düzeltilebilir olanlarý düzeltir.
        ///
        /// Anchor'lar EBEVEYN DÝKDÖRTGENÝNÝN ORANI: 0 = sol/alt, 1 = sað/üst.
        /// Üç bozukluk eskiden sessizce geçiyordu:
        ///
        ///   1. 0-1 DIÞI DEÐER - model oraný piksel sanýp 1.35 gönderiyor; eleman
        ///      ebeveyninin dýþýna taþýyor. SESSÝZCE düzeltiliyor.
        ///   2. min > max - dikdörtgen ters, Unity negatif boyutla çiziyor.
        ///      SESSÝZCE düzeltiliyor.
        ///   3. min == max - o eksende boyut SIFIR. Eleman hiyerarþide var, ekranda
        ///      hiçbir þey yok. Teþhisi en zor durum. REDDEDÝLÝYOR, çünkü ne kadar
        ///      yer istediðini bilemeyiz.
        /// </summary>
        private static bool NormalizeAnchors(
            JObject anchorMin, JObject anchorMax, string objectName, out string error)
        {
            error = null;

            if (anchorMin == null || anchorMax == null)
                return true;

            bool clamped = false;
            clamped |= ClampAxis(anchorMin, "x");
            clamped |= ClampAxis(anchorMin, "y");
            clamped |= ClampAxis(anchorMax, "x");
            clamped |= ClampAxis(anchorMax, "y");

            if (clamped)
            {
                Debug.LogWarning(
                    $"[UiMacroExpander] '{objectName}': anchor values outside 0-1 were clamped. " +
                    "Anchors are FRACTIONS of the parent rectangle - anything above 1 puts the element outside its parent.");
            }

            SwapIfInverted(anchorMin, anchorMax, "x", objectName);
            SwapIfInverted(anchorMin, anchorMax, "y", objectName);

            const float minimumSpan = 0.002f;

            float minX = ReadAxis(anchorMin, "x", 0f);
            float maxX = ReadAxis(anchorMax, "x", 1f);
            float minY = ReadAxis(anchorMin, "y", 0f);
            float maxY = ReadAxis(anchorMax, "y", 1f);

            if (maxX - minX < minimumSpan)
            {
                error =
                    $"'{objectName}' would have ZERO WIDTH: anchorMin.x and anchorMax.x are both about {minX.ToString("0.###", CultureInfo.InvariantCulture)}. " +
                    "This call was NOT executed - the element would exist in the hierarchy but draw nothing on screen. " +
                    "Give the two x values a real gap, for example anchorMin.x = 0.05 and anchorMax.x = 0.45.";
                return false;
            }

            if (maxY - minY < minimumSpan)
            {
                error =
                    $"'{objectName}' would have ZERO HEIGHT: anchorMin.y and anchorMax.y are both about {minY.ToString("0.###", CultureInfo.InvariantCulture)}. " +
                    "This call was NOT executed - the element would exist in the hierarchy but draw nothing on screen. " +
                    "Give the two y values a real gap, for example anchorMin.y = 0.80 and anchorMax.y = 0.95.";
                return false;
            }

            return true;
        }

        private static bool ClampAxis(JObject holder, string axis)
        {
            float? value = holder[axis]?.ToObject<float?>();

            if (!value.HasValue)
                return false;

            float clamped = Mathf.Clamp01(value.Value);

            if (Mathf.Abs(clamped - value.Value) < 0.0001f)
                return false;

            holder[axis] = clamped;
            return true;
        }

        private static void SwapIfInverted(JObject minObj, JObject maxObj, string axis, string objectName)
        {
            float? minVal = minObj[axis]?.ToObject<float?>();
            float? maxVal = maxObj[axis]?.ToObject<float?>();

            if (!minVal.HasValue || !maxVal.HasValue || minVal.Value <= maxVal.Value)
                return;

            Debug.LogWarning(
                $"[UiMacroExpander] '{objectName}': anchorMin.{axis} was greater than anchorMax.{axis} - swapped. An inverted rectangle renders with negative size.");

            minObj[axis] = maxVal.Value;
            maxObj[axis] = minVal.Value;
        }

        private static float ReadAxis(JObject holder, string axis, float fallback)
        {
            return holder?[axis]?.ToObject<float?>() ?? fallback;
        }

        // =====================================================
        // ÇAKIÞMA KONTROLÜ
        // =====================================================

        /// <summary>Bir elemanýn ebeveyni içindeki normalize dikdörtgeni (0-1).</summary>
        private struct LayoutRect
        {
            public float MinX, MinY, MaxX, MaxY;

            public float Width => Mathf.Max(0f, MaxX - MinX);
            public float Height => Mathf.Max(0f, MaxY - MinY);
            public float Area => Width * Height;

            public float OverlapWidth(LayoutRect o) => Mathf.Min(MaxX, o.MaxX) - Mathf.Max(MinX, o.MinX);
            public float OverlapHeight(LayoutRect o) => Mathf.Min(MaxY, o.MaxY) - Mathf.Max(MinY, o.MinY);

            public float OverlapArea(LayoutRect o)
            {
                float w = OverlapWidth(o);
                float h = OverlapHeight(o);
                return (w <= 0f || h <= 0f) ? 0f : w * h;
            }

            /// <summary>
            /// Neredeyse ayný yeri mi kaplýyor. Sekme paneli yýðýnlarýný tespit etmek
            /// için - onlar bilerek üst üste.
            /// </summary>
            public bool IsNearlyIdentical(LayoutRect o)
            {
                const float tol = 0.06f;
                return Mathf.Abs(MinX - o.MinX) < tol && Mathf.Abs(MinY - o.MinY) < tol &&
                       Mathf.Abs(MaxX - o.MaxX) < tol && Mathf.Abs(MaxY - o.MaxY) < tol;
            }

            public override string ToString()
            {
                // KÜLTÜRDEN BAÐIMSIZ. Bu metin MODELE gidiyor ve model JSON'da nokta
                // kullanýyor; Türkçe kültürde "0,9" yazsaydýk kendi sayý biçimini
                // tanýmayan bir düzeltme mesajý vermiþ olurduk.
                return string.Format(CultureInfo.InvariantCulture,
                    "x {0:0.###}-{1:0.###}, y {2:0.###}-{3:0.###}", MinX, MaxX, MinY, MaxY);
            }
        }

        /// <summary>
        /// Ebeveyn adý -> altýndaki elemanlarýn dikdörtgenleri.
        ///
        /// Tamamen mekanik bir denetim - tasarým kararýna karýþmýyor, sadece "burasý
        /// dolu, þu bantlar boþ" diyor.
        /// </summary>
        private static readonly Dictionary<string, List<KeyValuePair<string, LayoutRect>>> LayoutRegistry =
            new Dictionary<string, List<KeyValuePair<string, LayoutRect>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Bilerek üst üste konmuþ eleman yýðýnlarý: ebeveyn -> yýðýndaki adlar.
        ///
        /// Þema modele "sekme panelleri için allowOverlap kullan" diyor; model
        /// kullanýyor ve altý içerik paneli ayný dikdörtgende, hepsi görünür hâlde
        /// kalýyor. Artýk yýðýndaki ÝLK eleman görünür kalýyor, sonrakiler
        /// SetActive(false) yapýlýyor.
        /// </summary>
        private static readonly Dictionary<string, List<string>> OverlapStacks =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Kardeþ alanýnýn bu oranýndan fazlasý örtüþürse çaðrý reddedilir.
        ///
        /// %18: yuvarlamadan doðan gerçek örtüþmeler %1-3 aralýðýnda kalýyor. Daha
        /// yüksek bir eþik (%40 gibi) iki etiketin okunamaz hâlde üst üste binmesine
        /// izin veriyor.
        /// </summary>
        private const float OverlapThreshold = 0.18f;

        private static LayoutRect ToRect(JObject anchorMin, JObject anchorMax)
        {
            return new LayoutRect
            {
                MinX = ReadAxis(anchorMin, "x", 0f),
                MinY = ReadAxis(anchorMin, "y", 0f),
                MaxX = ReadAxis(anchorMax, "x", 1f),
                MaxY = ReadAxis(anchorMax, "y", 1f)
            };
        }

        /// <summary>
        /// Yeni elemanýn kardeþleriyle çakýþýp çakýþmadýðýný denetler. Çakýþýyorsa
        /// modele hangi kardeþle çakýþtýðýný VE hangi bantlarýn boþ olduðunu söyler -
        /// sadece "hayýr" demek modeli rastgele denemeye iter.
        /// </summary>
        private static bool WouldOverlapSibling(
            string parent, string name, LayoutRect rect, out string message)
        {
            message = null;

            if (string.IsNullOrWhiteSpace(parent))
                return false;

            if (!LayoutRegistry.TryGetValue(parent, out var siblings) || siblings.Count == 0)
                return false;

            foreach (var sibling in siblings)
            {
                // Ayný ad yeniden gelirse bu bir güncellemedir, çakýþma deðil.
                if (string.Equals(sibling.Key, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                float overlap = rect.OverlapArea(sibling.Value);
                if (overlap <= 0f)
                    continue;

                float smaller = Mathf.Min(rect.Area, sibling.Value.Area);
                if (smaller <= 0f || overlap / smaller < OverlapThreshold)
                    continue;

                // ÇAKIÞMANIN EKSENÝNÝ TESPÝT ET. Yan yana dizilmiþ elemanlarda
                // (bir komut çubuðundaki butonlar) dikey bant bilgisi vermek tamamen
                // yanlýþ yönü gösterir ve model ayný hatayý tekrarlar.
                bool horizontal = rect.OverlapWidth(sibling.Value) < rect.OverlapHeight(sibling.Value);

                int percent = Mathf.RoundToInt(overlap / smaller * 100f);

                // ============ MESAJ ARTIK ÇIKIÞ YOLU VERÝYOR ============
                // GERÇEK ARIZA: model 'MapArea'yý x 0.04-0.96 kurdu, sonra telemetri
                // panelini DOÐRU banda (x 0.76-1) koymak istedi ve reddedildi. Mesaj
                // "boþ bir bant bul" diyordu ama BOÞ BANT YOKTU - kardeþ her þeyi
                // kaplamýþtý. Model çýkmazda kaldý, beþ baþarýsýz adýmdan sonra görev
                // durdu.
                //
                // Doðru cevap "baþka yere koy" deðil, "kardeþi küçült" idi. Artýk
                // mesaj bunu söylüyor VE gönderilecek çaðrýyý hazýr veriyor - modelin
                // kendi baþýna doðru anchor'larý hesaplamasý gerekmiyor.
                //
                // Kýrpma yalnýzca sonuç makul kalýyorsa öneriliyor: kardeþ
                // MinShrunkSpan'in altýna inecekse o yol kapalý demektir ve eski
                // "boþ bant" tavsiyesine dönülüyor.
                // ========================================================
                string resolution = DescribeResolution(parent, sibling.Key, sibling.Value, rect, horizontal);

                message =
                    $"'{name}' at [{rect}] would overlap sibling '{sibling.Key}' at [{sibling.Value}] by {percent}%. " +
                    "This call was NOT executed - two siblings sharing a rectangle are drawn on top of each other.\n" +
                    resolution;

                return true;
            }

            return false;
        }


        /// <summary>Kýrpýldýktan sonra bir kardeþin kalabileceði en küçük geniþlik/yükseklik.</summary>
        private const float MinShrunkSpan = 0.12f;

        /// <summary>
        /// Çakýþmanýn NASIL çözüleceðini anlatýr.
        ///
        /// Ýki durum var ve ikisi tamamen farklý cevaplar gerektiriyor:
        ///
        ///   1. Boþ bir bant VAR  -> elemaný oraya taþý, ya da anchor'larý hiç verme.
        ///   2. Boþ bant YOK      -> çakýþýlan kardeþ fazla yer kaplýyor demektir.
        ///      Tek doðru çözüm ONU küçültmek. Bu durumda gönderilecek
        ///      manage_components çaðrýsý hazýr olarak veriliyor: model doðru
        ///      anchor'larý kendi hesaplamak zorunda kalmýyor, çünkü tam da o hesabý
        ///      yapamadýðý için buraya düþtü.
        /// </summary>
        private static string DescribeResolution(
            string parent, string siblingName, LayoutRect sibling, LayoutRect incoming, bool horizontal)
        {
            string bands = DescribeFreeBands(parent, horizontal);
            bool anyFreeBand = bands.IndexOf("Free ", StringComparison.Ordinal) >= 0;

            if (anyFreeBand)
            {
                return bands + "\n" +
                       "EASIEST FIX: omit anchorMin and anchorMax entirely. The agent then places this element below " +
                       "the last one under the same parent, with correct spacing and no arithmetic on your side.\n" +
                       "If this element is a PANEL that should own a band of the screen (a bar, a side column, the centre area), " +
                       "pass \"region\":\"top\"/\"bottom\"/\"left\"/\"right\"/\"center\" instead of anchors - the agent cuts it out of the free area.";
            }

            // Boþ bant yok: kardeþi kýrpmayý dene.
            LayoutRect shrunk = sibling;
            bool possible = false;

            if (horizontal)
            {
                if (incoming.MinX > sibling.MinX && incoming.MinX - 0.02f - sibling.MinX >= MinShrunkSpan)
                {
                    shrunk.MaxX = incoming.MinX - 0.02f;
                    possible = true;
                }
                else if (incoming.MaxX < sibling.MaxX && sibling.MaxX - (incoming.MaxX + 0.02f) >= MinShrunkSpan)
                {
                    shrunk.MinX = incoming.MaxX + 0.02f;
                    possible = true;
                }
            }
            else
            {
                if (incoming.MinY > sibling.MinY && incoming.MinY - 0.02f - sibling.MinY >= MinShrunkSpan)
                {
                    shrunk.MaxY = incoming.MinY - 0.02f;
                    possible = true;
                }
                else if (incoming.MaxY < sibling.MaxY && sibling.MaxY - (incoming.MaxY + 0.02f) >= MinShrunkSpan)
                {
                    shrunk.MinY = incoming.MaxY + 0.02f;
                    possible = true;
                }
            }

            if (!possible)
            {
                return bands + "\n" +
                       $"'{siblingName}' fills this container and cannot be shrunk enough to make room. " +
                       "Put this element inside a different parent, or leave it out.";
            }

            string call = string.Format(CultureInfo.InvariantCulture,
                "{{\"type\":\"manage_components\",\"params\":{{\"action\":\"set_property\",\"target\":\"{0}\"," +
                "\"componentType\":\"RectTransform\",\"properties\":{{" +
                "\"anchorMin\":{{\"x\":{1:0.###},\"y\":{2:0.###}}}," +
                "\"anchorMax\":{{\"x\":{3:0.###},\"y\":{4:0.###}}}," +
                "\"offsetMin\":{{\"x\":0,\"y\":0}},\"offsetMax\":{{\"x\":0,\"y\":0}}}}}}}}",
                siblingName, shrunk.MinX, shrunk.MinY, shrunk.MaxX, shrunk.MaxY);

            return
                $"THERE IS NO FREE BAND: '{siblingName}' was given more room than it needs and is covering the space " +
                $"this element belongs in. The fix is to SHRINK '{siblingName}' first, not to move this element.\n" +
                "Send EXACTLY this call now, then re-send your original call unchanged:\n" +
                call + "\n" +
                "The layout registry updates itself from that call, so your original call will then be accepted.";
        }

        /// <summary>
        /// Bir ebeveynin altýnda hangi bantlarýn boþ olduðunu insanca anlatýr.
        /// </summary>
        private static string DescribeFreeBands(string parent, bool horizontal)
        {
            string axisName = horizontal ? "horizontal" : "vertical";
            string axisField = horizontal ? "x" : "y";

            if (!LayoutRegistry.TryGetValue(parent, out var siblings) || siblings.Count == 0)
                return $"The whole 0.00-1.00 {axisName} range is free.";

            var occupied = siblings
                .Select(s => horizontal
                    ? new KeyValuePair<float, float>(s.Value.MinX, s.Value.MaxX)
                    : new KeyValuePair<float, float>(s.Value.MinY, s.Value.MaxY))
                .OrderBy(b => b.Key)
                .ToList();

            var free = new List<string>();
            float cursor = 0f;

            foreach (var band in occupied)
            {
                if (band.Key - cursor > 0.04f)
                {
                    free.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00}-{1:0.00}", cursor, band.Key));
                }

                cursor = Mathf.Max(cursor, band.Value);
            }

            if (1f - cursor > 0.04f)
            {
                free.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00}-1.00", cursor));
            }

            if (free.Count == 0)
            {
                return $"Every {axisName} band under '{parent}' is already taken - this container is full, so the element belongs somewhere else.";
            }

            return $"Free {axisName} bands (anchorMin.{axisField} - anchorMax.{axisField}) under '{parent}': {string.Join(" , ", free)}.";
        }

        /// <summary>Bu ad yerleþim kaydýnda geçiyor mu - ebeveyn ya da çocuk olarak.</summary>
        private static bool IsKnownLayoutName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (LayoutRegistry.ContainsKey(name))
                return true;

            foreach (var entry in LayoutRegistry)
            {
                foreach (var child in entry.Value)
                {
                    if (string.Equals(child.Key, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Bu ebeveynin altýnda kayýtlý bir eleman var mý.</summary>
        private static bool ParentHasChildren(string parent)
        {
            return !string.IsNullOrWhiteSpace(parent) &&
                   LayoutRegistry.TryGetValue(parent, out var children) &&
                   children.Count > 0;
        }

        private static void RegisterLayout(string parent, string name, LayoutRect rect)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                return;

            if (!LayoutRegistry.TryGetValue(parent, out var siblings))
            {
                siblings = new List<KeyValuePair<string, LayoutRect>>();
                LayoutRegistry[parent] = siblings;
            }

            for (int i = 0; i < siblings.Count; i++)
            {
                if (string.Equals(siblings[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    siblings[i] = new KeyValuePair<string, LayoutRect>(name, rect);
                    return;
                }
            }

            siblings.Add(new KeyValuePair<string, LayoutRect>(name, rect));
        }

        /// <summary>
        /// Kayýttan bir elemaný siler. Geri alma sýrasýnda kullanýlýyor - aksi hâlde
        /// baþarýsýz bir çaðrýnýn dikdörtgeni kayýtta kalýp sonraki meþru çaðrýlarý
        /// engelliyordu.
        /// </summary>
        private static void UnregisterLayout(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(parent) || !LayoutRegistry.TryGetValue(parent, out var siblings))
                return;

            siblings.RemoveAll(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
        }



        /// <summary>
        /// Bir kartýn adý verilmiþse, çaðrýyý kartýn ÝÇERÝK ALANINA yönlendirir.
        ///
        /// ============ MODELÝN EN SIK HATASI ============
        /// create_ui_card iki kullanýlabilir ad üretiyor: kartýn kendisi ve
        /// '<name>Content'. Satýrlar ÝKÝNCÝSÝNE eklenmeli. Prompt bunu üç ayrý yerde
        /// söylüyor, sonuç JSON'unda 'createdName' olarak da içerik alaný dönüyor -
        /// ama model yine de sýk sýk kartýn kendi adýný yazýyor.
        ///
        /// Sonucu sessiz ve ölümcül: kartýn altýnda zaten baþlýk (y 0.82-0.98) ve
        /// içerik alaný (y 0.04-0.78) duruyor, yani kart DOLU. Otomatik akýþ haklý
        /// olarak "yer yok" diyor:
        ///
        ///     "Auto-flow could not fit under 'Track0117Card':
        ///      only 0 of vertical space is left"
        ///
        /// Gerçek testte bu tek hata YEDÝ kartýn dördünü boþ býraktý - ekranda
        /// baþlýklý ama içi boþ kutular kaldý.
        ///
        /// Modelin öðrenmesini beklemek yerine niyetini uyguluyoruz: birisi bir kartýn
        /// içine satýr koymak istiyorsa kastettiði yer içerik alanýdýr, kartýn baþlýkla
        /// içerik arasýndaki boþluðu deðil. Belirsizlik yok, tahmin yok.
        ///
        /// YALNIZCA kart yapýsý gerçekten varsa devreye giriyor: hem '<name>Content'
        /// kayýtlý olmalý hem de kartýn kendisi altýnda kayýtlý çocuklar bulunmalý.
        /// Bu koþullar saðlanmýyorsa çaðrý olduðu gibi geçiyor.
        /// ==============================================
        /// </summary>
        private static string RedirectToCardContent(string parent)
        {
            if (string.IsNullOrWhiteSpace(parent))
                return parent;

            // ============ 'XContentContent' - FAZLA EK KIRPILIYOR ============
            // GERÇEK ARIZA, ÜÇ KARTI BÝRDEN BOÞ BIRAKTI:
            //     "Parent specified ('OutputCardContentContent') but not found."
            //
            // Boþ konteyner uyarýsý modele "parent'ý 'OutputCardContent' yap" diyor.
            // Model ise kartlara satýr eklerken öðrendiði kuralý uyguluyor ve sonuna
            // bir kez daha 'Content' ekliyor. Ortaya sahnede olmayan bir ad çýkýyor,
            // manage_gameobject "parent bulunamadý" diyor ve satýr hiç kurulmuyor.
            // Üç kartýn üçünde de ayný hata oldu; ekranda baþlýklý ama bomboþ üç kutu
            // kaldý.
            //
            // Niyet belirsiz deðil: böyle bir ad YOKSA ve son 'Content' ekini atýnca
            // GERÇEKTEN VAR OLAN bir ad çýkýyorsa, kastedilen odur. Tahmin deðil,
            // kayýttan doðrulanan bir düzeltme.
            // ================================================================
            for (int i = 0; i < 2; i++)
            {
                if (IsKnownLayoutName(parent) || !parent.EndsWith("Content", StringComparison.OrdinalIgnoreCase))
                    break;

                string trimmed = parent.Substring(0, parent.Length - "Content".Length);

                if (!IsKnownLayoutName(trimmed))
                    break;

                Debug.Log(
                    $"[UiMacroExpander] '{parent}' does not exist, but '{trimmed}' does - the suffix was repeated. " +
                    $"This element was placed in '{trimmed}' instead.");

                parent = trimmed;
            }

            // Zaten içerik alanýna gönderiliyorsa dokunma.
            if (parent.EndsWith("Content", StringComparison.OrdinalIgnoreCase))
                return parent;

            string contentName = parent + "Content";

            // Ýçerik alaný kartýn altýnda kayýtlý mý? Deðilse bu bir kart deðildir.
            if (!LayoutRegistry.TryGetValue(parent, out var children))
                return parent;

            bool hasContent = false;

            foreach (var child in children)
            {
                if (string.Equals(child.Key, contentName, StringComparison.OrdinalIgnoreCase))
                {
                    hasContent = true;
                    break;
                }
            }

            if (!hasContent)
                return parent;

            Debug.Log(
                $"[UiMacroExpander] '{parent}' is a card, so this element was placed in its content area '{contentName}' instead. " +
                "That is where a card's rows belong - the card itself already holds its title and content frame.");

            return contentName;
        }


        // =====================================================
        // PÝKSEL BÝLÝNÇLÝ YÜKSEKLÝK
        // =====================================================

        /// <summary>
        /// CanvasScaler referans yüksekliði. EnsureCanvas 1920x1080 kuruyor; bütün
        /// piksel hesaplarý bu referansa göre.
        /// </summary>
        private const float ReferenceScreenHeight = 1080f;

        /// <summary>
        /// Bir elemanýn EKRANIN ne kadarýný dikeyde kapladýðýný hesaplar (0-1).
        ///
        /// ============ NEDEN GEREKLÝ - SEYREK EKRANIN KÖKÜ ============
        /// Yerleþim sistemi tamamen ORAN tabanlý: bir satýr ebeveyninin %17'si. Bu,
        /// ebeveyn küçükken doðru, büyükken felakete dönüþüyor. Gerçek test: tek bir
        /// kart bir sekme panelinin tamamýný kapladý (~700 piksel), içindeki dört
        /// satýrýn her biri ~120 piksel oldu ve 15 puntoluk yazýlar dev boþluklarýn
        /// ortasýnda kayboldu. Gerçek uygulamalarda bir ayar satýrý 44-56 pikseldir.
        ///
        /// Oraný sabit tutmak yerine, ebeveynin EKRANDAKÝ gerçek yüksekliðini
        /// biliyorsak satýra piksel cinsinden üst sýnýr koyabiliriz. Kayýt zaten her
        /// elemanýn ebeveynine göre oranýný tutuyor; zincir boyunca çarpýnca mutlak
        /// oran çýkýyor:
        ///
        ///     satýr -> kart içeriði -> kart -> sekme paneli -> içerik alaný -> Canvas
        ///     0.17  x  0.79         x 0.94 x  1.0         x  0.92        x  1.0
        ///
        /// Kayýtta bulunmayan bir ebeveyn (kök ya da sahne dýþý) 1.0 sayýlýyor - yani
        /// en kötü durumda sýnýr biraz gevþek kalýyor, hiçbir zaman sýkýlaþmýyor.
        /// ============================================================
        /// </summary>
        private static float AbsoluteHeight(string name)
        {
            return AbsoluteHeight(name, 0);
        }

        private static float AbsoluteHeight(string name, int depth)
        {
            // Derinlik sýnýrý: kayýtta bozuk bir döngü olsa bile sonsuz özyineleme yok.
            if (string.IsNullOrWhiteSpace(name) || depth > 16)
                return 1f;

            foreach (var entry in LayoutRegistry)
            {
                foreach (var child in entry.Value)
                {
                    if (string.Equals(child.Key, name, StringComparison.OrdinalIgnoreCase))
                        return child.Value.Height * AbsoluteHeight(entry.Key, depth + 1);
                }
            }

            return 1f;
        }

        /// <summary>
        /// Bir elemanýn varsayýlan yükseklik oranýný, EKRANDA en fazla maxPixels
        /// kaplayacak þekilde sýnýrlar.
        ///
        /// Model 'height' verdiyse ona dokunulmuyor: bilerek büyük bir eleman
        /// istiyorsa (bir özet kartý, büyük bir okuma deðeri) o karar onun.
        /// Sýnýr yalnýzca VARSAYILANA uygulanýyor.
        ///
        /// Küçük bir ebeveynde sýnýr zaten varsayýlandan gevþek kalýyor ve hiçbir
        /// þey deðiþmiyor - bu deðiþiklik yalnýzca büyük konteynerlerdeki seyrekliði
        /// kaldýrýyor.
        /// </summary>
        /// <param name="minPixels">
        /// Elemanýn inebileceði en küçük piksel yüksekliði. Üst sýnýr tek baþýna
        /// yetmiyordu: 40 puntoluk bir okuma deðeri, ebeveyn küçükse varsayýlan oranla
        /// ~40 piksellik bir satýra sýkýþýyor ve yazý kutusunun dýþýna taþýp kart
        /// baþlýðýnýn üstüne biniyordu. Punto büyüdükçe taban da büyümeli.
        /// </param>
        private static float PixelCappedHeight(JObject p, string parent, float defaultFraction, float maxPixels, float minPixels = 0f)
        {
            float? explicitHeight = p["height"]?.ToObject<float?>();

            if (explicitHeight.HasValue)
                return explicitHeight.Value;

            float parentPixels = AbsoluteHeight(parent) * ReferenceScreenHeight;

            if (parentPixels <= 1f)
                return defaultFraction;

            float capped = Mathf.Min(defaultFraction, maxPixels / parentPixels);

            if (minPixels > 0f)
            {
                // Taban, üst sýnýrý geçebilir - büyük puntoda doðru olan da bu.
                // 0.9'u aþmasýn: eleman ebeveyninin tamamýný yemesin.
                capped = Mathf.Min(0.9f, Mathf.Max(capped, minPixels / parentPixels));
            }

            return capped;
        }

        /// <summary>CanvasScaler referans geniþliði - EnsureCanvas 1920x1080 kuruyor.</summary>
        private const float ReferenceScreenWidth = 1920f;

        /// <summary>
        /// Bir elemanýn EKRANIN ne kadarýný yatayda kapladýðýný hesaplar (0-1).
        /// AbsoluteHeight'ýn yatay karþýlýðý: metin geniþliði hesaplarý oranla deðil
        /// pikselle yapýlmalý, çünkü ayný oran dar bir sütunda ve geniþ bir kartta
        /// tamamen farklý sayýda karakter demek.
        /// </summary>
        private static float AbsoluteWidth(string name)
        {
            return AbsoluteWidth(name, 0);
        }

        private static float AbsoluteWidth(string name, int depth)
        {
            if (string.IsNullOrWhiteSpace(name) || depth > 16)
                return 1f;

            foreach (var entry in LayoutRegistry)
            {
                foreach (var child in entry.Value)
                {
                    if (string.Equals(child.Key, name, StringComparison.OrdinalIgnoreCase))
                        return child.Value.Width * AbsoluteWidth(entry.Key, depth + 1);
                }
            }

            return 1f;
        }

        /// <summary>Bir dikdörtgenin ekrandaki piksel yüksekliði.</summary>
        private static float PixelHeightOf(string parent, JObject anchorMin, JObject anchorMax)
        {
            float span = ReadAxis(anchorMax, "y", 1f) - ReadAxis(anchorMin, "y", 0f);
            return Mathf.Max(1f, span * AbsoluteHeight(parent) * ReferenceScreenHeight);
        }

        // =====================================================
        // OTURUM ACCENT RENGÝ
        // =====================================================

        /// <summary>
        /// Bu görevde modelin kullandýðý accent rengi.
        ///
        /// ============ NEDEN GEREKLÝ - TUTARSIZ TEMA ============
        /// Gerçek test: "amber accent" istendi. Model RPM çubuðuna amber fillColor,
        /// nav göstergesine amber accentColor verdi - ama toggle için onColor
        /// VERMEDÝ. Toggle varsayýlan maviyle kuruldu ve ekranda tek bir mavi eleman
        /// amber temanýn ortasýnda kaldý.
        ///
        /// Model accent rengini bir kez söylediyse, sonraki elemanlarda tekrar
        /// söylemesini beklemek yerine hatýrlýyoruz. Varsayýlan yalnýzca model HÝÇ
        /// accent vermemiþse devreye giriyor.
        ///
        /// Görev baþýnda (SyncFromSceneAsync) ve oturum sýfýrlanýrken temizleniyor:
        /// bir önceki ekranýn rengi yeni ekrana sýzmamalý.
        /// =======================================================
        /// </summary>
        private static JObject _sessionAccent;

        /// <summary>
        /// Oturum accent'i kullanýcýnýn isteðinden mi geldi? Geldiyse modelin seçtiði
        /// renkler onu EZMEZ.
        /// </summary>
        private static bool _sessionAccentFromRequest;

        /// <summary>Adý geçen accent renkleri: eþanlamlýlar (Ýngilizce + Türkçe) ve 0-1 RGB.</summary>
        private sealed class NamedAccent
        {
            public readonly string[] Words;
            public readonly Color Color;

            public NamedAccent(Color color, params string[] words)
            {
                Color = color;
                Words = words;
            }
        }

        /// <summary>
        /// Tonlar koyu tema üstünde okunacak þekilde seçildi: hepsi #1B222B gibi bir
        /// kart zemininde WCAG 3:1 üzerinde.
        /// </summary>
        private static readonly NamedAccent[] NamedAccents =
        {
            new NamedAccent(new Color(1.00f, 0.70f, 0.18f), "amber", "kehribar"),
            new NamedAccent(new Color(0.20f, 0.85f, 0.95f), "cyan", "camgöbeði", "camgobegi"),
            new NamedAccent(new Color(0.18f, 0.82f, 0.76f), "turquoise", "turkuaz"),
            new NamedAccent(new Color(0.12f, 0.70f, 0.68f), "teal"),
            new NamedAccent(new Color(0.36f, 0.55f, 1.00f), "blue", "mavi"),
            new NamedAccent(new Color(0.30f, 0.80f, 0.45f), "green", "yeþil", "yesil"),
            new NamedAccent(new Color(0.92f, 0.32f, 0.30f), "red", "kýrmýzý", "kirmizi"),
            new NamedAccent(new Color(1.00f, 0.55f, 0.15f), "orange", "turuncu"),
            new NamedAccent(new Color(0.64f, 0.48f, 1.00f), "purple", "violet", "mor"),
            new NamedAccent(new Color(1.00f, 0.84f, 0.22f), "yellow", "sarý", "sari"),
            new NamedAccent(new Color(1.00f, 0.46f, 0.70f), "pink", "pembe")
        };

        /// <summary>
        /// Kullanýcýnýn isteðinde adý geçen accent rengini oturum varsayýlaný yapar.
        ///
        /// ============ NEDEN GEREKLÝ - MODEL RENGÝ UNUTUYOR ============
        /// Ayný "Amber accent" isteði iki kez çalýþtýrýldý:
        ///   - Birinci turda model amber verdi, ekran amberdi.
        ///   - Ýkinci turda model accent'i BEYAZ seçti. RememberAccent o beyazý
        ///     sadakatle yaydý: toggle, çubuklar, nav göstergesi hep beyaz oldu.
        /// Tutarlýlýk mekanizmasý doðru çalýþtý - ama yanlýþ rengi tutarlý yaptý.
        ///
        /// Kullanýcý rengi zaten söylemiþ. Modelin hatýrlamasýna güvenmek yerine
        /// isteðin kendisi okunuyor ve renk KÝLÝTLENÝYOR: model renk vermeyen her
        /// eleman için bu kullanýlýyor, RememberAccent bu görevde onu ezemiyor.
        ///
        /// Model bir eleman için AÇIKÇA renk verirse o eleman yine o rengi alýyor -
        /// "bataryayý yeþil yap", "Görevi Durdur kýrmýzý olsun" gibi bilinçli
        /// istisnalar bozulmasýn.
        ///
        /// Eþleþme yalnýzca renk adý "accent"/"vurgu" kelimesinin yakýnýndaysa: "Hold
        /// Fire butonu amber olsun" bir buton rengidir, tema accent'i deðil.
        /// ===============================================================
        /// </summary>
        public static void SeedAccentFromRequest(string userPrompt)
        {
            _sessionAccentFromRequest = false;

            if (string.IsNullOrWhiteSpace(userPrompt))
                return;

            string text = userPrompt.ToLowerInvariant();

            foreach (NamedAccent accent in NamedAccents)
            {
                foreach (string word in accent.Words)
                {
                    string w = Regex.Escape(word);

                    // "amber accent", "turkuaz vurgu", "accent: amber", "vurgu rengi turkuaz"
                    bool nearAccent =
                        Regex.IsMatch(text, @"\b" + w + @"\s+(accent|vurgu)") ||
                        Regex.IsMatch(text, @"\b(accent|vurgu)\w*[\s:=\-]{1,3}(\w+\s+){0,2}" + w + @"\b");

                    if (!nearAccent)
                        continue;

                    _sessionAccent = ToJson(accent.Color);
                    _sessionAccentFromRequest = true;

                    Debug.Log(
                        $"[UiMacroExpander] Accent colour '{word}' taken from the request and locked for this task. " +
                        "Every element the model gives no explicit colour will use it.");

                    return;
                }
            }
        }

        /// <summary>Modelin açýkça verdiði bir accent rengini hatýrlar.</summary>
        private static void RememberAccent(JObject p, params string[] fields)
        {
            if (p == null || fields == null)
                return;

            // Kullanýcý rengi isteðinde söylediyse o kazanýr; modelin farklý seçimi
            // yalnýzca o tek elemanda kalýr, temaya yayýlmaz.
            if (_sessionAccentFromRequest)
                return;

            foreach (string field in fields)
            {
                if (p[field] is JObject c)
                {
                    // Görünmez ya da neredeyse görünmez bir renk accent olamaz.
                    float alpha = c["a"]?.ToObject<float?>() ?? 1f;

                    if (alpha < 0.5f)
                        continue;

                    _sessionAccent = (JObject)c.DeepClone();
                    return;
                }
            }
        }

        // =====================================================
        // RENK KAYDI VE KONTRAST DENETÝMÝ
        // =====================================================

        /// <summary>
        /// Obje adý -> o objenin arka plan rengi (RGB, 0-1).
        ///
        /// ============ NEDEN VAR - SESSÝZ OKUNMAZLIK ============
        /// Model koyu bir kartýn üstüne koyu gri yazý koyduðunda hiçbir þey hata
        /// vermiyor: çaðrý baþarýlý, obje kuruluyor, Hierarchy'de her þey yerinde.
        /// Ama ekranda yazý GÖRÜNMÜYOR. Kullanýcý bunu "bulanýk" ya da "eksik" diye
        /// bildiriyor, oysa sorun kontrast.
        ///
        /// Çakýþma denetimi yerleþim için ne yapýyorsa bu da renk için onu yapýyor:
        /// mekanik, ölçülebilir bir kusuru çaðrý çalýþmadan önce yakalýyor.
        /// ======================================================
        /// </summary>
        private static readonly Dictionary<string, Color> BackgroundColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// WCAG kontrast oranýnýn altýndaki metin okunmaz sayýlýr.
        ///
        /// 3.0 seçildi: WCAG'ýn büyük metin için verdiði alt sýnýr. 4.5 (küçük metin
        /// standardý) UI'da fazla katý kalýyor ve kasýtlý olarak soluk býrakýlmýþ
        /// ikincil metinleri de yakalardý - oysa onlarýn soluk olmasý tasarým tercihi.
        /// 3.0 gerçekten okunmayaný yakalýyor, sadece soluk olaný deðil.
        /// </summary>
        private const float MinContrastRatio = 3.0f;

        private static Color ToColor(JObject c, Color fallback)
        {
            if (c == null)
                return fallback;

            return new Color(
                c["r"]?.ToObject<float?>() ?? fallback.r,
                c["g"]?.ToObject<float?>() ?? fallback.g,
                c["b"]?.ToObject<float?>() ?? fallback.b,
                c["a"]?.ToObject<float?>() ?? fallback.a);
        }

        private static JObject ToJson(Color c)
        {
            return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
        }

        /// <summary>Bir elemanýn arka plan rengini kaydeder - metin kontrastý bunun üstünden hesaplanýyor.</summary>
        private static void RegisterBackground(string name, JObject color)
        {
            if (string.IsNullOrWhiteSpace(name) || color == null)
                return;

            Color c = ToColor(color, Color.black);

            // Þeffaf bir yüzey kendi rengini dayatmýyor: arkasýndaki ne ise o görünür.
            // Kaydetmek yanlýþ bir referans noktasý üretirdi.
            if (c.a < 0.2f)
                return;

            BackgroundColors[name] = c;
        }

        /// <summary>
        /// Bir elemanýn arkasýnda hangi rengin olduðunu bulur - kendisinde kayýt yoksa
        /// verilen yedek zincirinde arar.
        /// </summary>
        private static Color ResolveBackground(params string[] candidates)
        {
            if (candidates != null)
            {
                foreach (string name in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(name) &&
                        BackgroundColors.TryGetValue(name, out Color found))
                    {
                        return found;
                    }
                }
            }

            // Hiçbir þey bilinmiyorsa projenin koyu temasý varsayýlýyor. Yanlýþ
            // olabilir ama açýk renk varsaymaktan daha güvenli: bu projede ekranlarýn
            // tamamý koyu.
            return new Color(0.07f, 0.09f, 0.12f, 1f);
        }

        /// <summary>WCAG göreli parlaklýk.</summary>
        private static float RelativeLuminance(Color c)
        {
            return 0.2126f * LinearizeChannel(c.r) +
                   0.7152f * LinearizeChannel(c.g) +
                   0.0722f * LinearizeChannel(c.b);
        }

        private static float LinearizeChannel(float v)
        {
            v = Mathf.Clamp01(v);
            return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>Ýki renk arasýndaki WCAG kontrast oraný (1.0 - 21.0).</summary>
        private static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);

            float lighter = Mathf.Max(la, lb);
            float darker = Mathf.Min(la, lb);

            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary>
        /// Metin rengini arka planla karþýlaþtýrýr; okunmayacak kadar yakýnsa
        /// OKUNABÝLÝR bir tona çeker ve neden deðiþtirdiðini loglar.
        ///
        /// Renk TAMAMEN deðiþtirilmiyor: ayný ton korunup parlaklýðý kaydýrýlýyor, yani
        /// modelin seçtiði renk ailesi bozulmuyor. Koyu bir zeminde koyu mavi bir yazý
        /// açýk maviye dönüþüyor, beyaza deðil.
        /// </summary>
        private static JObject EnsureReadable(JObject textColor, Color background, string elementName, string role)
        {
            if (textColor == null)
                return null;

            Color text = ToColor(textColor, Color.white);

            // Görünmez olmasý istenmiþ olabilir - alfa tasarým kararýdýr, dokunulmuyor.
            if (text.a < 0.2f)
                return textColor;

            float ratio = ContrastRatio(text, background);

            if (ratio >= MinContrastRatio)
                return textColor;

            Color.RGBToHSV(text, out float h, out float sat, out float val);

            bool backgroundIsDark = RelativeLuminance(background) < 0.18f;

            // Zemin koyuysa yukarý, açýksa aþaðý doðru itiliyor. Doygunluk biraz
            // düþürülüyor: aþýrý doygun bir renk parlaklýðý artsa bile okunmuyor.
            Color adjusted = Color.HSVToRGB(h, Mathf.Min(sat, 0.55f), backgroundIsDark ? 0.95f : 0.18f);
            adjusted.a = text.a;

            float newRatio = ContrastRatio(adjusted, background);

            Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                "[UiMacroExpander] '{0}' {1} colour was unreadable on its background (contrast {2:0.0}:1, minimum {3:0.0}:1) " +
                "and was lightened to {4:0.0}:1. The hue was kept - only the brightness changed. " +
                "Pass a colour with more contrast if you want exact control.",
                elementName, role, ratio, MinContrastRatio, newRatio));

            return ToJson(adjusted);
        }

        // =====================================================
        // DÝKEY OTOMATÝK AKIÞ
        // =====================================================

        /// <summary>Otomatik yerleþimde elemanlar arasý dikey boþluk (ebeveyn oraný).</summary>
        private const float AutoFlowGap = 0.03f;

        /// <summary>Otomatik yerleþimde ilk elemanýn üst kenarý.</summary>
        private const float AutoFlowTop = 0.97f;

        /// <summary>Otomatik yerleþimde son elemanýn inebileceði en alt sýnýr.</summary>
        private const float AutoFlowBottom = 0.02f;

        /// <summary>
        /// Bir kardeþin "SÜTUN" sayýlmasý için gereken en küçük dikey oran - tek
        /// baþýna yeterli koþul.
        /// </summary>
        private const float FullColumnHeight = 0.85f;

        /// <summary>
        /// Bir kardeþ bu orandan darsa ve MinColumnHeight'tan uzunsa, tam yüksekliðe
        /// ulaþmasa bile SÜTUN sayýlýr.
        /// </summary>
        private const float MaxColumnWidth = 0.50f;

        /// <summary>Dar bir elemanýn sütun sayýlmasý için gereken en küçük yükseklik.</summary>
        private const float MinColumnHeight = 0.55f;

        /// <summary>Otomatik akýþýn kullanabileceði en dar sütun geniþliði.</summary>
        private const float MinFlowWidth = 0.10f;

        /// <summary>
        /// Konteyner adý -> kaç eþit SÜTUNA bölündüðü.
        ///
        /// ============ NEDEN VAR - YATAY YERLEÞÝM ============
        /// GERÇEK TEST: bir dashboard'da "a row of THREE status cards side by side"
        /// istendi. Model doðru içgüdüyle bir konteyner kurdu, ama içine kartlarý
        /// anchor vermeden koydu - kurallar öyle diyor - ve otomatik akýþ YALNIZCA
        /// DÝKEY olduðu için üç kart alt alta dizildi.
        ///
        /// Model bunu elle çözebilirdi: her karta 0.02-0.32, 0.35-0.65, 0.68-0.98
        /// bantlarýný vermek. Ama bu tam olarak tutturamadýðý aritmetik ve zaten dikey
        /// akýþý bu yüzden yazmýþtýk.
        ///
        /// Çözüm create_ui_button_bar'daki ile ayný: niyeti model söyler, hesabý kod
        /// yapar. Konteyner "columns":3 ile kuruluyor, içine konan her eleman sýradaki
        /// sütuna, eþit geniþlikte, tam yükseklikte yerleþiyor. Çakýþma imkânsýz.
        /// ====================================================
        /// </summary>
        private static readonly Dictionary<string, int> ColumnContainers =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sütunlu bir konteynerde kenar payý ve sütunlar arasý boþluk.</summary>
        private const float ColumnMargin = 0.02f;
        private const float ColumnGap = 0.02f;

        // =====================================================
        // EKRAN BÖLGELERÝ - "region" ÝLE YERLEÞTÝRME
        // =====================================================

        /// <summary>
        /// Ebeveyn adý -> o ebeveynde HENÜZ BÖLÜNMEMÝÞ alan.
        ///
        /// ============ NEDEN VAR - Y EKSENÝ TERS KULLANILIYORDU ============
        /// GERÇEK TEST, ÝKÝ TURDA ÜST ÜSTE: "a thin top bar across the top" istendi ve
        /// baþlýk ekranýn EN ALTINDA çýktý; ekranýn üst yarýsý tamamen boþ kaldý.
        ///
        /// Sebep modelin bir bilgi eksiði: Unity'de anchor'da y=0 ALT kenardýr, y=1
        /// üsttür. Model web mantýðýyla (y=0 = üst) hesaplýyor ve üst çubuða 0-0.08
        /// verip onu dibe yapýþtýrýyor. Sonra diðer bölgeler onun üstüne diziliyor,
        /// ekranýn gerçek üstü boþ kalýyor.
        ///
        /// Prompt'ta hazýr bölge haritalarý vardý ama model yine kendi hesaplýyordu.
        /// Ayný ilkeyi uyguluyoruz: bölgeyi ADIYLA istesin, sayýyý KOD versin -
        /// create_ui_button_bar'ýn eþit geniþlikte yaptýðý, columns'ýn yan yana
        /// yaptýðý þeyin ekran ölçeðindeki karþýlýðý.
        ///
        /// Klasik "dock" mantýðý: her bölge KALAN alandan payýný alýr ve kalan küçülür.
        /// Üst çubuk, alt çubuk, sol sütun, sað sütun sýrayla yerleþir; "center" geriye
        /// ne kaldýysa onu alýr. Çakýþma aritmetik olarak imkânsýz.
        /// ==================================================================
        /// </summary>
        private static readonly Dictionary<string, LayoutRect> DockRemaining =
            new Dictionary<string, LayoutRect>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bölge kalýnlýðý verilmezse kullanýlan varsayýlanlar.</summary>
        private const float DefaultBarThickness = 0.08f;
        private const float DefaultSideThickness = 0.22f;

        /// <summary>Bir bölgenin arkasýnda býrakmasý gereken en küçük alan.</summary>
        private const float MinRemainingSpan = 0.08f;

        /// <summary>
        /// 'region' deðerini bir yöne çevirir. Model "header", "footer", "sidebar" gibi
        /// makul eþanlamlýlar üretiyor; reddetmek hiçbir þey kazandýrmaz.
        /// </summary>
        private static string NormalizeRegion(string region)
        {
            if (string.IsNullOrWhiteSpace(region))
                return null;

            switch (region.Trim().ToLowerInvariant())
            {
                case "top":
                case "header":
                case "topbar":
                case "top_bar":
                case "strip":
                case "statusbar":
                case "status_bar":
                    return "top";

                case "bottom":
                case "footer":
                case "bottombar":
                case "bottom_bar":
                case "commandbar":
                case "command_bar":
                    return "bottom";

                case "left":
                case "sidebar":
                case "leftcolumn":
                case "left_column":
                case "nav":
                case "navigation":
                    return "left";

                case "right":
                case "rightcolumn":
                case "right_column":
                case "rail":
                case "inspector":
                    return "right";

                case "center":
                case "centre":
                case "main":
                case "content":
                case "fill":
                case "rest":
                case "middle":
                    return "center";

                default:
                    return null;
            }
        }

        /// <summary>
        /// Bir bölgeyi ebeveynin kalan alanýndan keser ve anchor'larýný verir.
        ///
        /// Sýra ÖNEMLÝ ve doðal: önce çubuklar, sonra yan sütunlar, en son merkez.
        /// Merkez "geriye ne kaldýysa" demektir, o yüzden en sona býrakýlmalý - prompt
        /// da bunu söylüyor.
        /// </summary>
        private static bool TryDockRegion(
            string parent, string region, float? size,
            out JObject anchorMin, out JObject anchorMax, out string error)
        {
            anchorMin = null;
            anchorMax = null;
            error = null;

            string dir = NormalizeRegion(region);

            if (dir == null)
            {
                error =
                    $"'region' does not accept '{region}'. Use one of: \"top\", \"bottom\", \"left\", \"right\", \"center\". " +
                    "The agent then computes the anchors, so you never have to - and a top bar can never end up at the bottom.";
                return false;
            }

            if (!DockRemaining.TryGetValue(parent, out LayoutRect rem))
                rem = new LayoutRect { MinX = 0f, MinY = 0f, MaxX = 1f, MaxY = 1f };

            if (rem.Width < MinRemainingSpan || rem.Height < MinRemainingSpan)
            {
                error =
                    $"'{parent}' has no free region left - its top, bottom and side bands are all taken. " +
                    "Put this element inside one of the regions you already created, or use a different parent.";
                return false;
            }

            float t = size.HasValue
                ? Mathf.Clamp(size.Value, 0.04f, 0.6f)
                : (dir == "left" || dir == "right" ? DefaultSideThickness : DefaultBarThickness);

            LayoutRect r = rem;

            switch (dir)
            {
                case "top":
                    t = Mathf.Min(t, rem.Height - MinRemainingSpan);
                    r.MinY = rem.MaxY - t;
                    rem.MaxY -= t;
                    break;

                case "bottom":
                    t = Mathf.Min(t, rem.Height - MinRemainingSpan);
                    r.MaxY = rem.MinY + t;
                    rem.MinY += t;
                    break;

                case "left":
                    t = Mathf.Min(t, rem.Width - MinRemainingSpan);
                    r.MaxX = rem.MinX + t;
                    rem.MinX += t;
                    break;

                case "right":
                    t = Mathf.Min(t, rem.Width - MinRemainingSpan);
                    r.MinX = rem.MaxX - t;
                    rem.MaxX -= t;
                    break;

                case "center":
                    // Kalanýn tamamý. Kalan sýfýrlanýyor: merkezden sonra dock edilecek
                    // bir bölge, merkezin üstüne binerdi.
                    rem = new LayoutRect { MinX = 0f, MinY = 0f, MaxX = 0f, MaxY = 0f };
                    break;
            }

            if (t <= 0.02f && dir != "center")
            {
                error =
                    $"'{parent}' does not have room for another {dir} region. " +
                    "Put this element inside an existing region instead.";
                return false;
            }

            DockRemaining[parent] = rem;

            anchorMin = new JObject { ["x"] = r.MinX, ["y"] = r.MinY };
            anchorMax = new JObject { ["x"] = r.MaxX, ["y"] = r.MaxY };

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[UiMacroExpander] Region '{0}' of '{1}' placed at x {2:0.##}-{3:0.##}, y {4:0.##}-{5:0.##}. " +
                "Remaining free area: x {6:0.##}-{7:0.##}, y {8:0.##}-{9:0.##}.",
                dir, parent, r.MinX, r.MaxX, r.MinY, r.MaxY, rem.MinX, rem.MaxX, rem.MinY, rem.MaxY));

            return true;
        }

        /// <summary>Bir konteyneri N eþit sütuna böler. 2-4 arasý kabul ediliyor.</summary>
        public static void RegisterColumnContainer(string name, int columns)
        {
            if (string.IsNullOrWhiteSpace(name) || columns < 2 || columns > 4)
                return;

            ColumnContainers[name] = columns;

            Debug.Log($"[UiMacroExpander] '{name}' is a {columns}-column container. " +
                      "Elements created inside it WITHOUT anchors are placed side by side, equal width.");
        }

        /// <summary>
        /// Otomatik akýþýn bir elemaný sýðdýrabileceði en küçük yükseklik.
        ///
        /// Bunun altýnda kalan bir alan pratikte kullanýlamaz: bir kart baþlýðý bile
        /// sýðmaz, satýr etiketi okunmaz. O noktada konteyner gerçekten doludur ve
        /// modeli baþka bir yere yönlendirmek doðru davranýþtýr.
        /// </summary>
        private const float MinAutoHeight = 0.08f;

        /// <summary>
        /// Bu kardeþ, ebeveyni YATAY olarak bölen bir sütun mu - yoksa akýþta sýrada
        /// duran normal bir satýr mý?
        ///
        /// ============ NEDEN SADECE YÜKSEKLÝK YETMÝYOR ============
        /// Ýlk sürüm tek bir eþik kullanýyordu: yükseklik >= 0.85. Gerçek testte bir
        /// müzik çalar ekranýnda albüm kapaðý paneli [x 0-0.3, y 0.1-0.92] olarak
        /// kuruldu - yüksekliði 0.82, yani eþiðin HEMEN ALTINDA. Sütun sayýlmadý,
        /// normal bir satýr gibi iþlendi ve akýþ onun ALTINA yerleþmeye çalýþtý:
        ///
        ///     "Auto-flow could not fit: only 0.05 of vertical space is left,
        ///      0.4 was needed"
        ///
        /// Oysa sað tarafta ekranýn %70'i boþtu. Üç kart da bu yüzden kurulamadý.
        ///
        /// Eþiði 0.80'e çekmek ayný sorunu 0.79'da tekrar üretirdi. Asýl ayýrt edici
        /// yükseklik deðil ORAN: bir sütun DAR ve UZUNDUR, bir satýr GENÝÞ ve KISADIR.
        /// Aradaki fark tek bir eþikten çok daha belirgin:
        ///
        ///     albüm kapaðý  0.30 x 0.82  -> dar ve uzun    -> sütun
        ///     sidebar       0.22 x 1.00  -> dar ve uzun    -> sütun
        ///     satýr         0.96 x 0.17  -> geniþ ve kýsa  -> satýr
        ///     kart          0.92 x 0.34  -> geniþ ve kýsa  -> satýr
        ///
        /// Tam yükseklik koþulu da korunuyor: ebeveynini dikeyde tamamen kaplayan bir
        /// eleman, geniþ olsa bile üstüne yerleþilecek bir þey deðildir.
        /// ========================================================
        /// </summary>
        private static bool IsColumn(LayoutRect r)
        {
            if (r.Height >= FullColumnHeight)
                return true;

            return r.Width <= MaxColumnWidth && r.Height >= MinColumnHeight;
        }

        /// <summary>
        /// Anchor verilmemiþse elemaný, ayný ebeveyndeki son kardeþin ALTINA yerleþtirir.
        ///
        /// ============ BÝNDÝRMENÝN KALICI ÇÖZÜMÜ ============
        /// GERÇEK ARIZA: bir kartýn içine üç satýr koymak için model þu bantlarý kendi
        /// hesaplamak zorundaydý: 0.70-0.95, 0.40-0.65, 0.10-0.35. Altý sayýnýn
        /// altýsýný da tutturmasý gerekiyordu. Tutturamadý - satýrlar üst üste bindi.
        ///
        /// create_ui_button_bar bu sorunu YATAYDA zaten çözmüþtü. Dikeyde karþýlýðý
        /// yoktu; artýk var.
        ///
        /// KURAL: anchorMin VE anchorMax'ýn ÝKÝSÝ DE verilmemiþse otomatik yerleþim
        /// devreye girer. Biri bile verilmiþse model bilinçli bir konum istiyordur ve
        /// ona karýþýlmaz.
        ///
        ///
        /// ============ YATAY SÜTUN FARKINDALIÐI - BU SÜRÜMDE EKLENDÝ ============
        /// GERÇEK ARIZA: bir müzik çalar ekranýnda içerik alaný ikiye bölündü -
        /// solda dar bir albüm kapaðý paneli (x 0-0.3, y 0-1), saðda kartlar. Akýþ
        /// yalnýzca Y eksenine bakýyordu ve albüm kapaðý TÜM dikey alaný kapladýðý
        /// için þu sonucu veriyordu:
        ///
        ///     "Auto-flow could not fit another element under 'ContentArea':
        ///      only 0 of vertical space is left"
        ///
        /// Oysa sað tarafta bomboþ bir sütun duruyordu. Akýþ onu göremiyordu, çünkü
        /// "dolu" kavramý yalnýzca dikeydi.
        ///
        /// Artýk iki aþama var:
        ///   1. SÜTUNLARI ÇIKAR - dikeyde neredeyse tamamýný kaplayan kardeþler
        ///      (sidebar, kapak paneli, telemetri rayý) ebeveyni yatay olarak bölüyor
        ///      demektir. Kapladýklarý x aralýðý kullanýlabilir geniþlikten düþülür.
        ///   2. KALAN SÜTUNDA AKIT - yalnýzca yeni x aralýðýyla YATAYDA ÖRTÜÞEN
        ///      kardeþler "üstünde bir þey var" sayýlýr. Baþka bir sütundaki bir satýr
        ///      bu elemaný aþaðý itmez.
        ///
        /// Böylece "solda dar panel, saðda alt alta kartlar" düzeni modelin hiç
        /// aritmetik yapmasýna gerek kalmadan kuruluyor.
        /// ======================================================================
        /// </summary>
        /// <param name="height">Elemanýn ebeveyn içindeki dikey oraný (0-1).</param>
        /// <param name="marginX">Soldan ve saðdan býrakýlacak yatay pay.</param>
        private static bool TryAutoPlaceVertical(
            JObject p, string parent, float height, float marginX,
            out JObject anchorMin, out JObject anchorMax)
        {
            anchorMin = null;
            anchorMax = null;

            // Biri bile verilmiþse model konumu bilerek seçiyor - karýþma.
            if (p["anchorMin"] != null || p["anchorMax"] != null)
                return false;

            if (string.IsNullOrWhiteSpace(parent))
                return false;

            // ---------- SÜTUNLU KONTEYNER: YAN YANA ----------
            // Dikey akýþtan ÖNCE bakýlýyor: bu konteynerde "alta yerleþ" diye bir þey
            // yok, elemanlar yana diziliyor.
            if (ColumnContainers.TryGetValue(parent, out int columns) && columns > 1)
            {
                int index = 0;

                if (LayoutRegistry.TryGetValue(parent, out var placed))
                    index = placed.Count;

                if (index >= columns)
                {
                    Debug.LogWarning(
                        $"[UiMacroExpander] '{parent}' was declared as a {columns}-column container and all {columns} columns are taken. " +
                        "The overlap check will now ask the model to place this element explicitly, or to use another container.");
                    return false;
                }

                float columnWidth = (1f - ColumnMargin * 2f - ColumnGap * (columns - 1)) / columns;
                float columnLeft = ColumnMargin + index * (columnWidth + ColumnGap);

                anchorMin = new JObject { ["x"] = columnLeft, ["y"] = AutoFlowBottom };
                anchorMax = new JObject { ["x"] = columnLeft + columnWidth, ["y"] = AutoFlowTop };

                return true;
            }

            height = Mathf.Clamp(height, 0.02f, 0.9f);

            float left = marginX;
            float right = 1f - marginX;
            float top = AutoFlowTop;
            float flowBottom = AutoFlowBottom;

            // ============ AKIÞ, BÖLGELERDEN ARTAN ALANDA ÇALIÞIR ============
            // GERÇEK ARIZA: model ekranýn alt %60'ýný bir 'bottom' bölgesine verdi.
            // Geriye ortada y 0.6-0.92 bandý kaldý. Sonra doðrudan MainCanvas'a bir
            // kart koymak istedi ve akýþ "yer yok" dedi:
            //
            //     "Auto-flow could not fit under 'MainCanvas': only 0 of vertical
            //      space is left"
            //
            // Çünkü akýþ TEK BÝR VARSAYIMLA çalýþýyordu: elemanlar hep yukarýdan aþaðý
            // dizilir, dolayýsýyla boþ yer her zaman EN ALTTAKÝ kardeþin altýndadýr.
            // Bölge yerleþtirme bu varsayýmý bozdu - orada en alttaki kardeþ ekranýn
            // DÝBÝNDE duruyor ve boþ alan ORTADA kalýyor. Akýþ oraya hiç bakmýyor,
            // varsayýlan dikdörtgene düþüyor, o da bölgeyle çakýþýyor ve görev
            // beþ baþarýsýz adýmda ölüyordu.
            //
            // Düzeltme: bu ebeveynde bölge yerleþtirme yapýldýysa akýþ, bölgelerden
            // ARTAN alanýn içinde çalýþýr. Dýþarýda kalan bölgeler (üst çubuk, alt
            // çubuk, yan sütunlar) akýþýn hesabýna hiç girmez - zaten onlar kendi
            // yerlerini almýþ durumda.
            // ================================================================
            if (DockRemaining.TryGetValue(parent, out LayoutRect free) &&
                free.Width >= MinRemainingSpan && free.Height >= MinRemainingSpan)
            {
                left = Mathf.Max(left, free.MinX + 0.01f);
                right = Mathf.Min(right, free.MaxX - 0.01f);
                top = Mathf.Min(top, free.MaxY - 0.01f);
                flowBottom = Mathf.Max(flowBottom, free.MinY + 0.01f);
            }

            if (LayoutRegistry.TryGetValue(parent, out var siblings) && siblings.Count > 0)
            {
                // ---------- 1. AÞAMA: SÜTUNLARI KULLANILABÝLÝR GENÝÞLÝKTEN DÜÞ ----------
                // Dikeyde neredeyse tamamýný kaplayan bir kardeþ, ebeveyni yatay olarak
                // bölen bir bölgedir - onun üstüne deðil, YANINA yerleþilir.
                foreach (var sibling in siblings)
                {
                    LayoutRect r = sibling.Value;

                    if (!IsColumn(r))
                        continue;

                    bool hugsLeft = r.MinX <= left + 0.01f;
                    bool hugsRight = r.MaxX >= right - 0.01f;

                    if (hugsLeft && !hugsRight)
                    {
                        left = Mathf.Max(left, r.MaxX + AutoFlowGap);
                    }
                    else if (hugsRight && !hugsLeft)
                    {
                        right = Mathf.Min(right, r.MinX - AutoFlowGap);
                    }
                    // Ýki kenara da yapýþan bir sütun ebeveynin tamamýný kaplýyor
                    // demektir; onu düþmek geriye hiç yer býrakmaz, o yüzden
                    // dokunulmuyor ve aþaðýdaki dikey akýþ devreye giriyor.
                }

                if (right - left < MinFlowWidth)
                {
                    Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                        "[UiMacroExpander] Auto-flow found no usable column under '{0}': the side columns leave only {1:0.###} of width. " +
                        "The overlap check will now ask the model to place this element explicitly.",
                        parent, Mathf.Max(0f, right - left)));
                    return false;
                }

                // ---------- 2. AÞAMA: YALNIZCA AYNI SÜTUNDAKÝ KARDEÞLERE BAK ----------
                // Baþka bir sütundaki satýr bu elemaný aþaðý itmemeli.
                float lowest = 1f;
                bool foundInColumn = false;

                foreach (var sibling in siblings)
                {
                    LayoutRect r = sibling.Value;

                    if (IsColumn(r))
                        continue;

                    bool overlapsHorizontally = r.MaxX > left + 0.001f && r.MinX < right - 0.001f;

                    if (!overlapsHorizontally)
                        continue;

                    // Akýþ bandýnýn tamamen DIÞINDA kalan bir kardeþ (üst çubuk, alt
                    // çubuk gibi yerleþmiþ bir bölge) bu elemaný aþaðý itmemeli.
                    bool overlapsFlowBand = r.MaxY > flowBottom + 0.001f && r.MinY < top - 0.001f;

                    if (!overlapsFlowBand)
                        continue;

                    lowest = Mathf.Min(lowest, r.MinY);
                    foundInColumn = true;
                }

                if (foundInColumn)
                    top = lowest - AutoFlowGap;
            }

            float available = top - flowBottom;

            // ============ SIÐDIR, VAZGEÇME ============
            // GERÇEK ARIZA: bir sütuna üç kart istendi. Varsayýlan kart yüksekliði
            // 0.34 olduðu için üçü toplam 1.08 yer istiyordu - sütunun kendisinden
            // büyük. Üçüncü kartta akýþ "sýðmýyor" deyip VAZGEÇTÝ:
            //
            //     "Auto-flow could not fit under 'LeftStatusColumn':
            //      only 0.21 of vertical space is left, 0.34 was needed"
            //
            // Vazgeçince create_ui_card kendi varsayýlanýna düþtü (y 0.5-0.9) ve
            // doðal olarak ikinci kartla çakýþtý. Model beþ kez denedi, görev durdu.
            //
            // Oysa 0.21 boþ yer VARDI. Biraz kýsa bir kart, hiç olmayan karttan
            // iyidir - üstelik kullanýcý zaten "kartlar sütunu doldurmasýn" demiþti.
            //
            // Artýk istenen yükseklik sýðmýyorsa eleman KALAN ALANA sýðdýrýlýyor.
            // Yalnýzca kalan yer okunabilir bir elemana yetmeyecek kadar azsa
            // (MinAutoHeight) vazgeçiliyor - o noktada konteyner gerçekten dolu
            // demektir ve çakýþma denetiminin modele bunu söylemesi doðru.
            // ==========================================
            if (height > available)
            {
                if (available < MinAutoHeight)
                {
                    Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                        "[UiMacroExpander] Auto-flow could not fit another element under '{0}': " +
                        "only {1:0.###} of vertical space is left, which is below the readable minimum of {2:0.###}. " +
                        "This container is full - the overlap check will now ask the model to place it elsewhere.",
                        parent, Mathf.Max(0f, available), MinAutoHeight));
                    return false;
                }

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[UiMacroExpander] Auto-flow shrank an element under '{0}' from {1:0.###} to {2:0.###} " +
                    "so it fits in the space that is left. Nothing overlaps.",
                    parent, height, available));

                height = available;
            }

            float bottom = top - height;

            anchorMin = new JObject { ["x"] = left, ["y"] = bottom };
            anchorMax = new JObject { ["x"] = right, ["y"] = top };

            return true;
        }

        /// <summary>
        /// Bir macro'nun üreteceði ALT OBJE adlarýnýn baþkasýyla çakýþýp çakýþmadýðýný
        /// denetler.
        ///
        /// 'Start' adlý bir buton 'StartLabel' adlý bir alt obje yaratýyor. Model
        /// sonradan 'StartLabel' adýnda bir etiket isterse sahnede AYNI ADDA ÝKÝ obje
        /// oluyor. GameObject.Find ilk bulduðunu döndürdüðü için sonraki her baðlama
        /// adýmý yanlýþ objeye gidiyor - ve hiçbir hata çýkmýyor.
        /// </summary>
        private static bool DerivedNamesAreFree(HashSet<string> known, IEnumerable<string> derived, out string error)
        {
            error = null;

            if (derived == null)
                return true;

            foreach (string d in derived)
            {
                if (known.Contains(NameKey("gameobject", d)))
                {
                    error =
                        $"This macro would create a child object named '{d}', but an object with that name already exists. " +
                        "Two objects with the same name break every later lookup, because Unity returns whichever it finds first. " +
                        "Choose a different 'name' for this element.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Her görsel macro'nun baþýnda yapýlan ORTAK denetim.
        ///
        /// ============ allowOverlap ARTIK SINIRLI ============
        /// Eski davranýþ: allowOverlap=true çakýþma denetimini TAMAMEN kapatýyordu.
        /// Gerçek testte model bunu her reddedilen çaðrýya ekledi ve kartýn içindeki
        /// satýrlar üst üste bindi - hiçbir þey engellemedi.
        ///
        /// Yeni davranýþ: allowOverlap yalnýzca NEREDEYSE AYNI dikdörtgende geçerli.
        /// Amacý buydu zaten: sekme panelleri ayný alaný paylaþýr ve biri hariç
        /// gizlenir. KISMÝ bindirmenin meþru bir kullanýmý yok - o sadece bozuk
        /// yerleþim demek - ve artýk allowOverlap ile de reddediliyor.
        /// ===================================================
        /// </summary>
        private static bool PreflightElement(
            JObject p, HashSet<string> known, string name, string parent,
            JObject anchorMin, JObject anchorMax, string alreadyExistsHint,
            IEnumerable<string> derivedNames, out string error)
        {
            error = null;

            if (!ValidateObjectName(name, out error))
                return false;

            if (!ValidateObjectName(parent, out string parentError))
            {
                error = "The 'parent' value is not usable: " + parentError;
                return false;
            }

            if (known.Contains(NameKey("gameobject", name)))
            {
                error = $"'{name}' already exists - do not create it again. {alreadyExistsHint}";
                return false;
            }

            if (!DerivedNamesAreFree(known, derivedNames, out error))
                return false;

            if (!NormalizeAnchors(anchorMin, anchorMax, name, out error))
                return false;

            LayoutRect rect = ToRect(anchorMin, anchorMax);
            bool allowOverlap = p["allowOverlap"]?.ToObject<bool?>() ?? false;

            if (allowOverlap && !IsLegitimateStack(parent, name, rect))
            {
                // Model allowOverlap'i bir kaçýþ yolu olarak kullanmýþ. Söyle ve
                // normal denetime devam et - bayrak burada hiçbir þey muaf tutmuyor.
                Debug.LogWarning(
                    $"[UiMacroExpander] '{name}' asked for allowOverlap but does not share a rectangle with any sibling under '{parent}'. " +
                    "allowOverlap only applies to switchable panels that occupy the SAME area; it is not a way to bypass layout checks. " +
                    "The normal overlap check still applies to this call.");

                allowOverlap = false;
            }

            if (!allowOverlap)
            {
                if (WouldOverlapSibling(parent, name, rect, out string overlapMessage))
                {
                    error = overlapMessage;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Bu eleman gerçekten bir SEKME YIÐINI mý - yani mevcut bir kardeþle
        /// neredeyse ayný dikdörtgeni mi paylaþýyor.
        ///
        /// Yýðýnýn ÝLK elemaný için henüz paylaþacak kardeþ yoktur; o da meþrudur.
        /// Ayýrt edici nokta þu: yýðýnýn ilki ile hiçbir kardeþle ÇAKIÞMAYAN bir
        /// eleman ayný þeydir, ikisi de zararsýz. Zararlý olan KISMÝ bindirmedir ve
        /// bu metot ona 'false' diyor.
        /// </summary>
        private static bool IsLegitimateStack(string parent, string name, LayoutRect rect)
        {
            if (!LayoutRegistry.TryGetValue(parent, out var siblings) || siblings.Count == 0)
                return true;   // ilk eleman - yýðýn olacaksa buradan baþlar

            bool touchesSomething = false;

            foreach (var sibling in siblings)
            {
                if (string.Equals(sibling.Key, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (rect.IsNearlyIdentical(sibling.Value))
                    return true;   // gerçek yýðýn

                if (rect.OverlapArea(sibling.Value) > 0f)
                    touchesSomething = true;
            }

            // Hiçbir kardeþe deðmiyorsa allowOverlap gereksiz ama zararsýz.
            // Kýsmen deðiyorsa bu bir yýðýn deðil, bozuk yerleþimdir.
            return !touchesSomething;
        }

        /// <summary>
        /// Bilerek üst üste konmuþ bir elemaný yýðýna ekler ve ÝLK eleman deðilse
        /// gizler. Sekme panelleri için doðru baþlangýç durumu: biri açýk, gerisi
        /// kapalý.
        ///
        /// ============ DÜZELTÝLEN MANTIK HATASI ============
        /// Eski kodun yorumu "allowOverlap verilmiþ ama aslýnda çakýþmayan bir eleman
        /// gizlenmemeli" diyordu; kodu ise tam tersini yapýyordu. 'stacksOnExisting'
        /// false olduðu hâlde yýðýn boþ deðilse eleman yine de gizleniyordu - yani
        /// ekranýn BAÞKA bir köþesinde duran, kimseyle çakýþmayan bir panel görünmez
        /// oluyordu ve sebebi hiçbir yerde yazmýyordu.
        ///
        /// Artýk yalnýzca GERÇEKTEN mevcut bir yýðýnýn üstüne binen eleman gizleniyor.
        /// =================================================
        /// </summary>
        private static async Task HandleOverlapStackAsync(string parent, string name, LayoutRect rect)
        {
            if (string.IsNullOrWhiteSpace(parent))
                return;

            bool stacksOnExisting = false;

            if (LayoutRegistry.TryGetValue(parent, out var siblings))
            {
                foreach (var sibling in siblings)
                {
                    if (string.Equals(sibling.Key, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (rect.IsNearlyIdentical(sibling.Value))
                    {
                        stacksOnExisting = true;
                        break;
                    }
                }
            }

            if (!OverlapStacks.TryGetValue(parent, out var stack))
            {
                stack = new List<string>();
                OverlapStacks[parent] = stack;
            }

            // Hiçbir kardeþin üstüne binmiyorsa bu bir yýðýn elemaný DEÐÝL: ne yýðýna
            // yazýlýr ne gizlenir. Yýðýnýn ilk elemaný da buraya düþer, çünkü henüz
            // üstüne bineceði bir þey yoktur - ilk kayýt aþaðýda yapýlýyor.
            if (!stacksOnExisting)
            {
                if (stack.Count == 0)
                    stack.Add(name);   // yýðýnýn ilk elemaný - görünür kalýr

                return;
            }

            if (!stack.Contains(name))
                stack.Add(name);

            if (stack.Count > 1 && !string.Equals(stack[0], name, StringComparison.OrdinalIgnoreCase))
            {
                await SetActiveAsync(name, false);

                Debug.Log(
                    $"[UiMacroExpander] '{name}' stacks on '{stack[0]}' under '{parent}' and was hidden " +
                    $"(stacked panel {stack.Count}). Exactly one panel in a stack stays visible; wire the tab logic later to switch between them.");
            }
        }

        // =====================================================
        // SETUP MACROS
        // =====================================================

        private static async Task<string> EnsureCanvas(JObject p, HashSet<string> known)
        {
            string name = p["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = "MainCanvas";

            if (!ValidateObjectName(name, out string nameError))
                return Error(nameError);

            if (known.Contains(NameKey("gameobject", name)))
                return Success($"Canvas '{name}' already exists, reused.");

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Canvas",
                    ["properties"] = new JObject { ["renderMode"] = "ScreenSpaceOverlay" }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "CanvasScaler",
                    ["properties"] = new JObject
                    {
                        ["uiScaleMode"] = "ScaleWithScreenSize",
                        ["referenceResolution"] = new JObject { ["x"] = 1920, ["y"] = 1080 },
                        ["screenMatchMode"] = "MatchWidthOrHeight",
                        ["matchWidthOrHeight"] = 0.5
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "GraphicRaycaster"
                }),
            };

            string result = await RunSequential(steps, name, "Canvas");

            if (IsAllSuccess(result))
                known.Add(NameKey("gameobject", name));

            return result;
        }

        private static async Task<string> EnsureEventSystem(HashSet<string> known)
        {
            const string name = "EventSystem";

            if (known.Contains(NameKey("gameobject", name)))
                return Success("EventSystem already exists, reused.");

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "EventSystem"
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "StandaloneInputModule"
                }),
            };

            string result = await RunSequential(steps, name, "EventSystem");

            if (IsAllSuccess(result))
                known.Add(NameKey("gameobject", name));

            return result;
        }

        // =====================================================
        // CONTAINER MACROS
        // =====================================================

        private static async Task<string> CreatePanel(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);

            JObject color = p["color"] as JObject ?? DefaultPanelColor();

            // Panel varsayýlaný ebeveyni TAMAMEN kaplamak. Bu bilinçli: bir panel
            // genellikle bir bölgenin tamamýdýr, bir satýr deðil. Bu yüzden panelde
            // otomatik dikey akýþ YOK - varsayýlaný zaten doðru.
            JObject anchorMin;
            JObject anchorMax;

            // ============ 'region' ÝLE YERLEÞTÝRME ============
            // Model bölgeyi adýyla istiyor, sayýyý kod veriyor. Y ekseninin yönünü
            // bilmesi gerekmiyor - "top" her zaman gerçekten üstte.
            string regionError = null;
            bool docked = false;

            if (p["region"] != null && p["anchorMin"] == null && p["anchorMax"] == null)
            {
                docked = TryDockRegion(parent, p["region"].ToString(), p["size"]?.ToObject<float?>(),
                    out anchorMin, out anchorMax, out regionError);

                if (!docked)
                    return Error(regionError);
            }
            else if (p["anchorMin"] == null && p["anchorMax"] == null && ParentHasChildren(parent) &&
                     TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.3f, 420f), 0f,
                         out anchorMin, out anchorMax))
            {
                // ============ PANEL DE OTOMATÝK AKIÞA GÝRÝYOR ============
                // GERÇEK ARIZA, DÖNGÜ ÜRETÝYORDU: panel varsayýlaný ebeveyni TAMAMEN
                // kaplamaktý. Ýlk panel için doðru, ikincisi için felaket - üst çubuk
                // zaten yerleþtiyse ikinci panel onunla %100 çakýþýyor ve reddediliyor.
                //
                // Daha kötüsü, red mesajý þunu söylüyordu:
                //     "EASIEST FIX: omit anchorMin and anchorMax entirely."
                // Kart ve satýrlar için doðru bir tavsiye, ama PANEL için YANLIÞTI:
                // anchor'suz panel akýþa girmiyor, ebeveyni kaplýyordu. Tavsiyeye uyan
                // model AYNI hatayý tekrar alýyordu. Gerçek testte iki adým böyle boþa
                // gitti, panel hiç kurulamadý ve ardýndan 'Parent specified
                // (StatusCardsContainer) but not found' zinciri baþladý.
                //
                // Artýk ebeveynde zaten çocuk varsa panel de son kardeþin altýna
                // yerleþiyor - kart ve satýrlarla ayný kural. Boþ bir ebeveynde eski
                // davranýþ korunuyor: ilk panel bölgenin tamamýný kaplar.
                // ========================================================
            }
            else
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0, ["y"] = 0 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 1, ["y"] = 1 };
            }

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", null, out string preflightError))
            {
                return Error(preflightError);
            }

            bool sharp = p["sharpCorners"]?.ToObject<bool?>() ?? false;

            // Tamamen þeffaf bir panel týklamalarý YUTMAMALI - içindeki butonlara
            // eriþimi engellemesin. Görünür bir panelde raycast hedefi kalmasý
            // normaldir: arkasýndaki dünyaya týklamayý engeller, istenen davranýþ.
            bool isInvisible = (color["a"]?.ToObject<float?>() ?? 1f) <= 0.01f;

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = color, ["raycastTarget"] = !isInvisible }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
            };

            if (p["outlineColor"] is JObject outlineColor)
            {
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = name,
                    ["componentType"] = "Outline",
                    ["properties"] = new JObject
                    {
                        ["effectColor"] = outlineColor,
                        ["effectDistance"] = new JObject { ["x"] = 1, ["y"] = -1 }
                    }
                }));
            }

            string result = await RunSequential(steps, name, "panel");

            if (IsAllSuccess(result))
            {
                known.Add(NameKey("gameobject", name));

                // Metin kontrastý bu kayda dayanýyor: bu panelin içine konan her
                // yazý, rengini buradan okuyup okunabilirliðini denetliyor.
                RegisterBackground(name, color);

                LayoutRect rect = ToRect(anchorMin, anchorMax);

                // SIRA ÖNEMLÝ: yýðýn denetimi kayýttan ÖNCE yapýlmalý, yoksa eleman
                // kendi kaydýyla karþýlaþtýrýlýr. HandleOverlapStackAsync kendi adýný
                // atlýyor ama sýra yine de açýk tutuluyor.
                if (p["allowOverlap"]?.ToObject<bool?>() ?? false)
                    await HandleOverlapStackAsync(parent, name, rect);

                RegisterLayout(parent, name, rect);

                // Sütunlu konteyner ilaný: içine anchor'sýz konan elemanlar yan yana
                // dizilecek (bkz. ColumnContainers).
                int columns = p["columns"]?.ToObject<int?>() ?? 0;

                if (columns >= 2)
                    RegisterColumnContainer(name, columns);

                if (!sharp)
                    await ApplySpriteAsync(name, SpriteRoundedLarge);
            }

            return result;
        }

        /// <summary>
        /// Baþlýklý kart: kart paneli + üstte baþlýk metni + altta satýrlar için içerik
        /// konteyneri.
        ///
        /// OTOMATÝK DÝKEY AKIÞ: anchor verilmezse kart, ayný ebeveyndeki son kartýn
        /// altýna yerleþir. Bir ayarlar panelinde üst üste kart dizmek en sýk yapýlan
        /// iþ ve eski varsayýlan (her kart 0.5-0.9) ikinci kartta çakýþma üretiyordu.
        /// </summary>
        private static async Task<string> CreateCard(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);

            // ============ VARSAYILAN YÜKSEKLÝK DÜÞÜRÜLDÜ ============
            // 0.34 ile bir sütuna yalnýzca ÝKÝ kart sýðýyordu: 3 x 0.34 + iki boþluk
            // = 1.08, yani ebeveynin kendisinden büyük. Üç kart isteyen her ekran
            // üçüncüde týkanýyordu.
            //
            // 0.28 ile üç kart rahat sýðýyor (3 x 0.28 + 0.06 = 0.90) ve dört kart
            // da sýðdýrma mantýðýyla kurulabiliyor. Ýki kartlý bir sütunda aradaki
            // fark gözle fark edilmiyor, çünkü kart zaten içeriðine göre deðil
            // verilen orana göre boyutlanýyor.
            //
            // Model kendi 'height' deðerini verirse o kazanýyor - bu yalnýzca
            // varsayýlan.
            // ========================================================
            float autoHeight = Mathf.Clamp(p["height"]?.ToObject<float?>() ?? 0.28f, 0.05f, 0.9f);

            JObject anchorMin;
            JObject anchorMax;

            // ============ ORTALANMIÞ KART - DÝYALOG / LOGIN ============
            // GERÇEK TEST: "One centred dialog card in the middle of the screen"
            // istendi. Model kartý anchor vermeden kurdu - kartlar için kural "anchor
            // verme, otomatik dizilsin" olduðu için. Otomatik akýþ kartý tam geniþlikte
            // EN ÜSTE koydu; login ekraný bir ayarlar listesine benzedi.
            //
            // Ortalanmýþ bir diyalog DÝZÝLEN bir kart deðil, ama model bunu ancak anchor
            // sayýlarýný kendisi hesaplayarak anlatabiliyordu. Artýk niyetini tek bir
            // bayrakla söylüyor, hesabý kod yapýyor: kart ebeveyninin tam ortasýna
            // yerleþiyor ve görev sonunda iki uçtan eþit sýkýþtýrýlýp ortada kalýyor.
            // ============================================================
            bool centered = p["centered"]?.ToObject<bool?>() ?? false;

            if (centered && p["anchorMin"] == null && p["anchorMax"] == null)
            {
                float w = Mathf.Clamp(p["width"]?.ToObject<float?>() ?? 0.36f, 0.15f, 0.9f);
                float h = Mathf.Clamp(p["height"]?.ToObject<float?>() ?? 0.6f, 0.15f, 0.9f);

                anchorMin = new JObject { ["x"] = 0.5f - w * 0.5f, ["y"] = 0.5f - h * 0.5f };
                anchorMax = new JObject { ["x"] = 0.5f + w * 0.5f, ["y"] = 0.5f + h * 0.5f };
            }
            else if (!TryAutoPlaceVertical(p, parent, autoHeight, 0.04f, out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.05, ["y"] = 0.5 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.95, ["y"] = 0.9 };
            }

            string titleName = name + "Title";
            string contentName = name + "Content";

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    $"Add rows to '{contentName}' instead.",
                    new[] { titleName, contentName }, out string preflightError))
            {
                return Error(preflightError);
            }

            string title = p["title"]?.ToString();
            JObject cardColor = p["color"] as JObject ?? DefaultCardColor();
            JObject titleColor = p["titleColor"] as JObject ?? DefaultTextColor();

            // Kart baþlýðý kartýn KENDÝ yüzeyinin üstünde duruyor; kontrast oradan
            // hesaplanmalý, ebeveyn panelden deðil.
            titleColor = EnsureReadable(titleColor, ToColor(cardColor, Color.black), name, "title");
            int titleFontSize = p["titleFontSize"]?.ToObject<int?>() ?? 18;
            bool hasTitle = !string.IsNullOrWhiteSpace(title);

            // ============ BAÞLIK VE ÝÇERÝK BANTLARI - PÝKSEL TABANLI ============
            // Eskiden sabit oranlardý: baþlýk kartýn %82-98'i, içerik %4-78'i. Küçük
            // bir kartta doðru görünüyordu. Ama gerçek testte tek bir kart bir sekme
            // panelinin tamamýný kapladý (~900 piksel) ve baþlýk bandý ~145 piksele,
            // baþlýk ile ilk satýr arasýndaki boþluk ~180 piksele çýktý. Kartýn
            // tepesinde koca bir boþluk, ortasýnda seyrek satýrlar.
            //
            // Artýk bantlar kartýn EKRANDAKÝ gerçek yüksekliðinden hesaplanýyor:
            // baþlýk bandý punto boyutuna göre sabit piksel (~40px), kenar paylarý
            // sabit piksel. Küçük bir kartta sonuç eskisiyle neredeyse ayný; büyük
            // bir kartta baþlýk tepede kalýyor ve içerik hemen altýndan baþlýyor.
            //
            // Oranlar sýnýrlandýrýlýyor: çok küçük bir kartta baþlýk kartýn
            // tamamýný yemesin, çok büyük bir kartta da görünmez incelikte kalmasýn.
            // =====================================================================
            float cardPixels = PixelHeightOf(parent, anchorMin, anchorMax);

            float topPad = Mathf.Clamp(8f / cardPixels, 0.01f, 0.03f);
            float titleBand = Mathf.Clamp(Mathf.Max(36f, titleFontSize * 2.2f) / cardPixels, 0.06f, 0.22f);

            float titleMaxY = 1f - topPad;
            float titleMinY = titleMaxY - titleBand;

            float contentMaxY = hasTitle
                ? titleMinY - Mathf.Clamp(4f / cardPixels, 0.005f, 0.02f)
                : 1f - topPad;
            float contentMinY = Mathf.Clamp(12f / cardPixels, 0.02f, 0.06f);

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = cardColor }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
            };

            var createdNames = new List<string> { name };

            if (hasTitle)
            {
                steps.Add(Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = titleName, ["parent"] = name }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = titleName,
                    ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = title,
                        ["fontSize"] = titleFontSize,
                        ["alignment"] = "Left",
                        ["color"] = titleColor,
                        ["raycastTarget"] = false
                    }
                }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property",
                    ["target"] = titleName,
                    ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.04, ["y"] = titleMinY },
                        new JObject { ["x"] = 0.96, ["y"] = titleMaxY })
                }));
                createdNames.Add(titleName);
            }

            // Ýçerik alaný: satýrlarýn ekleneceði görünmez konteyner.
            //
            // raycastTarget=false BÝLÝNÇLÝ. Eskiden yalnýzca alfa 0'a güveniliyordu;
            // Unity sürümlerine göre alfa-0 raycast davranýþý deðiþebildiði için artýk
            // bayraðý AÇIKÇA kapatýyoruz. Bu konteyner týklamalarý yakalarsa içindeki
            // toggle'lara ve butonlara eriþilemez.
            steps.Add(Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = contentName, ["parent"] = name }));
            steps.Add(Cmd("manage_components", new JObject
            {
                ["action"] = "add",
                ["target"] = contentName,
                ["componentType"] = "Image",
                ["properties"] = new JObject { ["color"] = TransparentColor(), ["raycastTarget"] = false }
            }));
            steps.Add(Cmd("manage_components", new JObject
            {
                ["action"] = "set_property",
                ["target"] = contentName,
                ["componentType"] = "RectTransform",
                ["properties"] = RectFillProperties(
                    new JObject { ["x"] = 0.02, ["y"] = contentMinY },
                    new JObject { ["x"] = 0.98, ["y"] = contentMaxY })
            }));
            createdNames.Add(contentName);

            if (p["outlineColor"] is JObject cardOutline)
            {
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = name,
                    ["componentType"] = "Outline",
                    ["properties"] = new JObject
                    {
                        ["effectColor"] = cardOutline,
                        ["effectDistance"] = new JObject { ["x"] = 1, ["y"] = -1 }
                    }
                }));
            }

            string result = await RunSequential(steps, contentName, "card",
                $"Card '{name}' created. Add its rows with parent='{contentName}' and WITHOUT anchors - they stack automatically.");

            if (IsAllSuccess(result))
            {
                foreach (string n in createdNames)
                    known.Add(NameKey("gameobject", n));

                // Kartýn yüzey rengi hem kendisi hem içerik alaný için geçerli:
                // içerik alaný þeffaf, yani satýrlarýn gerçek zemini kart.
                RegisterBackground(name, cardColor);
                RegisterBackground(contentName, cardColor);

                LayoutRect rect = ToRect(anchorMin, anchorMax);

                if (p["allowOverlap"]?.ToObject<bool?>() ?? false)
                    await HandleOverlapStackAsync(parent, name, rect);

                RegisterLayout(parent, name, rect);

                // Baþlýk ve içerik alaný da kayda giriyor. Eskiden girmiyorlardý ve
                // modelin kartýn ÝÇÝNE doðrudan koyduðu bir eleman baþlýðýn üstüne
                // binebiliyordu - çakýþma denetimi onlarý hiç görmüyordu.
                if (hasTitle)
                    RegisterLayout(name, titleName, new LayoutRect { MinX = 0.04f, MinY = titleMinY, MaxX = 0.96f, MaxY = titleMaxY });

                RegisterLayout(name, contentName, new LayoutRect
                {
                    MinX = 0.02f,
                    MinY = contentMinY,
                    MaxX = 0.98f,
                    MaxY = contentMaxY
                });

                await ApplySpriteAsync(name, SpriteRoundedLarge);

                // ============ KART DA SÜTUNLU KONTEYNER OLABÝLÝR ============
                // GERÇEK TEST: dashboard'da "three status cards side by side" istendi.
                // Model konteyneri create_ui_PANEL ile deðil create_ui_CARD ile kurdu -
                // makul bir seçim, baþlýklý bir grup istiyordu. Ama 'columns' yalnýzca
                // panelde tanýmlýydý, sessizce yok sayýldý ve üç kart yine alt alta
                // dizildi.
                //
                // Sütunlar KARTIN KENDÝSÝNE deðil ÝÇERÝK ALANINA kaydediliyor: bir
                // kartýn içine konan her eleman zaten oraya yönlendiriliyor
                // (RedirectToCardContent), yani çocuklarýn gerçek ebeveyni orasý.
                // ============================================================
                int cardColumns = p["columns"]?.ToObject<int?>() ?? 0;

                if (cardColumns >= 2)
                    RegisterColumnContainer(contentName, cardColumns);

                // Kart baþlýðý tek satýrlýk bir alanda duruyor - sýðmazsa "..." ile
                // kesilmeli, ikinci satýra atlayýp kýrpýlmamalý.
                // Büyük puntoda taþmaya izin veriliyor - bkz. ApplyTextPolishAsync.
                if (hasTitle)
                    await ApplyTextPolishAsync(titleName, allowWrap: titleFontSize >= LargeTextThreshold);
            }

            return result;
        }

        /// <summary>
        /// Basit bir görsel dikdörtgen: ikon, ayraç, yer tutucu alan, harita/kamera
        /// bölgesi, renk bloðu.
        ///
        /// ÝKON HEDEFÝ ÝÇÝN KRÝTÝK: model execute_code ile bir PNG üretip
        /// Assets/UI/Sprites/ altýna yazdýðýnda, o dosyayý bir sonraki komutta bu
        /// macro'nun 'sprite' parametresiyle ekrana koyabiliyor.
        /// </summary>
        private static async Task<string> CreateImage(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.1, ["y"] = 0.1 };
            JObject anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.9, ["y"] = 0.9 };

            string caption = p["caption"]?.ToString();
            bool hasCaption = !string.IsNullOrWhiteSpace(caption);
            string captionName = name + "Caption";

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.",
                    hasCaption ? new[] { captionName } : null, out string preflightError))
            {
                return Error(preflightError);
            }

            JObject color = p["color"] as JObject ?? DefaultPlaceholderColor();
            bool sharp = p["sharpCorners"]?.ToObject<bool?>() ?? false;

            var createdNames = new List<string> { name };

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject
                    {
                        ["color"] = color,

                        // Bir ikon veya yer tutucu týklamalarý YUTMAMALI: üstündeki
                        // butonlara eriþimi engellemesin. Gerekirse blockClicks ile
                        // açýlabilir (modal arka planlar için).
                        ["raycastTarget"] = p["blockClicks"]?.ToObject<bool?>() ?? false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
            };

            if (p["outlineColor"] is JObject outline)
            {
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = name,
                    ["componentType"] = "Outline",
                    ["properties"] = new JObject
                    {
                        ["effectColor"] = outline,
                        ["effectDistance"] = new JObject { ["x"] = 1, ["y"] = -1 }
                    }
                }));
            }

            // Yer tutucu alanlarýn ortasýnda ne olduðunu söyleyen bir yazý olmasý, boþ
            // gri bir dikdörtgenden çok daha profesyonel görünüyor.
            if (hasCaption)
            {
                steps.Add(Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = captionName, ["parent"] = name }));
                // Yer tutucu yazýsý görselin KENDÝ zemininin üstünde; koyu bir
                // dolgunun üstüne muted gri koymak onu görünmez yapardý.
                JObject captionColor = EnsureReadable(
                    p["captionColor"] as JObject ?? DefaultMutedTextColor(),
                    ToColor(color, Color.black), name, "caption");

                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = captionName,
                    ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = caption,
                        ["fontSize"] = p["captionFontSize"]?.ToObject<int?>() ?? 14,
                        ["alignment"] = "Center",
                        ["color"] = captionColor,
                        ["raycastTarget"] = false
                    }
                }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property",
                    ["target"] = captionName,
                    ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.05, ["y"] = 0.42 },
                        new JObject { ["x"] = 0.95, ["y"] = 0.58 })
                }));
                createdNames.Add(captionName);
            }

            string result = await RunSequential(steps, name, "image area");

            if (IsAllSuccess(result))
            {
                foreach (string n in createdNames)
                    known.Add(NameKey("gameobject", n));

                RegisterBackground(name, color);
                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                string spritePath = p["sprite"]?.ToString();

                if (!string.IsNullOrWhiteSpace(spritePath))
                {
                    await ApplyCustomSpriteAsync(name, spritePath,
                        p["preserveAspect"]?.ToObject<bool?>() ?? true);
                }
                else if (!sharp)
                {
                    await ApplySpriteAsync(name, SpriteRoundedLarge);
                }

                // Yer tutucu baþlýðý ("HARÝTA / KAMERA") geniþ bir alanýn ortasýnda
                // duruyor; birden fazla satýra yayýlmasý sorun deðil.
                if (hasCaption)
                    await ApplyTextPolishAsync(captionName, allowWrap: true);
            }

            return result;
        }

        // =====================================================
        // INTERACTIVE MACROS
        // =====================================================

        private static async Task<string> CreateButton(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);

            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.16f, 56f), 0.08f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.1, ["y"] = 0.4 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.9, ["y"] = 0.6 };
            }

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", new[] { name + "Label" }, out string preflightError))
            {
                return Error(preflightError);
            }

            return await BuildButtonAsync(p, known, name, parent, anchorMin, anchorMax);
        }

        /// <summary>
        /// Tek bir butonu kuran ortak gövde. CreateButton ve CreateButtonBar ikisi de
        /// bunu kullanýyor, böylece buton davranýþý iki yerde ayrý ayrý bozulamaz.
        /// </summary>
        private static async Task<string> BuildButtonAsync(
            JObject p, HashSet<string> known, string name, string parent,
            JObject anchorMin, JObject anchorMax)
        {
            string text = p["text"]?.ToString() ?? name;
            JObject buttonColor = p["buttonColor"] as JObject ?? DefaultButtonColor();
            JObject textColor = p["textColor"] as JObject ?? DefaultTextColor();
            int fontSize = p["fontSize"]?.ToObject<int?>() ?? 16;

            // Buton yazýsýnýn zemini butonun KENDÝ rengi. Açýk turkuaz bir butona
            // beyaz yazý koymak en sýk yapýlan okunmazlýk hatasý - burada yakalanýyor.
            textColor = EnsureReadable(textColor, ToColor(buttonColor, Color.black), name, "label");
            string alignment = p["alignment"]?.ToString() ?? "Center";
            string labelName = name + "Label";

            bool centered = string.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase);

            JObject labelMin = centered
                ? new JObject { ["x"] = 0, ["y"] = 0 }
                : new JObject { ["x"] = 0.06, ["y"] = 0 };
            JObject labelMax = centered
                ? new JObject { ["x"] = 1, ["y"] = 1 }
                : new JObject { ["x"] = 0.94, ["y"] = 1 };

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = buttonColor, ["raycastTarget"] = true }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Button"
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = labelName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = labelName, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = text, ["fontSize"] = fontSize,
                        ["alignment"] = alignment, ["color"] = textColor,

                        // Etiket týklamayý yakalarsa buton bazý durumlarda olayý
                        // almýyor. Metin hiçbir zaman raycast hedefi olmamalý.
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = labelName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(labelMin, labelMax)
                }),
            };

            string result = await RunSequential(steps, name, "button");

            if (IsAllSuccess(result))
            {
                known.Add(NameKey("gameobject", name));
                known.Add(NameKey("gameobject", labelName));
                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                await ApplySpriteAsync(name, SpriteRoundedSmall);

                // Buton yazýsý tek satýr kalmalý: kaydýrýrsa dikeyde ortalanmýþ
                // görünümü bozulur ve alt satýr kýrpýlýr. Büyük puntoda taþmaya izin
                // veriliyor, yoksa yazý hiç çizilmiyor.
                await ApplyTextPolishAsync(name, allowWrap: fontSize >= LargeTextThreshold);

                // Rötuþ: targetGraphic + ColorBlock. Ýkisi de manage_components ile
                // yapýlamýyor ama execute_code ile yapýlabiliyor.
                await WireButtonGraphicAsync(name);
                await ApplySelectableColorsAsync(name);
            }

            return result;
        }

        /// <summary>
        /// N butonu EÞÝT geniþlikte, aralarýnda düzgün boþlukla, TEK çaðrýda kurar.
        ///
        /// Bir komut çubuðu için model dört ayrý create_ui_button çaðrýsý yapýp her
        /// birinin bandýný kafasýnda hesaplamak zorundaydý: 0.02-0.24, 0.26-0.48,
        /// 0.50-0.72, 0.74-0.96. Dört sayýnýn dördünü de tutturmasý gerekiyordu ve
        /// tutturamýyordu. Artýk bant hesabýný kod yapýyor.
        /// </summary>
        private static async Task<string> CreateButtonBar(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            if (!(p["buttons"] is JArray buttonSpecs) || buttonSpecs.Count == 0)
            {
                return Error(
                    "'buttons' is required and must be a non-empty array. Each entry is either a plain string (the label) " +
                    "or an object like {\"name\":\"TakeoffButton\",\"text\":\"Kalkis\",\"buttonColor\":{\"r\":0.2,\"g\":0.6,\"b\":0.3,\"a\":1}}.");
            }

            if (buttonSpecs.Count > 8)
            {
                return Error(
                    $"'buttons' has {buttonSpecs.Count} entries, more than a single bar can show legibly (limit 8). " +
                    "Split them into two bars, or move the less important actions elsewhere.");
            }

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.14f, 72f), 0.02f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.0, ["y"] = 0.0 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 1.0, ["y"] = 0.1 };
            }

            // Önce her butonun adýný çöz, sonra hepsini birden çakýþma denetimine sok.
            var resolved = new List<JObject>();
            var derivedNames = new List<string>();

            for (int i = 0; i < buttonSpecs.Count; i++)
            {
                JToken spec = buttonSpecs[i];
                JObject entry;

                if (spec.Type == JTokenType.String)
                {
                    string label = spec.ToString();
                    entry = new JObject
                    {
                        ["name"] = MakeSafeChildName(name, label, i),
                        ["text"] = label
                    };
                }
                else if (spec is JObject obj)
                {
                    entry = (JObject)obj.DeepClone();

                    if (string.IsNullOrWhiteSpace(entry["text"]?.ToString()))
                        return Error($"Button {i + 1} in 'buttons' has no 'text'. Every button needs a visible label.");

                    if (string.IsNullOrWhiteSpace(entry["name"]?.ToString()))
                        entry["name"] = MakeSafeChildName(name, entry["text"].ToString(), i);
                }
                else
                {
                    return Error($"Entry {i + 1} in 'buttons' must be a string or an object, not {spec.Type}.");
                }

                string buttonName = entry["name"].ToString();

                if (!ValidateObjectName(buttonName, out string nameError))
                    return Error($"Button {i + 1}: {nameError}");

                derivedNames.Add(buttonName);
                derivedNames.Add(buttonName + "Label");
                resolved.Add(entry);
            }

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", derivedNames, out string preflightError))
            {
                return Error(preflightError);
            }

            // Konteyner: þeffaf, týklamalarý yutmayan bir çerçeve.
            var containerSteps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject
                    {
                        ["color"] = p["color"] as JObject ?? TransparentColor(),
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
            };

            string containerResult = await RunSequential(containerSteps, name, "button bar container");

            if (!IsAllSuccess(containerResult))
                return containerResult;

            known.Add(NameKey("gameobject", name));
            RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

            // ---------- EÞÝT BANTLARI HESAPLA ----------
            // n buton, aralarýnda (n-1) boþluk, iki kenarda birer kenar payý.
            int count = resolved.Count;
            float gap = Mathf.Clamp(p["gap"]?.ToObject<float?>() ?? 0.02f, 0f, 0.1f);
            float margin = Mathf.Clamp(p["margin"]?.ToObject<float?>() ?? 0.02f, 0f, 0.2f);

            float usable = 1f - (margin * 2f) - (gap * (count - 1));

            if (usable <= 0.05f)
            {
                return Error(
                    $"With {count} buttons, gap={gap} and margin={margin} there is no room left for the buttons themselves. " +
                    "Lower 'gap' or 'margin', or use fewer buttons.");
            }

            float buttonWidth = usable / count;

            var built = new List<string>();
            var failures = new List<string>();

            for (int i = 0; i < count; i++)
            {
                JObject entry = resolved[i];
                string buttonName = entry["name"].ToString();

                float minX = margin + (i * (buttonWidth + gap));

                JObject bMin = new JObject { ["x"] = minX, ["y"] = 0.15 };
                JObject bMax = new JObject { ["x"] = minX + buttonWidth, ["y"] = 0.85 };

                // Bar seviyesindeki ayarlar her butona miras geçiyor; buton kendi
                // deðerini verirse o kazanýyor.
                InheritIfMissing(entry, p, "buttonColor");
                InheritIfMissing(entry, p, "textColor");
                InheritIfMissing(entry, p, "fontSize");

                string one = await BuildButtonAsync(entry, known, buttonName, name, bMin, bMax);

                if (IsAllSuccess(one))
                    built.Add(buttonName);
                else
                    failures.Add($"{buttonName}: {ExtractMessage(one)}");
            }

            if (failures.Count > 0)
            {
                return new JObject
                {
                    ["status"] = "success",
                    ["result"] = new JObject
                    {
                        ["success"] = false,
                        ["message"] =
                            $"Button bar '{name}' was created and {built.Count} of {count} buttons were built, but {failures.Count} failed: " +
                            string.Join(" | ", failures) +
                            $". Add the missing ones individually with create_ui_button, using parent='{name}'.",
                        ["createdName"] = name
                    }
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Created button bar '{name}' with {count} equal-width buttons: {string.Join(", ", built)}.",
                    ["createdName"] = name
                }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Bir etiketten geçerli bir GameObject adý türetir. Türkçe karakterler ve
        /// boþluklar temizleniyor; ad üretilemezse sýra numarasý kullanýlýyor.
        /// </summary>
        private static string MakeSafeChildName(string barName, string label, int index)
        {
            var sb = new StringBuilder();

            foreach (char c in label)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                    sb.Append(c);
            }

            string cleaned = sb.ToString();

            if (cleaned.Length == 0 || char.IsDigit(cleaned[0]))
                cleaned = "Btn" + (index + 1);

            if (cleaned.Length > 24)
                cleaned = cleaned.Substring(0, 24);

            string result = barName + "_" + cleaned;

            // ValidateObjectName 64 karakterde kesiyor; uzun bir bar adý + uzun bir
            // etiket bu sýnýrý aþabiliyordu ve buton sessizce reddediliyordu.
            if (result.Length > 64)
                result = result.Substring(0, 60) + index;

            return result;
        }

        private static void InheritIfMissing(JObject child, JObject parent, string field)
        {
            if (child[field] == null && parent[field] != null)
                child[field] = parent[field].DeepClone();
        }

        /// <summary>
        /// Sidebar navigasyon satýrý: sola yaslý yazý + seçili/seçili deðil durumu +
        /// sol kenarda accent gösterge çubuðu.
        ///
        /// GÖSTERGE HER ZAMAN OLUÞUYOR: sekme mantýðý baðlandýðýnda
        /// 'indicator.SetActive(isSelected)' çaðýrýyor. Gösterge yalnýzca seçili
        /// item'da oluþturulsaydý, diðer sekmelere geçildiðinde hiç belirmezdi.
        ///
        /// OTOMATÝK DÝKEY AKIÞ: bir sidebar'daki nav item'lar tam olarak "alt alta
        /// dizilen eþit yükseklikte elemanlar" - otomatik akýþýn en doðal kullanýmý.
        /// </summary>
        private static async Task<string> CreateNavItem(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);

            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            // Nav göstergesinin rengi ekranýn accent'idir - sonraki elemanlar için hatýrla.
            RememberAccent(p, "accentColor");

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.09f, 52f), 0.05f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.05, ["y"] = 0.4 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.95, ["y"] = 0.5 };
            }

            string labelName = name + "Label";
            string indicatorName = name + "Indicator";

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.",
                    new[] { labelName, indicatorName }, out string preflightError))
            {
                return Error(preflightError);
            }

            string text = p["text"]?.ToString() ?? name;
            bool isSelected = p["isSelected"]?.ToObject<bool?>() ?? false;
            int fontSize = p["fontSize"]?.ToObject<int?>() ?? 15;

            // KRÝTÝK: seçili OLMAYAN item'ýn arka planý görünmez ama TIKLANABÝLÝR
            // olmalý. Tamamen þeffaf bir Image bazý Unity sürümlerinde raycast'te
            // atlanýyor - gerçek testte seçili olmayan nav item'lara hiç týklanamadý.
            JObject bgColor = p["color"] as JObject
                ?? (isSelected ? SelectedNavBackground()
                               : ClickableTransparentColor());

            JObject textColor = p["textColor"] as JObject
                ?? (isSelected ? DefaultTextColor() : DefaultSecondaryTextColor());

            // Seçili olmayan nav item'ýn zemini neredeyse þeffaf, yani gerçek zemin
            // sidebar panelinin rengi. Seçiliyken kendi accent dolgusu zemin oluyor.
            Color navBackground = isSelected
                ? ToColor(bgColor, ResolveBackground(parent))
                : ResolveBackground(parent);

            textColor = EnsureReadable(textColor, navBackground, name, "label");

            var createdNames = new List<string> { name, labelName, indicatorName };

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = bgColor, ["raycastTarget"] = true }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Button"
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = labelName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = labelName, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = text, ["fontSize"] = fontSize,
                        ["alignment"] = "Left", ["color"] = textColor,
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = labelName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.08, ["y"] = 0 },
                        new JObject { ["x"] = 0.96, ["y"] = 1 })
                }),

                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = indicatorName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = indicatorName, ["componentType"] = "Image",
                    ["properties"] = new JObject
                    {
                        ["color"] = p["accentColor"] as JObject ?? DefaultAccentColor(),
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = indicatorName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0.15 },
                        new JObject { ["x"] = 0.02, ["y"] = 0.85 })
                }),
            };

            string result = await RunSequential(steps, name, "nav item");

            if (IsAllSuccess(result))
            {
                foreach (string n in createdNames)
                    known.Add(NameKey("gameobject", n));

                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                await ApplySpriteAsync(name, SpriteRoundedSmall);
                await ApplySpriteAsync(indicatorName, SpritePill);

                // Nav etiketi dar bir sütunda: uzun bir sekme adý ikinci satýra
                // atlarsa gösterge çizgisiyle hizasý kayar.
                await ApplyTextPolishAsync(name, allowWrap: false);

                await WireButtonGraphicAsync(name);
                await ApplySelectableColorsAsync(name);

                if (!isSelected)
                    await SetActiveAsync(indicatorName, false);
            }

            return result;
        }

        private static async Task<string> CreateLabel(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);

            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent,
                    PixelCappedHeight(p, parent, 0.12f, Mathf.Max(36f, (p["fontSize"]?.ToObject<float?>() ?? 18f) * 2.4f)), 0.03f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0, ["y"] = 0.8 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 1, ["y"] = 1 };
            }

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", null, out string preflightError))
            {
                return Error(preflightError);
            }

            string text = p["text"]?.ToString() ?? name;
            JObject color = p["color"] as JObject ?? DefaultTextColor();
            int fontSize = p["fontSize"]?.ToObject<int?>() ?? 18;
            string alignment = p["alignment"]?.ToString() ?? "Center";

            // Serbest etiketin zemini ebeveyninin rengi.
            color = EnsureReadable(color, ResolveBackground(parent), name, "text");

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = text, ["fontSize"] = fontSize,
                        ["alignment"] = alignment, ["color"] = color,

                        // Serbest bir etiket týklamayý yakalarsa altýndaki butonlar
                        // saðýr kalýyor. Metin hiçbir zaman raycast hedefi olmamalý.
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
            };

            string result = await RunSequential(steps, name, "label");

            if (IsAllSuccess(result))
            {
                known.Add(NameKey("gameobject", name));
                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                // Serbest bir etiket genellikle bir baþlýk ya da büyük bir okuma
                // deðeri: tek satýr ve kesilmeden. Uzun gövde metni için zaten
                // create_ui_row'un description alaný var.
                await ApplyTextPolishAsync(name, allowWrap: fontSize >= LargeTextThreshold);
            }

            return result;
        }

        /// <summary>
        /// Gerçek, TIKLANABILIR toggle switch: track (Image + native Unity Toggle) +
        /// ON dolgusu + knob.
        /// </summary>
        private static async Task<string> CreateToggle(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.75, ["y"] = 0.25 };
            JObject anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.95, ["y"] = 0.75 };

            string knobName = name + "Knob";
            string fillName = name + "OnFill";

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.",
                    new[] { knobName, fillName }, out string preflightError))
            {
                return Error(preflightError);
            }

            bool isOn = p["isOn"]?.ToObject<bool?>() ?? false;

            RememberAccent(p, "onColor");

            JObject trackColor = p["trackColor"] as JObject ?? DefaultToggleOffColor();
            JObject onColor = p["onColor"] as JObject ?? DefaultAccentColor();
            JObject knobColor = p["knobColor"] as JObject ?? WhiteColor();

            var steps = BuildToggleSteps(name, parent, knobName, fillName, isOn,
                trackColor, onColor, knobColor, anchorMin, anchorMax);

            string result = await RunSequential(steps, name, isOn ? "toggle (ON)" : "toggle (OFF)");

            if (IsAllSuccess(result))
            {
                known.Add(NameKey("gameobject", name));
                known.Add(NameKey("gameobject", fillName));
                known.Add(NameKey("gameobject", knobName));
                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                await ApplyToggleSpritesAsync(name, fillName, knobName);
                await WireToggleGraphicAsync(name, fillName, isOn);
                await ApplySelectableColorsAsync(name);
            }

            return result;
        }

        /// <summary>
        /// "Solda etiket, saðda kontrol" satýrý.
        ///
        /// ============ ÝKÝ BÜYÜK DEÐÝÞÝKLÝK ============
        ///
        /// 1. OTOMATÝK DÝKEY AKIÞ. Eski varsayýlan HER satýr için y 0.4-0.6 idi: model
        ///    anchor vermezse ikinci satýr birincinin tam üstüne biniyordu. Gerçek
        ///    testte ekranýn bozulma sebebi buydu. Artýk anchor verilmezse satýr bir
        ///    öncekinin ALTINA yerleþiyor.
        ///
        /// 2. control ARTIK GERÇEKTEN ÇALIÞIYOR. Eski sürüm 'toggle' dýþýndaki her
        ///    deðeri REDDEDÝYORDU ve konsolda üst üste þu görülüyordu:
        ///        "create_ui_row does not support control='slider'"
        ///    Model niyetini açýkça söylemiþti; doðru cevap onu reddetmek deðil,
        ///    istediði kontrolü kurmaktý. Artýk 'toggle', 'slider', 'progress' ve
        ///    'input' destekleniyor - hepsi satýrýn sað tarafýna yerleþiyor.
        /// ==============================================
        /// </summary>
        private static async Task<string> CreateRow(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            string labelText = p["label"]?.ToString() ?? name;
            string descriptionText = p["description"]?.ToString();
            string valueText = p["value"]?.ToString();
            string rawControl = p["control"]?.ToString();
            string control = rawControl?.Trim().ToLowerInvariant() ?? "none";
            bool isOn = p["isOn"]?.ToObject<bool?>() ?? false;

            bool hasDescription = !string.IsNullOrWhiteSpace(descriptionText);
            bool hasValue = !string.IsNullOrWhiteSpace(valueText);

            // ---------- KONTROL TÜRÜNÜ ÇÖZ ----------
            // Eþ anlamlýlarý kabul ediyoruz: model 'progressbar', 'textfield' gibi
            // makul varyantlar üretiyor ve bunlarý reddetmek hiçbir þey kazandýrmýyor.
            bool hasToggle = false;
            bool hasSlider = false;
            bool hasProgress = false;
            bool hasInput = false;

            switch (control)
            {
                case "none":
                case "":
                    break;

                case "toggle":
                case "switch":
                case "checkbox":
                    hasToggle = true;
                    break;

                case "slider":
                case "range":
                    hasSlider = true;
                    break;

                case "progress":
                case "progressbar":
                case "progress_bar":
                case "bar":
                    hasProgress = true;
                    break;

                case "input":
                case "inputfield":
                case "input_field":
                case "text":
                case "textfield":
                    hasInput = true;
                    break;

                default:
                    // Hâlâ tanýnmayan bir deðer SESSÝZCE YUTULMAMALI: model kontrolü
                    // eklediðini sanýr, ekranda hiçbir þey çýkmaz, hata da verilmez.
                    return Error(
                        $"create_ui_row does not recognise control='{rawControl}'. " +
                        "Supported values: 'toggle', 'slider', 'progress', 'input', or omit 'control' entirely. " +
                        "For a plain read-only readout pass 'value' instead of 'control'.");
            }

            bool hasControl = hasToggle || hasSlider || hasProgress || hasInput;

            // ============ ÝÇERÝKSÝZ SATIR - "ROW OF BUTTONS" TUZAÐI ============
            // GERÇEK TEST: login ekranýnda "A row of two buttons: Sign in and Cancel"
            // istendi. Model "row" kelimesini görüp create_ui_row çaðýrdý - etiketsiz,
            // deðersiz, kontrolsüz. Satýr makrosu buton taþýyamaz; ekranda yalnýzca
            // satýrýn kendi adý ("LoginButtonRow") yazdý ve model iþi bitmiþ saydý.
            // Ýki buton hiç kurulmadý.
            //
            // Bu satýrýn gösterecek hiçbir þeyi yok - reddetmek hiçbir þey kaybettirmiyor
            // ve modeli doðru makroya yönlendiriyor. Adýnda ya da etiketinde "button"
            // geçip kontrolü olmayan bir satýr da ayný tuzaðýn iþareti.
            // ===================================================================
            string givenLabel = p["label"]?.ToString();
            bool labelIsReal = !string.IsNullOrWhiteSpace(givenLabel) &&
                               !string.Equals(givenLabel.Trim(), name, StringComparison.OrdinalIgnoreCase);

            bool mentionsButtons =
                name.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (givenLabel ?? "").IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasControl && !hasValue && !hasDescription && (!labelIsReal || mentionsButtons))
            {
                return Error(
                    $"create_ui_row '{name}' would show nothing but a label - it has no value, description or control. " +
                    "This call was NOT executed. A row cannot hold buttons. " +
                    $"If you meant a row of BUTTONS, use create_ui_button_bar with parent '{parent}' and a 'buttons' array, " +
                    "for example {\"type\":\"create_ui_button_bar\",\"params\":{\"name\":\"ActionBar\",\"parent\":\"" + parent +
                    "\",\"buttons\":[\"Sign in\",\"Cancel\"]}}.");
            }

            // Satýrýn kontrol rengi verildiyse accent budur - sonraki toggle ve
            // çubuklar renk verilmeden gelse bile ayný temayý kullansýn.
            RememberAccent(p, "onColor", "fillColor");

            // ---------- KONUM ----------
            // Açýklamalý satýr daha yüksek olmalý, yoksa iki satýr metin sýkýþýyor.
            //
            // PÝKSEL SINIRI: satýr ebeveyninin %17'si. Ebeveyn büyük bir kartsa bu
            // 120+ piksel demekti ve 15 puntoluk yazýlar dev boþluklarda kayboluyordu.
            // Artýk tek satýrlýk bir satýr en fazla ~52px, açýklamalý olan ~76px -
            // gerçek bir ayarlar uygulamasýndaki deðerler. Punto büyükse sýnýr da
            // büyüyor, yazý hiçbir zaman sýkýþmýyor.
            float rowFont = p["fontSize"]?.ToObject<float?>() ?? 15f;
            float autoHeight = PixelCappedHeight(p, parent,
                hasDescription ? 0.26f : 0.17f,
                hasDescription ? Mathf.Max(76f, rowFont * 5f) : Mathf.Max(52f, rowFont * 3.2f),
                // ASGARÝ: yazýnýn kendisi kadar yer her zaman ayrýlmalý. 40 puntoluk
                // "1 284" deðeri 40 piksellik bir satýra sýkýþýnca kutunun dýþýna taþýp
                // kart baþlýðýnýn üstüne biniyordu.
                hasDescription ? rowFont * 3.0f : rowFont * 1.8f);

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, autoHeight, 0.02f, out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.02, ["y"] = 0.4 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.98, ["y"] = 0.6 };
            }

            var derived = new List<string> { name + "Label" };
            if (hasDescription) derived.Add(name + "Description");
            if (hasValue) derived.Add(name + "Value");

            if (hasToggle)
            {
                derived.Add(name + "Toggle");
                derived.Add(name + "ToggleOnFill");
                derived.Add(name + "ToggleKnob");
            }

            if (hasSlider || hasProgress)
            {
                string barBase = name + (hasSlider ? "Slider" : "Bar");
                derived.Add(barBase);
                derived.Add(barBase + "Background");
                derived.Add(barBase + "FillArea");
                derived.Add(barBase + "Fill");
                derived.Add(barBase + "HandleArea");
                derived.Add(barBase + "Handle");
            }

            if (hasInput)
            {
                string inputBase = name + "Input";
                derived.Add(inputBase);
                derived.Add(inputBase + "TextArea");
                derived.Add(inputBase + "Placeholder");
                derived.Add(inputBase + "Text");
            }

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", derived, out string preflightError))
            {
                return Error(preflightError);
            }

            // Satýr konteynerinin arka planý varsayýlan olarak þeffaf VE raycast dýþý:
            // týklamalarý yakalamamalý, yoksa içindeki kontrole týklanamaz.
            JObject rowColor = p["color"] as JObject ?? TransparentColor();
            bool rowIsInvisible = (rowColor["a"]?.ToObject<float?>() ?? 1f) <= 0.01f;

            JObject labelColor = p["labelColor"] as JObject ?? DefaultTextColor();
            JObject descColor = p["descriptionColor"] as JObject ?? DefaultMutedTextColor();
            JObject valueColor = p["valueColor"] as JObject ?? DefaultSecondaryTextColor();

            // Satýrýn zemini kendi arka planý þeffafsa ebeveyninin rengi - ki
            // neredeyse her zaman öyle, çünkü satýr arka planý varsayýlan olarak
            // þeffaf. Üç metin de ayný zemine karþý denetleniyor.
            Color rowBackground = rowIsInvisible
                ? ResolveBackground(parent)
                : ToColor(rowColor, ResolveBackground(parent));

            labelColor = EnsureReadable(labelColor, rowBackground, name, "label");
            // Yalnýzca GERÇEKTEN VAR OLAN metin denetleniyor. Eskiden açýklamasý ya da
            // deðeri olmayan satýrlar için de denetim yapýlýyor ve hiç çizilmeyecek bir
            // metin için "okunmuyor" uyarýsý basýlýyordu - üç sahte uyarý, gerçek
            // uyarýlarý gömen bir gürültü.
            if (hasDescription)
                descColor = EnsureReadable(descColor, rowBackground, name, "description");

            if (hasValue)
                valueColor = EnsureReadable(valueColor, rowBackground, name, "value");
            int fontSize = p["fontSize"]?.ToObject<int?>() ?? 15;
            int descFontSize = p["descriptionFontSize"]?.ToObject<int?>() ?? 12;

            string labelName = name + "Label";

            // ============ ETÝKET GENÝÞLÝÐÝ - DÜZELTÝLEN ÇAKIÞMA ============
            // Eski kod etiketi HER DURUMDA 0.03-0.68 bandýna, deðer metnini ise
            // 0.55-0.78'e koyuyordu. 0.55-0.68 aralýðýnda ÝKÝSÝ ÜST ÜSTE BÝNÝYORDU.
            //
            // Artýk etiketin sað kenarý, saðda ne olduðuna göre hesaplanýyor.
            // Toggle dar (0.78'de baþlar), slider/input geniþ (0.50'de baþlar).
            float controlLeft;

            if (hasToggle) controlLeft = 0.78f;
            else if (hasSlider || hasProgress || hasInput) controlLeft = 0.48f;
            else controlLeft = 1f;

            // ============ ETÝKET GENÝÞLÝÐÝ - DEÐERE GÖRE ============
            // GERÇEK TEST: dar bir sütundaki slider'lý satýrlarda etiketler kesildi
            // ("Track thre...", "Minimum r..."). Sebep sabit bir paydý: hem deðer hem
            // kontrol varsa etikete controlLeft - 0.28 kalýyordu, yani slider'lý bir
            // satýrda geniþliðin %22'si. Yanýndaki "60" deðerine ise %22 ayrýlýyordu -
            // iki karakter için, etiketin tamamý kadar yer.
            //
            // Artýk deðer KENDÝ uzunluðuna göre yer alýyor; artan her þey etikete
            // gidiyor. Sayýsal deðerler ("60", "4 / 4", "92 C") dar bir alana sýðar;
            // uzun metinler ("Connected", "English") için pay geniþliyor.
            // ========================================================
            // DÜZELTÝLDÝ - DAR SÜTUNDA DEÐERLER KESÝLÝYORDU: pay karakter sayýsýna göre
            // SABÝT ORANLARA baðlýydý (6 karaktere kadar %11). Geniþ bir kartta yeterli,
            // ama 422 piksellik dar bir sütunda %11 yalnýzca 46 piksel ediyor ve
            // "Active" oraya sýðmýyordu: ekranda "Act...", "Conne..." görünüyordu.
            //
            // Oran yerine gerçek piksel geniþliði hesaplanýyor: karakter sayýsý x punto.
            // Ayný deðer dar bir sütunda daha BÜYÜK bir oran alýyor, geniþ bir kartta
            // daha küçüðünü - ikisinde de metnin tam olarak ihtiyacý kadar.
            float valueWidth = 0f;

            if (hasValue)
            {
                float parentWidthPx = Mathf.Max(1f, AbsoluteWidth(parent) * ReferenceScreenWidth);

                // 0.6 x punto: LiberationSans'ta ortalama karakter geniþliði. Sonundaki
                // pay, sað kenarla metin arasýndaki nefes payý.
                float neededPx = valueText.Trim().Length * rowFont * 0.6f + 10f;

                valueWidth = Mathf.Clamp(neededPx / parentWidthPx, 0.10f, 0.45f);
            }

            float labelRight;

            if (hasValue && hasControl) labelRight = Mathf.Max(0.22f, controlLeft - valueWidth - 0.04f);
            else if (hasControl) labelRight = controlLeft - 0.04f;
            else if (hasValue) labelRight = Mathf.Max(0.35f, 0.97f - valueWidth - 0.02f);
            else labelRight = 0.97f;

            JObject labelMin = new JObject { ["x"] = 0.03, ["y"] = hasDescription ? 0.45 : 0.0 };
            JObject labelMax = new JObject { ["x"] = labelRight, ["y"] = hasDescription ? 0.95 : 1.0 };

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = rowColor, ["raycastTarget"] = !rowIsInvisible }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = labelName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = labelName, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = labelText, ["fontSize"] = fontSize,
                        ["alignment"] = "Left", ["color"] = labelColor,
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = labelName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(labelMin, labelMax)
                }),
            };

            var createdNames = new List<string> { name, labelName };

            if (hasDescription)
            {
                string descName = name + "Description";

                steps.Add(Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = descName, ["parent"] = name }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = descName,
                    ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = descriptionText,
                        ["fontSize"] = descFontSize,
                        ["alignment"] = "Left",
                        ["color"] = descColor,
                        ["raycastTarget"] = false
                    }
                }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property",
                    ["target"] = descName,
                    ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.03, ["y"] = 0.05 },
                        new JObject { ["x"] = labelRight, ["y"] = 0.45 })
                }));

                createdNames.Add(descName);
            }

            // control ve value BÝRLÝKTE gelirse ikisi de gösterilir. Eski sürümde bu
            // bir 'else if' zinciriydi ve toggle varsa value SESSÝZCE yutuluyordu.
            var summaryParts = new List<string>();
            string rowToggleName = null;
            string rowFillName = null;
            string rowKnobName = null;

            if (hasValue)
            {
                string valueName = name + "Value";

                // Deðer metni, kontrolün hemen SOLUNA. Kontrol yoksa sað kenara kadar.
                // Geniþliði yukarýda metnin uzunluðuna göre hesaplandý.
                float valueRight = hasControl ? controlLeft - 0.02f : 0.97f;
                float valueLeft = Mathf.Max(labelRight + 0.02f, valueRight - valueWidth);

                steps.Add(Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = valueName, ["parent"] = name }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "add",
                    ["target"] = valueName,
                    ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = valueText,
                        ["fontSize"] = fontSize,
                        ["alignment"] = "Right",
                        ["color"] = valueColor,
                        ["raycastTarget"] = false
                    }
                }));
                steps.Add(Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property",
                    ["target"] = valueName,
                    ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = valueLeft, ["y"] = 0 },
                        new JObject { ["x"] = valueRight, ["y"] = 1 })
                }));

                createdNames.Add(valueName);
                summaryParts.Add("value text");
            }

            if (hasToggle)
            {
                rowToggleName = name + "Toggle";
                rowFillName = rowToggleName + "OnFill";
                rowKnobName = rowToggleName + "Knob";

                JObject trackColor = p["trackColor"] as JObject ?? DefaultToggleOffColor();
                JObject onColor = p["onColor"] as JObject ?? DefaultAccentColor();
                JObject knobColor = p["knobColor"] as JObject ?? WhiteColor();

                steps.AddRange(BuildToggleSteps(
                    rowToggleName, name, rowKnobName, rowFillName, isOn,
                    trackColor, onColor, knobColor,
                    new JObject { ["x"] = controlLeft, ["y"] = 0.28 },
                    new JObject { ["x"] = 0.97, ["y"] = 0.72 }));

                createdNames.Add(rowToggleName);
                createdNames.Add(rowFillName);
                createdNames.Add(rowKnobName);

                summaryParts.Add(isOn ? "toggle ON" : "toggle OFF");
            }

            string result = await RunSequential(steps, name,
                $"row ({(summaryParts.Count > 0 ? string.Join(" + ", summaryParts) : "no control")})");

            if (!IsAllSuccess(result))
                return result;

            foreach (string n in createdNames)
                known.Add(NameKey("gameobject", n));

            RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

            // Etiket ve açýklama da kayda giriyor; kontrol alt macro'su kendi
            // çakýþma denetimini yaparken bunlarý görmeli.
            RegisterLayout(name, labelName, ToRect(labelMin, labelMax));

            // ---------- METÝN TAÞMASI ----------
            // Satýrýn etiketi ve deðer metni TEK SATIRLIK alanlarda duruyor: sýðmazsa
            // "..." ile kesilmeli. Ýkinci satýra atlarlarsa satýr yüksekliði yetmediði
            // için alt yarýlarý kýrpýlýyor ve yazý bozuk görünüyor.
            //
            // Açýklama satýrý ise iki satýr kaldýracak kadar yer kaplýyor, orada
            // kaydýrma serbest.
            await ApplyTextPolishAsync(labelName, allowWrap: fontSize >= LargeTextThreshold);

            if (hasValue)
                await ApplyTextPolishAsync(name + "Value", allowWrap: fontSize >= LargeTextThreshold);

            if (hasDescription)
                await ApplyTextPolishAsync(name + "Description", allowWrap: true);

            if (rowToggleName != null)
            {
                RegisterLayout(name, rowToggleName, new LayoutRect
                {
                    MinX = controlLeft,
                    MinY = 0.28f,
                    MaxX = 0.97f,
                    MaxY = 0.72f
                });

                await ApplyToggleSpritesAsync(rowToggleName, rowFillName, rowKnobName);
                await WireToggleGraphicAsync(rowToggleName, rowFillName, isOn);
                await ApplySelectableColorsAsync(rowToggleName);
            }

            // ---------- SATIRIN ÝÇÝNE KONTROLÜ KUR ----------
            // Satýr kurulduktan SONRA yapýlýyor: alt macro'nun ebeveyni artýk sahnede
            // ve kayýtta. Kontrol baþarýsýz olursa satýr yine ayakta kalýyor ve mesaj
            // eksiði açýkça söylüyor - sessiz kayýp yok.
            if (hasSlider || hasProgress || hasInput)
            {
                string childResult = await BuildRowControlAsync(
                    p, known, name, controlLeft, hasSlider, hasProgress, hasInput);

                if (!IsAllSuccess(childResult))
                {
                    return new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["success"] = false,
                            ["message"] =
                                $"Row '{name}' was created with its label, but the {(hasInput ? "input field" : hasSlider ? "slider" : "progress bar")} inside it could not be built: " +
                                ExtractMessage(childResult) +
                                $" The row itself is fine - add the control separately with parent='{name}'.",
                            ["createdName"] = name
                        }
                    }.ToString(Newtonsoft.Json.Formatting.None);
                }

                summaryParts.Add(hasInput ? "input field" : hasSlider ? "slider" : "progress bar");

                return new JObject
                {
                    ["status"] = "success",
                    ["result"] = new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Created row '{name}' ({string.Join(" + ", summaryParts)}).",
                        ["createdName"] = name
                    }
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            return result;
        }

        /// <summary>
        /// Satýrýn sað tarafýna slider / progress bar / input field kurar.
        ///
        /// Satýr seviyesindeki ilgili ayarlar alt macro'ya MÝRAS geçiyor, böylece
        /// model iki ayrý yere deðer yazmak zorunda kalmýyor:
        ///   minValue, maxValue, value, wholeNumbers, fillColor, handleColor,
        ///   trackColor, placeholder, isPassword
        /// </summary>
        private static async Task<string> BuildRowControlAsync(
            JObject p, HashSet<string> known, string rowName, float controlLeft,
            bool isSlider, bool isProgress, bool isInput)
        {
            var child = new JObject
            {
                ["parent"] = rowName,
                ["anchorMin"] = new JObject { ["x"] = controlLeft, ["y"] = isInput ? 0.15 : 0.25 },
                ["anchorMax"] = new JObject { ["x"] = 0.97, ["y"] = isInput ? 0.85 : 0.75 }
            };

            foreach (string field in new[]
            {
                "minValue", "maxValue", "value", "wholeNumbers",
                "fillColor", "handleColor", "trackColor",
                "placeholder", "isPassword", "fontSize", "textColor"
            })
            {
                InheritIfMissing(child, p, field);
            }

            if (isInput)
            {
                child["name"] = rowName + "Input";

                // 'value' bir slider sayýsý olarak miras alýnmýþ olabilir; input için
                // baþlangýç metni 'text' alanýndan gelmeli.
                child.Remove("value");

                if (p["text"] != null)
                    child["text"] = p["text"].DeepClone();

                return await CreateInputField(child, known);
            }

            child["name"] = rowName + (isSlider ? "Slider" : "Bar");

            return await BuildBarAsync(child, known, interactive: isSlider);
        }

        /// <summary>
        /// Gerçekten SÜRÜKLENEBÝLÝR bir slider.
        ///
        /// Unity'nin Slider bileþeni tek baþýna ÇALIÞMAZ: iki alt objenin
        /// RectTransform'u Slider'ýn fillRect / handleRect alanlarýna BAÐLANMALI.
        /// Baðlanmazsa slider görünür ama sürüklenmez. O baðlama manage_components ile
        /// YAPILAMIYOR - converter gelen string'i asset yolu sanýyor.
        ///
        /// Yapý (Unity'nin kendi slider prefabýyla ayný):
        ///   Slider (Image + Slider)
        ///     +-- Background      arka oluk
        ///     +-- FillArea
        ///     |     +-- Fill      dolan kýsým      -> fillRect
        ///     +-- HandleArea
        ///           +-- Handle    sürüklenen top   -> handleRect
        /// </summary>
        private static async Task<string> CreateSlider(JObject p, HashSet<string> known)
        {
            return await BuildBarAsync(p, known, interactive: true);
        }

        /// <summary>
        /// SÜRÜKLENEMEYEN dolum çubuðu: batarya, sinyal gücü, saðlýk, yükleme.
        ///
        /// Bunun için tek seçenek create_ui_slider'dý ve YANLIÞTI: bir batarya
        /// göstergesini kullanýcý sürükleyip deðiþtirebiliyordu. Ayný yapý kuruluyor
        /// ama Slider.interactable=false ve handle gizli.
        /// </summary>
        private static async Task<string> CreateProgressBar(JObject p, HashSet<string> known)
        {
            return await BuildBarAsync(p, known, interactive: false);
        }

        /// <summary>
        /// Slider ve progress bar'ýn ortak gövdesi. Tek fark interactable ve handle
        /// görünürlüðü - kodu ikiye bölmek iki ayrý kýrýlma noktasý demek olurdu.
        /// </summary>
        private static async Task<string> BuildBarAsync(JObject p, HashSet<string> known, bool interactive)
        {
            string kindLabel = interactive ? "slider" : "progress bar";

            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.14f, 40f), 0.03f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.55, ["y"] = 0.30 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.97, ["y"] = 0.70 };
            }

            string backgroundName = name + "Background";
            string fillAreaName = name + "FillArea";
            string fillName = name + "Fill";
            string handleAreaName = name + "HandleArea";
            string handleName = name + "Handle";

            var allNames = new List<string> { name, backgroundName, fillAreaName, fillName, handleAreaName, handleName };

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", allNames.Skip(1), out string preflightError))
            {
                return Error(preflightError);
            }

            float minValue = p["minValue"]?.ToObject<float?>() ?? 0f;
            float maxValue = p["maxValue"]?.ToObject<float?>() ?? (interactive ? 1f : 100f);
            float value = p["value"]?.ToObject<float?>() ?? minValue;
            bool wholeNumbers = p["wholeNumbers"]?.ToObject<bool?>() ?? false;

            if (maxValue <= minValue)
                return Error($"maxValue ({maxValue}) must be greater than minValue ({minValue}).");

            value = Mathf.Clamp(value, minValue, maxValue);

            RememberAccent(p, "fillColor");

            JObject trackColor = p["trackColor"] as JObject ?? DefaultToggleOffColor();
            JObject fillColor = p["fillColor"] as JObject ?? DefaultAccentColor();
            JObject handleColor = p["handleColor"] as JObject ?? WhiteColor();

            // ============ ÇUBUK KALINLIÐI - DÜZELTÝLDÝ ============
            // GERÇEK ARIZA: progress bar 0.25-0.75 idi, yani elemanýn dikey alanýnýn
            // YÜZDE ELLÝSÝ. Hap biçimli sprite ile birleþince 1080p'de ~55 piksellik
            // dev bir kapsül çýkýyordu; ekranda batarya göstergesi bir düðmeye
            // benziyordu.
            //
            // Doðru oranlar farklý, çünkü iki elemanýn iþi farklý:
            //   - SLIDER sürüklenir. Tutamaðýn basacaðý bir yüzeye ihtiyacý var ve
            //     tutamak oluktan taþar; oluk çok ince olursa tutamak havada durur
            //     gibi görünür. %30 uygun.
            //   - PROGRESS BAR yalnýzca okunur. Tutamaðý yok, dokunulmuyor. Ýnce
            //     olmasý hem daha okunaklý hem de gerçek uygulamalardaki görünümü bu.
            //     %24 uygun.
            //
            // Deðerler eleman yüksekliðinin ORANI olduðu için, satýr içinde de tek
            // baþýna da tutarlý görünüyorlar.
            // ======================================================
            double trackLow = interactive ? 0.35 : 0.38;
            double trackHigh = interactive ? 0.65 : 0.62;
            double fillInset = interactive ? 0.02 : 0.0;

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = TransparentColor(), ["raycastTarget"] = interactive }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Slider",
                    ["properties"] = new JObject
                    {
                        ["minValue"] = minValue,
                        ["maxValue"] = maxValue,
                        ["value"] = value,
                        ["wholeNumbers"] = wholeNumbers,

                        // Progress bar sürüklenemez. Eskiden bunun için tek seçenek
                        // gerçek bir slider'dý ve kullanýcý bataryayý elle
                        // deðiþtirebiliyordu.
                        ["interactable"] = interactive
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),

                // Arka oluk
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = backgroundName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = backgroundName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = trackColor, ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = backgroundName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = trackLow },
                        new JObject { ["x"] = 1, ["y"] = trackHigh })
                }),

                // FillArea: handle yarýçapý kadar içeriden baþlýyor ki dolgu uçlardan
                // taþmasýn. Görünmez, sadece bir çerçeve.
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = fillAreaName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = fillAreaName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = TransparentColor(), ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = fillAreaName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = fillInset, ["y"] = trackLow },
                        new JObject { ["x"] = 1.0 - fillInset, ["y"] = trackHigh })
                }),
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = fillName, ["parent"] = fillAreaName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = fillName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = fillColor, ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = fillName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0 },
                        new JObject { ["x"] = 1, ["y"] = 1 })
                }),

                // HandleArea + Handle
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = handleAreaName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = handleAreaName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = TransparentColor(), ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = handleAreaName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.02, ["y"] = 0 },
                        new JObject { ["x"] = 0.98, ["y"] = 1 })
                }),
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = handleName, ["parent"] = handleAreaName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = handleName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = handleColor, ["raycastTarget"] = interactive }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = handleName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0.1 },
                        new JObject { ["x"] = 0.06, ["y"] = 0.9 })
                }),
            };

            string result = await RunSequential(steps, name, kindLabel);

            if (IsAllSuccess(result))
            {
                foreach (string n in allNames)
                    known.Add(NameKey("gameobject", n));

                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                await ApplySpriteAsync(backgroundName, SpritePill);
                await ApplySpriteAsync(fillName, SpritePill);
                await ApplySpriteAsync(handleName, SpriteCircle);

                // KRÝTÝK ADIM: referanslarý baðla. Bu olmadan slider sürüklenmez ve
                // progress bar hiç dolmaz.
                await WireSliderAsync(name, fillName, handleName, value, interactive);
            }

            return result;
        }

        /// <summary>
        /// Gerçekten YAZILABÝLÝR bir metin giriþ alaný.
        ///
        /// Slider ile ayný sorun: TMP_InputField'ýn textComponent alaný baðlanmazsa
        /// alan týklanýr, imleç görünür ama yazýlan hiçbir þey ekrana çýkmaz.
        ///
        /// Yapý:
        ///   InputField (Image + TMP_InputField)
        ///     +-- TextArea            kýrpma çerçevesi
        ///           +-- Placeholder   boþken görünen soluk ipucu
        ///           +-- Text          kullanýcýnýn yazdýðý metin  -> textComponent
        /// </summary>
        private static async Task<string> CreateInputField(JObject p, HashSet<string> known)
        {
            string name = RequireString(p, "name", out string err1);
            if (err1 != null) return Error(err1);

            string parent = RequireString(p, "parent", out string err2);
            if (err2 != null) return Error(err2);


            // Kart adý verildiyse içerik alanýna yönlendir - bkz. RedirectToCardContent.
            parent = RedirectToCardContent(parent);

            JObject anchorMin;
            JObject anchorMax;

            if (!TryAutoPlaceVertical(p, parent, PixelCappedHeight(p, parent, 0.16f, 52f), 0.05f,
                    out anchorMin, out anchorMax))
            {
                anchorMin = p["anchorMin"] as JObject ?? new JObject { ["x"] = 0.1, ["y"] = 0.4 };
                anchorMax = p["anchorMax"] as JObject ?? new JObject { ["x"] = 0.9, ["y"] = 0.6 };
            }

            string textAreaName = name + "TextArea";
            string placeholderName = name + "Placeholder";
            string textName = name + "Text";

            var allNames = new List<string> { name, textAreaName, placeholderName, textName };

            if (!PreflightElement(p, known, name, parent, anchorMin, anchorMax,
                    "Move on to the next element.", allNames.Skip(1), out string preflightError))
            {
                return Error(preflightError);
            }

            string placeholderText = p["placeholder"]?.ToString() ?? "Enter text...";
            string initialText = p["text"]?.ToString() ?? "";
            int fontSize = p["fontSize"]?.ToObject<int?>() ?? 15;
            bool isPassword = p["isPassword"]?.ToObject<bool?>() ?? false;

            JObject backgroundColor = p["color"] as JObject ?? DefaultToggleOffColor();
            JObject textColor = p["textColor"] as JObject ?? DefaultTextColor();
            JObject placeholderColor = p["placeholderColor"] as JObject ?? DefaultMutedTextColor();

            var steps = new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = name, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = name, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = backgroundColor, ["raycastTarget"] = true }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = name, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),

                // TextArea: metnin kutunun dýþýna taþmasýný engelleyen çerçeve.
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = textAreaName, ["parent"] = name }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = textAreaName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = TransparentColor(), ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = textAreaName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0.03, ["y"] = 0.1 },
                        new JObject { ["x"] = 0.97, ["y"] = 0.9 })
                }),

                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = placeholderName, ["parent"] = textAreaName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = placeholderName, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = placeholderText, ["fontSize"] = fontSize,
                        ["alignment"] = "Left", ["color"] = placeholderColor,
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = placeholderName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0 },
                        new JObject { ["x"] = 1, ["y"] = 1 })
                }),

                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = textName, ["parent"] = textAreaName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = textName, ["componentType"] = "TextMeshProUGUI",
                    ["properties"] = new JObject
                    {
                        ["text"] = initialText, ["fontSize"] = fontSize,
                        ["alignment"] = "Left", ["color"] = textColor,
                        ["raycastTarget"] = false
                    }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = textName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0 },
                        new JObject { ["x"] = 1, ["y"] = 1 })
                }),
            };

            string result = await RunSequential(steps, name, "input field");

            if (IsAllSuccess(result))
            {
                foreach (string n in allNames)
                    known.Add(NameKey("gameobject", n));

                RegisterLayout(parent, name, ToRect(anchorMin, anchorMax));

                await ApplySpriteAsync(name, SpriteRoundedSmall);

                // KRÝTÝK ADIM: TMP_InputField'ý ekle ve referanslarý baðla.
                await WireInputFieldAsync(name, textAreaName, placeholderName, textName, initialText, isPassword);
            }

            return result;
        }

        // =====================================================
        // SCRIPT AUTHORING
        // =====================================================

        /// <summary>
        /// Modelin script yazmasý için TEK klasör. OllamaToolAgent'taki
        /// GENERATED_SCRIPT_FOLDER ve PromptRulesUI'deki kural metni bu yolla
        /// eþleþmeli - üçü ayrýþýrsa model bir yere yazar, güvenlik baþka yere bakar.
        /// </summary>
        private const string GeneratedScriptFolder = "Assets/UI/Generated";

        /// <summary>
        /// Bir C# davranýþ script'ini diske yazar.
        ///
        /// GERÇEK TEST BULGUSU: model script'i execute_code ile yazmayý beceremiyordu.
        /// Sebep modelin kod yeteneði DEÐÝL, paketleme zinciri: kaynak kodun bir C#
        /// STRING LITERAL'inin içine gömülmesi gerekiyor, yani her satýr sonu ve
        /// týrnak ÝKÝ KEZ kaçýrýlmalý - bir kez C# için, bir kez JSON için. Qwen bunu
        /// tutturamýyor: "Line 3: Newline in constant" / "Line 4: Identifier expected".
        ///
        /// Bu macro ile kaynak kod JSON'un NORMAL bir alanýnda geliyor: tek kat kaçýþ,
        /// onu da JSON serileþtiricisi hallediyor.
        ///
        /// Dosya YAZILIR ama IMPORT EDÝLMEZ - import görev bitiminde
        /// GeneratedScriptWatchdog tarafýndan yapýlýyor, çünkü import anýnda domain
        /// reload çalýþan ajaný öldürüyor.
        /// </summary>
        private static string WriteScript(JObject p)
        {
            string name = RequireString(p, "name", out string nameErr);
            if (nameErr != null) return Error(nameErr);

            string content = p["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
                return Error("'content' is required - it must contain the full C# source of the script.");

            name = name.Trim();

            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);

            // Sadece sýnýf adý bekleniyor; model yol gönderirse son parçayý alýyoruz.
            int lastSlash = name.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash >= 0)
                name = name.Substring(lastSlash + 1);

            if (!IsValidTypeName(name))
                return Error($"'{name}' is not a valid C# class name. Use letters, digits and underscore only, and do not start with a digit.");

            // DOSYA ADI = SINIF ADI.
            //
            // Unity, dosya adý ile sýnýf adý farklý olan bir MonoBehaviour'u yükleyemez
            // ve verdiði hata sorunun kaynaðýný göstermez ("The referenced script on
            // this Behaviour is missing"). Yazmadan ÖNCE kesiyoruz.
            if (!ContentDeclaresClass(content, name, out string declaredNames))
            {
                return Error(
                    $"The file will be '{name}.cs' but the source declares '{declaredNames}'. " +
                    "Unity cannot load a MonoBehaviour whose file name and class name differ. " +
                    $"Either set name to '{declaredNames}' or rename the class to '{name}'.");
            }

            // ============ NAMESPACE DENETÝMÝ ============
            // Ajan bu tipi bir sonraki görevde component olarak ekliyor ve bunu SADECE
            // SINIF ADIYLA yapýyor (AddComponent("TabController") gibi). Namespace
            // içindeki bir tip o yolla BULUNAMAZ - ekleme sessizce baþarýsýz oluyor ve
            // kimse sebebini anlamýyor. Sekme baðlama hedefinin önündeki engel buydu.
            if (Regex.IsMatch(content, @"^\s*namespace\s+\w", RegexOptions.Multiline))
            {
                return Error(
                    "The source declares a namespace. Generated behaviour scripts must have NO namespace: " +
                    "the agent attaches them later by plain class name, and a namespaced type cannot be found that way. " +
                    "Remove the namespace declaration and keep the class at the top level.");
            }

            foreach (string forbidden in ForbiddenInGeneratedScript)
            {
                if (content.IndexOf(forbidden, StringComparison.Ordinal) >= 0)
                {
                    return Error(
                        $"The source contains '{forbidden}', which is not allowed in a generated runtime script. " +
                        "Editor-only APIs break builds and can break the Editor compile.");
                }
            }

            string path = GeneratedScriptFolder + "/" + name + ".cs";

            try
            {
                System.IO.Directory.CreateDirectory(GeneratedScriptFolder);

                // Sondaki yeni satýr bilinçli: bazý derleyici sürümleri dosya sonunda
                // yeni satýr olmadýðýnda uyarý veriyor.
                System.IO.File.WriteAllText(path, content.TrimEnd() + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UiMacroExpander] Could not write '{path}': {ex}");
                return Error($"Could not write '{path}': {ex.Message}");
            }

            Debug.Log($"[UiMacroExpander] Wrote generated script '{path}' ({content.Length} chars). It will be imported and compiled after this task finishes.");

            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject
                {
                    ["success"] = true,
                    ["message"] =
                        $"Script written to {path}. Unity has NOT compiled it yet, so the type does not exist. " +
                        "Do NOT try to add it as a component in this task - compilation happens automatically after the task ends. " +
                        "Finish the remaining work and reply with plain text.",
                    ["createdName"] = name,
                    ["scriptPath"] = path
                }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Üretilen bir RUNTIME script'inde bulunmamasý gereken ifadeler.
        ///
        /// UnityEditor'a dokunan bir MonoBehaviour derlemeyi Editor'de geçer ama build
        /// alýnýrken patlar - ve o an kimse sebebini bu dosyada aramaz.
        /// </summary>
        private static readonly string[] ForbiddenInGeneratedScript =
        {
            "UnityEditor",
            "[MenuItem",
            "#if UNITY_EDITOR",
            "System.IO.File.Delete",
            "Application.Quit",
            "AssetDatabase"
        };

        private static bool IsValidTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (char.IsDigit(name[0]))
                return false;

            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Kaynak kodun beklenen adda bir sýnýf bildirip bildirmediðini kontrol eder.
        /// Bildirmiyorsa bulunan adlarý döndürür ki model hangi ismi düzelteceðini
        /// bilsin.
        /// </summary>
        private static bool ContentDeclaresClass(string content, string expectedName, out string declaredNames)
        {
            declaredNames = "";

            var matches = Regex.Matches(content, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)");

            if (matches.Count == 0)
            {
                declaredNames = "no class at all";
                return false;
            }

            var names = new List<string>();

            foreach (Match m in matches)
            {
                string found = m.Groups[1].Value;

                if (string.Equals(found, expectedName, StringComparison.Ordinal))
                    return true;

                if (!names.Contains(found))
                    names.Add(found);
            }

            declaredNames = string.Join(", ", names);
            return false;
        }

        // =====================================================
        // EDITOR-SIDE FIXUPS (execute_code)
        // =====================================================

        /// <summary>
        /// Bir GameObject'i adýna göre bulan, PASÝF objeleri de gören yardýmcý kod.
        ///
        /// GameObject.Find yalnýzca AKTÝF objeleri bulur. CreateNavItem, seçili olmayan
        /// item'larýn gösterge çubuðunu SetActive(false) yapýyor; o andan sonra hiçbir
        /// kod o objeye eriþemiyordu - sekme mantýðý baðlanýrken göstergeler
        /// "bulunamadý" diye atlanýyor ve seçili sekmenin çizgisi hiç belirmiyordu.
        /// Ayný sorun üst üste binen sekme panelleri için de geçerli.
        ///
        /// Resources.FindObjectsOfTypeAll pasifleri de döndürüyor; prefab asset'lerini
        /// elemek için scene kontrolü yapýyoruz.
        /// </summary>
        private const string FindHelperCode =
            "System.Func<string, GameObject> __find = (n) => {\n" +
            "  var direct = GameObject.Find(n);\n" +
            "  if (direct != null) return direct;\n" +
            "  foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) {\n" +
            "    if (t.gameObject.name != n) continue;\n" +
            "    if (!t.gameObject.scene.IsValid()) continue;\n" +
            "    return t.gameObject;\n" +
            "  }\n" +
            "  return null;\n" +
            "};\n";

        /// <summary>
        /// execute_code ile küçük bir C# parçasý çalýþtýrýr ve sonucu TOLERE EDER.
        ///
        /// Bu katman, manage_components'ýn yapamadýðý iþler için var: bir component
        /// alanýna baþka bir SAHNE OBJESÝ atamak. MCP converter'ý gelen string'i asset
        /// yolu sanýyor: "Could not load asset at path 'XxxKnob' as type 'Graphic'".
        ///
        /// Baþarýsýzlýk ELEMANI ÝPTAL ETMEZ: yapý zaten kurulmuþ, sadece bir cila
        /// eksik kalýr. Bu yüzden Console'a Log (Error deðil) yazýyoruz.
        ///
        /// Kod yazarken DÝKKAT: execute_code sarmalayýcýsý yalnýzca System,
        /// System.Collections.Generic, System.Linq, System.Reflection, UnityEngine ve
        /// UnityEditor import ediyor. UnityEngine.UI ve TMPro TAM NÝTELÝKLÝ yazýlmalý.
        /// </summary>
        private static async Task<bool> RunEditorCodeAsync(string code, string purpose)
        {
            JObject call = Cmd("execute_code", new JObject
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
                Debug.Log($"[UiMacroExpander] {purpose} skipped: {ex.Message}");
                return false;
            }

            if (!WasStepSuccessful(result))
            {
                Debug.Log($"[UiMacroExpander] {purpose} did not apply. The element still works, only this refinement is missing.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// execute_code sonucundan, verilen ÖNEKLE baþlayan return deðerini çýkarýr ve
        /// öneki kýrpýp geri kalaný döndürür. Bulamazsa null.
        ///
        /// ============ NEDEN ÖNEK ============
        /// MCP sarmalayýcýsý sürüme göre deðeri 'result.result', 'result.value' ya da
        /// 'result.message' altýnda döndürebiliyor, ve zarfýn içinde baþka metinler de
        /// oluyor. Hangisinin gerçek dönüþ deðeri olduðunu anlamanýn güvenilir yolu,
        /// üretilen kodun cevabýna tanýnabilir bir önek koymak.
        ///
        /// Eski sürüm bunun yerine "içinde ayraç karakteri olan ilk alan" diyordu.
        /// Bu, VERÝ VARSA çalýþýyordu - ama boþ bir sahnede dönen deðer de boþ oluyor,
        /// ayraç bulunmuyor ve tamamen normal bir durum "okunamadý" sayýlýyordu.
        /// Önek boþ cevapta da bulunduðu için o ayrým artýk doðru yapýlýyor.
        /// ====================================
        /// </summary>
        private static string ExtractCodeReturnValue(string rawResult, string marker)
        {
            if (string.IsNullOrWhiteSpace(rawResult) || string.IsNullOrEmpty(marker))
                return null;

            try
            {
                var parsed = JObject.Parse(rawResult);

                if (parsed["result"] is JObject r)
                {
                    foreach (string field in new[] { "result", "value", "returnValue", "output", "message" })
                    {
                        string candidate = r[field]?.ToString();

                        if (string.IsNullOrEmpty(candidate))
                            continue;

                        int at = candidate.IndexOf(marker, StringComparison.Ordinal);

                        if (at >= 0)
                            return candidate.Substring(at + marker.Length);
                    }
                }
            }
            catch
            {
                // Zarf ayrýþtýrýlamadý; aþaðýdaki düz metin yolu deneniyor.
            }

            // Bazý sürümler sonucu hiç sarmalamadan düz metin olarak döndürüyor.
            int direct = rawResult.IndexOf(marker, StringComparison.Ordinal);

            return direct >= 0 ? rawResult.Substring(direct + marker.Length) : null;
        }

        /// <summary>
        /// Button.targetGraphic alanýný, ayný objedeki Image'a baðlar.
        ///
        /// NEDEN GEREKLÝ: targetGraphic atanmamýþsa Unity'nin hover/pressed/disabled
        /// renk geçiþleri HÝÇ çalýþmaz. Buton týklanýr, olay tetiklenir, ama kullanýcý
        /// hiçbir görsel geri bildirim almaz.
        ///
        /// Component MCP üzerinden eklendiðinde Unity bu alaný otomatik doldurmuyor;
        /// Editor'de elle eklendiðinde dolduruyor. Fark burada kapatýlýyor.
        /// </summary>
        private static async Task WireButtonGraphicAsync(string buttonName)
        {
            string safeName = EscapeForCode(buttonName);

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeName}\");\n" +
                "if (go == null) return \"not found\";\n" +
                "var btn = go.GetComponent<UnityEngine.UI.Button>();\n" +
                "var img = go.GetComponent<UnityEngine.UI.Image>();\n" +
                "if (btn == null || img == null) return \"missing component\";\n" +
                "btn.targetGraphic = img;\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"ok\";";

            await RunEditorCodeAsync(code, $"Button targetGraphic wiring for '{buttonName}'");
        }

        /// <summary>
        /// Selectable.colors (ColorBlock) deðerlerini ayarlar - hover ve pressed geri
        /// bildirimi.
        ///
        /// Önceki sürüm bunu denemiyordu; gerekçesi "SerializedProperty 'normalColor'
        /// not found on component 'Button'" idi. Doðru bir gözlem - o alanlar Button'un
        /// doðrudan property'si deðil, 'colors' adlý bir ColorBlock STRUCT'ýnýn içinde
        /// ve manage_components iç içe struct yazamýyor. Ama execute_code yazabiliyor.
        ///
        /// ColorBlock ÇARPAN tabanlý çalýþýyor: taban rengi Image'dan geliyor, ColorBlock
        /// onu çarpýyor. Bu yüzden her butonun kendi paletiyle tutarlý kalýyor.
        /// </summary>
        private static async Task ApplySelectableColorsAsync(string targetName)
        {
            string safeName = EscapeForCode(targetName);

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeName}\");\n" +
                "if (go == null) return \"not found\";\n" +
                "var sel = go.GetComponent<UnityEngine.UI.Selectable>();\n" +
                "if (sel == null) return \"no Selectable\";\n" +
                "var cb = sel.colors;\n" +
                "cb.normalColor = Color.white;\n" +
                "cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);\n" +
                "cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);\n" +
                "cb.selectedColor = new Color(1.06f, 1.06f, 1.06f, 1f);\n" +
                "cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);\n" +
                "cb.fadeDuration = 0.1f;\n" +
                "cb.colorMultiplier = 1f;\n" +
                "sel.colors = cb;\n" +
                "sel.transition = UnityEngine.UI.Selectable.Transition.ColorTint;\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"colors set\";";

            await RunEditorCodeAsync(code, $"Hover/pressed colors for '{targetName}'");
        }

        /// <summary>
        /// Toggle.targetGraphic'i track'e, Toggle.graphic'i ON DOLGUSUNA baðlar.
        ///
        /// Toggle.graphic "iþaretliyken görünen, iþaretsizken gizlenen" grafiktir;
        /// knob'u oraya baðlarsak toggle kapalýyken knob kaybolur. Ama hiç baðlamayýnca
        /// da toggle'ýn AÇIK mý KAPALI mý olduðu ekranda belli olmuyordu.
        ///
        /// Çözüm: knob'a deðil, track'in üstündeki ayrý bir ACCENT DOLGUSUNA baðlamak.
        /// ON'da dolgu beliriyor, OFF'ta kayboluyor. Knob her iki durumda da görünür
        /// kalýyor. Script gerektirmiyor, Unity'nin kendi mekanizmasýyla çalýþýyor.
        /// </summary>
        private static async Task WireToggleGraphicAsync(string toggleName, string fillName, bool isOn)
        {
            string safeName = EscapeForCode(toggleName);
            string safeFill = EscapeForCode(fillName);

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeName}\");\n" +
                "if (go == null) return \"not found\";\n" +
                "var tog = go.GetComponent<UnityEngine.UI.Toggle>();\n" +
                "var img = go.GetComponent<UnityEngine.UI.Image>();\n" +
                "if (tog == null || img == null) return \"missing component\";\n" +
                "tog.targetGraphic = img;\n" +
                $"var fill = __find(\"{safeFill}\");\n" +
                "if (fill != null) {\n" +

                // ============ DÜZELTÝLDÝ - KAPALI BAÞLAYAN TOGGLE HÝÇ AÇILAMIYORDU ============
                // Eskiden kapalý baþlayan toggle'ýn dolgusu SetActive(false) ile
                // gizleniyordu. Ama Unity'nin Toggle'ý açýlýp kapanýrken dolgunun yalnýzca
                // ÞEFFAFLIÐINI deðiþtirir (graphic.CrossFadeAlpha) - pasif bir objeyi asla
                // yeniden aktif etmez. Sonuç: kapalý baþlayan her toggle týklanýyor, deðeri
                // deðiþiyor, ama ekranda bir daha HÝÇ açýk görünmüyordu. Açýk baþlayanlar bu
                // hataya denk gelmediði için fark edilmesi zordu.
                //
                // Doðrusu: dolgu HER ZAMAN aktif kalýr, görünürlüðü Toggle'ýn kendi
                // mekanizmasý yönetir. Baþlangýç þeffaflýðý burada ayarlanýyor; sahne
                // yeniden açýldýðýnda Toggle.OnEnable (edit modda da çalýþýr) onu isOn'a
                // göre kendisi yeniden kuruyor.
                // =============================================================================
                "  fill.SetActive(true);\n" +
                "  var fillImg = fill.GetComponent<UnityEngine.UI.Image>();\n" +
                "  tog.graphic = fillImg;\n" +
                $"  if (fillImg != null) fillImg.canvasRenderer.SetAlpha({(isOn ? "1f" : "0f")});\n" +
                "}\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"graphic=\" + (tog.graphic != null);";

            await RunEditorCodeAsync(code, $"Toggle graphic wiring for '{toggleName}'");
        }

        /// <summary>
        /// Slider'ýn fillRect ve handleRect alanlarýný alt objelere baðlar.
        ///
        /// Bu, sliderin ÇALIÞMASI için zorunlu. manage_components sahne objesi
        /// referansý atayamadýðý için execute_code ile yapýlýyor.
        ///
        /// DEÐER YENÝDEN UYGULANIYOR: fillRect atandýktan sonra Unity dolgu geniþliðini
        /// kendiliðinden hesaplamýyor. Slider Editor'de BOÞ görünüyor, ancak kullanýcý
        /// sürükleyince düzeliyordu.
        ///
        /// Progress bar modunda handle GÝZLENÝYOR: sürüklenemeyen bir çubukta duran bir
        /// tutamak kullanýcýya "beni sürükle" diyor ama sürüklenmiyor - yanýltýcý.
        /// </summary>
        private static async Task WireSliderAsync(
            string sliderName, string fillName, string handleName, float value, bool interactive)
        {
            string safeSlider = EscapeForCode(sliderName);
            string safeFill = EscapeForCode(fillName);
            string safeHandle = EscapeForCode(handleName);

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeSlider}\");\n" +
                "if (go == null) return \"slider not found\";\n" +
                "var sl = go.GetComponent<UnityEngine.UI.Slider>();\n" +
                "if (sl == null) return \"no Slider component\";\n" +
                $"var fill = __find(\"{safeFill}\");\n" +
                $"var handle = __find(\"{safeHandle}\");\n" +
                "if (fill != null) sl.fillRect = fill.GetComponent<RectTransform>();\n" +
                "if (handle != null) {\n" +
                "  sl.handleRect = handle.GetComponent<RectTransform>();\n" +
                "  sl.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();\n" +
                $"  handle.SetActive({(interactive ? "true" : "false")});\n" +
                "}\n" +
                "sl.direction = UnityEngine.UI.Slider.Direction.LeftToRight;\n" +
                $"sl.interactable = {(interactive ? "true" : "false")};\n" +

                // Deðeri yeniden uygula: fillRect baðlandýktan sonra Unity dolguyu
                // kendiliðinden çizmiyor. Önce sýnýra it, sonra gerçek deðeri ver -
                // ayný deðeri tekrar atamak hiçbir güncelleme tetiklemiyor.
                "sl.value = sl.minValue;\n" +
                $"sl.value = {F(value)};\n" +

                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"fill=\" + (sl.fillRect != null) + \" handle=\" + (sl.handleRect != null) + \" value=\" + sl.value;";

            await RunEditorCodeAsync(code, $"Slider fill/handle wiring for '{sliderName}'");
        }

        /// <summary>
        /// TMP_InputField'ý ekler ve alt obje referanslarýný baðlar.
        ///
        /// Component EKLEME iþi de burada, çünkü TMP_InputField eklenir eklenmez
        /// textComponent'ini arýyor; boþsa Console'a uyarý basýyor ve alan saðýr
        /// kalýyor. Ekleme ve baðlamayý tek atomik adýmda yapmak bunu önlüyor.
        ///
        /// Tam nitelikli TMPro adlarý zorunlu: execute_code sarmalayýcýsý TMPro'yu
        /// import etmiyor.
        /// </summary>
        private static async Task WireInputFieldAsync(
            string fieldName, string textAreaName, string placeholderName, string textName,
            string initialText, bool isPassword)
        {
            string safeField = EscapeForCode(fieldName);
            string safeArea = EscapeForCode(textAreaName);
            string safePlaceholder = EscapeForCode(placeholderName);
            string safeText = EscapeForCode(textName);
            string safeInitial = EscapeForCode(initialText);

            string contentType = isPassword
                ? "TMPro.TMP_InputField.ContentType.Password"
                : "TMPro.TMP_InputField.ContentType.Standard";

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeField}\");\n" +
                "if (go == null) return \"field not found\";\n" +
                $"var area = __find(\"{safeArea}\");\n" +
                $"var ph = __find(\"{safePlaceholder}\");\n" +
                $"var txt = __find(\"{safeText}\");\n" +
                "if (area == null || ph == null || txt == null) return \"child objects missing\";\n" +
                "var field = go.GetComponent<TMPro.TMP_InputField>();\n" +
                "if (field == null) field = go.AddComponent<TMPro.TMP_InputField>();\n" +
                "var tmpText = txt.GetComponent<TMPro.TextMeshProUGUI>();\n" +

                // Tek satýrlýk alanda kelime kaydýrma metni ikinci satýra atýyor ve
                // imleç kayboluyor. TMP varsayýlaný kaydýrma açýk geliyor.
                "if (tmpText != null) {\n" +
                "  tmpText.enableWordWrapping = false;\n" +
                "  tmpText.overflowMode = TMPro.TextOverflowModes.Overflow;\n" +
                "}\n" +

                "field.textViewport = area.GetComponent<RectTransform>();\n" +
                "field.textComponent = tmpText;\n" +
                "field.placeholder = ph.GetComponent<TMPro.TextMeshProUGUI>();\n" +
                "field.targetGraphic = go.GetComponent<UnityEngine.UI.Image>();\n" +
                "field.lineType = TMPro.TMP_InputField.LineType.SingleLine;\n" +
                $"field.contentType = {contentType};\n" +
                $"field.text = \"{safeInitial}\";\n" +
                "field.ForceLabelUpdate();\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"textComponent=\" + (field.textComponent != null) + \" placeholder=\" + (field.placeholder != null);";

            await RunEditorCodeAsync(code, $"InputField wiring for '{fieldName}'");
        }


        /// <summary>
        /// Bir elemanýn altýndaki TÜM TextMeshProUGUI bileþenlerinin taþma davranýþýný
        /// ayarlar.
        ///
        /// ============ NEDEN GEREKLÝ - SESSÝZ BÝR KALÝTE KAYBI ============
        /// TMP varsayýlaný KELÝME KAYDIRMA AÇIK ve TAÞMA = Overflow. Bir satýr
        /// etiketinde bu üç farklý þekilde bozuluyor:
        ///
        ///   1. "Otomatik Kaydetme" gibi iki kelimelik bir etiket, dar bir alanda
        ///      ikinci satýra atlýyor - ama satýrýn yüksekliði tek satýra göre
        ///      ayarlandýðý için ikinci satýr KIRPILIYOR ve yazý yarým görünüyor.
        ///   2. Kaydýrma olmayan uzun tek kelimeler kutunun dýþýna taþýp komþu
        ///      elemanýn üstüne biniyor.
        ///   3. Kýrpýlan metin kullanýcýya "bulanýk" ya da "bozuk" görünüyor, oysa
        ///      sorun çözünürlük deðil taþma.
        ///
        /// CreateInputField bunu zaten yapýyordu (yoksa imleç kayboluyordu) ama baþka
        /// hiçbir yerde yapýlmýyordu.
        ///
        /// ÝKÝ DAVRANIÞ:
        ///   allowWrap = false -> tek satýr, sýðmayan kýsým "..." ile kesilir.
        ///       Etiketler, buton yazýlarý, deðer metinleri, kart baþlýklarý. Bunlar
        ///       tek satýrlýk alanlarda duruyor; ikinci satýra atlamak her zaman
        ///       kýrpýlma demek. Ellipsis en azýndan okunabilir ve kasýtlý görünüyor.
        ///   allowWrap = true -> çok satýr serbest.
        ///       Açýklama satýrlarý ve yer tutucu baþlýklarý; onlarýn alaný zaten
        ///       birden fazla satýrý kaldýracak yükseklikte.
        ///
        /// TEK ÇAÐRIDA TÜM ALT METÝNLER: her metin için ayrý bir tur atmak pahalý
        /// olurdu. Bu metot kökten aþaðý tarayýp hepsini birden ayarlýyor.
        ///
        /// TOLERE EDÝLEBÝLÝR: baþarýsýz olursa eleman yine ayakta kalýr, yalnýzca
        /// taþma davranýþý TMP varsayýlanýnda kalýr.
        /// </summary>
        /// <summary>
        /// Bu puntodan büyük metinlerde KESME yerine TAÞMA kullanýlýyor.
        ///
        /// ============ NEDEN - GÖRÜNMEZ OLAN BÜYÜK DEÐERLER ============
        /// GERÇEK TEST: bir dashboard'da 40 puntoluk "1 284", "92 %", "14 min"
        /// deðerleri ekranda HÝÇ görünmedi. Sebep Ellipsis modu: TMP metni kutuya
        /// sýðdýramazsa keser, kutu bir SATIR YÜKSEKLÝÐÝNDEN kýsaysa hiçbir þey
        /// çizmez. 15 puntoluk yazýlar 52 piksellik satýrlara rahat sýðdýðý için bu
        /// hiç ortaya çýkmamýþtý; 40 punto sýkýþmýþ bir satýrda tamamen kayboldu.
        ///
        /// Büyük bir okuma deðeri için doðru takas farklý: biraz taþmasý, hiç
        /// görünmemesinden iyidir.
        /// ==============================================================
        /// </summary>
        private const int LargeTextThreshold = 24;

        private static async Task ApplyTextPolishAsync(string rootName, bool allowWrap)
        {
            string safeRoot = EscapeForCode(rootName);

            string wrap = allowWrap ? "true" : "false";
            string overflow = allowWrap
                ? "TMPro.TextOverflowModes.Overflow"
                : "TMPro.TextOverflowModes.Ellipsis";

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeRoot}\");\n" +
                "if (go == null) return \"not found\";\n" +
                "int n = 0;\n" +
                "foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {\n" +
                $"  t.enableWordWrapping = {wrap};\n" +
                $"  t.overflowMode = {overflow};\n" +

                // Otomatik boyutlandýrma KAPALI tutuluyor: açýk olduðunda TMP yazýyý
                // alana sýðdýrmak için punto deðiþtiriyor ve kullanýcýnýn verdiði
                // tipografi ölçeði bozuluyor. Kullanýcý "satýr etiketleri 15 punto"
                // dediyse 15 punto olmalý, TMP'nin uygun gördüðü bir þey deðil.
                "  t.enableAutoSizing = false;\n" +

                "  n++;\n" +
                "}\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"polished \" + n;";

            await RunEditorCodeAsync(code, $"Text overflow settings for '{rootName}'");
        }


        // =====================================================
        // KART SIKIÞTIRMA - GÖREV SONU
        // =====================================================

        /// <summary>Son satýrýn altýnda býrakýlan boþluk (piksel).</summary>
        private const float CompactBottomPadPx = 16f;

        /// <summary>Bundan az kazanç varsa kart zaten sýký sayýlýr, dokunulmaz.</summary>
        private const float CompactMinGainPx = 24f;

        /// <summary>
        /// Her kartý içeriðine küçültür: kart son satýrýnýn CompactBottomPadPx altýnda
        /// biter, fazlasý kesilir.
        ///
        /// ============ NEDEN GEREKLÝ ============
        /// Model bir kartýn yüksekliðini satýrlarýný kurmadan ÖNCE seçmek zorunda -
        /// kaç satýr geleceðini henüz bilmiyor. Tek kartlý bir sekme panelinde genellikle
        /// büyük seçiyor. Gerçek test: dört satýrlýk kart panelin %90'ýný kapladý ve
        /// kartýn alt yarýsý boþ bir çerçeve olarak kaldý.
        ///
        /// Bunu model yerine KOD çözüyor, çünkü doðru cevap ancak iþ bittiðinde belli:
        /// son satýr nerede bitiyorsa kart da orada bitmeli.
        ///
        /// ============ NEDEN DÜZ KÜÇÜLTMEK YETMÝYOR ============
        /// Anchor'lar ORANLI. Kartýn yüksekliði yarýya inerse içindeki baþlýk, içerik
        /// alaný ve her satýr da yarýya iner - yazýlar ezilir. Bu yüzden kartla birlikte
        /// baþlýðýn, içerik alanýnýn ve satýrlarýn anchor'larý da yeniden hesaplanýyor,
        /// öyle ki hepsi PÝKSEL olarak ayný boyda ve kartýn tepesine göre ayný yerde
        /// kalsýn. Yalnýzca alttaki boþluk kesiliyor.
        ///
        /// ============ GÜVENLÝK KURALLARI ============
        ///   - Kartýn altýnda ayný sütunda baþka bir eleman varsa kart küçültülmüyor:
        ///     aradaki boþluk açýlýr ve üst üste dizilmiþ kartlarýn düzeni bozulurdu.
        ///   - Ýçerik alaný boþsa dokunulmuyor - boþ kart bir hatadýr, gizlenmemeli.
        ///   - Kartýn içerik dýþýndaki bir çocuðu (ör. alta sabitlenmiþ bir buton)
        ///     kesilecek bölgeye düþüyorsa kart küçültülmüyor.
        ///   - Kazanç CompactMinGainPx'ten azsa zaten sýkýdýr, dokunulmuyor. Bu yüzden
        ///     metot tekrar tekrar çaðrýlsa da güvenli: ikinci çaðrý hiçbir þey yapmaz.
        /// ======================================================
        /// </summary>
        /// <returns>Küçültülen kart sayýsý.</returns>
        public static async Task<int> CompactCardsAsync()
        {
            // Önce kart adlarýný topla, SONRA deðiþtir: kayýt üzerinde gezinirken onu
            // deðiþtirmek koleksiyon hatasý verir.
            var cards = new List<string>();

            foreach (var entry in LayoutRegistry)
            {
                string contentName = entry.Key + "Content";

                foreach (var child in entry.Value)
                {
                    if (string.Equals(child.Key, contentName, StringComparison.OrdinalIgnoreCase))
                    {
                        cards.Add(entry.Key);
                        break;
                    }
                }
            }

            int compacted = 0;

            foreach (string card in cards)
            {
                try
                {
                    if (await CompactCardAsync(card))
                        compacted++;
                }
                catch (Exception ex)
                {
                    // Tek bir kartýn sorunu diðerlerini durdurmamalý; sýkýþtýrma bir
                    // rötuþ, baþarýsýzlýðý ekraný bozmaz.
                    Debug.LogWarning($"[UiMacroExpander] Could not compact card '{card}': {ex.Message}");
                }
            }

            if (compacted > 0)
            {
                Debug.Log($"[UiMacroExpander] Compacted {compacted} card(s) to fit their content. " +
                          "Titles and rows kept their exact pixel size; only the empty space below the last row was removed.");
            }

            return compacted;
        }

        /// <summary>Bir elemanýn ebeveynini ve ebeveyn içindeki dikdörtgenini bulur.</summary>
        private static bool TryFindLayout(string name, out string parent, out LayoutRect rect)
        {
            parent = null;
            rect = default;

            foreach (var entry in LayoutRegistry)
            {
                foreach (var child in entry.Value)
                {
                    if (string.Equals(child.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        parent = entry.Key;
                        rect = child.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static async Task<bool> CompactCardAsync(string card)
        {
            if (!TryFindLayout(card, out string grand, out LayoutRect cardRect))
                return false;

            string contentName = card + "Content";

            if (!LayoutRegistry.TryGetValue(card, out var cardChildren))
                return false;

            LayoutRect contentRect = default;
            bool hasContent = false;

            foreach (var child in cardChildren)
            {
                if (string.Equals(child.Key, contentName, StringComparison.OrdinalIgnoreCase))
                {
                    contentRect = child.Value;
                    hasContent = true;
                    break;
                }
            }

            if (!hasContent)
                return false;

            if (!LayoutRegistry.TryGetValue(contentName, out var rows) || rows.Count == 0)
                return false;

            // ---------- ALTINDA KARDEÞ VAR MI ----------
            if (LayoutRegistry.TryGetValue(grand, out var siblings))
            {
                foreach (var sibling in siblings)
                {
                    if (string.Equals(sibling.Key, card, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Yan sütunlar (sidebar, kapak paneli) bu kartýn altýnda sayýlmaz.
                    if (IsColumn(sibling.Value))
                        continue;

                    bool sharesColumn = sibling.Value.MaxX > cardRect.MinX + 0.001f &&
                                        sibling.Value.MinX < cardRect.MaxX - 0.001f;

                    if (sharesColumn && sibling.Value.MaxY <= cardRect.MinY + 0.001f)
                        return false;
                }
            }

            // ---------- PÝKSEL ÖLÇÜLERÝ ----------
            float grandPx = AbsoluteHeight(grand) * ReferenceScreenHeight;
            float cardPx = cardRect.Height * grandPx;
            float contentPx = contentRect.Height * cardPx;

            if (cardPx < 1f || contentPx < 1f)
                return false;

            float lowest = 1f;

            foreach (var row in rows)
                lowest = Mathf.Min(lowest, row.Value.MinY);

            float gainPx = lowest * contentPx - CompactBottomPadPx;

            if (gainPx < CompactMinGainPx)
                return false;

            float newCardPx = cardPx - gainPx;
            float newContentPx = contentPx - gainPx;

            // Eski oraný yeni boya taþýmak için çarpanlar: pikselde sabit kalan bir
            // mesafe, küçülen bir ebeveynde daha büyük bir ORANA karþýlýk gelir.
            float kCard = cardPx / newCardPx;
            float kContent = contentPx / newContentPx;

            var cardUpdates = new List<KeyValuePair<string, LayoutRect>>();
            var rowUpdates = new List<KeyValuePair<string, LayoutRect>>();

            LayoutRect newCard = cardRect;

            // ORTALANMIÞ KART (diyalog, login) ORTADA KALMALI: tepesi sabit tutulup
            // alttan kýrpýlýrsa ekranýn üst yarýsýna kayar. Bu kartlar iki uçtan eþit
            // küçültülüyor. Ýçindeki baþlýk ve satýrlarýn hesabý deðiþmiyor, çünkü o
            // hesap kartýn konumuna deðil yalnýzca piksel boyuna baðlý.
            bool centeredCard =
                Mathf.Abs((cardRect.MinY + cardRect.MaxY) * 0.5f - 0.5f) < 0.03f &&
                Mathf.Abs((cardRect.MinX + cardRect.MaxX) * 0.5f - 0.5f) < 0.03f &&
                cardRect.MinY > 0.04f && cardRect.MaxY < 0.96f &&
                cardRect.Width < 0.8f;

            if (centeredCard)
            {
                float center = (cardRect.MinY + cardRect.MaxY) * 0.5f;
                float half = newCardPx / grandPx * 0.5f;
                newCard.MinY = center - half;
                newCard.MaxY = center + half;
            }
            else
            {
                newCard.MinY = cardRect.MaxY - newCardPx / grandPx;
            }

            foreach (var child in cardChildren)
            {
                LayoutRect r = child.Value;
                LayoutRect n = r;

                if (string.Equals(child.Key, contentName, StringComparison.OrdinalIgnoreCase))
                {
                    // Ýçerik alaný: tepesi ve alt kenar payý pikselde sabit, boyu
                    // kazanç kadar kýsalýyor.
                    n.MaxY = 1f - (1f - r.MaxY) * kCard;
                    n.MinY = r.MinY * kCard;
                }
                else
                {
                    // Baþlýk ve diðerleri: tepeye göre konumu ve boyu pikselde sabit.
                    n.MaxY = 1f - (1f - r.MaxY) * kCard;
                    n.MinY = n.MaxY - r.Height * kCard;

                    // Kesilecek bölgeye düþüyorsa (alta sabitlenmiþ bir eleman) bu kart
                    // güvenle küçültülemez.
                    if (n.MinY < -0.001f)
                        return false;
                }

                cardUpdates.Add(new KeyValuePair<string, LayoutRect>(child.Key, n));
            }

            foreach (var row in rows)
            {
                LayoutRect r = row.Value;
                LayoutRect n = r;

                n.MaxY = 1f - (1f - r.MaxY) * kContent;
                n.MinY = n.MaxY - r.Height * kContent;

                if (n.MinY < -0.001f)
                    return false;

                rowUpdates.Add(new KeyValuePair<string, LayoutRect>(row.Key, n));
            }

            // ---------- TEK execute_code ÝLE UYGULA ----------
            var code = new StringBuilder(FindHelperCode);
            code.AppendLine("int __n = 0;");

            AppendAnchorAssignment(code, card, newCard);

            foreach (var u in cardUpdates)
                AppendAnchorAssignment(code, u.Key, u.Value);

            foreach (var u in rowUpdates)
                AppendAnchorAssignment(code, u.Key, u.Value);

            code.AppendLine("UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());");
            code.AppendLine("return \"compacted \" + __n;");

            if (!await RunEditorCodeAsync(code.ToString(), $"Compacting card '{card}'"))
                return false;

            // ---------- KAYDI GÜNCELLE ----------
            // Yalnýzca sahne gerçekten deðiþtiyse: kayýt sahnenin aynasý olmalý.
            RegisterLayout(grand, card, newCard);

            foreach (var u in cardUpdates)
                RegisterLayout(card, u.Key, u.Value);

            foreach (var u in rowUpdates)
                RegisterLayout(contentName, u.Key, u.Value);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[UiMacroExpander] Card '{0}' compacted from {1:0} px to {2:0} px ({3} row(s)).",
                card, cardPx, newCardPx, rows.Count));

            return true;
        }

        /// <summary>Bir objenin anchor'larýný atayan tek satýrlýk C# kodu ekler.</summary>
        private static void AppendAnchorAssignment(StringBuilder code, string name, LayoutRect r)
        {
            string safe = EscapeForCode(name);

            code.Append("{ var go = __find(\"").Append(safe).Append("\"); if (go != null) { ")
                .Append("var rt = go.GetComponent<RectTransform>(); ")
                .Append("if (rt != null) { ")
                .Append("rt.anchorMin = new Vector2(").Append(F(r.MinX)).Append(", ").Append(F(Mathf.Clamp01(r.MinY))).Append("); ")
                .Append("rt.anchorMax = new Vector2(").Append(F(r.MaxX)).Append(", ").Append(F(Mathf.Clamp01(r.MaxY))).Append("); ")
                .Append("rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; ")
                .Append("UnityEditor.EditorUtility.SetDirty(go); __n++; } } }")
                .AppendLine();
        }

        /// <summary>
        /// Bir GameObject'in aktiflik durumunu ayarlar.
        ///
        /// Gösterge çubuklarý ve üst üste binen sekme panelleri için gerekli: hepsi
        /// oluþturuluyor ama yalnýzca biri görünür kalýyor.
        /// </summary>
        private static async Task SetActiveAsync(string objectName, bool active)
        {
            string safeName = EscapeForCode(objectName);
            string activeLiteral = active ? "true" : "false";

            string code =
                FindHelperCode +
                $"var go = __find(\"{safeName}\");\n" +
                "if (go == null) return \"not found\";\n" +
                $"go.SetActive({activeLiteral});\n" +
                "UnityEditor.EditorUtility.SetDirty(go);\n" +
                "return \"ok\";";

            await RunEditorCodeAsync(code, $"SetActive({activeLiteral}) on '{objectName}'");
        }

        /// <summary>
        /// Kýsmi olarak oluþturulmuþ objeleri siler.
        ///
        /// RunSequential 7 adýmýn 3'ünde patlarsa, ilk 2 adýmýn oluþturduðu
        /// GameObject'ler sahnede KALIYORDU. Ama 'known' setine EKLENMÝYORDU, çünkü
        /// ekleme yalnýzca tam baþarýda yapýlýyor. Sonuç zinciri:
        ///   1. Yarým eleman sahnede duruyor (görsel çöp)
        ///   2. Duplicate korumasý onu bilmiyor
        ///   3. Model ayný çaðrýyý tekrar deniyor
        ///   4. Unity AYNI ADDA ÝKÝNCÝ bir obje yaratýyor
        ///   5. GameObject.Find bundan sonra hangisini bulacaðýný bilmiyor
        ///
        /// DÝKKAT - GEÇÝLEN LÝSTE GERÇEKTEN OLUÞTURULANLAR OLMALI, planlanan adlar
        /// deðil. Planlanan listeyi geçmek, 1. adýmda patlayan bir macro'da BAÞKA
        /// elemanlara ait objeleri silmeye yol açar.
        /// </summary>
        private static async Task RollbackPartialAsync(List<string> actuallyCreated, string parent)
        {
            if (actuallyCreated == null || actuallyCreated.Count == 0)
                return;

            var sb = new StringBuilder(FindHelperCode);
            sb.AppendLine("int __removed = 0;");

            // Ters sýrada siliyoruz: çocuklar ebeveynden önce. Ebeveyn önce silinirse
            // Unity çocuklarý da siler ve sonraki __find çaðrýlarý boþa gider.
            for (int i = actuallyCreated.Count - 1; i >= 0; i--)
            {
                string safe = EscapeForCode(actuallyCreated[i]);
                sb.AppendLine($"{{ var go = __find(\"{safe}\"); if (go != null) {{ UnityEngine.Object.DestroyImmediate(go); __removed++; }} }}");
            }

            sb.AppendLine("return \"removed \" + __removed;");

            bool ok = await RunEditorCodeAsync(sb.ToString(), $"Rollback of {actuallyCreated.Count} partially created object(s)");

            if (ok)
            {
                Debug.LogWarning(
                    $"[UiMacroExpander] A macro failed halfway through; the {actuallyCreated.Count} object(s) it had already created " +
                    "were removed so the scene stays clean and the model can retry safely.");
            }
            else
            {
                Debug.LogWarning(
                    "[UiMacroExpander] A macro failed halfway through and the partial objects could NOT be removed: " +
                    string.Join(", ", actuallyCreated) + ". Delete them by hand if they appear in the Hierarchy.");
            }

            // Kayýttan da düþ - aksi hâlde baþarýsýz bir çaðrýnýn dikdörtgeni yerinde
            // kalýp sonraki meþru çaðrýlarý engelliyor.
            foreach (string n in actuallyCreated)
                UnregisterLayout(parent, n);
        }

        // =====================================================
        // SPRITE APPLICATION
        // =====================================================

        /// <summary>
        /// Sprite klasörü bir kez denetlenir. Eksikse kullanýcý TEK bir net uyarý görür.
        ///
        /// Önceki sürümde her eleman için ayrý ayrý sessiz bir Log yazýlýyordu: 30
        /// elemanlý bir ekranda Console 30 satýr doluyor, kullanýcý da sebebi
        /// anlamýyordu.
        ///
        /// ResetSession() bu önbelleði sýfýrlýyor: kullanýcý sprite'larý görev
        /// baþladýktan sonra üretirse, eski kodda bu oturum boyunca "yok" deniyordu.
        /// </summary>
        private static bool? _spritesAvailable;

        private static bool SpritesAvailable()
        {
            if (_spritesAvailable.HasValue)
                return _spritesAvailable.Value;

            bool allPresent =
                System.IO.File.Exists(SpriteRoundedSmall) &&
                System.IO.File.Exists(SpriteRoundedLarge) &&
                System.IO.File.Exists(SpritePill) &&
                System.IO.File.Exists(SpriteCircle);

            _spritesAvailable = allPresent;

            if (!allPresent)
            {
                Debug.LogWarning(
                    "[UiMacroExpander] Rounded corner sprites are missing from " + SpriteFolder + ". " +
                    "Every element will be built correctly but with SHARP corners. " +
                    "Run Tools > UI > Generate Rounded Sprites once, then rebuild the screen.");
            }

            return allPresent;
        }

        /// <summary>
        /// Bir Image'a yuvarlatýlmýþ sprite atar.
        ///
        /// RunSequential'ýn DIÞINDA ve TOLERE EDÝLEBÝLÝR olmasý bilinçli: sprite dosyasý
        /// yoksa bu atama baþarýsýz olur ama elemanýn kendisi zaten oluþmuþ durumda.
        /// Keskin köþeli bir panel, hiç olmayan bir panelden iyidir.
        ///
        /// 'type':'Sliced' kritik: sprite 9-slice olarak esner, köþe yarýçapý sabit
        /// kalýr. Bu olmadan sprite tamamen esner ve yuvarlaklýk orantýsýz görünür.
        /// </summary>
        private static async Task ApplySpriteAsync(string targetName, string spritePath)
        {
            // Dosya yoksa MCP çaðrýsýný hiç yapmýyoruz - her eleman için boþa giden bir
            // tur ve bir hata satýrý demek.
            if (!SpritesAvailable())
                return;

            JObject call = Cmd("manage_components", new JObject
            {
                ["action"] = "set_property",
                ["target"] = targetName,
                ["componentType"] = "Image",
                ["properties"] = new JObject
                {
                    ["sprite"] = spritePath,
                    ["type"] = "Sliced"
                }
            });

            string result = await MCPExecutor.Execute(call);

            if (!WasStepSuccessful(result))
            {
                Debug.Log($"[UiMacroExpander] Could not apply sprite '{spritePath}' to '{targetName}' - the element keeps sharp corners.");
            }
        }

        /// <summary>
        /// Modelin ürettiði ya da projede duran bir sprite'ý atar (ikon, logo, arka plan
        /// görseli).
        ///
        /// ÝKON HEDEFÝ ÝÇÝN KRÝTÝK: dosya yoksa NET bir uyarý basýlýyor. Sessizce
        /// geçmesi, modelin görseli koyduðunu sanmasýna yol açardý.
        /// </summary>
        private static async Task ApplyCustomSpriteAsync(string targetName, string spritePath, bool preserveAspect)
        {
            if (!System.IO.File.Exists(spritePath))
            {
                Debug.LogWarning(
                    $"[UiMacroExpander] Sprite '{spritePath}' does not exist, so '{targetName}' keeps its flat colour. " +
                    "Check the path, or leave 'sprite' out and use 'color' plus 'caption' for a placeholder.");
                return;
            }

            JObject call = Cmd("manage_components", new JObject
            {
                ["action"] = "set_property",
                ["target"] = targetName,
                ["componentType"] = "Image",
                ["properties"] = new JObject
                {
                    ["sprite"] = spritePath,
                    ["type"] = "Simple",
                    ["preserveAspect"] = preserveAspect
                }
            });

            string result = await MCPExecutor.Execute(call);

            if (!WasStepSuccessful(result))
            {
                Debug.Log($"[UiMacroExpander] Could not apply custom sprite '{spritePath}' to '{targetName}'.");
            }
        }

        /// <summary>
        /// Toggle'ýn parçalarýna sprite atar: track ve ON dolgusu hap, knob daire.
        /// </summary>
        private static async Task ApplyToggleSpritesAsync(string toggleName, string fillName, string knobName)
        {
            if (!SpritesAvailable())
                return;

            await ApplySpriteAsync(toggleName, SpritePill);

            if (!string.IsNullOrWhiteSpace(fillName))
                await ApplySpriteAsync(fillName, SpritePill);

            if (string.IsNullOrWhiteSpace(knobName))
                return;

            // Knob tam daire - 'Simple' kullanýyoruz çünkü daire 9-slice'a uygun deðil;
            // esnetilince ortasý bozulur.
            JObject call = Cmd("manage_components", new JObject
            {
                ["action"] = "set_property",
                ["target"] = knobName,
                ["componentType"] = "Image",
                ["properties"] = new JObject
                {
                    ["sprite"] = SpriteCircle,
                    ["type"] = "Simple"
                }
            });

            string result = await MCPExecutor.Execute(call);

            if (!WasStepSuccessful(result))
            {
                Debug.Log($"[UiMacroExpander] Could not apply circle sprite to '{knobName}' - the knob keeps square corners.");
            }
        }

        // =====================================================
        // SHARED BUILDERS
        // =====================================================

        /// <summary>
        /// Toggle adýmlarýný tek yerde üretiyor - CreateToggle ve CreateRow ikisi de
        /// bunu kullanýyor, böylece toggle davranýþý iki yerde ayrý ayrý bozulamaz.
        ///
        /// ÜÇ KATMAN:
        ///   1. Track   - nötr renkli arka plan, Toggle component'i burada
        ///   2. OnFill  - accent renkli dolgu, Toggle.graphic'e baðlanýyor:
        ///                ON'da görünür, OFF'ta gizli. DURUMU BU GÖSTERÝYOR.
        ///   3. Knob    - beyaz top, her iki durumda da görünür
        ///
        /// BÝLÝNEN SINIR: knob týklandýðýnda YER DEÐÝÞTÝRMEZ - bunun için bir davranýþ
        /// script'i gerekiyor (write_script ile üretilebilir). Ama durum artýk OnFill
        /// sayesinde net okunuyor.
        /// </summary>
        private static List<JObject> BuildToggleSteps(
            string toggleName, string parent, string knobName, string fillName, bool isOn,
            JObject trackColor, JObject onColor, JObject knobColor,
            JObject anchorMin, JObject anchorMax)
        {
            JObject knobMin = isOn ? new JObject { ["x"] = 0.52, ["y"] = 0.12 }
                                   : new JObject { ["x"] = 0.06, ["y"] = 0.12 };
            JObject knobMax = isOn ? new JObject { ["x"] = 0.94, ["y"] = 0.88 }
                                   : new JObject { ["x"] = 0.48, ["y"] = 0.88 };

            return new List<JObject>
            {
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = toggleName, ["parent"] = parent }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = toggleName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = trackColor, ["raycastTarget"] = true }
                }),
                // GERÇEK Toggle component'i - týklamayý algýlayan kýsým.
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = toggleName, ["componentType"] = "Toggle",
                    ["properties"] = new JObject { ["isOn"] = isOn }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = toggleName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(anchorMin, anchorMax)
                }),

                // ON DOLGUSU - durumu gösteren katman. Toggle.graphic'e baðlanacak.
                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = fillName, ["parent"] = toggleName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = fillName, ["componentType"] = "Image",
                    ["properties"] = new JObject { ["color"] = onColor, ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = fillName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(
                        new JObject { ["x"] = 0, ["y"] = 0 },
                        new JObject { ["x"] = 1, ["y"] = 1 })
                }),

                Cmd("manage_gameobject", new JObject { ["action"] = "create", ["name"] = knobName, ["parent"] = toggleName }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "add", ["target"] = knobName, ["componentType"] = "Image",

                    // Knob týklamayý yakalamamalý: yakalarsa toggle'ýn kendisi olayý
                    // almýyor ve switch'in tam ortasýna týklamak çalýþmýyor.
                    ["properties"] = new JObject { ["color"] = knobColor, ["raycastTarget"] = false }
                }),
                Cmd("manage_components", new JObject
                {
                    ["action"] = "set_property", ["target"] = knobName, ["componentType"] = "RectTransform",
                    ["properties"] = RectFillProperties(knobMin, knobMax)
                }),
            };
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private static JObject Cmd(string type, JObject parameters)
        {
            return new JObject { ["type"] = type, ["params"] = parameters };
        }

        private static JObject RectFillProperties(JObject anchorMin, JObject anchorMax)
        {
            return new JObject
            {
                ["anchorMin"] = anchorMin,
                ["anchorMax"] = anchorMax,
                ["offsetMin"] = new JObject { ["x"] = 0, ["y"] = 0 },
                ["offsetMax"] = new JObject { ["x"] = 0, ["y"] = 0 }
            };
        }

        private static JObject DefaultPanelColor() => new JObject { ["r"] = 0.05, ["g"] = 0.08, ["b"] = 0.12, ["a"] = 0.97 };
        private static JObject DefaultCardColor() => new JObject { ["r"] = 0.07, ["g"] = 0.11, ["b"] = 0.16, ["a"] = 1 };
        /// <summary>
        /// Varsayýlan accent: bu görevde model bir accent rengi verdiyse o, vermediyse
        /// mavi. Her çaðrý YENÝ bir kopya döndürüyor - ayný JObject iki ayrý komuta
        /// eklenirse Newtonsoft ikincisinde "already has a parent" hatasý veriyor.
        /// </summary>
        private static JObject DefaultAccentColor()
        {
            if (_sessionAccent != null)
                return (JObject)_sessionAccent.DeepClone();

            return new JObject { ["r"] = 0.36, ["g"] = 0.55, ["b"] = 1.0, ["a"] = 1 };
        }

        private static JObject DefaultButtonColor() => DefaultAccentColor();

        /// <summary>Seçili nav item'ýn arka planý: accent'in hafif, yarý saydam hali.</summary>
        private static JObject SelectedNavBackground()
        {
            JObject c = DefaultAccentColor();
            c["a"] = 0.22;
            return c;
        }
        private static JObject DefaultToggleOffColor() => new JObject { ["r"] = 0.20, ["g"] = 0.24, ["b"] = 0.29, ["a"] = 1 };
        private static JObject DefaultTextColor() => new JObject { ["r"] = 0.91, ["g"] = 0.94, ["b"] = 0.97, ["a"] = 1 };
        private static JObject DefaultSecondaryTextColor() => new JObject { ["r"] = 0.51, ["g"] = 0.58, ["b"] = 0.65, ["a"] = 1 };
        /// <summary>
        /// Soluk metin: açýklamalar, input ipuçlarý, yer tutucu yazýlarý.
        ///
        /// DÜZELTÝLDÝ - KENDÝ KONTRAST EÞÝÐÝNÝ GEÇEMÝYORDU: eski deðer (0.31, 0.38,
        /// 0.45) varsayýlan kart renginin üstünde 2.7:1, input kutusunun üstünde 1.7:1
        /// veriyordu. Kontrast denetimi her açýklamada uyarý basýyor, input ipuçlarý
        /// ("Username", "Password") ise neredeyse görünmüyordu. Yeni deðer kartta 4.8:1,
        /// input kutusunda 3.1:1 - hâlâ ana metinden (0.91) belirgin þekilde soluk.
        /// </summary>
        private static JObject DefaultMutedTextColor() => new JObject { ["r"] = 0.48, ["g"] = 0.54, ["b"] = 0.61, ["a"] = 1 };

        /// <summary>
        /// Yer tutucu alanlarýn varsayýlan rengi: arka plandan hafifçe ayrýlan ama
        /// dikkat çekmeyen bir yüzey. Koyu temada beyaz kullanmak göz alýyor.
        /// </summary>
        private static JObject DefaultPlaceholderColor() => new JObject { ["r"] = 0.11, ["g"] = 0.15, ["b"] = 0.20, ["a"] = 1 };

        /// <summary>
        /// Tamamen þeffaf. Týklamalarý yakalamamasý gereken konteynerlerde,
        /// raycastTarget=false ile birlikte kullanýlýyor.
        /// </summary>
        private static JObject TransparentColor() => new JObject { ["r"] = 0, ["g"] = 0, ["b"] = 0, ["a"] = 0 };

        /// <summary>
        /// Gözle görünmez ama TIKLANABÝLÝR arka plan.
        ///
        /// Unity, alfa 0 olan bir Image'ý raycast'te tamamen atlar - Raycast Target
        /// iþaretli olsa bile. Gerçek testte seçili olmayan nav item'lar bu yüzden hiç
        /// týklanamadý. Alfa 0.004 gözle ayýrt edilemez ama raycast çalýþýr.
        /// </summary>
        private static JObject ClickableTransparentColor() => new JObject { ["r"] = 0, ["g"] = 0, ["b"] = 0, ["a"] = 0.004 };

        private static JObject WhiteColor() => new JObject { ["r"] = 1, ["g"] = 1, ["b"] = 1, ["a"] = 1 };

        private static string RequireString(JObject p, string field, out string error)
        {
            error = null;
            string value = p[field]?.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"'{field}' is required for this macro.";
                return null;
            }

            return value.Trim();
        }

        /// <summary>
        /// Bir adým sonucu "bu obje zaten var" anlamýna mý geliyor.
        ///
        /// ============ TEKRAR DÖNGÜSÜNÜ KIRAN DENETÝM ============
        /// GERÇEK ARIZA: sahne komutlar arasýnda KALICI ama 'known' seti deðil. Ýkinci
        /// çalýþtýrmada manage_gameobject create adým 1'de "already exists" ile
        /// patlýyor, RollbackPartialAsync silecek bir þey bulamýyor (biz hiçbir þey
        /// oluþturmadýk), macro hata döndürüyor, model ayný çaðrýyý tekrar deniyor.
        /// Konsoldaki "Stuck re-creating elements that already exist (4 consecutive
        /// attempts)" tam olarak buydu ve görev yarýda kalýyordu.
        ///
        /// Doðru davranýþ: obje zaten oradaysa ÝSTENEN DURUM SAÐLANMIÞTIR. Onu
        /// SAHÝPLENÝP devam ediyoruz - sonraki adýmlar (component ekleme, RectTransform
        /// ayarlama) zaten 'target' ile çalýþýyor ve mevcut objeyi buluyor.
        /// ========================================================
        /// </summary>
        private static bool LooksLikeAlreadyExists(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult))
                return false;

            string text = rawResult.ToLowerInvariant();

            return text.Contains("already exist") ||
                   text.Contains("already has") ||
                   text.Contains("already had") ||
                   text.Contains("duplicate name") ||
                   text.Contains("name is already");
        }

        /// <summary>
        /// Macro adýmlarýný sýrayla çalýþtýrýr ve TEK SATIRLIK bir özet döndürür.
        ///
        /// BU METODUN BOYUT DAVRANIÞI KRÝTÝK. Önceki versiyonlarda her adýmýn tam ham
        /// JSON sonucu dönüyordu - 7 adýmlýk bir macro için ~1500-2500 token.
        /// OllamaToolAgent bunu geçmiþe yazýp sonraki HER adýmda modele tekrar
        /// gönderiyordu ve 20 macro sonra baðlam penceresi doluyordu.
        /// Artýk: baþarý = tek satýr (~30 token). Hata = ayrýntý korunuyor.
        ///
        /// ÜÇ DAVRANIÞ:
        ///   1. Adým baþarýlý            -> devam
        ///   2. Adým "zaten var" diyor   -> BAÞARI SAYILIR, obje sahiplenilir, devam
        ///   3. Adým gerçekten baþarýsýz  -> o ana kadar GERÇEKTEN oluþturulmuþ objeler
        ///                                  geri alýnýr ve hata döner
        ///
        /// 2. madde bu sürümde eklendi ve tekrar döngüsünü kýrýyor. Sahiplenilen obje
        /// 'actuallyCreated' listesine GÝRMÝYOR: onu biz oluþturmadýk, dolayýsýyla bir
        /// rollback'te silmeye hakkýmýz da yok.
        /// </summary>
        private static async Task<string> RunSequential(
            List<JObject> steps,
            string createdName,
            string kindLabel,
            string customSuccessMessage = null)
        {
            var actuallyCreated = new List<string>();
            var adopted = new List<string>();

            for (int i = 0; i < steps.Count; i++)
            {
                string raw = await MCPExecutor.Execute(steps[i]);

                bool isCreate =
                    string.Equals(steps[i]["type"]?.ToString(), "manage_gameobject", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(steps[i]["params"]?["action"]?.ToString(), "create", StringComparison.OrdinalIgnoreCase);

                if (WasStepSuccessful(raw))
                {
                    // Yalnýzca GERÇEKTEN oluþturulan GameObject'leri izle.
                    if (isCreate)
                    {
                        string createdObj = steps[i]["params"]?["name"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(createdObj))
                            actuallyCreated.Add(createdObj);
                    }

                    continue;
                }

                // ---------- ZATEN VAR: SAHÝPLEN VE DEVAM ET ----------
                if (isCreate && LooksLikeAlreadyExists(raw))
                {
                    string existing = steps[i]["params"]?["name"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(existing))
                        adopted.Add(existing);

                    Debug.Log(
                        $"[UiMacroExpander] '{existing}' was already in the scene, so it was adopted instead of re-created. " +
                        "The remaining steps configure the existing object.");

                    continue;
                }

                string failedCommand = steps[i]["type"]?.ToString() ?? "unknown";
                string failedTarget = steps[i]["params"]?["target"]?.ToString()
                                      ?? steps[i]["params"]?["name"]?.ToString()
                                      ?? "unknown";

                string parentName = steps.Count > 0 ? steps[0]["params"]?["parent"]?.ToString() : null;

                await RollbackPartialAsync(actuallyCreated, parentName);

                return new JObject
                {
                    ["status"] = "success",
                    ["result"] = new JObject
                    {
                        ["success"] = false,
                        ["message"] =
                            $"Failed at step {i + 1}/{steps.Count} while building '{createdName}': {failedCommand} on '{failedTarget}'. " +
                            "Everything this call had already created was removed, so the scene is clean and you can safely retry with a corrected call.",
                        ["failedStepResult"] = TryParseOrString(raw)
                    }
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            string message = customSuccessMessage ?? $"Created {kindLabel} '{createdName}'.";

            if (adopted.Count > 0)
            {
                message += $" ({adopted.Count} object(s) already existed and were reused: {string.Join(", ", adopted)}.)";
            }

            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject
                {
                    ["success"] = true,
                    ["message"] = message,
                    ["createdName"] = createdName
                }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool IsAllSuccess(string aggregatedResult)
        {
            try
            {
                var parsed = JObject.Parse(aggregatedResult);
                return parsed["result"]?["success"]?.ToObject<bool>() ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static bool WasStepSuccessful(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult)) return false;

            try
            {
                var parsed = JObject.Parse(rawResult);
                string outerStatus = parsed["status"]?.ToString();

                if (string.Equals(outerStatus, "error", StringComparison.OrdinalIgnoreCase)) return false;
                if (parsed["result"] is JObject r && r["success"] != null) return r.Value<bool>("success");

                return string.Equals(outerStatus, "success", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractMessage(string rawResult)
        {
            try
            {
                var parsed = JObject.Parse(rawResult);
                return parsed["result"]?["message"]?.ToString() ?? rawResult;
            }
            catch
            {
                return rawResult;
            }
        }

        private static JToken TryParseOrString(string raw)
        {
            try { return JToken.Parse(raw); }
            catch { return raw; }
        }

        private static string Success(string message)
        {
            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject { ["success"] = true, ["message"] = message }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string Error(string message)
        {
            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject { ["success"] = false, ["message"] = message }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
using System.Text;

namespace MCPForUnity.Editor.AI
{
    /// <summary>
    /// All UI-specific prompt rules. UGUI (Canvas-based) is the target system: every UI
    /// element is its own GameObject in the Hierarchy, built via macros (preferred)
    /// or manage_gameobject + manage_components (fallback).
    ///
    /// NOT UI Toolkit: the user explicitly needs objects visible in the Hierarchy
    /// and rendered in Game view. UI Toolkit keeps its whole element tree inside a
    /// single UIDocument component (invisible in Hierarchy), which does not satisfy
    /// that requirement.
    ///
    /// ---------------------------------------------------------------------------
    /// MÝMARÝ SÖZLEÞMELERÝ - bu dosyaya dokunan herkes okumalý:
    ///
    /// 1) UI kurallarý SADECE bu dosyada olmalý.
    ///
    /// 2) MACRO LÝSTESÝ ÜÇ YERDE EÞLEÞMELÝ:
    ///       UiMacroExpander.MacroToolNames        (gerçek uygulama)
    ///       OllamaToolAgent.BuildMacroToolSchemaJson (modelin gördüðü þema)
    ///       bu dosya                               (modelin okuduðu kurallar)
    ///    Üçü ayrýþýrsa sessiz kayýp olur:
    ///      - Kuralda olup þemada olmayan  -> model "þema otoritedir" kuralý yüzünden
    ///        o macro'yu güvenle kullanamaz; fiilen yok hükmündedir
    ///      - Þemada olup kodda olmayan    -> model olmayan tool'u çaðýrýp adým harcar
    ///      - Kodda olup ikisinde olmayan  -> o yetenek hiç kullanýlmaz
    ///    Liste bu dosyada ÜÇ yerde geçiyor: AppendUIPlanning,
    ///    AppendUiGenerationPipeline, AppendFinalUIReminder.
    ///
    /// 3) SIRALAMA ÖNEMLÝ. Model baðlamý yukarýdan aþaðý okur ve ÇELÝÞKÝ DURUMUNDA
    ///    SONRAKÝ TALÝMATA UYAR. AppendFinalUIReminder EN SON çaðrýlýr ve içindeki
    ///    her madde önceki bloklarýn özeti olmak zorundadýr.
    ///
    /// 4) PARAMETRE ANLATIRKEN KODA BAK. Prompt ile kod ayrýþtýðýnda KOD haklýdýr.
    ///
    /// 5) ARAÇ SORUMLULUKLARI:
    ///       write_script   - Assets/UI/Generated/ altýna .cs yazar (TEK yol)
    ///       execute_code   - a. derlenmiþ component'leri sahneye baðlar
    ///                        b. Assets/UI/Sprites/ altýna sprite üretir
    ///
    /// 6) AssetDatabase.Refresh() ve ImportAsset() MUTLAK YASAK.
    ///
    /// ---------------------------------------------------------------------------
    /// BU SÜRÜMDE DÜZELTÝLENLER - hepsi UiMacroExpander ile ayrýþmýþ noktalar
    ///
    /// A. OTOMATÝK DÝKEY AKIÞ HÝÇ ANLATILMIYORDU - EN BÜYÜK KAYIP.
    ///    UiMacroExpander artýk anchorMin/anchorMax verilmediðinde elemaný ayný
    ///    ebeveyndeki son kardeþin ALTINA kendisi yerleþtiriyor. Bu, üst üste
    ///    binmenin kalýcý çözümü: modelin bant aritmetiði yapma zorunluluðu ortadan
    ///    kalkýyor.
    ///    Ama bu dosyadaki HER örnek anchor içeriyordu ve "Every element needs
    ///    anchors" yazýyordu - yani özellik fiilen kullanýlmýyor, model hâlâ elle
    ///    hesap yapýp hata yapýyordu. Artýk <auto_layout_flow> bloðu var ve
    ///    örneklerin bir kýsmý bilinçli olarak anchor'sýz.
    ///
    /// B. control ARTIK DÖRT DEÐER ALIYOR. Bu dosya iki yerde "'control' HAS EXACTLY
    ///    ONE VALID VALUE: toggle" diyordu. Kod artýk 'toggle', 'slider', 'progress'
    ///    ve 'input' destekliyor ve kontrolü satýrýn saðýna kendisi kuruyor.
    ///    Eski metin modeli, kodun yapabildiði bir þeyden alýkoyuyordu.
    ///
    /// C. allowOverlap SIKILAÞTIRILDI. Kod artýk bu bayraðý yalnýzca NEREDEYSE AYNI
    ///    dikdörtgende kabul ediyor; kýsmi bindirmede yok sayýyor ve normal çakýþma
    ///    denetimi uygulanýyor. Model bunu bir kaçýþ yolu sanýp her reddedilen
    ///    çaðrýya eklediði için bu sýnýr kondu - ve burada anlatýlmak zorunda.
    ///
    /// D. "ZATEN VAR" ARTIK BAÞARI. Sahnede duran bir obje bulunduðunda macro onu
    ///    SAHÝPLENÝP yapýlandýrmaya devam ediyor ve BAÞARI döndürüyor. Model bunu
    ///    bir hata sanýp ayný çaðrýyý tekrarlýyordu.
    ///
    /// E. SEKME PANELLERÝ - allowOverlap tarifi korundu ve gizleme davranýþý
    ///    netleþtirildi.
    ///
    /// F. ÇAKIÞMA REDDÝ ARTIK FARKLI BÝR ÇÖZÜM ÖNERÝYOR. Eski hata mesajý
    ///    "allowOverlap ekle" diyordu ve model bunu genelleþtirdi. Yeni mesaj
    ///    "anchor'larý hiç verme" diyor - otomatik akýþa yönlendiriyor.
    ///
    /// G. MACRO LÝSTESÝ OllamaToolAgent ÞEMASIYLA EÞÝTLENDÝ. create_ui_image,
    ///    create_ui_button_bar, create_ui_slider, create_ui_progress_bar ve
    ///    create_ui_input burada tanýtýlýyordu ama þemada yoktu; model onlarý
    ///    güvenle kullanamýyordu.
    /// </summary>
    internal static class PromptRulesUI
    {
        // ------------------------------------------------------------------
        // TEK GÝRÝÞ NOKTASI
        // ------------------------------------------------------------------

        /// <summary>
        /// Tüm bloklarý doðru sýrada ekler.
        ///
        /// NOT: PromptBuilder bloklarý isteðin içeriðine göre tek tek de çaðýrabilir
        /// (koþullu yükleme). Bu metot elle test ve geriye dönük uyumluluk için.
        /// Final özet MUTLAKA en sonda kalmalý (bkz. sýnýf yorumu, madde 3).
        ///
        /// SIRA ÖNEMLÝ: AppendAutoLayoutFlowRules, AppendSiblingOverlapRules'tan ÖNCE
        /// gelmeli. Model bant aritmetiðini okumadan önce onu çoðu durumda hiç yapmasý
        /// gerekmediðini bilmeli; aksi hâlde hazýr tablolarý görüp elle hesaba devam
        /// eder.
        /// </summary>
        internal static void RegisterAll(StringBuilder sb)
        {
            AppendManageUIHardRules(sb);
            AppendUIPlanning(sb);
            AppendProfessionalUIArchitecture(sb);
            AppendUiGenerationPipeline(sb);
            AppendScreenRegionRules(sb);
            AppendAutoLayoutFlowRules(sb);
            AppendSiblingOverlapRules(sb);
            AppendTabbedScreenRules(sb);
            AppendUILayoutRules(sb);
            AppendRoundedCornerRules(sb);
            AppendBehaviourWiringRules(sb);
            AppendScriptAuthoringRules(sb);
            AppendIconAuthoringRules(sb);
            AppendModernDesignRules(sb);
            AppendDesignSystemRules(sb);
            AppendUiVisualReference(sb);
            AppendScreenPatterns(sb);
            AppendUIAgentRules(sb);
            AppendUiExecutionRequirement(sb);
            AppendFinalUIReminder(sb); // HER ZAMAN EN SON
        }

        /// <summary>
        /// VisionAgent/VisionClient baðlandýðýnda kullanýlacak sürüm. Þimdilik
        /// çaðrýlmýyor - baðlanmamýþ bir yeteneði tanýtmak modeli var olmayan bir
        /// tool'u denemeye iter.
        /// </summary>
        internal static void RegisterAllWithVision(StringBuilder sb)
        {
            AppendManageUIHardRules(sb);
            AppendUIPlanning(sb);
            AppendProfessionalUIArchitecture(sb);
            AppendVisionRules(sb);
            AppendUiGenerationPipeline(sb);
            AppendScreenRegionRules(sb);
            AppendAutoLayoutFlowRules(sb);
            AppendSiblingOverlapRules(sb);
            AppendTabbedScreenRules(sb);
            AppendUILayoutRules(sb);
            AppendRoundedCornerRules(sb);
            AppendBehaviourWiringRules(sb);
            AppendScriptAuthoringRules(sb);
            AppendIconAuthoringRules(sb);
            AppendModernDesignRules(sb);
            AppendDesignSystemRules(sb);
            AppendUiVisualReference(sb);
            AppendScreenPatterns(sb);
            AppendUIAgentRules(sb);
            AppendUiExecutionRequirement(sb);
            AppendFinalUIReminder(sb);
        }

        // ------------------------------------------------------------------
        // 1. HEDEF SÝSTEM VE ARAÇ SINIRLARI
        // ------------------------------------------------------------------

        internal static void AppendManageUIHardRules(StringBuilder sb)
        {
            sb.AppendLine("<ui_tool_rules>");
            sb.AppendLine("DO NOT use manage_ui. DO NOT create .uxml or .uss files. UI Toolkit is NOT the target system here.");
            sb.AppendLine("Reason: UI Toolkit stores the entire element tree inside a single UIDocument component, so individual buttons/panels never appear as GameObjects in the Hierarchy. That is not acceptable for this project.");
            sb.AppendLine("");
            sb.AppendLine("Build UGUI (Canvas-based) UI instead. Four tools, each with a distinct job:");
            sb.AppendLine("1. UI MACROS (create_ui_*)               : one call per element, always correct. Your default for anything visual.");
            sb.AppendLine("2. manage_gameobject / manage_components : raw fallback for the few elements no macro covers.");
            sb.AppendLine("3. write_script                          : authors a new C# behaviour script. The ONLY way to write a script. See <script_authoring>.");
            sb.AppendLine("4. execute_code                          : ONLY for wiring already-compiled components and generating icons. See <behaviour_wiring> and <icon_authoring>.");
            sb.AppendLine("");
            sb.AppendLine("Raw fallback signatures:");
            sb.AppendLine("- manage_gameobject : action=create, with 'name' and optional 'parent'");
            sb.AppendLine("- manage_components : action=add / action=set_property, with 'target', 'componentType' and 'properties'");
            sb.AppendLine("</ui_tool_rules>\n");
        }

        // ------------------------------------------------------------------
        // 2. PLANLAMA VE MAKRO ENVANTERÝ
        // ------------------------------------------------------------------

        internal static void AppendUIPlanning(StringBuilder sb)
        {
            sb.AppendLine("<ui_macro_preference>");
            sb.AppendLine("These macro tools are the PREFERRED way to build UI. Each replaces several raw calls with ONE reliable call that always gets the RectTransform, component order and text setup right.");
            sb.AppendLine("");
            sb.AppendLine("CONTAINERS");
            sb.AppendLine("- create_ui_panel      : a container / background rectangle (windows, sidebars, top and bottom bars)");
            sb.AppendLine("- create_ui_card       : a titled card + an inner content container named '<name>Content' - USE THIS for grouped sections");
            sb.AppendLine("");
            sb.AppendLine("CONTENT");
            sb.AppendLine("- create_ui_label      : a standalone text label (titles, headers, captions, large numeric readouts)");
            sb.AppendLine("- create_ui_image      : a plain visual rectangle - an icon slot, a map/camera placeholder, a colour block, a divider. Optional 'caption' draws centred text inside it; optional 'sprite' assigns an existing PNG from Assets/UI/Sprites/.");
            sb.AppendLine("- create_ui_row        : a settings row: label + optional description + optional right-side control (toggle, slider, progress bar or input field) and/or a right-aligned value text");
            sb.AppendLine("");
            sb.AppendLine("CONTROLS");
            sb.AppendLine("- create_ui_button     : a clickable button with a label");
            sb.AppendLine("- create_ui_button_bar : SEVERAL equal-width buttons in ONE call. Use this for any toolbar or command strip - it computes the bands for you, so the buttons cannot come out uneven or overlapping.");
            sb.AppendLine("- create_ui_nav_item   : a sidebar menu entry with selected/unselected state and an accent indicator - USE THIS for navigation");
            sb.AppendLine("- create_ui_toggle     : a standalone clickable on/off switch");
            sb.AppendLine("- create_ui_slider     : a REAL draggable range control");
            sb.AppendLine("- create_ui_progress_bar : a READ-ONLY fill bar for battery, signal strength, health, loading. Looks like a slider but cannot be dragged. Use THIS for anything the user only reads - a draggable battery gauge is wrong.");
            sb.AppendLine("- create_ui_input      : a REAL typeable text field");
            sb.AppendLine("");
            sb.AppendLine("OTHER");
            sb.AppendLine("- write_script         : writes a new C# behaviour script. See <script_authoring>.");
            sb.AppendLine("");
            sb.AppendLine("This list is EXHAUSTIVE. A create_ui_* name that is not on it does not exist - calling it wastes a step and returns an unknown-tool error.");
            sb.AppendLine("You do NOT need to call ensure_canvas or ensure_event_system - the agent creates the Canvas and EventSystem automatically before your first UI element.");
            sb.AppendLine("");
            sb.AppendLine("NEVER build a slider, progress bar or input field from raw calls. Unity needs internal reference fields - Slider.fillRect, Slider.handleRect, TMP_InputField.textComponent - and manage_components CANNOT assign them, because it treats the value as an asset path instead of a scene object. The result LOOKS correct but cannot be dragged, cannot fill, and shows nothing when the user types. The macros wire those fields for you.");
            sb.AppendLine("Use the raw fallback ONLY for Dropdown, RawImage and ScrollRect.");
            sb.AppendLine("</ui_macro_preference>\n");

            sb.AppendLine("<ui_planning>");
            sb.AppendLine("Before emitting UI tool calls, plan the GameObject hierarchy you will build:");
            sb.AppendLine("1. Identify the screen goal and list every panel/control needed. Write the COUNT down.");
            sb.AppendLine("2. Plan the parent-child tree (Canvas -> Panels -> Cards -> Rows/Controls).");
            sb.AppendLine("3. Decide which elements need HAND-PLACED anchors and which can use automatic stacking. See <auto_layout_flow> - it removes most of the arithmetic.");
            sb.AppendLine("4. Choose your palette and type scale ONCE, before the first call. See <modern_design_rules>.");
            sb.AppendLine("5. Build it top-down: parents must exist before their children.");
            sb.AppendLine("6. Give every object a unique, descriptive name - you reference these names later as 'parent' and 'target', AND when wiring up behaviour at the end.");
            sb.AppendLine("7. Pick the RIGHT macro for each element before you start. create_ui_label + create_ui_panel where create_ui_card fits costs three times as many calls; four create_ui_button calls where create_ui_button_bar fits costs four times as many.");
            sb.AppendLine("8. Plan the FINAL steps too: wiring the buttons so they actually switch panels (<behaviour_wiring>), and any custom behaviour that needs a new script (<script_authoring>).");
            sb.AppendLine("");
            sb.AppendLine("STATE THE PLAN ONCE, BRIEFLY, then start executing. Do not re-plan after every result - re-planning mid-build is how elements get created twice.");
            sb.AppendLine("</ui_planning>\n");
        }

        internal static void AppendProfessionalUIArchitecture(StringBuilder sb)
        {
            sb.AppendLine("<professional_ui_architecture>");
            sb.AppendLine("Build complete production-ready UGUI hierarchies:");
            sb.AppendLine("Canvas -> Window/Bars -> Panels -> Cards -> Rows -> Controls");
            sb.AppendLine("Every single UI element is its own GameObject with its own name, visible in the Hierarchy window.");
            sb.AppendLine("Group related elements under a container rather than parenting everything flat to the Canvas - a flat hierarchy is hard to reposition and looks unstructured.");
            sb.AppendLine("Rows belong INSIDE a card's content container, not directly on the main panel. That nesting is what makes the screen read as grouped sections instead of a flat list.");
            sb.AppendLine("Depth target: 4 to 6 levels below the Canvas for a real screen. One level (everything parented to the Canvas) is a prototype, not a UI.");
            sb.AppendLine("</professional_ui_architecture>\n");
        }

        // ------------------------------------------------------------------
        // 3. ÝNÞA HATTI
        // ------------------------------------------------------------------

        internal static void AppendUiGenerationPipeline(StringBuilder sb)
        {
            sb.AppendLine("<ui_generation_pipeline>");
            sb.AppendLine("UGUI BUILD SEQUENCE - emit ONE call per response.");
            sb.AppendLine("");
            sb.AppendLine("The Canvas and EventSystem are created FOR YOU automatically before your first UI element - do not create them yourself. Start directly at STEP 1, using \"parent\":\"MainCanvas\".");
            sb.AppendLine("");
            sb.AppendLine("STEP 1 - Structural panels as children of the Canvas. These DO need hand-placed anchors, because they divide the screen:");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"Sidebar\",\"parent\":\"MainCanvas\",\"color\":{\"r\":0.07,\"g\":0.09,\"b\":0.12,\"a\":1},\"outlineColor\":{\"r\":0.36,\"g\":0.55,\"b\":1,\"a\":0.35},\"sharpCorners\":true,\"anchorMin\":{\"x\":0,\"y\":0},\"anchorMax\":{\"x\":0.22,\"y\":1}}}");
            sb.AppendLine("");
            sb.AppendLine("STEP 2 - Navigation entries. NO ANCHORS: they stack automatically, top to bottom, in call order. Exactly ONE has isSelected=true:");
            sb.AppendLine("{\"type\":\"create_ui_nav_item\",\"params\":{\"name\":\"NavGeneral\",\"parent\":\"Sidebar\",\"text\":\"General\",\"isSelected\":true,\"fontSize\":16}}");
            sb.AppendLine("{\"type\":\"create_ui_nav_item\",\"params\":{\"name\":\"NavDisplay\",\"parent\":\"Sidebar\",\"text\":\"Display\",\"fontSize\":16}}");
            sb.AppendLine("{\"type\":\"create_ui_nav_item\",\"params\":{\"name\":\"NavAudio\",\"parent\":\"Sidebar\",\"text\":\"Audio\",\"fontSize\":16}}");
            sb.AppendLine("Three calls, zero arithmetic, guaranteed not to overlap. See <auto_layout_flow>.");
            sb.AppendLine("");
            sb.AppendLine("STEP 3 - A titled card per group. NO ANCHORS - cards stack downwards inside their parent. The card's inner content container is named '<name>Content' - add rows to THAT, not to the card itself:");
            sb.AppendLine("{\"type\":\"create_ui_card\",\"params\":{\"name\":\"GeneralCard\",\"parent\":\"GeneralPanel\",\"title\":\"GENERAL\",\"color\":{\"r\":0.10,\"g\":0.13,\"b\":0.17,\"a\":1},\"outlineColor\":{\"r\":0.36,\"g\":0.55,\"b\":1,\"a\":0.35},\"titleFontSize\":20,\"height\":0.38}}");
            sb.AppendLine("'height' is OPTIONAL and only used with automatic placement: it is the card's share of the parent's height, 0-1. Omit it for the default.");
            sb.AppendLine("");
            sb.AppendLine("STEP 4 - Rows inside that card's content container. NO ANCHORS - rows stack downwards. Include a 'description' - this is what makes the screen look like a real desktop app instead of a bare list:");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"NotificationsRow\",\"parent\":\"GeneralCardContent\",\"label\":\"Notifications\",\"description\":\"Show desktop alerts for important events\",\"control\":\"toggle\",\"isOn\":true,\"fontSize\":17,\"descriptionFontSize\":14}}");
            sb.AppendLine("");
            sb.AppendLine("A row whose control is a SLIDER - the macro builds the slider inside the row for you:");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"BrightnessRow\",\"parent\":\"DisplayCardContent\",\"label\":\"Brightness\",\"description\":\"Screen brightness level\",\"control\":\"slider\",\"minValue\":0,\"maxValue\":100,\"value\":70,\"wholeNumbers\":true,\"fontSize\":17}}");
            sb.AppendLine("");
            sb.AppendLine("A row with a value readout instead of a control:");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"LanguageRow\",\"parent\":\"GeneralCardContent\",\"label\":\"Language\",\"description\":\"Interface display language\",\"value\":\"English\",\"fontSize\":17}}");
            sb.AppendLine("");
            sb.AppendLine("'control' ACCEPTS EXACTLY FOUR VALUES, and the macro builds the control INSIDE the row automatically:");
            sb.AppendLine("  \"toggle\"   - a real clickable on/off switch on the right");
            sb.AppendLine("  \"slider\"   - a real draggable range control on the right. Pass minValue / maxValue / value / wholeNumbers on the ROW; they are handed to the slider.");
            sb.AppendLine("  \"progress\" - a read-only fill bar on the right (battery, signal, health). Same value parameters.");
            sb.AppendLine("  \"input\"    - a real typeable text field on the right. Pass 'placeholder' and optionally 'text' on the ROW.");
            sb.AppendLine("Omit 'control' entirely for a row with no control. Any OTHER value is rejected with an error - it is not silently ignored.");
            sb.AppendLine("'control' and 'value' CAN be used together - the value text is placed to the left of the control.");
            sb.AppendLine("You may still create a standalone create_ui_slider / create_ui_input with the row as its 'parent', but using 'control' is one call instead of two and places it correctly for you.");
            sb.AppendLine("");
            sb.AppendLine("STEP 5 - Titles, section headers and standalone text:");
            sb.AppendLine("{\"type\":\"create_ui_label\",\"params\":{\"name\":\"WindowTitle\",\"parent\":\"ContentArea\",\"text\":\"Settings\",\"color\":{\"r\":0.93,\"g\":0.95,\"b\":0.98,\"a\":1},\"fontSize\":32,\"alignment\":\"Left\",\"anchorMin\":{\"x\":0.04,\"y\":0.93},\"anchorMax\":{\"x\":0.7,\"y\":0.99}}}");
            sb.AppendLine("");
            sb.AppendLine("STEP 6 - Buttons. For a SINGLE button:");
            sb.AppendLine("{\"type\":\"create_ui_button\",\"params\":{\"name\":\"ApplyButton\",\"parent\":\"BottomBar\",\"text\":\"Apply\",\"buttonColor\":{\"r\":0.36,\"g\":0.55,\"b\":1.0,\"a\":1},\"fontSize\":16,\"anchorMin\":{\"x\":0.74,\"y\":0.22},\"anchorMax\":{\"x\":0.86,\"y\":0.78}}}");
            sb.AppendLine("");
            sb.AppendLine("For SEVERAL buttons in a row, use ONE create_ui_button_bar call instead - it computes equal widths and gaps for you, so they cannot come out uneven or overlapping:");
            sb.AppendLine("{\"type\":\"create_ui_button_bar\",\"params\":{\"name\":\"CommandBar\",\"parent\":\"MainCanvas\",\"buttons\":[\"Start\",\"Pause\",\"Reset\",{\"name\":\"StopButton\",\"text\":\"Stop\",\"buttonColor\":{\"r\":0.75,\"g\":0.25,\"b\":0.25,\"a\":1}}],\"buttonColor\":{\"r\":0.16,\"g\":0.22,\"b\":0.30,\"a\":1},\"anchorMin\":{\"x\":0,\"y\":0},\"anchorMax\":{\"x\":1,\"y\":0.10}}}");
            sb.AppendLine("Each entry is a plain string (the label) or an object with name/text/buttonColor. Bar-level buttonColor, textColor and fontSize are inherited by every button unless that button overrides them. Maximum 8 buttons per bar.");
            sb.AppendLine("");
            sb.AppendLine("STEP 7 - Standalone controls, when they are NOT inside a row. These MUST come from their macros:");
            sb.AppendLine("{\"type\":\"create_ui_slider\",\"params\":{\"name\":\"VolumeSlider\",\"parent\":\"AudioCardContent\",\"minValue\":0,\"maxValue\":100,\"value\":70,\"wholeNumbers\":true,\"fillColor\":{\"r\":0.36,\"g\":0.55,\"b\":1,\"a\":1}}}");
            sb.AppendLine("{\"type\":\"create_ui_progress_bar\",\"params\":{\"name\":\"BatteryBar\",\"parent\":\"TelemetryPanel\",\"minValue\":0,\"maxValue\":100,\"value\":78,\"fillColor\":{\"r\":0.30,\"g\":0.80,\"b\":0.45,\"a\":1}}}");
            sb.AppendLine("{\"type\":\"create_ui_input\",\"params\":{\"name\":\"UsernameInput\",\"parent\":\"LoginCard\",\"placeholder\":\"Username\",\"fontSize\":16}}");
            sb.AppendLine("maxValue must be strictly greater than minValue - the macro rejects the call otherwise.");
            sb.AppendLine("");
            sb.AppendLine("STEP 8 - Image areas: icon slots, map/camera placeholders, dividers, colour blocks:");
            sb.AppendLine("{\"type\":\"create_ui_image\",\"params\":{\"name\":\"MapArea\",\"parent\":\"MainCanvas\",\"caption\":\"MAP / CAMERA\",\"color\":{\"r\":0.10,\"g\":0.13,\"b\":0.18,\"a\":1},\"outlineColor\":{\"r\":0.36,\"g\":0.55,\"b\":1,\"a\":0.3},\"anchorMin\":{\"x\":0.26,\"y\":0.12},\"anchorMax\":{\"x\":0.98,\"y\":0.90}}}");
            sb.AppendLine("To show an actual image file, add \"sprite\":\"Assets/UI/Sprites/YourIcon64.png\". The file must already exist on disk - see <icon_authoring>.");
            sb.AppendLine("");
            sb.AppendLine("STEP 9 - Wire the behaviour so nav buttons actually switch panels. See <behaviour_wiring>. Skip only if the screen has no tabs.");
            sb.AppendLine("");
            sb.AppendLine("STEP 10 - OPTIONAL, only if the user asked for behaviour no existing component provides: write a script with write_script. See <script_authoring>.");
            sb.AppendLine("");
            sb.AppendLine("MANUAL FALLBACK - only for Dropdown, RawImage and ScrollRect, which have no macro:");
            sb.AppendLine("{\"type\":\"manage_gameobject\",\"params\":{\"action\":\"create\",\"name\":\"QualityDropdown\",\"parent\":\"ContentPanel\"}}");
            sb.AppendLine("{\"type\":\"manage_components\",\"params\":{\"action\":\"add\",\"target\":\"QualityDropdown\",\"componentType\":\"Image\",\"properties\":{\"color\":{\"r\":0.2,\"g\":0.22,\"b\":0.25,\"a\":1}}}}");
            sb.AppendLine("{\"type\":\"manage_components\",\"params\":{\"action\":\"set_property\",\"target\":\"QualityDropdown\",\"componentType\":\"RectTransform\",\"properties\":{\"anchorMin\":{\"x\":0.5,\"y\":0.6},\"anchorMax\":{\"x\":0.9,\"y\":0.68},\"offsetMin\":{\"x\":0,\"y\":0},\"offsetMax\":{\"x\":0,\"y\":0}}}}");
            sb.AppendLine("Raw calls get NO automatic placement - you must give them anchors yourself.");
            sb.AppendLine("");
            sb.AppendLine("For manual text, always use TextMeshProUGUI:");
            sb.AppendLine("{\"type\":\"manage_components\",\"params\":{\"action\":\"add\",\"target\":\"SomeLabel\",\"componentType\":\"TextMeshProUGUI\",\"properties\":{\"text\":\"LABEL\",\"fontSize\":16,\"alignment\":\"Center\",\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}}}");
            sb.AppendLine("");
            sb.AppendLine("KEY RULES:");
            sb.AppendLine("- Prefer the macros. They are one call instead of four and cannot get the component order wrong.");
            sb.AppendLine("- A macro element without anchors is placed automatically (see <auto_layout_flow>). A RAW element without anchors stays at Unity's default 100x100 at position (0,0) - everything piles up in the centre of the screen.");
            sb.AppendLine("- A manually built UGUI Button needs BOTH an Image (the clickable graphic) AND a Button component on the same GameObject.");
            sb.AppendLine("- Button labels are a SEPARATE child GameObject with a TextMeshProUGUI component - text does not go on the Button itself. (create_ui_button does this for you.)");
            sb.AppendLine("- 'parent' must be the name of a GameObject that already exists. Never reference something you have not created yet.");
            sb.AppendLine("- Build strictly top-down: panels first, then cards, then rows and controls inside them.");
            sb.AppendLine("");
            sb.AppendLine("CRITICAL - the Canvas must be the ancestor of EVERY UI element:");
            sb.AppendLine("A UI GameObject that is NOT a descendant of a Canvas is NEVER rendered - it exists in the Hierarchy but is completely invisible in Game view.");
            sb.AppendLine("Top-level panels use \"parent\":\"MainCanvas\". Every other element uses a panel, a card content container, or another element inside the Canvas as its parent.");
            sb.AppendLine("NEVER create a UI element without a 'parent'. The Canvas and EventSystem are the only parentless objects, and the agent already created both.");
            sb.AppendLine("The one legitimate exception is the behaviour host GameObject in <behaviour_wiring>, which holds no visual component and is intentionally at the scene root.");
            sb.AppendLine("");
            sb.AppendLine("CRITICAL - never create a second Canvas or EventSystem:");
            sb.AppendLine("The agent creates 'MainCanvas' (Canvas + CanvasScaler + GraphicRaycaster) and an 'EventSystem' (with StandaloneInputModule) automatically.");
            sb.AppendLine("Do NOT create another Canvas under any name. Nested Canvases break UGUI layout and scaling.");
            sb.AppendLine("Do NOT add Canvas, CanvasScaler, GraphicRaycaster, EventSystem or StandaloneInputModule to anything - all of that is already done.");
            sb.AppendLine("");
            sb.AppendLine("CRITICAL - RectTransform can NEVER be added:");
            sb.AppendLine("NEVER use action=add with componentType=RectTransform. It ALWAYS fails with 'Failed to add component RectTransform'.");
            sb.AppendLine("Reason: adding an Image, TextMeshProUGUI or Button makes Unity create the RectTransform automatically - it already exists, and a GameObject cannot have two Transforms.");
            sb.AppendLine("To position/size an element, use action=set_property on the RectTransform that is already there. Order matters: add the Image or text component FIRST, then set_property.");
            sb.AppendLine("");
            sb.AppendLine("CRITICAL - names must be unique, including the children macros create:");
            sb.AppendLine("Before creating a GameObject, check <conversation_history>: if a create call with that name already succeeded, do NOT create it again. Unity allows duplicate names and every later lookup then binds the wrong object.");
            sb.AppendLine("Macros also create hidden children named after their owner: '<name>Label', '<name>Content', '<name>Title', '<name>Knob', '<name>Indicator', '<name>Fill', '<name>Value', '<name>Description', '<name>Slider', '<name>Bar', '<name>Input', '<name>Toggle'. Do NOT give a separate element one of those names - the collision is detected and the call rejected.");
            sb.AppendLine("");
            sb.AppendLine("IF A MACRO FAILS PART-WAY, everything it had already created is removed automatically, so the scene stays clean. Read the error, fix that one problem, and re-send a corrected call - you are not leaving debris behind.");
            sb.AppendLine("");
            sb.AppendLine("IF A RESULT SAYS AN OBJECT 'already existed and was reused', THAT IS SUCCESS, not a failure. The object was left in the scene by earlier work and the macro adopted it instead of creating a duplicate. Move on to the next element - do NOT retry that call.");
            sb.AppendLine("</ui_generation_pipeline>\n");
        }

        /// <summary>
        /// OTOMATÝK DÝKEY AKIÞ - bu sürümün en önemli eklentisi.
        ///
        /// ============ NEDEN VAR - GERÇEK TEST BULGUSU ============
        /// Bir kartýn içine üç satýr koymak için model þu bantlarý kendi hesaplamak
        /// zorundaydý: 0.70-0.95, 0.40-0.65, 0.10-0.35. Altý sayýnýn altýsýný da
        /// tutturmasý gerekiyordu. Tutturamadý; satýrlar üst üste bindi ve ekran
        /// okunamaz hâle geldi.
        ///
        /// UiMacroExpander.TryAutoPlaceVertical bu sorunu çözdü: anchorMin VE
        /// anchorMax'ýn ÝKÝSÝ DE verilmezse eleman son kardeþin altýna yerleþiyor.
        /// Ama bu dosya bunu HÝÇ SÖYLEMÝYORDU ve tüm örnekleri anchor içeriyordu -
        /// yani çözüm kodda vardý, model haberdar deðildi.
        ///
        /// BU BLOK <sibling_overlap>'TAN ÖNCE GELMELÝ. Model bant aritmetiðini
        /// okumadan önce onu çoðu durumda hiç yapmasý gerekmediðini bilmeli; aksi
        /// hâlde hazýr tablolarý görüp elle hesaba devam eder.
        /// ========================================================
        /// </summary>
        internal static void AppendAutoLayoutFlowRules(StringBuilder sb)
        {
            sb.AppendLine("<auto_layout_flow>");
            sb.AppendLine("READ THIS BEFORE COMPUTING ANY ANCHOR NUMBERS. Most of the time you should not compute them at all.");
            sb.AppendLine("");
            sb.AppendLine("=== OMIT THE ANCHORS AND THE AGENT PLACES THE ELEMENT FOR YOU ===");
            sb.AppendLine("If a macro call has NEITHER anchorMin NOR anchorMax, the agent places that element directly BELOW the last element under the same parent, with correct spacing, full width and no overlap. Ever.");
            sb.AppendLine("Elements flow TOP TO BOTTOM in the order you create them. The first one starts at the top of its parent.");
            sb.AppendLine("");
            sb.AppendLine("This works for: create_ui_row, create_ui_card, create_ui_nav_item, create_ui_label, create_ui_button, create_ui_button_bar, create_ui_slider, create_ui_progress_bar, create_ui_input.");
            sb.AppendLine("It does NOT apply to create_ui_panel - a panel is a whole region, not a stacked item, so it defaults to filling its parent. Give panels explicit anchors.");
            sb.AppendLine("It does NOT apply to raw manage_components calls - those always need anchors.");
            sb.AppendLine("");
            sb.AppendLine("=== IT ALSO UNDERSTANDS SIDE-BY-SIDE COLUMNS ===");
            sb.AppendLine("If the parent already holds a FULL-HEIGHT panel on one side - an album cover, a sidebar, a telemetry rail - automatic placement puts your element in the column that is LEFT OVER, not on top of it, and stacks it from the top of that column.");
            sb.AppendLine("So a screen like \"a narrow cover panel on the left, three cards on the right\" needs NO arithmetic from you:");
            sb.AppendLine("  1. create_ui_panel CoverPanel, parent ContentArea, anchors {\"x\":0,\"y\":0} - {\"x\":0.30,\"y\":1}   <- a column, placed by hand");
            sb.AppendLine("  2. create_ui_card NowPlayingCard, parent ContentArea, NO anchors                       <- lands right of the cover");
            sb.AppendLine("  3. create_ui_card VolumeCard,     parent ContentArea, NO anchors                       <- lands under card 1");
            sb.AppendLine("  4. create_ui_card PlaylistCard,   parent ContentArea, NO anchors                       <- lands under card 2");
            sb.AppendLine("A full-height panel is one that covers most of its parent vertically. Elements in a DIFFERENT column never push yours down.");
            sb.AppendLine("");
            sb.AppendLine("=== USE IT FOR ANYTHING THAT STACKS ===");
            sb.AppendLine("Rows inside a card. Nav items inside a sidebar. Cards inside a tab panel. Labels stacked in a column. These are the cases where hand-computed bands go wrong, and they are exactly the cases automatic placement handles perfectly.");
            sb.AppendLine("Three rows in a card is three calls with no anchor fields at all:");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"NotificationsRow\",\"parent\":\"GeneralCardContent\",\"label\":\"Notifications\",\"control\":\"toggle\",\"isOn\":true}}");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"AutoSaveRow\",\"parent\":\"GeneralCardContent\",\"label\":\"Auto save\",\"control\":\"toggle\",\"isOn\":false}}");
            sb.AppendLine("{\"type\":\"create_ui_row\",\"params\":{\"name\":\"LanguageRow\",\"parent\":\"GeneralCardContent\",\"label\":\"Language\",\"value\":\"English\"}}");
            sb.AppendLine("No numbers, no overlap, no rejected calls.");
            sb.AppendLine("");
            sb.AppendLine("=== SIDE BY SIDE: LET THE CONTAINER DO IT ===");
            sb.AppendLine("Automatic flow is VERTICAL. To place elements NEXT TO each other, do not compute x bands - create their container with \"columns\" and then create the children WITHOUT anchors:");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"StatusCards\",\"parent\":\"MainCanvas\",\"columns\":3,\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0},\"anchorMin\":{\"x\":0,\"y\":0.55},\"anchorMax\":{\"x\":1,\"y\":0.92}}}");
            sb.AppendLine("{\"type\":\"create_ui_card\",\"params\":{\"name\":\"OutputCard\",\"parent\":\"StatusCards\",\"title\":\"OUTPUT\"}}");
            sb.AppendLine("{\"type\":\"create_ui_card\",\"params\":{\"name\":\"EfficiencyCard\",\"parent\":\"StatusCards\",\"title\":\"EFFICIENCY\"}}");
            sb.AppendLine("{\"type\":\"create_ui_card\",\"params\":{\"name\":\"DowntimeCard\",\"parent\":\"StatusCards\",\"title\":\"DOWNTIME\"}}");
            sb.AppendLine("Three cards, equal width, guaranteed not to overlap. 'columns' accepts 2 to 4, and the column count is the LIMIT - a fifth element needs its own container.");
            sb.AppendLine("'columns' works on create_ui_panel AND on create_ui_card, so a titled group can hold a row of elements too.");
            sb.AppendLine("Two labels at opposite ends of a bar (\"DRONE-01\" on the left, \"CONNECTED\" on the right) is the same thing: give the bar \"columns\":2 and set the second label's alignment to Right.");
            sb.AppendLine("");
            sb.AppendLine("=== OPTIONAL 'height' ===");
            sb.AppendLine("With automatic placement you may pass \"height\": a fraction of the parent's height, 0-1, for THAT element. Omit it and a sensible default is used per element type. Use it only when one item genuinely needs to be taller or shorter than its siblings.");
            sb.AppendLine("");
            sb.AppendLine("=== WHEN TO SET ANCHORS BY HAND ===");
            sb.AppendLine("Set them only when the position is structural and specific:");
            sb.AppendLine("- Panels that divide the screen: sidebar, content area, top bar, bottom bar.");
            sb.AppendLine("- Tab panels, which all share one rectangle (see <tabbed_screens>).");
            sb.AppendLine("- An element that must sit in a particular corner - a title in the top-left, a warning badge in the top-right.");
            sb.AppendLine("A CENTRED DIALOG OR LOGIN CARD is the exception that needs NO numbers: pass \"centered\":true on create_ui_card and give no anchors. The agent places it in the middle of its parent and keeps it centred when it is shrunk to its content.");
            sb.AppendLine("- Anything side-by-side horizontally rather than stacked (automatic flow is vertical only).");
            sb.AppendLine("For everything else, leaving the anchors out is both shorter and more reliable.");
            sb.AppendLine("");
            sb.AppendLine("=== MIXING IS FINE, BUT DO NOT HALF-SPECIFY ===");
            sb.AppendLine("Within one parent you may hand-place some children and let others flow - the automatic placement looks at what is already there and goes below it.");
            sb.AppendLine("But per CALL it is all or nothing: give BOTH anchorMin and anchorMax, or NEITHER. Giving only one disables automatic placement and falls back to a default for the missing half, which is almost never what you wanted.");
            sb.AppendLine("");
            sb.AppendLine("=== WHEN THE PARENT FILLS UP ===");
            sb.AppendLine("If there is no vertical room left, automatic placement stops and the overlap check tells you the container is full. That is a real signal: you are adding more elements than the container was meant to hold. Re-read the request and count what was actually asked for, or put the rest in a different container.");
            sb.AppendLine("</auto_layout_flow>\n");
        }

        /// <summary>
        /// Kardeþ çakýþmasý ve bant aritmetiði.
        ///
        /// ============ KODLA EÞLEÞMESÝ ZORUNLU ============
        /// UiMacroExpander.WouldOverlapSibling gerçekten denetliyor ve çakýþan çaðrýyý
        /// ÇALIÞTIRMADAN reddediyor, boþ bantlarý da söylüyor.
        ///
        /// 'allowOverlap' BURADA ANLATILMAK ZORUNDA. Sekme panelleri bilerek ayný
        /// dikdörtgeni paylaþýyor; model bu parametreyi bilmezse ilk paneli kurar ve
        /// ikinciden itibaren HEPSÝ reddedilir - sekmeli ekran hiç kurulamaz.
        ///
        /// BU SÜRÜMDE DEÐÝÞEN: kod artýk allowOverlap'i yalnýzca NEREDEYSE AYNI
        /// dikdörtgende kabul ediyor. Gerçek testte model bu bayraðý bir kaçýþ yolu
        /// sanýp her reddedilen çaðrýya ekledi, çakýþma denetimi tamamen devre dýþý
        /// kaldý ve kartýn içindeki satýrlar üst üste bindi. Sýnýr o yüzden kondu ve
        /// burada açýkça söylenmek zorunda.
        /// =================================================
        /// </summary>
        internal static void AppendSiblingOverlapRules(StringBuilder sb)
        {
            sb.AppendLine("<sibling_overlap>");
            sb.AppendLine("Two siblings given the same anchor range are drawn ON TOP OF EACH OTHER. This is the most frequent visible defect, and it is pure arithmetic - there is no excuse for it.");
            sb.AppendLine("The surest way to avoid it is not to do the arithmetic at all: see <auto_layout_flow>. The rules below apply to the elements you DO place by hand.");
            sb.AppendLine("");
            sb.AppendLine("THE AGENT ENFORCES THIS. Before a macro runs, it compares the new element's rectangle against every sibling already under that parent. If they overlap significantly, the call is NOT executed and you get an error that names the sibling you collided with AND lists the bands that are still free, on the axis where the collision actually happened.");
            sb.AppendLine("THAT ERROR IS A CORRECTION, NOT A FAILURE. The simplest fix is to re-send the call with NO anchors and let it be placed automatically. Otherwise read the free-band list and use a free band. Never re-send the same call unchanged.");
            sb.AppendLine("");
            sb.AppendLine("ANCHOR VALIDITY - also enforced:");
            sb.AppendLine("- Anchors are FRACTIONS of the parent rectangle, 0 to 1. NOT pixels. A value above 1 puts the element outside its parent; the agent clamps it back and warns you.");
            sb.AppendLine("- anchorMin must be SMALLER than anchorMax on both axes. Inverted values are swapped automatically.");
            sb.AppendLine("- Equal values give the element ZERO width or height: it exists in the Hierarchy and draws NOTHING. That call is rejected outright, because there is no way to guess what size you meant.");
            sb.AppendLine("");
            sb.AppendLine("IF YOU DO PLACE A GROUP BY HAND, WRITE OUT THE FULL BAND LIST FIRST, then use those exact numbers. Do not recompute per call - that is how two elements end up in the same band.");
            sb.AppendLine("");
            sb.AppendLine("VERTICAL STACK of N siblings inside a usable range LOW..HIGH:");
            sb.AppendLine("  span = HIGH - LOW ;  band = span / N ;  gap = band * 0.15");
            sb.AppendLine("  element i (0-based, TOP to BOTTOM):  anchorMax.y = HIGH - i*band ,  anchorMin.y = HIGH - (i+1)*band + gap");
            sb.AppendLine("  N=4 inside 0.04..0.96 gives: 0.765-0.960 , 0.535-0.730 , 0.305-0.500 , 0.075-0.270");
            sb.AppendLine("  N=3 inside 0.04..0.96 gives: 0.700-0.960 , 0.393-0.653 , 0.086-0.347");
            sb.AppendLine("Note that no two ranges touch, let alone overlap. (Automatic placement produces the same shape without you typing any of it.)");
            sb.AppendLine("");
            sb.AppendLine("HORIZONTAL COLUMNS - automatic flow does NOT cover these, so here are ready values:");
            sb.AppendLine("  2 columns: x 0.02-0.49 , 0.51-0.98");
            sb.AppendLine("  3 columns: x 0.02-0.32 , 0.35-0.65 , 0.68-0.98");
            sb.AppendLine("  4 columns: x 0.02-0.245 , 0.265-0.49 , 0.51-0.735 , 0.755-0.98");
            sb.AppendLine("For a strip of BUTTONS use create_ui_button_bar instead - it does this arithmetic internally and cannot get it wrong.");
            sb.AppendLine("");
            sb.AppendLine("An only child filling its parent uses anchorMin {\"x\":0,\"y\":0} and anchorMax {\"x\":1,\"y\":1}. That is not an overlap problem.");
            sb.AppendLine("For raw manage_components calls, always pair anchors with \"offsetMin\":{\"x\":0,\"y\":0} and \"offsetMax\":{\"x\":0,\"y\":0} so the element exactly fills its anchor rectangle. The macros do this for you.");
            sb.AppendLine("");
            sb.AppendLine("=== THE ONE LEGITIMATE OVERLAP - SWITCHABLE TAB PANELS ===");
            sb.AppendLine("When a screen has tabs, every content panel occupies the SAME anchor rectangle. They stack on purpose - the wiring step shows one and hides the rest at runtime.");
            sb.AppendLine("For those panels, and ONLY those, you MUST add \"allowOverlap\":true or the call will be REJECTED as an overlap:");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"GeneralPanel\",\"parent\":\"ContentArea\",\"allowOverlap\":true,\"anchorMin\":{\"x\":0,\"y\":0},\"anchorMax\":{\"x\":1,\"y\":1}}}");
            sb.AppendLine("");
            sb.AppendLine("allowOverlap IS NOT A GENERAL ESCAPE HATCH, AND THE AGENT ENFORCES THAT:");
            sb.AppendLine("It only takes effect when the element shares NEARLY THE SAME rectangle as an existing sibling - which is what a tab panel does. If the element merely overlaps a sibling PARTIALLY, the flag is IGNORED, the call is rejected as a normal overlap, and a warning is logged.");
            sb.AppendLine("Partial overlap has no legitimate use: it just means the layout is wrong. Adding the flag to a rejected call will not make it pass - fix the placement, or omit the anchors and let the agent place it.");
            sb.AppendLine("");
            sb.AppendLine("WHAT HAPPENS NEXT MATTERS: the agent keeps the FIRST panel of a stack VISIBLE and automatically HIDES every later one, so the screen stays readable instead of showing six panels on top of each other.");
            sb.AppendLine("This is correct and expected. The hidden panels still exist - they are just deactivated, and the wiring step will show the right one. Do NOT re-create them, and do NOT try to re-activate them.");
            sb.AppendLine("Because of that, build the tab whose content should be visible FIRST.");
            sb.AppendLine("</sibling_overlap>\n");
        }

        /// <summary>
        /// Sekmeli ekranlarýn yapýsý.
        ///
        /// ============ NEDEN AYRI BÝR BLOK - GERÇEK TEST BULGUSU ============
        /// "Sekmeli bir ayarlar ekraný yap, her sekmenin kendi içerik paneli olsun"
        /// isteðinde model sekme panellerini KART sandý ve alt alta dizdi; üçüncüsü
        /// haklý olarak reddedildi çünkü kart olduðu için allowOverlap göndermemiþti.
        ///
        /// Bu bir model hatasý deðil, prompt hatasýydý: sekme tarifi
        /// <sibling_overlap> bloðunun SONUNA gömülüydü, kart örnekleri ise
        /// <ui_generation_pipeline>'da adým adým gösteriliyordu. Model gördüðü en
        /// belirgin kalýbý uyguladý.
        ///
        /// Bu blok KOÞULSUZ yükleniyor, çünkü sekme yapýsý bir tasarým tercihi deðil
        /// SERT KISIT: yanlýþ kurulursa çaðrýlar reddediliyor ve ekran hiç
        /// tamamlanamýyor.
        /// ==================================================================
        /// </summary>
        internal static void AppendTabbedScreenRules(StringBuilder sb)
        {
            sb.AppendLine("<tabbed_screens>");
            sb.AppendLine("If the request mentions TABS, SECTIONS YOU SWITCH BETWEEN, or \"each tab has its own panel\", read this before your first call. Getting this shape wrong is the single most common way a tabbed screen fails to build.");
            sb.AppendLine("");
            sb.AppendLine("=== A TAB PANEL IS NOT A CARD ===");
            sb.AppendLine("These are two different structures and they are NOT interchangeable:");
            sb.AppendLine("");
            sb.AppendLine("CARDS stack VERTICALLY inside ONE visible area. Each card occupies its OWN band, they are all visible at the same time, and they never overlap. Use cards to group settings WITHIN a single tab.");
            sb.AppendLine("TAB PANELS all occupy the SAME rectangle. Only ONE is visible at a time; clicking a nav item hides the current one and shows another. They overlap completely, on purpose.");
            sb.AppendLine("");
            sb.AppendLine("If you build tab panels as cards, they stack vertically, all three stay visible at once, and the agent rejects the third one for overlapping the second. That is not a bug in the check - it means the structure is wrong.");
            sb.AppendLine("");
            sb.AppendLine("=== THE CORRECT STRUCTURE ===");
            sb.AppendLine("MainCanvas");
            sb.AppendLine("  +-- Sidebar                 one create_ui_panel, hand-placed anchors");
            sb.AppendLine("  |     +-- NavGeneral        create_ui_nav_item, isSelected=true, NO anchors");
            sb.AppendLine("  |     +-- NavDisplay        create_ui_nav_item, NO anchors");
            sb.AppendLine("  |     +-- NavAudio          create_ui_nav_item, NO anchors");
            sb.AppendLine("  +-- ContentArea             one create_ui_panel, hand-placed anchors - the shared frame the tabs live in");
            sb.AppendLine("        +-- GeneralPanel      create_ui_panel, allowOverlap=true, anchors 0,0 - 1,1");
            sb.AppendLine("        +-- DisplayPanel      create_ui_panel, allowOverlap=true, anchors 0,0 - 1,1");
            sb.AppendLine("        +-- AudioPanel        create_ui_panel, allowOverlap=true, anchors 0,0 - 1,1");
            sb.AppendLine("");
            sb.AppendLine("All three tab panels are children of ContentArea, all three have IDENTICAL anchors, and all three pass allowOverlap=true. Each tab's own content then goes INSIDE its panel - cards, rows, sliders, whatever that tab needs, and those can be placed automatically.");
            sb.AppendLine("");
            sb.AppendLine("=== BUILD ORDER - follow it exactly ===");
            sb.AppendLine("1. create_ui_panel Sidebar (parent MainCanvas)");
            sb.AppendLine("2. create_ui_panel ContentArea (parent MainCanvas) - to the right of the sidebar, no overlap with it");
            sb.AppendLine("3. create_ui_nav_item for EACH tab, inside Sidebar, WITHOUT anchors so they stack automatically. Exactly ONE has isSelected=true.");
            sb.AppendLine("4. create_ui_panel for EACH tab, inside ContentArea. Build the tab that should be VISIBLE FIRST - the agent keeps the first panel of a stack visible and hides the rest.");
            sb.AppendLine("5. Fill each tab panel with its own content, WITHOUT anchors so cards and rows stack automatically.");
            sb.AppendLine("6. Wire the nav items to the panels with execute_code - see <behaviour_wiring>.");
            sb.AppendLine("");
            sb.AppendLine("=== THE EXACT CALL FOR A TAB PANEL ===");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"GeneralPanel\",\"parent\":\"ContentArea\",\"allowOverlap\":true,\"color\":{\"r\":0,\"g\":0,\"b\":0,\"a\":0},\"sharpCorners\":true,\"anchorMin\":{\"x\":0,\"y\":0},\"anchorMax\":{\"x\":1,\"y\":1}}}");
            sb.AppendLine("Repeat with the SAME anchors and the SAME allowOverlap for every other tab, changing only 'name'.");
            sb.AppendLine("A fully transparent colour is right here: the tab panel is an invisible frame, and ContentArea already provides the visible background.");
            sb.AppendLine("The anchors ARE given by hand here, and that is deliberate: identical full-parent anchors are exactly what makes the agent treat them as one stack. Omitting them would place the panels one below another instead.");
            sb.AppendLine("");
            sb.AppendLine("=== NAMING ===");
            sb.AppendLine("Use a consistent pair so the wiring step can find both: nav item 'NavX' with panel 'XPanel' (NavGeneral/GeneralPanel, NavAudio/AudioPanel). The wiring code matches them by position in two arrays, so the order must be the same in both.");
            sb.AppendLine("");
            sb.AppendLine("=== WHAT HAPPENS AFTER YOU BUILD THEM ===");
            sb.AppendLine("The agent automatically DEACTIVATES every stacked panel except the first. That is correct - a screen showing three panels at once is unreadable. The hidden panels still exist and still contain their children.");
            sb.AppendLine("Do NOT re-create them. Do NOT try to re-activate them. Do NOT report them as missing.");
            sb.AppendLine("IMPORTANT: once a panel is deactivated, GameObject.Find can no longer see it. When you add content to a hidden tab panel, that still works - the macro looks up parents differently. But in your wiring code you MUST use the inactive-aware helper from <behaviour_wiring>.");
            sb.AppendLine("</tabbed_screens>\n");
        }

        /// <summary>
        /// EKRAN BÖLGELERÝ - bu sürümün ikinci önemli eklentisi.
        ///
        /// ============ NEDEN VAR - GERÇEK TEST BULGUSU ============
        /// Bir drone kontrol istasyonu istendi: "ekranýn büyük kýsmýný kaplayan harita
        /// alaný" ve "saðda dar bir telemetri paneli". Model haritayý x 0-1 yaptý,
        /// yani ekranýn TAMAMINA. Sonra telemetri panelini saða koymaya çalýþtý:
        ///
        ///     'TelemetryPanel' at [x 0.78-1] would overlap 'MapArea' at [x 0-1] by 100%
        ///
        /// Reddedildi. Model bu sefer paneli tüm ekrana koymayý denedi, o da
        /// reddedildi, ve görev týkandý. Ekranda yalnýzca durum çubuðu ve harita kaldý.
        ///
        /// Bu bir yerleþtirme hatasý deðil, BÖLGE PLANLAMA hatasýydý: "büyük kýsým"
        /// ifadesi "tamamý" diye okundu ve geri kalan her þeye yer kalmadý.
        ///
        /// Düzeltecek bilgi aslýnda <ui_layout_rules> içinde hazýr duruyordu - bölge
        /// haritalarý - ama o blok PromptBuilder tarafýndan HÝÇ ÇAÐRILMIYOR. Elenmiþti
        /// çünkü anchor ve boþluk kurallarý baþka bloklarda tekrarlanýyordu; bölge
        /// haritalarýnýn ise baþka hiçbir yerde karþýlýðý yoktu.
        ///
        /// Bu blok o boþluðu dolduruyor ve ZORUNLU: yanlýþ bölünmüþ bir ekran
        /// sonradan düzeltilemiyor, çünkü ilk panel kaydedildikten sonra diðer her
        /// çaðrý onunla çakýþýyor.
        ///
        /// KISA TUTULDU. Zorunlu her blok, taþma anýnda çekirdek kurallarýn yerini
        /// daraltýyor. Burada yalnýzca modelin kendi baþýna bulamadýðý þey var:
        /// hazýr bölge sayýlarý ve "önce böl, sonra doldur" kuralý.
        /// ========================================================
        /// </summary>
        internal static void AppendScreenRegionRules(StringBuilder sb)
        {
            sb.AppendLine("<screen_regions>");
            sb.AppendLine("DIVIDE THE SCREEN BEFORE YOU FILL IT. Decide every region's anchors FIRST, as one set of numbers, then create the panels. A region created without that plan usually takes the whole screen and leaves nothing for the rest.");
            sb.AppendLine("");
            sb.AppendLine("\"The big area\", \"the main view\", \"most of the screen\" NEVER mean x 0-1 and y 0-1. They mean what is left AFTER the bars and side panels take their share. Subtract first, then assign.");
            sb.AppendLine("");
            sb.AppendLine("READY REGION MAPS - use these numbers directly, they already fit together:");
            sb.AppendLine("- Top bar:        anchorMin {\"x\":0,\"y\":0.92}    anchorMax {\"x\":1,\"y\":1}");
            sb.AppendLine("- Bottom bar:     anchorMin {\"x\":0,\"y\":0}       anchorMax {\"x\":1,\"y\":0.10}");
            sb.AppendLine("- Left sidebar:   anchorMin {\"x\":0,\"y\":0.10}    anchorMax {\"x\":0.22,\"y\":0.92}");
            sb.AppendLine("- Right rail:     anchorMin {\"x\":0.76,\"y\":0.10} anchorMax {\"x\":1,\"y\":0.92}");
            sb.AppendLine("- Main area, sidebar on the left only:  {\"x\":0.22,\"y\":0.10} - {\"x\":1,\"y\":0.92}");
            sb.AppendLine("- Main area, right rail only:           {\"x\":0,\"y\":0.10}    - {\"x\":0.76,\"y\":0.92}");
            sb.AppendLine("- Main area, both sides:                {\"x\":0.22,\"y\":0.10} - {\"x\":0.76,\"y\":0.92}");
            sb.AppendLine("- Centred dialog:                       {\"x\":0.3,\"y\":0.3}   - {\"x\":0.7,\"y\":0.7}   (for a CARD, just pass \"centered\":true instead)");
            sb.AppendLine("");
            sb.AppendLine("=== SPLITTING A SCREEN: USE \"region\", NOT NUMBERS ===");
            sb.AppendLine("REMEMBER: in Unity anchors y=0 is the BOTTOM edge and y=1 is the TOP. A top bar is y 0.92-1, NOT y 0-0.08. Getting this backwards puts your header at the bottom of the screen and leaves the top half empty.");
            sb.AppendLine("So do not compute these bands at all. Give create_ui_panel a \"region\" and the agent cuts it out of the parent's FREE area:");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"TopBar\",\"parent\":\"MainCanvas\",\"region\":\"top\",\"size\":0.08}}");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"CommandBar\",\"parent\":\"MainCanvas\",\"region\":\"bottom\",\"size\":0.10}}");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"LeftColumn\",\"parent\":\"MainCanvas\",\"region\":\"left\",\"size\":0.22}}");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"RightColumn\",\"parent\":\"MainCanvas\",\"region\":\"right\",\"size\":0.22}}");
            sb.AppendLine("{\"type\":\"create_ui_panel\",\"params\":{\"name\":\"RadarArea\",\"parent\":\"MainCanvas\",\"region\":\"center\"}}");
            sb.AppendLine("ORDER MATTERS: bars and side columns first, \"center\" LAST - center takes whatever is left, so nothing can be docked after it. Regions never overlap, whatever order of sizes you pick.");
            sb.AppendLine("KEEP REGIONS THIN: \"size\" is for BARS and SIDE COLUMNS - roughly 0.06-0.12 for a bar, 0.18-0.28 for a column. Never dock a region that swallows half the screen. Content that stacks vertically (a row of cards, then a wide card under it) all goes inside ONE region - usually \"center\" - where elements stack automatically with no anchors.");
            sb.AppendLine("- Full-screen window with margin:       {\"x\":0.06,\"y\":0.08} - {\"x\":0.94,\"y\":0.92}");
            sb.AppendLine("Drop the top bar and the regions below it start at y 0 instead of 0.10; drop the bottom bar and they end at y 1.");
            sb.AppendLine("");
            sb.AppendLine("WORKED EXAMPLE - a control station with a status strip, a big display, a right-hand readout rail and a command bar:");
            sb.AppendLine("  StatusStrip   y 0.94-1.00 , x 0-1");
            sb.AppendLine("  CommandBar    y 0.00-0.10 , x 0-1");
            sb.AppendLine("  TelemetryRail x 0.76-1.00 , y 0.10-0.94");
            sb.AppendLine("  DisplayArea   x 0.00-0.76 , y 0.18-0.94        <- the 'big area', NOT x 0-1");
            sb.AppendLine("  InputRow      x 0.00-0.76 , y 0.10-0.17        <- between the display and the command bar");
            sb.AppendLine("Nothing overlaps, nothing is left out, and every later element has a region to live in.");
            sb.AppendLine("");
            sb.AppendLine("SPLITTING A REGION LEFT/RIGHT:");
            sb.AppendLine("Create the narrow side as a FULL-HEIGHT panel with hand-placed anchors, then create everything else in that region WITHOUT anchors - it automatically lands in the leftover column.");
            sb.AppendLine("  Narrow left column:  {\"x\":0,\"y\":0} - {\"x\":0.30,\"y\":1}     then the rest needs no anchors");
            sb.AppendLine("  Narrow right column: {\"x\":0.70,\"y\":0} - {\"x\":1,\"y\":1}     then the rest needs no anchors");
            sb.AppendLine("  Two equal halves:    {\"x\":0,\"y\":0} - {\"x\":0.49,\"y\":1}  and  {\"x\":0.51,\"y\":0} - {\"x\":1,\"y\":1}");
            sb.AppendLine("Never give a card or row the full width (x 0.05-0.95) in a region that already has a side column - that is the most common way this fails.");
            sb.AppendLine("");
            sb.AppendLine("Regions are the ONLY elements you place by hand. Everything inside a region - cards, rows, readouts, controls - is created without anchors and stacks automatically. See <auto_layout_flow>.");
            sb.AppendLine("</screen_regions>\n");
        }

        // ------------------------------------------------------------------
        // 4. YERLEÞÝM VE KÖÞELER
        // ------------------------------------------------------------------

        internal static void AppendUILayoutRules(StringBuilder sb)
        {
            sb.AppendLine("<ui_layout_rules>");
            sb.AppendLine("Professional layout rules:");
            sb.AppendLine("- Every UI element ends up with RectTransform anchors. For macros you either pass anchorMin/anchorMax or omit both and let the agent stack the element (see <auto_layout_flow>); raw calls need an explicit set_property.");
            sb.AppendLine("- Use anchorMin/anchorMax (fractions of the parent, 0-1) plus offsetMin/offsetMax for panels that should stretch.");
            sb.AppendLine("- NEVER resize a UI element by changing scale/localScale. Scale must stay (1,1,1) - scaling distorts text and pushes elements outside the Canvas.");
            sb.AppendLine("- Never set a negative width or height, and never set sizeDelta to fight the anchors you just set. Anchors are the single source of truth for size.");
            sb.AppendLine("- Never overlap interactive controls. See <sibling_overlap>.");
            sb.AppendLine("- Use a consistent spacing scale for every margin and gap: 8, 16, 24 or 32 pixels. Never invent one-off spacing values.");
            sb.AppendLine("- Leave breathing room: elements should not touch their parent's edges. Inset children by at least 16px from the container edge.");
            sb.AppendLine("- Align edges: elements in the same column share the same left/right anchors; elements in the same row share the same top/bottom anchors. Automatic placement does this for you.");
            sb.AppendLine("- Do NOT compress everything into one corner, and do NOT leave huge empty areas. A window should feel filled but not crowded.");
            sb.AppendLine("- A card should be sized to its CONTENT, not stretched to fill the whole panel. Three rows in a card that spans the entire screen height leaves a huge empty gap underneath them - give the card a 'height' that matches how many rows it holds, and let the empty space fall below the last card.");
            sb.AppendLine("- Reading order is top-down: titles above content, actions at the bottom. Never put the primary action above the content it applies to.");
            sb.AppendLine("- A number that is the POINT of the screen (a clock, a speed, a battery percentage) gets a large fontSize and a lot of space. Do not give the main readout the same size as a caption.");
            sb.AppendLine("");
            sb.AppendLine("For the anchor numbers that divide the screen into regions, see <screen_regions>.");
            sb.AppendLine("</ui_layout_rules>\n");
        }

        /// <summary>
        /// Köþe yuvarlaklýðý.
        ///
        /// DÜZELTÝLDÝ - VAR OLMAYAN PARAMETRE: bu blok bir zamanlar 'cornerRadius'
        /// anlatýyordu ve bir deðer skalasý veriyordu. UiMacroExpander böyle bir alan
        /// HÝÇ OKUMUYOR - yalnýzca 'sharpCorners' (bool) biliyor. Model her çaðrýya
        /// cornerRadius ekliyor, macro sessizce atýyordu: hem token yanýyor hem model
        /// yuvarlaklýðý kontrol ettiðini sanýyordu.
        /// </summary>
        internal static void AppendRoundedCornerRules(StringBuilder sb)
        {
            sb.AppendLine("<rounded_corners>");
            sb.AppendLine("Rounded corners are applied AUTOMATICALLY. Panels, cards, buttons, nav items, toggles and image areas get a pre-generated 9-slice sprite from Assets/UI/Sprites/ without you doing anything.");
            sb.AppendLine("");
            sb.AppendLine("There is exactly ONE parameter you control:");
            sb.AppendLine("- sharpCorners (boolean, on create_ui_panel and create_ui_image): pass true to KEEP square corners. Omit it for rounded.");
            sb.AppendLine("");
            sb.AppendLine("There is NO 'cornerRadius' parameter. Sending one is silently ignored and only wastes tokens.");
            sb.AppendLine("");
            sb.AppendLine("PASS sharpCorners true FOR: full-screen backgrounds and content areas that fill their parent edge to edge; top bars, bottom bars and sidebars anchored to the screen edge (a rounded corner against the screen edge looks like a rendering bug); panels that exist only as invisible containers.");
            sb.AppendLine("OMIT IT (rounded, the default) FOR: cards, dialogs and any surface sitting ON TOP of another surface. Buttons, nav items and toggles are always rounded and have no sharpCorners option at all.");
            sb.AppendLine("");
            sb.AppendLine("If corners look sharp when you expected rounded, the sprite files are missing and the user must run Tools > UI > Generate Rounded Sprites once. You cannot fix that from a tool call, and it does not stop the element from working.");
            sb.AppendLine("</rounded_corners>\n");
        }

        // ------------------------------------------------------------------
        // 5. DAVRANIÞ BAÐLAMA
        // ------------------------------------------------------------------

        /// <summary>
        /// UI oluþturulduktan SONRA davranýþ baðlama kurallarý.
        ///
        /// KAPSAM: burada baðlanan component ZATEN DERLENMÝÞ olmalý. Bu görevde
        /// write_script ile yazýlan bir script henüz derlenmediði için buraya konu
        /// olamaz - onun baðlanmasý kullanýcýnýn BÝR SONRAKÝ komutunda olur.
        ///
        /// GameObject.Find pasif objeleri BULAMAZ: seçili olmayan nav göstergeleri ve
        /// ilk hariç tüm sekme panelleri gizli, o objeler Find ile bulunamaz ve
        /// baðlama sessizce eksik kalýr.
        /// </summary>
        internal static void AppendBehaviourWiringRules(StringBuilder sb)
        {
            sb.AppendLine("<behaviour_wiring>");
            sb.AppendLine("AFTER the visual UI is complete, wire up its behaviour so buttons actually DO something.");
            sb.AppendLine("manage_components creates buttons but cannot attach click handlers. A screen with tabs that do not switch is unfinished. Use execute_code for this final step.");
            sb.AppendLine("");
            sb.AppendLine("SCOPE: this applies ONLY to components that already exist AND are compiled, such as UI.TabController. A script you wrote with write_script during THIS task is not compiled yet and cannot be attached here - that happens in the user's next command.");
            sb.AppendLine("");
            sb.AppendLine("AVAILABLE COMPONENT (already at Assets/UI/TabController.cs - reuse it, never rewrite it):");
            sb.AppendLine("UI.TabController switches between content panels when nav buttons are clicked and updates the selected/unselected colors. Public fields:");
            sb.AppendLine("  tabs             : List<UI.TabController.Tab>");
            sb.AppendLine("  startingTabIndex : int");
            sb.AppendLine("  selectedBackground / selectedTextColor / unselectedBackground / unselectedTextColor : Color");
            sb.AppendLine("Each Tab has: tabName (string), navButton (Button), contentPanel (GameObject), indicator (GameObject, optional), label (TextMeshProUGUI, optional).");
            sb.AppendLine("");
            sb.AppendLine("create_ui_nav_item already creates a '<name>Indicator' child on EVERY item and deactivates it on unselected ones, so TabController can show and hide them. You never create indicators yourself.");
            sb.AppendLine("Stacked tab panels are already deactivated except the first (see <sibling_overlap>), so TabController finds the correct starting state.");
            sb.AppendLine("");
            sb.AppendLine("WIRING CALL - emit ONCE, after every panel and nav item exists:");
            sb.AppendLine("{\"type\":\"execute_code\",\"params\":{\"action\":\"execute\",\"code\":\"<the C# below, as a single JSON string with \\n for newlines>\"}}");
            sb.AppendLine("");
            sb.AppendLine("Adapt ONLY the two name arrays to YOUR actual object names:");
            sb.AppendLine("System.Func<string, GameObject> find = (n) => {");
            sb.AppendLine("  var d = GameObject.Find(n);");
            sb.AppendLine("  if (d != null) return d;");
            sb.AppendLine("  foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) {");
            sb.AppendLine("    if (t.gameObject.name == n && t.gameObject.scene.IsValid()) return t.gameObject;");
            sb.AppendLine("  }");
            sb.AppendLine("  return null;");
            sb.AppendLine("};");
            sb.AppendLine("var existing = find(\"TabController\");");
            sb.AppendLine("if (existing != null) UnityEngine.Object.DestroyImmediate(existing);");
            sb.AppendLine("var host = new GameObject(\"TabController\");");
            sb.AppendLine("var tc = host.AddComponent<UI.TabController>();");
            sb.AppendLine("if (tc.tabs == null) tc.tabs = new System.Collections.Generic.List<UI.TabController.Tab>();");
            sb.AppendLine("string[] navNames = { \"NavGeneral\", \"NavDisplay\", \"NavAudio\" };");
            sb.AppendLine("string[] panelNames = { \"GeneralPanel\", \"DisplayPanel\", \"AudioPanel\" };");
            sb.AppendLine("int wired = 0;");
            sb.AppendLine("var missing = new System.Collections.Generic.List<string>();");
            sb.AppendLine("for (int i = 0; i < navNames.Length; i++) {");
            sb.AppendLine("  var nav = find(navNames[i]);");
            sb.AppendLine("  var panel = find(panelNames[i]);");
            sb.AppendLine("  if (nav == null) { missing.Add(navNames[i]); continue; }");
            sb.AppendLine("  if (panel == null) { missing.Add(panelNames[i]); continue; }");
            sb.AppendLine("  var btn = nav.GetComponent<UnityEngine.UI.Button>();");
            sb.AppendLine("  if (btn == null) { missing.Add(navNames[i] + \"(no Button)\"); continue; }");
            sb.AppendLine("  var tab = new UI.TabController.Tab();");
            sb.AppendLine("  tab.tabName = navNames[i];");
            sb.AppendLine("  tab.navButton = btn;");
            sb.AppendLine("  tab.contentPanel = panel;");
            sb.AppendLine("  var lbl = nav.transform.Find(navNames[i] + \"Label\");");
            sb.AppendLine("  if (lbl != null) tab.label = lbl.GetComponent<TMPro.TextMeshProUGUI>();");
            sb.AppendLine("  var ind = nav.transform.Find(navNames[i] + \"Indicator\");");
            sb.AppendLine("  if (ind != null) tab.indicator = ind.gameObject;");
            sb.AppendLine("  tc.tabs.Add(tab); wired++;");
            sb.AppendLine("}");
            sb.AppendLine("tc.startingTabIndex = 0;");
            sb.AppendLine("UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());");
            sb.AppendLine("return \"Wired \" + wired + \" tabs. Missing: \" + (missing.Count == 0 ? \"none\" : string.Join(\", \", missing));");
            sb.AppendLine("");
            sb.AppendLine("WHY THE 'find' HELPER EXISTS - THIS MATTERS: GameObject.Find CANNOT see deactivated objects. Nav indicators on unselected items ARE deactivated, and every stacked tab panel after the first IS deactivated. Plain GameObject.Find would report them as missing and the wiring would silently be incomplete. Always use the helper above, and transform.Find for children (it also finds inactive ones).");
            sb.AppendLine("");
            sb.AppendLine("READ THE RETURN VALUE. If it reports missing names, those are typos in YOUR arrays or elements you never created - fix that one thing and retry once. If wired equals 0, the names are wrong; do not retry the identical code.");
            sb.AppendLine("");
            sb.AppendLine("RULES FOR execute_code - these are absolute:");
            sb.AppendLine("- Permitted uses are EXACTLY two: (1) wiring already-compiled components as shown here, (2) generating an icon sprite per <icon_authoring>. Nothing else.");
            sb.AppendLine("- NEVER use it to write a script file. That is write_script - see <script_authoring>.");
            sb.AppendLine("- NEVER use it to create UI elements - that is what the macros are for.");
            sb.AppendLine("- NEVER call AssetDatabase.Refresh() or AssetDatabase.ImportAsset(). Both trigger a domain reload, which kills this agent mid-task and closes the chat window.");
            sb.AppendLine("- NEVER change project settings, delete or move assets, or touch anything outside the current scene and Assets/UI/Sprites/.");
            sb.AppendLine("- NEVER use EditorApplication.Exit, EditorUtility.RequestScriptReload or CompilationPipeline.RequestScriptCompilation.");
            sb.AppendLine("- Use FULLY QUALIFIED type names: UI.TabController, UnityEngine.UI.Button, TMPro.TextMeshProUGUI, System.Collections.Generic.List<T>. The wrapper imports only System, System.Collections.Generic, System.Linq, System.Reflection, UnityEngine and UnityEditor.");
            sb.AppendLine("- Always null-check lookups and 'continue' past missing entries instead of crashing.");
            sb.AppendLine("- End with a 'return' statement returning a short status string.");
            sb.AppendLine("- The agent validates every payload before running it. If it is rejected, read the reason and comply - do not reword the same forbidden operation.");
            sb.AppendLine("</behaviour_wiring>\n");
        }

        // ------------------------------------------------------------------
        // 6. SCRIPT ÜRETÝMÝ
        // ------------------------------------------------------------------

        /// <summary>
        /// Modelin kendi C# davranýþ script'ini yazmasýna izin veren kurallar.
        ///
        /// NEDEN execute_code DEÐÝL, write_script: execute_code ile kaynaðýn bir C#
        /// STRING LITERAL'ine gömülmesi gerekiyor, yani her satýr sonu ve týrnak ÝKÝ
        /// KEZ kaçýrýlmalý. Gerçek testte hiç çalýþmadý:
        ///     "Line 3: Newline in constant" / "Line 4: Identifier expected"
        ///
        /// YASAK LÝSTESÝ UiMacroExpander.ForbiddenInGeneratedScript ÝLE EÞLEÞMELÝ.
        /// </summary>
        internal static void AppendScriptAuthoringRules(StringBuilder sb)
        {
            sb.AppendLine("<script_authoring>");
            sb.AppendLine("When the UI needs behaviour that no existing component provides, you MAY write a new C# script.");
            sb.AppendLine("");
            sb.AppendLine("USE THE write_script MACRO. Never write a script through execute_code.");
            sb.AppendLine("Reason: execute_code requires the source to be embedded inside a C# string literal, which means escaping every newline and quote TWICE - once for C#, once for JSON. That reliably corrupts the file; the compiler reports 'Newline in constant' and the script never builds. write_script takes the source as a plain field, so no escaping is involved.");
            sb.AppendLine("");
            sb.AppendLine("WHEN TO WRITE ONE:");
            sb.AppendLine("- A button must do something UI.TabController does not cover (open a dialog, quit, reset a form, start or stop a timer, switch a colour theme).");
            sb.AppendLine("- A control needs custom visual feedback, such as a toggle knob that physically slides between the two ends.");
            sb.AppendLine("- The screen has real behaviour by its nature: a clock that never counts or a gauge that never moves is unfinished, and a script is what finishes it.");
            sb.AppendLine("- The user explicitly asks for interactive behaviour.");
            sb.AppendLine("");
            sb.AppendLine("WHEN NOT TO:");
            sb.AppendLine("- Plain tab switching - UI.TabController already handles that. Wire it, do not reimplement it.");
            sb.AppendLine("- Anything purely visual (colour, size, text). That is a macro parameter, not a script.");
            sb.AppendLine("- Speculative helpers 'in case they are useful later'. Write only what this request needs.");
            sb.AppendLine("Keep it to one or two scripts per request. Each extra file is another chance to break the compile.");
            sb.AppendLine("");
            sb.AppendLine("THE CALL:");
            sb.AppendLine("{\"type\":\"write_script\",\"params\":{\"name\":\"ToggleKnobSlider\",\"content\":\"using UnityEngine;\\nusing UnityEngine.UI;\\n\\npublic class ToggleKnobSlider : MonoBehaviour\\n{\\n    public RectTransform knob;\\n\\n    void Start()\\n    {\\n        var t = GetComponent<Toggle>();\\n        if (t == null) return;\\n        t.onValueChanged.AddListener(Apply);\\n        Apply(t.isOn);\\n    }\\n\\n    public void Apply(bool isOn)\\n    {\\n        if (knob == null) return;\\n        knob.anchorMin = isOn ? new Vector2(0.52f, 0.12f) : new Vector2(0.06f, 0.12f);\\n        knob.anchorMax = isOn ? new Vector2(0.94f, 0.88f) : new Vector2(0.48f, 0.88f);\\n        knob.offsetMin = Vector2.zero;\\n        knob.offsetMax = Vector2.zero;\\n    }\\n}\"}}");
            sb.AppendLine("");
            sb.AppendLine("PARAMETERS:");
            sb.AppendLine("- 'name'    : the class name only. No .cs extension, no folder path. The file becomes Assets/UI/Generated/<name>.cs");
            sb.AppendLine("- 'content' : the complete C# source as ordinary text. Write normal C# - the JSON encoder handles escaping. Do not wrap it in a string literal, do not add markdown fences, do not double-escape anything.");
            sb.AppendLine("");
            sb.AppendLine("AFTER THE CALL - THIS IS THE PART MODELS GET WRONG:");
            sb.AppendLine("The file is on disk but Unity has NOT compiled it. The type DOES NOT EXIST yet.");
            sb.AppendLine("- Do NOT call execute_code to add it as a component. Do NOT check whether the type exists.");
            sb.AppendLine("- Do NOT call AssetDatabase.Refresh or AssetDatabase.ImportAsset - both are blocked and both would kill this agent.");
            sb.AppendLine("Compilation happens automatically once this task ends. If it fails, the file is deleted automatically and the project stays usable.");
            sb.AppendLine("Finish any remaining visual work, then end with task_complete and mention in its summary which script you created. Attaching it happens in the user's NEXT command, once compilation has finished.");
            sb.AppendLine("");
            sb.AppendLine("SOURCE CODE RULES - the macro rejects most of these before writing:");
            sb.AppendLine("- The class name inside 'content' MUST match 'name' EXACTLY. Unity cannot load a MonoBehaviour whose file name and class name differ, and the error it gives ('the referenced script is missing') never points at the real cause.");
            sb.AppendLine("- ONE class per file. NO namespace - the agent attaches the type later by its PLAIN CLASS NAME, and a namespaced type cannot be found that way. A namespace declaration is REJECTED.");
            sb.AppendLine("- Derive from MonoBehaviour. No constructors, no static mutable state.");
            sb.AppendLine("- Pick a specific, unlikely-to-clash name. Generic names like UIManager, Controller or Helper may already exist in the project and cause a duplicate-definition error that blocks ALL compilation.");
            sb.AppendLine("- Include EVERY using the code needs: UnityEngine, UnityEngine.UI, TMPro as applicable. A missing using is the most common compile failure.");
            sb.AppendLine("- These strings are REJECTED anywhere in the source, including inside comments: 'UnityEditor', 'AssetDatabase', '[MenuItem', '#if UNITY_EDITOR', 'System.IO.File.Delete', 'Application.Quit'. Editor-only APIs in a runtime MonoBehaviour break builds.");
            sb.AppendLine("- Public fields for references, so they can be assigned later from the Inspector or a wiring call.");
            sb.AppendLine("- Null-check every reference before use, in every method. A NullReferenceException in Start spams the Console and hides real errors.");
            sb.AppendLine("- No Find or GetComponent inside Update - cache them in Start. No async void, no Task, no Thread, no file or network access.");
            sb.AppendLine("- Keep it short. A behaviour script that does one thing well is what is wanted here.");
            sb.AppendLine("</script_authoring>\n");
        }

        // ------------------------------------------------------------------
        // 7. ÝKON ÜRETÝMÝ
        // ------------------------------------------------------------------

        /// <summary>
        /// Ýkon üretici için prompt tarafý. Procedural Texture2D -> PNG ->
        /// Assets/UI/Sprites/.
        /// </summary>
        internal static void AppendIconAuthoringRules(StringBuilder sb)
        {
            sb.AppendLine("<icon_authoring>");
            sb.AppendLine("Icons are optional polish. Never delay finishing a screen for them.");
            sb.AppendLine("");
            sb.AppendLine("PREFERRED - reuse what already exists. Any PNG under Assets/UI/Sprites/ can be shown with one call:");
            sb.AppendLine("{\"type\":\"create_ui_image\",\"params\":{\"name\":\"HomeIcon\",\"parent\":\"NavHome\",\"sprite\":\"Assets/UI/Sprites/IconRing64.png\",\"color\":{\"r\":0.66,\"g\":0.71,\"b\":0.78,\"a\":1},\"anchorMin\":{\"x\":0.02,\"y\":0.25},\"anchorMax\":{\"x\":0.07,\"y\":0.75}}}");
            sb.AppendLine("");
            sb.AppendLine("FALLBACK - generate a simple procedural icon with execute_code. This is a permitted use. Keep to flat geometric shapes: circle, ring, square, chevron, plus. Do not attempt detailed artwork - the result is a small monochrome mask, not an illustration.");
            sb.AppendLine("");
            sb.AppendLine("int size = 64;");
            sb.AppendLine("var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);");
            sb.AppendLine("var px = new Color[size * size];");
            sb.AppendLine("float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;");
            sb.AppendLine("float outer = size * 0.44f, inner = size * 0.30f;");
            sb.AppendLine("for (int y = 0; y < size; y++) {");
            sb.AppendLine("  for (int x = 0; x < size; x++) {");
            sb.AppendLine("    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));");
            sb.AppendLine("    float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner + 1f);");
            sb.AppendLine("    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("tex.SetPixels(px); tex.Apply();");
            sb.AppendLine("System.IO.Directory.CreateDirectory(\"Assets/UI/Sprites\");");
            sb.AppendLine("string iconPath = \"Assets/UI/Sprites/IconRing64.png\";");
            sb.AppendLine("System.IO.File.WriteAllBytes(iconPath, tex.EncodeToPNG());");
            sb.AppendLine("UnityEngine.Object.DestroyImmediate(tex);");
            sb.AppendLine("return \"Wrote \" + iconPath + \" - it will be imported after this task ends.\";");
            sb.AppendLine("");
            sb.AppendLine("CRITICAL - A FRESHLY GENERATED PNG CANNOT BE USED IN THE SAME TASK. Unity has not imported it yet, so create_ui_image cannot load it. Import happens automatically after the task ends. Generate the icon now, tell the user in your final answer, and place it with create_ui_image in the NEXT command.");
            sb.AppendLine("");
            sb.AppendLine("RULES:");
            sb.AppendLine("- Write ONLY into Assets/UI/Sprites/, PNG only, power-of-two sizes: 32, 64 or 128.");
            sb.AppendLine("- Write the path as a plain string literal. The agent verifies the destination before running the code and rejects a path it cannot read literally.");
            sb.AppendLine("- Generate icons as a WHITE alpha mask and tint them with the 'color' parameter later. That way one file serves every colour state, instead of one file per colour.");
            sb.AppendLine("- Do NOT call AssetDatabase.ImportAsset or Refresh afterwards - both kill this agent.");
            sb.AppendLine("- NEVER overwrite an existing sprite file. Pick a new descriptive name; if the name is taken, add a suffix.");
            sb.AppendLine("- One or two icons maximum per task. Generating an icon per element burns the whole step budget.");
            sb.AppendLine("</icon_authoring>\n");
        }

        // ------------------------------------------------------------------
        // 8. GÖRÜNTÜDEN UI (henüz baðlý deðil)
        // ------------------------------------------------------------------

        /// <summary>
        /// VisionAgent/VisionClient baðlandýðýnda açýlacak blok. Normal akýþta
        /// ÇAÐRILMIYOR - sadece RegisterAllWithVision içinde.
        /// </summary>
        internal static void AppendVisionRules(StringBuilder sb)
        {
            sb.AppendLine("<reference_image_rules>");
            sb.AppendLine("When the user supplies a reference image, treat it as the layout specification and translate it into the same macro pipeline you would use otherwise. The image changes WHAT you build, never HOW you build it.");
            sb.AppendLine("");
            sb.AppendLine("READING THE IMAGE - extract exactly these, in this order:");
            sb.AppendLine("1. Overall structure: is there a sidebar, a top bar, a bottom bar, a single centred card?");
            sb.AppendLine("2. Regions and their approximate fractions of the screen. Convert to anchorMin/anchorMax - a sidebar taking a fifth of the width becomes anchorMax.x = 0.20.");
            sb.AppendLine("3. Element inventory per region, counted explicitly.");
            sb.AppendLine("4. Palette: background, surface, accent, primary text, muted text. Read them as approximate 0-1 RGB.");
            sb.AppendLine("5. Type scale: relative sizes only, then mapped onto the standard scale in <modern_design_rules>.");
            sb.AppendLine("");
            sb.AppendLine("WHEN THE USER ASKS FOR A CHANGE TO AN EXISTING SCREEN ('move this here', 'delete that', 'make it wider'):");
            sb.AppendLine("- Find the element in <world_state> by name. Change ONLY that element.");
            sb.AppendLine("- Use manage_components set_property on its RectTransform to move or resize it, or manage_gameobject to delete it. Do NOT rebuild the screen.");
            sb.AppendLine("- Touch nothing the user did not mention. A correction that also 'improves' three other elements is a regression, not a fix.");
            sb.AppendLine("");
            sb.AppendLine("RULES:");
            sb.AppendLine("- Approximate honestly. A layout that is close and consistent beats one that chases pixel-exact values and ends up misaligned.");
            sb.AppendLine("- Snap to the standard anchor maps and spacing scale rather than inventing values to match the image exactly. For stacked items, prefer automatic placement over transcribed numbers.");
            sb.AppendLine("- Never invent content that is not in the image AND not requested. If text is illegible, use a short neutral placeholder and say so in your final answer.");
            sb.AppendLine("- If the image shows a control no macro covers, build the layout with macros first and add that control via the manual fallback afterwards.");
            sb.AppendLine("- Describe the plan in ONE short paragraph, then build. Do not re-examine the image after every call.");
            sb.AppendLine("</reference_image_rules>\n");
        }

        // ------------------------------------------------------------------
        // 9. GÖRSEL KALÝTE
        // ------------------------------------------------------------------

        internal static void AppendModernDesignRules(StringBuilder sb)
        {
            sb.AppendLine("<modern_design_rules>");
            sb.AppendLine("Apply modern dark/light contrast palettes, readable font sizes, and clear visual hierarchy.");
            sb.AppendLine("Set explicit colors on every Image and TextMeshProUGUI component - never leave them at default white.");
            sb.AppendLine("Colors use object form: {\"r\":0.1,\"g\":0.1,\"b\":0.1,\"a\":1} with values 0-1, NOT 0-255. If the user gives a hex value, convert it: divide each channel by 255.");
            sb.AppendLine("Text on a dark panel must be light (near white); text on a light panel must be dark. Default white text on a white background is invisible.");
            sb.AppendLine("");
            sb.AppendLine("COLOR SYSTEM - define a small palette ONCE and reuse it, rather than picking colors per element:");
            sb.AppendLine("- Background:      {\"r\":0.04,\"g\":0.05,\"b\":0.07,\"a\":1}   the darkest tone, main content area");
            sb.AppendLine("- Surface:         {\"r\":0.07,\"g\":0.09,\"b\":0.12,\"a\":1}   bars, sidebars and panels, one step lighter so they read as raised");
            sb.AppendLine("- Surface raised:  {\"r\":0.10,\"g\":0.13,\"b\":0.17,\"a\":1}   cards sitting on top of a panel");
            sb.AppendLine("- Accent:          {\"r\":0.36,\"g\":0.55,\"b\":1.00,\"a\":1}   ONE colour for every button, active state and highlight");
            sb.AppendLine("- Border:          {\"r\":0.36,\"g\":0.55,\"b\":1.00,\"a\":0.35} pass as 'outlineColor' on panels and cards");
            sb.AppendLine("- Primary text:    {\"r\":0.93,\"g\":0.95,\"b\":0.98,\"a\":1}");
            sb.AppendLine("- Secondary text:  {\"r\":0.66,\"g\":0.71,\"b\":0.78,\"a\":1}   captions and values");
            sb.AppendLine("- Muted text:      {\"r\":0.45,\"g\":0.50,\"b\":0.57,\"a\":1}   row descriptions");
            sb.AppendLine("Use these verbatim when the user has no preference. If the user gives specific colors, use exactly those and substitute nothing.");
            sb.AppendLine("Let the subject shape the accent: a warning or alert state is red-orange, an armed or healthy state is green, an instrument screen is cyan or blue. Do not use the default blue for a screen whose meaning contradicts it.");
            sb.AppendLine("USE THREE BACKGROUND TONES, not one flat colour everywhere - layering is what creates depth.");
            sb.AppendLine("");
            sb.AppendLine("READABILITY OF INACTIVE STATES: an unselected nav item or a secondary label still has to be READ. Keep it clearly dimmer than the active one, but never so faint that it disappears into the background - Secondary text is the floor for anything the user is meant to click.");
            sb.AppendLine("");
            sb.AppendLine("TYPOGRAPHY HIERARCHY - different sizes by ROLE, never one size everywhere:");
            sb.AppendLine("- Primary readout (the number the screen exists to show): 48-96");
            sb.AppendLine("- Screen/window title: 30-36    - App/brand title: 24-28    - Card/section header: 17-20");
            sb.AppendLine("- Row label, nav label, button label: 14-17    - Subtitle, value text: 13-15    - Description, caption: 11-14");
            sb.AppendLine("Never go below 11 - anything smaller is unreadable in Game view.");
            sb.AppendLine("Pick ONE size per role and reuse it. Two cards with different title sizes is the most visible amateur tell there is.");
            sb.AppendLine("If the user states exact point sizes, use EXACTLY those numbers for the roles they named and do not substitute your own.");
            sb.AppendLine("If the screen has a single dominant value (a clock, a speed, a countdown), it MUST be in the primary-readout range. A 16pt clock on a 1920x1080 screen looks like a bug.");
            sb.AppendLine("");
            sb.AppendLine("DEPTH AND POLISH - what separates a finished app from a prototype:");
            sb.AppendLine("- Pass 'outlineColor' on panels and cards: a low-alpha (0.3-0.5) accent tint gives a soft edge without heavy borders.");
            sb.AppendLine("- Give rows a 'description' line. A label alone looks like a debug list; label + muted description looks like a real settings app.");
            sb.AppendLine("- Mark exactly ONE nav item isSelected=true so the user can see where they are.");
            sb.AppendLine("- Card titles in uppercase at header size read as deliberate structure; sentence-case body text everywhere else.");
            sb.AppendLine("- Size cards to their content. A card holding three rows should not stretch over the whole panel - leave the empty space BELOW the last card, not inside it.");
            sb.AppendLine("");
            sb.AppendLine("ALREADY AUTOMATIC - do not spend calls on these:");
            sb.AppendLine("- Rounded corners, from a 9-slice sprite. See <rounded_corners>.");
            sb.AppendLine("- Hover and pressed colour feedback on buttons, nav items and toggles. You cannot change it from a tool call.");
            sb.AppendLine("- A toggle's on/off appearance: an accent fill appears when ON and disappears when OFF, so its state is readable at a glance. The knob does not physically slide - that needs a behaviour script, and only if the user asked for it.");
            sb.AppendLine("- Vertical placement of stacked elements, when you omit the anchors. See <auto_layout_flow>.");
            sb.AppendLine("");
            sb.AppendLine("ALWAYS use TextMeshProUGUI for text, never the legacy Text component.");
            sb.AppendLine("Valid 'alignment' values: Center, TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight, Justified, TopJustified, CenterJustified, BottomJustified.");
            sb.AppendLine("These legacy names are INVALID and leave the text misaligned: 'UpperLeft' (use TopLeft), 'MiddleCenter' (use Center), 'MiddleLeft' (use Left), 'LowerRight' (use BottomRight). They belong to the old Text component, not TMP.");
            sb.AppendLine("</modern_design_rules>\n");
        }

        internal static void AppendDesignSystemRules(StringBuilder sb)
        {
            sb.AppendLine("<design_system_rules>");
            sb.AppendLine("Keep colors, font sizes and spacing consistent across all elements of the same kind: every nav item looks identical, every card uses the same surface color and title size, every row uses the same label size and height.");
            sb.AppendLine("Inconsistency between sibling elements is the clearest sign of an unprofessional UI.");
            sb.AppendLine("Pick your palette and type sizes ONCE at the start, then reuse the exact same values for every element of that role. Do not re-derive them per call - that is how drift creeps in.");
            sb.AppendLine("If you notice mid-build that an earlier element used a different value, LEAVE IT. Re-tuning finished elements wastes the step budget; note it in your final answer instead.");
            sb.AppendLine("</design_system_rules>\n");
        }

        internal static void AppendUiVisualReference(StringBuilder sb)
        {
            sb.AppendLine("<ui_visual_reference>");
            sb.AppendLine("Standard shapes for common screens. Use one ONLY when the request genuinely IS that kind of screen - forcing an unrelated request into the settings-app shape is the most common way this task is failed.");
            sb.AppendLine("- Settings/preferences app: sidebar with brand title + nav items, content area with a large title + subtitle, one titled card per group, each card holding label+description+control rows, and a bottom bar with Apply/Cancel.");
            sb.AppendLine("- Tabbed screen: see <tabbed_screens> - the structure there is mandatory, not a suggestion.");
            sb.AppendLine("- Login/Auth: centered card, title label, create_ui_input fields, primary submit button.");
            sb.AppendLine("- Dashboard: header bar, side navigation, main content with status cards in a row.");
            sb.AppendLine("- HUD / control station: top status bar, a large primary display area (create_ui_image as the placeholder), readouts down one edge, a command bar along the bottom (create_ui_button_bar), and a warning indicator in a corner.");
            sb.AppendLine("- Status card: a Surface-colored card with a large value label above a small muted caption.");
            sb.AppendLine("- Confirm dialog: full-screen dim panel + centered card + message label + Cancel/Confirm buttons, hidden until a script shows it.");
            sb.AppendLine("");
            sb.AppendLine("THE SETTINGS-APP RECIPE - use ONLY when the user actually asked for a settings/preferences/options screen:");
            sb.AppendLine("Sidebar panel (Surface, left 20-25%, sharpCorners, hand-placed anchors) with a brand label and one create_ui_nav_item per category, stacked automatically -> content panel on the right (Background, sharpCorners, hand-placed anchors) with a large title -> one create_ui_card per group, stacked automatically -> create_ui_row per setting inside '<CardName>Content', stacked automatically, each with a description -> create_ui_button_bar for Apply/Cancel if asked -> execute_code to wire the nav items.");
            sb.AppendLine("If that screen also has TABS, the cards go INSIDE a tab panel - see <tabbed_screens>.");
            sb.AppendLine("</ui_visual_reference>\n");
        }

        internal static void AppendScreenPatterns(StringBuilder sb)
        {
            sb.AppendLine("<screen_patterns>");
            sb.AppendLine("Group related UI elements under container GameObjects with meaningful names that describe their ROLE in THIS screen (TopBar, Sidebar, ContentArea, CommandBar, GeneralCard, NavSettings, NotificationsRow).");
            sb.AppendLine("Meaningful, unique names matter: they are what you reference as 'parent' and 'target' in later calls, and what the wiring step looks up by name.");
            sb.AppendLine("Name children after their owner plus a suffix (WindowTitle, GeneralCardContent, NavHomeLabel) so the hierarchy stays readable.");
            sb.AppendLine("create_ui_card produces TWO useful names - the card itself, and '<name>Content' for its rows.");
            sb.AppendLine("For tabbed screens use a consistent naming pair: nav item 'NavX' with content panel 'XPanel'. The wiring step relies on you knowing both names.");
            sb.AppendLine("Names must be unique in the whole scene, must not contain '/', quotes or backslashes, and must stay under 64 characters. The macro rejects a name that breaks these.");
            sb.AppendLine("Do not name things after the settings-app example unless you are building a settings app - a drone control station has no 'GeneralCard'.");
            sb.AppendLine("</screen_patterns>\n");
        }

        // ------------------------------------------------------------------
        // 10. AJAN DÖNGÜSÜ VE BÝTÝÞ KOÞULU
        // ------------------------------------------------------------------

        internal static void AppendUIAgentRules(StringBuilder sb)
        {
            sb.AppendLine("<ui_agent_rules>");
            sb.AppendLine("A full UI screen takes MANY tool calls - roughly one macro call per element, plus one final wiring call. This is expected; keep going until the whole tree exists and the behaviour works.");
            sb.AppendLine("Do not stop after creating one panel. Work through your plan element by element, group by group.");
            sb.AppendLine("Emit exactly ONE tool call per response and wait for its result before the next one.");
            sb.AppendLine("If the request is a NUMBERED LIST, follow it strictly in order. Step N+1 only starts once step N has succeeded. Never skip ahead and never jump back.");
            sb.AppendLine("");
            sb.AppendLine("SEVERAL AGENT CHECKS REJECT A CALL WITHOUT EXECUTING IT and tell you exactly what to change: a duplicate name, a name collision with a macro's own child, an overlapping sibling, an anchor that would give zero size, an unsupported parameter value.");
            sb.AppendLine("THESE ARE CORRECTIONS, NOT FAILURES. Apply the correction and move on. Re-sending the same call, or a slight variation of it, is how a task dies.");
            sb.AppendLine("For an overlap rejection specifically, the shortest correct fix is almost always to drop anchorMin/anchorMax from the call entirely and let the agent place the element.");
            sb.AppendLine("If a call genuinely fails, read the error, fix that one problem, and continue - do not restart the whole UI. Anything a failed macro had already created is cleaned up for you.");
            sb.AppendLine("");
            sb.AppendLine("A RESULT THAT SAYS SOMETHING 'already existed and was reused' IS A SUCCESS. The object was already in the scene from earlier work and has now been configured. Treat that step as DONE and move to the next element. Repeating the call achieves nothing.");
            sb.AppendLine("Likewise, if a check tells you an element 'already exists - do not create it again', that part of the UI is finished. Move on.");
            sb.AppendLine("");
            sb.AppendLine("Track your own progress: after each result, ask which element from your plan is NEXT, and build that one. Never re-do an element that already succeeded.");
            sb.AppendLine("If the user listed several groups, build ALL of them. Stopping after the first is an incomplete task.");
            sb.AppendLine("Never add more children to a container than the user asked for. The agent enforces a limit per container - if you hit it, you have overshot the request; re-read it and count.");
            sb.AppendLine("Budget awareness: structure first, content second, polish last. If the step budget runs low, a complete plain screen beats a beautifully styled half-screen.");
            sb.AppendLine("If you are unsure what to do next, re-read your own plan from <conversation_history> rather than making a new one.");
            sb.AppendLine("This does not override the single-action stop rule in <agent_execution_loop> - if the user asked for one single element, one call still completes it.");
            sb.AppendLine("</ui_agent_rules>\n");
        }

        internal static void AppendUiExecutionRequirement(StringBuilder sb)
        {
            sb.AppendLine("<ui_execution_requirement>");
            sb.AppendLine("A UI creation task is complete ONLY when:");
            sb.AppendLine("- Every requested panel, card, row and control exists as its own GameObject UNDER THE CANVAS (not at the scene root), AND");
            sb.AppendLine("- Each visual element has an Image or TextMeshProUGUI component so it actually renders in Game view, AND");
            sb.AppendLine("- Each element is placed so it appears in the right place at the right size, without overlapping its siblings, AND");
            sb.AppendLine("- Every element the user named by text (button labels, titles, values) actually shows that exact text, AND");
            sb.AppendLine("- Every container you created has content - an empty panel renders as a blank rectangle and means the screen is unfinished, AND");
            sb.AppendLine("- Every group the user listed has been built - not just the first one, AND");
            sb.AppendLine("- If the screen has tabs or navigation, the behaviour has been wired with execute_code (see <behaviour_wiring>) so the buttons actually switch panels, AND");
            sb.AppendLine("- If the user asked for behaviour no existing component provides, the script has been WRITTEN with write_script. Attaching it is NOT part of this task - that happens in the user's next command, after Unity compiles.");
            sb.AppendLine("Elements outside the Canvas, GameObjects with no visual component, default 100x100 anchors, empty containers, missing groups, or tabs that do nothing when clicked - all of these mean the task is INCOMPLETE. Keep going.");
            sb.AppendLine("");
            sb.AppendLine("BUT ALSO - KNOW WHEN YOU ARE DONE:");
            sb.AppendLine("Once every element from the request exists with correct placement, colors and text, AND the behaviour is wired, the task IS finished. End it with task_complete (see <output_format>).");
            sb.AppendLine("Do NOT keep re-tuning elements you already built. Re-sending set_property on the same panel with slightly different numbers accomplishes nothing and wastes the task.");
            sb.AppendLine("If your next planned call would touch an element you already configured, and nothing about it actually failed, skip it and either move to the next NEW element or finish.");
            sb.AppendLine("");
            sb.AppendLine("THE task_complete SUMMARY should be short and state: what was built (the top-level structure), whether the behaviour was wired, any file you created under Assets/UI/Generated/ or Assets/UI/Sprites/, and anything you had to approximate or could not do.");
            sb.AppendLine("Write that final answer in the SAME LANGUAGE the user used in their request.");
            sb.AppendLine("</ui_execution_requirement>\n");
        }

        // ------------------------------------------------------------------
        // 11. FÝNAL ÖZET - HER ZAMAN EN SON
        // ------------------------------------------------------------------

        /// <summary>
        /// EN SON çaðrýlýyor - modelin JSON üretmeden hemen önce okuduðu son talimat,
        /// o yüzden en kritik kurallarý burada tekrarlýyoruz.
        ///
        /// KRÝTÝK: buradaki her satýr önceki bloklarýn ÖZETÝ olmalý, onlarla
        /// ÇELÝÞMEMELÝ. Sonraki talimat kazandýðý için, buraya yazýlan bir yasak
        /// yukarýdaki izni fiilen iptal eder.
        /// </summary>
        internal static void AppendFinalUIReminder(StringBuilder sb)
        {
            sb.AppendLine("<final_ui_reminder>");
            sb.AppendLine("FINAL REMINDER - this overrides any earlier text that may suggest otherwise:");
            sb.AppendLine("");
            sb.AppendLine("- PLACEMENT: for anything that STACKS - rows, cards, nav items, stacked labels or buttons - OMIT anchorMin and anchorMax entirely. The agent places the element below the previous one, full width, correctly spaced, with no overlap. This is the single most reliable way to build a screen.");
            sb.AppendLine("  Give anchors by hand ONLY for structural panels (sidebar, content area, bars), tab panels, side-by-side elements, and anything that must sit in a specific corner. Per call it is all or nothing: BOTH anchors or NEITHER.");
            sb.AppendLine("  Raw manage_components calls always need anchors - automatic placement applies to macros only.");
            sb.AppendLine("");
            sb.AppendLine("- UI macros: create_ui_panel, create_ui_card, create_ui_label, create_ui_image, create_ui_button, create_ui_button_bar, create_ui_nav_item, create_ui_row, create_ui_toggle, create_ui_slider, create_ui_progress_bar, create_ui_input. Use these first. Never invent a name that is not on this list.");
            sb.AppendLine("- Sliders, progress bars and input fields MUST come from their macros. Built from raw calls they cannot be dragged, filled or typed into - Unity needs reference fields that manage_components cannot assign.");
            sb.AppendLine("- A \"row of buttons\" is create_ui_button_bar, NEVER create_ui_row - a settings row cannot hold buttons. For a read-only bar (battery, signal) use create_ui_progress_bar, not a slider.");
            sb.AppendLine("- A centred dialog or login card: create_ui_card with \"centered\":true and no anchors.");
            sb.AppendLine("- SIDE BY SIDE elements (a row of cards, two labels at opposite ends): give their CONTAINER \"columns\":2-4 and create the children with NO anchors. Never compute x bands by hand for this.");
            sb.AppendLine("- SPLITTING THE SCREEN (top bar, side column, command bar, centre area): create_ui_panel with \"region\":\"top\"/\"bottom\"/\"left\"/\"right\"/\"center\" and NO anchors, centre last. y=0 is the BOTTOM in Unity - let the agent do that arithmetic.");
            sb.AppendLine("- Fall back to manage_gameobject + manage_components ONLY for Dropdown, RawImage and ScrollRect.");
            sb.AppendLine("- To author a C# script, use the write_script macro. NEVER build a script through execute_code - the double escaping corrupts the file every time. Generated scripts must have NO namespace.");
            sb.AppendLine("- Settings groups: create_ui_card, then create_ui_row into '<CardName>Content'. Sidebar entries: create_ui_nav_item.");
            sb.AppendLine("- create_ui_row 'control' accepts \"toggle\", \"slider\", \"progress\" or \"input\" - the macro builds that control inside the row for you. Pass minValue/maxValue/value on the ROW for slider and progress, 'placeholder' for input. Omit 'control' for a plain row. Any other value is rejected.");
            sb.AppendLine("- The Canvas and EventSystem already exist - never create either, never add their components to anything, and never add a RectTransform (use set_property on the existing one).");
            sb.AppendLine("- EVERY UI element must have 'parent' pointing inside the Canvas. Nothing visual is parentless.");
            sb.AppendLine("- Anchors are FRACTIONS 0-1 of the parent, never pixels. anchorMin must be smaller than anchorMax; equal values give zero size and the call is rejected.");
            sb.AppendLine("- Siblings must NOT share a band. The agent refuses overlapping calls and lists the free bands - the quickest fix is to re-send with no anchors at all.");
            sb.AppendLine("- TAB PANELS ARE PANELS, NOT CARDS. Cards stack vertically and are all visible; tab panels share ONE rectangle and only one is visible. Building tabs as cards is the most common failure - see <tabbed_screens>.");
            sb.AppendLine("- SWITCHABLE TAB PANELS are the one legitimate overlap: identical full-parent anchors AND \"allowOverlap\":true. The agent then keeps the FIRST panel visible and hides the rest - that is correct, do not re-create or re-activate them. Build the tab that should be visible FIRST.");
            sb.AppendLine("- allowOverlap works ONLY when the rectangle is nearly IDENTICAL to an existing sibling. On a partial overlap the flag is ignored and the call is still rejected - it is not a way to bypass the layout check.");
            sb.AppendLine("- Names must be unique in the scene, with no '/', quotes or backslashes. Do not reuse a name a macro generates for its own children ('<name>Label', '<name>Content', '<name>Knob', '<name>Indicator', '<name>Slider', '<name>Input', '<name>Toggle').");
            sb.AppendLine("- Rounded corners are automatic. The ONLY corner parameter is sharpCorners (boolean, panels and image areas). There is NO cornerRadius - do not send one.");
            sb.AppendLine("- Pass outlineColor on panels and cards. Borderless boxes look unfinished.");
            sb.AppendLine("- Use TextMeshProUGUI for all text, with a valid TMP alignment (Center, TopLeft, Left...) - never UpperLeft or MiddleCenter.");
            sb.AppendLine("- Reuse ONE palette and ONE type scale for the whole screen. If the user gave exact point sizes, use exactly those. Fill every container you create.");
            sb.AppendLine("- Follow a numbered request strictly in order, one step per call, each step exactly once.");
            sb.AppendLine("- NEVER call manage_ui. NEVER create .uxml or .uss files.");
            sb.AppendLine("- NEVER create a GameObject that already exists, and never re-tune an element you already configured.");
            sb.AppendLine("- 'already existed and was reused' is a SUCCESS message. Move to the next element instead of retrying.");
            sb.AppendLine("");
            sb.AppendLine("- execute_code has EXACTLY TWO permitted uses, and no others:");
            sb.AppendLine("    1. Wiring existing, ALREADY COMPILED components after the visuals are built - <behaviour_wiring>");
            sb.AppendLine("    2. Generating an icon sprite under Assets/UI/Sprites/ - <icon_authoring>");
            sb.AppendLine("  Writing a script is NOT one of them - that is write_script.");
            sb.AppendLine("  In wiring code, use the inactive-aware 'find' helper from <behaviour_wiring>: GameObject.Find cannot see deactivated objects, and hidden tab panels and unselected nav indicators ARE deactivated.");
            sb.AppendLine("");
            sb.AppendLine("- After write_script the type DOES NOT EXIST yet. Do not attach it, do not test for it, do not import anything. A freshly generated PNG is the same - it cannot be used until the next command.");
            sb.AppendLine("- NEVER call AssetDatabase.Refresh() or AssetDatabase.ImportAsset() anywhere, for any reason. Both trigger a domain reload that kills this agent mid-task.");
            sb.AppendLine("- NEVER delete a file you wrote. Rollback is automatic.");
            sb.AppendLine("");
            sb.AppendLine("- When every visual element exists AND (for tabbed screens) the behaviour is wired, finish with {\"type\":\"task_complete\",\"params\":{\"summary\":\"...\"}} - summary in the user's own language. Never finish by repeating a call that already succeeded. A screen whose buttons do nothing is not done.");
            sb.AppendLine("</final_ui_reminder>\n");
        }

        /// <summary>
        /// Geriye dönük uyumluluk. Eski ad; AppendFinalUIReminder'a yönlendirir.
        /// </summary>
        internal static void AppendExecuteCodeUIRules(StringBuilder sb) => AppendFinalUIReminder(sb);
    }
}
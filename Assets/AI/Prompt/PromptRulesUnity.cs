using System.Text;

namespace MCPForUnity.Editor.AI
{
    /// <summary>
    /// Unity/MCP specific rules: tool selection strategy, primitive object
    /// creation, materials, schema authority/validation, output format,
    /// and vector formatting. Also owns scene context injection and
    /// tool-result reasoning.
    ///
    /// UI-specific rules live in PromptRulesUI - this file only points at it.
    ///
    /// ---------------------------------------------------------------------------
    /// BU SÜRÜMDE DÜZELTÝLEN ÇELÝÞKÝ - EN CÝDDÝSÝ
    ///
    /// AppendToolSelectionRules ve AppendToolPriority, UI'nin nasýl kurulacaðýný
    /// anlatýrken MACRO'LARDAN HÝÇ BAHSETMÝYORDU:
    ///
    ///   "Build UI as UGUI GameObjects using manage_gameobject + manage_components"
    ///   "manage_gameobject creates EVERY UI GameObject"
    ///   "These two tools together ARE the UI creation path"
    ///
    /// Oysa PromptRulesUI ayný promptta "macro'lar birincil yol, ham çaðrý sadece
    /// Dropdown/RawImage/ScrollRect için" diyor. Ýki blok da ZORUNLU, yani model her
    /// adýmda birbirini iptal eden iki talimat okuyordu.
    ///
    /// Çeliþkide model genellikle EN KESÝN ifadeyi seçer ve "ARE the UI creation
    /// path" son derece kesin bir cümle. Bu, modelin macro yerine ham çaðrýya
    /// kaymasýnýn doðrudan sebebi - ve ham çaðrýda otomatik yerleþim, çakýþma
    /// denetimi ve slider/input baðlama hiç çalýþmýyor.
    ///
    /// Artýk iki blok da macro'larý birincil yol olarak söylüyor ve ayrýntý için
    /// PromptRulesUI'ye yönlendiriyor. Ayrýca ikisi büyük ölçüde ayný þeyi
    /// tekrarladýðý için kýsaltýldýlar - kazanýlan yer, hiç gönderilemeyen ekran
    /// bölgesi haritalarýna gitti.
    ///
    /// DÝÐER DÜZELTMELER:
    ///
    /// A. AppendProductionAndUIFrame "set RectTransform anchors on EVERY element"
    ///    diyordu. Otomatik dikey akýþla çeliþiyor: macro'da anchor'ý OMIT etmek
    ///    artýk tercih edilen yol. Yeniden yazýldý.
    ///
    /// B. AppendVectorRulesIfRelevant'ýn 'toolSchema' parametresi HÝÇ
    ///    KULLANILMIYORDU - adý "IfRelevant" olmasýna raðmen hiçbir koþul yoktu.
    ///    Parametre korundu (çaðýran imzayý deðiþtirmemek için) ama artýk gerçekten
    ///    kullanýlýyor: þema vector alan adý içermiyorsa blok kýsa sürümüne iniyor.
    ///
    /// C. Yorumlardaki bozuk Türkçe karakterler ("DE   T :", "Art k", "EKLEND")
    ///    düzeltildi. Kod davranýþýný etkilemiyordu ama dosyayý okunmaz yapýyordu.
    ///
    /// D. BÝTÝÞ TALÝMATI UYGULANAMAZDI. Onlarca kural "iþ bitince düz metinle cevap
    ///    ver" diyordu, ama her araç adýmý forceJson ile çaðrýlýyor ve Ollama çýktýyý
    ///    JSON'a kilitliyor - model düz metin yazamaz. Bitirmek isteyen model son
    ///    çaðrýyý tekrarlýyor, koruma engelliyor, görev "Stuck" ile bitiyordu.
    ///    AppendFormatRules artýk tüm bu talimatlarý tek bir JSON bitiþ çaðrýsýna
    ///    eþliyor: task_complete.
    /// </summary>
    internal static class PromptRulesUnity
    {
        /// <summary>
        /// Araç seçim algoritmasý.
        ///
        /// DEÐÝÞTÝ: eskiden "manage_ui (UI Toolkit) is the ONLY UI creation tool"
        /// diyordu - PromptRulesUI'nin tam tersi. Sonra UGUI'ye çevrildi ama bu sefer
        /// de yalnýzca HAM çaðrýlarý söylüyordu; macro'lar yine yok sayýlýyordu.
        /// Artýk üçü de ayný þeyi söylüyor.
        /// </summary>
        internal static void AppendToolSelectionRules(StringBuilder sb)
        {
            sb.AppendLine("<tool_selection_rules>");
            sb.AppendLine("HOW TO PICK A TOOL:");
            sb.AppendLine("1. Understand the desired outcome, not just the keywords.");
            sb.AppendLine("2. Identify the capability it needs.");
            sb.AppendLine("3. Find the tool in <available_tools> whose description and schema match that capability.");
            sb.AppendLine("Never pick a tool because its NAME resembles a word in the request.");
            sb.AppendLine("");
            sb.AppendLine("- Prefer a specialized tool over a generic one.");
            sb.AppendLine("- Never rebuild with low-level components what a higher-level tool already does.");
            sb.AppendLine("- Never build primitives from MeshFilter/MeshRenderer when primitive creation exists.");
            sb.AppendLine("");
            sb.AppendLine("UI TOOL SELECTION:");
            sb.AppendLine("The UI macros (create_ui_*) are the PRIMARY way to build UI - one call per element, with anchors, component order, automatic placement and overlap checking already handled.");
            sb.AppendLine("manage_gameobject + manage_components are the RAW FALLBACK, used only for the few elements no macro covers (Dropdown, RawImage, ScrollRect).");
            sb.AppendLine("A slider, progress bar or input field built from raw calls LOOKS right but does not work - see <ui_macro_preference>.");
            sb.AppendLine("NEVER use manage_ui. NEVER create .uxml or .uss files. execute_code is only for wiring compiled components and generating icons.");
            sb.AppendLine("The exact call sequence is in <ui_generation_pipeline>.");
            sb.AppendLine("</tool_selection_rules>\n");
        }

        /// <summary>
        /// Öncelik sýrasý.
        ///
        /// DEÐÝÞTÝ: eskiden "manage_ui IS the default and only UI creation tool"
        /// diyordu; sonra "manage_gameobject creates EVERY UI GameObject" oldu - ikisi
        /// de macro katmanýný yok sayýyordu. Bu blok her adýmda gönderildiði için
        /// yanlýþ bilgi her adýmda tekrarlanýyordu.
        /// </summary>
        internal static void AppendToolPriority(StringBuilder sb)
        {
            sb.AppendLine("<tool_priority>");
            sb.AppendLine("Priority order:");
            sb.AppendLine("1. A UI macro (create_ui_*), for anything visual.");
            sb.AppendLine("2. A specialized MCP tool for the capability.");
            sb.AppendLine("3. manage_gameobject / manage_components, as the raw fallback.");
            sb.AppendLine("4. execute_code, ONLY for wiring compiled components or generating icons.");
            sb.AppendLine("manage_ui is not used in this project at all.");
            sb.AppendLine("</tool_priority>\n");
        }

        internal static void AppendPrimitiveRules(StringBuilder sb)
        {
            sb.AppendLine("<unity_primitive_rules>");
            sb.AppendLine("UNITY PRIMITIVE CREATION RULES:");
            sb.AppendLine("Primitive objects are special Unity objects.");
            sb.AppendLine("Never create primitives using MeshFilter + MeshRenderer manually.");
            sb.AppendLine("Never approximate primitive creation with components.");
            sb.AppendLine("When user requests:");
            sb.AppendLine("- Cube -> use cube primitive capability");
            sb.AppendLine("- Sphere -> use sphere primitive capability");
            sb.AppendLine("- Capsule -> use capsule primitive capability");
            sb.AppendLine("- Cylinder -> use cylinder primitive capability");
            sb.AppendLine("- Plane -> use plane primitive capability");
            sb.AppendLine("Object name does not define geometry.");
            sb.AppendLine("A GameObject named Sphere is NOT a sphere.");
            sb.AppendLine("The selected MCP capability must create the correct geometry.");
            sb.AppendLine("");
            // EKLENDÝ: 'primitive_type' yalnýzca 3D içindir - model UI için de
            // denemesin. Gerçek testte 'primitive_type':'Canvas' gönderdi ve çaðrý
            // boþa gitti.
            sb.AppendLine("IMPORTANT: 'primitive_type' only supports 3D shapes (Cube, Sphere, Capsule, Cylinder, Plane, Quad).");
            sb.AppendLine("There is NO primitive_type for Canvas, Panel, Button or Text. UI elements come from the UI macros - see <ui_generation_pipeline>.");
            sb.AppendLine("</unity_primitive_rules>\n");
        }

        internal static void AppendPrimitiveParameterRules(StringBuilder sb)
        {
            sb.AppendLine("<primitive_parameter_rules>");
            sb.AppendLine("When creating Unity primitives:");
            sb.AppendLine("Cube:   {\"name\":\"Cube\",\"primitive_type\":\"Cube\"}");
            sb.AppendLine("Sphere: {\"name\":\"Sphere\",\"primitive_type\":\"Sphere\"}");
            sb.AppendLine("Object name NEVER defines primitive type.");
            sb.AppendLine("");
            sb.AppendLine("PARENT / ROOT PLACEMENT RULE (applies to ANY GameObject creation):");
            sb.AppendLine("When the new GameObject should sit at the scene root, OMIT the 'parent' field entirely.");
            sb.AppendLine("NEVER pass an empty string for 'parent'. The resolver then looks for a GameObject literally named '' and fails with: Parent specified ('') but not found.");
            sb.AppendLine("Only include 'parent' when you have a real, already-existing GameObject name.");
            sb.AppendLine("For UI elements the opposite applies: they MUST have a parent inside the Canvas, otherwise they are invisible.");
            sb.AppendLine("</primitive_parameter_rules>\n");
        }

        internal static void AppendMaterialCapabilityRules(StringBuilder sb)
        {
            sb.AppendLine("<material_rules>");
            sb.AppendLine("MATERIAL AND COLOR OPERATIONS - 3D SCENE OBJECTS ONLY:");
            sb.AppendLine("To change the colour or material of a 3D object, use manage_material when available. Never use manage_gameobject for colour edits.");
            sb.AppendLine("");
            sb.AppendLine("UI colours are NOT materials. Pass 'color' directly to the UI macro that creates the element; for a raw element, set the 'color' property on its Image or TextMeshProUGUI via manage_components.");
            sb.AppendLine("</material_rules>\n");
        }

        internal static void AppendSchemaAuthority(StringBuilder sb)
        {
            sb.AppendLine("<schema_authority>");
            sb.AppendLine("The tool schema in <available_tools> is the ABSOLUTE source of truth.");
            sb.AppendLine("- Never invoke a tool name that does not exist there.");
            sb.AppendLine("- Never add properties the target tool's schema does not declare.");
            sb.AppendLine("- Always supply the exact required fields with the exact declared types.");
            sb.AppendLine("- A parameter marked optional may be omitted. Omitting anchorMin/anchorMax on a macro is not an oversight - it requests automatic placement.");
            sb.AppendLine("</schema_authority>\n");
        }

        internal static void AppendUnityApiProhibition(StringBuilder sb)
        {
            sb.AppendLine("<unity_api_prohibition>");
            sb.AppendLine("Never generate plain Unity C# code snippets unless invoking execute_code.");
            sb.AppendLine("Only generate valid MCP tool calls.");
            sb.AppendLine("</unity_api_prohibition>\n");
        }

        internal static void AppendScene(StringBuilder sb, string sceneInfo)
        {
            sb.AppendLine("<current_scene>");
            sb.AppendLine(sceneInfo);
            sb.AppendLine("Inspect the existing hierarchy above before creating anything.");
            sb.AppendLine("Reuse compatible objects: if a Canvas or EventSystem already exists, use it instead of creating a second one.");
            sb.AppendLine("Avoid duplicate managers, systems, Canvas, EventSystem or Camera.");
            sb.AppendLine("Prefer modification over recreation.");
            sb.AppendLine("</current_scene>\n");
        }

        internal static void AppendSchemaTools(StringBuilder sb, string schema)
        {
            sb.AppendLine("<available_tools>");
            sb.AppendLine("The following tools are provided directly by Unity MCP.");
            sb.AppendLine("Use ONLY tools existing inside this schema.");
            sb.AppendLine("Do not invent or rename tool names.");
            sb.AppendLine(schema);
            sb.AppendLine("</available_tools>\n");
        }

        internal static void AppendFormatRules(StringBuilder sb)
        {
            sb.AppendLine("<output_format>");
            sb.AppendLine("Emit ONLY raw JSON.");
            sb.AppendLine("Emit exactly ONE tool call per response - never an array, never multiple objects. The loop will ask you again for the next step.");
            sb.AppendLine("No markdown wrapping around the root output unless specified by schema.");
            sb.AppendLine("Required structure:");
            sb.AppendLine("{");
            sb.AppendLine("  \"type\": \"<exact_tool_name>\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine("      \"required_parameter\": \"value\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("HOW TO FINISH - READ THIS: your output is ALWAYS a single JSON object. Plain text is impossible here.");
            sb.AppendLine("So every rule that says 'reply with plain text', 'respond normally', 'answer in natural language' or 'stop' means exactly this, emitted ONCE when every requested part exists:");
            sb.AppendLine("{\"type\":\"task_complete\",\"params\":{\"summary\":\"<2-3 sentences in the user's language: what was built, what was wired, anything missing>\"}}");
            sb.AppendLine("Never end a task by re-sending a call that already succeeded - that is repeating, not finishing, and the agent blocks it.");
            sb.AppendLine("</output_format>\n");
        }

        internal static void AppendValidationRules(StringBuilder sb)
        {
            sb.AppendLine("<validation>");
            sb.AppendLine("Before EVERY tool call:");
            sb.AppendLine("Verify all required parameters exist in the target schema.");
            sb.AppendLine("Never return empty params {} if the schema requires parameters.");
            sb.AppendLine("Never pass an empty string (\"\") for a field that references another object (parent, target, path) - provide a real value or omit the field.");
            sb.AppendLine("Verify that any GameObject you reference as 'parent' or 'target' was actually created successfully in an earlier step.");
            sb.AppendLine("</validation>\n");
        }

        internal static void AppendInvalidOutputs(StringBuilder sb)
        {
            sb.AppendLine("<invalid_outputs>");
            sb.AppendLine("Forbidden outputs:");
            sb.AppendLine("{}");
            sb.AppendLine("{\"type\":\"\"}");
            sb.AppendLine("{\"type\":\"unknown\"}");
            sb.AppendLine("{\"params\":{}}");
            sb.AppendLine("{\"params\":{\"parent\":\"\"}}");
            sb.AppendLine("{\"type\":\"manage_ui\", ...}   (manage_ui is not used in this project)");
            sb.AppendLine("{\"type\":\"manage_components\",\"params\":{\"action\":\"add\",\"componentType\":\"RectTransform\"}}   (RectTransform can never be added)");
            sb.AppendLine("</invalid_outputs>\n");
        }

        internal static void AppendToolResultReasoning(StringBuilder sb)
        {
            sb.AppendLine("<tool_result_reasoning>");
            sb.AppendLine("Tool responses are execution evidence.");
            sb.AppendLine("A response can say \"status\":\"success\" at the outer level while containing \"success\":false inside \"result\" - that is a FAILURE. Read the inner result before deciding.");
            sb.AppendLine("The reverse also matters: a result saying an object 'already existed and was reused' is a SUCCESS. The object was already in the scene and has now been configured. Move on.");
            sb.AppendLine("Never repeat an identical MCP command whose previous execution already succeeded.");
            sb.AppendLine("");
            sb.AppendLine("TASK COMPLETION DETECTION:");
            sb.AppendLine("If the results show every requested object has been created or modified:");
            sb.AppendLine("- Consider the request COMPLETE.");
            sb.AppendLine("- Do NOT issue another tool call, do NOT recreate existing objects, do NOT keep planning.");
            sb.AppendLine("- Return a normal natural language response.");
            sb.AppendLine("</tool_result_reasoning>\n");
        }

        /// <summary>
        /// Vector biçimi kurallarý.
        ///
        /// DÜZELTÝLDÝ - PARAMETRE KULLANILMIYORDU: metodun adý "IfRelevant" ama
        /// 'toolSchema' hiçbir yerde okunmuyordu, yani blok her zaman tam uzunlukta
        /// gönderiliyordu. Artýk þema gerçekten vector alanlarý içeriyorsa tam sürüm,
        /// içermiyorsa yalnýzca tek satýrlýk özet gidiyor.
        /// </summary>
        internal static void AppendVectorRulesIfRelevant(StringBuilder sb, string toolSchema)
        {
            bool schemaMentionsVectors =
                !string.IsNullOrWhiteSpace(toolSchema) &&
                (toolSchema.IndexOf("position", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 toolSchema.IndexOf("rotation", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 toolSchema.IndexOf("scale", System.StringComparison.OrdinalIgnoreCase) >= 0);

            sb.AppendLine("<vector_format>");

            if (!schemaMentionsVectors)
            {
                // Þemada konum/dönüþ/ölçek alaný yoksa uzun anlatýmýn karþýlýðý yok.
                sb.AppendLine("Component properties via manage_components use OBJECT form: {\"anchorMin\":{\"x\":0,\"y\":0},\"color\":{\"r\":0.2,\"g\":0.2,\"b\":0.2,\"a\":1}}.");
                sb.AppendLine("</vector_format>\n");
                return;
            }

            sb.AppendLine("CRITICAL UNITY VECTOR RULE:");
            sb.AppendLine("Vector3 parameters on manage_gameobject (position, rotation, scale) MUST use JSON arrays [x, y, z].");
            sb.AppendLine("Correct: \"position\": [5, 0, 0]");
            sb.AppendLine("Wrong:   \"position\": {\"x\":5, \"y\":0, \"z\":0}");
            sb.AppendLine("");
            sb.AppendLine("EXCEPTION - component properties via manage_components use OBJECT form, matching Unity's own field names:");
            sb.AppendLine("\"properties\": {\"anchorMin\": {\"x\":0, \"y\":0}, \"color\": {\"r\":0.2,\"g\":0.2,\"b\":0.2,\"a\":1}}");
            sb.AppendLine("</vector_format>\n");
        }

        /// <summary>
        /// Üretim standardý.
        ///
        /// DÜZELTÝLDÝ: "set RectTransform anchors on EVERY element" diyordu ve bu
        /// otomatik dikey akýþla doðrudan çeliþiyordu - macro'da anchor'ý OMIT etmek
        /// artýk tercih edilen yol, bir eksiklik deðil.
        /// </summary>
        internal static void AppendProductionAndUIFrame(StringBuilder sb)
        {
            sb.AppendLine("<ui_and_production_standards>");
            sb.AppendLine("Production standard for UGUI:");
            sb.AppendLine("- Build a complete GameObject tree under the Canvas, nested by role rather than flat.");
            sb.AppendLine("- Every element must end up correctly positioned: either from anchors you passed, or from automatic placement when you omitted them. Both are valid; see <auto_layout_flow>.");
            sb.AppendLine("- Empty UI containers with no children and no visual component are strictly forbidden.");
            sb.AppendLine("</ui_and_production_standards>\n");
        }
    }
}
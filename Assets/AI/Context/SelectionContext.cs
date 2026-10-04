using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace AI.Context
{
    /// <summary>
    /// Seçim yakalama sýnýrlarý.
    /// </summary>
    [Serializable]
    public class SelectionCaptureOptions
    {
        /// <summary>Listelenecek çocuk sayýsý tavaný.</summary>
        public int MaxChildren = 20;

        /// <summary>Listelenecek bileþen sayýsý tavaný.</summary>
        public int MaxComponents = 12;

        /// <summary>Çoklu seçimde listelenecek ek obje sayýsý tavaný.</summary>
        public int MaxAdditionalSelected = 10;

        public static SelectionCaptureOptions Default => new SelectionCaptureOptions();
    }

    /// <summary>
    /// Unity Editor seçim bilgisini temsil eder.
    ///
    /// Token baþýna en yüksek sinyali veren context budur: kullanýcý Hierarchy'de bir
    /// objeyi seçmiþse "buna bir buton ekle" komutu üç satýrlýk context ile netleþir,
    /// aksi hâlde onlarca kelimelik tarif gerekir.
    /// </summary>
    [Serializable]
    public class SelectionContext
    {
        // ==============================
        // SELECTION STATE
        // ==============================

        public bool HasSelection;
        public int SelectionCount;

        // ==============================
        // ACTIVE OBJECT
        // ==============================

        public string ActiveObjectName = string.Empty;
        public string ActiveObjectPath = string.Empty;
        public string ActiveObjectTag = string.Empty;
        public string ActiveObjectLayer = string.Empty;

        /// <summary>Aktif objenin tür adý (GameObject, MonoScript, Material, GameObject prefab...).</summary>
        public string ActiveObjectType = string.Empty;

        /// <summary>Sahne içi obje mi (Hierarchy'den seçildi).</summary>
        public bool IsSceneObject;

        /// <summary>Proje varlýðý mý (Project penceresinden seçildi).</summary>
        public bool IsAsset;

        /// <summary>Varlýk seçildiyse dosya yolu. "bu script'i düzelt" komutlarý buna dayanýr.</summary>
        public string AssetPath = string.Empty;

        public bool ActiveObjectActive = true;

        /// <summary>Ebeveyn yolu - "bunun yanýna ekle" komutlarýnýn hedefi.</summary>
        public string ParentPath = string.Empty;

        /// <summary>Ebeveyn içindeki sýra numarasý.</summary>
        public int SiblingIndex = -1;

        /// <summary>RectTransform taþýyor mu - UI elemaný sinyali.</summary>
        public bool IsUiElement;

        public bool IsPrefabInstance;

        /// <summary>Prefab düzenleme modunda mý seçildi.</summary>
        public bool InPrefabStage;

        public bool HasMissingScript;

        // ==============================
        // COMPONENTS
        // ==============================

        public List<string> Components = new List<string>();

        /// <summary>Sýnýr nedeniyle listelenmeyen bileþen sayýsý.</summary>
        public int HiddenComponentCount;

        // ==============================
        // CHILDREN
        // ==============================

        public int ChildCount;
        public List<string> Children = new List<string>();

        /// <summary>Sýnýr nedeniyle listelenmeyen çocuk sayýsý.</summary>
        public int HiddenChildCount;

        // ==============================
        // MULTI SELECTION
        // ==============================

        /// <summary>Aktif obje dýþýndaki seçili objelerin yollarý/adlarý.</summary>
        public List<string> AdditionalSelected = new List<string>();

        public int HiddenSelectionCount;

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            HasSelection = false;
            SelectionCount = 0;

            ActiveObjectName = string.Empty;
            ActiveObjectPath = string.Empty;
            ActiveObjectTag = string.Empty;
            ActiveObjectLayer = string.Empty;
            ActiveObjectType = string.Empty;
            AssetPath = string.Empty;
            ParentPath = string.Empty;

            IsSceneObject = false;
            IsAsset = false;
            ActiveObjectActive = true;
            SiblingIndex = -1;
            IsUiElement = false;
            IsPrefabInstance = false;
            InPrefabStage = false;
            HasMissingScript = false;

            Components.Clear();
            HiddenComponentCount = 0;

            Children.Clear();
            ChildCount = 0;
            HiddenChildCount = 0;

            AdditionalSelected.Clear();
            HiddenSelectionCount = 0;
        }

        // ==============================
        // SUMMARY / RENDER
        // ==============================

        /// <summary>
        /// Geriye dönük uyumlu özet - artýk bileþen SAYISINI deðil ADLARINI veriyor.
        /// Eski hâlinde model "Components: 3" görüyordu ve hangi bileþenlerle çalýþtýðýný
        /// bilemediði için doðru tool çaðrýsý üretemiyordu.
        /// </summary>
        public string GetSummary()
        {
            return Render();
        }

        public string Render(int charBudget = 0)
        {
            if (!HasSelection)
                return "Selection: yok (kullanici hicbir sey secmemis).";

            var sb = new StringBuilder();

            if (IsAsset)
            {
                sb.Append("Selected asset: ").Append(ActiveObjectName);

                if (!string.IsNullOrEmpty(ActiveObjectType))
                    sb.Append(" (").Append(ActiveObjectType).Append(')');

                sb.AppendLine();

                if (!string.IsNullOrEmpty(AssetPath))
                    sb.Append("  path: ").AppendLine(AssetPath);
            }
            else
            {
                sb.Append("Selected: ").Append(
                    string.IsNullOrEmpty(ActiveObjectPath) ? ActiveObjectName : ActiveObjectPath);

                if (!ActiveObjectActive)
                    sb.Append(" (inactive)");

                if (InPrefabStage)
                    sb.Append(" [PREFAB MODE]");

                else if (IsPrefabInstance)
                    sb.Append(" [prefab instance]");

                sb.AppendLine();

                if (IsUiElement)
                    sb.AppendLine("  ui: RectTransform (UI elemani)");

                if (!string.IsNullOrEmpty(ParentPath))
                {
                    sb.Append("  parent: ").Append(ParentPath);

                    if (SiblingIndex >= 0)
                        sb.Append("  (sibling #").Append(SiblingIndex).Append(')');

                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(ActiveObjectTag) &&
                    !string.Equals(ActiveObjectTag, "Untagged", StringComparison.Ordinal))
                {
                    sb.Append("  tag: ").AppendLine(ActiveObjectTag);
                }

                if (Components.Count > 0)
                {
                    sb.Append("  components: ").Append(string.Join(", ", Components));

                    if (HiddenComponentCount > 0)
                        sb.Append(" (+").Append(HiddenComponentCount).Append(')');

                    sb.AppendLine();
                }

                if (HasMissingScript)
                    sb.AppendLine("  !MISSING_SCRIPT (bozuk script referansi var)");

                if (ChildCount > 0)
                {
                    sb.Append("  children (").Append(ChildCount).Append("): ")
                      .Append(string.Join(", ", Children));

                    if (HiddenChildCount > 0)
                        sb.Append(", +").Append(HiddenChildCount).Append(" daha");

                    sb.AppendLine();
                }
            }

            if (SelectionCount > 1)
            {
                sb.Append("  ayrica secili (").Append(SelectionCount - 1).Append("): ")
                  .Append(string.Join(", ", AdditionalSelected));

                if (HiddenSelectionCount > 0)
                    sb.Append(", +").Append(HiddenSelectionCount).Append(" daha");

                sb.AppendLine();
            }

            string result = sb.ToString().TrimEnd();

            if (charBudget > 0 && result.Length > charBudget)
            {
                result = result.Substring(0, Math.Max(0, charBudget - 20)).TrimEnd()
                         + "\n... (kirpildi)";
            }

            return result;
        }

        /// <summary>Delta karþýlaþtýrmasý için hafif imza.</summary>
        public string ComputeSignature()
        {
            return (IsAsset ? "A|" : "S|") +
                   ActiveObjectPath + "|" +
                   AssetPath + "|" +
                   SelectionCount + "|" +
                   Components.Count;
        }

        public override string ToString()
        {
            return Render();
        }

        // ==============================
        // CAPTURE (Editor)
        // ==============================

#if UNITY_EDITOR
        /// <summary>
        /// Mevcut Editor seçimini yakalar. Hem sahne objelerini hem proje varlýklarýný
        /// destekler; Prefab Mode'da yolu prefab kökünden hesaplar.
        /// </summary>
        public static SelectionContext Capture(SelectionCaptureOptions options = null)
        {
            options = options ?? SelectionCaptureOptions.Default;

            var context = new SelectionContext();

            try
            {
                UnityEngine.Object[] selected = Selection.objects;

                if (selected == null || selected.Length == 0)
                    return context;

                context.HasSelection = true;
                context.SelectionCount = selected.Length;

                UnityEngine.Object active = Selection.activeObject;

                if (active == null)
                    active = selected[0];

                var activeGo = active as GameObject;

                if (activeGo != null && !EditorUtility.IsPersistent(activeGo))
                    CaptureSceneObject(context, activeGo, options);
                else
                    CaptureAsset(context, active);

                CaptureAdditional(context, selected, active, options);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Secim okunamadi: " + ex.Message);
                context.Clear();
            }

            return context;
        }

        private static void CaptureSceneObject(
            SelectionContext context, GameObject go, SelectionCaptureOptions options)
        {
            context.IsSceneObject = true;
            context.ActiveObjectType = "GameObject";
            context.ActiveObjectName = go.name;
            context.ActiveObjectActive = go.activeInHierarchy;
            context.ActiveObjectTag = SafeTag(go);
            context.ActiveObjectLayer = LayerMask.LayerToName(go.layer);

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            context.InPrefabStage = stage != null && stage.IsPartOfPrefabContents(go);

            context.ActiveObjectPath = BuildPath(go.transform);
            context.IsPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(go);

            Transform parent = go.transform.parent;

            if (parent != null)
            {
                context.ParentPath = BuildPath(parent);
                context.SiblingIndex = go.transform.GetSiblingIndex();
            }

            context.IsUiElement = go.transform is RectTransform;

            CaptureComponents(context, go, options);
            CaptureChildren(context, go.transform, options);
        }

        private static void CaptureAsset(SelectionContext context, UnityEngine.Object asset)
        {
            if (asset == null)
                return;

            context.IsAsset = true;
            context.ActiveObjectName = asset.name;
            context.ActiveObjectType = asset.GetType().Name;

            string path = AssetDatabase.GetAssetPath(asset);
            context.AssetPath = path ?? string.Empty;

            // Seçilen varlýk bir prefab ise kök bileþenleri de deðerli bilgi.
            var prefab = asset as GameObject;

            if (prefab != null)
            {
                context.ActiveObjectType = "Prefab";
                CaptureComponents(context, prefab, SelectionCaptureOptions.Default);
            }
        }

        private static void CaptureComponents(
            SelectionContext context, GameObject go, SelectionCaptureOptions options)
        {
            Component[] components;

            try
            {
                components = go.GetComponents<Component>();
            }
            catch (Exception)
            {
                return;
            }

            int limit = Mathf.Max(1, options.MaxComponents);

            foreach (Component component in components)
            {
                if (component == null)
                {
                    // Bozuk script referansý. Sessizce atlamak yerine modele bildiriyoruz.
                    context.HasMissingScript = true;
                    continue;
                }

                string typeName = component.GetType().Name;

                if (string.Equals(typeName, "Transform", StringComparison.Ordinal) ||
                    string.Equals(typeName, "CanvasRenderer", StringComparison.Ordinal))
                {
                    continue;
                }

                if (context.Components.Count >= limit)
                {
                    context.HiddenComponentCount++;
                    continue;
                }

                context.Components.Add(typeName);
            }
        }

        private static void CaptureChildren(
            SelectionContext context, Transform transform, SelectionCaptureOptions options)
        {
            context.ChildCount = transform.childCount;

            int limit = Mathf.Max(0, options.MaxChildren);

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);

                if (child == null)
                    continue;

                if (context.Children.Count >= limit)
                {
                    context.HiddenChildCount = transform.childCount - context.Children.Count;
                    break;
                }

                context.Children.Add(child.name);
            }
        }

        private static void CaptureAdditional(
            SelectionContext context,
            UnityEngine.Object[] selected,
            UnityEngine.Object active,
            SelectionCaptureOptions options)
        {
            if (selected.Length <= 1)
                return;

            int limit = Mathf.Max(0, options.MaxAdditionalSelected);

            foreach (UnityEngine.Object obj in selected)
            {
                if (obj == null || obj == active)
                    continue;

                if (context.AdditionalSelected.Count >= limit)
                {
                    context.HiddenSelectionCount++;
                    continue;
                }

                var go = obj as GameObject;

                context.AdditionalSelected.Add(
                    go != null && !EditorUtility.IsPersistent(go)
                        ? BuildPath(go.transform)
                        : obj.name);
            }
        }

        private static string BuildPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var parts = new List<string>();
            Transform current = transform;
            int guard = 0;

            // guard: bozuk sahnelerde ebeveyn döngüsüne karþý.
            while (current != null && guard++ < 64)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string SafeTag(GameObject go)
        {
            try
            {
                return go.tag;
            }
            catch (Exception)
            {
                return "Untagged";
            }
        }
#endif
    }
}
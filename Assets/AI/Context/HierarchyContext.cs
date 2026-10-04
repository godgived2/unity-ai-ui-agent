using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AI.Context
{
    /// <summary>
    /// Hiyerarþi yakalama sýnýrlarý.
    ///
    /// Sýnýrsýz bir sahne dökümü baðlam penceresini tek baþýna doldurur; bu yüzden
    /// yakalama aþamasýnda buduyoruz, render aþamasýnda deðil. Böylece bellekte de
    /// devasa bir aðaç tutulmuyor.
    /// </summary>
    [Serializable]
    public class HierarchyCaptureOptions
    {
        /// <summary>Kök objeden itibaren inilecek maksimum derinlik.</summary>
        public int MaxDepth = 6;

        /// <summary>Toplam yakalanacak obje sayýsý tavaný.</summary>
        public int MaxObjects = 150;

        /// <summary>Tek bir ebeveynin altýndan alýnacak çocuk sayýsý tavaný.</summary>
        public int MaxChildrenPerNode = 25;

        /// <summary>Bir objede listelenecek bileþen sayýsý tavaný.</summary>
        public int MaxComponentsPerObject = 8;

        /// <summary>
        /// Doluysa sadece adý bu listedekilerden biriyle eþleþen kök objeler (ve altlarý)
        /// yakalanýr. UI görevlerinde "Canvas" vermek, Main Camera / Directional Light
        /// gibi alakasýz dallarý tamamen dýþarýda býrakýr.
        /// </summary>
        public List<string> RootNameFilter = new List<string>();

        /// <summary>Pasif (inactive) objeler de yakalansýn mý.</summary>
        public bool IncludeInactive = true;

        /// <summary>Adý bu ön eklerle baþlayan objeler atlanýr (editör çöpü, gizli objeler).</summary>
        public List<string> IgnoreNamePrefixes = new List<string>();

        public static HierarchyCaptureOptions Default => new HierarchyCaptureOptions();

        /// <summary>UI odaklý görevler için dar kapsamlý ön ayar.</summary>
        public static HierarchyCaptureOptions UiFocused()
        {
            return new HierarchyCaptureOptions
            {
                MaxDepth = 8,
                MaxObjects = 120,
                MaxChildrenPerNode = 30,
                RootNameFilter = new List<string> { "Canvas", "EventSystem" }
            };
        }
    }

    /// <summary>
    /// Unity Hierarchy bilgisini temsil eder ve LLM'e verilecek kompakt metni üretir.
    /// </summary>
    [Serializable]
    public class HierarchyContext
    {
        /// <summary>Yakalanan obje sayýsý (kýrpma sonrasý).</summary>
        public int ObjectCount;

        /// <summary>Sahnedeki gerçek toplam obje sayýsý (kýrpma öncesi). Modele "gördüðün
        /// her þey deðil" sinyalini vermek için gerekli.</summary>
        public int TotalObjectsInScene;

        /// <summary>Sýnýrlar yüzünden dýþarýda kalan obje oldu mu.</summary>
        public bool Truncated;

        public string SceneName = string.Empty;

        public List<HierarchyObject> RootObjects = new List<HierarchyObject>();

        // ==============================
        // MUTATION
        // ==============================

        public void AddObject(HierarchyObject obj)
        {
            if (obj == null)
                return;

            RootObjects.Add(obj);

            // Eski kodda bu satýr yoktu: ObjectCount hiç artmýyordu, sayaç ölüydü.
            ObjectCount += obj.CountSelfAndDescendants();
        }

        public void Clear()
        {
            RootObjects.Clear();
            ObjectCount = 0;
            TotalObjectsInScene = 0;
            Truncated = false;
            SceneName = string.Empty;
        }

        // ==============================
        // CAPTURE
        // ==============================

        /// <summary>
        /// Aktif sahneyi verilen sýnýrlar dahilinde yakalar. Editör dýþý derlemede de
        /// çalýþýr (yalnýzca UnityEngine API'leri kullanýlýr).
        /// </summary>
        public static HierarchyContext CaptureActiveScene(HierarchyCaptureOptions options = null)
        {
            options = options ?? HierarchyCaptureOptions.Default;

            var context = new HierarchyContext();

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return context;

            context.SceneName = scene.name;

            GameObject[] roots;
            try
            {
                roots = scene.GetRootGameObjects();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Hiyerarþi okunamadý: " + ex.Message);
                return context;
            }

            int budget = Mathf.Max(1, options.MaxObjects);
            int captured = 0;
            int total = 0;

            foreach (GameObject root in roots)
            {
                if (root == null)
                    continue;

                total += CountTransformTree(root.transform);

                if (!PassesRootFilter(root.name, options))
                    continue;

                if (!options.IncludeInactive && !root.activeInHierarchy)
                    continue;

                if (IsIgnored(root.name, options))
                    continue;

                if (captured >= budget)
                {
                    context.Truncated = true;
                    continue;
                }

                HierarchyObject node = CaptureNode(
                    root.transform, string.Empty, 0, options, ref captured, budget, context);

                if (node != null)
                    context.RootObjects.Add(node);
            }

            context.ObjectCount = captured;
            context.TotalObjectsInScene = total;

            if (captured < total)
                context.Truncated = true;

            return context;
        }

        private static HierarchyObject CaptureNode(
            Transform transform,
            string parentPath,
            int depth,
            HierarchyCaptureOptions options,
            ref int captured,
            int budget,
            HierarchyContext context)
        {
            if (transform == null || captured >= budget)
            {
                context.Truncated = true;
                return null;
            }

            captured++;

            string path = string.IsNullOrEmpty(parentPath)
                ? transform.name
                : parentPath + "/" + transform.name;

            var node = new HierarchyObject
            {
                Name = transform.name,
                Path = path,
                Tag = SafeTag(transform.gameObject),
                Layer = LayerMask.LayerToName(transform.gameObject.layer),
                Active = transform.gameObject.activeInHierarchy,
                IsUiElement = transform is RectTransform
            };

            CollectComponents(transform.gameObject, node, options);

            if (depth >= options.MaxDepth)
            {
                if (transform.childCount > 0)
                {
                    node.HiddenChildCount = transform.childCount;
                    context.Truncated = true;
                }
                return node;
            }

            int childLimit = Mathf.Min(transform.childCount, Mathf.Max(1, options.MaxChildrenPerNode));

            for (int i = 0; i < childLimit; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null)
                    continue;

                if (!options.IncludeInactive && !child.gameObject.activeInHierarchy)
                    continue;

                if (IsIgnored(child.name, options))
                    continue;

                if (captured >= budget)
                {
                    node.HiddenChildCount += transform.childCount - i;
                    context.Truncated = true;
                    break;
                }

                HierarchyObject childNode = CaptureNode(
                    child, path, depth + 1, options, ref captured, budget, context);

                if (childNode != null)
                    node.Children.Add(childNode);
            }

            if (transform.childCount > childLimit)
            {
                node.HiddenChildCount += transform.childCount - childLimit;
                context.Truncated = true;
            }

            return node;
        }

        /// <summary>
        /// Gürültü bileþenlerini eler. Transform her objede var, hiçbir bilgi taþýmýyor;
        /// RectTransform ise "bu bir UI elemaný" sinyali olduðu için KORUNUR.
        /// </summary>
        private static readonly HashSet<string> NoiseComponents =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Transform", "CanvasRenderer"
            };

        private static void CollectComponents(
            GameObject go, HierarchyObject node, HierarchyCaptureOptions options)
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

            int limit = Mathf.Max(1, options.MaxComponentsPerObject);

            foreach (Component component in components)
            {
                if (node.Components.Count >= limit)
                {
                    node.HiddenComponentCount++;
                    continue;
                }

                // null bileþen = eksik/bozuk script referansý. Modele bunu SÖYLEMEK
                // deðerli, sessizce atlamak deðil.
                if (component == null)
                {
                    node.HasMissingScript = true;
                    continue;
                }

                string typeName = component.GetType().Name;

                if (NoiseComponents.Contains(typeName))
                    continue;

                node.Components.Add(typeName);
            }
        }

        private static bool PassesRootFilter(string rootName, HierarchyCaptureOptions options)
        {
            if (options.RootNameFilter == null || options.RootNameFilter.Count == 0)
                return true;

            foreach (string filter in options.RootNameFilter)
            {
                if (string.IsNullOrWhiteSpace(filter))
                    continue;

                if (rootName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsIgnored(string name, HierarchyCaptureOptions options)
        {
            if (options.IgnoreNamePrefixes == null || options.IgnoreNamePrefixes.Count == 0)
                return false;

            foreach (string prefix in options.IgnoreNamePrefixes)
            {
                if (!string.IsNullOrEmpty(prefix) &&
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        private static int CountTransformTree(Transform transform)
        {
            if (transform == null)
                return 0;

            int count = 1;

            for (int i = 0; i < transform.childCount; i++)
                count += CountTransformTree(transform.GetChild(i));

            return count;
        }

        // ==============================
        // RENDERING
        // ==============================

        /// <summary>
        /// LLM'e verilecek kompakt aðaç metni. JSON deðil girintili düz metin: ayný bilgi
        /// için JSON'un yaklaþýk yarýsý kadar token harcar ve model aðaç yapýsýný daha
        /// doðru okur.
        ///
        /// Biçim:
        ///   Canvas [Canvas,CanvasScaler,GraphicRaycaster]
        ///     MainPanel [RectTransform,Image]
        ///       TabBar [RectTransform,HorizontalLayoutGroup]
        ///         Tab_1 [RectTransform,Image,Button,TabController]
        /// </summary>
        public string Render(int charBudget = 0)
        {
            if (RootObjects.Count == 0)
                return "Hierarchy: (bos sahne veya yakalama yapilmadi)";

            var sb = new StringBuilder();

            sb.Append("Scene: ").Append(string.IsNullOrEmpty(SceneName) ? "(unnamed)" : SceneName);
            sb.Append("  | objects: ").Append(ObjectCount);

            if (TotalObjectsInScene > ObjectCount)
                sb.Append(" / ").Append(TotalObjectsInScene);

            sb.AppendLine();

            foreach (HierarchyObject root in RootObjects)
            {
                root.Render(sb, 0);

                if (charBudget > 0 && sb.Length > charBudget)
                    break;
            }

            if (Truncated)
                sb.AppendLine("... (hiyerarsi kirpildi; tam liste icin hedef objeyi daralt)");

            string result = sb.ToString();

            if (charBudget > 0 && result.Length > charBudget)
            {
                result = result.Substring(0, Math.Max(0, charBudget - 40)).TrimEnd()
                         + "\n... (baglam butcesi doldu)";
            }

            return result;
        }

        /// <summary>
        /// Aðacýn yapýsal imzasý. Ýki adým arasýnda deðiþip deðiþmediðini anlamak için
        /// kullanýlýr - deðiþmemiþse hiyerarþiyi tekrar göndermeye gerek yok, bu 150
        /// adýmlýk bir döngüde en büyük tasarruf kalemi.
        /// </summary>
        public string ComputeSignature()
        {
            var sb = new StringBuilder();

            foreach (HierarchyObject root in RootObjects)
                root.AppendSignature(sb);

            unchecked
            {
                int hash = 17;
                string content = sb.ToString();

                for (int i = 0; i < content.Length; i++)
                    hash = hash * 31 + content[i];

                return hash.ToString("X8");
            }
        }

        /// <summary>
        /// Önceki yakalamaya göre eklenen / silinen yollarý döndürür. Delta context için.
        /// </summary>
        public string RenderDelta(HierarchyContext previous, int charBudget = 0)
        {
            if (previous == null)
                return Render(charBudget);

            var before = new HashSet<string>(previous.EnumeratePaths(), StringComparer.Ordinal);
            var after = new HashSet<string>(EnumeratePaths(), StringComparer.Ordinal);

            List<string> added = after.Where(p => !before.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            List<string> removed = before.Where(p => !after.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

            if (added.Count == 0 && removed.Count == 0)
                return "Hierarchy: son adimdan beri degisiklik yok.";

            var sb = new StringBuilder("Hierarchy degisiklikleri:").AppendLine();

            foreach (string path in added.Take(40))
                sb.Append("  + ").AppendLine(path);

            if (added.Count > 40)
                sb.Append("  + ... (").Append(added.Count - 40).AppendLine(" obje daha)");

            foreach (string path in removed.Take(20))
                sb.Append("  - ").AppendLine(path);

            if (removed.Count > 20)
                sb.Append("  - ... (").Append(removed.Count - 20).AppendLine(" obje daha)");

            string result = sb.ToString();

            if (charBudget > 0 && result.Length > charBudget)
                result = result.Substring(0, Math.Max(0, charBudget - 20)).TrimEnd() + "\n...";

            return result;
        }

        public IEnumerable<string> EnumeratePaths()
        {
            foreach (HierarchyObject root in RootObjects)
            {
                foreach (string path in root.EnumeratePaths())
                    yield return path;
            }
        }

        public HierarchyObject Find(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            foreach (HierarchyObject root in RootObjects)
            {
                HierarchyObject found = root.Find(path);
                if (found != null)
                    return found;
            }

            return null;
        }

        public override string ToString()
        {
            return Render();
        }
    }

    // =====================================
    // HIERARCHY OBJECT MODEL
    // =====================================

    [Serializable]
    public class HierarchyObject
    {
        public string Name;
        public string Path;
        public string Tag;
        public string Layer;
        public bool Active;

        /// <summary>RectTransform taþýyor mu - model için "bu UI" sinyali.</summary>
        public bool IsUiElement;

        /// <summary>Eksik (Missing Script) referansý var mý.</summary>
        public bool HasMissingScript;

        /// <summary>Sýnýrlar yüzünden gösterilmeyen çocuk sayýsý.</summary>
        public int HiddenChildCount;

        /// <summary>Sýnýrlar yüzünden gösterilmeyen bileþen sayýsý.</summary>
        public int HiddenComponentCount;

        public List<string> Components = new List<string>();
        public List<HierarchyObject> Children = new List<HierarchyObject>();

        public int CountSelfAndDescendants()
        {
            int count = 1;

            foreach (HierarchyObject child in Children)
            {
                if (child != null)
                    count += child.CountSelfAndDescendants();
            }

            return count;
        }

        internal void Render(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2).Append(Name);

            if (!Active)
                sb.Append(" (inactive)");

            if (Components.Count > 0)
                sb.Append(" [").Append(string.Join(",", Components)).Append(']');

            if (HiddenComponentCount > 0)
                sb.Append(" +").Append(HiddenComponentCount).Append("comp");

            if (HasMissingScript)
                sb.Append(" !MISSING_SCRIPT");

            sb.AppendLine();

            foreach (HierarchyObject child in Children)
            {
                if (child != null)
                    child.Render(sb, depth + 1);
            }

            if (HiddenChildCount > 0)
            {
                sb.Append(' ', (depth + 1) * 2)
                  .Append("... (+").Append(HiddenChildCount).AppendLine(" cocuk gosterilmedi)");
            }
        }

        internal void AppendSignature(StringBuilder sb)
        {
            sb.Append(Path).Append('|').Append(Components.Count).Append(';');

            foreach (HierarchyObject child in Children)
            {
                if (child != null)
                    child.AppendSignature(sb);
            }
        }

        internal IEnumerable<string> EnumeratePaths()
        {
            yield return Path;

            foreach (HierarchyObject child in Children)
            {
                if (child == null)
                    continue;

                foreach (string path in child.EnumeratePaths())
                    yield return path;
            }
        }

        internal HierarchyObject Find(string path)
        {
            if (string.Equals(Path, path, StringComparison.OrdinalIgnoreCase))
                return this;

            foreach (HierarchyObject child in Children)
            {
                if (child == null)
                    continue;

                HierarchyObject found = child.Find(path);
                if (found != null)
                    return found;
            }

            return null;
        }

        public override string ToString()
        {
            return Path ?? Name;
        }
    }
}
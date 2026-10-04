using System;
using System.Collections.Generic;
using System.Text;

namespace AI.Context
{
    /// <summary>
    /// Unity proje bilgisini temsil eder.
    ///
    /// Kapsam kuralý: ProjectName ve UnityVersion alanlarý UnityContext ile çakýþýr.
    /// Bu sýnýfýn Render() metodu onlarý YAZMAZ - ayný bilgi prompt'a iki kez girmesin
    /// diye ortam bilgisi UnityContext'in tekelindedir.
    ///
    /// Ayný þekilde AssetContext ile de envanter çakýþmasý vardýr: ProjectContext
    /// prompt'a girer (sayýlar + odaklý küçük bir alt küme), AssetContext ise arama
    /// ve lookup için kullanýlýr, prompt'a girmez.
    /// </summary>
    [Serializable]
    public class ProjectContext
    {
        /// <summary>Render() içinde listelenecek azami dosya sayýsý.</summary>
        public const int DefaultListLimit = 12;

        // ==============================
        // PROJECT INFO
        // ==============================

        public string ProjectName = string.Empty;
        public string ProjectPath = string.Empty;
        public string UnityVersion = string.Empty;

        // ==============================
        // ASSET DATA
        // ==============================

        public int TotalAssets;
        public int TotalScripts;
        public int TotalScenes;
        public int TotalPrefabs;
        public int TotalFolders;

        // ==============================
        // FILE LISTS
        // ==============================

        public List<string> Scripts = new List<string>();
        public List<string> Scenes = new List<string>();
        public List<string> Prefabs = new List<string>();
        public List<string> Folders = new List<string>();
        public List<string> Assets = new List<string>();

        // ==============================
        // UPDATE DATA
        // ==============================

        public void UpdateCounts()
        {
            // Null korumasý: tarayýcý listelerden birini doldurmadan býraktýðýnda
            // eski kod NullReferenceException atýyordu.
            TotalAssets = Assets != null ? Assets.Count : 0;
            TotalScripts = Scripts != null ? Scripts.Count : 0;
            TotalScenes = Scenes != null ? Scenes.Count : 0;
            TotalPrefabs = Prefabs != null ? Prefabs.Count : 0;
            TotalFolders = Folders != null ? Folders.Count : 0;
        }

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            ProjectName = string.Empty;
            ProjectPath = string.Empty;
            UnityVersion = string.Empty;

            Assets?.Clear();
            Scripts?.Clear();
            Scenes?.Clear();
            Prefabs?.Clear();
            Folders?.Clear();

            TotalAssets = 0;
            TotalScripts = 0;
            TotalScenes = 0;
            TotalPrefabs = 0;
            TotalFolders = 0;
        }

        // ==============================
        // LOOKUP
        // ==============================

        /// <summary>
        /// Adý veya yolu anahtar kelimeyi içeren script yollarýný döndürür.
        ///
        /// Eski hâlde Scripts listesi doluyordu ama eriþilemiyordu: "TabController.cs
        /// nerede?" sorusunun cevabý bellekteydi, kimse soramýyordu.
        /// </summary>
        public List<string> FindScripts(string keyword, int max = DefaultListLimit)
        {
            return FindIn(Scripts, keyword, max);
        }

        public List<string> FindPrefabs(string keyword, int max = DefaultListLimit)
        {
            return FindIn(Prefabs, keyword, max);
        }

        public List<string> FindScenes(string keyword, int max = DefaultListLimit)
        {
            return FindIn(Scenes, keyword, max);
        }

        public List<string> FindAssets(string keyword, int max = DefaultListLimit)
        {
            return FindIn(Assets, keyword, max);
        }

        /// <summary>Tam dosya adýyla ilk eþleþen script yolu (örn. "TabController.cs").</summary>
        public string FindScriptByFileName(string fileName)
        {
            if (Scripts == null || string.IsNullOrWhiteSpace(fileName))
                return null;

            foreach (string path in Scripts)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                if (string.Equals(ContextFormat.FileName(path), fileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        private static List<string> FindIn(List<string> source, string keyword, int max)
        {
            var result = new List<string>();

            if (source == null || string.IsNullOrWhiteSpace(keyword) || max <= 0)
                return result;

            foreach (string path in source)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                // ContainsIgnoreCase kullanýlýyor: ToLower() Türkçe kültürde 'I' -> 'ý'
                // yaptýðý için arama sessizce boþ dönerdi.
                if (!ContextFormat.ContainsIgnoreCase(path, keyword))
                    continue;

                result.Add(path);

                if (result.Count >= max)
                    break;
            }

            return result;
        }

        // ==============================
        // RENDER
        // ==============================

        public string GetSummary()
        {
            return Render();
        }

        /// <summary>
        /// Prompt'a girecek proje özeti.
        ///
        /// <paramref name="focusKeyword"/> verilirse (genelde kullanýcýnýn komutu),
        /// eþleþen script ve prefab yollarý da listelenir. Bu, modelin var olan bir
        /// script'i yeniden yazmak yerine ona referans vermesini saðlar.
        /// </summary>
        public string Render(int charBudget = 0, string focusKeyword = null, int listLimit = DefaultListLimit)
        {
            var sb = new StringBuilder();

            sb.Append("Project files: ")
              .Append(TotalScripts).Append(" scripts, ")
              .Append(TotalScenes).Append(" scenes, ")
              .Append(TotalPrefabs).Append(" prefabs, ")
              .Append(TotalFolders).Append(" folders, ")
              .Append(TotalAssets).Append(" assets total")
              .AppendLine();

            if (!string.IsNullOrWhiteSpace(focusKeyword))
            {
                AppendMatches(sb, "matching scripts", FindScripts(focusKeyword, listLimit));
                AppendMatches(sb, "matching prefabs", FindPrefabs(focusKeyword, listLimit));
            }
            else if (Scripts != null && Scripts.Count > 0)
            {
                // Odak yoksa yalnýzca birkaç script adý: modelin proje kodunun var
                // olduðunu bilmesi yeter, tam envanter deðil.
                var names = new List<string>();

                for (int i = 0; i < Scripts.Count && names.Count < Math.Min(listLimit, 8); i++)
                    names.Add(ContextFormat.FileName(Scripts[i]));

                if (names.Count > 0)
                {
                    sb.Append("  scripts: ")
                      .AppendLine(ContextFormat.JoinLimited(names, names.Count));
                }
            }

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        private static void AppendMatches(StringBuilder sb, string label, List<string> matches)
        {
            if (matches == null || matches.Count == 0)
                return;

            sb.Append("  ").Append(label).Append(": ")
              .AppendLine(ContextFormat.JoinLimited(matches, matches.Count));
        }

        public override string ToString()
        {
            return Render();
        }
    }
}
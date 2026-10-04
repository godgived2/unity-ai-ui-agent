using System;
using System.Collections.Generic;
using System.Text;

namespace AI.Context
{
    /// <summary>
    /// Unity Asset bilgilerini temsil eder.
    ///
    /// Kapsam kuralý: bu sýnýf PROMPT'A GÝRMEZ. Envanteri ProjectContext özetliyor;
    /// buradaki ayrýntýlý liste arama ve lookup içindir (agent bir varlýðýn yolunu ya da
    /// GUID'ini sorduðunda). Ýkisi birden prompt'a girerse ayný envanter iki kez gider.
    /// </summary>
    [Serializable]
    public class AssetContext
    {
        /// <summary>Bellekte tutulacak azami varlýk sayýsý. 0 veya altý = sýnýrsýz.</summary>
        public int Capacity = 5000;

        // ==============================
        // TOTAL COUNT
        // ==============================

        public int AssetCount;

        /// <summary>Kapasite aþýmý nedeniyle alýnmayan varlýk oldu mu.</summary>
        public bool Truncated;

        // ==============================
        // ASSETS
        // ==============================

        public List<UnityAssetInfo> Assets = new List<UnityAssetInfo>();

        /// <summary>Path -> asset, tekrar eklemeyi ve O(n) aramayý önlemek için.</summary>
        [NonSerialized]
        private Dictionary<string, UnityAssetInfo> _byPath;

        // ==============================
        // ADD ASSET
        // ==============================

        public void AddAsset(UnityAssetInfo asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.Path))
                return;

            EnsureIndex();

            // Eski kodda dedupe yoktu: ayný tarama iki kez çalýþtýðýnda liste ikiye
            // katlanýyordu ve AssetCount de buna göre þiþiyordu.
            if (_byPath.ContainsKey(asset.Path))
                return;

            if (Capacity > 0 && Assets.Count >= Capacity)
            {
                Truncated = true;
                return;
            }

            Assets.Add(asset);
            _byPath[asset.Path] = asset;
            AssetCount = Assets.Count;
        }

        public void AddRange(IEnumerable<UnityAssetInfo> assets)
        {
            if (assets == null)
                return;

            foreach (UnityAssetInfo asset in assets)
                AddAsset(asset);
        }

        private void EnsureIndex()
        {
            if (_byPath != null)
                return;

            // [NonSerialized] alan domain reload sonrasý null döner; yeniden kuruyoruz.
            _byPath = new Dictionary<string, UnityAssetInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityAssetInfo asset in Assets)
            {
                if (asset != null && !string.IsNullOrWhiteSpace(asset.Path))
                    _byPath[asset.Path] = asset;
            }
        }

        // ==============================
        // SEARCH / LOOKUP
        // ==============================

        /// <summary>
        /// Ad veya yol içinde geçen varlýklarý döndürür.
        ///
        /// Eski hâlde ToLower() kullanýlýyordu: Türkçe kültürde 'I' -> 'ý' olduðu için
        /// "Image" aramasý hiçbir zaman eþleþmiyordu. Ayrýca Name/Path null ise
        /// NullReferenceException atýyordu.
        /// </summary>
        public List<UnityAssetInfo> Search(string keyword, int max = 25)
        {
            var result = new List<UnityAssetInfo>();

            if (string.IsNullOrWhiteSpace(keyword) || max <= 0)
                return result;

            foreach (UnityAssetInfo asset in Assets)
            {
                if (asset == null)
                    continue;

                if (ContextFormat.ContainsIgnoreCase(asset.Name, keyword) ||
                    ContextFormat.ContainsIgnoreCase(asset.Path, keyword))
                {
                    result.Add(asset);

                    if (result.Count >= max)
                        break;
                }
            }

            return result;
        }

        public UnityAssetInfo FindByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            EnsureIndex();
            return _byPath.TryGetValue(path, out UnityAssetInfo asset) ? asset : null;
        }

        public UnityAssetInfo FindByGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return null;

            foreach (UnityAssetInfo asset in Assets)
            {
                if (asset != null &&
                    string.Equals(asset.Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }

            return null;
        }

        public List<UnityAssetInfo> Scripts(int max = 50) => Filter(a => a.IsScript, max);
        public List<UnityAssetInfo> Prefabs(int max = 50) => Filter(a => a.IsPrefab, max);
        public List<UnityAssetInfo> ScenesList(int max = 50) => Filter(a => a.IsScene, max);

        private List<UnityAssetInfo> Filter(Func<UnityAssetInfo, bool> predicate, int max)
        {
            var result = new List<UnityAssetInfo>();

            if (predicate == null || max <= 0)
                return result;

            foreach (UnityAssetInfo asset in Assets)
            {
                if (asset == null || !predicate(asset))
                    continue;

                result.Add(asset);

                if (result.Count >= max)
                    break;
            }

            return result;
        }

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            Assets.Clear();
            EnsureIndex();
            _byPath.Clear();

            AssetCount = 0;
            Truncated = false;
        }

        // ==============================
        // RENDER
        // ==============================

        public string GetSummary()
        {
            return Render();
        }

        /// <summary>
        /// Kýsa özet. Anahtar kelime verilirse yalnýzca eþleþen varlýklarýn yollarýný
        /// listeler - envanterin tamamýný asla yazmaz.
        /// </summary>
        public string Render(string keyword = null, int max = 10, int charBudget = 0)
        {
            var sb = new StringBuilder();

            sb.Append("Assets indexed: ").Append(AssetCount);

            if (Truncated)
                sb.Append(" (kapasite doldu, liste eksik)");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                List<UnityAssetInfo> matches = Search(keyword, max);

                if (matches.Count == 0)
                {
                    sb.Append("  '").Append(keyword).AppendLine("' ile eslesen varlik yok.");
                }
                else
                {
                    foreach (UnityAssetInfo asset in matches)
                        sb.Append("  ").AppendLine(asset.ToString());
                }
            }

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        public override string ToString()
        {
            return Render();
        }
    }

    // =====================================
    // ASSET MODEL
    // =====================================

    [Serializable]
    public class UnityAssetInfo
    {
        public string Name = string.Empty;
        public string Path = string.Empty;
        public string Type = string.Empty;
        public string Guid = string.Empty;
        public long Size;

        public bool IsFolder;
        public bool IsScript;
        public bool IsPrefab;
        public bool IsScene;

        public override string ToString()
        {
            var sb = new StringBuilder(string.IsNullOrEmpty(Path) ? Name : Path);

            if (!string.IsNullOrEmpty(Type))
                sb.Append(" (").Append(Type).Append(')');

            if (Size > 0)
                sb.Append(' ').Append(ContextFormat.ByteSize(Size));

            return sb.ToString();
        }
    }
}
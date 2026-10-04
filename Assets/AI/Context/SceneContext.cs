using System;
using System.Collections.Generic;
using System.Text;

namespace AI.Context
{
    /// <summary>
    /// Unity Scene bilgisini temsil eder.
    ///
    /// Önceki turda bu alanlar geçici bir SceneSnapshot sýnýfýnda tutuluyordu, çünkü bu
    /// dosya elimde yoktu. Artýk hepsi buraya taþýndý; SceneSnapshot'a gerek kalmadý.
    /// Eski alanlarýn hiçbiri kaldýrýlmadý, yalnýzca ekleme yapýldý.
    ///
    /// Kapsam kuralý: RootObjects listesi HierarchyContext ile çakýþýr. Burada bilerek
    /// düþük tavanla tutulur (ucuz baþlýk); aðacýn kendisi HierarchyContext'in iþidir.
    /// </summary>
    [Serializable]
    public class SceneContext
    {
        // ==============================
        // SCENE INFO
        // ==============================

        public string SceneName = string.Empty;
        public string ScenePath = string.Empty;
        public int BuildIndex = -1;

        // ==============================
        // STATE
        // ==============================

        public bool IsLoaded;
        public bool IsDirty;

        /// <summary>Sahne okunabilir durumda mý. False ise diðer alanlar güvenilmezdir.</summary>
        public bool IsValid;

        /// <summary>Geçersizse sebebi (agent'a "neden göremiyorum" cevabýný verir).</summary>
        public string InvalidReason = string.Empty;

        // ==============================
        // PREFAB STAGE
        // ==============================

        /// <summary>
        /// Prefab düzenleme modunda mýyýz.
        ///
        /// Kritik: prefab modundayken aktif sahne bir önizleme sahnesidir. Agent buna
        /// obje eklerse ana sahneye deðil prefab'a yazar. Bunu bilmeden çalýþan bir
        /// agent onlarca adýmý yanlýþ hedefe harcar.
        /// </summary>
        public bool InPrefabStage;

        public string PrefabAssetPath = string.Empty;
        public string PrefabRootName = string.Empty;

        // ==============================
        // UI RIG
        // ==============================

        /// <summary>Sahnede en az bir Canvas var mý.</summary>
        public bool HasCanvas;

        /// <summary>Sahnede EventSystem var mý (yoksa hiçbir UI etkileþimi çalýþmaz).</summary>
        public bool HasEventSystem;

        /// <summary>Ýlk bulunan Canvas'ýn adý - agent'ýn hedefleyeceði kök.</summary>
        public string PrimaryCanvasName = string.Empty;

        // ==============================
        // OBJECTS
        // ==============================

        public int RootObjectCount;
        public List<string> RootObjects = new List<string>();

        /// <summary>Tavan nedeniyle listelenmeyen kök obje sayýsý.</summary>
        public int HiddenRootObjectCount;

        // ==============================
        // MULTI-SCENE
        // ==============================

        /// <summary>Aktif sahne dýþýndaki yüklü sahnelerin adlarý.</summary>
        public List<string> AdditionalScenes = new List<string>();

        // ==============================
        // RENDER
        // ==============================

        public string GetSummary()
        {
            return Render();
        }

        public string Render(int charBudget = 0)
        {
            var sb = new StringBuilder();

            if (!IsValid)
            {
                sb.Append("Scene: okunamiyor");

                if (!string.IsNullOrEmpty(InvalidReason))
                    sb.Append(" (").Append(InvalidReason).Append(')');

                return sb.ToString();
            }

            if (InPrefabStage)
            {
                sb.Append("PREFAB MODE: ")
                  .Append(string.IsNullOrEmpty(PrefabRootName) ? "(root bilinmiyor)" : PrefabRootName);

                if (!string.IsNullOrEmpty(PrefabAssetPath))
                    sb.Append("  <- ").Append(PrefabAssetPath);

                sb.AppendLine();
                sb.AppendLine("  DIKKAT: aktif sahne prefab onizleme sahnesidir. Buraya eklenen " +
                              "objeler prefab'a kaydedilir, ana sahneye DEGIL.");
            }
            else
            {
                sb.Append("Scene: ")
                  .Append(string.IsNullOrEmpty(SceneName) ? "(unnamed)" : SceneName);

                if (IsDirty)
                    sb.Append(" *kaydedilmemis*");

                if (!IsLoaded)
                    sb.Append(" (yuklenmemis)");

                sb.Append("  | roots: ").Append(RootObjectCount);
                sb.AppendLine();
            }

            if (RootObjects.Count > 0)
            {
                sb.Append("  root objects: ")
                  .Append(ContextFormat.JoinLimited(RootObjects, RootObjects.Count));

                if (HiddenRootObjectCount > 0)
                    sb.Append(", +").Append(HiddenRootObjectCount).Append(" daha");

                sb.AppendLine();
            }

            sb.Append("  ui rig: ");
            sb.Append(HasCanvas
                ? "Canvas VAR" + (string.IsNullOrEmpty(PrimaryCanvasName)
                    ? string.Empty
                    : " (" + PrimaryCanvasName + ")")
                : "Canvas YOK");
            sb.Append(", ").Append(HasEventSystem ? "EventSystem VAR" : "EventSystem YOK");
            sb.AppendLine();

            if (AdditionalScenes.Count > 0)
            {
                sb.Append("  additive scenes: ")
                  .AppendLine(ContextFormat.JoinLimited(AdditionalScenes, 6));
            }

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        /// <summary>Delta karþýlaþtýrmasý için hafif imza.</summary>
        public string ComputeSignature()
        {
            return SceneName + "|" + RootObjectCount + "|" +
                   (InPrefabStage ? "P" : "S") + "|" +
                   (HasCanvas ? "C" : "-") + (HasEventSystem ? "E" : "-") + "|" +
                   (IsDirty ? "D" : "-");
        }

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            SceneName = string.Empty;
            ScenePath = string.Empty;
            BuildIndex = -1;

            IsLoaded = false;
            IsDirty = false;
            IsValid = false;
            InvalidReason = string.Empty;

            InPrefabStage = false;
            PrefabAssetPath = string.Empty;
            PrefabRootName = string.Empty;

            HasCanvas = false;
            HasEventSystem = false;
            PrimaryCanvasName = string.Empty;

            RootObjects.Clear();
            RootObjectCount = 0;
            HiddenRootObjectCount = 0;

            AdditionalScenes.Clear();
        }

        public override string ToString()
        {
            return Render();
        }
    }
}
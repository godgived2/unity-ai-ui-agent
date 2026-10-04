using System;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AI.Context
{
    /// <summary>
    /// Unity ortamýnýn genel context modeli.
    ///
    /// Kapsam kuralý: bu sýnýf ORTAM durumunu anlatýr (sürüm, platform, editör durumu).
    /// Sahne bilgisi SceneContext'in, seçim bilgisi SelectionContext'in iþidir. Eski
    /// ToString() bu ikisini de yazdýrýyordu ama ContextBuilder ilgili alanlarý hiç
    /// doldurmuyordu; model her adýmda bos "Scene:" ve "Selected:" satirlari goruyor,
    /// bunu "sahne yok / secim yok" diye okuyup ayni prompt'taki SCENE ve SELECTION
    /// bolumleriyle celisen bir tablo kuruyordu.
    /// </summary>
    [Serializable]
    public class UnityContext
    {
        // ==============================
        // PROJECT INFO
        // ==============================

        public string ProjectName = string.Empty;
        public string ProjectPath = string.Empty;

        // ==============================
        // UNITY INFO
        // ==============================

        public string UnityVersion = string.Empty;
        public string Platform = string.Empty;

        // ==============================
        // EDITOR STATE
        // ==============================

        public bool IsEditor;
        public bool IsPlaying;
        public bool IsCompiling;
        public bool IsUpdating;

        // ==============================
        // SCENE INFO (geriye dönük uyumluluk için korundu)
        // ==============================

        /// <summary>SceneContext bu bilgiyi taþýr. Burada yalnýzca eski kod kýrýlmasýn diye durur.</summary>
        public string ActiveScene = string.Empty;

        /// <summary>SceneContext bu bilgiyi taþýr.</summary>
        public string ActiveScenePath = string.Empty;

        // ==============================
        // SELECTION (geriye dönük uyumluluk için korundu)
        // ==============================

        /// <summary>SelectionContext bu bilgiyi taþýr.</summary>
        public string SelectedObject = string.Empty;

        // ==============================
        // TIME
        // ==============================

        public string Time = string.Empty;

        // ==============================
        // DERIVED STATE
        // ==============================

        /// <summary>
        /// Editör meþgul: derleme veya asset import sürüyor.
        ///
        /// Agent bu durumda tool çaðýrmamalý. Derleme sýrasýnda yapýlan sahne
        /// deðiþiklikleri domain reload ile kaybolabilir ve script referanslarý
        /// yarý yüklü durumdadýr.
        /// </summary>
        public bool IsBusy => IsCompiling || IsUpdating;

        /// <summary>
        /// Play Mode'da sahneye yapýlan deðiþiklikler Play'den çýkýnca kaybolur.
        /// Agent'ýn bunu bilmesi, boþa 20 adým harcamasýný engeller.
        /// </summary>
        public bool ChangesArePersistent => !IsPlaying;

        // ==============================
        // RENDER
        // ==============================

        public override string ToString()
        {
            return Render();
        }

        public string Render(int charBudget = 0)
        {
            var sb = new StringBuilder();

            sb.Append("Unity ");
            sb.Append(string.IsNullOrEmpty(UnityVersion) ? "(bilinmiyor)" : UnityVersion);

            if (!string.IsNullOrEmpty(Platform))
                sb.Append(" | ").Append(Platform);

            if (!string.IsNullOrEmpty(ProjectName))
                sb.Append(" | project: ").Append(ProjectName);

            sb.AppendLine();

            // Yalnýzca DÝKKAT gerektiren durumlar yazýlýr. "IsEditor: true" gibi her adýmda
            // ayný kalan ve hiçbir kararý deðiþtirmeyen satýrlar yazýlmaz.
            if (IsCompiling)
                sb.AppendLine("  DURUM: derleme suruyor - tool cagirma, derlemenin bitmesini bekle.");

            if (IsUpdating)
                sb.AppendLine("  DURUM: asset import suruyor - AssetDatabase su an tutarsiz olabilir.");

            if (IsPlaying)
                sb.AppendLine("  DURUM: PLAY MODE aktif - sahneye yapilan degisiklikler Play'den " +
                              "cikinca KAYBOLUR.");

            if (!string.IsNullOrEmpty(Time))
                sb.Append("  time: ").AppendLine(Time);

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        // ==============================
        // CAPTURE
        // ==============================

        /// <summary>
        /// Editör durum alanlarýný doldurur. Servis tarafýndan set edilmiþ alanlara
        /// (ProjectName, UnityVersion, Platform, Time) DOKUNMAZ - yalnýzca boþsa doldurur.
        /// Böylece UnityContextService ile çakýþmaz, onu tamamlar.
        /// </summary>
        public UnityContext EnrichEditorState()
        {
            try
            {
                if (string.IsNullOrEmpty(UnityVersion))
                    UnityVersion = Application.unityVersion;

                if (string.IsNullOrEmpty(Platform))
                    Platform = Application.platform.ToString();

                if (string.IsNullOrEmpty(ProjectName))
                    ProjectName = Application.productName;

                if (string.IsNullOrEmpty(ProjectPath))
                    ProjectPath = Application.dataPath;

                IsPlaying = Application.isPlaying;

#if UNITY_EDITOR
                IsEditor = true;
                IsCompiling = EditorApplication.isCompiling;
                IsUpdating = EditorApplication.isUpdating;
                IsPlaying = EditorApplication.isPlayingOrWillChangePlaymode;
#else
                IsEditor = Application.isEditor;
#endif

                if (string.IsNullOrEmpty(Time))
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Unity durumu okunamadi: " + ex.Message);
            }

            return this;
        }

        public static UnityContext Capture()
        {
            return new UnityContext().EnrichEditorState();
        }

        public void Clear()
        {
            ProjectName = string.Empty;
            ProjectPath = string.Empty;
            UnityVersion = string.Empty;
            Platform = string.Empty;

            IsEditor = false;
            IsPlaying = false;
            IsCompiling = false;
            IsUpdating = false;

            ActiveScene = string.Empty;
            ActiveScenePath = string.Empty;
            SelectedObject = string.Empty;
            Time = string.Empty;
        }
    }
}
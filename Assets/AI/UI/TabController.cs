using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI
{
    /// <summary>
    /// Sekmeli ekranlar için tab kontrolcüsü: bir nav butonuna týklandýðýnda ilgili
    /// paneli açar, diðerlerini kapatýr, ve butonlarýn görsel durumunu (arka plan rengi,
    /// yazý rengi, sol kenar göstergesi) günceller.
    ///
    /// Bu script UI'ý OLUÞTURMAZ - zaten var olan UI elemanlarýný birbirine baðlar.
    /// AI ajaný bunu execute_code ile sahneye ekleyip doldurur; elle de doldurulabilir.
    ///
    /// NEDEN AYRI BÝR SCRIPT: manage_components ile bir butonun onClick olayýna metot
    /// baðlanamýyor - Unity'nin API sýnýrý. Görsel eleman oluþturmakla davranýþ eklemek
    /// iki ayrý iþ.
    /// </summary>
    public class TabController : MonoBehaviour
    {
        [Serializable]
        public class Tab
        {
            [Tooltip("Sadece Inspector'da okunabilirlik için - koda etkisi yok.")]
            public string tabName = "Tab";

            [Tooltip("Týklanacak nav butonu (örn. NavGeneral). Button component'i olmalý.")]
            public Button navButton;

            [Tooltip("Bu sekme seçiliyken görünecek panel. Tüm sekmeler ayný paneli paylaþabilir - o durumda sadece renkler deðiþir.")]
            public GameObject contentPanel;

            [Tooltip("Opsiyonel: nav butonunun sol kenarýndaki accent çizgi. Boþ býrakýlabilir.")]
            public GameObject indicator;

            [Tooltip("Opsiyonel: nav butonunun yazýsý. Seçiliyken rengi deðiþir. Boþ býrakýlabilir.")]
            public TextMeshProUGUI label;
        }

        [Header("Sekmeler")]
        public List<Tab> tabs = new List<Tab>();

        [Header("Baþlangýç")]
        [Tooltip("Play'e basýldýðýnda hangi sekme açýk olsun (0 = listedeki ilk sekme).")]
        public int startingTabIndex = 0;

        [Header("Seçili sekme görünümü")]
        public Color selectedBackground = new Color(0.486f, 0.361f, 1f, 1f);
        public Color selectedTextColor = new Color(0.949f, 0.957f, 0.973f, 1f);

        [Header("Seçili olmayan sekme görünümü")]
        [Tooltip("Alfa 0 YAPMAYIN: Unity alfa 0 olan bir Image'ý raycast'te atlar ve butona týklanamaz hale gelir. 0.004 gözle görünmez ama týklanabilir kalýr.")]
        public Color unselectedBackground = new Color(0f, 0f, 0f, 0.004f);
        public Color unselectedTextColor = new Color(0.604f, 0.639f, 0.710f, 1f);

        public int CurrentTabIndex { get; private set; } = -1;

        /// <summary>Sekme deðiþtiðinde tetiklenir - ses efekti, kaydetme vb. için.</summary>
        public event Action<int> OnTabChanged;

        private void Start()
        {
            if (!ValidateSetup())
            {
                // Devam etmek NullReferenceException yaðmuruna yol açar.
                enabled = false;
                return;
            }

            WireUpButtons();
            SelectTab(Mathf.Clamp(startingTabIndex, 0, tabs.Count - 1), force: true);
        }

        /// <summary>
        /// Kurulumu kontrol eder. Eksik varsa HANGÝ sekmede NE eksik olduðunu tam olarak
        /// yazar - "bir yerde null var" demez.
        /// </summary>
        private bool ValidateSetup()
        {
            if (tabs == null || tabs.Count == 0)
            {
                Debug.LogError($"[TabController] '{name}' has no tabs configured.", this);
                return false;
            }

            bool ok = true;

            for (int i = 0; i < tabs.Count; i++)
            {
                Tab tab = tabs[i];
                string label = string.IsNullOrWhiteSpace(tab.tabName) ? $"#{i}" : $"'{tab.tabName}' (#{i})";

                if (tab.navButton == null)
                {
                    Debug.LogError($"[TabController] Tab {label}: 'navButton' is not assigned.", this);
                    ok = false;
                }

                if (tab.contentPanel == null)
                {
                    Debug.LogError($"[TabController] Tab {label}: 'contentPanel' is not assigned.", this);
                    ok = false;
                }

                // Ayný butonun iki sekmeye atanmasý sessiz bir hata olurdu.
                for (int j = i + 1; j < tabs.Count; j++)
                {
                    if (tab.navButton != null && tab.navButton == tabs[j].navButton)
                    {
                        Debug.LogError($"[TabController] Tab {label} and #{j} share the same button ('{tab.navButton.name}').", this);
                        ok = false;
                    }
                }
            }

            if (!ok)
            {
                Debug.LogError($"[TabController] '{name}' disabled - fix the errors above.", this);
            }

            return ok;
        }

        /// <summary>
        /// Her butonun onClick olayýna ilgili sekmeyi baðlar.
        ///
        /// RemoveAllListeners ÖNEMLÝ: Start birden fazla kez çalýþýrsa dinleyiciler üst
        /// üste binerdi ve tek týklama birden fazla kez tetiklenirdi.
        /// </summary>
        private void WireUpButtons()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i; // Closure tuzaðý: doðrudan 'i' kullanýlýrsa tüm butonlar
                               // son indeksi yakalar ve hepsi ayný sekmeyi açar.

                tabs[i].navButton.onClick.RemoveAllListeners();
                tabs[i].navButton.onClick.AddListener(() => SelectTab(index));
            }
        }

        /// <summary>
        /// Verilen sekmeyi açar, diðerlerini kapatýr. Dýþarýdan da çaðrýlabilir.
        /// </summary>
        public void SelectTab(int index) => SelectTab(index, force: false);

        private void SelectTab(int index, bool force)
        {
            if (tabs == null || tabs.Count == 0)
                return;

            if (index < 0 || index >= tabs.Count)
            {
                Debug.LogWarning($"[TabController] Invalid tab index {index}. Valid range: 0-{tabs.Count - 1}.", this);
                return;
            }

            // Ayný sekmeye tekrar týklamak zararsýz ama gereksiz - ve OnTabChanged'ý boþ
            // yere tetiklemek aboneleri þaþýrtýr. 'force' sadece Start için.
            if (!force && index == CurrentTabIndex)
                return;

            for (int i = 0; i < tabs.Count; i++)
            {
                ApplyTabState(tabs[i], i == index);
            }

            CurrentTabIndex = index;
            OnTabChanged?.Invoke(index);
        }

        /// <summary>Sekme adýyla seçim - indeks ezberlemek yerine okunabilir kod için.</summary>
        public void SelectTabByName(string tabName)
        {
            if (string.IsNullOrWhiteSpace(tabName))
                return;

            for (int i = 0; i < tabs.Count; i++)
            {
                if (string.Equals(tabs[i].tabName, tabName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectTab(i);
                    return;
                }
            }

            Debug.LogWarning($"[TabController] No tab named '{tabName}'.", this);
        }

        /// <summary>
        /// Bir sekmenin tüm görsel durumunu tek yerden yönetir.
        ///
        /// Opsiyonel alanlar (indicator, label) null olabilir - atlanýr, hata verilmez.
        ///
        /// PANEL PAYLAÞIMI: birden fazla sekme ayný paneli gösteriyorsa (henüz ayrý
        /// paneller yapýlmadýysa) o paneli kapatmýyoruz - aksi halde hiçbir sekmede
        /// içerik görünmezdi.
        /// </summary>
        private void ApplyTabState(Tab tab, bool isSelected)
        {
            if (tab.contentPanel != null)
            {
                bool sharedWithAnotherTab = false;

                if (!isSelected)
                {
                    foreach (Tab other in tabs)
                    {
                        if (other != tab && other.contentPanel == tab.contentPanel)
                        {
                            sharedWithAnotherTab = true;
                            break;
                        }
                    }
                }

                if (isSelected || !sharedWithAnotherTab)
                {
                    tab.contentPanel.SetActive(isSelected);
                }
            }

            if (tab.navButton != null)
            {
                // targetGraphic yerine doðrudan Image arýyoruz: nav butonlarýnda Image
                // her zaman ayný GameObject'te ve targetGraphic bazen atanmamýþ oluyor.
                Image background = tab.navButton.GetComponent<Image>();
                if (background != null)
                {
                    background.color = isSelected ? selectedBackground : unselectedBackground;
                }
            }

            if (tab.label != null)
            {
                tab.label.color = isSelected ? selectedTextColor : unselectedTextColor;
            }

            if (tab.indicator != null)
            {
                tab.indicator.SetActive(isSelected);
            }
        }

        private void OnValidate()
        {
            startingTabIndex = (tabs != null && tabs.Count > 0)
                ? Mathf.Clamp(startingTabIndex, 0, tabs.Count - 1)
                : 0;
        }
    }
}
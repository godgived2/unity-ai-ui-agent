using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityPackageManagerInfo = UnityEditor.PackageManager.PackageInfo;
#endif

namespace AI.Context
{
    /// <summary>
    /// Unity Package bilgilerini temsil eder.
    ///
    /// Bu projede asýl deðeri þu: UI kodu yazan bir agent'ýn TextMeshPro'nun kurulu olup
    /// olmadýðýný bilmesi gerekir. TMP yoksa model TMP_Text kullanan bir script yazar,
    /// derleme kýrýlýr ve watchdog rollback'e gider - hepsi context'te tek satýrla
    /// önlenebilecek bir israf.
    /// </summary>
    [Serializable]
    public class PackageContext
    {
        // Agent kararlarýný gerçekten deðiþtiren paketler. Render() yalnýzca bunlarý
        // yazar; 40 paketin tamamýný prompt'a dökmenin hiçbir faydasý yok.
        public const string TextMeshPro = "com.unity.textmeshpro";
        public const string UGui = "com.unity.ugui";
        public const string InputSystem = "com.unity.inputsystem";
        public const string Addressables = "com.unity.addressables";
        public const string Cinemachine = "com.unity.cinemachine";
        public const string Newtonsoft = "com.unity.nuget.newtonsoft-json";
        public const string Localization = "com.unity.localization";

        private static readonly string[] RelevantPackages =
        {
            TextMeshPro, UGui, InputSystem, Addressables, Cinemachine, Newtonsoft, Localization
        };

        // ==============================
        // PACKAGE COUNT
        // ==============================

        public int PackageCount;

        // ==============================
        // PACKAGES
        // ==============================

        public List<UnityPackageInfo> Packages = new List<UnityPackageInfo>();

        [NonSerialized]
        private Dictionary<string, UnityPackageInfo> _byName;

        // ==============================
        // ADD PACKAGE
        // ==============================

        public void AddPackage(UnityPackageInfo package)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Name))
                return;

            EnsureIndex();

            if (_byName.ContainsKey(package.Name))
                return;

            Packages.Add(package);
            _byName[package.Name] = package;
            PackageCount = Packages.Count;
        }

        private void EnsureIndex()
        {
            if (_byName != null)
                return;

            _byName = new Dictionary<string, UnityPackageInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityPackageInfo package in Packages)
            {
                if (package != null && !string.IsNullOrWhiteSpace(package.Name))
                    _byName[package.Name] = package;
            }
        }

        // ==============================
        // FIND PACKAGE
        // ==============================

        /// <summary>
        /// Eski hâlde '==' ile tam eþleþme aranýyordu ve null paket adýnda
        /// NullReferenceException atýyordu. Artýk büyük/küçük harf duyarsýz ve güvenli.
        /// </summary>
        public bool HasPackage(string packageName)
        {
            return TryGetPackage(packageName, out _);
        }

        public bool TryGetPackage(string packageName, out UnityPackageInfo package)
        {
            package = null;

            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            EnsureIndex();
            return _byName.TryGetValue(packageName, out package);
        }

        public string GetVersion(string packageName)
        {
            return TryGetPackage(packageName, out UnityPackageInfo package)
                ? package.Version
                : null;
        }

        // Agent'ýn en sýk ihtiyaç duyduðu kýsayollar.
        public bool HasTextMeshPro => HasPackage(TextMeshPro);
        public bool HasUGui => HasPackage(UGui);
        public bool HasNewInputSystem => HasPackage(InputSystem);
        public bool HasNewtonsoftJson => HasPackage(Newtonsoft);

        // ==============================
        // CLEAR
        // ==============================

        public void Clear()
        {
            Packages.Clear();
            EnsureIndex();
            _byName.Clear();
            PackageCount = 0;
        }

        // ==============================
        // CAPTURE
        // ==============================

        /// <summary>
        /// Kurulu paketleri okur.
        ///
        /// GetAllRegisteredPackages() senkron çalýþýr - Client.List() gibi asenkron bir
        /// istek baþlatýp beklemeye gerek yoktur, dolayýsýyla ReAct döngüsünü bloklamaz.
        /// </summary>
        public static PackageContext Capture()
        {
            var context = new PackageContext();

#if UNITY_EDITOR
            try
            {
                UnityPackageManagerInfo[] packages = UnityPackageManagerInfo.GetAllRegisteredPackages();

                if (packages == null)
                    return context;

                foreach (UnityPackageManagerInfo info in packages)
                {
                    if (info == null)
                        continue;

                    context.AddPackage(new UnityPackageInfo
                    {
                        Name = info.name,
                        Version = info.version,
                        Source = info.source.ToString(),
                        DisplayName = info.displayName,
                        Description = string.Empty // Açýklamalar uzun ve prompt'a girmiyor.
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AI Context] Paketler okunamadi: " + ex.Message);
            }
#endif

            return context;
        }

        // ==============================
        // RENDER
        // ==============================

        public string GetSummary()
        {
            return Render();
        }

        /// <summary>
        /// Yalnýzca agent kararýný deðiþtiren paketleri yazar. Tam listeyi asla dökmez.
        /// </summary>
        public string Render(int charBudget = 0)
        {
            if (PackageCount == 0)
                return "Packages: okunamadi.";

            var sb = new StringBuilder();
            sb.Append("Packages: ").Append(PackageCount).Append(" kurulu");

            var present = new List<string>();
            var missing = new List<string>();

            foreach (string name in RelevantPackages)
            {
                if (TryGetPackage(name, out UnityPackageInfo package))
                {
                    present.Add(ShortName(name) +
                                (string.IsNullOrEmpty(package.Version) ? string.Empty : " " + package.Version));
                }
                else
                {
                    missing.Add(ShortName(name));
                }
            }

            sb.AppendLine();

            if (present.Count > 0)
                sb.Append("  var: ").AppendLine(ContextFormat.JoinLimited(present, present.Count));

            if (missing.Count > 0)
                sb.Append("  YOK: ").AppendLine(ContextFormat.JoinLimited(missing, missing.Count));

            if (!HasTextMeshPro)
            {
                sb.AppendLine("  DIKKAT: TextMeshPro kurulu degil - TMP_Text / TMP_InputField " +
                              "kullanan script yazma, derleme kirilir.");
            }

            return ContextFormat.Truncate(sb.ToString().TrimEnd(), charBudget);
        }

        private static string ShortName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return string.Empty;

            int lastDot = packageName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < packageName.Length - 1
                ? packageName.Substring(lastDot + 1)
                : packageName;
        }

        public override string ToString()
        {
            return Render();
        }
    }

    // =====================================
    // PACKAGE MODEL
    // =====================================

    [Serializable]
    public class UnityPackageInfo
    {
        public string Name = string.Empty;
        public string Version = string.Empty;
        public string Source = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Version) ? Name : Name + "@" + Version;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;

namespace AI.Context
{
    /// <summary>
    /// Context dosyalarýnýn ortak metin yardýmcýlarý.
    ///
    /// Neden ayrý dosya: kýrpma, Türkçe-güvenli karþýlaþtýrma ve sýnýrlý liste birleþtirme
    /// her context sýnýfýnda tekrar ediyordu. Tek yerde toplanýnca davranýþ her bölümde
    /// ayný oluyor ve bütçe hesabý öngörülebilir kalýyor.
    /// </summary>
    public static class ContextFormat
    {
        public const string TruncationMark = "\n... (kirpildi)";

        /// <summary>
        /// Metni karakter bütçesine sýðdýrýr. budget &lt;= 0 ise kýrpma yapmaz.
        /// </summary>
        public static string Truncate(string text, int budget)
        {
            if (string.IsNullOrEmpty(text) || budget <= 0 || text.Length <= budget)
                return text ?? string.Empty;

            int keep = Math.Max(0, budget - TruncationMark.Length);

            if (keep == 0)
                return TruncationMark.TrimStart();

            // Kelime ortasýndan kesmemeye çalýþ.
            int cut = text.LastIndexOfAny(new[] { ' ', '\n' }, Math.Min(keep - 1, text.Length - 1));

            if (cut < keep / 2)
                cut = keep;

            return text.Substring(0, cut).TrimEnd() + TruncationMark;
        }

        /// <summary>
        /// Kültürden baðýmsýz küçük harfe çevirir.
        ///
        /// ToLower() KULLANMAYIN: Türkçe kültürde 'I' -> 'ý' olur ve "Image".ToLower()
        /// aramada "image" ile eþleþmez. Editor tr-TR ile çalýþtýðýnda sessizce hiçbir
        /// sonuç dönmemesinin sebebi tam olarak budur.
        /// </summary>
        public static string LowerInvariant(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.ToLowerInvariant();
        }

        public static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;

            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// En fazla <paramref name="max"/> öðeyi virgülle birleþtirir, kalaný "+N daha"
        /// olarak bildirir.
        /// </summary>
        public static string JoinLimited(IEnumerable<string> items, int max, string separator = ", ")
        {
            if (items == null || max <= 0)
                return string.Empty;

            var sb = new StringBuilder();
            int shown = 0;
            int hidden = 0;

            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                if (shown >= max)
                {
                    hidden++;
                    continue;
                }

                if (shown > 0)
                    sb.Append(separator);

                sb.Append(item);
                shown++;
            }

            if (hidden > 0)
                sb.Append(separator).Append('+').Append(hidden).Append(" daha");

            return sb.ToString();
        }

        /// <summary>Dosya yolundan yalnýzca dosya adýný alýr.</summary>
        public static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            int slash = path.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
        }

        /// <summary>Bayt sayýsýný okunabilir hâle getirir.</summary>
        public static string ByteSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";

            if (bytes < 1024L * 1024L)
                return (bytes / 1024f).ToString("0.#") + " KB";

            if (bytes < 1024L * 1024L * 1024L)
                return (bytes / (1024f * 1024f)).ToString("0.#") + " MB";

            return (bytes / (1024f * 1024f * 1024f)).ToString("0.#") + " GB";
        }

        /// <summary>Boþ olmayan bir satýrý baþlýklý olarak ekler.</summary>
        public static void AppendLabeled(StringBuilder sb, string label, string value)
        {
            if (sb == null || string.IsNullOrWhiteSpace(value))
                return;

            sb.Append(label).Append(": ").AppendLine(value.Trim());
        }
    }
}
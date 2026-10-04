using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Utils
{
    /// <summary>
    /// String iþlemleri için yardýmcý sýnýf.
    /// </summary>
    public static class StringUtility
    {


        // =====================================
        // CLEAN
        // =====================================

        public static string Clean(
            string value)
        {

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;



            return value
                .Trim();

        }





        // =====================================
        // CONTAINS
        // =====================================

        public static bool Contains(
            string source,
            string value)
        {

            if (string.IsNullOrEmpty(source) ||
               string.IsNullOrEmpty(value))
                return false;



            return source
                .IndexOf(
                    value,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;

        }





        // =====================================
        // WORDS
        // =====================================

        public static List<string> GetWords(
            string text)
        {

            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();



            return text
                .Split(
                    new[]
                    {
                        ' ',
                        '\n',
                        '\r',
                        '\t'
                    },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .ToList();

        }





        // =====================================
        // TRUNCATE
        // =====================================

        public static string Truncate(
            string text,
            int length)
        {

            if (string.IsNullOrEmpty(text))
                return string.Empty;



            if (text.Length <= length)
                return text;



            return text.Substring(
                0,
                length
            )
            +
            "...";

        }





        // =====================================
        // NORMALIZE NAME
        // =====================================

        public static string NormalizeName(
            string name)
        {

            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;



            return name
                .Trim()
                .Replace(
                    " ",
                    "_"
                )
                .ToLowerInvariant();

        }





        // =====================================
        // EQUALS IGNORE CASE
        // =====================================

        public static bool EqualsIgnoreCase(
            string a,
            string b)
        {

            return string.Equals(
                a,
                b,
                StringComparison.OrdinalIgnoreCase
            );

        }

    }

}
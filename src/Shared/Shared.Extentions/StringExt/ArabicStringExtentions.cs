using System.Text.RegularExpressions;

namespace Shared.Extentions.StringExt
{
    public static class ArabicStringExtentions
    {
        /// <summary>
        /// Removes Arabic diacritical marks from the given text.
        /// </summary>
        /// <param name="text">The text to remove diacritical marks from.</param>
        /// <returns>The text without diacritical marks.</returns>
        /// Unicode range U+064B to U+065F covers most Arabic diacritical marks.
        public static string RemoveTashkeel(this string text)
        {
            return Regex.Replace(text, @"[\u064B-\u065F\u0670]", string.Empty);
        }

        /// <summary>
        /// Removes Arabic diacritics: fatha, damma, kasra, shadda, sukun, tanwin, etc.
        /// Unicode range U+064B to U+065F covers most Arabic diacritical marks.
        /// </summary>
        public static string RemoveAllArabicDiacritics(this string text)
        {
            // \u0600-\u0605 = Arabic number signs and markers
            // \u064B-\u065F = Arabic diacritics (tashkeel)
            // \u0670       = Arabic letter superscript alef
            return Regex.Replace(text, @"[\u0600-\u0605\u064B-\u065F\u0670]", string.Empty);
        }

    }
}

namespace LisanBits.DataPipeline.Processing;

/// <summary>
/// High-performance string manipulation for Arabic NLP.
/// Uses ReadOnlySpan and string.Create to eliminate GC allocations during text preprocessing.
/// </summary>
public static class ArabicTextProcessor
{
    // Ranges of Arabic Tashkeel (Diacritics) in Unicode
    // U+064B to U+065F, and some others like Shadda, Sukun, Fathatan, etc.
    // 0x064B = Fathatan
    // 0x0652 = Sukun
    // 0x0670 = Dagger Alif
    
    /// <summary>
    /// Strips Arabic vowels (Tashkeel) from a given string.
    /// Allocates EXACTLY 1 new string with zero intermediate buffers.
    /// </summary>
    /// <param name="input">The string to process.</param>
    /// <returns>A new string stripped of Tashkeel.</returns>
    public static string StripTashkeel(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        ReadOnlySpan<char> span = input.AsSpan();
        
        // First pass: Count how many characters we will actually keep
        int validCharCount = 0;
        foreach (char c in span)
        {
            if (!IsTashkeel(c))
            {
                validCharCount++;
            }
        }

        // If no Tashkeel was found, return the original string to save allocation
        if (validCharCount == input.Length)
        {
            return input;
        }

        // Second pass: Create the new string with exact size
        return string.Create(validCharCount, input, (chars, state) =>
        {
            int index = 0;
            foreach (char c in state.AsSpan())
            {
                if (!IsTashkeel(c))
                {
                    chars[index++] = c;
                }
            }
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsTashkeel(char c)
    {
        // 0x064B - 0x065F covers the standard Arabic diacritics
        // 0x0670 is the superscript alif
        return (c >= 0x064B && c <= 0x065F) || c == 0x0670;
    }
}

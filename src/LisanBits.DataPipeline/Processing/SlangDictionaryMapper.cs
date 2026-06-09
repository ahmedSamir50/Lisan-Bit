using System.Collections.Frozen;

namespace LisanBits.DataPipeline.Processing;

public class SlangDictionaryMapper
{
    private readonly FrozenDictionary<string, string> _slangToRootDict;

    public SlangDictionaryMapper()
    {
        // For Epic 1, we hardcode the seed dictionary to guarantee extreme lookup speeds.
        // In production, this dictionary could be built from an external JSON file on startup.
        var dict = new Dictionary<string, string>
        {
            { "عبيط", "ع ب ط" },
            { "يلا", "و ل ي" },
            { "عشان", "ش ي أ" }, // Roughly maps to شئ
            { "دلوقتي", "و ق ت" },
            { "إزيك", "ز ي ي" },
            { "بتاع", "ت ب ع" }
        };

        // Freeze the dictionary for O(1) lock-free read-optimized access.
        _slangToRootDict = dict.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Attempts to map a slang word to a formal root. 
    /// First strips Tashkeel to normalize the input.
    /// </summary>
    public bool TryGetFormalRoot(string slangWord, out string? formalRoot)
    {
        var normalized = ArabicTextProcessor.StripTashkeel(slangWord);
        return _slangToRootDict.TryGetValue(normalized, out formalRoot);
    }
}

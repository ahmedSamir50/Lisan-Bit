using HtmlAgilityPack;
using LisanBits.DataPipeline.Data.Models;
using Shared.Extentions.StringExt;
using System.Text;
using System.Text.RegularExpressions;

namespace LisanBits.DataPipeline.Preprocessing;

public class LexiconParser
{
    private static readonly HashSet<string> ScholarNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ابن الأعرابي", "ابن سيده", "ابن بري", "أبو منصور", "أبو عبيد", "الزمخشري", "الجوهري", "الأزهري", 
        "الليث", "سيبويه", "ثعلب", "ابن سيرين", "الأصمعي", "ابن السكيت", "أبو زيد", "أبو عمرو", "أبو حنيفة", 
        "ابن الأثير", "اللحاني", "الجرجاني", "الفراء", "أبو عبيدة", "أبو بكر", "ابن سيدة الأندلسي", "السيوطي",
        "صاحب الصحاح", "أحمد بن فارس", "رضي الدين الصاغاني", "الصاغاني", "جمال الدين", "الفيروزابادي", "أبو الحسن",
        "المصنف", "أبو الدقيش", "أبو زبيد الطائي", "أبو زبيد", "الطائي", "الخليل", "قال الخليل", "ابن بري قال",
        "رؤبة", "عمر", "علي", "أبو عبد الله", "أبو العباس", "أبو حاتم", "ابن عباس", "ابن مسعود", "مجاهد", 
        "قتادة", "الشعبي", "الحسن", "سعيد بن جبير", "الفارسي", "ابن جني", "أبو علي", "أبو الفتح", "سعيد",
        "أبو تراب", "أبو عبيدة معمر بن المثنى", "معمر", "أبو مالك", "ابن مقبل", "رؤبة بن العجاج", "العجاج",
        "الفرزدق", "جرير", "ذو الرمة", "شمر", "أبو عمرو بن العلاء", "أبو سعيد", "النضر", "النضر بن شميل",
        "ابن شميل", "الأحمر", "أبو الهيثم", "ابن الأعرابي قال", "قال ابن بري", "أبو حنيفة الدينوري", "الدينوري",
        "أبو حنيفة قال", "ابن دريد", "أبو بكر بن دريد", "كراع", "أبو الحسن اللحياني", "أبو معاذ", "أبو خيرة"
    };

    private static readonly HashSet<string> Weights = new(StringComparer.OrdinalIgnoreCase)
    {
        "ككتف", "كحلزون", "كزبير", "كأمير", "كغراب", "كشداد", "كرمان", "كسحاب", "كجعفر", "كقنفذ", "كدرهم", 
        "كمقعد", "كمعظم", "كمنبر", "كصرد", "كعنب", "كزفر", "كهمزة", "كعفتان", "كعفّتان", "ككيزان", "كسروال",
        "كتوبة", "كثمامة", "كقنديل", "كقندل", "ككتاب", "كصاحب", "كعصفور", "كصبور", "كعنق", "كجبل"
    };

    private static readonly HashSet<string> GrammarTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ج", "منها", "وكأنه", "بالضم", "بالفتح", "بالكسر", "وبالكسر", "وبالضم", "وبالفتح", "وبالكسر والضم", 
        "وبهاء", "مشددة", "محرّكة", "محركة", "مشددةً", "محركةً", "وبالضمّ", "وبالكسرِ", "بالضمِّ", "ة", "ع", "د", 
        "ج:", "ع:", "ة:", "وع", "وة", "ود", "م", "بالضم والكسر", "مفتوحة", "مكسورة", "مضمومة", "وبالفتح والضم",
        "تثنية", "الجمع", "التصغير", "بالضم و الفتح", "بالضم والفتح", "مكسور", "مفتوح", "مضموم",
        "نادر", "نادرا", "قليل", "قليلا", "كثير", "كثيرا", "غالبا", "غالب", "جماعة", "محدث", "محدثون", "صحابة", "صحابي", "شاعر", "لقب",
        "خمسة", "ثلاثة", "أربعة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة", "واحد", "اثنان"
    };

    private static readonly HashSet<string> ContextMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "وفي الحديث", "وفي حديث", "وقيل", "يقال", "ويقال", "حكى", "حكي", "قال", "وقال", "وقاله", "حكاه", "ذكره",
        "وفي الصحاح", "وفي التهذيب", "تقول منه", "بلا همز", "بغير هاء", "بالتخفيف", "وبالتخفيف", "نسبة", "اسم",
        "وبخط بعضهم", "ويمد", "ويضم", "بضمهما", "بفتح", "بالفتح", "بكسر", "بالكسر", "ضم", "الضم", "فتح", "الفتح",
        "كسر", "الكسر", "قلت", "يروى", "يروي", "روى", "روي", "بعضهم", "وبعضهم", "الشارح", "المؤلف", "المصنف",
        "قيل", "الكاتب", "المؤرخ"
    };

    private static readonly HashSet<string> PrepositionsAndConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "من", "في", "على", "إلى", "الى", "عن", "مع", "بين", "تحت", "فوق", "عند", "لدى", "خلف", "أمام", "امام",
        "أو", "او", "أم", "ام", "ثم", "بل", "لا", "ما", "لم", "لن", "لو", "إذا", "اذا", "أن", "ان", "إن", "إنما", "انما",
        "إلا", "الا", "حتى", "حتي", "كي", "إذ", "اذ", "هو", "هي", "هم", "هن", "هذا", "هذه", "ذلك", "تلك", "الذي", "التي",
        "ابن", "ابنا", "بنو", "بني", "أبو", "أبي", "أبا", "وابن", "وبنو", "وبني", "وأبو", "وأبي", "وهو", "وهي", "وهما", "وهم", "وهن", "ومن", "وفي", "وعلى", "وعن", "وإلى", "وإذ", "وإذا", "وأن", "وإن", "وإلا", "ولكن", "بلا", "وبلا"
    };

    private static readonly HashSet<string> BreakPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "من", "في", "على", "إلى", "الى", "عن", "مع", "بين", "تحت", "فوق", "عند", "لدى", "خلف", "أمام", "امام",
        "ثم", "بل", "لا", "ما", "لم", "لن", "لو", "إذا", "اذا", "أن", "ان", "إن", "إنما", "انما", "إلا", "الا",
        "حتى", "حتي", "كي", "إذ", "اذ", "لأن", "لان", "لأنه", "لانه", "لأنها", "لانها", "لكونه", "لكونها",
        "هو", "هي", "هم", "هن", "هذا", "هذه", "ذلك", "تلك", "الذي", "التي",
        "ابن", "ابنا", "بنو", "بني", "أبو", "أبي", "أبا", "وابن", "وبنو", "وبني", "وأبو", "وأبي", "وهو", "وهي", "وهما", "وهم", "وهن", "ومن", "وفي", "وعلى", "وعن", "وإلى", "وإذ", "وإذا", "وأن", "وإن", "وإلا", "ولكن", "بلا", "وبلا"
    };

    public static List<LexiconEntry> ParseHtml(string html, int bookId, string bookName)
    {
        var entries = new List<LexiconEntry>();
        if (string.IsNullOrEmpty(html)) return entries;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var paragraphs = doc.DocumentNode.SelectNodes("//div[contains(@class, 'nass')]/p");
        if (paragraphs == null) return entries;

        string activeRoot = "أصل_عام";
        bool justFoundRootHeader = false;

        foreach (var p in paragraphs)
        {
            string text = p.InnerText.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            if (bookId == 150964)
            {
                // Timur's Slang
                var slangEntry = ParseSlangParagraph(p, bookName);
                if (slangEntry != null && 
                    !slangEntry.Root.Contains(' ') && 
                    IsValidWordStructure(slangEntry.Word) && 
                    !IsSkipWord(slangEntry.Word))
                {
                    entries.Add(slangEntry);
                }
            }
            else
            {
                string? newRoot = DetectRootHeader(bookId, text);
                if (!string.IsNullOrEmpty(newRoot))
                {
                    activeRoot = newRoot;
                    justFoundRootHeader = true;
                    continue;
                }

                var subEntries = ParseParagraph(p, activeRoot, justFoundRootHeader, bookName);
                foreach (var entry in subEntries)
                {
                    if (entry.Word.Length >= 2 && 
                        !entry.Root.Contains(' ') && 
                        IsValidWordStructure(entry.Word) && 
                        !IsSkipWord(entry.Word))
                    {
                        entries.Add(entry);
                    }
                }
                justFoundRootHeader = false;
            }
        }

        return entries;
    }

    private static bool IsValidWordStructure(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        string cleaned = word.Trim();
        var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1) return true;

        if (parts.Length == 2)
        {
            string secondWord = RemoveTashkeel(parts[1]);
            if (secondWord.StartsWith("و") && secondWord.Length > 2)
            {
                return true;
            }
        }

        if (parts.Length == 3)
        {
            string middle = RemoveTashkeel(parts[1]);
            if (middle == "او" || middle == "أو")
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPrepositions(string text)
    {
        var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (PrepositionsAndConjunctions.Contains(part))
            {
                return true;
            }
        }
        return false;
    }

    private static string StripCitations(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string cleaned = text;

        cleaned = Regex.Replace(cleaned, @"«[^»]*»", "");
        cleaned = Regex.Replace(cleaned, @"""[^""]*""", "");
        cleaned = Regex.Replace(cleaned, @"\{[^\}]*\}", "");

        return cleaned;
    }

    private static void ExtractWordsFromSegment(string val, List<string> list)
    {
        if (string.IsNullOrEmpty(val)) return;

        var parts = val.Split(new[] { ' ', '،', ',', '؛', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            string normPart = RemoveTashkeel(part);
            if (BreakPrepositions.Contains(normPart))
            {
                break;
            }

            string cleaned = CleanWord(part);
            if (cleaned.Length >= 2 && !IsSkipWord(cleaned) && !PrepositionsAndConjunctions.Contains(RemoveTashkeel(cleaned)))
            {
                list.Add(cleaned);
            }
        }
    }

    private static void ParseSemanticRelations(string definition, string headword, List<string> syns, List<string> ants, List<string> plurs)
    {
        if (string.IsNullOrEmpty(definition)) return;

        string strippedDef = StripCitations(definition);
        string cleanDef = strippedDef.Replace('؛', '،').Replace(';', '،');
        var segments = cleanDef.Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var seg in segments)
        {
            string trimmed = seg.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string normalized = RemoveTashkeel(trimmed).Trim();

            // 1. Plurals
            var plurMatch = Regex.Match(normalized, @"^(?:ج\b|جمع\b|الجمع\b|والجمع\b|جمعها\b|وجمعها\b|جمعه\b|وجمعه\b|جموع\b|الجموع\b|والجموع\b|جموعها\b|وجموعها\b)\s*:?\s*[\(\)]?\s*(?<val>[\u0621-\u064A\s]{2,40})");
            if (!plurMatch.Success)
            {
                plurMatch = Regex.Match(normalized, @"^\((?:ج|جمع)\)\s*(?<val>[\u0621-\u064A\s]{2,40})");
            }
            if (plurMatch.Success)
            {
                string val = plurMatch.Groups["val"].Value;
                ExtractWordsFromSegment(val, plurs);
                continue;
            }

            // 2. Antonyms
            var antMatch = Regex.Match(normalized, @"^(?:ضد\b|وضده\b|النقيض\b|ونقيضه\b|خلاف\b|عكس\b)\s*:?\s*(?<val>[\u0621-\u064A\s]{2,40})");
            if (antMatch.Success)
            {
                string val = antMatch.Groups["val"].Value;
                ExtractWordsFromSegment(val, ants);
                continue;
            }

            // 3. Synonyms (Explicit)
            var synMatch = Regex.Match(normalized, @"\b(?:أى|أي|بمعنى)\b\s*:?\s*(?<val>[\u0621-\u064A\s]{2,40})");
            if (synMatch.Success)
            {
                string val = synMatch.Groups["val"].Value;
                if (!ContainsPrepositions(RemoveTashkeel(val)))
                {
                    ExtractWordsFromSegment(val, syns);
                }
                continue;
            }

            // 4. Fallback (Descriptive)
            if (ContainsPrepositions(normalized)) continue;

            string cleanedSeg = Regex.Replace(trimmed, @"^(?:بالضم|بالفتح|بالكسر|بالكسر والضم|بالضم والفتح|بالضم والكسر|وبالكسر|وبالضم|وبالفتح|وبهاء|محرّكة|محركة|وع|وة|ود|ك[^\s]+)\s*:?\s*", "").Trim();
            cleanedSeg = Regex.Replace(cleanedSeg, @"^\b(?:ة|ع|د|م)\b\s*:?\s*", "").Trim();
            cleanedSeg = Regex.Replace(cleanedSeg, @"^(?:ة|ع|د|م|ج):\s*", "").Trim();
            cleanedSeg = Regex.Replace(cleanedSeg, @"^[ووأأ]?ـ\s*[^\s:]*:\s*", "").Trim();
            cleanedSeg = Regex.Replace(cleanedSeg, @"^[ووأأ]?ـ\s*", "").Trim();

            string splitPattern = @"\s+\b(?:لأن|لان|لأنه|لانه|لأنها|لانها|لكونه|لكونها|بسبب|إذ|اذ)\b\s*";
            var parts = Regex.Split(cleanedSeg, splitPattern);
            if (parts.Length > 0)
            {
                cleanedSeg = parts[0].Trim();
            }

            string normCleanSeg = RemoveTashkeel(cleanedSeg).Trim();
            if (string.IsNullOrEmpty(normCleanSeg)) continue;

            var words = normCleanSeg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                if (words.Length >= 3 && (words[1] == "او" || words[1] == "أو"))
                {
                    string w1 = CleanWord(words[0]);
                    string w2 = CleanWord(words[2]);
                    if (w1.Length >= 2 && !IsSkipWord(w1) && !PrepositionsAndConjunctions.Contains(w1)) syns.Add(w1);
                    if (w2.Length >= 2 && !IsSkipWord(w2) && !PrepositionsAndConjunctions.Contains(w2)) syns.Add(w2);
                }
                else if (words.Length >= 2 && words[1].StartsWith("و") && words[1].Length > 2)
                {
                    string w1 = CleanWord(words[0]);
                    string w2 = CleanWord(words[1]);
                    if (w1.Length >= 2 && !IsSkipWord(w1) && !PrepositionsAndConjunctions.Contains(w1)) syns.Add(w1);
                    if (w2.Length >= 2 && !IsSkipWord(w2) && !PrepositionsAndConjunctions.Contains(w2)) syns.Add(w2);
                }
                else
                {
                    string w1 = CleanWord(words[0]);
                    if (w1.Length >= 2 && !IsSkipWord(w1) && !PrepositionsAndConjunctions.Contains(w1))
                    {
                        syns.Add(w1);
                    }
                }
            }
        }

        string cleanHead = CleanWord(headword);
        syns.RemoveAll(s => s == cleanHead || RemoveTashkeel(s) == RemoveTashkeel(cleanHead));
    }

    private static string? DetectRootHeader(int bookId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string textNoTashkeel = RemoveTashkeel(text).Trim();

        if (bookId == 1687)
        {
            var match = Regex.Match(textNoTashkeel, @"^\s*(?<root>[\u0621-\u064A]{3,5})\s*:\s*$");
            if (match.Success)
            {
                string root = CleanWord(match.Groups["root"].Value);
                if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
            }
        }
        else if (bookId == 7283)
        {
            var match = Regex.Match(textNoTashkeel, @"^\s*•\s*(?<root>[\u0621-\u064A]{3,5})\b");
            if (match.Success)
            {
                string root = CleanWord(match.Groups["root"].Value);
                if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
            }
        }
        else if (bookId == 7030)
        {
            var match = Regex.Match(textNoTashkeel, @"^\s*\[(?<root>[\u0621-\u064A]{3,5})\]\s*$");
            if (match.Success)
            {
                string root = CleanWord(match.Groups["root"].Value);
                if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
            }
        }
        else if (bookId == 7028)
        {
            var match = Regex.Match(textNoTashkeel, @"^\s*\((?<root>[\u0621-\u064A]{3,5})\)");
            if (match.Success)
            {
                string root = CleanWord(match.Groups["root"].Value);
                if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
            }
        }
        else if (bookId == 1682)
        {
            var chMatch = Regex.Match(textNoTashkeel, @"^\[باب\s+[\u0621-\u064A\s]+\((?<roots>[\u0621-\u064A\s،]+?)(?:مستعملان|مهملان|مستعمل|مهمل)?\)\]");
            if (chMatch.Success)
            {
                var rootsStr = chMatch.Groups["roots"].Value;
                var parts = rootsStr.Split(new[] { ' ', '،', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string root = CleanWord(parts[0]);
                    if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
                }
            }

            var match = Regex.Match(textNoTashkeel, @"^\s*(?<root>[\u0621-\u064A]{2,5})\s*:");
            if (match.Success)
            {
                string root = CleanWord(match.Groups["root"].Value);
                if (root.Length >= 2 && root.Length <= 5 && !root.Contains(' ') && !IsSkipWord(root)) return root;
            }
        }

        return null;
    }

    private static List<LexiconEntry> ParseParagraph(HtmlNode p, string activeRoot, bool justFoundRootHeader, string bookName)
    {
        var entries = new List<LexiconEntry>();
        var childNodes = p.ChildNodes;

        string? currentWord = null;
        var currentDef = new StringBuilder();

        foreach (var node in childNodes)
        {
            if (node.NodeType == HtmlNodeType.Element && (node.Name == "span" || node.Name == "b"))
            {
                var className = node.GetAttributeValue("class", "");
                if (className.Contains("c5") || className.Contains("c2") || node.Name == "b")
                {
                    string spanText = node.InnerText.Trim();
                    if (string.IsNullOrEmpty(spanText)) continue;

                    string cleanedSpan = CleanWord(spanText);
                    if (!IsSkipWord(cleanedSpan) && cleanedSpan.Length >= 2 && IsDerivativeOfRoot(cleanedSpan, activeRoot))
                    {
                        if (currentWord != null && currentDef.Length > 0)
                        {
                            var entryDef = CleanDefinition(currentDef.ToString());
                            var syns = new List<string>();
                            var ants = new List<string>();
                            var plurs = new List<string>();
                            ParseSemanticRelations(entryDef, currentWord, syns, ants, plurs);

                            entries.Add(new LexiconEntry
                            {
                                Root = activeRoot,
                                Word = currentWord,
                                Definition = entryDef,
                                Synonyms = string.Join(",", syns),
                                Antonyms = string.Join(",", ants),
                                Plurals = string.Join(",", plurs),
                                SourceBook = bookName
                            });
                        }

                        currentWord = cleanedSpan;
                        currentDef.Clear();

                        int colonIdx = spanText.IndexOf(':');
                        if (colonIdx > 0 && colonIdx < spanText.Length - 1)
                        {
                            currentDef.Append(spanText.Substring(colonIdx + 1));
                        }
                    }
                    else
                    {
                        currentDef.Append(" " + spanText);
                    }
                }
                else
                {
                    currentDef.Append(" " + node.InnerText);
                }
            }
            else if (node.NodeType == HtmlNodeType.Text)
            {
                string text = node.InnerText;
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (currentWord == null)
                {
                    var startMatch = Regex.Match(text, @"^\s*(?<word>[\u0600-\u06FF\s]+?)(?:،|,|:)\s*");
                    if (startMatch.Success)
                    {
                        string wordPart = startMatch.Groups["word"].Value.Trim();
                        string cleanedWord = CleanWord(wordPart);
                        if (!IsSkipWord(cleanedWord) && cleanedWord.Length >= 2 && IsDerivativeOfRoot(cleanedWord, activeRoot))
                        {
                            currentWord = cleanedWord;
                            currentDef.Append(text.Substring(startMatch.Index + startMatch.Length));
                            continue;
                        }
                    }

                    if (justFoundRootHeader)
                    {
                        currentWord = activeRoot;
                    }
                }

                currentDef.Append(text);
            }
        }

        if (currentWord != null && currentDef.Length > 0)
        {
            var entryDef = CleanDefinition(currentDef.ToString());
            var syns = new List<string>();
            var ants = new List<string>();
            var plurs = new List<string>();
            ParseSemanticRelations(entryDef, currentWord, syns, ants, plurs);

            entries.Add(new LexiconEntry
            {
                Root = activeRoot,
                Word = currentWord,
                Definition = entryDef,
                Synonyms = string.Join(",", syns),
                Antonyms = string.Join(",", ants),
                Plurals = string.Join(",", plurs),
                SourceBook = bookName
            });
        }

        return entries;
    }

    private static LexiconEntry? ParseSlangParagraph(HtmlNode p, string bookName)
    {
        var firstChild = p.ChildNodes.FirstOrDefault(n => 
            n.NodeType == HtmlNodeType.Element && 
            (n.Name == "span" || n.Name == "b") && 
            !n.GetAttributeValue("class", "").Contains("anchor"));
        if (firstChild == null) return null;

        string firstChildText = firstChild.InnerText.Trim();
        if (!firstChildText.EndsWith(":")) return null;

        string rawWord = firstChildText.TrimEnd(':').Trim();
        if (string.IsNullOrEmpty(rawWord)) return null;

        if (rawWord.Length > 15) return null;
        var parts = rawWord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 3) return null;

        if (rawWord.Contains("ص") || rawWord.Contains("ج") || rawWord.Contains("سنة") || 
            rawWord.Contains("كتاب") || rawWord.Contains("شرح") || rawWord.Contains("العامة") || 
            rawWord.Contains("العامّة") || rawWord.Contains("الأمير") || rawWord.Contains("ابن") || 
            rawWord.Contains("أبو") || rawWord.Contains("في") || rawWord.Contains("من"))
        {
            return null;
        }

        string word = CleanWord(rawWord);
        if (word.Length < 2) return null;

        string root = HeuristicRoot(word);

        string fullText = p.InnerText;
        int colonIdx = fullText.IndexOf(':');
        string def = colonIdx >= 0 ? fullText.Substring(colonIdx + 1) : fullText;
        string entryDef = CleanDefinition(def);

        var syns = new List<string>();
        var ants = new List<string>();
        var plurs = new List<string>();
        ParseSemanticRelations(entryDef, word, syns, ants, plurs);

        return new LexiconEntry
        {
            Root = root,
            Word = word,
            Definition = entryDef,
            Synonyms = string.Join(",", syns),
            Antonyms = string.Join(",", ants),
            Plurals = string.Join(",", plurs),
            SourceBook = bookName
        };
    }

    private static string HeuristicRoot(string word)
    {
        string strong = GetStrongConsonants(word);
        if (strong.Length >= 3 && strong.Length <= 4)
        {
            return strong;
        }
        return word;
    }

    private static bool IsDerivativeOfRoot(string word, string root)
    {
        if (string.IsNullOrEmpty(root) || root == "أصل_عام") return true;

        string rootStr = GetStrongConsonants(root);
        string wordStr = GetStrongConsonants(word);

        if (rootStr.Length == 0) return true;

        return IsSubsequence(rootStr, wordStr);
    }

    private static bool IsSubsequence(string sub, string main)
    {
        int subIdx = 0;
        int mainIdx = 0;
        while (subIdx < sub.Length && mainIdx < main.Length)
        {
            if (sub[subIdx] == main[mainIdx])
            {
                subIdx++;
            }
            mainIdx++;
        }
        return subIdx == sub.Length;
    }

    private static string GetStrongConsonants(string word)
    {
        if (string.IsNullOrEmpty(word)) return "";
        string normalized = RemoveTashkeel(word);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            if (IsStrongConsonant(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static bool IsStrongConsonant(char c)
    {
        return c == 'ب' || c == 'ت' || c == 'ث' || c == 'ج' || c == 'ح' || c == 'خ' ||
               c == 'د' || c == 'ذ' || c == 'ر' || c == 'ز' || c == 'س' || c == 'ش' ||
               c == 'ص' || c == 'ض' || c == 'ط' || c == 'ظ' || c == 'ع' || c == 'غ' ||
               c == 'ف' || c == 'ق' || c == 'ك' || c == 'ل' || c == 'م' || c == 'ن' ||
               c == 'ه';
    }

    private static string CleanDefinition(string def)
    {
        if (string.IsNullOrEmpty(def)) return "";
        
        string cleaned = def;
        cleaned = Regex.Replace(cleaned, @"«[0-9\u0660-\u0669]+»", "");
        cleaned = Regex.Replace(cleaned, @"\([0-9\u0660-\u0669]+\)", "");
        cleaned = Regex.Replace(cleaned, @"\[[0-9\u0660-\u0669]+\]", "");
        cleaned = Regex.Replace(cleaned, @"[0-9\u0660-\u0669]+", "");
        
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        cleaned = cleaned.Replace("«", "").Replace("»", "");
        return cleaned.Trim().TrimStart(':', '،', ' ', ',', ';', '؛').TrimEnd(':', '،', ' ', ',', ';', '؛');
    }

    private static bool IsSkipWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return true;
        if (word.Length <= 1) return true;

        var parts = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 4) return true;

        if (ScholarNames.Contains(word)) return true;
        if (Weights.Contains(word)) return true;
        if (GrammarTerms.Contains(word)) return true;
        if (ContextMarkers.Contains(word)) return true;

        foreach (var part in parts)
        {
            if (ScholarNames.Contains(part)) return true;
            if (GrammarTerms.Contains(part)) return true;
            if (ContextMarkers.Contains(part)) return true;
        }

        if (word.StartsWith("وقال ") || word.StartsWith("وفي ") || word.StartsWith("حكى ") || 
            word.StartsWith("حكاه ") || word.StartsWith("ذكره ") || word.StartsWith("روى ") || 
            word.StartsWith("ذكر ") || word.StartsWith("قلت "))
            return true;

        return false;
    }

    private static string CleanWord(string word)
    {
        if (string.IsNullOrEmpty(word)) return "";

        string cleaned = word.RemoveTashkeel().Trim();
        cleaned = Regex.Replace(cleaned, @"[^\u0621-\u064A\s]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        if (cleaned.StartsWith("ال")) cleaned = cleaned.Substring(2);
        else if (cleaned.StartsWith("وال")) cleaned = cleaned.Substring(3);
        else if (cleaned.StartsWith("بال")) cleaned = cleaned.Substring(3);
        else if (cleaned.StartsWith("كال")) cleaned = cleaned.Substring(3);
        else if (cleaned.StartsWith("لل")) cleaned = cleaned.Substring(2);
        else
        {
            if (cleaned.StartsWith("و") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(1);
            }
            else if (cleaned.StartsWith("ف") && cleaned.Length > 3)
            {
                cleaned = cleaned.Substring(1);
            }
        }

        return cleaned.Trim();
    }

    
}

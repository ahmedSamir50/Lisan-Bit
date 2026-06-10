namespace LisanBits.DataPipeline.Preprocessing;

public enum GrammaticalState
{
    Nominative,  // Marfu' (Noun)
    Accusative,  // Mansub (Noun)
    Genitive,    // Majrour (Noun)
    Indicative,  // Marfu' (Verb)
    Subjunctive, // Mansub (Verb)
    Jussive      // Majzoum (Verb)
}

public class ClauseFrame
{
    public string GovernorLemma { get; set; } = string.Empty;
    public string GovernorPos { get; set; } = string.Empty;
    public HashSet<GrammaticalState> SatisfiedRoles { get; } = [];
    public GrammaticalState InheritableState { get; set; } = GrammaticalState.Nominative;
}

public class SyntacticEngine
{
    private readonly Stack<ClauseFrame> _clauseStack = new();
    private GrammaticalState? _nextForcedState;
    private GrammaticalState? _lastResolvedState;

    public int StackDepth => _clauseStack.Count;

    public void Reset()
    {
        _clauseStack.Clear();
        _nextForcedState = null;
        _lastResolvedState = null;
    }

    public void PushClause(string governorLemma, string governorPos, GrammaticalState defaultInheritable = GrammaticalState.Nominative)
    {
        var frame = new ClauseFrame
        {
            GovernorLemma = governorLemma,
            GovernorPos = governorPos,
            InheritableState = defaultInheritable
        };
        _clauseStack.Push(frame);
    }

    public void PopClause()
    {
        if (_clauseStack.Count > 0)
        {
            _clauseStack.Pop();
        }
    }

    public GrammaticalState ProcessToken(string word, string pos, string root, string lemma)
    {
        // 1. Force state from immediately preceding structural trigger (e.g. preposition)
        if (_nextForcedState.HasValue)
        {
            var forced = _nextForcedState.Value;
            _nextForcedState = null;
            _lastResolvedState = forced;
            return forced;
        }

        // 2. Resolve particles and structural triggers
        if (IsPreposition(pos, lemma))
        {
            // Prepositions immediately force the next governed noun to be Genitive
            _nextForcedState = GrammaticalState.Genitive;
            _lastResolvedState = GrammaticalState.Genitive; // prepositions don't inflect, but we return Genitive for context
            return GrammaticalState.Genitive;
        }

        if (IsConjunction(pos, lemma))
        {
            // Conjunctions ('Atf) inherit the case of the preceding resolved element
            if (_lastResolvedState.HasValue)
            {
                _nextForcedState = _lastResolvedState.Value;
            }
            return _lastResolvedState ?? GrammaticalState.Nominative;
        }

        // 3. Process Verb clauses
        if (pos.StartsWith("V", StringComparison.OrdinalIgnoreCase) || pos.Equals("VERB", StringComparison.OrdinalIgnoreCase))
        {
            // Determine default verb mood (e.g. Subjunctive if preceded by particle like 'an', else Indicative)
            var mood = GrammaticalState.Indicative;
            
            // Push new verb clause frame
            PushClause(lemma, "V", GrammaticalState.Nominative);
            _lastResolvedState = mood;
            return mood;
        }

        // 4. Inna & Kana particles
        if (lemma == "إنَّ" || lemma == "ان")
        {
            PushClause(lemma, "INNA", GrammaticalState.Accusative);
            _lastResolvedState = GrammaticalState.Accusative;
            return GrammaticalState.Accusative;
        }

        if (lemma == "كَانَ" || lemma == "كان")
        {
            PushClause(lemma, "KANA", GrammaticalState.Nominative);
            _lastResolvedState = GrammaticalState.Nominative;
            return GrammaticalState.Nominative;
        }

        // 5. Nouns, Adjectives, Pronouns
        if (_clauseStack.Count > 0)
        {
            var activeFrame = _clauseStack.Peek();
            
            if (activeFrame.GovernorPos == "V")
            {
                // Verb governor expects Subject (Nominative) then Object (Accusative)
                if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Nominative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Nominative);
                    _lastResolvedState = GrammaticalState.Nominative;
                    return GrammaticalState.Nominative;
                }
                else if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Accusative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Accusative);
                    _lastResolvedState = GrammaticalState.Accusative;
                    return GrammaticalState.Accusative;
                }
            }
            else if (activeFrame.GovernorPos == "INNA")
            {
                // Inna: Subject is Accusative, Predicate is Nominative
                if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Accusative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Accusative);
                    _lastResolvedState = GrammaticalState.Accusative;
                    return GrammaticalState.Accusative;
                }
                else if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Nominative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Nominative);
                    PopClause(); // clause fully satisfied
                    _lastResolvedState = GrammaticalState.Nominative;
                    return GrammaticalState.Nominative;
                }
            }
            else if (activeFrame.GovernorPos == "KANA")
            {
                // Kana: Subject is Nominative, Predicate is Accusative
                if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Nominative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Nominative);
                    _lastResolvedState = GrammaticalState.Nominative;
                    return GrammaticalState.Nominative;
                }
                else if (!activeFrame.SatisfiedRoles.Contains(GrammaticalState.Accusative))
                {
                    activeFrame.SatisfiedRoles.Add(GrammaticalState.Accusative);
                    PopClause(); // clause fully satisfied
                    _lastResolvedState = GrammaticalState.Accusative;
                    return GrammaticalState.Accusative;
                }
            }
        }

        // Fallback default state
        var fallback = _lastResolvedState ?? GrammaticalState.Nominative;
        _lastResolvedState = fallback;
        return fallback;
    }

    private static bool IsPreposition(string pos, string lemma)
    {
        return pos.Equals("P", StringComparison.OrdinalIgnoreCase) || 
               pos.Contains("PREP", StringComparison.OrdinalIgnoreCase) || 
               lemma == "فِي" || lemma == "في" || lemma == "عَلَى" || lemma == "على" || 
               lemma == "بِ" || lemma == "لِ" || lemma == "مِنْ" || lemma == "من" || lemma == "إِلَى" || lemma == "الى";
    }

    private static bool IsConjunction(string pos, string lemma)
    {
        return pos.Contains("CONJ", StringComparison.OrdinalIgnoreCase) || 
               lemma == "وَ" || lemma == "و" || lemma == "فَ" || lemma == "ف" || lemma == "ثُمَّ" || lemma == "ثم";
    }
}

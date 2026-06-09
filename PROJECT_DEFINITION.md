# PROJECT DEFINITION: Lisan - Bits (لسان - بتس)
## A Lightweight, Root-Based Arabic Language Model using Ternary Computing

### 1. Executive Summary
Lisan Bits Model is a novel Proof of Concept (POC) for an Arabic Large Language Model (LLM) designed to run efficiently on consumer-grade hardware (CPU-based) without requiring massive GPU VRAM. By combining Classical Arabic Morphology (Root-based systems), Graph Database Technology, and Ternary Bit-Math (1, 0, -1), we aim to create a fast, context-aware engine capable of handling both formal Classical Arabic and Egyptian Colloquial Slang.

The model uses a **Genus-Aware Hierarchical Context** system to route queries not just to a broad domain (e.g., Science) but to a specific sub-domain leaf (e.g., `Science/Medicine/Cardiology`), enabling meaningful semantic disambiguation at unprecedented compute efficiency.

### 2. Problem Statement
- **Compute Inefficiency:** Current LLMs rely on 16-bit/32-bit floating-point matrices, requiring massive power and VRAM.
- **Arabic Complexity:** Arabic is a derivational language where meaning is carried by Roots (usually 3 letters).
- **Vocabulary scale:** 16,000+ Roots → Millions of words.
- **Tashkeel (Diacritics):** Drastically changes meaning (e.g., 'alam = world vs. 'alam = flag).
- **Context Ambiguity:** Standard models struggle to distinguish between medical, poetic, and colloquial contexts without massive parameter counts.
- **Corpus Imbalance:** Real-world Arabic corpora are heavily skewed toward religious and colloquial text. Without active enrichment, classifiers degenerate to majority-class prediction.

### 3. Core Hypothesis (The "Science")
We propose that Arabic linguistic logic (Morphology) can be separated from Contextual Intuition (Language Modeling).

- **Logic (Graph DB):** The deterministic rules of roots and word formation (Wazn) will be stored in a directed Graph structure in Neo4j, including a hierarchical taxonomy of contexts (`(:Context)-[:HAS_SUB_CONTEXT]->(:Context)`).
- **Intuition (Bit-Math):** The probabilistic association of words (how they are used in cardiology vs. slang) will be stored in a Ternary (-1, 0, 1) Neural Matrix.
- **Router (ML.NET):** A two-level cascade of binary classifiers predicts the hierarchical context path (e.g., `Science/Medicine/Cardiology`) from Farasa root-based TF-IDF features.

### 4. Architecture & Tech Stack

#### A. The Engine: C# & .NET
- **Language:** C# (for high-performance, low-level memory control).
- **Libraries:** `System.Numerics.Tensors` (SIMD/Vectorization), `Span<T>` (Memory slicing).
- **Why C#:** Unlike Python, C# allows us to perform bitwise operations (XNOR/Popcount) on raw data structures without interpreter overhead, making our Bit-Math extremely fast.
- **Inference Constraint:** TorchSharp (`libtorch`) is **training-only**. It must NOT be present in the inference path. Weights are exported to binary ProtoBuf format.

#### B. The Brain: Ternary Matrix (The Embedding Layer)
Instead of float32, we use sbyte (Signed Byte).
- **Values:**
  - `1`: Strong Positive Correlation (Words appear together).
  - `0`: No Correlation (Pruned/Disconnected).
  - `-1`: Strong Negative Correlation (Words are antonyms or never appear together).
- **Math Operation:** Replaced heavy Matrix Multiplication with XNOR-Popcount logic.

#### C. The Knowledge Base: Graph Database (Neo4j)
Stores the Hierarchical Structure of the language.
- **Nodes:** Roots (e.g., ع م ل), Patterns (Wazn), Words, Contexts (including hierarchical sub-contexts), Grammar Rules.
- **Edges:** `DERIVED_FROM`, `SYNONYM_OF`, `REQUIRES_CASE`, `HAS_SUB_CONTEXT`.
- **Taxonomy:** `(:Context {name:"Science"})-[:HAS_SUB_CONTEXT]->(:Context {name:"Medicine"})-[:HAS_SUB_CONTEXT]->(:Context {name:"Cardiology"})`.
- **Integration:** At inference, path strings from the ML.NET classifier (e.g., `"Science/Medicine/Cardiology"`) are resolved to Neo4j nodes by name lookup. Neo4j owns all IDs and relationships.

#### D. The Pipeline Orchestrator (.NET Aspire & Persistence)
Manages the massive data acquisition and model routing workflow.
- **.NET Aspire:** Acts as the single entry point (`LisanBits.AppHost`) to orchestrate the scraping workers, databases, and APIs.
- **SQLite & EF Core:** Provides resilient, local checkpointing. `ContextVector` stores resolved English taxonomy leaf paths with scalar weights: `{"Science/Medicine/Cardiology": 0.8, "Linguistics/Slang": 0.2}`.
- **Blazor Web Dashboard:** A centralized, real-time UI (`LisanBits.Dashboard`) to monitor active scraping jobs and training progress.

### 5. Data Strategy & Contexts

#### Active Taxonomy (10 Root Domains)
| Domain | Example Sub-Contexts |
|---|---|
| Religion | `Religion/Islam/Quran`, `Religion/Islam/Hadith`, `Religion/Islam/Fiqh`, `Religion/Islam/Aqeedah` |
| Science | `Science/Physics`, `Science/Chemistry`, `Science/Biology`, `Science/Medicine/Cardiology`, `Science/Medicine/Neurology`, `Science/Mathematics` |
| Finance | `Finance/Economics` |
| Literature | `Literature/Poetry`, `Literature/Poetry/Classical` |
| Sports | `Sports/Football` |
| DailyLife | `DailyLife/Food` |
| News | `News` |
| Linguistics | `Linguistics/Classical/LexicalDictionary`, `Linguistics/Slang` |

#### Corpus Balance Policy
- No single category may exceed **40% of total rows**.
- Target minimum **5,000 articles** per major domain.
- All Shamela lexicon sources (`Id ∈ {1, 31, 32, 33, 34, 35}`) are always `Linguistics/Classical/LexicalDictionary`.
- All Sunnah.com sources are always `Religion/Islam/Hadith`.

#### Context A: Classical / Fusha (The "Knowledge" Base)
- **Sources:** Shamela.ws (Books 1687 & 7030).
- **Focus:** Root words, definitions, poetic usage, grammar rules.
- **Preprocessing:** Extract all 3-4-5 letter roots. Map derivations back to the root.
- **Tashkeel Handling:** Normalize Tashkeel for input but store Tashkeel variations as metadata in the Graph to resolve meaning ambiguity.

#### Context B: Egyptian Slang (The "Conversational" Base)
- **Sources:** Nofal dataset (660,636 rows), ARB-EGY-CMP dataset.
- **Focus:** Common phrases, slang grammar, loanwords.
- **Preprocessing:** Strip Arabic vowels. Map slang terms to nearest formal root.

#### Context C: Genus-Aware Sub-Domains (The "Specialist" Layer)
- **Sources:** Arabic Wikipedia category crawls (IDs 36–45), Altibbi, domain-specific sites.
- **Context Resolution:** Wikipedia HTML category footer (`#mw-normal-catlinks`) → Arabic-to-Taxonomy mapping table → English leaf path.
- **News Site Fallback:** Arabic section breadcrumbs → taxonomy mapping → `DataSourceConfig.Category` (family only).
- **Quality Gate:** Accept only blocks with ≥ 7 unique Farasa-extracted roots.

### 6. Workflow & Structure

#### Step 1: The "Router" (Genus-Aware Context Classifier)
A two-level cascade of lightweight binary classifiers:
- **Level-0:** Routes to one of 10 root families (e.g., Science, Religion).
- **Level-1:** Routes to a specific leaf sub-context within the activated family (e.g., `Medicine → Cardiology`).
- **Feature Input:** Root-based TF-IDF (not raw tokens) from Farasa.
- **Class Weights:** $w_i = N / (K \cdot N_k)$ to handle corpus imbalance.
- **Early Stopping:** F1 convergence ($\Delta F1 < 0.001$ over 3 epochs). Safety cap: 30 minutes.

#### Step 2: The "Graph Lookup" (Morphological Analysis)
- Tokenize input words.
- Strip prefixes/suffixes to isolate the root.
- **Query Graph DB:** Word: `العاملين` → Root `ع م ل` → Pattern `Fa'ileen` → Plural Masculine Active Participle.

#### Step 3: The "Bit-Match" (Semantic Search with Depth-Decay Superposition)
- Convert the input sentence into a Bit Vector.
- Apply depth-decayed superposition across activated sub-context matrices:
  `V_hybrid = sign(w_family × V_family + w_genus × V_genus + w_species × V_species)`
- Calculate similarity using integer dot product (−1, 0, 1).
- Retrieve the most probable next word or answer.

### 7. Potential Issues & Mitigation Strategies

| Issue | Severity | Mitigation Strategy |
| :--- | :--- | :--- |
| **Tashkeel Ambiguity** | High | Treat Tashkeel as a separate "re-scoring" layer. Initial search is based on consonants (Roots). Final check uses Tashkeel to refine meaning. |
| **Data Sparsity (Bit-Math)** | Medium | Ternary nets (1-bit) can lose subtle nuance. We mitigate this by having a larger dimension size (more vectors) to compensate for the low precision of individual weights. |
| **Slang-to-Formal Mapping** | High | Slang often breaks grammatical rules. We will treat the "Egyptian Module" as a separate, isolated neural graph that runs parallel to the formal one, only cross-referencing when necessary. |
| **Graph DB Latency** | Low | Use an in-memory Graph cache for the most frequent 1,000 roots to avoid slow disk queries during inference. |
| **Corpus Imbalance** | High | Active corpus enrichment (IDs 36–45). Class-weighted ML.NET training ($w_i = N/(K \cdot N_k)$). Balance gate: no category >40%. |
| **Arabic Wikipedia Category Mismatch** | High | Explicit Arabic-to-Taxonomy mapping table (23+ entries). Longest-match rule. No raw Arabic strings stored as context paths. |

### 8. Definition Target (Success Criteria)
- **Performance:** Capable of processing inputs <100ms on a standard CPU (no GPU).
- **Memory:** The entire model (Vectors + Graph) must fit under 500MB RAM.
- **Accuracy:** Successfully disambiguate words based on context (Medical vs. Poetry) with >85% precision.
- **Hybrid Capability:** Understand a prompt in Egyptian Slang and retrieve an answer from Classical books (Shamela).
- **Hierarchical Routing:** Correctly route a medical fragment to `Science/Medicine/Cardiology` (not just `Science`).

### 9. Implementation Status Update (2026-06)

The following operational architecture is now implemented:

- **Distributed runtime:** Aspire AppHost orchestrates DataPipeline, Dashboard, `farasa-api`, and `conceptnet-importer` via service discovery.
- **Persistent ingestion state:** `pipeline.db` holds `DataSourceConfigs`, queue state, raw corpus, and processed NLP rows. Single consolidated `InitialCreate` migration.
- **Corpus ingested so far:**
  - Nofal slang CSV (660,636 rows).
  - ARB-EGY-CMP local JSON source with automatic prepare pipeline.
- **Safe local-corpus preparation:**
  - Download -> unzip -> fix -> publish is enforced before source processing starts.
  - Preparation is serialized to prevent duplicate runs and file-lock collisions.
  - Corrected JSON output is published atomically to avoid partial reads.
- **Downloader policy:** direct anonymous URL download is attempted first; credentialed retry is used only on auth-required responses.
- **Real-time monitoring path:** Worker broadcasts progress over SignalR to Dashboard (`/scraperhub`) with periodic polling fallback.
- **Farasa integration policy:**
  - DataPipeline uses absolute Farasa endpoint URI from config.
  - Service-discovery-compatible default endpoint is used.
  - NLP fallback writing is configurable and defaults to strict mode (no silent heuristic writes).
- **Knowledge Base (Graph DB) Integration:**
  - Database cleaned: Duplicates pruned (382,290 duplicates deleted) from raw SQLite.
  - Neo4j container configured and orchestrated in .NET Aspire.
  - Direct pipeline seeding: As the Farasa pipeline preprocesses data, it automatically indexes word-root derivations and context vector association weights into Neo4j in real-time.
  - ConceptNet graph seeding: Specialized `LisanBits.ConceptNetImporter` Web API service streams, filters, and bulk-inserts ConceptNet Arabic semantic edges into Neo4j, with start/status/reset triggers exposed directly in the Blazor dashboard UI.
  - Automatic indexing: Unique constraints are configured on `:Word(text)`, `:Root(text)`, and `:Context(name)` to optimize bulk merge queries.
- **Solution Cleanup:**
  - Consolidated all historical migrations into a single `InitialCreate` migration. SQLite database fully reset to clean state with final seed data.
  - Removed `TestingLixiconRootsExtractions` obsolete project reference from `LisanBits.slnx`.
  - Cleaned `DbProbe/Program.cs` — removed repair queries and unused helpers; audit-only mode retained.
- **Trainer:** Fixed `LisanBits.Trainer` compile errors related to TorchSharp namespaces and weight initialization API usage.
- **GrammarPipeline:** Implemented and integrated with Neo4j and the Blazor dashboard, enabling Quranic Treebank ingestion, statistical rule mining, and multiclass grammatical tagger training.

These updates keep the original scientific direction intact while making the ingestion, preprocessing, and knowledge base layers fully operational and production-safe.

---

**Project Status:** Active Development — Epic 1.5 / Epic 2.6 / Epic 3.3 Next.
**Supervisor:** AI Assistant.
**Lead Developer:** User.

> **Supervisor's Note:**
> This markdown file formalizes your idea. Notice how we separated the "Static Knowledge" (The Graph/Dictionary) from the "Dynamic Processing" (The Bit-Math). This is the key to making it lightweight.
>
> Your homework now is to look at the "Next Steps" section. Do you want to start with Step 1 (Scraping/Parsing) or Step 3 (The C# Engine)? I recommend Step 1, as we need data to test the engine.
# Lisan - Bits (لسان - بتس) Initial Project Plan

## Epic 0: Infrastructure & Orchestration (The Backbone)
*Setting up the highly resilient data and monitoring architecture before heavy scraping begins.*

- **Task 0.1: .NET Aspire Orchestration**
  - Configure `LisanBits.AppHost` as the single entry point.
  - Wire up the `DataPipeline` worker, `Farasa` API, and the new `Dashboard`.
- **Task 0.2: SQLite & EF Core Persistence**
  - Implement a local SQLite database in the `DataPipeline` to store raw HTML/Text and track scraper state/checkpoints.
- **Task 0.3: Resilient Scraping (Polly)**
  - Integrate `Microsoft.Extensions.Http.Resilience` to add exponential backoff and retry logic to the web scrapers.
- **Task 0.4: Centralized Blazor Dashboard**
  - Create the `LisanBits.Dashboard` Blazor Web App to visualize live scraping progress via a Minimal API hosted on the `DataPipeline` worker.

---

## Epic 1: Data Acquisition & Preprocessing (NLP)
*The foundation of our model relies on a robust dictionary of roots, words, and derivations.*

- **Task 1.1: Classical & Religious Corpus Construction**
  - Download and parse **Tanzil.net** XML dumps for the Holy Quran (clean, structured, diacritized).
  - Implement a **Universal N-Depth Queue-Based Crawler (BFS)** to autonomously discover and parse all sub-books and chapters on **Sunnah.com** without manual pagination mapping.
  - Locate and extract keyless open-source **Shamela SQL Dumps** (if available) or fallback to universal scraping if necessary.
- **Task 1.2: Formal Arabic Preprocessing Pipeline (Farasa / MADAMIRA)**
  - Utilize **Farasa (QCRI)** or **MADAMIRA** for initial heavy-lifting (Segmentation, POS Tagging, Root Extraction) during the preprocessing phase to avoid reinventing complex morphological rules. *Note: These are discarded during the final lightweight inference phase.*
  - **Feature Extraction:** Extract POS (Noun, Verb, Preposition) and Case State (Nominative, Accusative, Genitive) to map a single Root to all its inflected variations.
- **Task 1.3: Egyptian Colloquial Slang Corpus Construction**
  - Process and ingest the **Nofal (Egyptian Slang Collection)** dataset containing 660,636 rows into the pipeline database.
- **Task 1.4: Slang Preprocessing Pipeline**
  - Strip Arabic vowels from the slang corpus.
  - **Manual Seed:** Start with a "Dictionary Lookup Table" for the top 100 most common slang words mapped to formal roots.
- **Task 1.5: Hierarchical Knowledge Domains Corpus (General Ontology)**
  - Utilize the **Universal N-Depth Crawler** to traverse the infinite hierarchy of **Arabic Wikipedia** Categories (e.g., Science, Medicine) and **Hsoub Academy** domains, dynamically mapping Topic → SubTopic → Article to establish a general ontology.
  - Establish a generalized taxonomy of contexts (e.g., Science → Math → Statistics, or Religion → Quran → Tafseer).
  - **Genus-Aware Corpus Enrichment (Added — Expert-Reviewed):** Add 10 new Wikipedia sub-category sources (IDs 36–45) targeting under-represented domains. No single category may exceed 40% of total rows. Target minimum 5,000 articles per major domain:
    | ID | Target Domain | Arabic Wikipedia Category |
    |---|---|---|
    | 36 | Science/Physics | `تصنيف:فيزياء` |
    | 37 | Science/Chemistry | `تصنيف:كيمياء` |
    | 38 | Science/Biology | `تصنيف:أحياء` |
    | 39 | Science/Mathematics | `تصنيف:رياضيات` |
    | 40 | Science/Medicine/Cardiology | `تصنيف:أمراض_القلب` |
    | 41 | Science/Medicine/Neurology | `تصنيف:الجهاز_العصبي` |
    | 42 | Finance/Economics | `تصنيف:اقتصاد` |
    | 43 | Literature/Poetry | `تصنيف:شعر_عربي` |
    | 44 | DailyLife/Food | `تصنيف:طعام_وشراب` |
    | 45 | Religion/Islam/Fiqh | `تصنيف:فقه_إسلامي` |
  - **Context Quality Filter:** Accept only text blocks with ≥ 7 unique Farasa-extracted roots. Blocks that fail have `ContextVector` left as `{}`.
  - **Wikipedia Category Resolution:** Extract `#mw-normal-catlinks` footer and map Arabic category names to taxonomy paths using an Arabic-to-Taxonomy lookup table. No raw Arabic strings may be stored as context paths.
  - **News Site Fallback Mapping:** For news sources (Youm7, Masrawy), map Arabic section breadcrumbs (`رياضة`, `اقتصاد`, etc.) to taxonomy paths before falling back to `DataSourceConfig.Category`.
  - **Sunnah.com Labeling:** All Sunnah.com rows are labeled `Religion/Islam/Hadith`. Phase 2 will use book structure for deeper sub-paths.

---

## Epic 2: The Knowledge Base (Graph DB)
*Storing the deterministic logic of Arabic morphology.*

- **Task 2.1: Graph Schema Design (Root-Centric & Context-Aware) (Completed)**
  - Design the Neo4j schema focusing on the root: `(Root:Word)-[:HAS_PATTERN]->(Wazn:Pattern)`.
  - Implement generalized Context mappings: `(Word)-[:BELONGS_TO]->(ContextNode)`.
- **Task 2.2: Transfer Learning via ConceptNet & Wikidata (The "Seed") (Completed)**
  - Download the Arabic portion of **ConceptNet** (`/c/ar/`) and **Wikidata** scientific hierarchies.
  - Filter and bulk-insert these edges into Neo4j to establish an instant baseline of 500,000+ relational concepts without training from scratch.
- **Task 2.3: Syntactic Rules & Inflection Schema via Treebank Ingestion (Revised)**
  - **Phase 1: Ingesting Quranic Treebank:** Parse the word-by-word dependency graph (e.g., Quranic Arabic Corpus XML/TSV). Seed Neo4j with syntactic relationships such as:
    ```cypher
    // Example: كَتَبَ الطَّالِبُ الدَّرْسَ (The student wrote the lesson)
    // 'Kataba' (Verb) GOVERNS 'Talibu' (Subject) and 'Darsa' (Object)
    MATCH (verb:Word {text: 'كتب'})
    MATCH (subj:Word {text: 'طالب'})
    MERGE (verb)-[:GOVERNS {role: 'Subject'}]->(subj)
    ```
  - **Phase 2: Statistical Rule Extraction ("Pattern Miner"):** Query the graph to extract statistical grammatical trigger relationships (e.g. probability of Case Ending given neighbor distance patterns, producing rules like `{"Inna": "Next_Is_Accusative"}`). Export these rules as a JSON schema for the runtime Grammar Validator.
  - **Phase 3: Generalizing to Shamela (ML.NET Tagger):** Train a sequence classifier (L-BFGS or SDCA) on the Quranic Treebank using context features (sliding window of POS tags and un-diacritized words) to predict case endings and verb moods on raw un-diacritized texts like Shamela.
- **Task 2.4: Database Instantiation & Population (Completed)**
  - Append our specialized, extracted data (Quran, Hadith, Shamela, Wiki) on top of the ConceptNet baseline graph.
- **Task 2.5: In-Memory Caching Strategy**
  - Cache the **Adjacency List** (`Dictionary<int, List<int>>`) in C#.
  - Use dynamically extensible Bitwise `[Flags]` Enums representing contexts.
- **Task 2.6: Hierarchical Taxonomy Graph — Genus-Aware (New Task)**
  - Seed Neo4j with `(:Context)-[:HAS_SUB_CONTEXT]->(:Context)` edges representing the full taxonomy tree.
  - Example: `Science → Medicine → Cardiology → Mitral_Valve`.
  - Seeding driven by `ContextClassifierManager` on GrammarPipeline startup.
  - Neo4j owns all IDs and parent-child relationships. SQLite `ContextVector` stores only resolved English leaf path strings (e.g., `"Science/Medicine/Cardiology"`). No graph IDs embedded in SQLite.
  - Verification: `MATCH p=(:Context {name: "Science"})-[:HAS_SUB_CONTEXT*]->() RETURN p LIMIT 25`.

---

## Epic 3: The Brain (Ternary Math & ML)
*Replacing massive float32 matrices with lightweight ternary (-1, 0, 1) math.*

- **Task 3.1: Word Embedding Generation (TorchSharp - TRAINING ONLY)**
  - Train a simple float-based Skip-gram model using TorchSharp on consonant-only (vowel-stripped) text to capture clean semantic concepts while avoiding diacritic-induced data sparsity.
  - **Critical Constraint:** Export weights and discard TorchSharp for the final application.
- **Task 3.2: Post-Training Quantization (PTQ)**
  - Convert float embeddings into ternary weights (1, 0, -1) using `sbyte`, mimicking the **BitNet b1.58** methodology.
  - **Bias Correction:** Implement a step to calculate average quantization error and add a float bias term to compensate.
- **Task 3.3: Genus-Aware Hierarchical Context Classifiers & Syntactic Tagger (ML.NET)**
  - **Context Classifiers — Two-Level Cascade (Revised from LightGBM — Expert-Reviewed):**
    - **Level-0 (Family Router):** 10 independent binary `LbfgsLogisticRegressionBinaryTrainer` models, one per root domain. Selects active families.
    - **Level-1 (Sub-Router):** One multiclass classifier per activated family, routing to the specific leaf sub-context (e.g., `Medicine → Cardiology`).
    - **Feature Pipeline:** Raw text → Farasa root extraction → Root token array → TF-IDF featurizer → L-BFGS trainer. *Uses root-based TF-IDF, NOT raw Unicode tokens, since ML.NET's default featurizer does not understand Arabic morphology.*
    - **Class-Weighted Training:** Each sample receives weight $w_i = N / (K \cdot N_k)$ via `ExampleWeightColumnName` to counteract corpus imbalance.
    - **Early Stopping:** Stop when $\Delta F1 < 0.001$ over last 3 consecutive epochs. Absolute safety timeout: 30 minutes (configurable via `GrammarPipeline:TrainingTimeoutMinutes`).
    - **Quality Gates:** Level-0 F1 ≥ 0.80 per category; Level-1 AUC ≥ 0.85.
    - **ContextVector Output:** `{"Science/Medicine/Cardiology": 0.8, "Linguistics/Slang": 0.2}` — leaf paths with scalar weights only. No graph IDs in SQLite.
  - **Syntactic Tagger:** Train a sequence classifier (L-BFGS or SDCA) on the Quranic Treebank to predict the `GrammaticalState` enum labels:
    ```csharp
    public enum GrammaticalState
    {
        // Nouns (I'rab Al-Asma)
        Nominative,  // Marfu'
        Accusative,  // Mansub
        Genitive,    // Majrour
        
        // Verbs (I'rab Al-Af'al)
        Indicative,  // Marfu' (e.g., يَكْتُبُ)
        Subjunctive, // Mansub (e.g., لَنْ يَكْتُبَ)
        Jussive      // Majzoum (e.g., لَمْ يَكْتُبْ)
    }
    ```
    Features: Sliding window of POS tags and un-diacritized words: `[POS_{t-2}, POS_{t-1}, Lemma_{t-1}, POS_{t+1}, POS_{t+2}]`.
  - **Top-K Selection:** The context classifier will return the Top 2 or 3 highest confidence contexts to avoid exploding permutations.

---

## Epic 4: The Core Engine (C# & Systems Programming)
*Building the high-performance CPU inference engine.*

- **Task 4.0: Runtime Input Vectorization, Normalization Symmetry & OOV Handling**
  - Implement a runtime text analyzer/normalizer in `LisanBits.WebApi` to convert raw user string inputs into binary/ternary vectors and feature matrices.
  - **Normalization Symmetry:** Ensure the runtime `InputNormalizer` is an exact clone/shares code with the training `DataNormalizer` (e.g. stripping Tashkeel/diacritics and performing unicode normalization). Any discrepancy will lead to false vocabulary misses.
  - **Linguistic OOV Fallback:** If a token is Out-Of-Vocabulary (OOV), query Neo4j for its grammatical root:
    ```cypher
    MATCH (w:Word {text: $token})-[:DERIVED_FROM]->(r:Root) RETURN r.text
    ```
    If a root is found, retrieve the Root's embedding vector. If no root is found, default to the `<UNK>` embedding.
  - **Context Feature Assembly:** Dynamically build the context sliding window features `[POS_{t-2}, POS_{t-1}, Lemma_{t-1}, POS_{t+1}, POS_{t+2}]` using lookups in the cached vocabulary.
  - **Domain Superposition:** Feed normalized input to the context classifier. If a domain (e.g., Medical) is predicted, merge the domain's context matrix with the input embedding, clamping the result to ternary space (-1, 0, 1) using SIMD.
- **Task 4.1: BitVector Implementation (System.Numerics.Tensors)**
  - Implement a highly optimized `BitVector` struct utilizing `System.Numerics.Tensors` and `TensorPrimitives` (AVX2/AVX-512).
- **Task 4.2: Vector Superposition (Context Merging)**
  - Implement integer addition and `sign()` clamping using SIMD to merge Top-K context vectors instantly.
- **Task 4.3: Binary Serialization (ProtoBuf)**
  - Use **Protocol Buffers (ProtoBuf)** to save and load the quantized ternary matrices from disk instantly, avoiding massive JSON overhead.
- **Task 4.4: Graph DB Integration**
  - Implement the C# Neo4j driver integration using strictly **Asynchronous I/O** (`async/await`).
- **Task 4.5: Two-Stage Auto-Regressive Inference Pipeline**
  - **Stage 1 (Conceptual Selector):** Use Ternary Match modules to find the concept of the next word (consonant-only lemma) via dot product execution.
  - **Stage 2 (Syntactic Morpher):** Implement the `SyntacticEngine` utilizing a `Stack<ClauseFrame>` pushdown automaton (PDA) to handle non-contiguous structures:
    ```csharp
    public class ClauseFrame
    {
        public string GovernorLemma { get; set; } = string.Empty;
        public string GovernorPos { get; set; } = string.Empty;
        public HashSet<GrammaticalState> SatisfiedRoles { get; } = new();
        public GrammaticalState InheritableState { get; set; } = GrammaticalState.Nominative;
    }
    ```
    The engine maintains context frames, resolving prepositions immediately, tracking verb-subject-object roles, and copying inheritable state for adjectives (`Sifah`) and conjunctions (`Ma'tuf`). Combined with the soft-constraint sequence tagger probabilities from ML.NET, select the correct diacritized `InflectedForm` from the Neo4j database.

---

## Epic 5: Evaluation & Refinement
*Proving our success criteria.*

- **Task 5.1: Performance Profiling**
  - Benchmark inference times. Target: <100ms per input on standard CPU.
  - Profile RAM usage. Target: <500MB total footprint.
- **Task 5.2: Disambiguation Accuracy Testing**
  - Build an evaluation dataset with ambiguous words based on context (Medical vs. Poetry) and Tashkeel.
  - Evaluate the model's precision. Target: >85% precision.
- **Task 5.3: Hybrid Capability Testing (Golden Set)**
  - Create a "Golden Set" of 20 questions to test end-to-end routing, Top-K superposition, and auto-regressive generation accuracy.

---

## Current Delivery Snapshot (2026-06)

### Completed Operational Milestones
- Aspire orchestration is active for DataPipeline + Dashboard + farasa-api + conceptnet-importer.
- SQLite pipeline persistence and queue-driven crawling are running.
- Dashboard live monitoring is wired with SignalR push and polling fallback.
- Nofal slang local source flow is integrated.
- ARB-EGY-CMP local source is integrated with `download -> unzip -> fix -> load` readiness gating.
- Local corpus prep concurrency protections are in place to avoid duplicate prep and file-lock conflicts.
- Farasa preprocessing endpoint routing was hardened to absolute/service-discovery endpoint resolution.
- Strict NLP quality mode is available to prevent writing fallback morphology when Farasa is unavailable.
- Neo4j container configured and orchestrated in .NET Aspire.
- Direct pipeline seeding: As the Farasa pipeline preprocesses data, it automatically indexes word-root derivations and context vector association weights into Neo4j in real-time.
- ConceptNet graph seeding: Specialized `LisanBits.ConceptNetImporter` Web API service streams, filters, and bulk-inserts ConceptNet Arabic semantic edges into Neo4j, with start/status/reset triggers exposed directly in the Blazor dashboard UI.
- Unique constraints set up on `:Word(text)`, `:Root(text)`, and `:Context(name)` to optimize bulk merge queries.
- Consolidated all historical migrations into a single `InitialCreate` migration. SQLite database fully reset to a clean starting state with final seed data.
- Fixed `LisanBits.Trainer` compile errors related to TorchSharp namespaces and weight initialization API usage, making the Skip-gram trainer and quantization model build cleanly.
- Implemented and integrated `LisanBits.GrammarPipeline` with Neo4j and the Blazor dashboard, enabling Quranic Treebank ingestion, statistical rule mining, and multiclass grammatical tagger training.
- Removed `TestingLixiconRootsExtractions` obsolete project reference from `LisanBits.slnx`.
- Cleaned `DbProbe/Program.cs` — removed repair queries and unused helpers; audit-only mode retained.

### Immediate Operational Notes
- Keep `FarasaApi:AllowFallback=false` for quality-first runs.
- Keep local-corpus sources active; readiness gate prevents early queue processing before files are prepared.
- Use `DbProbe` audit mode for source/queue/coverage verification after major ingest runs.
- `ContextVector` must contain resolved English taxonomy leaf paths only. No raw Arabic category strings.

---

----------------------------------------------------------------------------

---

# Lisan - Bits: Model Improvement & Quality Assurance (QA) Project
*A parallel dedicated track focusing strictly on scientific metrics, data validation, and parameter profiling.*

## Epic 1: Data Quality & Input Verification
*Gatekeeping the data to ensure "Garbage in, Gold out" does not happen.*

- **Task 1.0: Data Lineage & Traceability (Schema Update)**
  - Update `RawUniversalData` to include a `SourceUrl` or `ResourceIdentifier` column. Without this, we cannot trace where a row came from, making automated QA impossible for existing data.
  - Future scraped data must save its exact origin URL.
- **Task 1.1: The Golden Set (Input Validation Rules)**
  - Manually curate a "Golden Set" of 50-100 random dictionary entries **per data source** (e.g., 50 for Shamela, 50 for Waseet, 50 for Wikipedia). 
  - **The Mapping Mechanism:** The Golden Set will be a separate table or file mapped by `SourceUrl`. 
  - This acts as the "Ground Truth" to validate *each specific scraper's extraction logic*. Unlike raw scraped data which might accidentally pull HTML noise, sidebars, or truncate definitions, the Golden Set ensures we know exactly what a perfect extraction looks like for that specific website's layout.
- **Task 1.2: Automated Data Validation Gate (Composite Distance Metric)**
  - Implement an automated script to compare scraped text against the Golden Set ground truth by joining on `SourceUrl`.
  - Calculate **Word-Level Jaccard Similarity** (bag of words overlap).
  - Calculate **Levenshtein Distance** (to capture string edit distance and structural similarity).
  - Compute a composite average score of both metrics to automatically discard fuzzy or noisy matches before they enter the training pipeline.

## Epic 2: Model Parameter Tuning & Genus-Awareness
*Ensuring the model learns fine-grained context and does not overfit to dominant domains.*

- **Task 2.1: Context-Aware Self-Attention (Replaces Domain Tags & Stratification)**
  - Eliminate explicit domain tagging prefixes or strict data stratification (which bottlenecks training sizes to the smallest category). 
  - Instead, leverage the existing `ContextVector` within `ProcessedUniversalData` and enforce domain bias natively through the model's self-attention mechanism, allowing it to focus on the user's intended topic without throwing away massive amounts of training data.
- **Task 2.2: L2 Regularization (Weight Decay)**
  - Apply L2 Regularization to the Bit-Math engine to penalize large weights. This stops the model from memorizing loud signals and encourages it to distribute learning across more nuanced, specific features.

## Epic 3: Profiling Data Quantity (The Learning Curve)
*Applying the "Elbow Method" to know exactly when to stop scraping and training.*

- **Task 3.1: Data Segmentation & Learning Curves**
  - Chunk the dataset into incrementally larger subsets (10%, 20%, 30%, etc.).
  - Train on the subset and measure the Training Loss vs. Validation Loss (on a held-out 20% validation set).
- **Task 3.2: Early Stopping & Capacity Measurement**
  - Plot the Learning Curve (Data Size vs. Validation Loss).
  - Stop adding data or training when **Validation Loss stops decreasing** (the "Knee" or "Elbow"). If Validation Loss is high but Training Loss is low, the model is overfitting, requiring *more data* or stronger regularization. If both plateau, capacity is reached.

## Epic 4: Model Quality & Accuracy Evaluation
*Measuring how well the model actually performs after training.*

- **Task 4.1: The Golden Set (Output)**
  - Create a dataset of Question-Answer pairs where the answer is a specific Arabic concept or word definition.
- **Task 4.2: Vector & Semantic Similarity Metrics**
  - Measure **Cosine Similarity** between the *Model's Output Prediction Vector* and the *Golden Truth Vector*.
- **Task 4.3: Generative Metrics & Confidence**
  - Measure **Perplexity** to evaluate the model's confidence and detect hallucinations.
  - Apply **ROUGE** or **BLEU** scores if evaluating generated text definitions to measure semantic alignment with human-written definitions.
- **Task 4.4: Internal Mechanics & Transformer Evaluation (The Supervisor's Metrics)**
  - **Parameter Footprint:** Calculate theoretical memory size ($N \times 1.58$ bits for Ternary weights).
  - **Attention Entropy:** Measure the dispersion of Self-Attention weights to ensure the model isn't suffering from "Attention Collapse" (attending uniformly to everything or collapsing to a single token).
  - **LayerNorm Variance Tracking:** Monitor activation variance before and after Layer Normalization to prevent gradient vanishing/exploding.
  - **Residual Flow:** Measure Cosine Similarity between layer inputs and outputs to ensure Transformer blocks are actually learning transformations, not just passing identity.
  - **Softmax Calibration (ECE):** Calculate Expected Calibration Error to ensure a 90% Softmax probability actually reflects a 90% real-world accuracy. Tune **Temperature ($T$)** based on Perplexity vs. Generation Diversity tradeoffs.

## Epic 5: QA Results & Dashboard Integration
*Visualizing the health and performance of the data and model pipelines.*

- **Task 5.1: Live QA Metrics Dashboard**
  - Create a dedicated new page in the `LisanBits.Dashboard` project.
  - Visualize Jaccard/Levenshtein validation pass rates, Learning Curves (Overfitting/Underfitting alerts), and Output Accuracy metrics.
  - Provide real-time analysis to the administrator on data quality and model health.

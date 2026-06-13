# PROJECT DEFINITION: Lisan-Bit (لسان - بتس)
## Arabic Linguistic Intelligence via Root-Centric GraphRAG and Quantization-Optimized SLM

Aligned to the locked execution baseline in LISAN_BIT_SINGLE_SOURCE_OF_TRUTH.md (v2 — SSE + Data-Driven Dialect).

### 1. Executive Summary
Lisan-Bit is an Arabic Linguistic Intelligence system designed for morphology-aware retrieval, grammatical analysis, diacritization, lexical reasoning, and dialect-aware support. It is built to run in compact deployment modes on standard consumer hardware while preserving a stronger Arabic-specific linguistic stack than a generic assistant.

The project combines four core layers:
- a root-centric Neo4j knowledge graph for deterministic linguistic structure
- a native Arabic NLP layer in .NET for normalization, morphology, syntax, and dialect handling
- a retrieval-first runtime that grounds answers in Quran, Hadith, Tafsir, dictionaries, and curated corpora
- a small language model trained in a standard FP16 path and then progressively quantized for efficient deployment

The current program is no longer defined as a ternary-first proof of concept. The primary delivery path is RAG-first, standard-transformer-first, and quantization-gated. Ternary-from-scratch research remains conditional and is pursued only if post-training compression fails the required quality thresholds.

### 2. Problem Statement
The project addresses five coupled problems:
- Arabic meaning is root-driven, derivational, and structurally different from languages that dominate mainstream LLM design.
- Diacritics, morphology, and grammar create ambiguity that generic models often resolve weakly without explicit linguistic structure.
- Religious, lexical, and classical Arabic material requires grounded source retrieval rather than unsupported freeform generation.
- Large general-purpose models are expensive to train, expensive to run, and poorly optimized for consumer-grade Arabic-first deployment.
- Real-world Arabic corpora are imbalanced and can degrade context classifiers without targeted enrichment and strict quality controls.

### 3. Core Thesis
Lisan-Bit separates Arabic linguistic intelligence into complementary layers:

- **Deterministic structure:** roots, patterns, syntax signals, lexical relations, and context taxonomy are represented explicitly in a graph and supporting NLP components.
- **Grounded retrieval:** relevant Quran, Hadith, Tafsir, dictionary, and corpus passages are retrieved before answer generation.
- **Neural fluency and ambiguity handling:** a compact SLM handles synthesis, explanation, and uncertainty where deterministic logic alone is insufficient.

This design treats Arabic morphology and knowledge grounding as first-class system components rather than hoping a generic model will infer them implicitly.

### 4. Product Definition
Lisan-Bit is primarily an Arabic Linguistic Intelligence system, not a general-purpose Arabic chatbot.

Primary capabilities:
- root extraction, pattern identification, lemma recovery, and POS-aware analysis
- grammatical parsing and case-aware reasoning
- diacritization with deterministic plus neural refinement
- lexical and etymological explanation
- Quranic, Hadith, Tafsir, and dictionary-grounded retrieval
- Dialect-aware handling with deep Egyptian Arabic support: etymological root mapping, morphological reanalysis, syntactic reordering, and dialect-matched response generation

Primary non-goals:
- competing directly with frontier LLMs on broad general chat
- code generation or programming assistance
- unrestricted freeform religious authority without grounding and citation

### 5. Religious and Trust Guardrails
For Quran, Hadith, Tafsir, and fiqh-adjacent usage, the system must operate as a grounded retrieval assistant.

Required baseline behavior:
- answers should be grounded in retrieved sources
- Tafsir, Hadith, Ayah, and fiqh-sensitive answers should cite the source basis used
- answer quality must be understood as limited by the ingested corpus
- disputed interpretations should be presented as sourced views, not unsourced certainty
- the system must avoid unsupported ruling-style output

### 6. Architecture and Tech Stack

#### A. Runtime and APIs
- **Language:** C# and .NET
- **API Layer:** ASP.NET Core
- **Orchestration:** .NET Aspire via `LisanBits.AppHost`
- **Dashboard:** Blazor-based operational and QA monitoring

#### B. Native Arabic NLP Layer
- **Tokenizer:** Arabic normalization, sentence splitting, morphology-aware segmentation, 32K BPE tokenization (includes dialect vocabulary; OOV < 0.05% on Egyptian)
- **Morphology:** root, pattern, lemma, POS, disambiguation, and dialect etymological lookup
- **Syntax:** grammar-aware parsing and structural validation
- **Dialect (data-driven):** detection CNN, trained etymological root map (SQLite, built from MADAR + ARB-EGY-CMP alignment), learned syntactic rewrite engine, and dialect-matched response generation. No manual dictionary curation — all mappings derived from parallel corpora, scraping, and AI generation.

#### C. Knowledge and Persistence Layer
- **Neo4j:** root-centric graph, lexical relations, contextual taxonomy, source-linked knowledge retrieval, and DialectWord nodes with DIALECT_MAPS_TO / SHARES_ROOT relationships
- **SQLite + EF Core:** ingestion state, raw and processed corpus persistence, checkpoints, QA traceability, and dialect etymology table (surface_form → etym_root → msa_equivalent)
- **Context storage:** English taxonomy leaf paths only, with scalar weights

#### D. Retrieval Layer
- **GraphRAG:** root, pattern, meaning, synonym, domain, and source traversal via Neo4j; dual-root queries for dialect (dialect etymological root + MSA root)
- **Vector retrieval:** semantic search over indexed corpus passages; dual-query for dialect (original dialect text + reconstructed MSA)
- **Context assembly:** tiered retrieval budgets at 4K, 8K, 16K, and 32K; dialect reconstruction annotations and `[DIALECT]` / `[MSA-RECON]` context markers included

#### E. Model Layer
- **Primary model path:** standard FP16 transformer with morphological feature injection
- **Quantization path:** FP16 -> INT8 -> INT4 -> 2-bit packed ternary evaluation
- **Conditional research path:** ternary-from-scratch only if compressed primary path fails quality gates
- **Training framework:** PyTorch + Accelerate + DeepSpeed for model training and distillation
- **Inference framework:** .NET runtime with ONNX Runtime as primary path; Ollama/GGUF as secondary runtime path
- **TorchSharp role:** architecture validation and small-model training in .NET (dialect detection CNN, diacritization model, grammatical tagger); not the primary 458M training path
- **.NET-first boundary:** use .NET/C# for all feasible components; Python is limited to: (a) 458M model training, (b) one-time embedding model fine-tuning, and (c) one-time word alignment for dialect etymology. All runtime components are .NET.
- **SSE streaming:** `/v1/chat/completions` defaults to Server-Sent Events with OpenAI-compatible chunk format; blocking mode available as fallback

### 7. Model Strategy
The project no longer defines ternary math as the default inference identity of the system.

Current baseline:
- train a standard transformer first
- inject root, pattern, and POS features into the model input representation
- evaluate quality at FP16
- progressively quantize for deployment efficiency
- keep ternary research as an optional follow-on path

This makes the plan safer, more measurable, and more aligned with available compute.

### 8. Data Strategy and Coverage

#### A. Core Religious and Linguistic Sources
- Quran from verified Tanzil text
- Hadith collections from curated Sunnah sources
- Tafsir and religious reference material
- classical and modern dictionaries including Lisan Al-Arab and Al-Waseet

#### B. General and Specialist Sources
- Arabic Wikipedia
- OSCAR Arabic
- CC-100 Arabic
- news, literature, and domain-specific corpora

#### C. Dialect and Slang Sources (Data-Driven Pipeline)
- MADAR-28: parallel sentences across 28 Arabic city dialects and MSA (primary alignment training source)
- ARB-EGY-CMP: Egyptian-MSA parallel corpus (already integrated)
- Nofal dataset: Egyptian Arabic slang and colloquial expressions (already integrated)
- OpenSubtitles Arabic: dialect-labeled movie/TV subtitles
- Egyptian social media and web forums: scraped via C# pipeline with quality gates
- AI-generated parallel pairs: teacher model (Jais/cloud API) fills vocabulary gaps

**Dialect data principle:** Every dialect mapping is derived from data or AI generation — never manually entered. The etymological root map, morphological reanalysis patterns, and syntactic reordering rules are products of statistical alignment on parallel corpora. New dialect data is incorporated by re-running the pipeline, not by code changes.

#### D. Coverage Policy
- no single context family should dominate the corpus beyond the defined balance thresholds
- under-represented domains must be actively enriched
- context labels must be resolved to English taxonomy leaf paths
- quality gates apply before text is accepted for training or retrieval indexing

### 9. Processing Workflow

#### Step 1: Query Understanding
- normalize input text
- extract morphology and roots
- detect dialect; if dialect detected, run reconstruction pipeline (etymological root lookup → syntactic reordering → MSA reconstruction)
- classify domain, intent, and dialect

#### Step 2: Grounded Retrieval
- for dialect queries: run graph and vector retrieval with both dialect etymological roots and reconstructed MSA (dual-root / dual-query)
- for MSA queries: standard root, meanings, patterns, related words, and context
- merge and rank evidence according to context tier; include dialect reconstruction annotations in context

#### Step 3: Response Construction
- if the model is unavailable, answer through template-based grounded responses (includes dialect etymology template)
- if the model is available, generate using retrieved context plus morphology-aware features; if `dialect_match=true`, respond in the user's dialect
- stream response token-by-token via SSE; post-process through syntactic constraints and diacritization
- dialect reconstruction is best-effort: unmapped words fall back gracefully to zero-vector / original text

### 10. Customer Support and Agent Extension
The system can support a customer-support-style agent on top of the core RAG stack, but this is an application layer, not the baseline identity of the model.

#### A. Baseline behavior (must ship)
- grounded retrieval over freeform religious authority
- source citation for Tafsir, Hadith, Ayah, and fiqh-sensitive answers
- explicit limitation that answer quality depends on ingested source coverage and quality
- no ruling-style answers without citation and scope control

#### B. Backlog epics (application-layer expansion)
1. **Religious Answer Safety Layer** — citation-first answering, disputed-interpretation handling, non-fatwa guardrails (MSA + dialect query detection).
2. **Customer Support Agent Layer** — intent routing, citation traceability, safety/escalation rules, answer templates, conversation memory, and dialect-matched responses.
3. **Source Quality and Coverage Expansion** — Tafsir breadth, Hadith source-quality ranking, provenance scoring, and coverage-gap reporting.
4. **Dialect Pipeline Expansion** — extend etymological map and reconstruction engine to Levantine and Gulf Arabic using MADAR data; continuous dialect scraping pipeline for vocabulary evolution; dialect code-switching detection.

This extension path is especially suitable for Quran, Hadith, Tafsir, Arabic meaning lookup, and guided support over curated knowledge sources.

### 11. Quality and Evaluation Policy
The project includes explicit quality controls rather than relying only on model loss.

Required evaluation families:
- data lineage and source traceability
- golden-set extraction validation by source (including 200+ Egyptian↔MSA pairs for dialect alignment)
- Jaccard and Levenshtein quality gates for scraped content
- morphology, grammar, diacritization, and retrieval benchmarks
- dialect-specific benchmarks: etymological root accuracy (> 70%), MSA reconstruction acceptability (> 60%), dialect RAG relevance (> 50%), dialect-matched response quality (> 70%)
- quantization quality comparisons across deployment formats
- learning-curve analysis for data scaling decisions
- SSE streaming first-token latency (< 2 sec GPU, < 5 sec CPU)

### 12. Success Criteria
- grounded Arabic linguistic answers with explicit source-aware behavior
- morphology and grammar performance aligned with the benchmark targets in the master plan
- retrieval-first religious and lexical support that remains bounded by ingested sources
- compact deployment modes that fit standard consumer hardware
- successful routing to fine-grained contexts such as `Science/Medicine/Cardiology` instead of only broad families
- quantization promotion only when quality gates are met (INT8 >= 99% FP16, INT4 >= 97% FP16, 2-bit >= 95% FP16)
- dialect benchmarks remain within 2% tolerance at each quantization level
- SSE streaming functional with < 2 sec first-token latency on T1000 GPU

### 13. Current Implementation Status
The following delivery foundation is already operational:
- Aspire orchestration for DataPipeline, Dashboard, Farasa API, ConceptNet importer, and Neo4j
- SQLite-based ingestion state and checkpointing
- local corpus preparation and readiness gating
- real-time dashboard monitoring
- strict Farasa fallback policy support
- direct Neo4j seeding from preprocessing
- ConceptNet Arabic semantic edge import flow
- consolidated database migration baseline
- GrammarPipeline integration for treebank ingestion, rule mining, and grammatical tagger work

The project is therefore beyond a pure conceptual PoC. It is now an active implementation program with operational ingestion, persistence, orchestration, graph integration, and training infrastructure.

### 14. Program Direction
Immediate direction is governed by the consolidated master plan in `LISAN_BIT_SINGLE_SOURCE_OF_TRUTH.md` (v2, 28-week timeline).

Priority path:
- finish the retrieval-first product path with SSE streaming
- build the dialect data pipeline (corpus → word alignment → etymological root map → syntactic rewrite engine)
- complete the native Arabic NLP layer including Lisan.Dialect subsystem
- expand grounded religious and lexical support with guardrails
- train the primary FP16 model (includes Egyptian dialect data at ~15-20% of corpus)
- quantize and package for efficient deployment

Execution governance follows the master-plan split between:
- baseline product behavior and technical gates (9 epics, 28 weeks)
- backlog expansion layers for religious safety, customer-support workflows, and multi-dialect expansion

### 15. Final Definition
Lisan-Bit is a specialized Arabic linguistic intelligence platform with GraphRAG, native Arabic NLP, a data-driven Egyptian dialect system, SSE-streamed chat, and a compact quantization-optimized SLM. Its strongest value is not generic conversation, but grounded Arabic understanding, retrieval, explanation, and dialect-aware support over linguistically and religiously significant sources. Dialect knowledge is trained, scraped, and AI-generated — never manually curated.

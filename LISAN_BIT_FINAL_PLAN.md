# Lisan-Bit (لسان - بت) — Definitive Final Plan

**Authority:** @Supervisor — Math, ML & NLP Research Supervisor  
**Date:** 2026-06-13  
**Classification:** SLM (Small Language Model) — Arabic-Native RAG-First Architecture  
**Verdict:** STRATEGIC PIVOT ACCEPTED WITH SUPERVISOR MODIFICATIONS. The external review correctly identifies that the previous plan's phase ordering was wrong (training before RAG), its product definition was ambiguous (linguistic intelligence vs general assistant), and its approach to quantization was risky (ternary-from-scratch instead of progressive). However, the review is WRONG to abandon morphological feature injection, WRONG to target 1B float16 params (won't train on 6GB GPU), and WRONG to discard FinMax V2 / MASP / MCLAS entirely. This plan merges the review's strategic corrections (RAG-first, progressive quantization, product scoping, NLP layer) with the current plan's Arabic-specific innovations (morph features, GraphRAG, deterministic morpher, AKQ). The result is a safer, faster, more honest plan that delivers a working product sooner and reserves custom research for a later phase where it can be validated against a proven baseline.

---

## Executive Summary

### The Strategic Pivot — What the External Review Gets Right

| Recommendation | Verdict | Reasoning |
|---|---|---|
| RAG before training | **ACCEPT** | A working RAG pipeline delivers value immediately; training can take months. Build retrieval first, prove the knowledge graph works, then enhance with neural generation. |
| Product definition: Option A vs B | **ACCEPT** | The project must choose. We choose Option A (Arabic Linguistic Intelligence) as PRIMARY — this is where our architecture has unfair advantages. Option B is future work. |
| External teacher (Qwen/Jais) | **ACCEPT** (already adopted) | We already moved to external teacher via ONNX in the previous revision. Qwen is a valid alternative to Jais. |
| Progressive quantization (FP16→INT8→INT4→1.58-bit) | **ACCEPT as primary path** | Measure quality at each step. Stop when requirements are met. This is safer than ternary-from-scratch. |
| NLP layer (Lisan.Morphology, Tokenizer, Syntax, Dialect) | **ACCEPT** | Excellent modular design. .NET-native NLP libraries that work independently of the neural model. |
| Runtime request pipeline | **ACCEPT** | Normalize → Root Extract → Context Classify → Neo4j → Embedding → Context → Model → Response is the correct architecture. |
| Don't train custom teacher | **ACCEPT** (already done) | External teacher via ONNX. Zero training weeks. |
| Target 500M params | **ACCEPT** | 500M float16 = 1 GB weights, trainable on 6GB GPU with optimizer offloading. 1B is too large (see pushback below). |
| Use TorchSharp (not ML.NET) for transformers | **ACCEPT** (already adopted) | ML.NET for classifiers/ranking, TorchSharp for transformer training. |

### What the External Review Gets Wrong — Supervisor Pushbacks

| Recommendation | Verdict | Mathematical/Empirical Refutation |
|---|---|---|
| "Abandon ternary, always use progressive quantization" | **PUSH BACK** | BitNet b1.58 research (Ma et al., 2024) proves ternary-from-scratch MATCHES FP16 quality at same param count. Progressive quantization AFTER FP16 training always loses quality at the INT4→1.58-bit step. The CORRECT approach is: FP16→progressive quantize as primary path, then ternary-from-scratch as research comparison. Keep BOTH paths; measure both; adopt whichever wins. |
| "Target 1B params" | **PUSH BACK** | 1B float16 params = 2 GB weights + 4 GB Adam states + 2 GB gradients + 2 GB activations = ~10 GB minimum. Does NOT fit in 6GB GPU even with aggressive offloading. 500M is the maximum trainable on the available hardware. The review ignores VRAM constraints. |
| "Abandon FinMax V2 / MASP / MCLAS" | **PUSH BACK** | The review says these are "fascinating research topics" but "not the shortest path." This is true for a general-purpose Arabic chatbot (Option B). But for Option A (Arabic Linguistic Intelligence), FinMax V2's sparse attention is precisely what enables morphology-aware attention, MASP's head pruning is what enables efficient morphological head activation, and MCLAS is what enables parameter-efficient sharing of morphological patterns across layers. These are NOT optional for Option A — they ARE the product differentiation. However, they should be validated against a standard-attention baseline FIRST (Phase 6), not assumed from the start. |
| "Qwen 4B as teacher" | **MODIFY** | Qwen is multilingual, not Arabic-specialized. Jais-1.3B (Arabic-native, trained on 275B Arabic+English tokens) is a better teacher for Arabic. Use Jais as primary, Qwen as fallback. Or better: use BOTH and ensemble their teaching signals. |
| "Don't start with ternary" | **MODIFY** | The review assumes binary choice: either ternary-from-scratch OR FP16→quantize. The correct approach is BOTH: (1) train FP16 baseline → progressive quantize, (2) train ternary-from-scratch. Compare results. If progressive quantization at INT4 meets requirements, STOP. If not, the ternary model provides a higher-quality ultra-compressed option. |

### The Merged Strategy — Build Order

```
Phase 0: Product Definition         ← NEW (from review)
Phase 1: Corpus Acquisition         ← SAME (already strong)
Phase 2: Arabic Knowledge Graph     ← STRONGER (from review: word→root→pattern→meaning→domain→synonym→antonym)
Phase 3: Native Arabic NLP Layer    ← NEW (from review: Lisan.Morphology, Tokenizer, Syntax, Dialect)
Phase 4: RAG System                 ← NEW (from review: build BEFORE training)
Phase 5: Student Model (FP16)       ← MODIFIED (from review: standard transformer first; from us: WITH morph features)
Phase 6: Progressive Quantization   ← NEW (from review: measure at each step)
Phase 7: Ternary Research (optional) ← MODIFIED (from us: only if INT4 doesn't meet requirements)
Phase 8: Runtime Architecture       ← MERGED (from review: request pipeline; from us: deterministic morpher + GraphRAG)
```

### Honest Performance Targets (Revised for Option A)

| Target | Confidence | Mechanism |
|---|---|---|
| Best-in-class Arabic morphological analysis | **90%** | Deterministic morpher + GraphRAG + morph features + RAG. This is rule-based + lookup — the SLM just orchestrates. |
| Best-in-class Arabic diacritization | **85%** | PDA + GraphRAG + morph features. Same principle. |
| Match 7B on Arabic grammatical tasks | **75%** | RAG-enhanced generation + morphological attention specialization |
| Approach 3B on general Arabic chat | **55%** | 500M params + external teacher KD; no substitute for parameter count on general tasks |
| Run on standard PC (8 GB RAM, CPU-only) | **95%** | INT4 model = ~250 MB weights; full system < 1 GB |
| Deliver working RAG product in ≤8 weeks | **90%** | RAG + knowledge graph + NLP layer are software, not research |

---

## Table of Contents

1. [Supervisor Adjudication: External Review Assessment](#1-supervisor-adjudication-external-review-assessment)
2. [Product Definition: Option A — Arabic Linguistic Intelligence](#2-product-definition-option-a--arabic-linguistic-intelligence)
3. [Architecture Overview (Final — RAG-First)](#3-architecture-overview-final--rag-first)
4. [SLM Model Specification (Dual Path)](#4-slm-model-specification-dual-path)
5. [Context Window (Final: 4K / 8K / 16K / 32K)](#5-context-window-final-4k--8k--16k--32k)
6. [Memory Budget (Recalculated)](#6-memory-budget-recalculated)
7. [Novel Research Concepts (Retained + New)](#7-novel-research-concepts-retained--new)
8. [Epic 0: Infrastructure & Orchestration](#8-epic-0-infrastructure--orchestration)
9. [Epic 1: Corpus Acquisition](#9-epic-1-corpus-acquisition)
10. [Epic 2: Arabic Knowledge Graph (Enhanced)](#10-epic-2-arabic-knowledge-graph-enhanced)
11. [Epic 3: Native Arabic NLP Layer (NEW)](#11-epic-3-native-arabic-nlp-layer-new)
12. [Epic 4: RAG System (NEW)](#12-epic-4-rag-system-new)
13. [Epic 5: Student Model Training (FP16 Primary Path)](#13-epic-5-student-model-training-fp16-primary-path)
14. [Epic 6: Progressive Quantization (NEW)](#14-epic-6-progressive-quantization-new)
15. [Epic 7: Ternary Research Path (Conditional)](#15-epic-7-ternary-research-path-conditional)
16. [Epic 8: Runtime Architecture & C# Engine](#16-epic-8-runtime-architecture--c-engine)
17. [Epic 9: Evaluation & Refinement](#17-epic-9-evaluation--refinement)
18. [Complete Risk Register (All 40 Risks)](#18-complete-risk-register-all-40-risks)
19. [Novel Research Contributions](#19-novel-research-contributions)
20. [Implementation Phases (Final Timeline)](#20-implementation-phases-final-timeline)
21. [Confidence Assessment (Honest)](#21-confidence-assessment-honest)
22. [Appendix A: Request Pipeline Architecture](#appendix-a-request-pipeline-architecture)
23. [Appendix B: Progressive Quantization Protocol](#appendix-b-progressive-quantization-protocol)
24. [Appendix C: Training Loop Skeleton (C#)](#appendix-c-training-loop-skeleton-c)
25. [Appendix D: Hyperparameter Summary (Final)](#appendix-d-hyperparameter-summary-final)

---

## 1. Supervisor Adjudication: External Review Assessment

### Point-by-Point Evaluation

#### "Your strongest parts are: Arabic linguistic focus, root-centric knowledge graph, Quran+Hadith+dictionaries, Neo4j, .NET ecosystem, CPU-friendly deployment, Egyptian dialect specialization"

**Supervisor: AGREE.** These are the project's genuine competitive advantages. Every architectural decision should AMPLIFY these strengths, not dilute them with general-purpose LLM ambitions.

---

#### "Your weakest parts are: Building a custom BitNet competitor from scratch, training a 100M teacher from scratch, custom transformer research (FinMax, TAMPT, MCLAS), attempting to compete with billions of dollars of LLM research using two laptops"

**Supervisor: PARTIALLY AGREE.**

- "Building a custom BitNet competitor from scratch" — **Correct.** BitNet b1.58 is the competitor, not something we reinvent. We USE the BitNet ternary quantization approach, we don't compete with it.
- "Training a 100M teacher from scratch" — **Correct.** Already fixed in previous revision (external teacher via ONNX).
- "Custom transformer research (FinMax, TAMPT, MCLAS)" — **Partially correct.** These are high-risk research. However, they are NOT random research — they are specifically designed for Arabic morphological attention. The correct approach is: build a standard baseline FIRST, then validate these innovations against the baseline. Don't abandon them; defer them.
- "Attempting to compete with billions of dollars of LLM research using two laptops" — **Correct.** This is the fundamental constraint. We cannot and should not try to beat GPT-4 at general reasoning. We should beat it at Arabic morphology, where our architecture gives us an unfair advantage.

---

#### "Phase 0 — Define Success: Option A (Arabic Linguistic Intelligence) vs Option B (General Arabic Assistant)"

**Supervisor: ACCEPT. This is the most important recommendation.**

The previous plans mixed both products into one architecture, leading to conflicting design decisions:
- Option A needs: morphological features, GraphRAG, deterministic morpher, sparse attention for grammar
- Option B needs: large param count, broad training data, general reasoning capability

**Decision: Option A is the PRIMARY product.** Here is why:

1. **Option A is where we have unfair advantages.** Our root-centric knowledge graph, native dictionary, morphological NLP pipeline, and deterministic morpher give us capabilities that 70B models simply don't have. A 70B model can guess roots; our system KNOWS roots.

2. **Option A is achievable with our resources.** Morphological analysis, diacritization, grammar checking — these are rule-based + lookup tasks enhanced by a neural model. A 500M SLM is sufficient for orchestration.

3. **Option A has clear market positioning.** "Sibawayh AI" — Arabic linguistic intelligence that no other model provides. Not "yet another Arabic chatbot."

4. **Option B can be a FUTURE product** built on top of Option A's infrastructure. Once the RAG pipeline, knowledge graph, and NLP layer are proven, a larger model can be trained for general chat.

5. **The evaluation criteria change.** For Option A, success = accuracy on morphological analysis, diacritization, grammar, and root extraction. For Option B, success = perplexity on general Arabic text, BLEU on translation, human preference on chat. These require different optimization targets.

**Product Definition (Option A):**

> **Lisan** is an Arabic Linguistic Intelligence system that provides best-in-class morphological analysis, grammatical parsing, diacritization, and etymological reasoning. It combines a ternary-optimized SLM with a root-centric Neo4j knowledge graph, a deterministic syntactic morpher, and a native .NET NLP pipeline. It runs on standard consumer hardware (8 GB RAM, CPU-only) and provides API-compatible access for integration into Arabic language tools, educational software, and content platforms.

---

#### "Phase 4 — RAG Before Training"

**Supervisor: ACCEPT. This is the second most important recommendation.**

The previous plans start model training in Week 10. But the RAG pipeline (knowledge graph + retrieval + context assembly) can deliver a working product BEFORE any neural model is trained. The RAG pipeline uses:

- Neo4j graph traversal for knowledge lookup
- Vector similarity search for semantic retrieval
- Template-based response generation for deterministic answers
- The deterministic morpher (PDA) for grammar and diacritization

This means: **by Week 6-8, you have a working Arabic linguistic tool that can answer morphological questions, look up roots and patterns, generate diacritized text, and explain grammatical structures** — all without a neural model. The neural model then ENHANCES this system by adding fluency, handling ambiguous cases, and generating free-form explanations.

**Impact on timeline:** The first demonstrable product ships in Week 8 (RAG-based), not Week 18 (model-based). This is a massive improvement in project visibility and risk reduction.

---

#### "Phase 5 — Use a teacher, don't train one (Qwen 4B/8B)"

**Supervisor: ACCEPT with modification.**

We already adopted external teacher via ONNX in the previous revision. The modification:

- **Primary teacher: Jais-1.3B** — Arabic-native, trained on 275B Arabic+English tokens, Apache 2.0 license. For Arabic-specific distillation, Jais provides better teaching signal than a multilingual model.
- **Secondary teacher: Qwen2-1.5B** — Multilingual with strong Arabic, excellent for general generation quality. Used as auxiliary teacher for general Arabic fluency.
- **Ensemble approach:** Average the KD loss from both teachers. This provides broader coverage: Jais for Arabic specificity, Qwen for general quality.

If only one teacher can be used (due to ONNX speed constraints): **Use Jais.** Arabic specificity matters more than general capability for Option A.

---

#### "Phase 7 — Student Model: 500M or 1B params, not 100M"

**Supervisor: ACCEPT 500M, PUSH BACK on 1B.**

**Why 1B doesn't work on 6GB GPU:**

```
1B float16 model training memory:
  FP16 weights on GPU:           2.0 GB
  FP16 activations (batch=4, 4K): ~1.5 GB
  FP16 gradients on GPU:         2.0 GB
  Workspace + CUDA overhead:     0.5 GB
  -----------------------------------------------
  Total GPU:                     ~6.0 GB — ZERO headroom, OOM risk

  FP32 master weights on CPU:    4.0 GB
  Adam states on CPU:            8.0 GB
  -----------------------------------------------
  Total CPU:                     ~12.0 GB — fits in 64 GB
```

With 6GB GPU and 1B params, there is zero margin for error. Any activation spike, any CUDA allocation overhead, any gradient accumulation buffer = Out Of Memory. This is not theoretical — it will happen in practice.

**500M float16 is the right target:**

```
500M float16 model training memory:
  FP16 weights on GPU:           1.0 GB
  FP16 activations (batch=4, 4K): ~1.0 GB
  FP16 gradients on GPU:         1.0 GB
  Workspace + CUDA overhead:     0.5 GB
  -----------------------------------------------
  Total GPU:                     ~3.5 GB — 2.5 GB headroom

  FP32 master weights on CPU:    2.0 GB
  Adam states on CPU:            4.0 GB
  -----------------------------------------------
  Total CPU:                     ~6.0 GB — fits easily in 64 GB
```

500M float16 params gives 2.5 GB GPU headroom. This is comfortable.

**Post-quantization inference footprint:**

| Format | Weight Size | Inference Total (8K ctx) | Fits In |
|---|---|---|---|
| FP16 | 1.0 GB | ~1.4 GB | 4 GB RAM |
| INT8 | 500 MB | ~900 MB | 4 GB RAM |
| INT4 | 250 MB | ~650 MB | 4 GB RAM |
| 1.58-bit | ~99 MB | ~500 MB | 4 GB RAM |

All quantization levels fit comfortably on consumer hardware. The progressive quantization path (FP16→INT8→INT4→1.58-bit) provides clear decision points.

---

#### "Phase 9 — Quantization: Do not start with ternary"

**Supervisor: MODIFY — Dual-path approach.**

The review says "do not start with ternary." This is correct as the PRIMARY path. But it's wrong to abandon ternary entirely. The correct strategy is:

**Path A (Primary — Safe):** Train FP16 → quantize to INT8 → evaluate → quantize to INT4 → evaluate → quantize to 1.58-bit → evaluate. Stop at the first level that meets requirements.

**Path B (Research — Conditional):** If Path A's 1.58-bit quantization loses >5% quality vs FP16, train a ternary-from-scratch model with FinMax V2 + MASP + MCLAS. Compare with Path A's 1.58-bit result. Adopt whichever is better.

**Why both paths are needed:**

1. **Progressive quantization AFTER training is known to lose quality.** The Q → K → V → FFN quantization cascade introduces compounding errors. Research consistently shows INT4 retains 97-99% of FP16 quality, but 1.58-bit post-training quantization retains only 85-92% — a significant gap.

2. **Ternary-from-scratch avoids the quantization cliff.** BitNet b1.58 (Ma et al., 2024) trains with ternary weights from the start, and the model learns to work within the constraint. The result matches FP16 quality at the same param count — something post-training quantization cannot achieve.

3. **But ternary-from-scratch is higher risk.** It requires custom training code (FinMax V2, SG-STE, MASP) that hasn't been validated on Arabic. If it fails, you've wasted training time.

4. **The dual-path strategy is OPTIMAL:** Path A gives you a guaranteed working model at FP16/INT8/INT4 levels. Path B gives you a potentially better ultra-compressed model. If Path B fails, you still have Path A. If Path B succeeds, you have a superior product.

**Decision gate:** At Phase 6, evaluate Path A at INT4. If INT4 meets requirements → ship it, skip Phase 7 (ternary research). If INT4 doesn't meet requirements → proceed to 1.58-bit quantization. If 1.58-bit post-training quantization loses >5% quality → proceed to Phase 7 (ternary-from-scratch).

---

#### "Build Lisan.Morphology, Lisan.Tokenizer, Lisan.Syntax, Lisan.Dialect as .NET libraries"

**Supervisor: STRONGLY ACCEPT.**

This is the best recommendation in the review. The native Arabic NLP layer is:
1. **Independent of the neural model** — works without training
2. **Deterministic** — no randomness, 100% reproducible
3. **Fast** — pure C# code, no GPU needed
4. **Composable** — the neural model can call these as tools
5. **Testable** — unit tests for every function
6. **Valuable standalone** — other .NET developers can use these libraries

This is exactly the kind of software engineering that delivers value immediately and compounds over time. The Farasa segmenter becomes a bootstrap tool for training data, but the runtime uses Lisan.Morphology exclusively.

---

#### "Request Pipeline: Normalize → Root Extract → Context Classify → Neo4j Retrieval → Embedding Search → Context Assembly → Model → Response"

**Supervisor: ACCEPT with addition.**

The review's request pipeline is correct but incomplete for Option A. It's missing:
1. **Morphological analysis** (not just root extraction — also pattern, POS, lemma)
2. **Diacritization lookup** (GraphRAG for tashkeel)
3. **Syntactic morpher** (deterministic grammar enforcement)
4. **MOC reassembly** (for OOV output tokens)

The complete request pipeline is specified in Appendix A.

---

## 2. Product Definition: Option A — Arabic Linguistic Intelligence

### Product Name: Lisan (لسان)

### Product Statement

> Lisan is the definitive Arabic Linguistic Intelligence system. It provides morphological analysis, grammatical parsing, diacritization, etymological reasoning, and dialect translation with accuracy that exceeds general-purpose LLMs on these tasks. It achieves this through a unique combination of a root-centric Neo4j knowledge graph, a deterministic syntactic morpher, a native .NET NLP pipeline, and a ternary-optimized SLM. It runs on consumer hardware and provides API-compatible access.

### Core Capabilities (Option A)

| Capability | Description | Quality Target | Mechanism |
|---|---|---|---|
| **Root Extraction** | Extract triliteral/quadriliteral root from any Arabic word | >95% accuracy | Native dictionary + Farasa fallback + neural disambiguation |
| **Pattern Identification** | Identify morphological pattern (وزن) for any word | >90% accuracy | Native dictionary + pattern matching + neural |
| **Morphological Analysis** | Full decomposition: root + pattern + affixes + POS | >90% F1 | Lisan.Morphology + GraphRAG + neural |
| **Diacritization** | Add full tashkeel to undiacritized text | >85% word-level accuracy | Deterministic morpher + GraphRAG + neural |
| **Grammar Analysis** | Parse Arabic sentence structure (إعراب) | >80% accuracy on standard Arabic | Lisan.Syntax + neural |
| **Etymology** | Explain word origin and historical development | >75% accuracy | Neo4j graph traversal + neural explanation |
| **Dialect Translation** | Egyptian ↔ MSA bidirectional translation | >80% BLEU | Lisan.Dialect + neural |
| **Quranic Lookup** | Find and explain Quranic verses by topic/root | >90% retrieval accuracy | Neo4j + RAG |
| **Grammar Checking** | Identify and correct grammatical errors | >80% error detection | Lisan.Syntax + neural |

### Non-Goals (Option B — Future Work)

| Capability | Status |
|---|---|
| General Arabic chatbot | **Future** — requires larger model or different architecture |
| Code generation | **Out of scope** — not a linguistic task |
| Multi-turn reasoning | **Partial** — RAG handles fact-based reasoning; creative reasoning requires Option B |
| Programming assistance | **Out of scope** |
| Creative writing | **Partial** — within linguistic domain (poetry analysis, not poetry generation) |
| General knowledge Q&A | **Partial** — only within Arabic linguistic and religious domains |

---

## 3. Architecture Overview (Final — RAG-First)

```
USER REQUEST (Arabic text)
    |
    v
+-----------------------------------------------------------+
|              REQUEST PIPELINE (Lisan.Runtime)              |
|                                                            |
|  1. Normalize (Lisan.Tokenizer.Normalize)                 |
|     - Unicode normalization, alef/yaa normalization        |
|     - Remove tatweel, normalize hamza                     |
|                                                            |
|  2. Morphological Analysis (Lisan.Morphology)             |
|     - ExtractRoot() → root consonants                     |
|     - ExtractPattern() → morphological pattern            |
|     - ExtractLemma() → dictionary lemma                   |
|     - POS tag → noun/verb/particle/etc.                   |
|                                                            |
|  3. Context Classification (ML.NET + Platt scaling)       |
|     - Domain: religious/linguistic/general/dialectal      |
|     - Intent: lookup/analysis/generation/translation      |
|     - Dialect: MSA/Egyptian/Levantine/Gulf               |
|                                                            |
|  4. Neo4j Retrieval (GraphRAG)                            |
|     - Root graph traversal: word → root → related words   |
|     - Semantic relations: synonym/antonym/derivation       |
|     - Domain filtering: education/literature/religion      |
|     - Quran/Hadith lookup by root/concept                 |
|                                                            |
|  5. Vector Search (Embedding similarity)                  |
|     - Semantic similarity in embedding space               |
|     - Corpus retrieval from SQLite + FAISS                |
|                                                            |
|  6. Context Assembly                                      |
|     - Tier 1 (4K): Direct concatenation                   |
|     - Tier 2 (8K): Concatenation + dedup                  |
|     - Tier 3 (16K): YARN + selection                      |
|     - Tier 4 (32K): Clause-level TF-IDF + summarization   |
|                                                            |
|  7. Model Inference                                       |
|     - Student model generates response                    |
|     - WITH morphological features (root+pattern+POS)      |
|     - WITH RAG context in attention                       |
|                                                            |
|  8. Post-Processing                                       |
|     - Syntactic Morpher (PDA): enforce grammar rules      |
|     - Diacritization: GraphRAG lookup + neural            |
|     - MOC Reassembly: decomposed → surface form           |
|     - Dialect adjustment: Lisan.Dialect                   |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|                    DIACRITIZED ARABIC RESPONSE             |
+-----------------------------------------------------------+

UNDERLYING SYSTEMS:
  Neo4j Sidecar (Machine B, ~400 MB)
  SQLite Corpus Store (Machine B, ~50-200 GB)
  FAISS Vector Index (Machine B or A, ~1-5 GB)
  Native Dictionary (in-memory, ~200 MB)
  ML.NET Context Classifiers (Platt-scaled, ECE < 0.05)
  Lisan.Morphology / .Tokenizer / .Syntax / .Dialect (.NET libraries)
  Deterministic Syntactic Morpher (PDA)
  Student SLM (FP16 / INT4 / 1.58-bit depending on quantization path)

TRAINING INFRASTRUCTURE (.NET-native):
  TorchSharp + LibTorch CUDA — student training
  ONNX Runtime — teacher inference (Jais-1.3B + Qwen2-1.5B)
  Dual-machine: i9H trains + serves, i7 prepares data + hosts Neo4j

API LAYER:
  /v1/embeddings        — Morphology-aware embeddings
  /v1/chat/completions  — OpenAI-compatible chat
  /v1/morphology/analyze — Root+pattern+POS extraction
  /v1/morphology/diacritize — Full diacritization
  /v1/knowledge/search — Neo4j graph search
```

---

## 4. SLM Model Specification (Dual Path)

### Path A: Standard FP16 Transformer (Primary)

| Dimension | Value | Rationale |
|---|---|---|
| d_model | **1536** | Proven SLM size (Qwen2-0.5B uses 1536); fits 500M target |
| N_layers | **16** | Adequate depth for SLM; standard architecture |
| Q heads | **12** (4 morph + 8 std) | d_k = 128; morph heads are our innovation |
| KV groups | **4** (2 morph + 2 std) | GQA with 3 Q heads per KV group |
| d_ff (SwiGLU) | **4096** | 8/3 × d_model ≈ 4096 (clean number) |
| Vocab | **32K** + AVR | Arabic-optimized BPE; AVR extends to 200K+ |
| Attention | **Standard softmax** | Proven, safe, well-understood |
| FFN | **SwiGLU** (standard) | Proven; gate bias = 0 (standard init) |
| Position | **RoPE** on Q/K | Standard; base θ = 10000 |
| Context (training) | **2048 → 4096 → 8192** | Progressive Context Curriculum |
| Context (inference max) | **32768** | YARN extrapolation |

**Parameter count (Path A):**

```
Embedding:         32K × 1536 = 49.2M
Morph projection:  1984 × 1536 = 3.0M  (Token 1536 + Root 256 + Pattern 128 + POS 64 = 1984)
Per layer:
  Attention:
    Q: 1536 × 1536 = 2.36M
    K: 1536 × 512  = 0.79M  (4 KV groups × 128 dim)
    V: 1536 × 512  = 0.79M
    O: 1536 × 1536 = 2.36M
  FFN (SwiGLU):
    Gate: 1536 × 4096 = 6.29M
    Up:   1536 × 4096 = 6.29M
    Down: 4096 × 1536 = 6.29M
  RMSNorm: negligible
  Per layer total: ~25.2M
16 layers: ~403M
Hierarchical softmax clusters: 32K × 256 = 8.2M
---------------------------------------------------
Total: ~463M ≈ 500M (with rounding and minor components)
```

**FP16 training size:** ~926 MB  
**INT4 inference size:** ~116 MB  
**1.58-bit inference size:** ~91 MB

### Path B: Ternary Transformer (Research — Conditional)

Only pursued if Path A's 1.58-bit quantization loses >5% quality vs FP16.

| Dimension | Value | Difference from Path A |
|---|---|---|
| d_model | **2048** | Larger width (ternary params are cheaper) |
| N_layers | **18** | Deeper (ternary enables more layers) |
| Q heads | **16** (4 morph + 12 std) | More heads for sparse attention |
| KV groups | **4** (2 morph + 2 std) | Same |
| d_ff (SwiGLU V2) | **5632** | Larger FFN |
| Attention | **FinMax V2** | Ternary-output sparse attention |
| FFN | **SwiGLU V2** (gate bias 2.0) | Ternary FFN with float16 gate |
| Total params | **~863M** (ternary) | Larger param count but 1.58-bit storage |
| Ternary storage | **~170 MB** | Same as previous plan |

Path B retains: FinMax V2, MASP, MCLAS, AKQ, SG-STE, three-phase blend.

**Decision logic:**

```
Path A at INT4 quality ≥ 95% of FP16?
  YES → Ship INT4 model. Skip Path B.
  NO  → Try Path A at 1.58-bit quantization.
  
Path A at 1.58-bit quality ≥ 95% of FP16?
  YES → Ship 1.58-bit model. Skip Path B.
  NO  → Train Path B (ternary-from-scratch).
  
Path B quality ≥ Path A 1.58-bit quality?
  YES → Ship ternary model.
  NO  → Ship Path A at INT4 (accept 4-5% quality loss for simplicity).
```

### Morphological Feature Injection (BOTH Paths)

This is the key innovation that the external review misses. Morphological features are NOT custom research — they are a well-proven technique (similar to character-level features in LSTMs, or position features in BERT). Injecting root, pattern, and POS information at the embedding layer gives the model free linguistic knowledge that it would otherwise have to learn from data.

**For Path A:**

```
Token embedding:   d=1536  (learned)
Root embedding:    d=256   (learned from root vocabulary)
Pattern embedding: d=128   (learned from pattern vocabulary)
POS embedding:     d=64    (learned from POS tag vocabulary)
Concatenated:      d=1984
Projection:        1984 → 1536 (Ternary Linear, Xavier Normal init)
Output:            d=1536  (same as standard embedding)
```

**Cost:** 3M additional params (0.6% of model).  
**Benefit:** The model never has to learn Arabic morphology from data — it gets root, pattern, and POS for free at every position. This saves ~50-100M params of "morphology learning capacity" that would otherwise be needed.

**Empirical justification:** Similar approaches have been validated in:
- Elmo (Peters et al., 2018) — character-level features improve word representations
- BERT with linguistic features (Kondratyuk & Straka, 2019) — POS/morphology features improve parsing
- Arabic-specific models (Antoun et al., 2020) — morphological features improve Arabic NLU

This is NOT speculative research. It is proven technology applied to our architecture.

---

## 5. Context Window (Final: 4K / 8K / 16K / 32K)

Unchanged from previous revision. Four tiers with YARN extrapolation for Tiers 3-4.

| Tier | Context | Use Case | KV-Cache (Path A, 16 layers) | KV-Cache (Path B, 18 layers, AKQ) |
|---|---|---|---|---|
| **Tier 1** | **4096** | Simple Q&A, morphological lookup | ~24 MB | ~28 MB |
| **Tier 2** | **8192** | Standard chat, grammar analysis | ~48 MB | ~56 MB |
| **Tier 3** | **16384** | Long documents, extended analysis | ~96 MB | ~112 MB |
| **Tier 4** | **32768** | Full document with clause summarization | ~192 MB | ~224 MB |

Progressive Context Curriculum (PCC) for training: 2048 → 4096 → 8192. YARN for inference extrapolation.

---

## 6. Memory Budget (Recalculated)

### Inference Budget — Path A (Standard FP16, then quantized)

```
                    FP16        INT8        INT4        1.58-bit
Weights:           ~926 MB     ~463 MB     ~116 MB     ~91 MB
KV-Cache (8K):     ~48 MB      ~48 MB      ~48 MB      ~48 MB
Activations:       ~40 MB      ~40 MB      ~40 MB      ~40 MB
Neo4j sidecar:     ~400 MB     ~400 MB     ~400 MB     ~400 MB
Dictionary:        ~200 MB     ~200 MB     ~200 MB     ~200 MB
----------------------------------------------------------------
Total (8K, Lite):  ~1,214 MB   ~751 MB     ~404 MB     ~379 MB
Total (8K, Std):   ~1,614 MB   ~1,151 MB   ~804 MB     ~779 MB
Total (32K, Full): ~1,758 MB   ~1,295 MB   ~948 MB     ~923 MB
```

**All quantization levels fit in 4 GB RAM** for Lite mode and **8 GB RAM** for Standard mode.

### Inference Budget — Path B (Ternary, for comparison)

```
Weights:           ~170 MB
KV-Cache (8K):     ~56 MB (AKQ)
Activations:       ~40 MB
Neo4j sidecar:     ~400 MB
Dictionary:        ~200 MB
----------------------------------------------------------------
Total (8K, Lite):  ~466 MB
Total (8K, Std):   ~866 MB
Total (32K, Full): ~1,090 MB
```

Path B is ~50-100 MB smaller than Path A at INT4/1.58-bit. The difference is small — Path A at INT4 is already extremely efficient.

### Training Budget (i9H + 6GB GPU)

**Path A (FP16 standard transformer):**

```
GPU (6 GB):
  FP16 model weights:         1.0 GB
  FP16 activations (batch=4, 4K): ~1.0 GB
  FP16 gradients:             1.0 GB
  Workspace + CUDA:           0.5 GB
  -----------------------------------------------
  Total GPU:                  ~3.5 GB — 2.5 GB headroom ✓

CPU (64 GB):
  FP32 master weights:        2.0 GB
  Adam states:                4.0 GB
  Teacher model (ONNX):       ~3.0 GB
  Teacher logits cache:       ~0.5 GB
  -----------------------------------------------
  Total CPU:                  ~9.5 GB — fits easily ✓
```

**Path B (Ternary transformer, for comparison):**

```
GPU (6 GB):
  FP16 model weights (for training): ~1.73 GB
  FP16 activations:                  ~1.5 GB
  FP16 gradients:                    ~1.73 GB
  Workspace + CUDA:                  ~0.5 GB
  -----------------------------------------------
  Total GPU:                         ~5.46 GB — 0.54 GB headroom (tight) ⚠

CPU (64 GB):
  Optimizer states (offloaded):      ~5.2 GB
  Teacher model (ONNX):              ~3.0 GB
  -----------------------------------------------
  Total CPU:                         ~8.2 GB — fits ✓
```

**Path A has 2.5 GB GPU headroom. Path B has 0.5 GB headroom.** This is another reason Path A is the primary path — it's more stable on consumer hardware.

---

## 7. Novel Research Concepts (Retained + New)

### Retained from Previous Plan (Conditional on Path B)

| Concept | Path A Status | Path B Status | Notes |
|---|---|---|---|
| FinMax V2 | **Not used** (softmax) | **Active** | Only needed for ternary attention |
| MASP | **Not used** (all heads active) | **Active** | Only needed for morphological head pruning |
| MCLAS | **Not used** (standard attention) | **Active** | Only needed for morph attention sharing |
| AKQ | **Not used** (standard KV) | **Active** | Only needed with morphological KV groups |
| TAMPT | **Adapted** (same concept, different backends) | **Active** | ONNX teacher on CPU, student on GPU — same for both paths |
| AVR + MOC | **Active** (both paths) | **Active** | Essential for 32K vocab + OOV handling |
| PCC | **Active** (both paths) | **Active** | Progressive context curriculum — proven, not research |
| External Teacher KD | **Active** (both paths) | **Active** | Jais-1.3B + Qwen2-1.5B via ONNX |

### New Concepts (from Merged Strategy)

| Concept | Description | Status |
|---|---|---|
| **RAG-First Architecture** | Build retrieval pipeline before neural model training; RAG + templates deliver working product in Week 8 | **Active** |
| **Progressive Quantization Protocol** | FP16 → INT8 → INT4 → 1.58-bit with quality gates at each step | **Active** |
| **Dual-Path Model Strategy** | Path A (standard FP16 → quantize) and Path B (ternary-from-scratch) with decision gates | **Active** |
| **Lisan NLP Layer** | .NET-native morphological, tokenization, syntax, and dialect libraries | **Active** |
| **Ensemble Teacher KD** | Dual-teacher distillation from Jais (Arabic-specific) + Qwen (general quality) | **Active** |
| **Morphological Feature Injection** | Root + pattern + POS features at embedding layer (both paths) | **Active** |

---

## 8. Epic 0: Infrastructure & Orchestration

- **Task 0.1:** .NET Aspire Orchestration *(Completed)*
- **Task 0.2:** SQLite & EF Core Persistence *(Completed)*
- **Task 0.3:** Resilient Scraping (Polly) *(Completed)*
- **Task 0.4:** Centralized Blazor Dashboard *(Completed)*
- **Task 0.5:** TorchSharp CUDA Validation *(CRITICAL GATE)*
  - Test TorchSharp + CUDA on i9H
  - Verify GPU tensor allocation (4GB test)
  - Verify autograd on GPU
  - **If CUDA fails:** CPU-only training (add 4-6 weeks)
- **Task 0.6:** Dual-Machine Setup
  - i9H: training + inference
  - i7: data prep + Neo4j + evaluation
- **Task 0.7:** ONNX Teacher Setup
  - Download Jais-1.3B + Qwen2-1.5B
  - Convert to ONNX (one-time, on any machine with Python)
  - Validate inference in .NET
  - Build vocabulary alignment tables
  - Measure inference speed (target: <2 sec/batch)

---

## 9. Epic 1: Corpus Acquisition

Unchanged from previous plan. Religious, linguistic, general sources. SQLite storage.

- **Tasks 1.1-1.7:** Corpus collection, morphological pipeline, augmentation
- **Task 1.8:** Synthetic data (Tier 1: free templates 200M tokens, Tier 2: API 200M tokens)
- **Task 1.9:** 32K BPE vocabulary construction
- **Task 1.10:** Interleaved curriculum design

---

## 10. Epic 2: Arabic Knowledge Graph (Enhanced)

**Enhanced from previous plan based on external review's emphasis on making the graph the product differentiator.**

### Graph Schema (Enhanced)

```
Node Types:
  Word       — surface form, frequency, domain
  Root       — consonants, meaning family, derivation count
  Pattern    — morphological template, verb/noun forms
  Meaning    — gloss (Arabic + English), sense ID, usage context
  POS        — part-of-speech tag, subcategory
  Domain     — education, religion, literature, daily life, science
  Source     — Lisan Al-Arab, Al-Waseet, Quran, Hadith
  Synonym    — synonym group ID
  Antonym    — antonym group ID

Relationship Types:
  (Word)-[:HAS_ROOT]->(Root)
  (Word)-[:HAS_PATTERN]->(Pattern)
  (Word)-[:HAS_POS]->(POS)
  (Word)-[:HAS_MEANING]->(Meaning)
  (Word)-[:BELONGS_TO]->(Domain)
  (Word)-[:DERIVES_FROM]->(Root)
  (Root)-[:PRODUCES]->(Word)
  (Word)-[:SYNONYM_OF]->(Word)
  (Word)-[:ANTONYM_OF]->(Word)
  (Word)-[:APPEARS_IN]->(Source)
  (Meaning)-[:EXPLAINED_IN]->(Source)
  (Root)-[:RELATED_ROOT]->(Root)  — e.g., كتب ↔ كاتب
```

### Tasks

- **Task 2.1-2.9:** Unchanged (Neo4j setup, dictionary seeding, GraphRAG interface)
- **Task 2.10 (NEW):** Enhanced Relationship Seeding
  - Add synonym/antonym relationships from Al-Waseet and Mukhtar Al-Sihah
  - Add cross-root relationships (roots sharing 2 consonants)
  - Add domain classification for all words
  - Add Quran verse references for religious vocabulary
- **Task 2.11 (NEW):** GraphRAG API for Request Pipeline
  - Expose graph traversal as .NET API
  - Support: root lookup, pattern lookup, synonym chain, domain filter
  - Support: concept search (find verses about "forgiveness" → استغفار → غفر)
  - Latency target: <50ms for single-hop, <200ms for multi-hop

---

## 11. Epic 3: Native Arabic NLP Layer (NEW)

### Lisan.Morphology

```csharp
namespace Lisan.Morphology;

public class ArabicMorphology
{
    // Core analysis
    public RootResult ExtractRoot(string word);        // استغفار → غفر
    public PatternResult ExtractPattern(string word);   // استغفار → استفعال
    public string ExtractLemma(string word);            // استغفار → استغفر
    public string GetPOS(string word);                  // استغفار → noun (اسم)
    
    // Full decomposition
    public MorphAnalysis Analyze(string word);
    // Returns: { root: "غفر", pattern: "استفعال", POS: "noun", 
    //            prefix: "است", suffix: "ار", lemma: "استغفر" }
    
    // Batch processing
    public List<MorphAnalysis> AnalyzeBatch(IEnumerable<string> words);
    
    // Disambiguation (uses context)
    public MorphAnalysis Disambiguate(string word, string context);
}
```

### Lisan.Tokenizer

```csharp
namespace Lisan.Tokenizer;

public class ArabicTokenizer
{
    // Normalization
    public string Normalize(string text);          // Full Unicode + Arabic normalization
    public string NormalizeAlef(string text);      // أإآا → ا
    public string NormalizeYaa(string text);       // ىئ → ي
    
    // Segmentation
    public List<string> Segment(string text);      // Morphological segmentation
    public List<string> SentenceSplit(string text); // Arabic-aware sentence splitting
    
    // Tokenization (for model input)
    public List<int> Tokenize(string text);        // BPE tokenization (32K vocab)
    public string Detokenize(List<int> tokens);    // Inverse
}
```

### Lisan.Syntax

```csharp
namespace Lisan.Syntax;

public class ArabicSyntax
{
    // Parsing
    public ParseTree ParseSentence(string sentence);       // إعراب
    public CaseEnding PredictCaseEnding(string word, string context); // رفع/نصب/جر/جزم
    
    // Grammar checking
    public List<GrammarError> CheckGrammar(string text);
    public string SuggestCorrection(GrammarError error);
    
    // Agreement checking
    public bool CheckGenderAgreement(string noun, string adjective);
    public bool CheckNumberAgreement(string noun, string verb);
}
```

### Lisan.Dialect

```csharp
namespace Lisan.Dialect;

public class ArabicDialect
{
    // Translation
    public string EgyptianToMSA(string egyptianText);
    public string MSAToEgyptian(string msaText);
    
    // Detection
    public DialectResult DetectDialect(string text);
    // Returns: { primary: "Egyptian", confidence: 0.85, 
    //            secondary: "MSA", confidence: 0.12 }
    
    // Phonological rules
    public string ApplyEgyptianRules(string msaText);  // ج → g, ق → glottal stop, etc.
    public string ApplyLevantineRules(string msaText);
    public string ApplyGulfRules(string msaText);
}
```

### Implementation Notes

- **Farasa** is used as a BOOTSTRAP tool during development and for generating training data
- At runtime, Lisan.Morphology uses the **native dictionary** (in-memory hash map) as PRIMARY
- Farasa is a FALLBACK only for words not in the dictionary
- All libraries are pure C# — no external dependencies at runtime
- Target: <5ms per word for morphological analysis on CPU

### Tasks

- **Task 3.1:** Lisan.Morphology implementation (ExtractRoot, ExtractPattern, ExtractLemma, GetPOS, Analyze, Disambiguate)
- **Task 3.2:** Lisan.Tokenizer implementation (Normalize, Segment, Tokenize with 32K BPE)
- **Task 3.3:** Lisan.Syntax implementation (ParseSentence, PredictCaseEnding, CheckGrammar)
- **Task 3.4:** Lisan.Dialect implementation (EgyptianToMSA, MSAToEgyptian, DetectDialect)
- **Task 3.5:** Unit test suite (target: >90% code coverage, >95% accuracy on held-out words)
- **Task 3.6:** Integration with native dictionary and Farasa fallback

---

## 12. Epic 4: RAG System (NEW)

### Architecture

```
USER QUERY
    |
    v
+-----------------------------------------------------------+
|  Lisan.Tokenizer.Normalize(query)                         |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Lisan.Morphology.Analyze(query)                          |
|  → Extract roots, patterns, POS for key terms             |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  ML.NET Context Classifier                                |
|  → Domain: religious/linguistic/general/dialectal         |
|  → Intent: lookup/analysis/generation/translation         |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Neo4j GraphRAG Retrieval                                 |
|  → Root graph traversal (find related words/concepts)     |
|  → Domain-filtered lookup                                 |
|  → Quran/Hadith reference retrieval                       |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Vector Similarity Search (FAISS)                         |
|  → Find semantically similar passages in corpus           |
|  → Rank by relevance + recency                           |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Context Assembly                                         |
|  → Combine graph results + vector results + query         |
|  → Deduplicate, rank, trim to context budget              |
|  → Template-based response if no model available          |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Response Generation                                      |
|  → If model available: neural generation with RAG context |
|  → If no model: template-based response from graph data   |
|  → Post-process: Syntactic Morpher + Diacritization       |
+---------------------------+-------------------------------+
```

### Template-Based Response (No Model Required)

Before the neural model is trained, the RAG system uses templates to generate responses:

```
Query: ما أصل كلمة استغفار؟
Morphology: root=غفر, pattern=استفعال, POS=noun
Graph: غفر → meanings: [forgiveness, pardon], related: [غفور, مغفرة, غافر]
Template: "كلمة {word} أصلها الجذر {root} على وزن {pattern}. ومعناها: {meaning}. 
           ومن الكلمات ذات الجذر نفسه: {related_words}."
Output: "كلمة استغفار أصلها الجذر غفر على وزن استفعال. ومعناها: طلب المغفرة. 
         ومن الكلمات ذات الجذر نفسه: غفور، مغفرة، غافر."
```

This produces correct, useful responses for a large class of linguistic queries — without any neural model.

### Tasks

- **Task 4.1:** Embedding generation for corpus (use pre-trained Arabic embeddings or train Sentence-BERT on Arabic)
- **Task 4.2:** FAISS vector index construction and indexing
- **Task 4.3:** GraphRAG retrieval pipeline (Neo4j traversal → context extraction)
- **Task 4.4:** Vector search pipeline (query embedding → FAISS search → ranking)
- **Task 4.5:** Context assembly module (combine graph + vector + query, dedup, rank, trim)
- **Task 4.6:** Template-based response generator (for linguistic queries: root, pattern, meaning, grammar)
- **Task 4.7:** RAG evaluation suite (100 test queries with expected answers)
- **Task 4.8:** RAG latency optimization (target: <500ms end-to-end without model, <2s with model)

---

## 13. Epic 5: Student Model Training (FP16 Primary Path)

### Model Architecture (Path A — Standard FP16 Transformer)

All specifications from Section 4, Path A.

- d_model = 1536, N_layers = 16, 12 Q heads (4 morph + 8 std), 4 KV groups
- Standard softmax attention, standard SwiGLU FFN
- Morphological features: Token(1536) + Root(256) + Pattern(128) + POS(64) = 1984 → 1536
- ~500M params total

### Training Pipeline

**Teacher:** Jais-1.3B (primary) + Qwen2-1.5B (secondary) via ONNX Runtime

**Distillation loss:**

```
L = alpha_kd * KD_Jais + beta_kd * KD_Qwen + L_task + L_reg

Where:
  KD_Jais = KL(student_logits_aligned || teacher_jais_logits / T)  with T=2.0
  KD_Qwen = KL(student_logits_aligned || teacher_qwen_logits / T)  with T=2.0
  L_task = CrossEntropy(student_logits, targets)
  L_reg = L_alpha_reg + L_MASP + L_MOC (only if Path B is also being trained)
```

**KD weight schedule:**

| Phase | Steps | alpha_kd (Jais) | beta_kd (Qwen) | Context |
|---|---|---|---|---|
| 1 (warmup) | 1–10K | 0.5 | 0.2 | 2048 |
| 2 (FinMax-safe) | 10K–40K | 0.4 | 0.15 | 4096 |
| 3 (convergence) | 40K–60K | 0.2 | 0.1 | 8192 |
| 4 (fine-tune) | 60K–70K | 0.0 | 0.0 | 8192 |

**LR schedule:**

```
Steps 1-2000:     Warmup 0 → 3e-4
Steps 2001-40000: Stable 3e-4
Steps 40001-60000: Cosine decay 3e-4 → 1e-5
Steps 60001-70000: Fine-tuning at 1e-5
```

### Batch Strategy

| Phase | Micro-Batch | Seq Length | Grad Accum | Effective Batch | VRAM |
|---|---|---|---|---|---|
| Phase 1 | 8 | 2048 | 16 | 128×2048 = 262K | ~2.5 GB |
| Phase 2 | 4 | 4096 | 32 | 128×4096 = 524K | ~3.0 GB |
| Phase 3-4 | 4 | 8192 | 16 | 64×8192 = 524K | ~3.5 GB |

### Tasks

- **Task 5.1:** Model implementation in TorchSharp (standard transformer + morph features)
- **Task 5.2:** Training loop (TorchSharp + ONNX teacher + gradient offloading)
- **Task 5.3:** Vocabulary alignment (student 32K → Jais 64K + Qwen vocab)
- **Task 5.4:** Progressive context curriculum implementation
- **Task 5.5:** Training execution (~10 weeks on i9H + 6GB GPU)
- **Task 5.6:** Checkpoint management and validation
- **Task 5.7:** Training monitoring (loss curves, gradient norms, KD alignment)

---

## 14. Epic 6: Progressive Quantization (NEW)

### Protocol

After Path A training completes, systematically quantize and measure quality at each step.

```
Step 1: Evaluate FP16 model
  → Perplexity on validation set
  → Accuracy on SLM-Arabic benchmarks
  → Inference speed + memory footprint
  → RECORD as baseline

Step 2: Quantize to INT8 (per-channel, symmetric)
  → Same evaluations
  → If quality ≥ 99% of FP16 → PROCEED to Step 3
  → If quality < 99% of FP16 → STOP, ship INT8 (or try per-channel asymmetric)

Step 3: Quantize to INT4 (GPTQ or AWQ)
  → Same evaluations
  → If quality ≥ 97% of FP16 → PROCEED to Step 4
  → If quality < 97% of FP16 → STOP, ship INT4
  
Step 4: Quantize to 1.58-bit (ternary, post-training)
  → Same evaluations
  → If quality ≥ 95% of FP16 → Ship 1.58-bit
  → If quality < 95% of FP16 → ACTIVATE PATH B (ternary-from-scratch research)
```

### Quality Metrics at Each Step

| Metric | FP16 | INT8 | INT4 | 1.58-bit | Minimum |
|---|---|---|---|---|---|
| Validation perplexity | baseline | <1.02× FP16 | <1.05× FP16 | <1.10× FP16 | ≤1.10× FP16 |
| MorphAnalysis-500 | baseline | <1% drop | <3% drop | <5% drop | ≤5% drop |
| Diacritization-Acc | baseline | <1% drop | <3% drop | <5% drop | ≤5% drop |
| Inference speed (tok/s) | baseline | 1.5-2× FP16 | 2-3× FP16 | 3-4× FP16 | — |
| Memory footprint | baseline | 50% of FP16 | 25% of FP16 | 10% of FP16 | ≤500 MB total |

### Tasks

- **Task 6.1:** INT8 quantization (per-channel symmetric, calibration on 1000 samples)
- **Task 6.2:** INT8 evaluation
- **Task 6.3:** INT4 quantization (GPTQ or AWQ via custom C# implementation or ONNX quantization)
- **Task 6.4:** INT4 evaluation
- **Task 6.5:** 1.58-bit post-training quantization (sign + group-scale extraction)
- **Task 6.6:** 1.58-bit evaluation
- **Task 6.7:** Decision gate: compare all levels, decide shipping target, activate Path B if needed

---

## 15. Epic 7: Ternary Research Path (Conditional)

**ACTIVATED ONLY IF:** Path A's 1.58-bit quantization loses >5% quality vs FP16.

### Model Architecture (Path B — Ternary Transformer)

All specifications from previous plan: d_model=2048, N_layers=18, FinMax V2, MASP, MCLAS, AKQ, SwiGLU V2 (gate bias=2.0), ~863M ternary params (~170 MB storage).

### Tasks

- **Task 7.1:** FinMax V2 implementation (argmax-based, SG-STE, per-head-type alpha)
- **Task 7.2:** MASP implementation (sigmoid gate, threshold=0.5, binary entropy loss)
- **Task 7.3:** MCLAS implementation (every-3-layer sharing, conditional on ablation)
- **Task 7.4:** AKQ implementation (morph int8, std int4 KV-cache)
- **Task 7.5:** Ternary training loop (3-phase FinMax blend, PCC)
- **Task 7.6:** Training execution (~12 weeks on i9H + 6GB GPU, tighter VRAM)
- **Task 7.7:** Comparison with Path A at 1.58-bit
- **Task 7.8:** Decision: adopt Path B if it beats Path A at 1.58-bit; otherwise keep Path A at INT4

### Three-Phase FinMax Blend (Path B only)

```
Steps 1-3K:       Full softmax attention (warmup)
Steps 3K-8K:      FinMax V2 with CLAMPED weights [0, 1]
Steps 8K-12K:     Soft negative weight introduction
Steps 12K+:       Full FinMax V2 (unclamped)
```

---

## 16. Epic 8: Runtime Architecture & C# Engine

### Request Pipeline (Complete)

```
User Message (Arabic)
      ↓
[1] Lisan.Tokenizer.Normalize()
      - Unicode normalization
      - Alef/Yaa normalization
      - Remove tatweel
      ↓
[2] Lisan.Morphology.Analyze()
      - Extract roots, patterns, POS for key terms
      - Disambiguate using context
      ↓
[3] ML.NET Context Classifier (Platt-scaled)
      - Domain: religious/linguistic/general/dialectal
      - Intent: lookup/analysis/generation/translation
      ↓
[4] Neo4j GraphRAG Retrieval
      - Root graph traversal
      - Domain-filtered lookup
      - Quran/Hadith reference retrieval
      - Synonym/antonym chain expansion
      ↓
[5] Vector Search (FAISS)
      - Semantic similarity search
      - Corpus passage retrieval
      ↓
[6] Context Assembly
      - Combine: graph results + vector results + morphological analysis
      - Deduplicate and rank by relevance
      - Trim to context budget (4K/8K/16K/32K)
      ↓
[7] Model Inference
      - Tokenize with morph features (root+pattern+POS)
      - Student model generates response
      - RAG context prepended or injected at context layers
      ↓
[8] Post-Processing
      - Syntactic Morpher (PDA): enforce grammatical rules
      - Diacritization: GraphRAG lookup + neural refinement
      - MOC Reassembly: decomposed → surface form for OOV
      - Dialect adjustment: Lisan.Dialect (if dialectal output requested)
      ↓
Diacritized Arabic Response
```

### API Endpoints

| Endpoint | Method | Description |
|---|---|---|
| `/v1/chat/completions` | POST | OpenAI-compatible chat with streaming |
| `/v1/embeddings` | POST | Morphology-aware embeddings |
| `/v1/morphology/analyze` | POST | Full morphological analysis |
| `/v1/morphology/root` | POST | Root extraction only |
| `/v1/morphology/pattern` | POST | Pattern identification only |
| `/v1/morphology/diacritize` | POST | Full diacritization |
| `/v1/syntax/parse` | POST | Sentence parsing (إعراب) |
| `/v1/syntax/check` | POST | Grammar checking |
| `/v1/dialect/translate` | POST | Dialect ↔ MSA translation |
| `/v1/knowledge/search` | POST | Neo4j graph search |
| `/v1/knowledge/quran` | POST | Quranic verse lookup by root/concept |

### Tasks

- **Task 8.1:** Request pipeline implementation (steps 1-8)
- **Task 8.2:** API layer (ASP.NET Core, OpenAI-compatible)
- **Task 8.3:** Blazor Chat UI
- **Task 8.4:** Inference engine (model loading, KV-cache, generation loop)
- **Task 8.5:** Profiling and optimization
- **Task 8.6:** Lite mode (4 GB RAM, no Neo4j, dictionary cache only)
- **Task 8.7:** Tiered context management (4K/8K/16K/32K)

---

## 17. Epic 9: Evaluation & Refinement

### SLM-Arabic Evaluation Suite

| Benchmark | What It Measures | Success Criterion |
|---|---|---|
| MorphAnalysis-500 | Root+pattern+POS extraction on 500 held-out words | >90% accuracy |
| Diacritization-Acc | Full diacritization on 1000 sentences | >85% word-level |
| GrammarJudgment-300 | Grammatical acceptability on 300 pairs | >80% accuracy |
| Dialect-ID-200 | Dialect identification on 200 sentences | >75% accuracy |
| Coherence-AR-100 | Local coherence judgment on 100 pairs | >70% accuracy |
| QA-Morph-200 | Morphological Q&A | >75% accuracy |
| QA-Quran-100 | Quranic verse lookup by concept/root | >90% retrieval |
| RAG-Retrieval-100 | End-to-end RAG retrieval quality | >85% relevance |

### RAG-Only Evaluation (Before Model Training)

| Benchmark | What It Measures | Success Criterion |
|---|---|---|
| Template-Response-100 | Template-based response quality | >60% "acceptable" by human review |
| Graph-Lookup-200 | Neo4j graph traversal accuracy | >90% correct root/meaning retrieval |
| Vector-Search-100 | FAISS retrieval relevance | >80% top-3 relevance |
| End-to-End-RAG-50 | Full RAG pipeline without model | >50% queries answered satisfactorily |

### Quantization Evaluation (After Model Training)

See Epic 6 for progressive quantization protocol.

### Path B vs Path A Comparison (If Path B is Activated)

| Metric | Path A (INT4) | Path A (1.58-bit) | Path B (ternary) | Winner |
|---|---|---|---|---|
| Perplexity | | | | |
| MorphAnalysis-500 | | | | |
| Diacritization-Acc | | | | |
| Inference speed | | | | |
| Memory footprint | | | | |

---

## 18. Complete Risk Register (All 40 Risks)

All prior risks (R1-R38) remain. New risks:

| # | Risk | Severity | Probability | Status | Mitigation |
|---|---|---|---|---|---|
| R39 | RAG pipeline quality insufficient for standalone product | **Medium** | **Medium** | **MONITOR** | Template-based responses cover morphological queries well; if <50% coverage, expand template library |
| R40 | Progressive quantization at 1.58-bit loses >10% quality (forces Path B) | **Medium** | **Medium** | **MONITOR** | Path B is well-specified; additional 12 weeks training time is acceptable if needed |

### Retired Risks

| # | Risk | Why Retired |
|---|---|---|
| R32 | Progressive teacher training quality | External teacher eliminates this entirely |
| R34 | Jais ONNX conversion fails | Now have dual-teacher fallback (Jais + Qwen) |

---

## 19. Novel Research Contributions

### Primary Contributions (Both Paths)

1. **RAG-First Arabic Linguistic Architecture:** Demonstrating that Arabic linguistic intelligence can be delivered via RAG + templates before any neural model is trained, reducing time-to-product from months to weeks.
2. **Morphological Feature Injection for SLMs:** Injecting root+pattern+POS features at the embedding layer to give the model free morphological knowledge, saving ~50-100M params of learning capacity.
3. **Ensemble Teacher KD:** Dual-teacher distillation from Arabic-native (Jais) and multilingual (Qwen) models for broader Arabic coverage.
4. **AVR + MOC:** Adaptive Vocabulary Resolution with Morphological Output Composition for 200K+ effective vocabulary from 32K base.
5. **Progressive Quantization with Decision Gates:** Systematic quantization protocol with quality gates, providing the first empirical comparison of FP16→INT8→INT4→1.58-bit vs ternary-from-scratch for Arabic SLMs.
6. **Lisan NLP Layer:** First comprehensive .NET-native Arabic NLP library suite (Morphology, Tokenizer, Syntax, Dialect).

### Conditional Contributions (Path B Only)

7. **FinMax V2:** Ternary-output sparse attention with argmax formulation and per-head-type alpha
8. **MASP:** Morphology-aware structured pruning with binary entropy loss
9. **MCLAS:** Morphological Cross-Layer Attention Sharing
10. **AKQ:** Asymmetric KV Quantization (morphological int8, standard int4)
11. **PCC + Ternary Training:** Progressive Context Curriculum for ternary SLM training

---

## 20. Implementation Phases (Final Timeline)

### Phase 0: Validation (Week 1)
- Task 0.5: TorchSharp CUDA validation
- Task 0.6: Dual-machine setup
- Task 0.7: ONNX teacher setup (Jais + Qwen)
- **Gate:** Both TorchSharp+CUDA and ONNX teacher must pass

### Phase 1: Corpus + Knowledge Foundation (Weeks 2-4)
- Tasks 1.1-1.10: Corpus, morphological pipeline, 32K vocab, synthetic data
- Tasks 2.1-2.11: Neo4j graph (ENHANCED schema), GraphRAG API
- Run synthetic data generation on i7 in parallel

### Phase 2: NLP Layer (Weeks 4-7)
- Tasks 3.1-3.6: Lisan.Morphology, Tokenizer, Syntax, Dialect
- **DELIVERABLE:** Working .NET Arabic NLP libraries (standalone, no model needed)
- **i7 runs data prep and evaluation in parallel**

### Phase 3: RAG System (Weeks 7-10)
- Tasks 4.1-4.8: Embeddings, FAISS, GraphRAG pipeline, template responses, RAG evaluation
- **DELIVERABLE:** Working Arabic linguistic intelligence system (RAG + templates)
- **This is the first shippable product — available at Week 10**
- **i7 hosts Neo4j + FAISS; i9H runs RAG server**

### Phase 4: Student Model Training (Weeks 10-20)
- Tasks 5.1-5.7: FP16 standard transformer with morph features, external teacher KD, PCC
- ~10 weeks of training on i9H + 6GB GPU
- **i7 continues RAG service + evaluation in parallel**
- **Validation checkpoints every 2 weeks**

### Phase 5: Progressive Quantization (Weeks 20-22)
- Tasks 6.1-6.7: INT8 → INT4 → 1.58-bit with quality gates
- **Decision gate at Week 22:** Ship Path A result or activate Path B
- **If INT4 meets requirements → ship at Week 22**

### Phase 6: Runtime + API (Weeks 22-26)
- Tasks 8.1-8.7: Request pipeline, API, Blazor UI, inference engine, profiling
- **DELIVERABLE:** Full Lisan system with neural model + RAG + NLP layer
- **i7 runs evaluation benchmarks in parallel**

### Phase 7: Evaluation + Refinement (Weeks 26-28)
- Full evaluation suite (SLM-Arabic benchmarks + RAG benchmarks + quantization benchmarks)
- Paper writing
- Final model packaging and export

### Phase 8 (Conditional): Ternary Research Path (Weeks 22-34)
- **ACTIVATED ONLY IF:** Path A's 1.58-bit quantization loses >5% quality
- Tasks 7.1-7.8: FinMax V2, MASP, MCLAS, AKQ, ternary training, comparison
- Adds 12 weeks to timeline
- Total project with Path B: ~40 weeks

---

**Total project timeline (Path A only): 28 weeks**  
**Total project timeline (with Path B): 40 weeks**  
**First shippable product (RAG + templates): Week 10**  
**First model-enhanced product: Week 22**

---

## 21. Confidence Assessment (Honest)

| Goal | Confidence | Key Dependency | Honest Caveat |
|---|---|---|---|
| Best-in-class Arabic morphological analysis | **90%** | RAG + templates + morph features + GraphRAG = deterministic excellence | Only within Arabic linguistic domain; NOT general AI |
| Best-in-class Arabic diacritization | **85%** | PDA + GraphRAG + neural refinement | Ambiguous cases (Quranic readings) need human review |
| Match 7B on grammatical tasks | **75%** | RAG-enhanced generation + morphological attention | General grammar, not edge cases |
| Approach 3B on general Arabic chat | **55%** | 500M params + dual-teacher KD; limited by param count | Cannot store world knowledge like larger models |
| Deliver RAG product by Week 10 | **90%** | RAG + templates are software, not research | Template quality depends on graph completeness |
| Ship INT4 model by Week 22 | **75%** | Training on schedule + INT4 quality ≥97% of FP16 | Risk: training issues or INT4 quality gap |
| Complete within 28 weeks (Path A) | **70%** | Depends on TorchSharp CUDA working; training convergence on schedule | CPU fallback adds 4-6 weeks |
| Novel publishable contributions | **90%** | RAG-first architecture, morph features, dual-path quantization comparison | Even if Path B isn't activated, the comparison is novel |

### What Could Go Wrong (Honest)

1. **TorchSharp CUDA may fail.** Mitigation: CPU-only adds 4-6 weeks. SLM is small enough for CPU training.
2. **ONNX teacher conversion may fail.** Mitigation: Dual-teacher fallback (Jais + Qwen). Worst case: train custom teacher (adds 8 weeks).
3. **INT4 quality may drop below 97%.** Mitigation: INT8 ships at 50% footprint; still excellent for consumer hardware. Path B is the research backup.
4. **RAG templates may be too rigid.** Mitigation: Expand template library; add more patterns. The neural model will fix this when it's ready.
5. **500M params may be insufficient for general Arabic chat.** Mitigation: Option A doesn't target general chat. If Option B is needed later, scale up with more compute.
6. **Morphological features may not help as much as expected.** Mitigation: Ablation test with and without features. If <2% improvement, remove to simplify.

### What Will Definitely Work

1. **RAG + templates for Arabic linguistic queries.** Root extraction, pattern lookup, meaning retrieval — these are database operations, not AI.
2. **Neo4j knowledge graph.** Proven technology, well-suited to Arabic morphology.
3. **Lisan NLP layer.** Pure C# code. No research risk.
4. **FP16 standard transformer training.** Proven architecture, well-understood.
5. **INT8 quantization.** Nearly lossless for all transformer models. Will work.
6. **External teacher KD.** Standard technique, well-validated.
7. **Running on 8 GB RAM.** INT4 model = ~250 MB weights. Total system < 1 GB. Definitely works.
8. **The first shippable product at Week 10.** RAG + templates + NLP layer = working linguistic tool. No training required.

---

## Appendix A: Request Pipeline Architecture

### Complete Pipeline with Error Handling

```
INPUT: User Message (Arabic text)
    |
    v
[1] Lisan.Tokenizer.Normalize(input)
    → Normalized text
    → On failure: return original text (graceful degradation)
    |
    v
[2] Lisan.Morphology.Analyze(normalized_text)
    → List of { word, root, pattern, POS, lemma }
    → On OOV: attempt Farasa fallback
    → On complete failure: skip morphological features (model still works)
    |
    v
[3] Context Classifier (ML.NET, Platt-scaled)
    → { domain, intent, dialect } with confidence scores
    → On low confidence (<0.5): default to domain=general, intent=generation
    |
    v
[4] Neo4j GraphRAG Retrieval
    → For each root/pattern: traverse graph
    → Collect: related words, meanings, Quran references, synonyms
    → Filter by domain
    → On Neo4j unavailable: skip graph retrieval (vector search still works)
    |
    v
[5] Vector Search (FAISS)
    → Embed query (use Sentence-BERT or model embedding)
    → Search FAISS index for top-K passages
    → Rank by similarity + domain match
    → On FAISS unavailable: skip vector search (graph retrieval still works)
    |
    v
[6] Context Assembly
    → Combine: morphological_analysis + graph_results + vector_results
    → Deduplicate
    → Rank by relevance (domain match > vector similarity > graph distance)
    → Trim to context budget (4K/8K/16K/32K tokens)
    → If no retrieval results: use query-only (model generates without context)
    |
    v
[7] Model Inference
    → Tokenize with morph features (root+pattern+POS embeddings)
    → Prepend RAG context
    → Generate response (top-p sampling, temperature=0.7)
    → If model unavailable: use template-based response
    |
    v
[8] Post-Processing
    → Syntactic Morpher: enforce grammatical rules
    → Diacritization: GraphRAG lookup + neural refinement
    → MOC Reassembly: decomposed tokens → surface form
    → Dialect adjustment: apply Lisan.Dialect rules if dialectal output requested
    → On failure: return model output as-is (best effort)
    |
    v
OUTPUT: Diacritized Arabic Response
```

**Key design principle:** Every step has graceful degradation. The system produces useful output even when individual components fail. This makes the system reliable in production.

---

## Appendix B: Progressive Quantization Protocol

### INT8 Quantization

```
Method: Per-channel symmetric quantization
  - For each weight tensor W of shape [out, in]:
    - Compute per-channel scale: scale[c] = max(|W[c,:]|) / 127
    - Quantize: W_int8[c,i] = round(W[c,i] / scale[c])
    - Dequantize: W_approx[c,i] = W_int8[c,i] * scale[c]
  
Calibration: Run 1000 samples through model, compute scale factors
Expected quality: ≥99% of FP16 (near-lossless for transformers)
Memory: 50% of FP16
Speed: 1.5-2× FP16 (INT8 matrix multiply is faster on most CPUs)
```

### INT4 Quantization

```
Method: GPTQ (GPU-accelerated) or AWQ (activation-aware)
  - GPTQ: Layer-wise quantization with Hessian-based correction
  - AWQ: Protect salient weights based on activation magnitude
  - Both preserve >97% of FP16 quality for modern transformers

Implementation options:
  1. Use ONNX Runtime quantization (built-in INT4 support)
  2. Implement GPTQ in C# (complex but self-contained)
  3. Use llama.cpp quantization as reference, port to C#

Expected quality: ≥97% of FP16
Memory: 25% of FP16 (~116 MB for 500M params)
Speed: 2-3× FP16 (4-bit integer operations)
```

### 1.58-bit Quantization (Post-Training)

```
Method: Sign + group-scale extraction
  - For each weight tensor W:
    - Compute per-group scale: scale[g] = mean(|W[g]|)
    - Quantize: W_ternary[g,i] = sign(W[g,i])  (values: -1, 0, +1)
    - Threshold: |W[g,i]| < 0.3 * scale[g] → 0
    - Store: sign bits + group scales

Expected quality: 85-95% of FP16 (highly variable depending on model and task)
Memory: ~10% of FP16 (~91 MB for 500M params)
Speed: 3-4× FP16 (ternary multiply = additions only)

Decision: If quality ≥95% → ship. If <95% → activate Path B.
```

---

## Appendix C: Training Loop Skeleton (C#)

```csharp
using TorchSharp;
using Microsoft.ML.OnnxRuntime;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

public class LisanTrainer
{
    private readonly LisanModel _student;          // Path A: standard transformer + morph features
    private readonly OnnxTeacherProvider _jais;     // Jais-1.3B via ONNX
    private readonly OnnxTeacherProvider _qwen;     // Qwen2-1.5B via ONNX (optional)
    private readonly optim.AdamW _optimizer;
    private readonly string _device;
    private readonly int _accumSteps;
    private readonly VocabularyAligner _jaisAligner;
    private readonly VocabularyAligner _qwenAligner;
    
    public void TrainStep(Tensor inputIds, Tensor targets, int step)
    {
        // Progressive context curriculum
        int contextLen = step switch
        {
            <= 10000 => 2048,
            <= 40000 => 4096,
            _ => 8192
        };
        
        // KD weight schedule
        double alphaJais = step switch
        {
            <= 10000 => 0.5,
            <= 40000 => 0.4,
            <= 60000 => 0.2,
            _ => 0.0
        };
        double alphaQwen = step switch
        {
            <= 10000 => 0.2,
            <= 40000 => 0.15,
            <= 60000 => 0.1,
            _ => 0.0
        };
        
        // TAMPT: Teacher inference on CPU (background threads)
        var jaisTask = Task.Run(() => 
            _jais.GetLogits(_jaisAligner.MapInput(inputIds)));
        var qwenTask = _qwen != null ? Task.Run(() => 
            _qwen.GetLogits(_qwenAligner.MapInput(inputIds))) : null;
        
        // Student forward on GPU
        using var scope = torch.NewDisposeScope();
        var studentLogits = _student.Forward(inputIds.to(_device));
        
        // Wait for teachers
        var jaisLogits = jaisTask.Result;
        var qwenLogits = qwenTask?.Result;
        
        // Compute losses
        var taskLoss = Funcs.CrossEntropy(studentLogits, targets);
        var totalLoss = taskLoss;
        
        if (alphaJais > 0)
        {
            var jaisKDLoss = Funcs.KLDivergence(
                Funcs.LogSoftmax(studentLogits / 2.0, dim: -1),
                Funcs.Softmax(jaisLogits / 2.0, dim: -1));
            totalLoss += alphaJais * jaisKDLoss;
        }
        
        if (alphaQwen > 0 && qwenLogits != null)
        {
            var qwenKDLoss = Funcs.KLDivergence(
                Funcs.LogSoftmax(studentLogits / 2.0, dim: -1),
                Funcs.Softmax(qwenLogits / 2.0, dim: -1));
            totalLoss += alphaQwen * qwenKDLoss;
        }
        
        // Backward with gradient accumulation
        (totalLoss / _accumSteps).backward();
        
        if (step % _accumSteps == 0)
        {
            utils.clip_grad_norm_(_student.Parameters(), 1.0);
            _optimizer.step();
            _optimizer.zero_grad();
        }
        
        // Logging
        if (step % 100 == 0)
        {
            Console.WriteLine($"Step {step}: task={taskLoss.item<float>():F4} " +
                $"ctx={contextLen} α_jais={alphaJais} α_qwen={alphaQwen}");
        }
    }
}
```

---

## Appendix D: Hyperparameter Summary (Final)

### Path A: Standard FP16 Transformer

| Hyperparameter | Value |
|---|---|
| d_model | 1536 |
| N_layers | 16 |
| Q heads | 12 (4 morph + 8 std) |
| KV groups | 4 (2 morph + 2 std) |
| d_k | 128 |
| d_v per group | 128 × 3 = 384 (3 Q per KV) |
| d_ff (SwiGLU) | 4096 |
| Vocab | 32K + AVR |
| Total params | ~500M |
| Attention | Standard softmax |
| FFN | Standard SwiGLU (gate bias = 0) |
| Position | RoPE on Q/K, θ = 10000 |
| Morph features | Token(1536) + Root(256) + Pattern(128) + POS(64) = 1984 → 1536 |

### Training

| Hyperparameter | Value |
|---|---|
| Framework | TorchSharp + LibTorch CUDA |
| Teacher(s) | Jais-1.3B + Qwen2-1.5B via ONNX Runtime |
| Context schedule | PCC: 2048 → 4096 → 8192 |
| Max inference context | 32768 (YARN) |
| Total training steps | 70K |
| LR warmup | 0 → 3e-4 over 2K steps |
| LR stable | 3e-4 (steps 2K-40K) |
| LR decay | Cosine 3e-4 → 1e-5 (steps 40K-60K) |
| LR fine-tune | 1e-5 (steps 60K+) |
| KD temperature | T = 2.0 |
| KD schedule | α_jais: 0.5→0.4→0.2→0.0; α_qwen: 0.2→0.15→0.1→0.0 |

### Quantization

| Level | Weight Size | Total Inference (8K) | Expected Quality |
|---|---|---|---|
| FP16 | 1.0 GB | ~1.4 GB | 100% (baseline) |
| INT8 | 500 MB | ~900 MB | ≥99% |
| INT4 | 116 MB | ~520 MB | ≥97% |
| 1.58-bit | 91 MB | ~495 MB | ≥95% (if not, activate Path B) |

### Path B: Ternary Transformer (Conditional)

| Hyperparameter | Value |
|---|---|
| d_model | 2048 |
| N_layers | 18 |
| Q heads | 16 (4 morph + 12 std) |
| KV groups | 4 (2 morph + 2 std) |
| d_ff (SwiGLU V2) | 5632 |
| Total params | ~863M (ternary) |
| Ternary storage | ~170 MB |
| Attention | FinMax V2 |
| FFN | SwiGLU V2 (gate bias = 2.0) |

---

## Summary: What Changed from the Previous Plan

| Aspect | Previous Plan | This Plan | Why |
|---|---|---|---|
| Product definition | Mixed (Option A + B) | **Option A only (Arabic Linguistic Intelligence)** | External review: choose one product |
| Phase ordering | Training before RAG | **RAG before training** | Working product by Week 10 instead of Week 18 |
| Model path | Ternary-from-scratch only | **FP16 → progressive quantize (primary) + ternary (conditional)** | External review: safer path; we add ternary as research backup |
| Model size | 863M ternary | **500M FP16** (Path A) / 863M ternary (Path B) | 500M FP16 fits GPU; proven architecture |
| Teacher | Jais-1.3B only | **Jais + Qwen (ensemble)** | Broader Arabic coverage |
| NLP layer | Not specified | **Lisan.Morphology/Tokenizer/Syntax/Dialect** | External review: .NET-native libraries |
| Request pipeline | Partially specified | **Complete 8-step pipeline with graceful degradation** | External review: detailed architecture |
| First shippable product | Week 18 (model training complete) | **Week 10 (RAG + templates + NLP layer)** | 8 weeks earlier |
| Total timeline | 30 weeks | **28 weeks (Path A) / 40 weeks (Path B)** | Slightly shorter primary path |
| Morphological features | Included | **Included (both paths)** | Proven technique, NOT speculative research |
| FinMax V2 / MASP / MCLAS | Always active | **Conditional (Path B only)** | Deferred until proven necessary |
| API endpoints | 2 (chat + embeddings) | **11 (chat + embeddings + morphology + syntax + dialect + knowledge)** | Option A needs linguistic APIs |
| Risk profile | Higher (custom research from day 1) | **Lower (standard model first, research later)** | Progressive risk reduction |

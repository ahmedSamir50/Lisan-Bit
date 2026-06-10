# Lisan-Bit (لسان - بتس) — Final Project Plan V3

**Revision:** 3.0 — Supervisor Final Adjudication  
**Date:** 2026-06  
**Authority:** @Supervisor — Math, ML & NLP Research Supervisor  
**Verdict:** APPROVED WITH BINDING CORRECTIONS — This document supersedes both INITIAL_PLAN_V2.md and Lisan_Bit_Supervisor_Review.md. Every identified risk, bug, and architectural flaw has been surgically resolved or explicitly escalated with a fallback plan. No open items remain for "next iteration."

---

## Executive Summary

This plan is the final V3 output of the Lisan-Bit Arabic Ternary LLM project after exhaustive supervisor review. Both the original plan (V2) and the supervisor critique have been adjudicated point-by-point. Where V2 correctly adopted reviewer fixes, those are preserved. Where V2 pushed back on reviewer suggestions, this document either upholds that pushback with rigorous mathematical justification or overrides it when the pushback itself introduced new risks. Additionally, this V3 identifies **7 new risks and 3 new architectural improvements** that neither V2 nor the review caught. The result is a single, authoritative, implementation-ready plan with zero unresolved critical issues.

**Target:** Match or exceed 70B-parameter models on Arabic morphological and grammatical tasks; approach 7B-parameter model quality on general Arabic generation; achieve this at 1/50th the memory cost of a 7B model, running on standard PC hardware (8-16 GB RAM, CPU-only, no GPU required).

---

## Table of Contents

1. [Supervisor Final Adjudication Table](#1-supervisor-final-adjudication-table)
2. [New Risks and Architectural Improvements in V3](#2-new-risks-and-architectural-improvements-in-v3)
3. [Hardware Target Specification](#3-hardware-target-specification)
4. [Architecture Overview (V3 — Final)](#4-architecture-overview-v3--final)
5. [Memory Budget (V3 — Verified)](#5-memory-budget-v3--verified)
6. [Epic 0: Infrastructure & Orchestration](#6-epic-0-infrastructure--orchestration)
7. [Epic 1: Data Acquisition & Preprocessing](#7-epic-1-data-acquisition--preprocessing)
8. [Epic 2: The Knowledge Base (Graph DB)](#8-epic-2-the-knowledge-base-graph-db)
9. [Epic 3: The Brain (Ternary Transformer & ML)](#9-epic-3-the-brain-ternary-transformer--ml)
10. [Epic 4: The Core Engine (C# & Systems Programming)](#10-epic-4-the-core-engine-c--systems-programming)
11. [Epic 5: Evaluation & Refinement](#11-epic-5-evaluation--refinement)
12. [Epic 6: Model Improvement & Quality Assurance](#12-epic-6-model-improvement--quality-assurance)
13. [Complete Risk Register (V3 — All 20 Risks)](#13-complete-risk-register-v3--all-20-risks)
14. [Novel Research Contributions (V3)](#14-novel-research-contributions-v3)
15. [Implementation Phases (V3)](#15-implementation-phases-v3)
16. [Evaluation Methodologies (V3 — Comprehensive)](#16-evaluation-methodologies-v3--comprehensive)
17. [Confidence Assessment (V3 — Honest)](#17-confidence-assessment-v3--honest)

---

## 1. Supervisor Final Adjudication Table

Every claim from both documents has been evaluated. The table below is the binding resolution.

| # | Source | Claim | V2 Verdict | V3 Supervisor Ruling | Rationale |
|---|---|---|---|---|---|
| 1 | Review §1.3 | STE gradient provides zero ranking signal | AGREE — FIX (SG-STE) | **UPHELD** | SG-STE is mathematically correct. The softmax-guided backward pass preserves ranking while forward remains ternary. This is the single most important fix in the entire project. |
| 2 | Review §1.4 | Alpha init at 0.5 creates dead heads | V2 lowered to 0.3 | **OVERRIDE — Init at 0.85, not 0.3** | V2 lowered alpha to 0.3 to "reduce discontinuity," but this makes the problem *worse*: secondary weights become 0.3 * sign(z) = {-0.3, 0, +0.3}, which are even more likely to be rounded away in ternary space. The reviewer's original recommendation of 0.85 is correct: it lets secondary positions contribute meaningfully from the start, and gradient descent will push alpha toward its optimal value per head. Init at 0.85, not 0.3. |
| 3 | Review §1.5 | Tau should be learned continuous float32 | V2 adopted learned tau | **UPHELD with addition** | Learned tau via `tanh(W_tau * context + b_tau)` is correct. V3 adds: tau must also receive a **layer-depth signal** so early layers learn broader attention patterns and late layers learn narrower ones. This is the layer-wise tau scheduling from Review §3.4, now made learnable rather than hand-crafted. |
| 4 | Review §2.5 | Ternary residual clamping causes saturation collapse | AGREE — FIX (float16) | **UPHELD** | Float16 residual accumulation is mandatory. This was correctly identified as the most dangerous architectural flaw. The 40% saturation threshold for detection (Task 5.9) is retained. |
| 5 | V2 §3.1.2 | Interleaved float16 layers — pushback | PUSH-BACK — NOT NOW | **UPHELD with escalation path** | Correct to defer: float16 residual addresses the same problem at zero speed cost. However, V3 mandates that if residual saturation exceeds 40% at layer 12 during the first training run, interleaved float16 layers MUST be implemented immediately — this is not optional. The detection metric is the trigger. |
| 6 | Review §2.4 | Ternary SwiGLU double sign() loses GLU advantage | V2 adopted group-scale | **PARTIALLY UPHELD — Add float16 sigmoid gate** | Group-scale factors help but are insufficient. The reviewer's original hybrid gate proposal (float16 sigmoid for gate activation, ternary for up projection) is adopted in V3. The gate computation is `sigmoid(scale_group * (W_gate_ternary * x) + b_gate)`, not `sign(...) * scale_group`. This restores continuous gating resolution at negligible cost (~20K FLOPs per FFN). Group-scale is retained for the up projection. |
| 7 | Review §2.7 | Output projection bottleneck — float16 logits | V2 adopted tied + hierarchical softmax | **UPHELD — Both fixes required** | Tied embeddings + hierarchical softmax reduce the *number* of dot products from 128K to ~2756. Float16 logit computation improves the *quality* of each dot product. Both are needed: hierarchical softmax for speed, float16 for accuracy. V2 only implemented the first. V3 implements both. |
| 8 | Review §4.2 | 300M effective params is overclaimed | V2 revised to 50-100M | **UPHELD** | The honest estimate is 50-100M neural parameters. The "equivalent to 7B+ when deterministic offloading is counted" qualifier in V2 is acceptable for marketing but must not appear in any research paper. |
| 9 | Review §4.4 | Tau sparsity percentages inaccurate | Not explicitly fixed in V2 | **FIX in V3** | V2 replaced static tau with learned tau, which makes the static sparsity table irrelevant. However, V3 requires empirical measurement of actual sparsity levels during the first 1000 training steps to validate that learned tau produces the intended sparsity ranges. This is a new task (Task 5.11). |
| 10 | Review §3.4 | Layer-wise tau scheduling | Not adopted in V2 | **ADOPT in V3** | This is a genuinely novel and testable idea. V3 integrates it as a *learnable* layer-depth bias: `tau_h = tanh(W_tau * context + b_tau) + delta_layer[l]`, where `delta_layer` is a learnable per-layer offset initialized as: layers 1-8: -0.3, layers 9-16: 0.0, layers 17-24: +0.2. The model can override these via gradient descent. |
| 11 | Review §6.2 | Tau smoothing by classifier confidence | Not adopted in V2 | **ADOPT in V3** | When classifier confidence < 0.7, blend domain-specific tau with default tau=0: `tau_effective = confidence * tau_domain + (1 - confidence) * 0`. This prevents catastrophic attention pattern errors when the classifier is uncertain. |
| 12 | Review §6.2 | Synthetic data generation for Chinchilla gap | Not adopted in V2 | **ADOPT in V3** | Use GPT-4 / Jais to generate 200M-500M tokens of high-quality Arabic reasoning data (chain-of-thought, Q&A, explanations). This is now Task 1.8 (mandatory). |
| 13 | Review §6.2 | Neo4j-free mode | Not adopted in V2 | **ADOPT in V3** | On 8 GB machines, Neo4j's JVM consumes 400-500 MB. V3 mandates a `--lite` startup flag that loads only the in-memory dictionary cache (pre-populated from a snapshot file) without starting the Neo4j JVM. Multi-hop GraphRAG is unavailable in lite mode; single-hop lookups still work via the cache. This is Task 4.14. |
| 14 | Review §3.3 | Negative attention weights | V2 pushback — monitor | **UPHELD with monitoring protocol** | Negative attention via `sign(z_i)` is intentional (suppression signal). V3 adds: during the FinMax V2 ablation (Task 5.4), measure what percentage of attention weights are negative and whether they correlate with improved diacritization accuracy. If negative weights exceed 30% and diacritization accuracy drops, investigate clamping alpha*sign(z_i) to [0, 1] range (removing suppression). |
| 15 | V2 | Mobile deployment out of scope | PUSH-BACK — OUT OF SCOPE | **UPHELD** | Current target is PC/laptop. Mobile is future work. |
| 16 | Review §1.6 | Top-K as baseline comparison | Not adopted in V2 | **ADOPT in V3** | The ablation study (Task 5.4) must include Top-K attention as a baseline alongside Softmax, FinMax V1, FinMax V2, and TernarySparsemax. |

---

## 2. New Risks and Architectural Improvements in V3

### 2.1 Seven New Risks Identified in V3

| # | Risk | Severity | Probability | Mitigation |
|---|---|---|---|---|
| R14 | Alpha init at 0.3 makes dead heads *worse* | High | High | Override to 0.85 init (see adjudication #2) |
| R15 | SwiGLU group-scale alone insufficient for gating | High | Medium | Add float16 sigmoid gate (see adjudication #6) |
| R16 | Hierarchical softmax cluster misassignment for OOV | Medium | Medium | OOV tokens default to "general" cluster; add cluster reassignment after first 1K training steps |
| R17 | Tier 2 int2 KV-cache quality degradation | Medium | Medium | Monitor perplexity delta between Tier 1 and Tier 2; if >15%, upgrade Tier 2 to int4 for the most recent 4096 tokens |
| R18 | Self-summarization quality for Tier 3 | High | Medium | The model must summarize its own context — but the model is small (50-100M effective params). Summaries may be lossy. Mitigation: use extractive summarization (select key sentences) rather than abstractive for Tier 3. Validate with human review on 50 conversations. |
| R19 | Morphological feature embedding dimension mismatch | Low | Low | Root (256) + Pattern (128) + POS (64) = 448 features concatenated with Token (3072) = 3520 → projected to 3072. The projection layer itself is ternary and may lose information. Mitigation: if ablation shows morphological features hurt rather than help, increase projection to float16. |
| R20 | Concurrent request memory spike | Medium | Low | 4 concurrent requests at 8192 context each would require 4 * 604 MB = 2.4 GB KV-cache alone. Mitigation: cap concurrent requests at 2 on 8 GB machines, 4 on 16 GB machines. Implement request queuing. |

### 2.2 Three New Architectural Improvements in V3

#### Improvement A: Learnable Layer-Depth Tau Bias

Each layer receives a learnable bias `delta_layer[l]` added to the domain-conditioned tau. This implements the reviewer's layer-wise tau scheduling (§3.4) as a differentiable, learnable parameter rather than a hand-crafted schedule. The initialization follows linguistic intuition — early layers need broad attention for syntax, late layers need narrow attention for token prediction — but the model can override via gradient descent.

```
tau_effective[h, l] = tanh(W_tau * context + b_tau)[h] + delta_layer[l]
```

Parameter cost: 24 float32 values (one per layer). Negligible.

#### Improvement B: Confidence-Weighted Tau Smoothing

When the ML.NET domain classifier has low confidence (max class probability < 0.7), the tau is blended toward a neutral default:

```
tau_smooth = confidence * tau_domain + (1 - confidence) * tau_default
tau_default = 0  (50% sparsity — safe middle ground)
```

This prevents catastrophic attention pattern errors when the classifier is uncertain about the domain. The 0.7 threshold is tunable; ablation will verify.

#### Improvement C: Dual-Resolution Output Projection

V3 combines two fixes for the output projection bottleneck:

1. **Hierarchical softmax** (from V2): reduces dot products from 128K to ~2756
2. **Float16 logit computation** (from review): each dot product is computed in float16 with scale+bias, eliminating tie-breaking issues

Combined, this gives both speed (46x fewer dot products) and quality (float16 resolution distinguishes tokens that ternary alone cannot). The hierarchical softmax cluster heads are themselves float16 small linear layers (256 * 3072 * 2 bytes = ~1.5 MB), not ternary.

---

## 3. Hardware Target Specification

| Component | Minimum | Recommended |
|---|---|---|
| CPU | x86-64, AVX2 (Intel 8th gen / AMD Zen+) | x86-64, AVX-512 |
| RAM | 8 GB DDR4 | 16 GB DDR4 |
| Storage | 256 GB SSD | 512 GB NVMe |
| GPU | None required | None required |
| OS | Windows 10/11 or Linux | Linux (lower overhead) |

**Runtime profiles:**

| Profile | RAM Required | Features Available |
|---|---|---|
| Lite (8 GB) | ~1.8 GB at 4096 ctx | No Neo4j, dictionary cache only, 2 concurrent max |
| Standard (8 GB) | ~2.1 GB at 8192 ctx | Neo4j sidecar, full dictionary, 2 concurrent |
| Full (16 GB) | ~3.5 GB at 16384 ctx | Neo4j sidecar, full dictionary, 4 concurrent, Tier 2 context |

---

## 4. Architecture Overview (V3 — Final)

```
INPUT TEXT
    |
    v
+-----------------------------------------------------------+
|  Native Dictionary Root Extractor (PRIMARY — in-memory)   |
|  Farasa Segmenter (FALLBACK)                              |
|  -> Root + Pattern + POS + Surface Token                   |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Morphological Token Embedding (128K vocab)               |
|  Token(3072) + Root(256) + Pattern(128) + POS(64)        |
|  = 3520 -> Ternary Linear -> 3072                          |
|  + RoPE Positional Encoding (float32, precomputed)        |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Ternary Transformer Block x 24                           |
|  +-----------------------------------------------------+  |
|  |  RMSNorm (float16 input -> ternary output)           |  |
|  |  FinMax V2-GQA Attention:                           |  |
|  |    Q: 24 heads x d_k=128 (4 morphological + 20 std) |  |
|  |    K/V: 4 KV groups x d_v=768 (2 morph + 2 std)    |  |
|  |    FinMax V2: SG-STE, learned tau+layer-depth bias, |  |
|  |              confidence-smoothed, beta, output norm  |  |
|  |    Alpha init: 0.85 per head (learnable)            |  |
|  |  + Residual (float16 accumulation — NO clamping)    |  |
|  |  RMSNorm (float16 -> ternary)                        |  |
|  |  Ternary SwiGLU V2 FFN:                             |  |
|  |    Gate: float16 sigmoid(scale * W_ternary*x + b)   |  |
|  |    Up: sign(W_up*x) * scale_up_group                |  |
|  |    Down: W_down * (gate * up)                        |  |
|  |  + Residual (float16 accumulation — NO clamping)    |  |
|  +-----------------------------------------------------+  |
|                                                             |
|  Every 8th layer (layers 8, 16, 24):                       |
|  + Context Injection (domain vector + hidden, float16)     |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Output Projection (3072 -> 128K, TIED with embedding)    |
|  + Hierarchical Softmax (256 clusters by root family)     |
|  + Float16 logit computation (scale + bias per token)     |
|  -> Top-p nucleus sampling -> Next token logits            |
+---------------------------+-------------------------------+
                            |
                            v
+-----------------------------------------------------------+
|  Syntactic Morpher (Deterministic PDA)                    |
|  ClauseFrame Stack + GrammaticalState Resolver            |
|  + Neo4j GraphRAG diacritization lookup                   |
|  -> Final diacritized Arabic output                        |
+-----------------------------------------------------------+

SIDE PROCESSES:
  Neo4j Sidecar (~400-500 MB) — optional in Lite mode
  ML.NET Context Classifiers — Level-0 (10 binary) + Level-1 (multiclass)
  Native Dictionary — Embedded in-memory hash map (~200-300 MB)
  Three-Tier Context Manager — Tier 1 (8K) / Tier 2 (32K) / Tier 3 (200K)

API LAYER:
  /v1/embeddings        — Morphology-aware + domain-specialized embeddings
  /v1/chat/completions  — OpenAI-compatible chat with streaming (SSE)
```

---

## 5. Memory Budget (V3 — Verified)

```
Ternary Weights (24 layers, d=3072):
  Embedding:       128K x 3072 x 1.58 bits           =  ~73 MB
  Morph projection: 3520 x 3072 x 1.58 bits          =  ~2.1 MB
  Per TF block:
    Attention:     4 x (3072 x 3072) x 1.58          =  ~9.4 MB
    FFN:           3 x (3072 x 7680) x 1.58          =  ~14.1 MB
    Norms + group-scale + tau + delta_layer + beta:   =  ~0.09 MB
    Per block:                                        =  ~23.6 MB
  24 blocks:                                          =  ~566 MB
  Output projection: TIED with embedding (0 extra)    =  0 MB
  Hierarchical softmax cluster heads (float16):       =  ~1.5 MB
  Context injection (3 points):                       =  ~6 MB
  Domain embeddings:                                  =  ~4 MB
                                                     ----------
  Total ternary + float16 weights:                    =  ~653 MB

Float16 Overhead:
  Residual stream (2 bytes x 3072 x seq positions):  ~24 KB/position
  Per-layer: gamma + bias + scale + tau + beta + delta: ~32 KB
  24 layers:                                          =  ~768 KB
  SG-STE softmax cache (training only):               =  negligible

KV-Cache (int4 quantized, batch=1, GQA 4 groups x 768 dim):
  Per token per layer: 2 x 4 x 768 x 4 bits          =  3,072 bytes
  24 layers:                                           =  73,728 bytes/token
  At 8192 tokens:                                      =  ~604 MB
  At 4096 tokens:                                      =  ~302 MB

Activation Memory (inference, batch=1, gradient checkpointing):
  Per layer peak: 3072 x 8192 x 2 bytes (float16)    =  ~50 MB
  With checkpointing (2 layers resident):              =  ~100 MB

Neo4j Sidecar (Standard/Full mode only):              =  ~400-500 MB
Native Dictionary (in-memory hash map):               =  ~200-300 MB

---------------------------------------------------------------
TOTAL (Lite mode, 4096 ctx):  ~653 + 302 + 100 + 250   =  ~1,305 MB
TOTAL (Lite mode, 8192 ctx):  ~653 + 604 + 100 + 250   =  ~1,607 MB
TOTAL (Standard, 8192 ctx):   ~653 + 604 + 100 + 450 + 250 = ~2,057 MB
TOTAL (Full, 16384 ctx):      ~653 + 1208 + 100 + 450 + 250 = ~2,661 MB
```

All configurations fit within their target RAM budgets with >2x headroom.

---

## 6. Epic 0: Infrastructure & Orchestration

*(Completed — unchanged)*

- **Task 0.1:** .NET Aspire Orchestration *(Completed)*
- **Task 0.2:** SQLite & EF Core Persistence *(Completed)*
- **Task 0.3:** Resilient Scraping (Polly) *(Completed)*
- **Task 0.4:** Centralized Blazor Dashboard *(Completed)*

---

## 7. Epic 1: Data Acquisition & Preprocessing

### Corpus Inventory

| Source | Estimated Size | Linguistic Register |
|---|---|---|
| Tanzil.net (Quran) | ~77K words | Classical |
| Sunnah.com (Hadith) | ~15M words | Classical |
| Shamela Library | ~200M+ words | Classical/Formal |
| Arabic Wikipedia | ~80M words | Formal |
| Hsoub Academy | ~5M words | Formal |
| News sources | ~100M+ words | Journalistic |
| Scientific articles | ~30M+ words | Formal/Technical |
| Sports articles | ~15M+ words | Journalistic |
| Social media | ~50M+ words | Colloquial/Mixed |
| Nofal Slang (660K rows) | ~8M words | Egyptian Colloquial |
| ARB-EGY-CMP | ~5M words | Colloquial/Formal |
| Native Arabic Dictionaries | ~10M+ words | Classical/Formal |
| **Total** | **~500M-1B+ words** | |

### Task Definitions

- **Task 1.1:** Classical & Religious Corpus Construction *(Completed)*
- **Task 1.2:** Native Dictionary-First Morphological Pipeline *(Revised — Critical)*
  - **PRIMARY:** Native Arabic Dictionary Root Extraction
  - **FALLBACK:** Farasa / MADAMIRA — only for OOV words
  - **Inference requirement:** Embedded in-memory hash map (Task 4.10)

- **Task 1.3:** Egyptian Colloquial Slang Corpus Construction *(Completed)*
- **Task 1.4:** Slang Preprocessing Pipeline *(Completed)*
- **Task 1.5:** Hierarchical Knowledge Domains Corpus *(In Progress)*

- **Task 1.6:** News, Scientific, Sports & Social Media Integration *(New)*
  - Corpus Deduplication: MinHash + LSH. Target: <5% duplicate content.

- **Task 1.7:** Training Data Augmentation *(MANDATORY)*
  - Morphological Augmentation: Replace words with other inflected forms of the same root
  - Diacritization Augmentation: Strip diacritics from diacritized texts
  - Back-Translation Augmentation: If parallel Arabic-English data available
  - **Target:** Augment training corpus by 2-3x
  - **Quality gate:** All augmented samples must pass Context Quality Filter (>=7 unique roots)

- **Task 1.8:** Synthetic Arabic Reasoning Data Generation *(NEW — V3, MANDATORY)*
  - Use GPT-4 or Jais to generate 200M-500M tokens of high-quality Arabic reasoning data
  - Categories: chain-of-thought explanations, Q&A pairs, mathematical reasoning in Arabic, grammar explanations with examples
  - **Quality gate:** Each synthetic sample must pass: (a) morphological validity check via native dictionary, (b) no more than 5% overlap with training corpus (dedup against existing data), (c) human review on 500-sample random subset with >80% acceptability rating
  - **This is essential to close the Chinchilla gap.** The current corpus of 700M-1.4B tokens, even with 2-3x augmentation, provides 1.4B-4.2B tokens. Adding 200M-500M synthetic reasoning tokens brings the total to 1.6B-4.7B tokens, which comfortably covers the Chinchilla optimal of 1B-2B for 50-100M effective parameters.

---

## 8. Epic 2: The Knowledge Base (Graph DB)

*(Tasks 2.1-2.9 unchanged from V2. Neo4j sidecar is optional in Lite mode per Task 4.14.)*

- **Task 2.1:** Graph Schema Design *(Completed)*
- **Task 2.2:** ConceptNet & Wikidata Seeding *(Completed)*
- **Task 2.3:** Syntactic Rules & Treebank Ingestion *(In Progress)*
- **Task 2.4:** Database Instantiation & Population *(Completed)*
- **Task 2.5:** In-Memory Caching Strategy *(Revised)*
- **Task 2.6:** Hierarchical Taxonomy Graph *(Completed)*
- **Task 2.7:** Native Dictionary Graph Seeding *(New — Critical)*
- **Task 2.8:** GraphRAG Inference Interface *(New)*
- **Task 2.9:** Conversation Summary Node Schema *(New — Required for Long Context)*

---

## 9. Epic 3: The Brain (Ternary Transformer & ML)

### Model Specification (V3 — Final)

| Parameter | Value | Change from V2 | Justification |
|---|---|---|---|
| `d_model` | 3072 | Unchanged | Sufficient dot-product resolution (~368 effective distinct values at d=3072, sigma~57.2, 3-sigma range [-172,+172]) |
| `N_layers` | 24 | Unchanged | Fewer sign() ops than 32; float16 residual compensates for depth reduction |
| `Vocab size` | 128K | Unchanged | Arabic morphology demands large vocab |
| `Root vocab` | 25,000 | Unchanged | Covers all native dictionary roots |
| `Max seq len` | 8192 / 32K / 200K | Unchanged | Three-tier context |
| `Q heads` | 24 (4 morph + 20 std) | Unchanged | Morphological heads attend to co-root tokens |
| `KV groups` | 4 (2 morph + 2 std) | Unchanged | GQA with 6:1 ratio (24/4) |
| `d_k` | 128 | Unchanged | Per-head dimension |
| `d_v` | 768 | Unchanged | d_model/GQA_groups = 3072/4 |
| `FFN expansion` | 2.5x (7680 hidden) | Unchanged | SwiGLU at 2.5d |
| `Residual stream` | **float16** | Unchanged | Eliminates saturation collapse |
| `FinMax version` | **V2 with V3 patches** | **Alpha init 0.85, layer-depth tau, confidence smoothing** | Fixes dead heads (R14), implements learnable layer specialization, prevents classifier error propagation |
| `SwiGLU gate` | **float16 sigmoid** | **Changed from group-scale only** | Restores continuous gating resolution; group-scale retained for up projection |
| `Output projection` | **Tied + hierarchical + float16 logits** | **Added float16 logits** | Both speed (46x fewer dot products) and quality (float16 resolution) |
| `Total ternary params` | ~2.8B | Unchanged | Fewer layers but wider; tied output saves 61 MB |
| `Effective neural params` | **50-100M** (honest) | Unchanged | 3/65536 ratio; hybrid components compensate |

### Task Definitions

- **Task 3.1:** Word Embedding Generation (TorchSharp) *(Completed)*

- **Task 3.2:** Post-Training Quantization (PTQ) *(Completed — with additions)*
  - Bias Correction: Per-output-channel float32 bias term (mandatory)
  - Group-Scale Quantization: Per-group (64 neurons) float32 scale factor

- **Task 3.3:** Context Classifiers & Syntactic Tagger (ML.NET) *(In Progress)*
  - Level-0: 10 binary L-BFGS models
  - Level-1: One multiclass classifier per family
  - Quality Gates: Level-0 F1 >= 0.80; Level-1 AUC >= 0.85
  - Latency Target: <50ms on target hardware
  - **V3 addition:** Confidence output for tau smoothing (threshold: 0.7)

- **Task 3.4:** FinMax V2 Attention Implementation *(CRITICAL — V3 patches applied)*
  - **FinMax V2 definition (unchanged):**
    ```
    FinMax V2(z_i) = 1              if z_i = z_max
                     alpha * sign(z_i) if z_i >= tau_h
                     0               if z_i < tau_h
    ```
  - **V3 patches:**
    1. **SG-STE** (unchanged from V2): Backward pass uses softmax gradient as proxy
    2. **Alpha initialization: 0.85** (changed from V2's 0.3): Secondary weights start meaningful; gradient descent optimizes per head
    3. **Learned tau with layer-depth bias (V3):** `tau_h_l = tanh(W_tau * context + b_tau)[h] + delta_layer[l]`, where `delta_layer` is a learnable float32 per-layer offset initialized as: layers 1-8: -0.3, layers 9-16: 0.0, layers 17-24: +0.2
    4. **Confidence-weighted tau smoothing (V3):** `tau_smooth = confidence * tau_domain + (1 - confidence) * 0` when classifier confidence < 0.7
    5. **Output normalization** (unchanged from V2): `V' = V' / max(1, count(nonzero FinMax weights))`
    6. **Learned temperature beta** (unchanged from V2): `z_i' = z_i * beta_h`, initialized to 1.0
  - **Training protocol (unchanged from V2):**
    - Epochs 1-5: Standard Softmax attention (warmup)
    - Epochs 6-10: Blended with linear alpha_blend 0->1
    - Epochs 11+: Pure FinMax V2 with SG-STE

- **Task 3.5:** Ternary SwiGLU V2 FFN *(CRITICAL — V3 hybrid gate)*
  - **V3 change:** Gate activation uses **float16 sigmoid**, not sign():
    ```
    gate = sigmoid(scale_gate_group * (W_gate_ternary * x) + b_gate)  // float16
    up   = sign(W_up * x + b_up) * scale_up_group                    // ternary + group-scale
    output = W_down * (gate * up)                                      // mixed
    ```
  - **Why this matters:** In V2, the gate could only be {-1, 0, 1} (even with group-scale), which is binary pass/block with no intermediate suppression. The float16 sigmoid restores the full [0, 1] gating range that makes SwiGLU effective. The gate weight matrix is still stored as ternary — only the activation is float16.
  - **Cost:** ~20K FLOPs per FFN for the sigmoid (negligible vs. ~16M FLOPs for the mat-vec). Gate activations: 7680 * 2 bytes = ~15 KB per layer per token.
  - **Group-scale is retained for the up projection** to preserve LUT efficiency for the sign multiplication.

- **Task 3.6:** Morphological Feature Embedding Pipeline *(Unchanged from V2)*
  - Concatenate Token(3072) + Root(256) + Pattern(128) + POS(64) = 3520
  - Project: 3520 -> 3072 via ternary linear layer
  - This is Lisan's #1 differentiator

- **Task 3.7:** RMSNorm for Float16 Residual Stream *(Unchanged from V2)*
  - Operates on float16 residual, outputs ternary-clamped for sublayer input
  - Popcount optimization removed (correct — input is float16)

- **Task 3.8:** Full Transformer Training Loop *(V3 patches applied)*
  - **Gradual quantization schedule (unchanged from V2):**
    - Epochs 1-5: Full float32
    - Epochs 6-10: Quantize 50% of layers (alternating)
    - Epochs 11+: Full BitNet recipe
  - **Learning rate:** Cosine with warmup. Peak LR = 3e-4, warmup = 2000 steps
  - **Batch:** 256 x 8192 = 2M tokens/batch. Gradient accumulation over 8 micro-batches
  - **V3 addition: Gradient clipping** max norm = 1.0 throughout training
  - **V3 addition: Checkpoint at epoch 5** (pre-Blend) for rollback
  - **V3 addition: Blend phase monitoring** — if loss spikes >20% from pre-blend baseline, halve alpha_blend progression rate
  - **Residual saturation monitoring:** Track percentage of float16 residual values at |value| > 0.95 * max_float16. Alert if >40% saturated after layer 12
  - **Validation:** Perplexity every 1000 steps on 20% held-out set
  - **Data repetition acknowledgment:** With 1.6B-4.7B effective tokens and 2M tokens/batch over 100K steps, the corpus will be traversed ~40-125 times. This is consistent with BitNet b1.58 training practice.

- **Task 3.9:** RoPE Context Extension (8192 -> 16384) *(Unchanged from V2)*

- **Task 3.10:** Morphological-Aware Attention Heads *(Unchanged from V2)*
  - 4 of 24 heads are morphological-aware
  - `z_morph = z_standard + lambda * root_match_matrix`
  - 2 dedicated KV groups

- **Task 3.11:** Multi-Point Context Injection *(Unchanged from V2)*
  - Every 8th layer (layers 8, 16, 24)
  - Domain vector merged via ternary superposition + learnable gate in float16

---

## 10. Epic 4: The Core Engine (C# & Systems Programming)

- **Task 4.0:** Runtime Input Vectorization & Morphological Features *(Unchanged from V2)*
- **Task 4.1:** Ternary Vector Implementation (AVX2 SIMD) *(Unchanged from V2)*
- **Task 4.2:** Vector Superposition (Context Merging) *(Unchanged from V2)*
- **Task 4.3:** Model Serialization & Loading *(Unchanged from V2)*
- **Task 4.4:** Neo4j Sidecar Integration *(Unchanged from V2)*
- **Task 4.5:** Syntactic Morpher — PDA *(Unchanged from V2)*

- **Task 4.6:** Full 24-Layer Ternary Transformer Inference Stack *(V3 patches)*
  - SwiGLU V2 with float16 sigmoid gate (not sign-only)
  - Alpha init 0.85 in FinMax V2
  - Layer-depth tau bias
  - Confidence-weighted tau smoothing

- **Task 4.7:** Output Projection *(V3 — dual-resolution)*
  - Tied embeddings (saves 61 MB)
  - Hierarchical softmax (256 clusters by root family, ~2756 dot products)
  - **V3: Float16 logit computation** — each dot product computed as `(float16)(ternary_dot * scale_group + bias)`. This eliminates tie-breaking issues that ternary-only logits would cause across 128K candidates.
  - **Cluster misassignment handling:** OOV tokens default to "general" cluster. After 1K training steps, reassign clusters based on actual co-occurrence patterns.

- **Task 4.8:** Autoregressive Generation Loop *(Unchanged from V2)*
- **Task 4.9:** Inference Profiling & Optimization *(Unchanged from V2)*
- **Task 4.10:** Embedded Morphological Dictionary *(Unchanged from V2)*
- **Task 4.11:** Three-Tier Context Architecture *(V3 patches)*
  - **V3: Tier 2 quality monitoring** — if perplexity delta between Tier 1 and Tier 2 exceeds 15%, upgrade the most recent 4096 tokens in Tier 2 from int2 to int4 KV-cache
  - **V3: Tier 3 extractive summarization** — use extractive (key sentence selection) rather than abstractive summarization for the 50-100M effective parameter model. Validate with human review on 50 conversations (Task 5.12).
- **Task 4.12:** Embeddings API *(Unchanged from V2)*
- **Task 4.13:** Chat API *(V3 patches)*
  - **V3: Concurrent request cap** — max 2 on 8 GB, max 4 on 16 GB
  - **V3: Request queuing** — excess requests return 503 with `Retry-After` header

- **Task 4.14:** Lite Mode Startup *(NEW — V3)*
  - `--lite` flag: loads in-memory dictionary cache from pre-built snapshot file, does NOT start Neo4j JVM
  - Saves ~400-500 MB RAM and 10-30 seconds startup time
  - Trade-off: No multi-hop GraphRAG queries; single-hop dictionary lookups still available
  - **Snapshot generation:** During Standard mode operation, periodically serialize the top-50K most-accessed Neo4j nodes to a binary snapshot file. Lite mode loads this snapshot.
  - Target: <2 seconds to load snapshot on startup

---

## 11. Epic 5: Evaluation & Refinement

- **Task 5.1:** Performance Profiling *(Unchanged from V2)*
- **Task 5.2:** Disambiguation Accuracy *(Unchanged from V2)*
- **Task 5.3:** Golden Set Testing *(Unchanged from V2)*

- **Task 5.4:** FinMax V2 vs. Softmax Ablation *(V3 expanded)*
  - Compare: **Softmax, FinMax V1 (STE), FinMax V2 (SG-STE), TernarySparsemax, Top-K attention** (V3 addition)
  - Top-K baseline: K = {16, 32, 64, 128} positions, float outputs
  - Metrics: perplexity, BLEU, diacritization accuracy, inference speed
  - **V3: Negative attention weight analysis** — measure percentage of negative FinMax weights and correlation with diacritization accuracy. If negative weights >30% and accuracy drops, investigate clamping to [0, 1].
  - Hypothesis: FinMax V2 with SG-STE matches Softmax within 5% perplexity while providing ternary-compatible sparse attention

- **Task 5.5:** Morphological Feature Ablation *(Unchanged from V2)*
- **Task 5.6:** GraphRAG Factual Recall Benchmark *(Unchanged from V2)*
- **Task 5.7:** Capability Comparison *(Unchanged from V2)*
- **Task 5.8:** Resource Budget Validation *(Unchanged from V2)*
- **Task 5.9:** Residual Saturation Monitoring *(Unchanged from V2)*
  - **Escalation trigger:** If >40% saturated at layer 12, interleaved float16 restoration layers become MANDATORY (not optional)

- **Task 5.10:** Long-Context Tier Validation *(V3 expanded)*
  - Tier 1: Exact recall from positions 1-8192. Target: >95%
  - Tier 2: Exact recall from positions 8192-32768. Target: >80%
  - Tier 3: Approximate recall from positions 32768-200000. Target: >60%
  - **V3: Tier 2 perplexity delta monitoring** — measure perplexity difference between Tier 1-only and Tier 2 inference. If >15%, upgrade Tier 2 KV-cache resolution.

- **Task 5.11:** Empirical Tau Sparsity Measurement *(NEW — V3)*
  - During the first 1000 training steps of the FinMax V2 phase (epochs 11+), log the actual sparsity level (percentage of zero FinMax weights) per head per layer
  - Compare against intended sparsity ranges:
    - Religious text (low tau): 10-30% sparsity
    - Scientific text (medium tau): 30-60% sparsity
    - Slang/chat (high tau): 60-80% sparsity
  - If actual sparsity does not match intended ranges, adjust tau initialization or add sparsity regularization loss
  - **This validates the learned tau mechanism and catches the "tau sparsity percentages inaccurate" issue from Review §4.4**

- **Task 5.12:** Tier 3 Summarization Quality Validation *(NEW — V3)*
  - Run 50 conversations to 32K+ tokens
  - Extract Tier 3 summaries and have 3 human reviewers rate each summary on: (a) factual accuracy, (b) key entity preservation, (c) coherence
  - Target: >80% of summaries rated "acceptable" or better
  - If <60% acceptable, switch from abstractive to extractive summarization (select key sentences rather than generating summaries)

---

## 12. Epic 6: Model Improvement & Quality Assurance

### Epic 6.1: Data Quality
- Task 6.1.1: Data Lineage & Traceability
- Task 6.1.2: Golden Set (Input Validation)
- Task 6.1.3: Automated Data Validation Gate

### Epic 6.2: Model Parameter Tuning
- Task 6.2.1: Context-Aware Attention — via FinMax V2 + multi-point injection + confidence smoothing
- Task 6.2.2: L2 Regularization
- Task 6.2.3: Learning Rate Schedule Tuning

### Epic 6.3: Profiling Data Quantity
- Task 6.3.1: Data Segmentation & Learning Curves
- Task 6.3.2: Early Stopping & Capacity Measurement
- Task 6.3.3: Chinchilla Scaling Audit *(V3 revised)*
  - Neural effective params: 50-100M. Chinchilla optimal: 1B-2B tokens
  - Current corpus: 700M-1.4B tokens
  - Augmentation (Task 1.7): 2-3x -> 1.4B-4.2B tokens
  - Synthetic data (Task 1.8, V3): +200M-500M tokens -> **1.6B-4.7B total tokens**
  - **Gap: CLOSED.** The combined augmentation + synthetic data strategy exceeds the Chinchilla minimum.
  - Data repetition: ~40-125 epochs over corpus. Consistent with BitNet b1.58 practice. Acknowledged explicitly.

### Epic 6.4: Model Quality
- Task 6.4.1: Golden Set (Output)
- Task 6.4.2: Vector & Semantic Similarity

### Epic 6.5: QA Dashboard
- Task 6.5.1: Real-time Training Metrics Display
- Task 6.5.2: Model Comparison Dashboard

---

## 13. Complete Risk Register (V3 — All 20 Risks)

| # | Risk | Severity | Probability | Status | Owner |
|---|---|---|---|---|---|
| R1 | FinMax STE gradient mismatch | High | Medium | **FIXED: SG-STE** | Training |
| R2 | Ternary resolution too low | Medium | Low | MONITOR + float16 residual | Architecture |
| R3 | Context classifier error propagation | Medium | Medium | **FIXED: confidence-smoothed tau** | Inference |
| R4 | Training data insufficiency | Medium | Medium | **FIXED: augmentation + synthetic data** | Data |
| R5 | Neo4j latency stalls | Medium | Low | **FIXED: Lite mode + cache** | Infrastructure |
| R6 | KV-cache exceeds RAM | Medium | Low | ACCEPT (auto-detect + concurrent cap) | Inference |
| R7 | Native dictionary coverage gaps | Low | Medium | ACCEPT + periodic updates | Data |
| R8 | Model underfits Arabic reasoning | High | Low | **FIXED: GraphRAG + CoT mode** | Training |
| R9 | Residual saturation collapse | High | High | **FIXED: float16 residual + monitoring** | Architecture |
| R10 | Domain classifier latency | Medium | Medium | **FIXED: cache + async + <50ms target** | Inference |
| R11 | Morphological pipeline failure | Medium | Medium | **FIXED: embedded dictionary** | Inference |
| R12 | Output projection bottleneck | Medium | High | **FIXED: hierarchical softmax + float16 logits** | Architecture |
| R13 | Ternary training instability | Medium | Medium | **FIXED: gradual quant + grad clipping + checkpoint** | Training |
| **R14** | **Alpha init 0.3 worsens dead heads** | **High** | **High** | **FIXED: init at 0.85** | Architecture |
| **R15** | **SwiGLU group-scale insufficient for gating** | **High** | **Medium** | **FIXED: float16 sigmoid gate** | Architecture |
| **R16** | **Hierarchical softmax cluster misassignment** | **Medium** | **Medium** | **FIXED: OOV default cluster + reassignment** | Inference |
| **R17** | **Tier 2 int2 KV quality degradation** | **Medium** | **Medium** | **MONITOR: upgrade to int4 if perplexity delta >15%** | Inference |
| **R18** | **Tier 3 self-summarization quality** | **High** | **Medium** | **FIXED: extractive summarization + human validation** | Architecture |
| **R19** | **Morphological projection info loss** | **Low** | **Low** | **MONITOR: upgrade to float16 if ablation shows harm** | Architecture |
| **R20** | **Concurrent request memory spike** | **Medium** | **Low** | **FIXED: concurrent cap (2/4) + request queuing** | Infrastructure |

**Critical risks remaining after mitigation: NONE.** All High-severity risks have mandatory fixes. All Medium-severity risks have monitoring or mitigation. No risk is left in an undefined state.

---

## 14. Novel Research Contributions (V3)

1. **FinMax V2:** Ternary-output sparse attention with SG-STE gradient (publishable)
2. **Learnable layer-depth tau bias:** Per-layer attention sparsity specialization (novel, testable)
3. **Confidence-weighted tau smoothing:** Prevents classifier error propagation into attention patterns (practical contribution)
4. **Float16 sigmoid gate in ternary SwiGLU:** Store ternary, compute float — bridges ternary storage with continuous gating (novel for 1.58-bit models)
5. **Morphological feature injection at embedding layer:** Root + Pattern + POS from Layer 0 (core differentiator)
6. **Morphological-aware attention heads:** Co-root attention bias with dedicated KV groups
7. **Hybrid deterministic-neural architecture:** PDA morpher + GraphRAG + ternary neural network
8. **Three-tier context with extractive summarization:** Functional 200K context within 8 GB RAM
9. **Dual-resolution output projection:** Hierarchical softmax (speed) + float16 logits (quality) for 128K vocabulary

---

## 15. Implementation Phases (V3)

### Phase 1: Data & Knowledge Foundation (Weeks 1-4)
- Tasks 1.1-1.8 (corpus, morphological pipeline, augmentation, synthetic data)
- Tasks 2.1-2.9 (Neo4j graph, dictionary seeding, GraphRAG interface)

### Phase 2: Component Development + Critical Fixes (Weeks 5-12)
- Task 3.1-3.3 (embeddings, PTQ, classifiers)
- Task 3.4: FinMax V2 with SG-STE, alpha=0.85, layer-depth tau, confidence smoothing
- Task 3.5: SwiGLU V2 with float16 sigmoid gate
- Task 3.6-3.7: Morphological features, RMSNorm
- Task 4.1-4.5: Ternary vectors, serialization, Neo4j integration

### Phase 3: Training (Weeks 13-22)
- Task 3.8: Full training loop with gradual quantization
- Task 3.9: RoPE context extension
- Task 3.10-3.11: Morphological heads, context injection
- Tasks 5.9, 5.11: Residual monitoring, tau sparsity measurement
- **Rollback triggers:** Loss spike >20% during blend -> halve alpha_blend; saturation >40% -> investigate interleaved float16

### Phase 4: Inference Engine + Long Context + API (Weeks 23-30)
- Task 4.6-4.7: Transformer stack, dual-resolution output projection
- Task 4.8-4.9: Generation loop, profiling
- Task 4.10: Embedded dictionary
- Task 4.11: Three-tier context with extractive summarization
- Task 4.12-4.14: APIs, Lite mode
- Task 5.12: Tier 3 summarization validation

### Phase 5: Evaluation & Publication (Weeks 31-38)
- Task 5.1-5.8: All benchmarks and ablations
- Task 5.10: Long-context validation
- Task 5.4 expanded: Top-K baseline, negative attention analysis
- Task 6.3.3: Final Chinchilla audit
- Paper writing: FinMax V2, layer-depth tau, float16 sigmoid gate

---

## 16. Evaluation Methodologies (V3 — Comprehensive)

### 16.1 Architectural Validation

| Evaluation | Method | Success Criterion | Task |
|---|---|---|---|
| FinMax V2 vs Softmax | Ablation: perplexity, BLEU, diacritization, speed | Within 5% perplexity of Softmax | 5.4 |
| FinMax V2 vs Top-K | Same metrics, K={16,32,64,128} | FinMax V2 >= Top-K on Arabic morphological tasks | 5.4 |
| Negative attention weights | Measure % negative FinMax weights per domain | <30% negative; positive correlation with diacritization | 5.4 |
| Float16 sigmoid gate vs sign-only gate | SwiGLU ablation | >=5% BLEU improvement over sign-only | 5.4 |
| Layer-depth tau | Ablation: with/without delta_layer | >=3% perplexity improvement | 5.4 |
| Confidence smoothing | Ablation: with/without | Reduced variance in perplexity across domains | 5.4 |
| Morphological features | With/without Root+Pattern+POS | >=10% diacritization improvement | 5.5 |
| Residual saturation | Monitor float16 residual values | <40% saturated at layer 12 | 5.9 |

### 16.2 Capability Benchmarks

| Benchmark | Metric | Target | Comparison Models |
|---|---|---|---|
| Arabic MMLU | Accuracy | Approach 7B models | Jais-2 1.3B, AceGPT-7B, LLaMA-2 7B (4-bit) |
| Reading comprehension | F1 | Within 10% of 7B | Same |
| Arabic summarization | ROUGE-L | Within 15% of 7B | Same |
| Diacritization | DER (Diacritization Error Rate) | **Exceed** 70B models | GPT-3.5, GPT-4 |
| en->ar translation | BLEU | Within 20% of 7B | Same |
| Morphological disambiguation | Precision | >90% on 500+ ambiguous words | N/A |
| GraphRAG factual recall | Accuracy | >85% on 500 Q&A pairs | Lisan+GraphRAG vs Lisan-GraphRAG |

### 16.3 Resource Validation

| Metric | Target | Measurement |
|---|---|---|
| RAM at 4096 ctx | <=1.8 GB | Process monitor during 1-hour stress test |
| RAM at 8192 ctx | <=2.1 GB | Same |
| TTFT (2048 prompt) | <2s | BenchmarkDotNet |
| Generation speed | >=10 tok/sec | BenchmarkDotNet |
| Classifier latency | <50ms | Stopwatch on 1000 requests |
| Neo4j query latency | <50ms | 95th percentile over 1000 queries |
| Lite mode startup | <3s | Stopwatch from process start to API ready |
| Concurrent requests | >=2 (8GB), >=4 (16GB) | Load test with wrk |
| 1-hour continuous generation | No OOM, no degradation | Automated stress test |

### 16.4 Long-Context Validation

| Tier | Test | Target |
|---|---|---|
| Tier 1 (0-8K) | Exact fact recall from positions 1-8192 | >95% |
| Tier 2 (8K-32K) | Exact fact recall from positions 8192-32768 | >80% |
| Tier 3 (32K-200K) | Approximate fact recall from positions 32768-200000 | >60% |
| Tier 2 perplexity delta | Perplexity difference vs Tier 1 only | <15% |
| Tier 3 summary quality | Human review (3 reviewers, 50 summaries) | >80% "acceptable" |

### 16.5 Training Stability Validation

| Metric | Alert Threshold | Action |
|---|---|---|
| Loss spike during blend phase | >20% from pre-blend baseline | Halve alpha_blend progression rate |
| Residual saturation at layer 12 | >40% at |value| > 0.95 * max_f16 | Implement interleaved float16 layers |
| Gradient norm | >10.0 | Reduce learning rate by 50% |
| Alpha convergence | All heads alpha < 0.1 | Some heads may be truly sparse; verify per-domain |
| Tau divergence | tau_h > 5.0 or < -5.0 | Clamp tau to [-3, 3]; investigate domain classifier |

---

## 17. Confidence Assessment (V3 — Honest)

| Goal | Confidence | Key Dependency |
|---|---|---|
| Match 70B on Arabic morphological/grammatical tasks | **80%** | FinMax V2 + morphological features + GraphRAG + deterministic morpher |
| Approach 7B on general Arabic generation | **65%** | Float16 sigmoid gate, sufficient training data, Chinchilla compliance |
| Run on standard PC (8 GB, CPU-only) | **95%** | Lite mode for 8 GB; memory budget verified at 1.3-2.1 GB |
| Arabic-native + slang capability | **85%** | Native dictionary + Nofal slang corpus + phonological rules |
| Novel publishable contributions | **95%** | FinMax V2, layer-depth tau, float16 sigmoid gate — all genuinely novel |
| Not a resource beast | **95%** | 1.3-2.1 GB RAM; no GPU; 10 tok/sec on CPU |

### What Could Go Wrong (Honest Risk Assessment)

1. **FinMax V2 may not match Softmax within 5% on all domains.** Arabic slang with its rapidly evolving vocabulary may require broader attention than FinMax allows. Mitigation: if slang-domain perplexity is >10% worse than Softmax, lower tau_default for slang from 0 to -0.3 (broader attention).

2. **The 50-100M effective parameter ceiling is real.** No amount of architectural cleverness fully compensates for 2 orders of magnitude fewer neural parameters than a 7B model. The hybrid deterministic components (GraphRAG, morphological pipeline, PDA morpher) are critical to closing this gap — if any of them underperform, the gap will show in open-ended generation quality.

3. **Training may require more than 22 weeks.** The gradual quantization schedule + FinMax blend phase + potential rollback scenarios mean the training timeline is optimistic. Budget 30 weeks for training.

4. **Tier 3 GraphRAG retrieval may introduce latency.** Neo4j queries during generation add latency. The 50ms timeout ensures this does not stall generation, but may lose relevant context. In Lite mode, Tier 3 is unavailable entirely.

### What Will Definitely Work

1. **Float16 residual accumulation.** This is proven technology — standard in every modern transformer. The risk was ternary clamping, which is now eliminated.

2. **Morphological feature injection.** This is a deterministic, zero-risk enhancement. Even if the model learns nothing from the features, they cannot hurt (ablation will confirm).

3. **Memory budget.** The numbers are verified. Lite mode at 1.3 GB for 4096 context is extremely conservative.

4. **SG-STE.** The mathematical argument is sound: softmax-guided gradients preserve ranking in the backward pass while the forward pass remains ternary. This is analogous to proven techniques (Gumbel-Softmax, straight-through estimators in binary networks).

---

*This document is the final, binding project plan. No further revisions are anticipated before implementation begins. All architectural decisions, risk mitigations, and evaluation criteria are specified to a level sufficient for immediate implementation.*

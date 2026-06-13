# Lisan-Bit: Single Source of Truth Project Plan

**Date:** 2026-06-14
**Status:** Locked Execution Baseline (v3 — Restructured Dependencies + Complete Data Sources)

---

## 1. Product Definition

Lisan-Bit is an Arabic Linguistic Intelligence system designed for morphology-aware retrieval, grammatical analysis, diacritization, lexical reasoning, and dialect-aware support. It runs on standard consumer hardware while preserving a stronger Arabic-specific linguistic stack than a generic assistant.

The project combines four core layers:

- A root-centric Neo4j knowledge graph for deterministic linguistic structure
- A native Arabic NLP layer in .NET for normalization, morphology, syntax, and dialect handling
- A retrieval-first runtime that grounds answers in Quran, Hadith, Tafsir, dictionaries, and curated corpora
- A small language model trained in a standard FP16 path and then progressively quantized for efficient deployment

The primary delivery path is RAG-first, standard-transformer-first, and quantization-gated. Ternary-from-scratch research remains conditional and is pursued only if post-training compression fails the required quality thresholds.

**Primary capabilities:**

- Root extraction, pattern identification, lemma recovery, and POS-aware analysis
- Grammatical parsing and case-aware reasoning (إعراب)
- Diacritization with deterministic plus neural refinement
- Lexical and etymological explanation
- Quranic, Hadith, Tafsir, and dictionary-grounded retrieval
- Dialect-aware handling with deep Egyptian Arabic support: etymological root mapping, morphological reanalysis, syntactic reordering, and dialect-matched response generation

**Primary non-goals:**

- Competing directly with frontier LLMs on broad general chat
- Code generation or programming assistance
- Unrestricted freeform religious authority without grounding and citation

**Target deployment profile:**

- Minimum: 4 GB RAM, CPU-only, 4K context (Lite mode with 2-bit ternary model)
- Recommended: 8 GB RAM, CPU-only, 8K context (Standard mode with INT4 model)
- Extended: 16 GB RAM, CPU or GPU, 32K context (Full mode with INT4 model + INT8 KV cache)

---

## 2. Strategic Principles

1. **RAG-first delivery.** Retrieval, graph reasoning, templates, and NLP utilities ship before model training completes. The product is useful from Day 1 without the neural model.
2. **Standard transformer first.** Train FP16, then progressively quantize. Ternary-from-scratch is conditional only.
3. **Morphological feature injection is baseline**, not optional.
4. **Quantization is gated by measured quality.** No shipping without passing evaluation gates.
5. **Deterministic systems own rules and structure; the neural model handles ambiguity and fluency.**
6. **Runtime degrades gracefully** when components are unavailable.
7. **.NET-first technology stack.** Use .NET/C# for everything feasible. Use Python only where no .NET alternative exists. The only required Python components are: (a) 458M model training, (b) one-time embedding model fine-tuning, and (c) one-time dialect alignment model training. Everything else runs in .NET.
8. **All parameter counts and memory budgets verified from first principles.**
9. **Training-inference boundary:** Training uses PyTorch (Python); inference uses ONNX Runtime (.NET). TorchSharp is the .NET-native model tool for architecture definition, validation, and small model training. The 458M model training uses PyTorch because TorchSharp lacks AMP, gradient checkpointing, CPU optimizer offloading, and flash attention — all required to fit 458M on the T1000 4GB GPU.
10. **Training orchestration is .NET-driven** via sidecar pattern: .NET prepares data, launches Python training, monitors progress, and validates results.
11. **SSE streaming is the primary chat interface.** The `/v1/chat/completions` endpoint defaults to Server-Sent Events streaming with OpenAI-compatible chunk format. Blocking mode remains available as fallback.
12. **Dialect knowledge is trained, scraped, and AI-generated — never manually curated.** The Egyptian dialect system builds its etymological root maps, morphological reanalysis patterns, and syntactic reordering rules from parallel corpora (MADAR, ARB-EGY-CMP), dialect scraping (Egyptian social media, forums, subtitles), and teacher model generation (Jais/cloud AI). No manual dictionary entry is required for dialect support. The system learns dialect→MSA mappings from data.
13. **Implementation follows the dependency chain.** Each epic's gate must be passed before the next begins. No parallelization of dependent steps. See Section 19 for the full dependency graph.

---

## 3. Delivery Snapshot

Operational milestones already completed in the current repository:

- Aspire orchestration is active for DataPipeline, Dashboard, farasa-api, conceptnet-importer, and Neo4j.
- SQLite pipeline persistence and queue-driven crawling are operational.
- Dashboard live monitoring is wired with SignalR push and polling fallback.
- Nofal slang local source flow is integrated.
- ARB-EGY-CMP local source ingestion is integrated with download, unzip, fix, and load readiness gating.
- Local corpus preparation is concurrency-protected to avoid duplicate prep and file-lock conflicts.
- Farasa preprocessing endpoint routing has been hardened to absolute or service-discovery resolution.
- Strict NLP quality mode is available to prevent fallback morphology writes when Farasa is unavailable.
- Neo4j container orchestration is configured in Aspire.
- Real-time Neo4j seeding from preprocessing is in place for word-root derivations and context weights.
- ConceptNet Arabic semantic edge streaming and bulk insertion are integrated through the ConceptNet importer service.
- Unique constraints exist on `:Word(text)`, `:Root(text)`, and `:Context(name)`.
- Historical migrations were consolidated into a single `InitialCreate` baseline.
- LisanBits.Trainer compile issues around TorchSharp namespaces and weight initialization were corrected.
- LisanBits.GrammarPipeline is integrated with Neo4j and the dashboard for treebank ingestion, rule mining, and grammatical tagger training.
- Obsolete project references were removed from the solution.
- DbProbe remains in audit-focused mode for verification tasks.

**Operational notes:**

- Keep `FarasaApi:AllowFallback=false` for quality-first runs.
- Keep local corpus sources active; readiness gating prevents premature queue processing.
- Use DbProbe audit mode after major ingest runs.
- `ContextVector` stores English taxonomy leaf paths only.

---

## 4. Target Architecture

### 4.1 End-to-End Runtime Pipeline

```
User Input
    |
    v
[1] Normalize Arabic Text (Lisan.Tokenizer — C#)
    |
    v
[2] Morphological Analysis (Lisan.Morphology — C#) -> roots, patterns, lemmas, POS, affixes
    |
    v
[3] Dialect Detection + Dialect Reconstruction (Lisan.Dialect — C# + ONNX)
    |   ├── Detection: CNN → dialect label + confidence
    |   ├── Etymological Root Lookup: trained alignment map (SQLite)
    |   ├── Morphological Reanalysis: pattern-based root derivation for dialect words
    |   └── Syntactic Reordering: learned rewrite rules → MSA reconstruction
    |
    v
[4] Domain / Intent Classification (C#)
    |
    v
[5] Graph Retrieval (Neo4j — C# driver)  ----+---- [6] Vector Retrieval (FaissNet/HNSW.Net — C#)
    |   (dual-root: dialect etym. + MSA)       |    (dual-query: original + reconstructed)
    +------------------------------------------+
    |
    v
[7] Context Assembly (C# — tier-aware, deduplicated, priority-ordered, dialect-annotated)
    |
    v
[8] Model Inference with Morphological Feature Injection (ONNX Runtime — C#)
    |   SSE streaming: token-by-token generation → immediate flush
    |
    v
[9] Post-Processing: syntactic constraints, diacritization, dialect adaptation, reassembly (C#)
    |
    v
SSE Stream → Client
```

The entire runtime pipeline is .NET/C#. The only non-.NET component at inference time is the ONNX model file itself (generated from PyTorch training).

### 4.2 Dialect Reconstruction Pipeline

When dialect is detected (step 3), a secondary pipeline reconstructs the MSA equivalent for retrieval and model context:

```
"عايز ارجع المنتج ده"
       │
       ▼
Step 3a: Dialect Detection
  CNN (5M params, ONNX) → Egyptian (94%)

Step 3b: Per-Token Etymological Analysis
  عايز → MSA dict FAIL → Etym map HIT: root ع-و-ز, pattern فاعل
         → MSA equivalent: أريد (root أ-ر-د) / أحتاج (root ح-و-ج)
  ارجع → MSA dict HIT: root ر-ج-ع, Form IV
  المنتج → MSA dict HIT: root ن-ت-ج, passive participle
  ده → MSA dict FAIL → Etym map HIT: demonstrative ← reduction of "هذا"
         → Syntactic tag: POST-posed demonstrative

Step 3c: Syntactic Reordering
  "المنتج ده" → "هذا المنتج" (demonstrative repositioned: POST → PRE)

Step 3d: MSA Reconstruction
  "أريد إرجاع هذا المنتج"

Step 3e: Dual-context for model:
  [DIALECT: egyptian] عايز←عوز→أريد | ده←هذا(reorder) [/DIALECT]
  [MSA-RECON] أريد إرجاع هذا المنتج [/MSA-RECON]
```

All etymological mappings and reordering rules in this pipeline are **derived from trained models and parallel corpora**, not manually curated dictionaries.

### 4.3 Graceful Degradation Rules

| Failure Point | Behavior |
|---|---|
| Normalization fails | Continue with original text; log warning |
| Morphology is partial | Continue with available tokens; inject zero vectors for missing features |
| Dialect detection fails | Treat as MSA; skip reconstruction |
| Etymological map misses a word | Continue with zero-vector for that token's dialect features |
| Syntactic reordering fails | Use original word order; may reduce retrieval quality |
| Graph retrieval unavailable | Continue with vector search only |
| Vector retrieval unavailable | Continue with graph retrieval only |
| Both retrieval layers unavailable | Template-only response; do not attempt model inference without context |
| Model unavailable | Template-based response generation using graph + dictionary data |
| Context budget exceeded | Reduce context tier (32K → 16K → 8K → 4K) and retry |
| All systems unavailable | Return error with guidance |

### 4.4 Context Tiering

| Tier | Max Tokens | Strategy | Deduplication | Target Mode |
|---|---:|---|---|---|
| 4K | 4,096 | Direct concatenation with priority ordering | None | Lite |
| 8K | 8,192 | Concatenation + MinHash dedup (Jaccard 0.85) | MinHash | Standard |
| 16K | 16,384 | YARN-style segment selection (512-token segments) | MinHash + segment ranking | Extended |
| 32K | 32,768 | Clause-level TF-IDF ranking + extractive summarization | Full pipeline | Full |

### 4.5 Context Priority Ordering

1. Quranic verse references (if query is religious)
2. Dictionary definitions from graph (direct root/pattern match — includes dialect etymological roots)
3. Graph-neighborhood context (1-2 hop traversal via both dialect and MSA roots)
4. Vector-similar passages (semantic relevance — queries run with both original and reconstructed MSA)
5. Domain-specific background (from taxonomy)

---

## 5. Model Baseline

### 5.1 Primary Model Architecture

| Parameter | Value | Rationale |
|---|---|---|
| d_model | 1,536 | Balance between capacity and memory |
| N_layers | 16 | Sufficient depth for Arabic morphological composition |
| Q heads | 12 | 12 x 128 = 1,536 = d_model |
| KV groups | 4 | GQA 3:1 ratio; reduces KV cache by 3x |
| d_k | 128 | Standard head dimension |
| d_ff | 4,096 | SwiGLU ratio = 8/3 x d_model |
| Vocab | 32,768 | Arabic-optimized BPE (Section 11.2) |
| Attention | Standard softmax | Stable, well-understood |
| FFN | SwiGLU | Proven superior to ReLU/GLU variants |
| Position encoding | RoPE on Q and K | Enables YARN extrapolation to 32K |
| Normalization | RMSNorm (pre-norm) | No bias, fewer parameters, stable |
| Dropout | 0.0 | Standard for production LLMs |
| Training context | 2,048 → 4,096 → 8,192 | Progressive curriculum |
| Inference context | Up to 32,768 | Via YARN position extrapolation |

### 5.2 Morphological Feature Injection

```
h = Concat(TokenEmb(x), RootEmb(r(x)), PatternEmb(p(x)), POSEmb(t(x)))  # dim = 1,984
h = MorphProjection(h)                                                    # dim = 1,536
```

| Component | Input Space | Embedding Dim | Parameters |
|---|---:|---:|---:|
| Token embedding | 32,768 | 1,536 | 50,331,648 |
| Root embedding | 6,000 | 256 | 1,536,000 |
| Pattern embedding | 1,200 | 128 | 153,600 |
| POS embedding | 50 | 64 | 3,200 |
| Morph projection (weight + bias) | 1,984 → 1,536 | — | 3,048,960 |
| **Injection subtotal** | | | **55,073,408** |

When morphological analysis is unavailable (proper nouns, loanwords, unmapped dialect words), zero vectors replace missing features. The projection layer learns to handle this; approximately 15-20% of training tokens have incomplete morphology. Dialect tokens with etymological root mapping receive their dialect root embedding rather than a zero vector, preserving morphological signal.

### 5.3 Parameter Count — Verified From First Principles

**Embedding and projection:**

| Component | Calculation | Parameters |
|---|---|---:|
| Token embedding | 32,768 x 1,536 | 50,331,648 |
| Root embedding | 6,000 x 256 | 1,536,000 |
| Pattern embedding | 1,200 x 128 | 153,600 |
| POS embedding | 50 x 64 | 3,200 |
| Morph projection (weight + bias) | (1,984 x 1,536) + 1,536 | 3,048,960 |
| **Embedding subtotal** | | **55,073,408** |

**Per-layer transformer:**

| Component | Shape | Parameters |
|---|---|---:|
| W_Q | 1,536 x 1,536 | 2,359,296 |
| W_K | 1,536 x 512 | 786,432 |
| W_V | 1,536 x 512 | 786,432 |
| W_O | 1,536 x 1,536 | 2,359,296 |
| RMSNorm (attn) | 1,536 | 1,536 |
| W_gate (SwiGLU) | 1,536 x 4,096 | 6,291,456 |
| W_up (SwiGLU) | 1,536 x 4,096 | 6,291,456 |
| W_down (SwiGLU) | 4,096 x 1,536 | 6,291,456 |
| RMSNorm (FFN) | 1,536 | 1,536 |
| **Per-layer total** | | **25,168,896** |

**16 layers:** 402,702,336
**Final RMSNorm:** 1,536

**Grand total: 457,777,280** (approximately 458M)

### 5.4 Quantization Path

| Stage | Format | Bits/Param | Weight Size | Quality Gate |
|---|---|---:|---:|---|
| 1 | FP16 baseline | 16 | 916 MB | Baseline |
| 2 | INT8 per-channel symmetric | 8 | 458 MB | >= 99% of FP16 |
| 3 | INT4 (GPTQ or AWQ, group_size=128) | 4 | ~229 MB | >= 97% of FP16 |
| 4 | 2-bit packed ternary (PTQ) | 2 | ~128 MB | >= 95% of FP16 |

**Quantization skip list:**

- Always skip: token embedding, morphological embeddings, morph projection, final RMSNorm
- For INT4 and below: also skip W_Q and W_O of layer 0, W_down of layer 15

### 5.5 Conditional Research Path: Ternary-from-Scratch

If PTQ ternary loses > 5% quality AND QoRA recovery fails: activate ternary-from-scratch research with FinMax V2, MASP, MCLAS, AKQ. Does not block primary delivery. Additional 10-16 weeks if activated.

---

## 6. Memory and Deployment Budgets

### 6.1 Model Weights and KV Cache

**KV cache calculation:**

- Per token per layer: 2 (K+V) x n_kv_heads x d_k = 2 x 4 x 128 = 1,024 elements
- Per token total: 16 x 1,024 = 16,384 elements
- FP16: 16,384 x 2 = 32,768 bytes per token
- INT8: 16,384 x 1 = 16,384 bytes per token

| Context | KV FP16 | KV INT8 |
|---:|---:|---:|
| 4,096 | 128 MB | 64 MB |
| 8,192 | 256 MB | 128 MB |
| 16,384 | 512 MB | 256 MB |
| 32,768 | 1,024 MB | 512 MB |

**Complete model memory:**

| Format | Weight | KV 4K | KV 8K | Act | Total 4K | Total 8K |
|---|---:|---:|---:|---:|---:|---:|
| FP16 | 916 MB | 128 MB | 256 MB | 40 MB | ~1,284 MB | ~1,412 MB |
| INT8 | 458 MB | 128 MB | 256 MB | 40 MB | ~826 MB | ~954 MB |
| INT4 | 229 MB | 128 MB | 256 MB | 40 MB | ~597 MB | ~725 MB |
| 2-bit ternary | 128 MB | 128 MB | 256 MB | 40 MB | ~496 MB | ~624 MB |

### 6.2 Full System Modes

| Mode | Context | Model Format | Model+KV+Act | Neo4j | Dict | NLP | Dialect | Embed | Total | Target RAM |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Lite | 4K | 2-bit ternary | 496 MB | 0 | 200 MB | 20 MB | 15 MB | 50 MB | ~781 MB | 4 GB |
| Lite | 4K | INT4 | 597 MB | 0 | 200 MB | 20 MB | 15 MB | 50 MB | ~882 MB | 4 GB |
| Standard | 8K | INT4 | 725 MB | 400 MB | 200 MB | 20 MB | 15 MB | 50 MB | ~1,410 MB | 8 GB |
| Standard | 8K | INT8 | 954 MB | 400 MB | 200 MB | 20 MB | 15 MB | 50 MB | ~1,639 MB | 8 GB |
| Full | 32K | INT4 + INT8 KV | 845 MB | 400 MB | 200 MB | 20 MB | 15 MB | 50 MB | ~1,530 MB | 16 GB |
| Full | 32K | INT4 + FP16 KV | 1,357 MB | 400 MB | 200 MB | 20 MB | 15 MB | 50 MB | ~2,042 MB | 16 GB |

**Dialect memory:** ~15 MB for the etymological root map (SQLite lookup table + ONNX alignment model) + dialect detection CNN (5M params ≈ 10 MB FP16). Total dialect subsystem ≈ 15-25 MB depending on mode.

### 6.3 Training Budget — T1000 4GB + 64GB RAM

**Hardware:** Intel Core i9H, NVIDIA T1000 4GB (Turing, 896 CUDA cores, ~2.5 TFLOPS FP16), 64 GB RAM, Ollama runtime available.

**GPU memory breakdown (PyTorch + DeepSpeed ZeRO-2 + gradient checkpointing + flash attention):**

| Component | Size | Notes |
|---|---:|---|
| FP16 model weights | 916 MB | Stays on GPU |
| Activation checkpoints (every 2 layers) | ~200 MB | Reduced from ~1,600 MB without checkpointing |
| CUDA overhead + temporary buffers | ~150 MB | cuBLAS, cuDNN workspaces |
| **GPU total** | **~1,266 MB** | **Fits within 4 GB with ~2,734 MB headroom** |

**CPU memory breakdown:**

| Component | Size |
|---|---:|
| FP32 master weights | 1,832 MB |
| Adam optimizer m + v states | 3,664 MB |
| Gradients (offloaded from GPU) | 916 MB |
| DataLoader + preprocessing buffers | ~500 MB |
| Ollama teacher model (Q4_K_M ~800 MB) | ~800 MB |
| **CPU total** | **~7,712 MB** (fits in 64 GB) |

### 6.4 KV-Cache Quantization

| Mode | KV Format | Rationale |
|---|---|---|
| Lite (4K) | FP16 | Small cache; quality matters |
| Standard (8K) | FP16 | Fits in 8 GB with INT4 model |
| Full (32K) | INT8 | Required to fit in 16 GB |

---

## 7. Training Infrastructure

### 7.1 Training Framework Decision

The 458M model training is the ONLY component that requires Python/PyTorch.

**Why TorchSharp cannot train 458M on T1000 4GB:**

TorchSharp v0.107.0 does not provide mixed-precision training (AMP), gradient checkpointing, CPU optimizer state offloading, or flash attention. Without these features, the minimum VRAM for 458M FP16 training is approximately 10-12 GB. The T1000 has 4 GB. This is a hardware constraint, not a software preference.

**What TorchSharp CAN and WILL do (maximizing .NET footprint):**

| Component | Size | TorchSharp? | Rationale |
|---|---|---|---|
| Model architecture definition | N/A | Yes | Define in both PyTorch and TorchSharp for dual validation |
| Diacritization neural model | 25M params | Yes | Small enough for TorchSharp on T1000 4GB |
| Dialect detection CNN | 5M params | Yes | Very small; TorchSharp handles easily |
| Grammatical tagger | 10M params | Yes | Small enough |
| 458M primary model training | 458M params | No | Requires AMP + checkpointing + CPU offloading |
| 458M primary model inference | 458M params | Via ONNX Runtime | TorchSharp can load weights for validation; ONNX Runtime is faster for production |

**Training-inference boundary:**

```
TRAINING (Python)                    INFERENCE (.NET)
==================                   ================
PyTorch model definition     --->    TorchSharp model definition (for validation)
PyTorch training loop        --->    ONNX Runtime inference (for production)
torch.onnx.export()          --->    Microsoft.ML.OnnxRuntime
GGUF conversion              --->    Ollama / llama.cpp P/Invoke
```

**Training orchestration via .NET sidecar:**

1. .NET prepares training data (C# pipeline) and writes to shared directory.
2. .NET launches Python training script via `Process.Start()` with configuration.
3. Python trains model, writes checkpoints to shared directory.
4. .NET monitors training progress via checkpoint files and log parsing.
5. .NET validates checkpoints by loading into TorchSharp and running test inferences.
6. .NET exports validated checkpoints to ONNX (via Python script) or loads directly.

### 7.2 Teacher Strategy

**Primary teacher: Jais-1.3B served via Ollama on CPU**

| Teacher | Model | How Served | Memory |
|---|---|---|---|
| Primary | Jais-1.3B (Q4_K_M) | Ollama on CPU | ~800 MB RAM |
| Secondary | Qwen2-1.5B (Q4_K_M) | Ollama on CPU | ~1,000 MB RAM |

**Fallback teacher via cloud API:**

If local teacher quality is insufficient, use free API keys from https://github.com/alistaitsacle/free-llm-api-keys for cloud-based teacher inference (e.g., OpenAI-compatible endpoints). Use this for:

- Final-phase distillation where teacher quality matters most
- Generating high-quality Arabic training data (explanations, grammar analyses)
- Generating dialect etymological mappings and parallel sentence pairs
- Benchmark evaluation assistance

### 7.3 Vocabulary Alignment

- Dual tokenization (Lisan BPE + Jais tokenizer) on 10K Arabic sentences
- Monotonic alignment via dynamic programming
- Target: > 90% coverage
- Fallback: position-ratio interpolation

### 7.4 Context Curriculum

| Step Range | Context | Effective Batch |
|---:|---:|---:|
| 1 - 10K | 2,048 | 8 (gradient accumulation) |
| 10K - 40K | 4,096 | 4 |
| 40K - 70K | 8,192 | 2 |

### 7.5 Optimization Baseline

| Parameter | Value |
|---|---|
| Framework | PyTorch + Accelerate + DeepSpeed ZeRO-2 |
| Precision | Mixed (FP16 forward, FP32 master weights on CPU) |
| CPU offload | Optimizer states, gradients, and master weights |
| Gradient checkpointing | Every 2 layers |
| Flash attention | Enabled (PyTorch 2.0+) |
| Gradient clipping | 1.0 |
| Weight decay | 0.01 |
| Dropout | 0.0 |
| LR warmup | 3e-4 over 2K steps |
| Stable LR | 3e-4 through 40K |
| Cosine decay | 3e-4 → 1e-5 by 60K |
| Fine-tuning LR | 1e-5 through 70K |

### 7.6 Checkpointing and Recovery

- Save every 2,000 steps; keep last 5 + best validation.
- Validate every 2,000 steps.
- On 3 consecutive regressions: rollback, reduce LR by 0.5x.
- On divergence (loss > 10x): rollback, reduce LR by 0.1x.

### 7.7 Training Throughput and Wall-Time — T1000 4GB

| Context | Batch | Grad Accum | Est. Steps/sec | Est. Tokens/sec |
|---|---:|---:|---:|---:|
| 2,048 | 1 | 8 | ~0.18 | ~369 |
| 4,096 | 1 | 4 | ~0.11 | ~450 |
| 8,192 | 1 | 2 | ~0.06 | ~492 |

**Wall-time calculation:**

| Phase | Steps | Steps/sec | Duration |
|---|---:|---:|---:|
| 2K context (1-10K) | 10,000 | 0.18 | ~55 hours |
| 4K context (10K-40K) | 30,000 | 0.11 | ~303 hours |
| 8K context (40K-70K) | 30,000 | 0.06 | ~500 hours |
| **Total** | **70,000** | | **~858 hours (~36 days)** |

**Conservative estimate with interruptions:** Plan for **40-45 days of continuous training**. With the 30-week timeline (210 days), this leaves ample room: training runs from Week 12 to approximately Week 18-19.

**Mitigation if training is too slow:**

- Reduce total steps from 70K to 50K (adjust LR schedule: warmup 2K, stable 30K, decay 40K, finetune 50K). Estimated time: ~25 days.
- Use cloud API teacher for distillation, which produces better soft labels and may allow fewer training steps.
- Use gradient accumulation of 4 even at 2K context (smaller effective batch but faster steps).

### 7.8 Knowledge Distillation

| Phase | Steps | Alpha (KL weight) | Temperature |
|---|---:|---:|---:|
| Pre-training distillation | 1-50K | 0.7 | 4.0 |
| Fine-tuning distillation | 50K-65K | 0.3 | 2.0 |
| No-distillation annealing | 65K-70K | 0.0 | 1.0 |

**Loss function:**

```
L = alpha * KL_div(student_logits/T, teacher_logits/T) + (1-alpha) * CE(student_logits, ground_truth)
```

---

## 8. Data and Knowledge Foundation

### 8.1 Primary Arabic Linguistic Sources — Shamela.ws

Shamela.ws is the **primary source for classical Arabic linguistic texts**. It provides structured, searchable, machine-accessible Arabic text — not PDF/OCR. The following texts are essential for the knowledge graph, morphological analysis, and grammatical parser:

**Dictionaries (Lexical Foundation):**

| Source | Arabic Title | Shamela URL | Role |
|---|---|---|---|
| Al-Qamus Al-Muhit | القاموس المحيط | https://shamela.ws/book/7283 | Classical dictionary; root-based lexical entries |
| Mukhtar Al-Sihah | مختار الصحاح | https://shamela.ws/book/23193 | Compact dictionary; widely used root-based reference |
| Tag Al-Aroos | تاج العروس | https://shamela.ws/book/7030 | Comprehensive dictionary; encyclopedic lexical coverage |
| Al-Waseet | المعجم الوسيط | https://shamela.ws/book/7028 | Modern Arabic dictionary (Arabic Language Academy) |
| Al-Ain | العين | https://shamela.ws/book/1682 | Earliest Arabic dictionary (Al-Khalil ibn Ahmad); phonological/root-based |
| Lisan Al-Arab | لسان العرب | Available via almaany.com | Classical dictionary; most comprehensive classical Arabic lexicon |
| Taimoor Dictionary | معجم تيمور | https://shamela.ws/book/150964 | **Contains dialects and slang**; critical for dialect etymology |

**Grammar References (Syntactic Foundation):**

| Source | Arabic Title | Shamela URL | Role |
|---|---|---|---|
| Alfiyyat Ibn Malik | ألفية ابن مالك | https://shamela.ws/book/356 | 1,002 grammatical rules in verse; primary grammar rule source |
| Qatr Al-Nada | قطر الندى وبل الصدى | https://shamela.ws/book/11376 | Grammar reference by Ibn Hisham; foundational syntax rules |
| Sharh Shudhur Al-Dhahab | شرح شذور الذهب في معرفة كلام العرب | https://shamela.ws/book/6969 | Ibn Hisham's grammar commentary; detailed syntactic analysis |
| Al-Kitab (Sibawayh) | الكتاب | https://shamela.ws/book/23018 | Foundational Arabic grammar; earliest systematic grammar treatise |

### 8.2 Religious Corpus

| Source | Content | Access |
|---|---|---|
| Tanzil | Verified Quran text (Uthmanic script) | https://tanzil.net/download/ |
| Quranic Arabic Corpus | Morphological and syntactic annotations of Quran | https://corpus.quran.com/ |
| Tafsir Al-Tabari | تفسير الطبري | Shamela.ws + scraping |
| Tafsir Ibn Kathir | تفسير ابن كثير | Shamela.ws + scraping |
| Tafsir Al-Jalalayn | تفسير الجلالين | Shamela.ws + scraping |

**Sunnah.com — All Hadith Books (15+ collections):**

| Book | Arabic Name | URL |
|---|---|---|
| Sahih al-Bukhari | صحيح البخاري | https://sunnah.com/bukhari |
| Sahih Muslim | صحيح مسلم | https://sunnah.com/muslim |
| Sunan al-Tirmidhi | سنن الترمذي | https://sunnah.com/tirmidhi |
| Sunan Abu Dawud | سنن أبي داود | https://sunnah.com/abudawud |
| Sunan al-Nasa'i | سنن النسائي | https://sunnah.com/nasai |
| Sunan Ibn Majah | سنن ابن ماجه | https://sunnah.com/ibnmajah |
| Muwatta Malik | موطأ مالك | https://sunnah.com/malik |
| Riyad as-Salihin | رياض الصالحين | https://sunnah.com/riyadussalihin |
| Bulugh al-Maram | بلوغ المرام | https://sunnah.com/bulugh |
| Al-Adab Al-Mufrad | الأدب المفرد | https://sunnah.com/adab |
| Shama'il Muhammadiyya | الشمائل المحمدية | https://sunnah.com/shamail |
| Mishkat al-Masabih | مشكاة المصابيح | https://sunnah.com/mishkat |
| 40 Hadith Nawawi | الأربعون النووية | https://sunnah.com/nawawi40 |
| 40 Hadith Qudsi | الأحاديث القدسية | https://sunnah.com/qudsi40 |
| Hisn al-Muslim | حصن المسلم | https://sunnah.com/hisn |

### 8.3 General Arabic Corpus

| Source | Content | Access |
|---|---|---|
| OSCAR Arabic | Web-crawled Arabic (filtered) | https://oscar-project.org/ |
| Arabic Wikipedia | Encyclopedic + category taxonomy | https://dumps.wikimedia.org/ |
| CC-100 Arabic | Common Crawl filtered | https://data.statmt.org/cc-100/ |
| Hindawi | Arabic literature | https://www.hindawi.org/ |
| OPUS | Parallel corpus | https://opus.nlpl.eu/ |
| MADAR | Dialect corpus (28 cities + MSA) | https://camel.abudhabi.nyu.edu/madar/ |

### 8.4 Dialect-Specific Corpus

The dialect subsystem requires three categories of dialect data, all sourced programmatically — no manual dictionary curation.

**Structured Parallel Corpora:**

| Source | Content | Purpose | Access |
|---|---|---|---|
| MADAR-28 | Parallel sentences: 28 Arabic city dialects ↔ MSA (12K sentences per city) | Training etymological alignment model + syntactic reordering model | https://camel.abudahi.nyu.edu/madar/ |
| ARB-EGY-CMP | Egyptian-MSA comparable/parallel corpus; **contains Twitter comments and tweets** | Egyptian-specific alignment pairs; dialect vocabulary from social media | Already integrated in pipeline |
| Nofal dataset | Egyptian Arabic slang and colloquial expressions; **contains Twitter comments and tweets** | Dialect vocabulary + usage patterns from real social media | Already integrated in pipeline |
| OpenSubtitles (Arabic) | Movie/TV subtitles with dialect mixing | Dialect detection training + colloquial vocabulary | https://opus.nlpl.eu/OpenSubtitles.php |

**Scraped Dialect Data:**

| Source | Content | Purpose | Access |
|---|---|---|---|
| Egyptian social media | Twitter/X, Facebook, Reddit Arabic dialect posts | Real-world dialect usage, slang evolution | Scraping (C# pipeline, with quality gates) |
| Egyptian web forums | MASRrawi, Youm7 comments, Arabic Stack Overflow | Informal Egyptian Arabic patterns | Scraping (C# pipeline) |
| Taimoor Dictionary (Shamela) | معجم تيمور — **contains dialects and slang entries** | Historical dialect vocabulary with etymological annotations | https://shamela.ws/book/150964 |

**AI-Generated Dialect Data:**

| Source | Content | Purpose | Access |
|---|---|---|---|
| AI-generated parallel pairs | Jais/cloud model generates Egyptian→MSA translations | Fill gaps in scraped data; expand coverage | Teacher model (Ollama API) |

### 8.5 Dialect Data Pipeline — Trained, Not Manual

**Principle: Every dialect mapping is derived from data or AI generation, never hand-entered.**

The pipeline has four stages that run before the main model training:

**Stage 1: Parallel Corpus Construction (Automated)**

```
MADAR-28 (336K parallel sentences)
    + ARB-EGY-CMP (Twitter comments + tweets, existing)
    + Nofal slang (Twitter comments + tweets, existing)
    + Taimoor Dictionary dialect entries (Shamela.ws, structured)
    + OpenSubtitles Arabic (dialect-labeled)
    + Scraped Egyptian social media (with dialect detection pre-filtering)
    = Raw parallel corpus
```

- Language identification: Panlingo.FastText filters non-Arabic
- Dialect labeling: Existing MADAR labels + automatic detection for scraped data
- Quality gates: MinHash dedup, length ratio filtering (0.5-2.0), character-encoding validation
- Target: 500K+ Egyptian↔MSA parallel sentence pairs

**Stage 2: Etymological Root Alignment (Trained Model)**

From the parallel corpus, a statistical alignment model learns dialect-word → MSA-word → root mappings:

1. **Word alignment:** Run fast_align (or EFmarAlign) on Egyptian↔MSA parallel sentences to get word-level correspondences. One-time Python operation (~2 hours on 500K sentences).
2. **Root projection:** For each aligned (dialect_word, MSA_word) pair, look up the MSA word's root in the Neo4j graph. Assign that root to the dialect word.
3. **Pattern derivation:** For each dialect word with an assigned root, attempt morphological pattern matching against known Arabic patterns. If no pattern matches, mark as "irregular."
4. **Confidence scoring:** Alignments with agreement from multiple sentence pairs get higher confidence. Low-confidence entries are flagged for AI augmentation.

**Output:** SQLite table `DialectEtymology` — the product of statistical alignment, not manual entry.

**Stage 3: AI-Augmented Gap Filling (Teacher Model)**

For dialect words that appear in scraped data but lack alignment evidence:

1. Collect unmapped dialect words (frequency >= 5 in scraped corpus).
2. Batch-send to teacher model with etymology prompt.
3. Validate AI-generated mappings against linguistic constraints.
4. Add validated entries with `source='ai_generated'` and lower confidence score.

**Stage 4: Syntactic Reordering Model (Trained on Parallel Corpus)**

1. Parse aligned sentence pairs using Lisan.Syntax (MSA side) and shallow parser (dialect side).
2. Extract transformation patterns by comparing dependency structures.
3. Cluster patterns by construction type.
4. Compile into a rewrite engine (C#) — patterns stored as data, not hardcoded.

### 8.6 Genus-Aware Corpus Enrichment

| ID | Domain | Arabic Category |
|---|---|---|
| 36 | Science/Physics | تصنيف:فيزياء |
| 37 | Science/Chemistry | تصنيف:كيمياء |
| 38 | Science/Biology | تصنيف:أحياء |
| 39 | Science/Mathematics | تصنيف:رياضيات |
| 40 | Science/Medicine/Cardiology | تصنيف:أمراض_القلب |
| 41 | Science/Medicine/Neurology | تصنيف:الجهاز_العصبي |
| 42 | Finance/Economics | تصنيف:اقتصاد |
| 43 | Literature/Poetry | تصنيف:شعر_عربي |
| 44 | DailyLife/Food | تصنيف:طعام_وشراب |
| 45 | Religion/Islam/Fiqh | تصنيف:فقه_إسلامي |

No single category exceeding 40% of rows; target minimum 5,000 articles per major domain.

### 8.7 Corpus Processing Pipeline (.NET-Native)

| Step | Operation | .NET Tool |
|---|---|---|
| 1 | Download raw data + scrape Shamela.ws | HttpClient + custom scrapers (C#) |
| 2 | Language identification | Panlingo.LanguageIdentification.FastText (C# NuGet) |
| 3 | Unicode normalization | Lisan.Tokenizer normalizer (C#) |
| 4 | Length filtering | Custom filter (C#) |
| 5 | Near-duplicate detection | MinHashSharp + custom LSH index (C#) |
| 6 | Cross-set deduplication | Global hash registry (C#) |
| 7 | Quranic text verification | Custom verifier against Tanzil canonical (C#) |
| 8 | Morphological annotation | Farasa API + Lisan.Morphology (C#) |
| 9 | Quality scoring | Custom scorer (C#) |
| 10 | Chunking | Custom chunker (C#) |

The entire corpus processing pipeline runs in .NET. The only external dependency is Farasa (HTTP API call from C#), already integrated in the Aspire orchestration.

---

## 9. Data Quality and QA Baseline

### 9.1 Data Lineage

- Every ingested record carries `SourceUrl` or `ResourceIdentifier`.
- Preserve exact source identity, ingestion timestamp, pipeline version.
- Dialect etymology entries carry `source` field: `alignment`, `ai_generated`, or `scraped`.

### 9.2 Input Golden Sets

- Curate 500+ validated extraction examples per data source.
- Key each by exact source identifier.
- Store: source URL, expected text, expected annotations, validator identity.
- For dialect: 200+ Egyptian↔MSA sentence pairs manually validated for alignment quality.

### 9.3 Automated Extraction Validation

- Compare extractions against golden entries.
- Compute word-level Jaccard similarity.
- Compute Levenshtein distance.
- Gate threshold: Jaccard >= 0.85 AND normalized Levenshtein >= 0.90.

### 9.4 Data Cleaning Pipeline

- Language identification: Panlingo.FastText, threshold 0.9
- Unicode normalization: NFC + Arabic-specific (C#)
- Length filtering: 100 - 100,000 chars
- Near-duplicate: MinHashSharp + custom LSH (C#)
- Quranic text verification against Tanzil canonical
- Morphological annotation cross-checking
- Dialect data: additional filter for Arabic-script-only, remove bot-generated content
- Shamela.ws extraction validation: verify structured text quality against known entries

### 9.5 Learning Curves and Capacity Measurement

- Train on incremental slices (10%, 25%, 50%, 75%, 100%).
- Stop scaling when validation gains flatten (<0.5% over 5K steps).

### 9.6 Model Quality Metrics

| Metric | Purpose | When Measured |
|---|---|---|
| Perplexity | Core LM quality | Every validation step |
| ROUGE-L | Explanation quality | After fine-tuning |
| Attention entropy | Detect attention collapse | Every 5K steps |
| RMSNorm variance | Detect training instability | Every 1K steps |
| Expected Calibration Error | Confidence reliability | After each quantization |
| Morphological feature utilization | Verify injection effectiveness | Ablation at step 20K |
| Dialect reconstruction accuracy | Verify dialect→MSA pipeline | After dialect model training |

### 9.7 QA Dashboard

- Scraper validation pass rates per source
- Jaccard/Levenshtein quality distributions
- Learning curves and overfit alerts
- Retrieval and model benchmark summaries
- Quantization comparison results
- Data contamination checks
- Dialect etymology coverage and confidence distribution

### 9.8 Data Contamination Prevention

- MinHash deduplication ACROSS train/validation/test splits.
- Hash at paragraph level.
- Global "seen hash" registry.
- Post-split audit: sample 1,000 test paragraphs, check 10-gram overlap with train. Target: <0.1%.

### 9.9 Bias Measurement and Mitigation

- Domain representation: flag any domain > 30% of total tokens.
- Dialect representation: Egyptian and MSA each >= 15% of dialect-tagged data.
- Oversample under-represented domains; do not downsample.

---

## 10. Arabic Knowledge Graph

### 10.1 Core Schema

| Node | Required Properties |
|---|---|
| Word | text, frequency, diacritized_form, dialect_tag |
| Root | text, verbal_noun, participles |
| Pattern | text, formula, derived_forms_count |
| Meaning | text, language, context, source |
| POS | tag, features_json |
| Domain | name, path, parent |
| Source | name, url, confidence |
| Synset | id, gloss |
| Context | name, path, parent_path |
| DialectWord | text, dialect, etym_root, msa_equivalent, msa_root, pattern, confidence |

Required relationships: HAS_ROOT, HAS_PATTERN, HAS_POS, HAS_MEANING, BELONGS_TO, DERIVES_FROM, PRODUCES, SYNONYM_OF, ANTONYM_OF, APPEARS_IN, EXPLAINED_IN, RELATED_ROOT, IN_SYNSET, HAS_SUB_CONTEXT, GOVERNS, DIALECT_MAPS_TO (DialectWord → Word), SHARES_ROOT (DialectWord → Root).

### 10.2 Seeding Priorities — Logical Dependency Order

The graph must be seeded in dependency order — dictionaries first (roots + patterns), then Quranic annotations, then cross-references:

1. **Dictionaries** (Al-Qamus Al-Muhit, Mukhtar Al-Sihah, Tag Al-Aroos, Al-Waseet, Al-Ain, Lisan Al-Arab) → Roots, patterns, meanings, word entries
2. **Dialect dictionary** (Taimoor) → DialectWord nodes with dialect tags
3. **Quranic annotations** (Quranic Arabic Corpus) → Morphological links, root usage in Quran
4. **Grammar references** (Alfiyyat Ibn Malik, Qatr Al-Nada, Shudhur Al-Dhahab, Sibawayh) → Grammar rules, syntactic patterns
5. **Hadith references** (Sunnah.com all 15+ books) → Religious context, vocabulary in context
6. **Synonym/antonym networks** (Arabic WordNet, ConceptNet) → Semantic relations
7. **Cross-root relationships** → Derivation chains across roots
8. **Taxonomy hierarchy** (Wikipedia categories) → Domain classification

### 10.3 GraphRAG Query Targets

| Query Type | Latency Target |
|---|---|
| Root lookup | < 20 ms |
| Pattern lookup | < 20 ms |
| Synonym chain (1-3 hops) | < 100 ms |
| Domain-filtered search | < 50 ms |
| Quranic concept lookup | < 30 ms |
| Related-word traversal (1-2 hops) | < 200 ms |
| Context hierarchy | < 150 ms |
| Dialect root lookup (DialectWord → Root) | < 20 ms |
| Dual-root retrieval (dialect root + MSA root) | < 40 ms |

### 10.4 Graph Quality Assurance

- Every Word has HAS_ROOT or "no_root" flag.
- Every Root has PRODUCES relationship.
- Every DialectWord has DIALECT_MAPS_TO relationship.
- No orphan nodes (except by design).
- Spot audit: 200 random Word nodes, >= 95% correct.
- Spot audit: 100 random DialectWord nodes, >= 80% correct etymological mapping.

---

## 11. Native Arabic NLP Layer

### 11.1 Lisan.Morphology (C#)

**Implementation layers (cascading fallback):**

1. **Primary:** In-memory dictionary lookup from Neo4j export. ~200K words. < 2 ms/word.
2. **Secondary:** Pattern-based heuristics (prefix/suffix stripping, pattern matching). < 5 ms/word.
3. **Dialect etymological lookup:** DialectWord node lookup from trained alignment map. Returns etymological root + MSA equivalent. < 2 ms/word.
4. **Tertiary:** CAMeL (development-time only, not shipped).
5. **Fallback:** Partial analysis, zero vectors for missing features. Always succeeds.

**Runtime target:** < 5 ms/word on CPU (primary), < 15 ms/word (including fallback).

### 11.2 Lisan.Tokenizer (C#) — BPE Training Specification

**Tokenizer type:** BPE, vocabulary size 32,768

**Training tool:** `Microsoft.ML.Tokenizers` (C# NuGet)

| Step | Operation | Detail |
|---|---|---|
| 1 | Assemble training corpus | OSCAR Arabic + Wikipedia + linguistic texts + dialect corpus. Target: 3-5B characters. |
| 2 | Pre-tokenization | Arabic Unicode normalization + split on whitespace/punctuation (C#) |
| 3 | Character coverage | 0.9995 for Arabic script |
| 4 | Special tokens | 12 tokens: `<s>`, `</s>`, `<pad>`, `<unk>`, `[CONTEXT]`, `[DIALECT]`, `[MSA-RECON]`, morph/dialect markers. Leaves 32,756 BPE merges. |
| 5 | Train | `Microsoft.ML.Tokenizers` BPE training in C# |
| 6 | Validate OOV | OOV < 0.01% on Arabic text (including Egyptian) |
| 7 | Validate subword efficiency | Avg tokens/word < 1.5 |
| 8 | Validate morphological alignment | Root-boundary alignment > 60% |
| 9 | Validate dialect coverage | OOV on Egyptian text < 0.05% |
| 10 | Build teacher alignment map | Tokenize 10K Arabic sentences with Lisan BPE + Jais tokenizer; build monotonic alignment. Target: > 90% coverage. |

### 11.3 Lisan.Syntax (C#)

- Rule-driven parser with Arabic grammar production rules
- Sources: **Alfiyyat Ibn Malik** (1,002 rules from Shamela.ws), **Qatr Al-Nada** (Shamela.ws), **Shudhur Al-Dhahab** (Shamela.ws), **Sibawayh's Al-Kitab** (Shamela.ws), Quranic Arabic Corpus annotations, PADT patterns
- Coverage target: 80% MSA, 60% Egyptian
- Neural fallback for unparseable sentences

### 11.4 Lisan.Dialect (C#) — Data-Driven Dialect System

```
Lisan.Dialect
├── Lisan.Dialect.Detection        — CNN (5M params, TorchSharp → ONNX)
├── Lisan.Dialect.Etymology        — Trained alignment map (SQLite, built from Section 8.5 pipeline)
├── Lisan.Dialect.Reconstructor    — Learned rewrite engine (C#, pattern table from Section 8.5 Stage 4)
└── Lisan.Dialect.Translator       — Neural Egyptian↔MSA generation (via primary model or teacher)
```

**11.4.1 Detection (5M params, TorchSharp)**
- Character-level CNN trained on MADAR + OpenSubtitles + scraped dialect-labeled data
- Target: > 75% on MADAR benchmark

**11.4.2 Etymology — Trained Alignment Map (SQLite)**
- Built by: word alignment on parallel corpus (Stage 2) + AI augmentation (Stage 3)
- Coverage target: > 80% of Egyptian words in MADAR test set
- Confidence threshold: entries with confidence < 0.5 treated as low confidence

**11.4.3 Reconstructor — Learned Rewrite Engine (C#)**
- Pattern table populated by Section 8.5 Stage 4 extraction
- Patterns stored as data — new patterns added by re-running extraction pipeline
- Learned pattern categories: demonstrative repositioning, circumfix negation, reduced negation, future prefix substitution, compound conjunction, adverbial reduction, intensifier substitution, root shift in common verbs, discourse marker shift, aspectual marker, demonstrative reduction, interrogative shift, preposition shift

**11.4.4 Translator — Neural Dialect Generation**
- Primary path: 458M model generates in dialect with `[DIALECT: egyptian]` marker
- Fallback path: MSA generation + reverse transformation patterns
- Teacher-assisted path: cached teacher model dialect paraphrases

**11.4.5 Egyptian Dialect — Known Phenomena Coverage**

The following phenomena are expected to be learned from the data pipeline. This table documents what the system should handle, not what is manually coded:

| Phenomenon | Egyptian Example | MSA Equivalent | How It Is Learned |
|---|---|---|---|
| Etymological root shift | عايز ← عوز | أريد ← أرد | Word alignment on parallel corpus → root projection |
| Demonstrative repositioning | المنتج ده | هذا المنتج | Dependency structure comparison on aligned sentences |
| Circumfix negation (ما...ش) | ما ينفعش | لا ينفع | Pattern extraction from aligned negated sentences |
| Reduced negation (مش) | مش نافع | ليس مفيداً | Pattern extraction + alignment |
| Future prefix substitution | هروح | سأذهب | Morphological analysis + alignment |
| Compound conjunction | علشان / عشان | لأن / لكي | Token-level alignment |
| Adverbial reduction | كده | هكذا | Token-level alignment |
| Intensifier substitution | أوي | جداً | Token-level alignment |
| Root shift in common verbs | جاب ← ج-ي-ب | أحضر ← ح-ض-ر | Root projection from alignment |
| Discourse marker shift | بقي | إذن / ثم | Context-dependent alignment |
| Aspectual marker | بـ + verb (بيكتب) | present tense | Morphological analysis + alignment |
| Demonstrative reduction | ده / دي / دول | هذا / هذه / هؤلاء | Phonological pattern matching |
| Interrogative shift | إيه / إيهما | ماذا / أي | Token-level alignment |
| Preposition shift | في (meaning "to") | إلى | Context-dependent alignment |

### 11.5 Lisan.Diacritization (C# + ONNX)

- **Deterministic layer:** Rule-based from morphological analysis. ~70% coverage.
- **Neural refinement:** 4-layer transformer (d_model=512, ~25M params) trained via TorchSharp on T1000 4GB. Exported to ONNX for runtime inference. Training data includes both MSA and Egyptian dialect text with diacritics.
- **Combined target:** > 85% word-level accuracy.

---

## 12. RAG System

### 12.1 Retrieval Layers

| Layer | .NET Tool | Fallback |
|---|---|---|
| Graph retrieval | Neo4j C# driver | Vector only |
| Graph retrieval (dialect root) | Neo4j C# driver (DialectWord → Root traversal) | MSA root only |
| Vector retrieval | FaissNet or HNSW.Net | Graph only |
| Vector retrieval (dialect) | FaissNet/HNSW.Net (query with reconstructed MSA) | Original query only |
| Domain ranking | Custom C# classifier | Skip ranking |
| Morphology-aware query expansion | Lisan.Morphology + Lisan.Dialect.Etymology (C#) | Original query |

### 12.2 Template-Based Response Layer

8 template types: root explanation, pattern explanation, grammar explanation, Quranic references, synonym/antonym listing, diacritization, dialect translation, dialect etymology explanation.

Minimum quality target: 75% acceptable by human review.

### 12.3 Context Assembly Rules

- Combine morphology + graph + vector evidence when available.
- Include dialect reconstruction annotations for dialect queries.
- For dialect queries: run graph retrieval with BOTH dialect etymological root AND MSA root; merge results.
- For dialect queries: run vector retrieval with BOTH original dialect text AND reconstructed MSA; merge results.
- Deduplicate beyond 4K.
- Priority: domain match > graph distance > vector similarity.
- `[CONTEXT]` boundary token between context and query.
- `[DIALECT: label]` marker for dialect context.
- `[MSA-RECON]` marker for reconstructed MSA text.

**Latency targets:** < 500 ms without model, < 2 sec with model (first token), then SSE streaming.

### 12.4 Embedding Model Specification

**Primary:** Arabertv02 fine-tuned on Arabic NLI + retrieval pairs.

| Step | Tool | Environment |
|---|---|---|
| 1 | Fine-tune Arabert on XNLI Arabic | Python (one-time, ~4 hours) |
| 2 | Fine-tune on 50K in-domain retrieval pairs (MSA + dialect) | Python (one-time, ~2 hours) |
| 3 | Export to ONNX | `optimum-cli` (Python, one-time) |
| 4 | Build FaissNet/HNSW.Net index | C# (production pipeline) |
| 5 | Embedding inference at runtime | ONNX Runtime (C#, every query) |

**Fallback embedding:** `paraphrase-multilingual-MiniLM-L12-v2` ONNX export.

### 12.5 Arabic-Specific Retrieval Challenges

| Challenge | Solution |
|---|---|
| Morphological mismatch | Root-based query expansion (C#) |
| RTL handling | Logical-order storage; display-layer RTL |
| Diacritics vs. undiacritized | Normalize to undiacritized for retrieval; diacritized for ranking |
| Multiple analyses | Retrieve for all, rank by combined relevance |
| Dialect mismatch | Dialect detection + etymological root expansion + MSA reconstruction (C#) |
| Dialect word not in MSA index | Dual-query: original dialect text + MSA reconstruction; merge results |
| Post-posed demonstrative (المنتج ده) | Reconstruct as هذا المنتج before vector embedding |

---

## 13. Quantization and Runtime Packaging

### 13.1 INT8

- Per-channel symmetric, group_size=1
- Calibration: 512 validation samples (512 tokens each)
- Gate: >= 99% of FP16

### 13.2 INT4

- GPTQ or AWQ, group_size=128
- Skip: embeddings, morph projection, layer 0 W_Q/W_O, layer 15 W_down
- Gate: >= 97% of FP16
- Fallback: group_size=64, then QoRA (Section 13.5), then INT8

### 13.3 2-Bit Packed Ternary

- Group-scale + ternary sign, 2-bit storage, group_scale FP16 per 64 weights
- Total: ~114 MB (signs) + ~14 MB (scales) = 128 MB
- Skip: same as INT4 + all attention projections
- Gate: >= 95% of FP16
- Fallback: QoRA, then defer to ternary-from-scratch

### 13.4 Packaging Targets

| Format | Deployment | Weight Size |
|---|---|---:|
| FP16 | Server-grade | 916 MB |
| INT8 | Safe compact | 458 MB |
| INT4 | Default consumer | 229 MB |
| 2-bit ternary | Ultra-light (gated) | 128 MB |

### 13.5 LoRA/QoRA Recovery

1. Quantize to target format
2. Train LoRA adapters (rank=16, alpha=32) on W_Q, W_V, W_gate, W_up for all 16 layers
3. 50K steps, LR=1e-4
4. If still below gate: increase rank to 32
5. Max LoRA budget: 50M params. Merge after training for zero inference overhead.
6. If still insufficient: fall back to next higher precision.

### 13.6 Layerwise Quantization Sensitivity Analysis

1. Quantize each layer independently (others in FP16)
2. Measure perplexity impact per layer
3. Top 20% most sensitive: keep at higher precision
4. Typical: attention W_O and first/last layers are most sensitive

---

## 14. Inference Runtime (.NET)

### 14.1 Runtime Architecture

```
Lisan .NET Host (ASP.NET Core + .NET Aspire)
    |
    +-- Lisan.Morphology (C#, in-memory dictionary + heuristics + dialect etymology)
    +-- Lisan.Tokenizer (C#, Microsoft.ML.Tokenizers BPE)
    +-- Lisan.Syntax (C#, rule-driven parser)
    +-- Lisan.Dialect (C#, detection CNN + etymology SQLite + rewrite engine + translator)
    +-- Lisan.Diacritization (C# deterministic + ONNX neural)
    +-- Lisan.GraphRAG (Neo4j C# driver + FaissNet/HNSW.Net, dual-root queries)
    +-- Lisan.Model (ONNX Runtime — primary; Ollama — secondary for GGUF)
    +-- Lisan.API (ASP.NET Core minimal API with SSE streaming)
    +-- Lisan.TrainingOrchestrator (C# sidecar manager for Python training)
```

### 14.2 Model Inference Runtime

| Runtime | Format | CPU Perf | GPU Support | Use Case |
|---|---|---|---|---|
| ONNX Runtime | ONNX | Best CPU | CUDA EP (T1000) | **Primary deployment** |
| Ollama | GGUF | Good CPU | Good | **Secondary; also serves teacher** |
| TorchSharp | .pt weights | Moderate | Limited CUDA | **Validation + prototyping** |

### 14.3 SSE Streaming — Primary Chat Interface

The `/v1/chat/completions` endpoint defaults to SSE streaming, compatible with OpenAI's streaming format.

**Request:**

```
POST /v1/chat/completions
Headers:
  Authorization: Bearer <key>
  Content-Type: application/json
Body: {
  "model": "lisan-bit",
  "messages": [...],
  "stream": true,
  "stream_options": { "include_usage": true },
  "dialect_match": true,
  "max_tokens": 2048
}
```

**SSE Response Stream:**

```
data: {"id":"lisan-abc123","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant","content":""},"finish_reason":null}]}

data: {"id":"lisan-abc123","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"لو"},"finish_reason":null}]}

data: {"id":"lisan-abc123","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":" عايز"},"finish_reason":null}]}

...

data: {"id":"lisan-abc123","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: {"id":"lisan-abc123","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":128,"completion_tokens":45,"total_tokens":173}}

data: [DONE]
```

**Streaming pipeline design:**

- **Phase 1 (blocking):** Normalize → Morphology → Dialect Detection + Reconstruction → Retrieval → Context Assembly. Typical latency: 50-500ms.
- **Phase 2 (streaming):** ONNX Runtime generates one token at a time. Each token is immediately flushed as an SSE chunk.
- **First-token latency target:** < 2 seconds on GPU, < 5 seconds on CPU.

### 14.4 Inference Acceleration

| Technique | Phase | Speedup |
|---|---|---|
| ONNX graph optimization | Initial release | 20-40% |
| KV cache reuse | Initial release | 50-80% repeat computation reduction |
| T1000 GPU inference (ONNX Runtime CUDA EP) | Initial release | 3-5x vs CPU |
| SSE token streaming | Initial release | Perceived latency < 200ms to first token |
| Speculative decoding | Post-launch | 2-3x greedy |
| Continuous batching | Post-launch | 2-4x throughput |

### 14.5 Inference Performance Targets

| Mode | First Token Latency | Throughput |
|---|---|---|
| Morphology-only | < 50 ms | > 1,000 words/sec |
| RAG-only | < 500 ms | > 2 queries/sec |
| Full + model (4K, T1000 GPU, SSE) | < 2 sec first token | > 0.5 queries/sec |
| Full + model (4K, CPU only, SSE) | < 5 sec first token | > 0.2 queries/sec |

---

## 15. Benchmarks and Acceptance Criteria

### 15.1 Core Benchmarks

| Benchmark | Target | Construction |
|---|---:|---|
| MorphAnalysis-500 | > 90% | 500 words, 2 linguists + adjudication |
| Diacritization-Acc | > 85% | 1,000 sentences (Quranic + news) |
| GrammarJudgment-300 | > 80% | 150 grammatical + 150 ungrammatical |
| Dialect-ID-200 | > 75% | 50/dialect from MADAR |
| Coherence-AR-100 | > 3.5/5 | 100 passages, 3 annotators |
| QA-Morph-200 | > 75% | 200 morphology questions |
| QA-Quran-100 | > 90% | 100 questions with known references |
| RAG-Retrieval-100 | > 85% | 100 queries, top-3 relevance |

### 15.2 Dialect-Specific Benchmarks

| Benchmark | Target | Construction |
|---|---:|---|
| Dialect-Etymology-200 | > 70% etymological root correct | 200 Egyptian words, linguist-validated roots |
| Dialect-Reconstruction-100 | > 60% MSA reconstruction acceptable | 100 Egyptian sentences, 3 annotators |
| Dialect-RAG-50 | > 50% relevant retrieval | 50 Egyptian queries, top-3 relevance |
| Dialect-Response-50 | > 70% dialect-matched responses acceptable | 50 Egyptian queries, human review |
| Dialect-Syntactic-50 | > 50% reordering correct | 50 Egyptian sentences with post-posed demonstratives, negation, etc. |

### 15.3 RAG-Only Acceptance

| Benchmark | Target |
|---|---:|
| Template-Response-100 | >= 75% |
| Graph-Lookup-200 | >= 90% |
| Vector-Search-100 | >= 80% |
| End-to-End-RAG-50 | >= 50% |

### 15.4 Performance Targets

- CPU narrow sub-tasks: < 100 ms
- RAG without model: < 500 ms
- Lite mode RAM: < 781 MB
- SSE first token: < 2 sec (GPU), < 5 sec (CPU)

### 15.5 Inter-Annotator Agreement

- Minimum 2 annotators per benchmark.
- Cohen's Kappa >= 0.65 (categorical), Spearman >= 0.70 (ordinal).
- Below threshold: adjudication + guideline refinement.

### 15.6 Held-Out Blind Test Set

- 20% of each benchmark reserved as blind test.
- Evaluated ONCE by independent evaluator.
- Reported results are blind test results.

---

## 16. Continuous Integration and Regression Testing

| Test Type | Frequency | Gate |
|---|---|---|
| Unit tests | Every commit | 100% pass |
| Integration tests | Every PR | 100% pass |
| Golden set validation | Daily | Jaccard >= 0.85 |
| Regression (model) | Every checkpoint | No > 2% drop |
| Quantization gate | Each quantization level | Meets threshold |
| Contamination check | Weekly | < 0.1% |
| Dialect etymology accuracy | After alignment pipeline re-run | > 70% root correct |
| Dialect reconstruction quality | After pattern extraction re-run | > 60% acceptable |

---

## 17. API Surface

| Endpoint | Method | Description | Streaming |
|---|---|---|---|
| `/v1/chat/completions` | POST | OpenAI-compatible chat (SSE primary) | Yes (SSE) |
| `/v1/embeddings` | POST | Text embeddings | No |
| `/v1/morphology/analyze` | POST | Full morphological analysis | No |
| `/v1/morphology/root` | POST | Root extraction | No |
| `/v1/morphology/pattern` | POST | Pattern extraction | No |
| `/v1/morphology/diacritize` | POST | Diacritization | No |
| `/v1/syntax/parse` | POST | Sentence parsing + i'rab | No |
| `/v1/syntax/check` | POST | Grammar checking | No |
| `/v1/dialect/detect` | POST | Dialect detection | No |
| `/v1/dialect/reconstruct` | POST | Dialect → MSA reconstruction with etymology | No |
| `/v1/dialect/translate` | POST | Dialect ↔ MSA translation | No |
| `/v1/dialect/etymology` | POST | Etymological root lookup for dialect word | No |
| `/v1/knowledge/search` | POST | Graph + vector search | No |
| `/v1/knowledge/quran` | POST | Quranic concept lookup | No |

**Auth:** API key via `Authorization: Bearer <key>`. Rate limit: 60 req/min.

---

## 18. Risks and Controls

| Risk | Severity | Control | Fallback |
|---|---|---|---|
| T1000 4GB VRAM insufficient for training | Critical | DeepSpeed ZeRO-2 + CPU offload + gradient checkpointing + flash attention | CPU-only training (10x slower) |
| Training too slow on T1000 | High | Reduce steps to 50K; cloud API teacher | Extend timeline to 32 weeks |
| PyTorch CUDA incompatibility on T1000 | High | Week 1 validation; T1000 is CUDA 7.5 (PyTorch supports) | CPU-only training |
| Jais not available in Ollama | Medium | Convert Jais to GGUF; cloud API teacher | Use Qwen2-1.5B as teacher |
| INT4 quality loss > 3% | Medium | Layerwise analysis + QoRA | Ship INT8 default |
| Training divergence | Medium | Checkpoint rollback + LR reduction | Reduce batch, extend warmup |
| Template coverage insufficient | Medium | Expand library aggressively | Ship at 60%, iterate |
| Morphological features hurt quality | Low | Ablation at 20K | Remove injection |
| Graph data quality errors | High | Spot audits, 200-node manual audit | Flag low-confidence edges |
| KV-cache memory at 32K | High | INT8 KV cache quantization | Default 16K for 8GB |
| Syntax parser gaps | Medium | Incremental rules + neural fallback | Mark as "unanalyzed" |
| Vocabulary alignment failures | Medium | Position-ratio fallback | Train without distillation |
| Cloud API rate limits | Low | Batch requests; cache responses | Use local Ollama teacher only |
| Corpus contamination | High | Cross-set dedup protocol | Re-split and retrain |
| Dialect alignment model produces wrong roots | High | Confidence scoring + linguist spot-check 100 entries | Use AI-generated mappings with lower confidence |
| Dialect parallel corpus too small | Medium | AI-augmented gap filling; expand scraping | Ship with MSA-only fallback for unmapped words |
| Dialect syntactic reordering errors | Medium | Confidence scoring; fall back to word-by-word mapping | Skip reordering, use original word order |
| SSE streaming breaks on slow connections | Low | Buffer management + client-side reconnection | Fall back to blocking mode |
| Dialect training data bias | Medium | Balance: no dialect > 60% of dialect corpus | Downsample dominant dialect |
| Shamela.ws scraping blocked or rate-limited | Medium | Respect robots.txt; throttle requests; use cached dumps | Manual download + upload |

---

## 19. Implementation Epics and Tasks — Logical Dependency Order

**Dependency chain:**

```
Epic 1: Toolchain
    ↓
Epic 2: Data Acquisition + Cleaning
    ↓
Epic 3: Knowledge Graph Seeding (needs clean data)
    ↓
Epic 4: Morphology + Tokenizer (needs graph for lookups + clean corpus for BPE)
    ↓
Epic 5: Dialect Data Pipeline (needs morphology for root projection + parallel corpus)
    ↓
Epic 6: NLP Layer (needs morphology + dialect etymology + graph)
    ↓
Epic 7: RAG System + SSE (needs NLP + graph + vectors)
    ↓
Epic 8: Model Training (needs all above)
    ↓
Epic 9: Quantization (needs trained model)
    ↓
Epic 10: Runtime Integration + API (needs quantized model + all components)
    ↓
Epic 11: Evaluation + Publication (needs complete system)
```

### Epic 1: Validation and Toolchain (Week 1)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 1.1 | Validate PyTorch + CUDA on T1000 4GB. | 2 hours | CUDA available; training loop runs |
| 1.2 | Install and validate DeepSpeed ZeRO-2 with CPU offloading. | 4 hours | CPU offload works; GPU memory within 4 GB |
| 1.3 | Validate flash attention on T1000 (PyTorch 2.0+). | 2 hours | `scaled_dot_product_attention` uses flash kernel |
| 1.4 | Validate TorchSharp CUDA 12.8 in .NET solution on T1000. | 4 hours | Tensor ops on T1000 GPU; model forward pass works |
| 1.5 | Validate Microsoft.ML.Tokenizers BPE training in C#. | 4 hours | Train small BPE; encode/decode roundtrip |
| 1.6 | Validate ONNX Runtime in .NET with CUDA EP on T1000. | 4 hours | Load ONNX model; GPU inference works |
| 1.7 | Install and validate Ollama. Pull and run Jais or test model on CPU. | 4 hours | Ollama serves model on CPU; API responds |
| 1.8 | Validate Neo4j + FaissNet/HNSW.Net in .NET. | 4 hours | Write/query graph; index and search vectors |
| 1.9 | Validate Panlingo.FastText + MinHashSharp. | 2 hours | Language ID and MinHash work |
| 1.10 | Set up Python sidecar: .NET launches Python training, monitors output. | 4 hours | Sidecar pattern works end-to-end |
| 1.11 | Test cloud API keys. | 2 hours | At least one endpoint responds |
| 1.12 | Validate fast_align/EFmarAlign on small parallel corpus. | 4 hours | Word alignment produces reasonable results |
| 1.13 | Validate SSE streaming in ASP.NET Core minimal API. | 4 hours | SSE endpoint streams tokens |
| 1.14 | Validate Shamela.ws scraping: download a book, extract structured text. | 4 hours | Structured Arabic text extracted from Shamela page |

**Gate:** All tools confirmed working on T1000 + Core i9H + 64GB RAM. SSE streaming functional. Shamela scraping functional.

### Epic 2: Data Acquisition and Cleaning (Weeks 2-5)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 2.1 | Scrape and ingest Shamela.ws dictionaries: Al-Qamus Al-Muhit, Mukhtar Al-Sihah, Tag Al-Aroos, Al-Waseet, Al-Ain, Taimoor. | 40 hours | All 6 dictionaries extracted with structured entries (word, root, meaning, pattern) |
| 2.2 | Scrape and ingest Shamela.ws grammar references: Alfiyyat Ibn Malik, Qatr Al-Nada, Shudhur Al-Dhahab, Sibawayh's Al-Kitab. | 24 hours | All 4 grammar texts extracted with rule annotations |
| 2.3 | Ingest Quranic text from Tanzil; verify against canonical. | 4 hours | 100% canonical match |
| 2.4 | Ingest all 15+ Hadith books from Sunnah.com. | 24 hours | All books ingested with metadata (narrator, book, chapter, grade) |
| 2.5 | Ingest Quranic Arabic Corpus annotations. | 8 hours | Morphological + syntactic annotations loaded |
| 2.6 | Ingest OSCAR/CC-100/Wikipedia Arabic. | 16 hours | > 1B tokens raw |
| 2.7 | Ingest Hindawi + OPUS + MADAR. | 16 hours | Literary + parallel + dialect data loaded |
| 2.8 | Ingest ARB-EGY-CMP + Nofal (Twitter data — already integrated). | 4 hours | Existing data accessible; Twitter comments/tweets identified |
| 2.9 | Scrape Egyptian social media (Twitter/X, forums). | 24 hours | 100K+ Egyptian sentences with quality gates |
| 2.10 | Language ID + Unicode normalization + length filtering (C#). | 8 hours | All docs pass gates |
| 2.11 | MinHash deduplication within and across sources (C#). | 8 hours | Near-duplicate < 5% |
| 2.12 | Train/test/val split with contamination prevention (C#). | 4 hours | < 0.1% n-gram overlap |
| 2.13 | Morphological annotation via Farasa API (C# client). | 24 hours | All docs annotated |
| 2.14 | Curate golden sets: 500+ per source + 200 Egyptian↔MSA pairs. | 24 hours | Stored with identifiers |
| 2.15 | QA dashboard live with all metrics. | 8 hours | Dashboard operational |

**Gate:** All data sources downloaded, cleaned, deduped, and annotated. Corpus > 500M tokens. Golden sets validated. Dialect parallel corpus > 500K pairs ready.

### Epic 3: Knowledge Graph Seeding (Weeks 5-7)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 3.1 | Seed Neo4j with dictionary data from Shamela (Al-Qamus, Mukhtar, Tag Al-Aroos, Al-Waseet, Al-Ain). | 24 hours | 200K+ Word nodes, 6K+ Root nodes, meanings and patterns |
| 3.2 | Seed Lisan Al-Arab entries (from almaany.com or Shamela). | 16 hours | Comprehensive classical Arabic coverage |
| 3.3 | Seed Quranic annotations + cross-references from Quranic Arabic Corpus. | 8 hours | All Quranic words linked to roots, patterns, and verses |
| 3.4 | Seed grammar rules from Alfiyyat Ibn Malik, Qatr Al-Nada, Shudhur Al-Dhahab, Sibawayh. | 16 hours | 1,000+ grammar production rules and syntactic patterns |
| 3.5 | Seed Hadith references from all 15+ Sunnah.com books. | 16 hours | Hadith entries with narrator chains, grades, book/chapter metadata |
| 3.6 | Seed ConceptNet Arabic edges. | 8 hours | 100K+ edges |
| 3.7 | Build taxonomy from Wikipedia categories. | 8 hours | 10+ domains, 5K+ articles each |
| 3.8 | Seed Taimoor dialect entries as DialectWord nodes. | 8 hours | Dialect words with dialect tags and etymological hints |
| 3.9 | Validate graph: spot audit 200 Word nodes, 100 Root nodes. | 8 hours | >= 95% correct |
| 3.10 | Seed synonym/antonym networks (Arabic WordNet). | 8 hours | SYNONYM_OF, ANTONYM_OF relationships |

**Gate:** Graph > 200K Word nodes, 6K+ Root nodes, grammar rules loaded, Hadith linked, taxonomy built. DialectWord nodes from Taimoor present.

### Epic 4: Morphology + Tokenizer (Weeks 7-9)

*Depends on: Epic 3 (graph for dictionary lookups), Epic 2 (clean corpus for BPE training)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 4.1 | Lisan.Morphology: in-memory dictionary lookup from Neo4j export (C#). | 16 hours | < 2 ms/word, > 90% coverage |
| 4.2 | Lisan.Morphology: pattern-based heuristics (C#). | 16 hours | < 5 ms/word, 80% coverage |
| 4.3 | Lisan.Morphology: disambiguation (C#). | 16 hours | > 70% correct on ambiguous |
| 4.4 | Train BPE tokenizer using Microsoft.ML.Tokenizers (C#), including dialect corpus. | 8 hours | OOV < 0.01% MSA, < 0.05% Egyptian |
| 4.5 | Build token alignment map (Lisan BPE ↔ Jais). | 8 hours | > 90% coverage |
| 4.6 | Lisan.Tokenizer: BPE encode/decode (C#). | 16 hours | Roundtrip on 10K sentences |
| 4.7 | Validate morphological feature injection pipeline end-to-end. | 8 hours | Root/Pattern/POS embeddings correctly injected for test sentences |

**Gate:** Morphology > 85% coverage. BPE passes all validation. Token alignment > 90%. Feature injection verified.

### Epic 5: Dialect Data Pipeline (Weeks 9-11)

*Depends on: Epic 4 (morphology for root projection), Epic 2 (parallel corpus), Epic 3 (graph for root lookups)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 5.1 | Run fast_align on Egyptian↔MSA parallel corpus (500K+ sentences). | 4 hours | Word alignment output for all pairs |
| 5.2 | Build etymological root map from aligned pairs + Neo4j root lookup. | 16 hours | SQLite table with > 3,000 Egyptian entries, > 60% MADAR test coverage |
| 5.3 | Validate etymological map: 100-entry spot check by linguist. | 8 hours | >= 70% correct roots |
| 5.4 | AI-augmented gap filling: send unmapped words to teacher model. | 16 hours | Total > 5,000 Egyptian entries |
| 5.5 | Extract syntactic transformation patterns from aligned dependency structures. | 24 hours | Pattern table with >= 10 distinct pattern categories |
| 5.6 | Validate transformation patterns on 50 Egyptian sentences. | 8 hours | >= 50% correct MSA reconstruction |
| 5.7 | Seed Neo4j DialectWord nodes from etymological map. | 8 hours | DIALECT_MAPS_TO relationships created |
| 5.8 | Build dialect etymology confidence scoring. | 4 hours | Confidence scores assigned to all entries |
| 5.9 | Integration test: end-to-end dialect reconstruction pipeline. | 8 hours | "عايز ارجع المنتج ده" → "أريد إرجاع هذا المنتج" |

**Gate:** Etymological map covers > 60% of Egyptian test vocabulary. Reconstruction accuracy > 50% on test sentences. No manual entries in the map.

### Epic 6: NLP Layer (Weeks 11-14)

*Depends on: Epic 4 (morphology), Epic 5 (dialect etymology), Epic 3 (graph)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 6.1 | Lisan.Morphology: dialect etymological lookup integration (C#). | 8 hours | Dialect words resolved to roots |
| 6.2 | Lisan.Syntax: rule-driven parser from Alfiyyat + Qatr Al-Nada + Shudhur + Sibawayh rules (C#). | 32 hours | 80% MSA parsing |
| 6.3 | Lisan.Syntax: case ending prediction (C#). | 16 hours | > 70% on Quranic text |
| 6.4 | Lisan.Dialect: detection CNN via TorchSharp (C#, train on T1000). | 16 hours | > 75% on MADAR |
| 6.5 | Lisan.Dialect: etymology lookup module (C#, SQLite). | 8 hours | < 2 ms/word lookup |
| 6.6 | Lisan.Dialect: reconstruction engine (C#, pattern table). | 16 hours | Syntactic reordering + MSA reconstruction |
| 6.7 | Lisan.Dialect: end-to-end integration (C#). | 16 hours | "عايز ارجع المنتج ده" → correct MSA + annotations |
| 6.8 | Lisan.Diacritization: deterministic layer (C#). | 16 hours | > 70% word accuracy |
| 6.9 | Lisan.Diacritization: neural model via TorchSharp (C#, includes dialect data). | 24 hours | > 92% word accuracy |
| 6.10 | Integrate diacritization pipeline (C#). | 8 hours | Combined > 85% |
| 6.11 | Unit tests: > 90% coverage. | 16 hours | All pass |

**Gate:** NLP libs pass tests. Morphology > 85%. Diacritization > 85%. Dialect reconstruction > 50% on benchmark.

### Epic 7: RAG System + SSE Streaming (Weeks 14-17)

*Depends on: Epic 6 (NLP), Epic 3 (graph), Epic 4 (embeddings)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 7.1 | Fine-tune Arabert on MSA + dialect retrieval pairs (Python, one-time) + export to ONNX. | 24 hours | Quality > MiniLM baseline on both MSA and Egyptian queries |
| 7.2 | Build FaissNet/HNSW.Net index (C#). | 16 hours | Search < 10 ms |
| 7.3 | GraphRAG query engine: all query types including dual-root dialect queries (C#). | 24 hours | Within latency targets |
| 7.4 | Morphology-aware query expansion + dialect root expansion (C#). | 8 hours | > 20% more relevant results for dialect queries |
| 7.5 | Context assembly with tiering + dedup + dialect annotations (C#). | 16 hours | Within budget, no duplicates, dialect markers present |
| 7.6 | Template-based responses for 8 types including dialect etymology (C#). | 24 hours | 80% query type coverage |
| 7.7 | End-to-end RAG pipeline (C#). | 16 hours | < 500 ms latency |
| 7.8 | Graceful degradation (C#). | 8 hours | Never crashes; dialect failure degrades to MSA |
| 7.9 | SSE streaming endpoint for chat (C#). | 16 hours | SSE streams tokens; OpenAI-compatible format |
| 7.10 | All API endpoints including dialect endpoints (C#). | 24 hours | Functional; OpenAPI spec |
| 7.11 | RAG-only benchmarks (MSA + dialect). | 16 hours | Meet Section 15.3 + 15.2 targets |
| 7.12 | Integration testing. | 8 hours | All pass |

**Gate:** RAG-only product shippable with SSE streaming. Template >= 75%, Graph >= 90%. Dialect endpoints functional.

### Epic 8: Model Training (Weeks 12-20)

*Depends on: Epic 4 (tokenizer + morph injection), Epic 2 (training data), Epic 7 can run in parallel for RAG-only product*

Note: Model training starts at Week 12 (after BPE + morphology are ready) and runs in parallel with Epics 5-7. The RAG-only product ships independently at the end of Epic 7.

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 8.1 | Implement model architecture in PyTorch + TorchSharp (dual definition). | 24 hours | Both produce same output on test input |
| 8.2 | Implement DeepSpeed ZeRO-2 training loop (Python). | 16 hours | Loss decreases; GPU memory stays within 4 GB |
| 8.3 | Set up Ollama teacher: pull/convert Jais-1.3B, test API from Python. | 8 hours | Teacher logits available via Ollama API |
| 8.4 | Vocabulary alignment + distillation loss (Python). | 16 hours | > 90% alignment; valid KL loss |
| 8.5 | Implement .NET training orchestrator sidecar (C#). | 8 hours | .NET launches, monitors, validates training |
| 8.6 | Phase 1: 2K context, steps 1-10K. | ~55 hours | Smooth loss decrease |
| 8.7 | Phase 2: 4K context, steps 10K-40K. | ~303 hours | Steady improvement |
| 8.8 | Phase 3: 8K context, steps 40K-70K. | ~500 hours | Final perplexity < 25 |
| 8.9 | Ablation: with vs. without morphological injection at step 20K. | 8 hours | Injection improves > 2% |
| 8.10 | Ablation: with vs. without dialect training data. | 8 hours | Dialect data improves dialect benchmark > 5% |
| 8.11 | Learning curve analysis. | 16 hours | Capacity limits identified |
| 8.12 | Validate checkpoints in TorchSharp (C#). | 8 hours | TorchSharp loads weights; correct output |
| 8.13 | Export to ONNX + GGUF (for Ollama). | 8 hours | Both formats validated |
| 8.14 | Full benchmark suite (MSA + dialect). | 16 hours | All Section 15.1 + 15.2 targets met |

**Gate:** FP16 model passes benchmarks. ONNX + GGUF validated. TorchSharp can load and run model. Dialect benchmarks met.

### Epic 9: Quantization (Weeks 20-22)

*Depends on: Epic 8 (trained model)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 9.1 | Layerwise sensitivity analysis. | 16 hours | Skip list generated |
| 9.2 | INT8 quantization + benchmarks. | 8 hours | >= 99% FP16 |
| 9.3 | INT4 quantization + benchmarks. | 16 hours | >= 97% FP16 |
| 9.4 | If INT4 < 97%: QoRA recovery. | 24 hours | INT4 + QoRA >= 97% |
| 9.5 | 2-bit ternary + benchmarks. | 16 hours | >= 95% FP16 |
| 9.6 | If ternary < 95%: QoRA recovery. | 24 hours | Ternary + QoRA >= 95% |
| 9.7 | If ternary still < 95%: defer. | 0 hours | Ship INT4 |
| 9.8 | Package all formats (ONNX + GGUF). | 8 hours | All validated |
| 9.9 | Dialect benchmark at each quantization level. | 8 hours | Dialect degradation < 2% relative to FP16 |

**Gate:** INT4 passes. INT8 passes. Ternary passes or deferred. Dialect benchmarks within tolerance.

### Epic 10: Runtime Integration and API (Weeks 22-26)

*Depends on: Epic 9 (quantized model), Epic 7 (RAG + SSE)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 10.1 | ONNX Runtime model inference in Lisan.Model (C#). | 16 hours | < 2 sec for 4K on T1000 |
| 10.2 | SSE streaming integration with ONNX Runtime token generation (C#). | 16 hours | First token < 2 sec; smooth streaming |
| 10.3 | Full pipeline integration with dialect reconstruction (C#). | 24 hours | Correct Arabic outputs for MSA + Egyptian |
| 10.4 | `/v1/chat/completions` SSE endpoint finalization (C#). | 16 hours | OpenAI-compatible streaming |
| 10.5 | `/v1/embeddings` endpoint (C#). | 8 hours | 768-dim embeddings |
| 10.6 | Dialect API endpoints: detect, reconstruct, translate, etymology (C#). | 16 hours | All functional |
| 10.7 | Remaining API endpoints (C#). | 16 hours | All functional |
| 10.8 | Deployment mode switching (C#). | 8 hours | Memory within budget |
| 10.9 | KV cache quantization for 32K (C#). | 8 hours | 32K fits in 16 GB |
| 10.10 | Integration testing (C#). | 16 hours | All pass |
| 10.11 | Performance testing on T1000 + CPU. | 8 hours | Targets met |
| 10.12 | Security testing. | 8 hours | No vulnerabilities |
| 10.13 | Docker image. | 8 hours | Starts in < 30 sec |

**Gate:** Full system functional. All APIs work including SSE streaming. Performance targets met. Dialect endpoints operational.

### Epic 11: Evaluation and Publication (Weeks 26-30)

*Depends on: Epic 10 (complete system)*

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 11.1 | Full benchmark suite (all quantization levels, MSA + dialect). | 24 hours | Results documented |
| 11.2 | Blind test set evaluation. | 16 hours | Meets acceptance criteria |
| 11.3 | Evaluation report. | 24 hours | Complete and reviewed |
| 11.4 | Paper draft. | 40 hours | Ready for submission |
| 11.5 | User documentation (MSA + dialect features). | 24 hours | Complete |
| 11.6 | Developer documentation. | 16 hours | Complete |
| 11.7 | Security audit. | 16 hours | No critical vulnerabilities |
| 11.8 | Dialect pipeline documentation and re-run instructions. | 8 hours | Complete |

---

## 20. Religious and Trust Guardrails

### 20.1 Citation-First Answering

For Quran, Hadith, Tafsir, and fiqh-adjacent queries:

- All answers must be grounded in retrieved sources
- Answers must cite the specific source (Surah:Ayah for Quran, narrator chain for Hadith, scholar for Tafsir)
- The system must explicitly state when its corpus does not cover the queried topic
- Template responses for religious content must include source citations by default
- Dialect queries about religious topics must still cite canonical sources; dialect reconstruction is used for understanding but citations reference canonical texts

### 20.2 Disputed Interpretation Handling

- When multiple Tafsir sources offer different interpretations, present all sourced views
- Never present one interpretation as the only correct view without attribution
- Flag questions about fiqh rulings as "requires qualified scholar guidance"
- Never issue fatwa-style responses

### 20.3 Non-Fatwa Guardrails

- Detect queries seeking religious rulings (فتوى, حكم, هل يجوز, and dialect equivalents like حكم ايه, يجوز ولا لأ)
- Respond with: "This question requires a qualified scholar. I can provide related Quranic verses, Hadith references, and scholarly opinions for your reference."
- Provide sourced references without ruling

### 20.4 Dialect and Religious Content

- Dialect reconstruction applies to understanding the user's query, not to the religious source text
- Quranic verses are always quoted in their canonical form regardless of the user's dialect
- Hadith references are always in their canonical Arabic form
- Explanations and paraphrases may be in the user's dialect if `dialect_match=true`
- The system never rephrases a Quranic verse into dialect

---

## 21. Backlog Expansion Epics (Post-Baseline)

### Backlog Epic A: Religious Answer Safety Layer

| Task | Description | Prerequisite |
|---|---|---|
| A.1 | Citation-first answering for all religious queries | Baseline RAG |
| A.2 | Disputed-interpretation detection and multi-view presentation | Neo4j with Tafsir |
| A.3 | Non-fatwa guardrail: detect ruling-seeking queries (MSA + dialect) | Intent classifier + dialect detection |
| A.4 | Source-confidence scoring for Hadith (sahih/hasan/da'if) | Hadith metadata |
| A.5 | Quranic verse verification against canonical Tanzil | Tanzil (done) |

### Backlog Epic B: Customer Support Agent Layer

| Task | Description | Prerequisite |
|---|---|---|
| B.1 | Intent routing for Arabic customer support queries (MSA + dialect) | Dialect + intent classifier |
| B.2 | Citation traceability in agent responses | RAG + source metadata |
| B.3 | Safety and escalation rules for sensitive topics | Religious safety layer |
| B.4 | Conversation memory (short-term session context) | API + session management |
| B.5 | Answer templates for common support scenarios | Template system (built) |
| B.6 | Dialect-matched responses for customer support | Dialect translator (built) |

### Backlog Epic C: Source Quality and Coverage Expansion

| Task | Description | Prerequisite |
|---|---|---|
| C.1 | Tafsir breadth expansion (more scholars) | Baseline graph |
| C.2 | Hadith source-quality ranking | Hadith metadata |
| C.3 | Provenance scoring for all ingested sources | Data lineage |
| C.4 | Coverage-gap reporting | Graph + QA dashboard |
| C.5 | Levantine and Gulf dialect translation | Dialect detection + alignment pipeline |
| C.6 | Extend dialect alignment pipeline to Levantine/Gulf | MADAR data for 28 cities |

### Backlog Epic D: Dialect Pipeline Expansion

| Task | Description | Prerequisite |
|---|---|---|
| D.1 | Extend etymological map to Levantine Arabic | MADAR Levantine data + scraping |
| D.2 | Extend etymological map to Gulf Arabic | MADAR Gulf data + scraping |
| D.3 | Train dialect-specific reconstruction engines | Sufficient parallel corpus per dialect |
| D.4 | Continuous dialect scraping pipeline for vocabulary evolution | C# scraper + quality gates |
| D.5 | Dialect code-switching detection (MSA↔dialect within same utterance) | Detection model retraining |

---

## 22. Dataset and Tooling Reference

### Data Sources — Complete Catalog

| Resource | Category | Role | Access |
|---|---|---|---|
| **Shamela.ws — Dictionaries** | | | |
| Al-Qamus Al-Muhit | Dictionary | Classical dictionary; root-based lexical entries | https://shamela.ws/book/7283 |
| Mukhtar Al-Sihah | Dictionary | Compact dictionary; root-based reference | https://shamela.ws/book/23193 |
| Tag Al-Aroos | Dictionary | Comprehensive dictionary; encyclopedic coverage | https://shamela.ws/book/7030 |
| Al-Waseet | Dictionary | Modern Arabic dictionary (Arabic Language Academy) | https://shamela.ws/book/7028 |
| Al-Ain | Dictionary | Earliest Arabic dictionary (Al-Khalil ibn Ahmad) | https://shamela.ws/book/1682 |
| Taimoor | Dictionary (dialect) | **Contains dialects and slang**; critical for dialect etymology | https://shamela.ws/book/150964 |
| **Shamela.ws — Grammar** | | | |
| Alfiyyat Ibn Malik | Grammar | 1,002 grammatical rules in verse | https://shamela.ws/book/356 |
| Qatr Al-Nada | Grammar | Grammar reference by Ibn Hisham | https://shamela.ws/book/11376 |
| Shudhur Al-Dhahab | Grammar | Ibn Hisham's syntax commentary | https://shamela.ws/book/6969 |
| Al-Kitab (Sibawayh) | Grammar | Foundational Arabic grammar treatise | https://shamela.ws/book/23018 |
| **Other Linguistic** | | | |
| Lisan Al-Arab | Dictionary | Most comprehensive classical Arabic lexicon | https://www.almaany.com/ |
| Quranic Arabic Corpus | Annotations | Morphological + syntactic annotations | https://corpus.quran.com/ |
| ConceptNet | Semantic edges | Synonym/antonym/related concepts | https://conceptnet.io/ |
| Arabic WordNet | Lexical | Synonym/antonym networks | https://globalwordnet.github.io/gwn/ |
| PADT | Syntax | Syntax treebank training | https://lindat.mff.cuni.cz/ |
| **Religious** | | | |
| Tanzil | Quran | Verified Quran text | https://tanzil.net/download/ |
| Sunnah.com | Hadith | 15+ Hadith books (Bukhari, Muslim, Tirmidhi, Abu Dawud, Nasa'i, Ibn Majah, Malik, etc.) | https://sunnah.com/ |
| **General Arabic Corpus** | | | |
| OSCAR Arabic | General | Web-crawled Arabic (filtered) | https://oscar-project.org/ |
| Arabic Wikipedia | General | Encyclopedic + taxonomy | https://dumps.wikimedia.org/ |
| CC-100 Arabic | General | Common Crawl filtered | https://data.statmt.org/cc-100/ |
| Hindawi | Literature | Arabic literature | https://www.hindawi.org/ |
| OPUS | Parallel | Parallel corpus | https://opus.nlpl.eu/ |
| **Dialect** | | | |
| MADAR-28 | Dialect | Parallel corpus: 28 cities + MSA | https://camel.abudhabi.nyu.edu/madar/ |
| ARB-EGY-CMP | Dialect (Twitter) | Egyptian-MSA parallel; **Twitter comments and tweets** | Already integrated |
| Nofal dataset | Dialect (Twitter) | Egyptian slang; **Twitter comments and tweets** | Already integrated |
| OpenSubtitles Arabic | Dialect | Movie/TV subtitles with dialect mixing | https://opus.nlpl.eu/OpenSubtitles.php |
| **Model Resources** | | | |
| Jais-1.3B | Teacher | Primary teacher model | https://huggingface.co/inceptionai/jais-1p3b |
| Qwen2-1.5B | Teacher | Auxiliary teacher | https://huggingface.co/Qwen/Qwen2-1.5B |
| Arabert | Embedding | Embedding model for fine-tuning | https://huggingface.co/aubmindlab/bert-base-arabertv02 |
| XNLI Arabic | NLI | NLI training data | https://huggingface.co/datasets/xnli |
| Free LLM API Keys | Cloud | Cloud teacher + dialect generation | https://github.com/alistaitsacle/free-llm-api-keys |

### .NET Tooling

| Library | NuGet Package | Role |
|---|---|---|
| Microsoft.ML.Tokenizers | v2.0.0+ | BPE tokenizer training and inference |
| Microsoft.ML.OnnxRuntime | v1.26.0 | Model inference |
| Microsoft.ML.OnnxRuntime.Gpu | v1.26.0 | GPU inference on T1000 |
| TorchSharp | v0.107.0 | Model prototyping, small model training, validation |
| TorchSharp-cuda-linux/windows | v0.107.0 | CUDA 12.8 support |
| FaissNet | v1.1.0 | Vector search (FAISS C++ wrapper) |
| HNSW.Net | v26.6+ | Pure managed HNSW vector search |
| Neo4j.Driver | v5.x | Graph database access |
| Panlingo.LanguageIdentification.FastText | v0.7.2 | Language identification |
| MinHashSharp | latest | MinHash signature computation |
| Microsoft.Data.Sqlite | v8.x | SQLite persistence + dialect etymology storage |
| Microsoft.EntityFrameworkCore.Sqlite | v8.x | ORM for corpus state |

### Python Tooling (One-Time / Sidecar)

| Tool | Role | When Used |
|---|---|---|
| PyTorch + DeepSpeed | 458M model training | Epic 8 |
| fast_align / EFmarAlign | Word alignment for dialect etymology | Epic 5 |
| optimum-cli | ONNX export for embedding model | Epic 7 |
| Arabert fine-tuning scripts | Embedding model fine-tuning | Epic 7 |

---

## 23. Final Execution Rules

1. This document is the sole planning baseline.
2. No task begins until its prerequisite Epic's gate is passed.
3. No deviation from quantization quality gates.
4. All parameter counts and memory budgets verified from first principles.
5. **.NET-first stack:** use .NET/C# for everything feasible. Python is used ONLY for: (a) 458M model training via PyTorch, (b) one-time embedding model fine-tuning, and (c) one-time word alignment for dialect etymology. All other components run in .NET.
6. TorchSharp is the .NET-native model tool for architecture definition, validation, and small model training. The 458M model training uses PyTorch because TorchSharp lacks AMP, gradient checkpointing, CPU optimizer offloading, and flash attention — all required to fit 458M on T1000 4GB.
7. Training orchestration is .NET-driven via sidecar pattern.
8. Every checkpoint must pass automated regression tests before promotion.
9. Ollama serves the teacher model on CPU, keeping T1000 GPU free for student training.
10. Cloud API keys provide fallback teacher and evaluation assistance when needed.
11. **SSE streaming is the primary chat interface.**
12. **Dialect knowledge is trained, scraped, and AI-generated — never manually curated.**
13. **Dialect reconstruction is best-effort.** If the pipeline cannot reconstruct a dialect word to MSA, the system falls back gracefully.
14. **Shamela.ws is the primary source for classical Arabic texts.** Do not use "Digitized PDF/OCR" when structured text is available from Shamela.
15. **Implementation follows the dependency chain** (Section 19). No epic starts before its prerequisite epics' gates are passed. The only permitted parallelism is: Epic 8 (model training) can overlap with Epics 5-7 since it depends only on Epic 4 (tokenizer + morphology).

---

## Appendix A: Self-Review and Validation

### Parameter Count Verification

- Token embedding: 32,768 x 1,536 = 50,331,648
- Root embedding: 6,000 x 256 = 1,536,000
- Pattern embedding: 1,200 x 128 = 153,600
- POS embedding: 50 x 64 = 3,200
- Morph projection: 1,984 x 1,536 + 1,536 = 3,048,960
- Per layer: 25,168,896
- 16 layers: 402,702,336
- Final RMSNorm: 1,536
- **Total: 457,777,280** ✓

### Memory Budget Verification

- INT4 weight: 457,777,280 x 4 bits / 8 ≈ 229 MB ✓
- 2-bit ternary: ≈ 114 MB + 14 MB scales = 128 MB ✓
- KV 4K FP16: 128 MB ✓
- KV 32K INT8: 512 MB ✓
- Dialect subsystem: 15-25 MB ✓

### Training Memory Verification — T1000 4GB

- GPU: 916 + 200 + 150 = 1,266 MB. **Fits in 4 GB.** ✓
- CPU: 7,712 MB. **Fits in 64 GB.** ✓

### Training Time Verification — T1000

- Conservative with interruptions: **36-45 days** ✓ (fits within Week 12-20 window)

### .NET Coverage Verification

19 of 22 components are .NET/C# (86%). The 3 Python components are: (1) 458M training, (2) one-time embedding fine-tuning, and (3) one-time word alignment. ✓

### Data Source Coverage Verification

- Dictionaries: 7 sources (6 Shamela + Lisan Al-Arab) ✓
- Grammar: 4 Shamela sources (Alfiyyat, Qatr, Shudhur, Sibawayh) ✓
- Dialect dictionary: Taimoor (contains dialects + slang) ✓
- Hadith: 15+ books from Sunnah.com ✓
- Twitter/dialect data: ARB-EGY-CMP + Nofal (both Twitter-based) ✓
- No "Digitized PDF/OCR" references for texts available on Shamela ✓

### Dialect Pipeline Data-Driven Verification

- Etymological root map source: fast_align on parallel corpus ✓
- AI gap-filling source: teacher model ✓
- Syntactic reordering source: dependency structure comparison ✓
- No manual dictionary entries required ✓
- Pipeline is re-runnable ✓

### Dependency Chain Verification

```
Epic 1 (Toolchain) → Epic 2 (Data) → Epic 3 (Graph) → Epic 4 (Morph+Tokenizer)
    → Epic 5 (Dialect Pipeline) → Epic 6 (NLP) → Epic 7 (RAG+SSE)
    → Epic 10 (Runtime)
Epic 4 also enables → Epic 8 (Training) → Epic 9 (Quantization) → Epic 10
Epic 11 (Evaluation) depends on Epic 10
```

No circular dependencies. Each epic's gate must be passed before dependents start. ✓

### Timeline Verification

- 11 Epics over 30 weeks
- Critical path: Toolchain → Data → Graph → Morph → Dialect → NLP → RAG → Runtime → Evaluation
- Training runs Weeks 12-20 (parallel with Epics 5-7)
- Total: 30 weeks (expanded from 28 due to explicit graph seeding epic + correct data source handling)
- No manual curation dependencies ✓

---

## Appendix B: Dialect Reconstruction — Example Traces

### Example 1: "عايز ارجع المنتج ده"

```
Input: "عايز ارجع المنتج ده"

Step 1: Dialect Detection → Egyptian (94%)

Step 2: Per-Token Etymological Analysis
  عايز → Etym map HIT (conf 0.92): root ع-و-ز, pattern فاعل, MSA أريد (root أ-ر-د)
  ارجع → MSA dict HIT: root ر-ج-ع, Form IV
  المنتج → MSA dict HIT: root ن-ت-ج, passive participle
  ده → Etym map HIT (conf 0.96): demonstrative ← "هذا", POST-posed

Step 3: Syntactic Reordering
  "المنتج ده" → "هذا المنتج"

Step 4: MSA Reconstruction: "أريد إرجاع هذا المنتج"

Step 5: Dual Retrieval → roots {ع-و-ز, أ-ر-د, ح-و-ج, ر-ج-ع, ن-ت-ج}

Step 6: Model Generation (SSE, dialect-matched)
  "لو عايز ترجع المنتج ده، ممكن تعمل الآتي..."
```

### Example 2: "علشان مش نافع معايا"

```
Input: "علشان مش نافع معايا"

Step 1: Dialect Detection → Egyptian (96%)

Step 2: Etymological Analysis
  علشان → Etym map: compound على+شان → MSA لأن/لكي
  مش → Etym map: reduced ما+ش → MSA ليس/لا
  نافع → MSA dict: root ن-ف-ع → MSA مفيد
  معايا → Etym map: مع+ي(ـا) → MSA معي

Step 3: Syntactic Reordering
  "مش نافع" → "ليس مفيداً"

Step 4: MSA Reconstruction: "لأنه ليس مفيداً معي"

Step 5: Dual Retrieval → roots {ن-ف-ع, م-ع}

Step 6: Model Generation (SSE, dialect-matched)
  "فاهمك — لو المنتج مش نافع معاك، ممكن ترجعه..."
```

### Example 3: "كده مش هروح الشغل النهاردة"

```
Input: "كده مش هروح الشغل النهاردة"

Step 1: Dialect Detection → Egyptian (98%)

Step 2: Etymological Analysis
  كده → Etym map: reduction of هكذا → MSA هكذا
  مش → Etym map: negation → MSA لن
  هروح → Etym map: هـ(future) + ر-و-ح → MSA سأذهب (root shift: ر-و-ح ≠ ذ-ه-ب)
  الشغل → MSA dict: root ش-غ-ل → MSA العمل
  النهاردة → Etym map: النهار+ة → MSA اليوم

Step 3: Syntactic Reordering: future prefix هـ → سـ

Step 4: MSA Reconstruction: "هكذا لن أذهب إلى العمل اليوم"

Step 5: Dual Retrieval → roots {ر-و-ح, ذ-ه-ب, ش-غ-ل}

Step 6: Model Generation (SSE, dialect-matched)
  "يعني مش هتروح الشغل النهاردة؟ خذ راحتك..."
```

### Example 4: "ما ينفعش أكله ده"

```
Input: "ما ينفعش أكله ده"

Step 1: Dialect Detection → Egyptian (93%)

Step 2: Etymological Analysis
  ما → circumfix negation opener
  ينفعش → ي-ن-ف-ع + ش → root ن-ف-ع, MSA لا ينفع
  أكله → أكل + ه → root أ-ك-ل
  ده → Etym map: demonstrative (POST-posed) ← هذا

Step 3: Syntactic Reordering
  "ما ينفعش" → "لا ينفع"
  "أكله ده" → "هذا أكله"

Step 4: MSA Reconstruction: "لا يصلح أكله هذا"

Step 5: Dual Retrieval → roots {ن-ف-ع, أ-ك-ل}

Step 6: Model Generation (SSE, dialect-matched)
  "فعلاً ده مش كويس للأكل — ممكن ترجعه..."
```

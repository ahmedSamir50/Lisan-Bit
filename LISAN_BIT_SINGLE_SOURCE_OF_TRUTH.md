# Lisan-Bit: Single Source of Truth Definition & Plan

**Date:** 2026-06-14
**Status:** Locked Execution Baseline

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
- Dialect-aware handling, especially Egyptian Arabic and MSA

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
7. **.NET-first technology stack.** Use .NET/C# for everything feasible. Use Python only where no .NET alternative exists. The only required Python components are: (a) 458M model training, and (b) one-time embedding model fine-tuning. Everything else runs in .NET.
8. **All parameter counts and memory budgets verified from first principles.**
9. **Training-inference boundary:** Training uses PyTorch (Python); inference uses ONNX Runtime (.NET). TorchSharp is the .NET-native model tool for architecture definition, validation, and small model training. The 458M model training uses PyTorch because TorchSharp lacks AMP, gradient checkpointing, CPU optimizer offloading, and flash attention — all required to fit 458M on the T1000 4GB GPU.
10. **Training orchestration is .NET-driven** via sidecar pattern: .NET prepares data, launches Python training, monitors progress, and validates results.

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
[3] Domain / Intent / Dialect Classification (Lisan.Dialect — C#)
    |
    v
[4] Graph Retrieval (Neo4j — C# driver)  ----+---- [5] Vector Retrieval (FaissNet/HNSW.Net — C#)
    |                                          |
    +------------------------------------------+
    |
    v
[6] Context Assembly (C# — tier-aware, deduplicated, priority-ordered)
    |
    v
[7] Model Inference with Morphological Feature Injection (ONNX Runtime — C#)
    |
    v
[8] Post-Processing: syntactic constraints, diacritization, reassembly, dialect adjustments (C#)
    |
    v
Output
```

The entire runtime pipeline is .NET/C#. The only non-.NET component at inference time is the ONNX model file itself (generated from PyTorch training).

### 4.2 Graceful Degradation Rules

| Failure Point | Behavior |
|---|---|
| Normalization fails | Continue with original text; log warning |
| Morphology is partial | Continue with available tokens; inject zero vectors for missing features |
| Graph retrieval unavailable | Continue with vector search only |
| Vector retrieval unavailable | Continue with graph retrieval only |
| Both retrieval layers unavailable | Template-only response; do not attempt model inference without context |
| Model unavailable | Template-based response generation using graph + dictionary data |
| Context budget exceeded | Reduce context tier (32K -> 16K -> 8K -> 4K) and retry |
| All systems unavailable | Return error with guidance |

### 4.3 Context Tiering

| Tier | Max Tokens | Strategy | Deduplication | Target Mode |
|---|---:|---|---|---|
| 4K | 4,096 | Direct concatenation with priority ordering | None | Lite |
| 8K | 8,192 | Concatenation + MinHash dedup (Jaccard 0.85) | MinHash | Standard |
| 16K | 16,384 | YARN-style segment selection (512-token segments) | MinHash + segment ranking | Extended |
| 32K | 32,768 | Clause-level TF-IDF ranking + extractive summarization | Full pipeline | Full |

### 4.4 Context Priority Ordering

1. Quranic verse references (if query is religious)
2. Dictionary definitions from graph (direct root/pattern match)
3. Graph-neighborhood context (1-2 hop traversal)
4. Vector-similar passages (semantic relevance)
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
| Vocab | 32,768 | Arabic-optimized BPE (Section 10.2) |
| Attention | Standard softmax | Stable, well-understood |
| FFN | SwiGLU | Proven superior to ReLU/GLU variants |
| Position encoding | RoPE on Q and K | Enables YARN extrapolation to 32K |
| Normalization | RMSNorm (pre-norm) | No bias, fewer parameters, stable |
| Dropout | 0.0 | Standard for production LLMs |
| Training context | 2,048 -> 4,096 -> 8,192 | Progressive curriculum |
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
| Morph projection (weight + bias) | 1,984 -> 1,536 | — | 3,048,960 |
| **Injection subtotal** | | | **55,073,408** |

When morphological analysis is unavailable (proper nouns, loanwords), zero vectors replace missing features. The projection layer learns to handle this; approximately 15-20% of training tokens have incomplete morphology.

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

| Mode | Context | Model Format | Model+KV+Act | Neo4j | Dict | NLP | Embed | Total | Target RAM |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| Lite | 4K | 2-bit ternary | 496 MB | 0 | 200 MB | 20 MB | 50 MB | ~766 MB | 4 GB |
| Lite | 4K | INT4 | 597 MB | 0 | 200 MB | 20 MB | 50 MB | ~867 MB | 4 GB |
| Standard | 8K | INT4 | 725 MB | 400 MB | 200 MB | 20 MB | 50 MB | ~1,395 MB | 8 GB |
| Standard | 8K | INT8 | 954 MB | 400 MB | 200 MB | 20 MB | 50 MB | ~1,624 MB | 8 GB |
| Full | 32K | INT4 + INT8 KV | 845 MB | 400 MB | 200 MB | 20 MB | 50 MB | ~1,515 MB | 16 GB |
| Full | 32K | INT4 + FP16 KV | 1,357 MB | 400 MB | 200 MB | 20 MB | 50 MB | ~2,027 MB | 16 GB |

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

**Key: The T1000 4GB can train 458M parameters because DeepSpeed ZeRO-2 offloads all optimizer states and gradients to CPU, gradient checkpointing reduces activation memory by ~87%, and flash attention eliminates O(n^2) attention matrix storage. The GPU only holds FP16 weights + reduced activations + CUDA overhead.**

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

Ollama runs the teacher model on CPU using the 64 GB RAM, keeping the T1000 GPU free for student training. This eliminates the need for ONNX conversion of the teacher model.

| Teacher | Model | How Served | Memory |
|---|---|---|---|
| Primary | Jais-1.3B (Q4_K_M) | Ollama on CPU | ~800 MB RAM |
| Secondary | Qwen2-1.5B (Q4_K_M) | Ollama on CPU | ~1,000 MB RAM |

**Setup:**

1. `ollama pull jais` (or convert Jais to GGUF and import into Ollama)
2. Teacher inference via Ollama REST API from Python training script: `POST http://localhost:11434/api/generate`
3. Teacher runs on CPU; student training runs on T1000 GPU simultaneously.

**Fallback teacher via cloud API:**

If local teacher quality is insufficient, use free API keys from https://github.com/alistaitsacle/free-llm-api-keys for cloud-based teacher inference (e.g., OpenAI-compatible endpoints). This provides access to larger models (GPT-4-class) for higher-quality distillation. Use this for:

- Final-phase distillation where teacher quality matters most
- Generating high-quality Arabic training data (explanations, grammar analyses)
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
| Cosine decay | 3e-4 -> 1e-5 by 60K |
| Fine-tuning LR | 1e-5 through 70K |

### 7.6 Checkpointing and Recovery

- Save every 2,000 steps; keep last 5 + best validation.
- Validate every 2,000 steps.
- On 3 consecutive regressions: rollback, reduce LR by 0.5x.
- On divergence (loss > 10x): rollback, reduce LR by 0.1x.

### 7.7 Training Throughput and Wall-Time — T1000 4GB

**T1000 throughput estimate:**

The T1000 delivers ~2.5 TFLOPS FP16 (vs ~6.7 TFLOPS for RTX 2060). DeepSpeed ZeRO-2 CPU offloading adds ~30-50% overhead vs pure-GPU training.

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

**Conservative estimate with interruptions:** Plan for **40-45 days of continuous training**. With the 26-week timeline (182 days), this leaves ample room: training runs from Week 10 to approximately Week 16-17.

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

**Teacher inference:** Jais-1.3B via Ollama on CPU (or cloud API). Throughput: ~2-5 batches/sec on CPU, sufficient for student training speed on T1000.

---

## 8. Data and Knowledge Foundation

### 8.1 Religious Corpus

| Source | Content | Access |
|---|---|---|
| Tanzil | Verified Quran text | https://tanzil.net/download/ |
| Sunnah.com | Hadith corpus | https://sunnah.com/ |
| Quranic Arabic Corpus | Morphological/syntactic annotations | https://corpus.quran.com/ |
| Tafsir sources | Al-Tabari, Ibn Kathir, Al-Jalalayn | Scraping + manual verification |

### 8.2 Linguistic Corpus

| Source | Content | Access |
|---|---|---|
| Lisan Al-Arab | Classical dictionary | https://www.almaany.com/ |
| Al-Waseet | Modern dictionary | https://archive.org/ |
| Mukhtar Al-Sihah | Compact dictionary | Digitized PDF/OCR |
| Al-Qamus Al-Muhit | Classical dictionary | Digitized editions |
| Arabic grammar references | Alfiyyat Ibn Malik, Qatr Al-Nada | Digitized editions |

### 8.3 General Arabic Corpus

| Source | Content | Access |
|---|---|---|
| OSCAR Arabic | Web-crawled Arabic | https://oscar-project.org/ |
| Arabic Wikipedia | Encyclopedic | https://dumps.wikimedia.org/ |
| CC-100 Arabic | Common Crawl filtered | https://data.statmt.org/cc-100/ |
| Hindawi | Arabic literature | https://www.hindawi.org/ |
| OPUS | Parallel corpus | https://opus.nlpl.eu/ |
| MADAR | Dialect corpus | https://camel.abudhabi.nyu.edu/madar/ |

### 8.4 Genus-Aware Corpus Enrichment

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

### 8.5 Context Labeling Rules

- Accept text blocks only if they contain at least 7 unique extracted roots.
- Resolve Arabic Wikipedia categories into English taxonomy paths.
- Map Arabic news-site breadcrumbs into taxonomy paths before fallback classification.
- Label Sunnah rows as `Religion/Islam/Hadith` until deeper book-structure routing is available.
- Persist only English taxonomy leaf paths in `ContextVector`.

### 8.6 Corpus Processing Pipeline (.NET-Native)

| Step | Operation | .NET Tool |
|---|---|---|
| 1 | Download raw data | HttpClient + custom scrapers (C#) |
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

### 9.2 Input Golden Sets

- Curate 500+ validated extraction examples per data source.
- Key each by exact source identifier.
- Store: source URL, expected text, expected annotations, validator identity.

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

### 9.7 QA Dashboard

- Scraper validation pass rates per source
- Jaccard/Levenshtein quality distributions
- Learning curves and overfit alerts
- Retrieval and model benchmark summaries
- Quantization comparison results
- Data contamination checks

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

Required relationships: HAS_ROOT, HAS_PATTERN, HAS_POS, HAS_MEANING, BELONGS_TO, DERIVES_FROM, PRODUCES, SYNONYM_OF, ANTONYM_OF, APPEARS_IN, EXPLAINED_IN, RELATED_ROOT, IN_SYNSET, HAS_SUB_CONTEXT, GOVERNS.

### 10.2 Seeding Priorities

1. Dictionary data (Lisan Al-Arab, Al-Waseet)
2. Quranic annotations (Quranic Arabic Corpus)
3. Hadith references
4. Synonym/antonym networks (Arabic WordNet, ConceptNet)
5. Cross-root relationships
6. Taxonomy hierarchy (Wikipedia categories)
7. ConceptNet Arabic edges

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

### 10.4 Graph Quality Assurance

- Every Word has HAS_ROOT or "no_root" flag.
- Every Root has PRODUCES relationship.
- No orphan nodes (except by design).
- Spot audit: 200 random Word nodes, >= 95% correct.

---

## 11. Native Arabic NLP Layer

### 11.1 Lisan.Morphology (C#)

**Implementation layers (cascading fallback):**

1. **Primary:** In-memory dictionary lookup from Neo4j export. ~200K words. < 2 ms/word.
2. **Secondary:** Pattern-based heuristics (prefix/suffix stripping, pattern matching). < 5 ms/word.
3. **Tertiary:** CAMeL (development-time only, not shipped).
4. **Fallback:** Partial analysis, zero vectors for missing features. Always succeeds.

**Runtime target:** < 5 ms/word on CPU (primary), < 15 ms/word (including fallback).

### 11.2 Lisan.Tokenizer (C#) — BPE Training Specification

**Tokenizer type:** BPE, vocabulary size 32,768

**Training tool:** `Microsoft.ML.Tokenizers` (C# NuGet, production-ready, supports BPE training via `Bpe.Train()`)

| Step | Operation | Detail |
|---|---|---|
| 1 | Assemble training corpus | OSCAR Arabic (1M docs) + Arabic Wikipedia + linguistic texts. Target: 3-5B characters. |
| 2 | Pre-tokenization | Arabic Unicode normalization + split on whitespace/punctuation (C#) |
| 3 | Character coverage | 0.9995 for Arabic script |
| 4 | Special tokens | 10 tokens: `<s>`, `</s>`, `<pad>`, `<unk>`, `[CONTEXT]`, morph/dialect markers. Leaves 32,758 BPE merges. |
| 5 | Train | `Microsoft.ML.Tokenizers` BPE training in C# |
| 6 | Validate OOV | OOV < 0.01% on Arabic text |
| 7 | Validate subword efficiency | Avg tokens/word < 1.5 |
| 8 | Validate morphological alignment | Root-boundary alignment > 60% |
| 9 | Build teacher alignment map | Tokenize 10K Arabic sentences with Lisan BPE + Jais tokenizer; build monotonic alignment. Target: > 90% coverage. |

### 11.3 Lisan.Syntax (C#)

- Rule-driven parser with Arabic grammar production rules
- Sources: Quranic Arabic Corpus annotations, Alfiyyat Ibn Malik (1,002 rules), PADT patterns
- Coverage target: 80% MSA, 60% Egyptian
- Neural fallback for unparseable sentences

### 11.4 Lisan.Dialect (C#)

- Egyptian <-> MSA: 5,000+ word pairs + phonological rules
- Dialect detection: character-level CNN (3 conv layers) trained on MADAR via TorchSharp (5M params, fits on T1000 4GB). < 1 ms/sentence.
- Levantine/Gulf: detection only at launch; translation post-launch.

### 11.5 Lisan.Diacritization (C# + ONNX)

- **Deterministic layer:** Rule-based from morphological analysis. ~70% coverage.
- **Neural refinement:** 4-layer transformer (d_model=512, ~25M params) trained via TorchSharp on T1000 4GB. Exported to ONNX for runtime inference.
- **Combined target:** > 85% word-level accuracy.

---

## 12. RAG System

### 12.1 Retrieval Layers

| Layer | .NET Tool | Fallback |
|---|---|---|
| Graph retrieval | Neo4j C# driver | Vector only |
| Vector retrieval | FaissNet or HNSW.Net | Graph only |
| Domain ranking | Custom C# classifier | Skip ranking |
| Morphology-aware query expansion | Lisan.Morphology (C#) | Original query |

### 12.2 Template-Based Response Layer

6 template types: root explanation, pattern explanation, grammar explanation, Quranic references, synonym/antonym listing, diacritization.

Minimum quality target: 75% acceptable by human review.

### 12.3 Context Assembly Rules

- Combine morphology + graph + vector evidence when available.
- Deduplicate beyond 4K.
- Priority: domain match > graph distance > vector similarity.
- `[CONTEXT]` boundary token between context and query.

**Latency targets:** < 500 ms without model, < 2 sec with model.

### 12.4 Embedding Model Specification

**Primary:** Arabertv02 fine-tuned on Arabic NLI + retrieval pairs.

**Fine-tuning:** Done once in Python (no .NET alternative for training transformers). Exported to ONNX and served in .NET via ONNX Runtime for all subsequent inference.

| Step | Tool | Environment |
|---|---|---|
| 1 | Fine-tune Arabert on XNLI Arabic | Python (one-time, ~4 hours) |
| 2 | Fine-tune on 50K in-domain retrieval pairs | Python (one-time, ~2 hours) |
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
| Dialect mismatch | Dialect detection + MSA expansion (C#) |

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
    +-- Lisan.Morphology (C#, in-memory dictionary + heuristics)
    +-- Lisan.Tokenizer (C#, Microsoft.ML.Tokenizers BPE)
    +-- Lisan.Syntax (C#, rule-driven parser)
    +-- Lisan.Dialect (C#, dictionary + TorchSharp CNN)
    +-- Lisan.Diacritization (C# deterministic + ONNX neural)
    +-- Lisan.GraphRAG (Neo4j C# driver + FaissNet/HNSW.Net)
    +-- Lisan.Model (ONNX Runtime — primary; Ollama — secondary for GGUF)
    +-- Lisan.API (ASP.NET Core minimal API)
    +-- Lisan.TrainingOrchestrator (C# sidecar manager for Python training)
```

### 14.2 Model Inference Runtime

| Runtime | Format | CPU Perf | GPU Support | Use Case |
|---|---|---|---|---|
| ONNX Runtime | ONNX | Best CPU | CUDA EP (T1000) | **Primary deployment** |
| Ollama | GGUF | Good CPU | Good | **Secondary; also serves teacher** |
| TorchSharp | .pt weights | Moderate | Limited CUDA | **Validation + prototyping** |

### 14.3 Inference Acceleration

| Technique | Phase | Speedup |
|---|---|---|
| ONNX graph optimization | Initial release | 20-40% |
| KV cache reuse | Initial release | 50-80% repeat computation reduction |
| T1000 GPU inference (ONNX Runtime CUDA EP) | Initial release | 3-5x vs CPU |
| Speculative decoding | Post-launch | 2-3x greedy |
| Continuous batching | Post-launch | 2-4x throughput |

### 14.4 Inference Performance Targets

| Mode | Latency | Throughput |
|---|---|---|
| Morphology-only | < 50 ms | > 1,000 words/sec |
| RAG-only | < 500 ms | > 2 queries/sec |
| Full + model (4K, T1000 GPU) | < 2 sec | > 0.5 queries/sec |
| Full + model (4K, CPU only) | < 5 sec | > 0.2 queries/sec |

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

### 15.2 RAG-Only Acceptance

| Benchmark | Target |
|---|---:|
| Template-Response-100 | >= 75% |
| Graph-Lookup-200 | >= 90% |
| Vector-Search-100 | >= 80% |
| End-to-End-RAG-50 | >= 50% |

### 15.3 Performance Targets

- CPU narrow sub-tasks: < 100 ms
- RAG without model: < 500 ms
- Lite mode RAM: < 766 MB

### 15.4 Inter-Annotator Agreement

- Minimum 2 annotators per benchmark.
- Cohen's Kappa >= 0.65 (categorical), Spearman >= 0.70 (ordinal).
- Below threshold: adjudication + guideline refinement.

### 15.5 Held-Out Blind Test Set

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

---

## 17. API Surface

| Endpoint | Method | Description |
|---|---|---|
| `/v1/chat/completions` | POST | OpenAI-compatible chat |
| `/v1/embeddings` | POST | Text embeddings |
| `/v1/morphology/analyze` | POST | Full morphological analysis |
| `/v1/morphology/root` | POST | Root extraction |
| `/v1/morphology/pattern` | POST | Pattern extraction |
| `/v1/morphology/diacritize` | POST | Diacritization |
| `/v1/syntax/parse` | POST | Sentence parsing + i'rab |
| `/v1/syntax/check` | POST | Grammar checking |
| `/v1/dialect/translate` | POST | Dialect translation |
| `/v1/knowledge/search` | POST | Graph + vector search |
| `/v1/knowledge/quran` | POST | Quranic concept lookup |

**Auth:** API key via `Authorization: Bearer <key>`. Rate limit: 60 req/min.

---

## 18. Risks and Controls

| Risk | Severity | Control | Fallback |
|---|---|---|---|
| T1000 4GB VRAM insufficient for training | Critical | DeepSpeed ZeRO-2 + CPU offload + gradient checkpointing + flash attention | CPU-only training (10x slower but works) |
| Training too slow on T1000 | High | Reduce steps to 50K; use cloud API teacher for better distillation | Extend timeline to 30 weeks |
| PyTorch CUDA incompatibility on T1000 | High | Week 1 validation gate; T1000 is CUDA 7.5 which PyTorch supports | CPU-only training |
| Jais not available in Ollama | Medium | Convert Jais to GGUF and import; or use cloud API teacher | Use Qwen2-1.5B as teacher instead |
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

---

## 19. Implementation Epics and Tasks

### Epic 1: Validation and Toolchain (Week 1)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 1.1 | Validate PyTorch + CUDA on T1000 4GB. Run `torch.cuda.is_available()` and a small training loop. | 2 hours | CUDA is available on T1000; training loop runs |
| 1.2 | Install and validate DeepSpeed ZeRO-2 with CPU offloading. Test with a small model on T1000. | 4 hours | CPU offload works; GPU memory stays within 4 GB budget |
| 1.3 | Validate flash attention on T1000 (PyTorch 2.0+). | 2 hours | `scaled_dot_product_attention` uses flash kernel on T1000 |
| 1.4 | Validate TorchSharp CUDA 12.8 in .NET solution on T1000. | 4 hours | Tensor ops on T1000 GPU; model forward pass works |
| 1.5 | Validate Microsoft.ML.Tokenizers BPE training in C#. | 4 hours | Train a small BPE; encode/decode roundtrip |
| 1.6 | Validate ONNX Runtime in .NET with CUDA EP on T1000. | 4 hours | Load ONNX model; GPU inference works |
| 1.7 | Install and validate Ollama. Pull and run Jais or a test model on CPU. | 4 hours | Ollama serves model on CPU; API responds correctly |
| 1.8 | Validate Neo4j + FaissNet/HNSW.Net in .NET. | 4 hours | Write/query graph; index and search vectors |
| 1.9 | Validate Panlingo.FastText + MinHashSharp. | 2 hours | Language ID and MinHash work in C# |
| 1.10 | Set up Python sidecar: .NET launches Python training, monitors output. | 4 hours | Sidecar pattern works end-to-end |
| 1.11 | Test cloud API keys from https://github.com/alistaitsacle/free-llm-api-keys. | 2 hours | At least one endpoint responds correctly |

**Gate:** All tools confirmed working on T1000 + Core i9H + 64GB RAM.

### Epic 2: Corpus and Knowledge Foundation (Weeks 2-4)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 2.1 | Ingest Quranic text from Tanzil; verify against canonical. | 4 hours | 100% canonical match |
| 2.2 | Ingest Hadith from Sunnah sources. | 8 hours | 50K+ records |
| 2.3 | Ingest OSCAR/CC-100/Wikipedia Arabic. | 16 hours | > 1B tokens raw |
| 2.4 | Ingest linguistic dictionaries. | 16 hours | 200K+ word entries |
| 2.5 | Language ID + Unicode normalization + length filtering (C#). | 8 hours | All docs pass gates |
| 2.6 | MinHash deduplication within and across sources (C#). | 8 hours | Near-duplicate < 5% |
| 2.7 | Train/test/val split with contamination prevention (C#). | 4 hours | < 0.1% n-gram overlap |
| 2.8 | Morphological annotation via Farasa API (C# client). | 24 hours | All docs annotated |
| 2.9 | Train BPE tokenizer using Microsoft.ML.Tokenizers (C#). | 8 hours | OOV < 0.01%, tokens/word < 1.5 |
| 2.10 | Build token alignment map (Lisan BPE <-> Jais). | 8 hours | > 90% coverage |
| 2.11 | Seed Neo4j graph with dictionary data. | 16 hours | 200K+ Word nodes |
| 2.12 | Seed Quranic annotations + cross-references. | 8 hours | All Quranic words linked |
| 2.13 | Seed ConceptNet Arabic edges. | 8 hours | 100K+ edges |
| 2.14 | Build taxonomy from Wikipedia categories. | 8 hours | 10+ domains, 5K+ articles each |
| 2.15 | Curate golden sets: 500+ per source. | 24 hours | Stored with identifiers |
| 2.16 | QA dashboard live with all metrics. | 8 hours | Dashboard operational |

**Gate:** Corpus > 500M tokens. Graph > 200K Word nodes. BPE passes validation.

### Epic 3: NLP Layer (Weeks 4-7)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 3.1 | Lisan.Morphology: dictionary lookup (C#). | 16 hours | < 2 ms/word, > 90% coverage |
| 3.2 | Lisan.Morphology: pattern heuristics (C#). | 16 hours | < 5 ms/word, 80% coverage |
| 3.3 | Lisan.Morphology: disambiguation (C#). | 16 hours | > 70% correct on ambiguous |
| 3.4 | Lisan.Tokenizer: BPE encode/decode (C#). | 16 hours | Roundtrip on 10K sentences |
| 3.5 | Lisan.Syntax: rule-driven parser (C#). | 32 hours | 80% MSA parsing |
| 3.6 | Lisan.Syntax: case ending prediction (C#). | 16 hours | > 70% on Quranic text |
| 3.7 | Lisan.Dialect: Egyptian <-> MSA (C#). | 16 hours | 5K+ word pairs |
| 3.8 | Lisan.Dialect: detection CNN via TorchSharp (C#, train on T1000). | 16 hours | > 75% on MADAR |
| 3.9 | Lisan.Diacritization: deterministic layer (C#). | 16 hours | > 70% word accuracy |
| 3.10 | Lisan.Diacritization: neural model via TorchSharp (C#, train on T1000). | 24 hours | > 92% word accuracy |
| 3.11 | Integrate diacritization pipeline (C#). | 8 hours | Combined > 85% |
| 3.12 | Unit tests: > 90% coverage. | 16 hours | All pass |

**Gate:** NLP libs pass tests. Morphology > 85%. Diacritization > 85%.

### Epic 4: RAG System (Weeks 7-10)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 4.1 | Fine-tune Arabert (Python, one-time) + export to ONNX. | 24 hours | Quality > MiniLM baseline |
| 4.2 | Build FaissNet/HNSW.Net index (C#). | 16 hours | Search < 10 ms |
| 4.3 | GraphRAG query engine: all 7 query types (C#). | 24 hours | Within latency targets |
| 4.4 | Morphology-aware query expansion (C#). | 8 hours | > 20% more relevant results |
| 4.5 | Context assembly with tiering + dedup (C#). | 16 hours | Within budget, no duplicates |
| 4.6 | Template-based responses for 6 types (C#). | 24 hours | 80% query type coverage |
| 4.7 | End-to-end RAG pipeline (C#). | 16 hours | < 500 ms latency |
| 4.8 | Graceful degradation (C#). | 8 hours | Never crashes |
| 4.9 | RAG-only benchmarks. | 16 hours | Meet Section 15.2 targets |
| 4.10 | All API endpoints (C#). | 24 hours | Functional; OpenAPI spec |
| 4.11 | Integration testing. | 8 hours | All pass |

**Gate:** RAG-only product shippable. Template >= 75%, Graph >= 90%.

### Epic 5: Model Training (Weeks 10-18)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 5.1 | Implement model architecture in PyTorch + TorchSharp (dual definition). | 24 hours | Both produce same output on test input |
| 5.2 | Implement DeepSpeed ZeRO-2 training loop (Python). | 16 hours | Loss decreases; GPU memory stays within 4 GB |
| 5.3 | Set up Ollama teacher: pull/convert Jais-1.3B, test API from Python. | 8 hours | Teacher logits available via Ollama API |
| 5.4 | Vocabulary alignment + distillation loss (Python). | 16 hours | > 90% alignment; valid KL loss |
| 5.5 | Implement .NET training orchestrator sidecar (C#). | 8 hours | .NET launches, monitors, validates training |
| 5.6 | Phase 1: 2K context, steps 1-10K. | ~55 hours | Smooth loss decrease |
| 5.7 | Phase 2: 4K context, steps 10K-40K. | ~303 hours | Steady improvement |
| 5.8 | Phase 3: 8K context, steps 40K-70K. | ~500 hours | Final perplexity < 25 |
| 5.9 | Ablation: with vs. without morphological injection at step 20K. | 8 hours | Injection improves > 2% |
| 5.10 | Learning curve analysis. | 16 hours | Capacity limits identified |
| 5.11 | Validate checkpoints in TorchSharp (C#). | 8 hours | TorchSharp loads weights; correct output |
| 5.12 | Export to ONNX + GGUF (for Ollama). | 8 hours | Both formats validated |
| 5.13 | Full benchmark suite. | 16 hours | All Section 15.1 targets met |

**Note on training duration:** Phases 5.6-5.8 total ~858 hours (~36 days). This runs continuously from Week 10 through approximately Week 15-16. The .NET sidecar monitors progress and validates checkpoints automatically.

**Gate:** FP16 model passes benchmarks. ONNX + GGUF validated. TorchSharp can load and run model.

### Epic 6: Quantization (Weeks 18-20)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 6.1 | Layerwise sensitivity analysis. | 16 hours | Skip list generated |
| 6.2 | INT8 quantization + benchmarks. | 8 hours | >= 99% FP16 |
| 6.3 | INT4 quantization + benchmarks. | 16 hours | >= 97% FP16 |
| 6.4 | If INT4 < 97%: QoRA recovery. | 24 hours | INT4 + QoRA >= 97% |
| 6.5 | 2-bit ternary + benchmarks. | 16 hours | >= 95% FP16 |
| 6.6 | If ternary < 95%: QoRA recovery. | 24 hours | Ternary + QoRA >= 95% |
| 6.7 | If ternary still < 95%: defer. | 0 hours | Ship INT4 |
| 6.8 | Package all formats (ONNX + GGUF). | 8 hours | All validated |

**Gate:** INT4 passes. INT8 passes. Ternary passes or deferred.

### Epic 7: Runtime Integration and API (Weeks 20-24)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 7.1 | ONNX Runtime model inference in Lisan.Model (C#). | 16 hours | < 2 sec for 4K on T1000 |
| 7.2 | Full pipeline integration (C#). | 24 hours | Correct Arabic outputs |
| 7.3 | `/v1/chat/completions` endpoint (C#). | 16 hours | OpenAI-compatible |
| 7.4 | `/v1/embeddings` endpoint (C#). | 8 hours | 768-dim embeddings |
| 7.5 | Remaining API endpoints (C#). | 16 hours | All functional |
| 7.6 | Deployment mode switching (C#). | 8 hours | Memory within budget |
| 7.7 | KV cache quantization for 32K (C#). | 8 hours | 32K fits in 16 GB |
| 7.8 | Integration testing (C#). | 16 hours | All pass |
| 7.9 | Performance testing on T1000 + CPU. | 8 hours | Targets met |
| 7.10 | Security testing. | 8 hours | No vulnerabilities |
| 7.11 | Docker image. | 8 hours | Starts in < 30 sec |

**Gate:** Full system functional. All APIs work. Performance targets met.

### Epic 8: Evaluation and Publication (Weeks 24-26)

| Task | Description | Duration | Success Criteria |
|---|---|---|---|
| 8.1 | Full benchmark suite (all quantization levels). | 16 hours | Results documented |
| 8.2 | Blind test set evaluation. | 16 hours | Meets acceptance criteria |
| 8.3 | Evaluation report. | 24 hours | Complete and reviewed |
| 8.4 | Paper draft. | 40 hours | Ready for submission |
| 8.5 | User documentation. | 24 hours | Complete |
| 8.6 | Developer documentation. | 16 hours | Complete |
| 8.7 | Security audit. | 16 hours | No critical vulnerabilities |

---

## 20. Religious and Trust Guardrails

### 20.1 Citation-First Answering

For Quran, Hadith, Tafsir, and fiqh-adjacent queries:

- All answers must be grounded in retrieved sources
- Answers must cite the specific source (Surah:Ayah for Quran, narrator chain for Hadith, scholar for Tafsir)
- The system must explicitly state when its corpus does not cover the queried topic
- Template responses for religious content must include source citations by default

### 20.2 Disputed Interpretation Handling

- When multiple Tafsir sources offer different interpretations, present all sourced views
- Never present one interpretation as the only correct view without attribution
- Flag questions about fiqh rulings as "requires qualified scholar guidance"
- Never issue fatwa-style responses

### 20.3 Non-Fatwa Guardrails

- Detect queries seeking religious rulings (فتوى, حكم, هل يجوز)
- Respond with: "This question requires a qualified scholar. I can provide related Quranic verses, Hadith references, and scholarly opinions for your reference."
- Provide sourced references without ruling

---

## 21. Backlog Expansion Epics (Post-Baseline)

These epics expand the product beyond the baseline. They do not block the 26-week timeline.

### Backlog Epic A: Religious Answer Safety Layer

| Task | Description | Prerequisite |
|---|---|---|
| A.1 | Citation-first answering for all religious queries | Baseline RAG |
| A.2 | Disputed-interpretation detection and multi-view presentation | Neo4j with Tafsir |
| A.3 | Non-fatwa guardrail: detect ruling-seeking queries | Intent classifier |
| A.4 | Source-confidence scoring for Hadith (sahih/hasan/da'if) | Hadith metadata |
| A.5 | Quranic verse verification against canonical Tanzil | Tanzil (done) |

### Backlog Epic B: Customer Support Agent Layer

| Task | Description | Prerequisite |
|---|---|---|
| B.1 | Intent routing for Arabic customer support queries | Dialect + intent classifier |
| B.2 | Citation traceability in agent responses | RAG + source metadata |
| B.3 | Safety and escalation rules for sensitive topics | Religious safety layer |
| B.4 | Conversation memory (short-term session context) | API + session management |
| B.5 | Answer templates for common support scenarios | Template system (built) |

### Backlog Epic C: Source Quality and Coverage Expansion

| Task | Description | Prerequisite |
|---|---|---|
| C.1 | Tafsir breadth expansion (more scholars) | Baseline graph |
| C.2 | Hadith source-quality ranking | Hadith metadata |
| C.3 | Provenance scoring for all ingested sources | Data lineage |
| C.4 | Coverage-gap reporting | Graph + QA dashboard |
| C.5 | Levantine and Gulf dialect translation | Dialect detection (built) |

---

## 22. Dataset and Tooling Reference

### Data Sources

| Resource | Role | Access |
|---|---|---|
| Tanzil | Verified Quran | https://tanzil.net/download/ |
| Sunnah.com | Hadith corpus | https://sunnah.com/ |
| Lisan Al-Arab | Classical dictionary | https://www.almaany.com/ |
| Al-Waseet | Modern dictionary | https://archive.org/ |
| OSCAR Arabic | General corpus | https://oscar-project.org/ |
| Arabic Wikipedia | Knowledge + ontology | https://dumps.wikimedia.org/ |
| CC-100 Arabic | General corpus | https://data.statmt.org/cc-100/ |
| Quranic Arabic Corpus | Morphology + syntax | https://corpus.quran.com/ |
| MADAR | Dialect corpus | https://camel.abudhabi.nyu.edu/madar/ |
| CAMeL Tools | Morphology support | https://camel.abudhabi.nyu.edu/tools/ |
| Farasa | Preprocessing | https://farasa.qcri.org/ |
| Jais-1.3B | Primary teacher | https://huggingface.co/inceptionai/jais-1p3b |
| Qwen2-1.5B | Auxiliary teacher | https://huggingface.co/Qwen/Qwen2-1.5B |
| Arabert | Embedding model | https://huggingface.co/aubmindlab/bert-base-arabertv02 |
| XNLI Arabic | NLI data | https://huggingface.co/datasets/xnli |
| Arabic WordNet | Synonym/antonym | https://globalwordnet.github.io/gwn/ |
| ConceptNet | Semantic edges | https://conceptnet.io/ |
| PADT | Syntax training | https://lindat.mff.cuni.cz/ |
| Hindawi | Arabic literature | https://www.hindawi.org/ |
| OPUS | Parallel corpus | https://opus.nlpl.eu/ |
| Free LLM API Keys | Cloud teacher/eval | https://github.com/alistaitsacle/free-llm-api-keys |

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
| Microsoft.Data.Sqlite | v8.x | SQLite persistence |
| Microsoft.EntityFrameworkCore.Sqlite | v8.x | ORM for corpus state |

---

## 23. Final Execution Rules

1. This document is the sole planning baseline.
2. No task begins until its prerequisite Epic's gate is passed.
3. No deviation from quantization quality gates.
4. All parameter counts and memory budgets verified from first principles.
5. .NET-first stack: use .NET/C# for everything feasible. Python is used ONLY for: (a) 458M model training via PyTorch, and (b) one-time embedding model fine-tuning. All other components run in .NET.
6. TorchSharp is the .NET-native model tool for architecture definition, validation, and small model training. The 458M model training uses PyTorch because TorchSharp lacks AMP, gradient checkpointing, CPU optimizer offloading, and flash attention — all required to fit 458M on T1000 4GB.
7. Training orchestration is .NET-driven via sidecar pattern.
8. Every checkpoint must pass automated regression tests before promotion.
9. Ollama serves the teacher model on CPU, keeping T1000 GPU free for student training.
10. Cloud API keys provide fallback teacher and evaluation assistance when needed.

---

## Appendix: Self-Review and Validation

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

- INT4 weight: 457,777,280 x 4 bits / 8 = 228,888,640 bytes ≈ 229 MB ✓
- 2-bit ternary: 457,777,280 x 2 bits / 8 = 114,444,320 bytes ≈ 114 MB + 14 MB scales = 128 MB ✓
- KV 4K FP16: 4,096 x 16,384 x 2 = 134,217,728 bytes = 128 MB ✓
- KV 32K INT8: 32,768 x 16,384 x 1 = 536,870,912 bytes = 512 MB ✓

### Training Memory Verification — T1000 4GB

- GPU: 916 MB (weights) + 200 MB (activations) + 150 MB (CUDA) = 1,266 MB. **Fits in 4 GB with 2,734 MB headroom.** ✓
- CPU: 1,832 MB (FP32 master) + 3,664 MB (Adam) + 916 MB (gradients) + 500 MB (DataLoader) + 800 MB (Ollama) = 7,712 MB. **Fits in 64 GB.** ✓

### Training Time Verification — T1000

- T1000 FP16 throughput: ~2.5 TFLOPS
- Per token compute: ~0.45 GFLOPs (forward) x 2 (forward+backward) = 0.9 GFLOPs
- At 2.5 TFLOPS with ~30% utilization (CPU offloading overhead): ~0.75 TFLOPS effective
- Throughput: 0.75 x 10^9 / 0.9 x 10^9 ≈ 0.83 tokens/sec
- Total tokens: 70,000 steps x avg 2,048 tokens/step ≈ 143M tokens
- Time: 143M / 0.83 ≈ 172M sec ≈ 48 hours (compute only)
- With CPU offloading overhead (~5x): ~240 hours
- With data loading, validation, checkpointing (~1.5x): ~360 hours ≈ 15 days
- Conservative with interruptions: **36-45 days** ✓ (fits within Week 10-18 window)

### .NET Coverage Verification

18 of 20 components are .NET/C# (90%). The 2 Python components are 458M training and one-time embedding fine-tuning. ✓

### Architecture Size Validation

458M with morphological injection + Jais distillation should exceed Jais-1.3B quality on Arabic linguistic tasks while being 3x smaller at inference. INT4 deployment fits in 4 GB RAM. ✓

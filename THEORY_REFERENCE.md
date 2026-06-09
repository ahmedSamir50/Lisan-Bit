# Lisan Bits Model - Mathematical & ML Theory Reference

This document serves as the foundational academic reference for the Math, Statistics, and Machine Learning theories driving the Lisan Bits Model. It is designed to keep all developers and AI personas aligned on the science behind the engine.

---

## 1. Natural Language Processing (NLP) & Embeddings

### 1.1 Skip-gram Model (Word2Vec)
*Target: Epic 3 (TorchSharp Training)*

The Skip-gram model is a shallow neural network designed to learn word embeddings. Unlike Continuous Bag of Words (CBOW) which predicts a word from its context, Skip-gram predicts the context (surrounding words) given a target word.
- **Math Concept:** It maximizes the probability of observing context words given a center word.
- **Relevance:** For Arabic roots, Skip-gram is excellent at capturing semantic relationships (e.g., words derived from the same root will naturally cluster together in the vector space).
- **Training Implementation:** TorchSharp (C# bindings for LibTorch). Used **training-only**. Weights are exported to binary and TorchSharp is discarded for inference.

### 1.2 Vector Space & Cosine Similarity
*Target: Epic 4 (Ternary Similarity Logic)*

Once words are converted into vectors, we need a way to measure how "similar" they are.
- **Math Concept:** Cosine Similarity measures the cosine of the angle between two vectors in a multi-dimensional space.
- **Formula:** `Cosine(A, B) = (A · B) / (||A|| * ||B||)`
- **Relevance:** In our Ternary matrix, the Dot Product (`A · B`) becomes the primary driver of similarity.

### 1.3 Root-Based TF-IDF Feature Extraction for ML.NET Classifiers
*Target: Epic 3 (Context Classifiers — Feature Pipeline)*

ML.NET's default `FeaturizeText` transformer operates on raw Unicode tokens, which is insufficient for Arabic. Arabic is a derivational language: `كتب`, `يكتب`, and `مكتوب` are distinct tokens but share root `كتب`.

**The mandated Arabic feature pipeline:**
```
Raw Text → Farasa Root Extractor → Root Token Array → TF-IDF Featurizer → L-BFGS Trainer
```

**TF-IDF formula** for root token $r$ in document $d$:
$$\text{TF-IDF}(r, d) = \text{TF}(r, d) \times \log\left(\frac{N}{\text{DF}(r)}\right)$$

Where:
- $\text{TF}(r, d)$ = frequency of root $r$ in document $d$.
- $N$ = total number of documents.
- $\text{DF}(r)$ = number of documents containing root $r$.

This collapses all morphological variants of a root into a single feature dimension, dramatically reducing sparsity and improving classifier performance.

---

## 2. Quantization & Ternary Computing

### 2.1 Post-Training Quantization (PTQ)
*Target: Epic 3 (Ternary Quantization)*

Standard models use 32-bit floating-point numbers (`float32`). PTQ compresses these models *after* they are trained to save RAM and compute.
- **Ternary Quantization (-1, 0, 1):** We compress `float32` into an `sbyte` (Signed Byte).
- **Thresholding Function:**
  To convert floats to ternary, we use a threshold `Δ` based on the mean absolute value of the weights.
  - If `weight > Δ` ➔ `1`
  - If `weight < -Δ` ➔ `-1`
  - Else ➔ `0`

### 2.2 Mathematical Quantization Function
Let $W \in \mathbb{R}^{V \times D}$ be the float embedding matrix. The quantized weights $\tilde{W} \in \{-1, 0, 1\}^{V \times D}$ and the scalar scaling factor $\gamma$ are defined as:
$$\gamma = \frac{1}{V \cdot D} \sum_{i=1}^{V} \sum_{j=1}^{D} |W_{i,j}|$$
$$\Delta = \alpha \cdot \gamma \quad (\text{where } \alpha \text{ is a threshold factor, typically } 0.7)$$
$$\tilde{W}_{i,j} = \text{sign}(W_{i,j}) \cdot \mathbb{I}(|W_{i,j}| > \Delta)$$
Where $\mathbb{I}$ is the indicator function. The original weights are approximated during inference by scaling:
$$W \approx \gamma \cdot \tilde{W}$$

### 2.3 Quantization Error & Bias Correction
*Target: Epic 3 (Bias Correction)*

When you round a precise float (e.g., `0.73`) to a ternary integer (`1`), you lose information (`0.27`). This is the **Quantization Error**.
- **The Error Matrix:** $E = W - \gamma \tilde{W}$
- **Bias Correction:** Across millions of parameters, this error compounds. To pull the mathematical expectation of the output back to zero, we compute the expected bias vector $b \in \mathbb{R}^V$ across the dataset activations $X$:
  $$b_i = \mathbb{E}[X] \cdot \sum_{j=1}^{D} E_{i,j}$$
  During inference, the scaling and bias are applied to the ternary dot product:
  $$\hat{y}_i = \gamma (X \cdot \tilde{W}_i^T) + b_i$$

---

## 3. High-Performance Vector Math (Systems Engineering)

### 3.1 Ternary Dot Product & SIMD
*Target: Epic 4 (TensorPrimitives)*

Standard Matrix Multiplication requires expensive floating-point Arithmetic Logic Units (ALUs).
- **SIMD (Single Instruction, Multiple Data):** Modern CPUs (using AVX2/AVX-512) can process multiple numbers in a single clock cycle.
- **Relevance:** By using `.NET TensorPrimitives.DotProduct` on an array of `sbyte`, the CPU can load 32 or 64 ternary values into a single register and multiply/accumulate them simultaneously. This is why our engine can run in `<100ms` without a GPU.
- **Inference Constraint:** TorchSharp (`libtorch`) must NOT be present in the inference path. Its C++ dependencies violate the <500MB and lightweight constraints.

### 3.2 Vector Superposition (Context Merging)
*Target: Epic 4 (Top-K Context Merging)*

When a query spans multiple contexts (e.g., Religion and Astronomy), we must hybridize the contexts. Instead of expensive floating-point weighted averages, we use Integer Addition.
- **The Math:** `V_hybrid = sign(V_contextA + V_contextB)`
- **The 'Sign' Function Clamp:**
  We add the `sbyte` arrays element-by-element. If the sum is positive, the bit becomes `1`. If negative, `-1`. If `0`, it remains `0` (the contexts cancel each other out).
- **Efficiency:** This keeps the resulting vector clamped to the Ternary space (-1, 0, 1) and executes in nanoseconds since it only involves integer addition.

### 3.3 Depth-Decayed Superposition for Hierarchical Contexts
*Target: Epic 3 (Genus-Aware Classifier Output)*

When a word is classified to a leaf path like `Science/Medicine/Cardiology`, we must build a hierarchically weighted contribution, not a flat one. Nodes higher in the hierarchy are more generic; nodes lower are more specific. We apply a **depth decay** factor $\lambda \in (0,1)$ (typically $0.5$):

$$W_{\text{family}} = s \cdot \lambda^0, \quad W_{\text{genus}} = s \cdot \lambda^1, \quad W_{\text{species}} = s \cdot \lambda^2$$

For a leaf confidence score $s = 0.8$ and $\lambda = 0.5$:
- `Science` contribution: $0.8 \times 1.0 = 0.80$
- `Medicine` contribution: $0.8 \times 0.5 = 0.40$
- `Cardiology` contribution: $0.8 \times 0.25 = 0.20$

The resulting ContextVector stored in SQLite:
```json
{
  "Science": 0.80,
  "Science/Medicine": 0.40,
  "Science/Medicine/Cardiology": 0.20,
  "Linguistics/Slang": 0.10
}
```

This ensures general family matrices contribute broadly while specific leaf matrices contribute precisely, without losing the specificity of the Genus/Species distinction.

---

## 4. Graph Theory & Data Structures

### 4.1 Root-Centric Morphology Graph
*Target: Epic 2 (Neo4j Schema)*

Arabic language logic is deterministic. Instead of forcing a neural network to memorize grammar, we store it in a Directed Graph.
- **Math Concept:** A Graph `G = (V, E)` where `V` are Vertices (Roots, Words, Contexts) and `E` are Edges (Derivations, Sub-Contexts).

### 4.2 The Adjacency List (Caching)
*Target: Epic 2 (In-Memory Cache)*

Loading a massive Graph Database from disk during inference is too slow (Latency).
- **Computer Science Concept:** An Adjacency List represents a graph as an array of lists. In C#, this is represented as a `Dictionary<int, List<int>>`, where the key is a Root ID, and the value is a list of connected Word IDs.
- **Big-O Complexity:** Looking up a word's derivations drops from `O(log N)` (DB Disk Seek) to `O(1)` (RAM Hash Table lookup).

### 4.3 Hierarchical Taxonomy Graph (Genus-Aware)
*Target: Epic 2.6 (Neo4j Taxonomy Seeding)*

The taxonomy tree is stored in Neo4j as `(:Context)-[:HAS_SUB_CONTEXT]->(:Context)` edges. This graph is separate from the flat Bitmask used in inference caching, and is the authoritative source of Context identity and relationships.

Example graph structure:
```cypher
(:Context {name: "Science"})-[:HAS_SUB_CONTEXT]->(:Context {name: "Medicine"})
(:Context {name: "Medicine"})-[:HAS_SUB_CONTEXT]->(:Context {name: "Cardiology"})
(:Context {name: "Cardiology"})-[:HAS_SUB_CONTEXT]->(:Context {name: "Mitral_Valve"})
```

The SQLite `ContextVector` column references **leaf path strings** (e.g., `"Science/Medicine/Cardiology"`). Neo4j owns all IDs, parent-child relationships, and outgoing edges. This keeps the two stores cleanly decoupled.

---

## 5. Statistical Classifiers

### 5.1 The Genus-Aware Context Router (ML.NET)
*Target: Epic 3 (Hierarchical Context Classifiers)*

The routing architecture is a **two-level cascade in Binary Relevance mode**:
- **Level-0 (Family Router):** 10 independent binary `LbfgsLogisticRegressionBinaryTrainer` models, one per root domain (Religion, Science, Medical, Finance, Sports, DailyLife, Literature, News, Linguistics, Slang). Each model outputs a probability for its family.
- **Level-1 (Sub-Router):** For each Level-0 family activated above confidence threshold, a dedicated sub-classifier routes to the specific leaf sub-context.

**Class-Weighted Training (mandatory for imbalanced corpus):**
$$w_i = \frac{N}{K \cdot N_k}$$
Where $N$ = total samples, $K$ = number of classes, $N_k$ = count of class $k$. Applied via `ExampleWeightColumnName` in ML.NET.

**Early Stopping by F1 Convergence:**
$$\Delta F1 = F1_e - F1_{e-3} < 0.001 \Rightarrow \text{Stop}$$
Minimum quality gates: Level-0 F1 ≥ 0.80 per category; Level-1 AUC ≥ 0.85.

**Top-K Selection:** Return only Top 2 or 3 contexts above confidence threshold. All others discarded. This prevents permutation explosion during superposition.

### 5.2 Syntactic Sequence Tagger (L-BFGS Maximum Entropy)
*Target: Epic 3 & Epic 4 (Syntactic Tagger)*

To predict the `GrammaticalState` sequence (Nominative, Accusative, etc.) of raw un-diacritized words, we model the sequence tagging problem.
- **Mathematical Model:** Multiclass Logistic Regression trained via the L-BFGS optimization method. It models the conditional probability of state $y_t$ at word index $t$:
  $$P(y_t = c \mid \mathbf{x}_t) = \frac{\exp(\mathbf{w}_c \cdot \mathbf{x}_t)}{\sum_{j=1}^{C} \exp(\mathbf{w}_j \cdot \mathbf{x}_t)}$$
- **Context Sliding Window:** The feature vector $\mathbf{x}_t$ represents a sliding window of neighborhood characteristics:
  $$\mathbf{x}_t = [POS_{t-2}, POS_{t-1}, Lemma_{t-1}, POS_{t+1}, POS_{t+2}, Word_t, POS_t]$$
  This captures local syntactic agreement constraints (e.g., a preceding Preposition $POS_{t-1}$ strongly forces the Genitive state).

### 5.3 Data Quality Filter: 7 Unique Root Floor
*Target: Epic 1.5 (Corpus Ingestion Quality Gate)*

A text block is accepted for taxonomy labeling only if Farasa root extraction yields at least **7 distinct roots**:
$$\text{Accept block if } |\{r_1, r_2, \ldots, r_n\} \text{ (distinct roots)}| \geq 7$$

This filter is corpus-invariant (unlike word count), correctly rejects function-word-dominated fragments, and does not penalize lexically dense short texts.

---

## 6. Generative Syntax & I'rab (الإعراب)

### 6.1 The "Two-Stage" Generator Model
*Target: Epic 4 (Syntactic Validator)*

Standard LLMs hallucinate grammar because they rely purely on next-token statistical probabilities. Lisan-Al-Bits uses a deterministic Two-Stage Generation model to separate *Concept (Meaning)* from *Syntax (Grammar)*.

- **Stage 1: The Conceptual Selector (Bit-Math)**
  - The Ternary Embeddings calculate the most likely semantic "Concept" (e.g., The root concept for "Mosque") using `TensorPrimitives.DotProduct`.
- **Stage 2: The Syntactic Morpher (Graph Pathfinding)**
  - Arabic syntax (Nahw) requires Case Endings (I'rab) based on the governing word ('Amil).
  - The system analyzes the previous Part of Speech (e.g., Preposition).
  - It traverses the Graph DB to find the specific Grammatical Rule edge (e.g., `REQUIRES_GENITIVE_CASE`).
  - It filters the Concept from Stage 1 to strictly output the Inflected Form that matches the Rule (e.g., "المسجدِ" instead of "المسجدُ").
- **Mathematical Relevance:** This converts the probabilistic generation of LLMs into a **Markov Chain constrained by a deterministic State Machine**, guaranteeing 100% Arabic grammatical accuracy without increasing the parameter count of the neural matrix.

### 6.2 Pushdown Automaton (PDA) for Nested Arabic Syntax
*Target: Epic 4 (Syntactic Parse Engine)*

Arabic grammar features nested dependencies (such as embedded relative clauses *Sila*, non-contiguous Subject-Object agreements, and scoped conditional statements). A simple finite state machine cannot track nested scopes. We formally model this using a **Pushdown Automaton (PDA)**:
$$M = (Q, \Sigma, \Gamma, \delta, q_0, Z_0, F)$$
Where:
- $Q$ is the set of control states.
- $\Sigma$ is the input alphabet (words, POS tags, and features).
- $\Gamma$ is the stack alphabet consisting of syntactic frames `ClauseFrame`.
- $\delta$ is the transition function mapping $Q \times (\Sigma \cup \{\epsilon\}) \times \Gamma \rightarrow Q \times \Gamma^*$.
- $q_0$ is the start state.
- $Z_0$ is the initial bottom-of-stack symbol.
- $F$ is the set of accepting states.

The PDA maintains context frames `ClauseFrame`:
```csharp
public class ClauseFrame
{
    public string GovernorLemma { get; set; } = string.Empty;
    public string GovernorPos { get; set; } = string.Empty;
    public HashSet<GrammaticalState> SatisfiedRoles { get; } = new();
    public GrammaticalState InheritableState { get; set; } = GrammaticalState.Nominative;
}
```
- **Push Actions:** Triggered when encountering structural separators (e.g., relative pronouns *Mawsul* like "الذي", or conditional particles *In* like "إن").
- **Pop Actions:** Triggered when the grammatical roles (e.g., the predicate of *Inna* or the verb's main subject and object) are satisfied.
- **Inheritance Path:** Allows adjectives (*Sifah*) and coordinating conjunctions (*Ma'tuf*) to copy the grammatical state of the governing noun directly from the active stack frame.

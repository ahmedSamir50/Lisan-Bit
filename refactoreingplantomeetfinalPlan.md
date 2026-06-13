# Lisan-Bit: Detailed Refactoring Plan for Epics 1-4

This document outlines the exact, step-by-step technical implementation required to align the current codebase with the locked `LISAN_BIT_Plan.md` for Epics 1, 2, 3, and 4 based on our analysis of what is already implemented. 

---

## 1. Epic 1: Toolchain & Validation (Sidecar Orchestrator)
The primary model (458M) training must occur in Python/PyTorch due to TorchSharp limitations. `.NET` will act as a sidecar orchestrator to manage and monitor this process.

### Refactoring `LisanBits.Trainer/Program.cs`
- **Remove existing model:** Remove the current TorchSharp `SkipGramModel` logic entirely from `LisanBits.Trainer/Program.cs`.
- **Implement TrainingOrchestrator pattern:**
  - Add logic to prepare training data from SQLite and export it to a shared format/directory for Python to consume.
  - Use `System.Diagnostics.Process.Start()` to launch the Python training script.
  - Monitor the Python process stdout/stderr for training progress and loss metrics.
  - Implement checkpoint validation using TorchSharp: periodically load the `.pt` or exported `ONNX` model weights to run verification inferences.

### New Python Script `src/LisanBits.Trainer/PythonScripts/train_model.py`
- Create a baseline Python script stub that the C# orchestrator will call. 
- It will handle the PyTorch + DeepSpeed ZeRO-2 training loop using the provided parameters from the plan.

---

## 2. Epic 2: Data Acquisition and Cleaning
The pipeline must clean data before insertion, utilizing FastText for Language Identification and MinHash for deduplication. *Note: Shamela and Sunnah are already correctly configured with XPaths in PipelineDbContext and LexiconParser.cs.*

### Dependency Addition
- **[MODIFY]** `src/LisanBits.DataPipeline/LisanBits.DataPipeline.csproj`: Add NuGet packages `Panlingo.LanguageIdentification.FastText` (v0.7.2 or latest) and `MinHashSharp`.

### New Data Cleaner Service
- **[NEW]** `src/LisanBits.DataPipeline/Preprocessing/DataCleaner.cs`:
  - **Language Identification**: Load the Panlingo FastText model to filter out non-Arabic content (threshold > 0.9).
  - **Deduplication**: Implement MinHash signature generation and compare against a global "seen hash" registry (can be in-memory or in SQLite) to ensure near-duplicates (Jaccard >= 0.85) are discarded.
  - **Normalization**: Migrate the `StripTashkeelAndNormalize` logic (currently inside Trainer) here as a standard normalizer.

### Worker Pipeline Integration
- **[MODIFY]** `src/LisanBits.DataPipeline/Worker.cs`:
  - Inject the `DataCleaner` service into the scraping loop.
  - Right before executing `db.RawUniversalData.Add(...)` or `db.LexiconEntries.Add(...)`, process the extracted text through the `DataCleaner`. 
  - Skip insertion if the text fails the Language ID or Deduplication gates.

---

## 3. Epic 3: Knowledge Graph Seeding
We need to explicitly move the cleaned Shamela dictionary entries and generic corpus data from SQLite into the Neo4j Knowledge Graph.

### New Graph Seeder Project
- **[NEW]** Scaffold `src/LisanBits.GraphSeeder/LisanBits.GraphSeeder.csproj` as a .NET Worker Service.
- Add NuGet package `Neo4j.Driver` and `Microsoft.EntityFrameworkCore.Sqlite`.
- Add a project reference to `LisanBits.DataPipeline` to reuse the EF Core models.

### Seeding Implementation
- **[NEW]** `src/LisanBits.GraphSeeder/Worker.cs`:
  - Initialize connections to SQLite and Neo4j.
  - Read from `LexiconEntries` in SQLite (which handles Shamela, Taimoor, etc.).
  - Construct Neo4j nodes: `Word`, `Root`, and `Pattern`.
  - Create the required relationships: `(:Word)-[:HAS_ROOT]->(:Root)`.
  - For Taimoor dialect entries (identified by source ID `150964` or BookName), create `DialectWord` nodes and `DIALECT_MAPS_TO` relationships.

### Solution Update
- **[MODIFY]** `LisanBits.slnx`: Add `src/LisanBits.GraphSeeder/LisanBits.GraphSeeder.csproj`.

---

## 4. Epic 4: Morphology + Tokenizer
These foundational natural language processing libraries must be implemented in C# as shared class libraries, so they can be consumed by the GraphRAG pipeline and the DataPipeline.

### Lisan Tokenizer Shared Library
- **[NEW]** Scaffold `src/Shared/Lisan.Tokenizer/Lisan.Tokenizer.csproj` as a .NET Class Library.
- Add NuGet package `Microsoft.ML.Tokenizers`.
- **[NEW]** `BpeTrainer.cs`: Logic to train a vocabulary of 32,768 tokens using the BPE algorithm on the cleaned text corpus.
- **[NEW]** `LisanTokenizer.cs`: BPE encode/decode wrapper for runtime usage.

### Lisan Morphology Shared Library
- **[NEW]** Scaffold `src/Shared/Lisan.Morphology/Lisan.Morphology.csproj` as a .NET Class Library.
- Add NuGet package `Neo4j.Driver`.
- **[NEW]** `MorphologyAnalyzer.cs`: Implements the cascading fallback logic:
  1. In-memory dictionary lookup (exported or queried from Neo4j).
  2. Pattern-based heuristics (prefix/suffix stripping).
  3. Dialect etymological lookup.
- **[NEW]** `PatternHeuristics.cs`: Contains structural Arabic pattern matching rules.

### Solution Update
- **[MODIFY]** `LisanBits.slnx`: Add references to the new `Lisan.Tokenizer` and `Lisan.Morphology` shared library projects.

---

## 5. Epic 5: Dialect Data Pipeline
**Current State:** Missing. Raw parallel corpora (ARB-EGY-CMP) are downloaded in the data pipeline, but the logic to align words, build etymology maps, and extract syntactic patterns is absent.
**Plan Requirement:** Align words, map dialect to MSA roots, extract patterns, and AI gap-fill.

### Dialect Pipeline Project
- **[NEW]** Scaffold `src/LisanBits.DialectPipeline/LisanBits.DialectPipeline.csproj` (or a Python project).
- Implement a worker that runs `fast_align` on the parallel corpus.
- Map dialect terms to MSA roots via Neo4j and export the resulting alignment map to a SQLite database (`DialectEtymology.db`).

---

## 6. Epic 6: NLP Layer
**Current State:** Missing.
**Plan Requirement:** `Lisan.Syntax`, `Lisan.Dialect`, and `Lisan.Diacritization`.

### Lisan Syntax
- **[NEW]** `src/Shared/Lisan.Syntax/Lisan.Syntax.csproj`: Implement rule-driven parser using grammar rules extracted from Shamela.

### Lisan Dialect
- **[NEW]** `src/Shared/Lisan.Dialect/Lisan.Dialect.csproj`: Implement the dialect detection CNN (using TorchSharp), SQLite etymology lookup, and the MSA syntactic rewrite engine.

### Lisan Diacritization
- **[NEW]** `src/Shared/Lisan.Diacritization/Lisan.Diacritization.csproj`: Implement deterministic rule-based diacritization and integrate the 25M parameter TorchSharp neural refinement model.

---

## 7. Epic 7: RAG System + SSE Streaming
**Current State:** `LisanBits.WebApi` exists but only contains a background cache loader and a diagnostic endpoint. No Vector DB, no GraphRAG queries, no SSE endpoints are implemented.
**Plan Requirement:** FaissNet/HNSW vector search, GraphRAG engine, template responses, and SSE streaming chat endpoint.

### WebApi Expansion
- **[MODIFY]** `src/LisanBits.WebApi/LisanBits.WebApi.csproj`: Add `HNSW.Net` or `FaissNet` for vector retrieval.
- **[NEW]** `src/LisanBits.WebApi/Services/RagEngineService.cs`: Implement dual-root GraphRAG and vector retrieval with context tiering.
- **[NEW]** `src/LisanBits.WebApi/Endpoints/ChatEndpoints.cs`: Implement the `/v1/chat/completions` endpoint using SSE (Server-Sent Events) streaming.
- **[NEW]** `src/LisanBits.WebApi/Endpoints/DialectEndpoints.cs`: Expose the dialect reconstruction and morphology APIs.

---

## Execution Constraints
- All edits must be validated by building the solution (`dotnet build`).
- Ensure graceful degradation paths for all extraction failures.
- No direct modification to the PyTorch standard library behavior, only orchestration via .NET.

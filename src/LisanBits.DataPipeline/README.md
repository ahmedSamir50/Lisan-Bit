# LisanBits.DataPipeline

## Overview
`LisanBits.DataPipeline` is the heavy-lifting engine of the Lisan Bits project. It is a high-performance background worker that continuously ingests Arabic data (web and local corpora), then preprocesses it into structured NLP outputs.

## Core Philosophy: Infinite + Safe Ingestion
The pipeline is designed for long-running ingestion, but with strict operational safety:
- No destructive recovery steps are required for normal operation.
- Local-corpus sources are readiness-gated (download/extract/fix must complete before queue processing starts).
- DB writes are serialized to avoid `SQLITE_BUSY` issues under high concurrency.

## Key Components

### 1. The Universal Scraper (`Acquisition/UniversalHtmlScraper.cs`)
- **Purpose:** A dynamic HTML parser capable of scraping any website without needing a dedicated class per site.
- **Logic:** It relies on the `DataSourceConfigs` SQLite table to determine the `BaseUrl` and `TargetXPath`.
- **Supported local formats:**
  - `CSV_FILE` (chunked, queue-pagination via `?skip=`)
  - `JSONL_TEXT_FILE` (chunked JSON-lines parsing, extracts `text` field, queue-pagination via `?skip=`)
- **Concurrency:** Driven by `Worker.cs`, it launches parallel asynchronous tasks to scrape multiple websites simultaneously.

### 2. The Orchestrator (`Worker.cs`)
- **Purpose:** The main loop of the background service.
- **Logic:** It queries active sources and runs dedicated source loops.
- **Thread Safety:** SQLite locks on concurrent writes. To prevent `SQLITE_BUSY` exceptions from 15 parallel scraping threads, the worker utilizes a static `SemaphoreSlim(1,1)`. This ensures that while network IO happens in full parallel, database writes are strictly serialized.
- **Local corpus safety gates (Id 29 and Id 30):**
  - Source processing does not start unless the expected local file exists.
  - Dataset preparation is serialized with a dedicated prep lock to avoid duplicate runs and file-lock races.
  - Source 30 (ARB-EGY-CMP) flow: download -> unzip -> fix encoding -> atomic publish of corrected file.
- **Downloader behavior:** direct anonymous URL download is attempted first; authenticated Kaggle fallback is only used on `401/403`.

### 3. Entity Framework Core (`Data/PipelineDbContext.cs`)
- **Purpose:** Manages the `pipeline.db` SQLite database.
- **Tables:**
  - `DataSourceConfigs`: The URLs, XPaths, and Categories of websites to scrape.
  - `RawUniversalData`: The raw Arabic text dumped by the Universal Scraper.
  - `ProcessedUniversalData`: The structured NLP data resulting from the Farasa pipeline.
  - `ScrapeJobs`: Tracks pagination and state checkpoints to ensure the scraper resumes where it left off.

### 4. The Farasa Preprocessing Service (`Preprocessing/FarasaPreprocessingService.cs`)
- **Purpose:** Converts raw Arabic text into structured NLP tokens with Multi-Label Context Vectors.
- **Logic:** Runs in the background, pulling batches of 50 rows and calling `/analyze` on the configured Farasa endpoint.
- **Endpoint behavior:** now uses an absolute URI resolved from configuration (`FarasaApi:BaseUrl` + `FarasaApi:AnalyzePath`) with service-discovery-friendly defaults.
- **Quality mode:** controlled by `FarasaApi:AllowFallback`.
  - `false` (recommended): strict mode, do not write heuristic fallback NLP.
  - `true`: fallback tokenization is allowed when Farasa is unavailable.
- **Output:** stores `RootSequence`, `PosSequence`, and category-aware `ContextVector` JSON.

## Active Source Notes
- **Id 29: Nofal Slang (Local CSV|XLS|XLSX)**
  - Base path is config-driven (`SlangDataset:CsvPath`).
  - Intended as local import source.
- **Id 30: ARB-EGY-CMP (Local JSON)**
  - Base path is config-driven (`SlangDataset:CmpCorrectedPath`).
  - Uses `JSONL_TEXT_FILE` target type.
  - Readiness-gated and safe for always-on activation.

## Configuration Keys (Current)
- `FarasaApi:BaseUrl`
- `FarasaApi:AnalyzePath`
- `FarasaApi:AllowFallback`
- `DashboardHubUrl`
- `SlangDataset:CsvPath`
- `SlangDataset:KaggleUrl`
- `SlangDataset:CmpKaggleUrl`
- `SlangDataset:CmpCorrectedPath`
- `SlangDataset:CmpWordsThreshold`

## Execution Flow
1. **Source loop:** for each active source, worker verifies readiness (local datasets) then processes queue items.
2. **Ingestion:** scraper extracts text and stores rows in `RawUniversalData`.
3. **Progress broadcast:** worker sends progress to dashboard hub.
4. **NLP loop:** `FarasaPreprocessingService` processes raw rows into `ProcessedUniversalData`.

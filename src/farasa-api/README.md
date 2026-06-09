# farasa-api

## Overview
`farasa-api` is a localized container wrapper for the Farasa NLP tools. Since Farasa is historically built in Java/Python by the Qatar Computing Research Institute (QCRI), it cannot be natively imported into a high-performance .NET C# process without heavy overhead. This project solves that by hosting Farasa as a standalone HTTP microservice.

## Core Logic & Purpose
The goal is to provide a clean, RESTful API (`/analyze`) that the `LisanBits.DataPipeline` (written in C#) can invoke to perform heavy linguistic operations on raw Arabic text.

## Key Components

### 1. `main.py` (FastAPI)
- **Logic:** Hosts a Python ASGI web server using FastAPI. 
- **Current Implementation:** Attempts real Farasa initialization at startup (`farasapy` + Java JARs). If initialization fails, service remains available with a naive fallback tokenizer.
- **Response Contract:** returns JSON `{"tokens": [{"word": "...", "root": "...", "pos": "..."}]}`.
- **Health Endpoint:** `GET /health` returns `{"status": "healthy", "farasa_active": <bool>}` to indicate whether real Farasa is active.

### 2. `Dockerfile`
- **Logic:** Packages the Python FastAPI app into a container image. 
- **JAR handling:** supports Farasa JAR bundles packaged as `.tar.gz` artifacts and extracts them into `/app/farasa_jars`.
- **Orchestration:** `LisanBits.AppHost` builds/runs this image and exposes endpoint `farasa-endpoint` for the Data Pipeline.

## Execution Flow
1. Receives a POST request with `{"text": "Arabic sentence"}`.
2. Tokenizes the text.
3. Applies morphological algorithms to extract the Root.
4. Identifies the Part of Speech.
5. Returns a JSON array of `TokenResponse` objects back to the caller.

When real Farasa jars are unavailable, service-level fallback tokenization is used to keep `/analyze` available.

## Operational Note
Data quality enforcement is controlled in DataPipeline (`FarasaApi:AllowFallback`). Recommended production setting is `false` to avoid writing heuristic NLP data when Farasa is down.

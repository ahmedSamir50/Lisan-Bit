# LisanBits.AppHost

## Overview
`LisanBits.AppHost` is the orchestrator of the entire Lisan Bits distributed architecture, powered by **.NET Aspire**. It acts as the central control plane, responsible for booting up, configuring, and wiring the various microservices and background workers that constitute the Lisan Bits pipeline.

## Core Logic & Purpose
In a microservices environment, managing port bindings, connection strings, and startup order can become a chaotic bottleneck. `AppHost` solves this by defining the infrastructure as code (IaC) using C#. 

It ensures that:
1. All sub-projects start in the correct dependency order.
2. The endpoints (like the Farasa NLP container) are correctly injected into the consuming applications.
3. Telemetry, logging, and health checks are centrally aggregated.

## Key Components

### `AppHost.cs`
This is the entry point of the orchestrator.
- **Farasa API Node:** It provisions the `farasa-api` Docker container using `builder.AddDockerfile()`, mapping it to a local endpoint (`farasa-endpoint`). This exposes the Java/Python NLP engine to the rest of the .NET ecosystem.
- **Data Pipeline Node:** It spins up the `LisanBits.DataPipeline` worker process. Crucially, it uses `.WithReference(farasaApi.GetEndpoint("farasa-endpoint"))` to securely pass the Farasa URL environment variables into the Data Pipeline, ensuring the pipeline knows exactly how to contact the NLP engine without hardcoded IP addresses.
- **Dashboard Node:** It boots the Blazor Server UI `LisanBits.Dashboard` and adds a dependency on the Data Pipeline. This dependency ensures the Data Pipeline starts first (allowing EF Core to run SQLite database migrations) before the Dashboard attempts to query the `pipeline.db` file.

## Current Endpoint Contract (2026-06)
- `farasa-api` exposes endpoint name `farasa-endpoint` (container target port `8000`).
- DataPipeline should consume Farasa via `FarasaApi:BaseUrl` using service-discovery-friendly host naming (`http://farasa-endpoint`).
- Dashboard hub endpoint is `/scraperhub` and is used by DataPipeline to push live progress updates.

## Execution Flow
When you run `dotnet run` on this project, the Aspire AppHost:
1. Starts the Docker engine (if required) and spins up `farasa-api`.
2. Starts the `LisanBits.DataPipeline` Worker.
3. Starts the `LisanBits.Dashboard`.
4. Provides a localized Aspire Dashboard (usually at port 18888) to monitor the live logs, traces, and metrics of all 3 running nodes.

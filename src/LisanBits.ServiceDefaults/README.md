# LisanBits.ServiceDefaults

## Overview
`LisanBits.ServiceDefaults` is a shared C# library that standardizes the infrastructure wiring for every .NET project in the Lisan Bits solution.

## Core Logic & Purpose
In a cloud-native or microservices environment, boilerplate code for observability and resilience is repetitive. If every project (Dashboard, DataPipeline, WebApi) had to manually configure OpenTelemetry, Prometheus exporters, and HTTP resilience policies, the codebase would become bloated and error-prone.

This project exposes the `AddServiceDefaults()` extension method.

## Key Components

### 1. `Extensions.cs`
- **OpenTelemetry (Otel):** Automatically configures distributed tracing, logging, and metrics. This means when a request flows from the Dashboard to the Database, or from the DataPipeline to the Farasa API, the Aspire Dashboard can render a visual timeline of the execution.
- **Health Checks:** Registers `/health` and `/alive` endpoints to ensure Kubernetes/Aspire knows if the service is healthy.
- **Service Discovery:** Integrates standard .NET service discovery so HTTP clients can seamlessly resolve URLs like `http://farasa-endpoint` without needing explicit IPs.

## Execution Flow
Any .NET executable in the solution simply calls `builder.AddServiceDefaults();` in its `Program.cs`. This single line guarantees the project conforms to the centralized observability and resilience standards.

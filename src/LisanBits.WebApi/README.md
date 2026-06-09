# LisanBits.WebApi

## Overview
`LisanBits.WebApi` is currently a placeholder project designed to serve as the final HTTP interface for the trained 1-Bit Large Language Model (LLM). 

## Core Logic & Purpose
While the `DataPipeline` handles data ingestion (Epic 1), and Neo4j handles data structuring (Epic 2), this Web API will eventually handle **Inference** (Epic 4). 

Once the `LisanBitModel` is trained and quantized to a 1-Bit/1.58-Bit format, it will be loaded into memory by this C# Web API. 

## Key Components

### 1. `Program.cs`
- **Logic:** Configures a minimal API architecture using .NET 10.
- **Service Defaults:** Calls `builder.AddServiceDefaults()` to hook into the Aspire telemetry and health monitoring system.

## Future Execution Flow (Epic 4)
1. User sends a POST request (`/chat`) to this API.
2. The API uses custom C# `System.Numerics.Tensors` or SIMD CPU intrinsics to run forward propagation on the quantized Ternary matrices.
3. The API decodes the output logits back into Arabic text and streams the response back to the user over HTTP/SignalR.

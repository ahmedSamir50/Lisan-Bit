# LisanBits.Dashboard

## Overview
`LisanBits.Dashboard` is the graphical command center for the Lisan Bits Model. It is a web application built using **Blazor Server** running on .NET 10. It provides real-time visibility and control over the data ingestion and preprocessing pipeline.

## Core Logic & Purpose
Because the `DataPipeline` runs endlessly in the background as an "Infinite Corpus" generator, researchers need a way to monitor its progress, see exactly how much Arabic text has been scraped, and configure new targets on the fly. The Dashboard fulfills this need without requiring direct SQLite database manipulation.

## Key Components

### 1. `Program.cs` (EF Core + SignalR Safety)
- **Logic:** The dashboard uses an absolute path to connect to the exact same `pipeline.db` SQLite file that the `LisanBits.DataPipeline` worker uses. 
- **Concurrency:** It registers `IDbContextFactory<PipelineDbContext>` rather than a scoped `DbContext`. Blazor Server maintains a persistent SignalR connection, and using a factory ensures that concurrent UI events (like auto-refreshing stats) don't trigger EF Core "second operation started" lock exceptions.
- **Hub route safety:** status-code re-execution and HTTPS redirection are bypassed for `/scraperhub` traffic so SignalR protocol frames are not rewritten.

### 2. The Command Center (`Components/Pages/Home.razor`)
- **Data Constitution (Stats):** Queries the `RawUniversalData` table, groups by `Category`, and visualizes the total Sentence and Word counts. It visually tracks the dataset's entropy (highlighting categories that naturally expand).
- **Active Scraper Jobs:** Displays the `ScrapeJobs` table to show which pagination indices the Universal Scraper is currently processing in real-time.
- **Dynamic Configuration (Add New Source):** A form that inserts directly into the `DataSourceConfigs` table. It utilizes a `<datalist>` to allow users to either pick existing categories (Religion, Medical, Science) or type entirely new ones (e.g., "Philosophy"). The background `Worker` immediately picks up new rows on its next iteration and begins scraping.
- **Live updates:** combines push (`ProgressUpdated` via SignalR) and timer refresh fallback (5 seconds) to keep counters moving even if a push event is missed.
- **Reset Controls:** Provides highly destructive buttons that execute raw `DELETE` SQL commands to wipe specific sources or the entire database, useful during iterative development.

### 3. Hub + Progress Service
- `Hubs/ScraperHub.cs` receives `BroadcastProgress(sourceName, newIndex)` calls from DataPipeline and rebroadcasts to all clients.
- `Services/ScraperProgressService.cs` bridges hub events to UI components (`OnProgressUpdated`).

## Execution Flow
1. User opens the browser. Blazor establishes a SignalR connection.
2. `Home.razor`'s `OnInitializedAsync` loads the initial stats from SQLite.
3. Worker progress events are pushed through `/scraperhub` and applied in the page.
4. A background timer invokes `LoadData()` every 5 seconds as safety fallback.

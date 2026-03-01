# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Before writing code or implementing changes, create a plan and verify it with the user.

## Build & Run Commands

```bash
# Build everything
dotnet build

# Run the server-hosted Blazor app (includes simulation API)
dotnet run --project src/ElectionSim.Server

# Run the standalone Blazor WebAssembly app (no API)
dotnet run --project src/ElectionSim.Web

# DataTools CLI commands
dotnet run --project src/ElectionSim.DataTools -- generate-sample  # Generate dev data (343 ridings)
dotnet run --project src/ElectionSim.DataTools -- download          # Download Elections Canada CSVs
dotnet run --project src/ElectionSim.DataTools -- process           # Process raw data into JSON
dotnet run --project src/ElectionSim.DataTools -- scrape-338        # Scrape 338Canada polling
dotnet run --project src/ElectionSim.DataTools -- scrape-cbc        # Scrape CBC Poll Tracker
dotnet run --project src/ElectionSim.DataTools -- demographics      # Download/process census demographics
dotnet run --project src/ElectionSim.DataTools -- all               # Run full pipeline
```

There are no test projects currently.

## Architecture

Four projects in `src/`, all targeting .NET 10.0 (preview) with C# 12+ and nullable reference types:

- **ElectionSim.Core** — Pure library with no external dependencies. Contains domain models (`Models/`) and simulation logic (`Simulation/`).
- **ElectionSim.Web** — Blazor WebAssembly SPA. Loads JSON data from `wwwroot/data/`, manages state via DI-scoped services, renders interactive dashboard with Razor components.
- **ElectionSim.Server** — ASP.NET Core host for the Blazor WASM app. Exposes a private API (`POST /api/simulation/run`, `GET /api/simulation/latest`) to programmatically run simulations and persist results to `simulations/{year}/{datetime}.json`. On client startup, the latest snapshot is loaded asynchronously.
- **ElectionSim.DataTools** — Console app for the data pipeline. Downloads/scrapes election data, processes it into JSON. Dependencies: AngleSharp (HTML parsing), CsvHelper (CSV processing).

### Data & Simulation Docs

- **[ARCHITECTURE.md](ARCHITECTURE.md)** — Project dependency graph, data flow diagrams, simulation pipeline detail, and component tree.
- **[DATA.md](DATA.md)** — Data sources, file locations, processing pipeline, and JSON schemas.
- **[SIMULATION.md](SIMULATION.md)** — Monte Carlo simulation methodology, noise model, validation framework, calibration history, and known limitations. References `data/validation-results.json`.
- **[TERMINOLOGY.md](TERMINOLOGY.md)** — Glossary of electoral, statistical, and simulation domain terms.

### Web UI Structure

`SimulationState` (scoped service) holds polling inputs, config, and results; fires `OnStateChanged` for reactive updates. Key components:
- **PollInputPanel** — National/regional vote share sliders
- **SimulationControls** — Run button, baseline year selector, config parameters
- **SeatBarChart** — Seat projections with confidence intervals (P5–P95)
- **CanadaMap** — Grid cartogram colored by predicted winner, opacity = win probability
- **RidingDetailPopup** — Per-riding win probabilities and historical results

Each Razor component has a paired scoped `.razor.css` file. Party colors come from `PartyColorProvider.GetColor()`.

### Key Enums

- **Party**: LPC, CPC, NDP, BQ, GPC, PPC, Other
- **Region**: Atlantic, Quebec, Ontario, Prairies, BritishColumbia, North

All domain models use C# `record` types with `System.Text.Json` camelCase serialization.

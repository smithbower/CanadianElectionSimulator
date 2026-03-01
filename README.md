# Canadian Election Simulator

A Monte Carlo simulator for Canadian federal elections. Combines regional polling data with historical riding-level results to produce probabilistic seat projections, riding-level win probabilities, and confidence intervals.

The model uses census data from [Statistics Canada](https://www150.statcan.gc.ca/n1/en/catalogue/98-401-X2021029), riding-level election outcomes from [Elections Canada](https://www.elections.ca/res/rep/off/ovrGE45/home.html), and polling data from [338Canada](https://338canada.com/polls.htm). Polls are aggregated using [CBC Poll Tracker's methodology](https://newsinteractives.cbc.ca/elections/poll-tracker/canada/), and 338Canada's polling firm ranking.

NOTE: This tool was developed with clanker help - specifically, Claude Code. The CLAUDE.md file has been included in the repo.

## Quick Start

```bash
# Build
dotnet build

# Generate sample data (no real data files needed)
dotnet run --project src/ElectionSim.DataTools -- generate-sample

# Run the Blazor WebAssembly app
dotnet run --project src/ElectionSim.Web

# Recommended: Or run the server-hosted app (includes simulation API)
dotnet run --project src/ElectionSim.Server
```

## Projects

| Project | Description |
|---------|-------------|
| **ElectionSim.Core** | Domain models and simulation engine  |
| **ElectionSim.Web** | Blazor WebAssembly dashboard with interactive polling sliders, seat chart, and hex cartogram |
| **ElectionSim.Server** | ASP.NET Core host for the WASM app; exposes a simulation API and persists snapshots |
| **ElectionSim.DataTools** | CLI for downloading, processing, and validating election data |
| **ElectionSim.DailyRunner** | Automated poll scraper (sources from 338Canada) that triggers server-side simulations on new data |

## Data Pipeline

```bash
dotnet run --project src/ElectionSim.DataTools -- download      # Elections Canada CSVs
dotnet run --project src/ElectionSim.DataTools -- process       # Transform to JSON
dotnet run --project src/ElectionSim.DataTools -- demographics  # Census data
dotnet run --project src/ElectionSim.DataTools -- validate      # Hindcast tests
```

## Documentation

- **[ARCHITECTURE.md](ARCHITECTURE.md)** -- Project structure, data flow, and simulation pipeline diagrams.
- **[DATA.md](DATA.md)** -- Data sources, directory layout, processing pipeline, and JSON schemas.
- **[SIMULATION.md](SIMULATION.md)** -- Monte Carlo methodology, noise model, validation framework, and calibration history.
- **[CLAUDE.md](CLAUDE.md)** -- Build commands, architecture overview, and conventions for AI-assisted development.
- **[LICENSE.txt](LICENSE.txt)** -- Project license document (MIT license)

## Tech Stack

- .NET 10.0 (preview), C# 12+, nullable reference types
- Blazor WebAssembly (standalone + server-hosted)
- AngleSharp + CsvHelper for data pipeline
- Playwright for automated poll scraping

## License
This project is MIT licensed. Please feel free to fork and use at your discretion! But if you do, please provide attribution.

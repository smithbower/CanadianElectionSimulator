# Architecture

## Project Dependency Graph

```
ElectionSim.DailyRunner ──→ ElectionSim.Core
       │
       └──→ (HTTP) ──→ ElectionSim.Server ──→ ElectionSim.Core
                              │
                              └──→ (hosts) ──→ ElectionSim.Web ──→ ElectionSim.Core

ElectionSim.DataTools ──→ ElectionSim.Core
```

- **ElectionSim.Core** is referenced by all other projects. It has zero external dependencies.
- **ElectionSim.Web** references Core and is hosted by Server (or runs standalone).
- **ElectionSim.Server** references Core directly for server-side simulation.
- **ElectionSim.DataTools** references Core for shared model types and simulation logic (validation).
- **ElectionSim.DailyRunner** references Core for model types; communicates with Server via HTTP.

## Data Flow

### Offline Pipeline (DataTools)

```
Elections Canada CSVs ──→ DownloadCommand ──→ data/raw/*.csv
338Canada / CBC HTML  ──→ Scrape*Command  ──→ data/raw/*.html
StatsCan Census CSV   ──→ DemographicsCmd ──→ data/raw/census-*.csv

data/raw/* ──→ ProcessCommand ──→ data/processed/*.json
                               ──→ src/ElectionSim.Web/wwwroot/data/*.json

data/processed/* ──→ ValidateCommand ──→ data/validation-results.json
```

Processed JSON files are written to two locations: `data/processed/` (canonical copy) and
`src/ElectionSim.Web/wwwroot/data/` (served to the Blazor app at runtime).

### Runtime (Web + Server)

```
wwwroot/data/*.json
       │
       ▼
   DataService          Fetches JSON via HttpClient on app startup
       │
       ▼
 SimulationState        Holds polling inputs, config, and results; fires OnStateChanged
       │
       ▼
 SimulationPipeline     Projects riding-level vote shares (swing model + optional demographic prior)
       │
       ▼
MonteCarloSimulator     Runs N parallel simulations with correlated Student-t noise
       │
       ▼
 SimulationSummary      Seat distributions, riding win probabilities, vote share percentiles
       │
       ▼
   UI Components        SeatBarChart, CanadaMap, PollInputPanel, RegionBreakdownTable, etc.
```

### Automated Daily Run (DailyRunner)

```
338Canada poll pages ──→ PollScraper (Playwright) ──→ ScrapedPoll[]
                                                          │
                                                          ▼
                                                   PollWeightCalculator
                                                   (age/size/grade weighting)
                                                          │
                                                          ▼
                                               Weighted regional averages
                                                          │
                                                          ▼
                                              POST /api/simulation/run
                                                   (SimulationApiClient)
                                                          │
                                                          ▼
                                              SimulationBackgroundService
                                                   processes the queue
                                                          │
                                                          ▼
                                              SimulationService.RunSimulationAsync()
                                                          │
                                                          ▼
                                              simulations/{year}/{datetime}.json
                                              + trend-cache.json updated
```

## Simulation Pipeline Detail

The simulation runs in two phases: **projection** and **Monte Carlo sampling**.

### Phase 1: Vote Share Projection

```
Regional polls + Baseline election results
       │
       ▼
SwingCalculator.ComputeSwingRatios()       Proportional: poll / baseline_avg
SwingCalculator.ComputeAdditiveDeltas()    Additive: poll - baseline_avg
       │
       ▼
SwingCalculator.ProjectRidingVoteSharesBlended()
  For each riding:
    proportional = baseline_share * swing_ratio
    additive     = baseline_share + additive_delta
    blended      = alpha * proportional + (1-alpha) * additive
       │
       ▼
(Optional) BlendWithDemographicPrior()     Ridge regression from census variables
       │
       ▼
double[riding, party] projected vote shares
```

### Phase 2: Monte Carlo Sampling

```
For each of N simulations (default 10,000):
  ┌─────────────────────────────────────────────────────┐
  │ 1. Draw national noise     (1 correlated vector)    │
  │ 2. Draw regional noise     (7 correlated vectors)   │
  │ 3. For each of 343 ridings:                         │
  │    a. Draw riding noise    (1 correlated vector)     │
  │    b. adjusted = projected + national + regional     │
  │                           + riding noise             │
  │    c. Clamp negatives, renormalize to sum=1          │
  │    d. Winner = argmax(adjusted)  [FPTP]              │
  │ 4. Record seat counts, riding winners, histograms   │
  └─────────────────────────────────────────────────────┘

Noise at each level:
  - Student-t distribution (df=5) for heavy tails
  - Cholesky-decomposed inter-party correlation matrix
  - Per-party sigma (national), per-region multiplier (regional/riding)
```

### Aggregation

After all simulations complete:

- **Seat distributions**: mean, median, P5/P25/P75/P95, min, max per party
- **Government formation**: majority (172+ seats) and minority (plurality < 172) probabilities
- **Riding win probabilities**: fraction of simulations won by each party
- **Vote share distributions**: per-riding per-party percentiles from 0.5%-bin histograms

## Web UI Component Tree

```
App
 └─ MainLayout
     └─ Home (page)
         ├─ NavHeader                    Jump links between result cards
         ├─ PollInputPanel               National + regional vote share sliders
         ├─ SimulationControls           Run button, baseline year, config params
         ├─ SeatBarChart                 Horizontal bar chart with CI whiskers
         ├─ SeatHistogram                Per-party seat count distribution
         ├─ GovernmentFormation          Majority/minority probability display
         ├─ CanadaMap                    Hex cartogram (343 hexagons)
         ├─ RegionBreakdownTable         Regional seat/vote projections
         ├─ CloseRidingsTable            Competitive ridings (sorted by margin)
         ├─ FlippedRidingsTable          Ridings that changed party from baseline
         ├─ TrendLineChart               Historical seat/probability trends
         └─ RidingDetailPopup            Modal with per-riding details
```

Each component has a paired scoped `.razor.css` file. Components with complex logic use a
`.razor.cs` code-behind partial class (CloseRidingsTable, FlippedRidingsTable, TrendLineChart).

## Server API

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/health` | GET | None | Health check |
| `/api/simulation/run` | POST | X-Api-Key | Enqueue a simulation request |
| `/api/simulation/latest` | GET | None | Most recent simulation snapshot |
| `/api/simulation/trends` | GET | None | Aggregated trend data from all snapshots |
| `/api/simulation/snapshot?timestamp=` | GET | None | Specific snapshot by timestamp |

The `/api/simulation/run` endpoint uses a bounded channel (capacity=1, drop-oldest) so that
only the latest polling data gets simulated. `SimulationBackgroundService` processes the queue
sequentially.

## Key Design Decisions

- **Pure Core library**: No framework dependencies in ElectionSim.Core, enabling reuse across
  Blazor WASM (single-threaded), server (multi-threaded), and CLI (validation).
- **Parallel.For with thread-local state**: Monte Carlo simulations use thread-local RNG and
  histogram arrays, merged via lock after completion. The WASM variant (`RunAsync`) yields to
  the UI thread between 5% batches.
- **Record types everywhere**: All domain models are immutable records with camelCase JSON
  serialization via `System.Text.Json`.
- **Scoped state service**: `SimulationState` acts as a lightweight state store with
  `OnStateChanged` events for reactive Blazor re-rendering, avoiding a full state management
  library.
- **Trend cache**: Historical trend data is cached in `trend-cache.json` and incrementally
  updated (O(1) append) rather than rebuilt from all snapshots (O(n)) on each request.

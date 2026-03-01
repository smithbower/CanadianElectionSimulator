# Data Sources & Pipeline

This document describes the data sources used by the election simulator, where files live in the project, and how the processing pipeline works.

## Data Sources

### Elections Canada (Official Results)

Official riding-level election results from Elections Canada, downloaded as CSV files.

| Election | Ridings | Source | Local file |
|----------|---------|--------|------------|
| 2025 (45th) | 343 | Elections Canada ZIP | `data/raw/2025_results_elections_canada.csv` |
| 2021 (44th) | 338 | Elections Canada CSV | `data/raw/2021_results_elections_canada.csv` |
| 2019 (43rd) | 338 | Elections Canada CSV | `data/raw/2019_results_elections_canada.csv` |
| 2015 (42nd) | 338 | Elections Canada CSV | `data/raw/2015_results_elections_canada.csv` |
| 2011 (41st) | 308 | Elections Canada CSV | `data/raw/2011_results_elections_canada.csv` |
| 2008 (40th) | 308 | Elections Canada CSV | `data/raw/2008_results_elections_canada.csv` |

Each CSV contains one row per candidate with fields: province, riding name, riding number, candidate name (with party suffix), and vote count. Newer files (2015+) are UTF-8 encoded; older files (2008, 2011) use Windows-1252 encoding — the processor auto-detects this.

### 338Canada (Polling Projections)

Scraped from `338canada.com/federal.htm` using AngleSharp. Saves raw HTML to `data/raw/338canada-federal.html`. The scraper is a stub that saves the page but does not parse projection data (site format changes frequently).

### CBC Poll Tracker

Scraped from `newsinteractives.cbc.ca/elections/poll-tracker/canada/` using AngleSharp. Saves raw HTML to `data/raw/cbc-poll-tracker.html` and attempts to extract embedded JSON from `<script>` tags. Also a stub — format-specific parsing not implemented.

### Statistics Canada Census (Demographics)

2021 Census Profile data for all 343 federal electoral districts (2023 Representation Order). Downloaded as a bulk CSV from Statistics Canada (catalogue 98-401-X2021029). Contains ~2,600 characteristics per riding; the pipeline extracts 9 key demographic variables: median income, education, visible minority %, immigrant %, francophone %, population density, median age, Indigenous %, and homeownership rate.

| Data | Source | Local file |
|------|--------|------------|
| Census Profile (FED 2023 RO) | StatsCan 98-401-X2021029 | `data/raw/census-2021-fed-2023ro.csv` |

### Generated Sample Data

`SampleDataGenerator` creates synthetic data for 343 ridings for development/testing without requiring real data files.

## Directory Structure

```
data/
  raw/                              # Raw downloaded/scraped files (not committed)
    2025_results_elections_canada.csv
    2021_results_elections_canada.csv
    2019_results_elections_canada.csv
    2015_results_elections_canada.csv
    2011_results_elections_canada.csv
    2008_results_elections_canada.csv
    338canada-federal.html
    cbc-poll-tracker.html
  processed/                        # Processed JSON (committed)
    ridings.json                    # 343 riding definitions (id, name, province, region, lat/lng)
    results-2025.json               # 2025 riding-level results
    results-2021.json               # 2021 results mapped to 2025 riding IDs (338→343)
    results-2019.json               # 2019 results mapped to 2025 riding IDs (338→343)
    results-2015.json               # 2015 results mapped to 2025 riding IDs (338→343)
    results-2011.json               # 2011 results mapped to 2025 riding IDs (308→343)
    results-2008.json               # 2008 results mapped to 2025 riding IDs (308→343)
    polling.json                    # Regional polling averages
    hex-layout.json                 # Hex cartogram positions for map visualization
    demographics.json               # Census demographic profiles per riding (9 variables)
  validation-results.json           # Output of the validate command (see SIMULATION.md)

src/ElectionSim.Web/wwwroot/data/   # Copies of processed JSON served to the Blazor app
    ridings.json
    results-2025.json
    results-2021.json
    polling.json
```

Both `data/processed/` and `src/ElectionSim.Web/wwwroot/data/` receive identical copies of the core JSON files during processing.

## Processing Pipeline

The pipeline is implemented in `src/ElectionSim.DataTools/` and driven by CLI commands (see `Program.cs`).

### Step 1: Download (`download`)

**File:** `Commands/DownloadCommand.cs`

Downloads Elections Canada CSV files to `data/raw/`. Skips files that already exist. The 2025 data comes as a ZIP archive; 2021 is a direct CSV download.

```bash
dotnet run --project src/ElectionSim.DataTools -- download
```

### Step 2: Scrape (`scrape-338`, `scrape-cbc`)

**Files:** `Commands/Scrape338Command.cs`, `Commands/ScrapeCbcCommand.cs`

Fetches polling projection pages and saves raw HTML. These are stubs — they save the raw pages but do not parse structured data. Use `generate-sample` for development.

```bash
dotnet run --project src/ElectionSim.DataTools -- scrape-338
dotnet run --project src/ElectionSim.DataTools -- scrape-cbc
```

### Step 3: Process (`process`)

**File:** `Commands/ProcessCommand.cs`

Transforms raw CSVs into JSON files consumed by the simulator and web app.

1. **Parse 2025 CSV** to establish the master riding list (343 ridings). Extracts riding ID, name (bilingual split), province, region, and candidate results. Party is identified from suffixes in the candidate field (e.g., "Liberal/Liberal").

2. **Parse historical CSVs** (2021, 2019, 2015, 2011, 2008) and map old riding IDs to current 2025 riding IDs:
   - First tries name matching (normalized, case-insensitive).
   - Falls back to matching by riding number if the ID exists in 2025.
   - If multiple old ridings map to one new riding (redistricting), vote shares are averaged weighted by total votes.
   - 338-riding elections (2015–2021) map to ~279 of 343 current ridings.
   - 308-riding elections (2008–2011) map to ~251 of 343 current ridings due to the larger redistricting gap.

3. **Generate polling.json** from 2025 regional vote share averages (computed from actual results).

4. **Write outputs** to both `data/processed/` and `src/ElectionSim.Web/wwwroot/data/`.

```bash
dotnet run --project src/ElectionSim.DataTools -- process
```

### Step 4: Demographics (`demographics`)

**File:** `Commands/DemographicsCommand.cs`

Downloads and processes the 2021 Census Profile for 343 FEDs (2023 Representation Order). Extracts 9 demographic variables, computes proportions and normalizes values. Outputs `demographics.json` to both `data/processed/` and `src/ElectionSim.Web/wwwroot/data/`.

```bash
dotnet run --project src/ElectionSim.DataTools -- demographics
```

### Step 5: Validate (`validate`)

**File:** `Commands/ValidateCommand.cs`

Runs hindcast tests and model diagnostics against historical data. Writes detailed results to `data/validation-results.json`. See [SIMULATION.md](SIMULATION.md) for details.

```bash
dotnet run --project src/ElectionSim.DataTools -- validate
```

### Full Pipeline (`all`)

Runs download, scrape, process, and validate in sequence:

```bash
dotnet run --project src/ElectionSim.DataTools -- all
```

### Generate Sample Data (`generate-sample`)

**File:** `Commands/SampleDataGenerator.cs`

Creates synthetic riding and result data for development without requiring real Elections Canada CSVs.

```bash
dotnet run --project src/ElectionSim.DataTools -- generate-sample
```

## Data Flow

```
Elections Canada CSVs ──→ download ──→ data/raw/*.csv
338Canada / CBC pages ──→ scrape ────→ data/raw/*.html

data/raw/*.csv ──→ process ──→ data/processed/*.json
                             ──→ src/ElectionSim.Web/wwwroot/data/*.json

data/processed/*.json ──→ validate ──→ data/validation-results.json

wwwroot/data/*.json ──→ Blazor DataService ──→ SimulationState ──→ MonteCarloSimulator ──→ UI
```

## JSON Schemas

All JSON uses camelCase property names and string enum values (via `JsonStringEnumConverter`).

### ridings.json

Array of `Riding` records: `{ id, name, nameFr, province, region, latitude, longitude }`.

### results-{year}.json

Array of `RidingResult` records: `{ ridingId, year, candidates: [{ party, votes, voteShare }], totalVotes }`.

### polling.json

Array of `RegionalPoll` records: `{ region, voteShares: { party: share } }`.

### validation-results.json

Contains hindcast results, calibration bins, empirical sigma analysis, swing model comparison, and sigma sweep grid. See [SIMULATION.md](SIMULATION.md) for interpretation.

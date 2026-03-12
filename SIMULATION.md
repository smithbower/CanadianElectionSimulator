# Simulation Model

This document describes how the Monte Carlo election simulator works, how it has been validated, and how its parameters were calibrated.

## Overview

The simulator projects Canadian federal election outcomes by combining regional polling data with historical riding-level results. It runs thousands of simulated elections, each with randomized noise, to produce probability distributions for seat counts and riding-level winners.

**Key files:**
- `src/ElectionSim.Core/Simulation/MonteCarloSimulator.cs` — simulation engine
- `src/ElectionSim.Core/Simulation/SwingCalculator.cs` — swing projection logic
- `src/ElectionSim.Core/Models/SimulationConfig.cs` — configurable parameters
- `src/ElectionSim.Core/Models/SimulationResults.cs` — output types
- `src/ElectionSim.Core/Simulation/SimulationPipeline.cs` — projection pipeline (swing + by-election blending + demographic prior)
- `src/ElectionSim.Core/Models/PostElectionEvent.cs` — post-election event and by-election result types
- `src/ElectionSim.Core/Models/ParliamentaryState.cs` — parliamentary state derivation from events

## Swing Model

The simulator uses a **hybrid swing model** that blends proportional (ratio) and additive (uniform) swing to project riding-level vote shares from a baseline election. The blend is controlled by `SwingBlendAlpha` (0.0 = pure additive, 1.0 = pure proportional, default = 0.0).

### SwingCalculator

1. **Regional swing ratios** (proportional): For each party-region pair, compute `current_poll / baseline_regional_average`. This gives a multiplicative factor.

2. **Regional additive deltas**: For each party-region pair, compute `current_poll - baseline_regional_average`. This gives an absolute shift.

3. **Blended riding-level projection**: For each riding and party:
   ```
   proportional_proj = max(baseline_share, 0.005) * swing_ratio
   additive_proj     = baseline_share + additive_delta
   blended           = alpha * proportional_proj + (1 - alpha) * additive_proj
   ```
   Negative values are clamped to 0, then shares are renormalized to sum to 1.0.

### Why Additive Swing

Validation across 4 election transitions (2008→2011, 2011→2015, 2015→2021, 2021→2025) showed additive swing has 5.6% lower projection RMSE than proportional (0.0539 vs 0.0571). An alpha sweep across all transitions confirmed alpha=0.0 (pure additive) minimizes average Brier score, with monotonic improvement from proportional to additive. Large parties (LPC, CPC) benefit most from additive swing, while small parties (NDP, BQ, GPC) slightly favor proportional — but the aggregate effect favors additive.

The original `ProjectRidingVoteShares()` method is retained for backward compatibility.

## Monte Carlo Engine

`MonteCarloSimulator.Run()` executes N simulations (default 10,000) using `Parallel.For` for performance.

### Per-simulation steps

1. **Draw national noise**: One correlated random error vector for all parties, shared across all ridings. Each party has its own sigma from `DefaultPartyUncertainty` (derived from empirical residuals across 4 election transitions). Users can override via `PartyUncertainty`. When `UseCorrelatedNoise` is enabled (default), noise is drawn using Cholesky decomposition to create inter-party correlations that respect the compositional constraint of vote shares (see [Correlated Party Noise](#correlated-party-noise)).

2. **Draw regional noise**: One correlated random error vector per region. Uses `RegionalSigma * RegionalSigmaMultiplier[region]` to scale noise by regional volatility.

3. **Draw riding noise**: One correlated random error vector per riding. Uses `RidingSigma * RegionalSigmaMultiplier[region]` (same per-region multiplier).

4. **Compute adjusted vote shares**: For each riding and party:
   ```
   adjusted = projected_share + national_error + regional_error + riding_error
   ```
   Negative values are clamped to zero, then shares are renormalized to sum to 1.0.

5. **Determine winner**: FPTP — the party with the highest adjusted vote share wins the riding.

6. **Record results**: Seat counts per party, riding winners, and vote share histograms (0.5% bins) are accumulated across simulations.

### Noise Distribution

The simulator uses **Student-t noise** (default df=5) rather than Gaussian to capture heavy-tailed election surprises. This was motivated by empirical residual analysis showing excess kurtosis of ~19.8 (Gaussian would be 0).

- `NextStudentT(rng, df)`: Samples via `z / sqrt(v / df)` where `z ~ N(0,1)` and `v ~ chi-squared(df)`.
- A **scale factor** `sqrt((df-2)/df)` is applied so the effective standard deviation matches the configured sigma values. For df=5 this is ~0.775.
- When `DegreesOfFreedom` is null or infinity, the simulator falls back to Gaussian noise (Box-Muller transform).
- Only integer df values are supported (chi-squared sampled as sum of squared Gaussians).

### Correlated Party Noise

When `UseCorrelatedNoise` is enabled (default: true), party noise at each level (national, regional, riding) is drawn using **Cholesky-decomposed correlation matrices** rather than independently. This ensures that when one party receives a positive shock, competing parties tend to receive negative shocks — reflecting the zero-sum nature of vote share competition.

**Methodology:**

1. A 6×6 inter-party **correlation matrix** was estimated from demeaned residuals across 5 election transitions (2008→2011, 2011→2015, 2015→2019, 2019→2021, 2021→2025), using 1,431 riding observations. Residuals were demeaned within each riding to remove the common-mode component (which is already captured by sigma scaling and renormalization).

2. Small regularization (ε=0.01 on diagonal) restores full rank, since demeaned residuals for K parties have rank K−1.

3. The **Cholesky factor** L (lower-triangular, L·Lᵀ ≈ correlation matrix) is pre-computed and stored in `CorrelationData.cs`.

4. At simulation time, for each noise draw (national, regional, or riding level):
   ```
   z[p] = independent_noise_sample()  for p = 0..5
   correlated[p] = Σ_{q=0}^{p} L[p,q] * z[q]
   error[p] = correlated[p] * sigma[p]
   ```

**Key correlation structure:**

| Party Pair | Correlation | Interpretation |
|-----------|------------|----------------|
| LPC–BQ | −0.833 | Strong competition in Quebec |
| LPC–PPC | −0.928 | Strong inverse (PPC gains when LPC loses) |
| LPC–NDP | −0.582 | Left-of-center competition |
| NDP–PPC | +0.614 | Both benefit when LPC declines |
| BQ–PPC | +0.816 | Both benefit when LPC declines |
| LPC–CPC | −0.179 | Mild competition (less direct than expected) |
| CPC–NDP | −0.344 | Some competition |

The correlation matrix is applied identically at all three noise levels (national, regional, riding). Per-party sigma scaling is separate and user-adjustable. Setting `UseCorrelatedNoise = false` reverts to independent noise draws.

**Key files:**
- `src/ElectionSim.Core/Simulation/CorrelationData.cs` — pre-computed Cholesky factor
- `src/ElectionSim.Core/Simulation/MonteCarloSimulator.cs` — correlated noise generation

### By-Election Baseline Blending

When by-election results are available for a riding, the simulator blends them into the baseline before computing swing projections. This is applied at the very start of the projection pipeline — before swing calculation, demographic priors, or Monte Carlo noise.

**Methodology:**

1. Post-election events are loaded from `post-election-events.json` and filtered to the current parliament (matching the baseline election year).

2. For each riding that has a by-election result, the baseline vote shares are replaced with a weighted blend:
   ```
   effective_share[party] = (1 - w) * general_election_share + w * by_election_share
   ```
   where `w = ByElectionBlendWeight` (default 0.3 / 30%).

3. If a party appeared in the by-election but not the general election (or vice versa), the missing share is treated as 0. After blending, shares are renormalized to sum to 1.0.

4. Ridings without by-election results are unaffected — their baseline remains the general election result.

**Rationale:** By-elections provide more recent local signal than the last general election, but they also have different dynamics (lower turnout, protest voting, no government formation at stake). The 30% blend weight gives by-election results meaningful influence without overweighting these differences.

**Key files:**
- `src/ElectionSim.Core/Simulation/SimulationPipeline.cs` — `BlendByElectionBaselines()` method
- `src/ElectionSim.Core/Models/PostElectionEvent.cs` — `ByElectionResult` record
- `src/ElectionSim.Core/Models/SimulationConfig.cs` — `ByElectionBlendWeight` parameter

### Demographic Prior

When `UseDemographicPrior` is enabled (default: false), the simulator blends swing-projected vote shares with a census-based demographic prior. This is applied *before* the Monte Carlo noise stage — it modifies the projected vote shares that the simulator uses as its baseline.

**Methodology:**

1. **Ridge regression** is fitted for each party: `vote_share[riding, party] = β₀ + β₁·income + β₂·education + ... + β₉·homeowner + ε`, trained on historical election results paired with census demographics.

2. The fitted model produces `E[vote_share | demographics]` for every riding.

3. **Blending**: For each riding, `final = (1 - w) × projected + w × demographic_prior`, where `w = DemographicBlendWeight` (default 0.15). Ridings with no historical baseline data get `w = 1.0` (full demographic prior).

4. The blended shares are renormalized to sum to 1.0 before being passed to the Monte Carlo simulator.

**Census variables** (from Statistics Canada 2021 Census, retabulated to 2023 Representation Order):
- Median household income (normalized 0–1)
- % university-educated (Bachelor's+)
- % visible minority
- % immigrant
- % francophone (first official language spoken)
- Population density (log-transformed, normalized 0–1)
- Median age (normalized 0–1)
- % Indigenous identity
- % homeowner

**Key files:**
- `src/ElectionSim.Core/Models/RidingDemographics.cs` — demographic data record
- `src/ElectionSim.Core/Simulation/DemographicPrior.cs` — ridge regression and prediction
- `src/ElectionSim.Core/Simulation/SwingCalculator.cs` — `BlendWithDemographicPrior()` method
- `src/ElectionSim.DataTools/Commands/DemographicsCommand.cs` — census data pipeline
- `data/processed/demographics.json` — processed demographic data

### Default Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `NumSimulations` | 10,000 | Number of Monte Carlo iterations |
| `NationalSigma` | 6.0% | National-level noise standard deviation (fallback when no per-party value exists) |
| `RegionalSigma` | 2.6% | Regional-level noise standard deviation (base, before per-region multiplier) |
| `RidingSigma` | 6.5% | Riding-level noise standard deviation (base, before per-region multiplier) |
| `DegreesOfFreedom` | 5.0 | Student-t df (null = Gaussian) |
| `SwingBlendAlpha` | 0.0 | Swing model blend: 0.0 = pure additive, 1.0 = pure proportional |
| `RegionalSigmaMultipliers` | See below | Per-region multiplier applied to RegionalSigma and RidingSigma |
| `UseCorrelatedNoise` | true | Use Cholesky-based correlated inter-party noise (false = independent) |
| `ByElectionBlendWeight` | 0.3 | Weight given to by-election results when blending into baseline (0.0–1.0) |
| `UseDemographicPrior` | false | Blend census demographic prior into riding projections |
| `DemographicBlendWeight` | 0.15 | Weight given to demographic prior (0.0–1.0); ridings with no baseline get 1.0 |
| `Seed` | null | RNG seed for reproducibility (null = random) |

#### Per-Region Sigma Multipliers

Both `RegionalSigma` and `RidingSigma` are scaled by a per-region multiplier to account for geographic volatility differences. The effective sigmas for a riding in region R are:

```
effective_regional_sigma = RegionalSigma * multiplier[R]
effective_riding_sigma   = RidingSigma * multiplier[R]
```

National sigma remains uniform (it's a national-level draw shared across all ridings).

Default multipliers derived from empirical residual std dev ratios across 4 election transitions (region std / overall std of 25.2%). Defined in `SimulationConfig.DefaultRegionalSigmaMultipliers`:

| Region | Residual Std | Multiplier | Effective Regional | Effective Riding |
|--------|-------------|-----------|-------------------|-----------------|
| Alberta | 35.5% | 1.41 | 3.67% | 9.2% |
| Quebec | 30.6% | 1.22 | 3.17% | 7.9% |
| British Columbia | 26.8% | 1.06 | 2.76% | 6.9% |
| Prairies | 21.5% | 0.85 | 2.21% | 5.5% |
| Ontario | 20.3% | 0.81 | 2.11% | 5.3% |
| Atlantic | 10.9% | 0.43 | 1.12% | 2.8% |
| North | 5.1% | 0.50 (floor) | 1.30% | 3.3% |

The North multiplier uses a floor of 0.50 rather than the raw ratio (0.20) because the empirical estimate is unreliable with only 3 ridings. Users can override via `RegionalSigmaMultipliers` (null = use defaults).

#### Per-Party National Sigma Defaults

Derived from empirical residuals across 4 election transitions (2008→2011, 2011→2015, 2015→2021, 2021→2025), scaled to the noise component. Defined in `SimulationConfig.DefaultPartyUncertainty`.

| Party | Default Sigma | Rationale |
|-------|--------------|-----------|
| LPC | 12.0% | Highest volatility — large swings in 2015 and 2025 |
| CPC | 8.0% | Second most volatile, consistent across transitions |
| GPC | 7.0% | High relative to vote share; small-party overestimation effect |
| NDP | 6.0% | Moderate volatility |
| BQ | 4.0% | Most stable — concentrated in Quebec |
| PPC | 3.0% | Low absolute volatility; only existed since 2019 |

Combined effective sigma: ~7.94% (quadrature sum of national + regional + riding).

### Aggregation

After all simulations complete, results are aggregated into a `SimulationSummary`:

- **Seat distributions**: Per-party mean, median, P5/P25/P75/P95, min, max across all simulations.
- **Majority/minority probabilities**: Fraction of simulations where each party wins 172+ seats (majority) or leads with fewer (minority).
- **Riding win probabilities**: Per-riding fraction of simulations won by each party.
- **Vote share distributions**: Per-riding per-party percentiles computed from 0.5%-bin histograms.

### Performance

- `Parallel.For` with thread-local RNG and histogram arrays, merged via `lock` after completion.
- Pre-computed 2D arrays (`double[riding, party]`) for projected vote shares.
- `Interlocked.Increment` for thread-safe riding win counters.
- `RunAsync` variant yields to the UI thread between 5% batches for Blazor WebAssembly (single-threaded).

## Validation

The validation framework (`src/ElectionSim.DataTools/Commands/ValidateCommand.cs`) tests the model against historical data. Run it with:

```bash
dotnet run --project src/ElectionSim.DataTools -- validate
```

Results are written to `data/validation-results.json`.

### Hindcasting

Five hindcast tests predict a known election from the immediately prior election:
- **2025 from 2021**: Uses 2021 results as baseline, 2025 regional averages as "polls", predicts 2025 riding outcomes.
- **2021 from 2019**: Tests the minor shift between two Trudeau minority governments. Uses 338→343 riding mapping (279 mapped ridings).
- **2019 from 2015**: Tests the 2015 majority → 2019 minority transition. Uses 338→343 riding mapping (279 mapped ridings).
- **2015 from 2011**: Tests the large 2015 LPC surge / NDP collapse. Uses 308→343 riding mapping (251 mapped ridings).
- **2011 from 2008**: Tests the NDP Orange Wave (Layton). Uses 308→343 riding mapping (251 mapped ridings).

For each hindcast, the validator computes:
- **Seat MAE**: Mean absolute error between predicted mean seats and actual seats per party.
- **Riding accuracy**: Fraction of ridings where the predicted winner (highest win probability) matches the actual winner.
- **Brier score**: Multi-class Brier score across all ridings (lower is better; no-skill baseline ~0.25).
- **Log loss**: Average negative log-probability of the actual winner.
- **CI coverage**: Fraction of actual riding-level vote shares falling within the P5-P95 interval (target: 90%).

### Calibration Analysis

Win probabilities are binned into 10% buckets and compared against actual win rates. A well-calibrated model should show actual rates close to the bin midpoints (e.g., ridings predicted at 70-80% should be won ~75% of the time).

### Empirical Sigma Analysis

Computes residuals (actual vote share minus projected vote share, without noise) across all available election transitions (up to 5: 2008→2011, 2011→2015, 2015→2019, 2019→2021, 2021→2025). Reports:
- Overall residual standard deviation
- Breakdown by party and region
- Variance decomposition into national, regional, and riding components
- Recommended sigma values from the decomposition

### Swing Model Comparison

Compares proportional swing against additive swing using RMSE. Also reports residual skewness and excess kurtosis to assess distributional assumptions.

### Alpha Sweep

Sweeps `SwingBlendAlpha` from 0.0 to 1.0 (in 0.1 steps) across all 5 election transitions. For each alpha, runs 1,000 simulations per hindcast (seed=42) and reports per-transition Brier scores, average Brier, accuracy, CI coverage, and projection RMSE. Identifies the optimal alpha by minimum average Brier score.

### Sigma Sweep

Performs a grid search over national sigma [3-8%] x riding sigma [2-7%] with regional sigma fixed at 2.6% and df=5. Runs 1,000 simulations per combination (seed=42) and reports Brier score, CI coverage, and riding accuracy for each.

### Current Validation Results

From `data/validation-results.json` (5 hindcasts using consecutive elections, additive swing alpha=0.0, per-party national sigma, per-region sigma multipliers, correlated inter-party noise):

| Metric | 2025 from 2021 | 2021 from 2019 | 2019 from 2015 | 2015 from 2011 | 2011 from 2008 |
|--------|---------------|---------------|---------------|---------------|---------------|
| Riding accuracy | 77.0% (264/343) | 93.2% (260/279) | 87.1% (243/279) | 59.5% (166/279) | 90.4% (227/251) |
| Brier score | 0.3749 | 0.1671 | 0.2185 | 0.5726 | 0.1781 |
| Log loss | 0.7399 | 0.3234 | 0.4164 | 1.3395 | 0.3708 |
| CI coverage (P5-P95) | 85.4% | 99.6% | 99.0% | 82.4% | 98.7% |

The 2019 election data splits the old "2021 from 2015" hindcast into two consecutive transitions. The 2021-from-2019 hindcast is the easiest (93.2% accuracy, 0.167 Brier) — the 2019→2021 shift was minimal (both Trudeau minorities). The 2019-from-2015 hindcast (87.1%, 0.219 Brier) captures a moderate swing from majority to minority.

The 2015-from-2011 hindcast remains the hardest: the 2015 election saw the largest swing in modern Canadian history (LPC surge from 3rd to majority, NDP collapse). CI coverage below 90% in two hindcasts (2025: 85.4%, 2015: 82.4%) reflects the model's overconfidence for elections with large inter-election shifts. The three easier transitions (2021: 99.6%, 2019: 99.0%, 2011: 98.7%) show significant over-dispersion.

Correlated noise improved the hardest hindcasts: 2015-from-2011 Brier improved from 0.5793→0.5726 and CI coverage from 81.5%→82.4%. Log loss improved for both hard cases (2025: 0.7582→0.7399, 2015: 1.3584→1.3395). Easy hindcasts showed slightly higher Brier scores because the correlated noise structure reduces renormalization dampening, increasing effective uncertainty.

### Known Limitations

- Combined model sigma varies by party (LPC ~14.2%, PPC ~7.4% in quadrature with regional + riding) but is still below the empirical residual std (22.7% across 5 transitions). The gap is mostly systematic swing model error, not random noise.
- Calibration at high confidence (90-100% bin) shows 88.5% actual win rate vs ~95% expected — the model is still overconfident for its most confident predictions.
- Student-t with df=5 produces kurtosis=6, which partially addresses the empirical excess kurtosis of 46.5 (measured across 5 transitions) but does not fully capture it.
- Per-party sigma defaults are derived from empirical residuals (LPC 12%, CPC 8%, NDP 6%, BQ 4%, GPC 7%, PPC 3%). Users can override via `PartyUncertainty`.
- The swing model does not account for candidate-specific effects, redistricting changes, or strategic voting dynamics.
- The 308→343 riding mapping for 2008/2011 elections covers only ~251 of 343 ridings (73%). The 338→343 mapping for 2015/2019 elections covers ~279 ridings (81%). Ridings created during the 2022 redistribution have no pre-2025 historical data.

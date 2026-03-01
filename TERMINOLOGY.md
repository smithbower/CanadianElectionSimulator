# Terminology

Domain-specific terms used throughout the codebase and documentation.

## Electoral

- **FPTP (First Past the Post)**: Canada's electoral system. The candidate with the most votes in a riding wins the seat, regardless of whether they have a majority.
- **Riding**: A federal electoral district. Canada currently has 343 ridings under the 2023 Representation Order.
- **Majority government**: A party wins 172+ seats (more than half of 343), giving it enough seats to pass legislation without support from other parties.
- **Minority government**: A party wins the most seats but fewer than 172. It must negotiate with other parties to govern.
- **Baseline election**: The historical election used as the starting point for projections. Vote shares from this election are adjusted by swing to produce riding-level projections.
- **Redistribution / Redistricting**: Periodic redrawing of riding boundaries based on population changes. The most recent redistribution (2023 Representation Order) changed Canada from 338 to 343 ridings. Historical results must be mapped to current riding boundaries.

## Parties

| Abbreviation | Full Name |
|-------------|-----------|
| **LPC** | Liberal Party of Canada |
| **CPC** | Conservative Party of Canada |
| **NDP** | New Democratic Party |
| **BQ** | Bloc Quebecois |
| **GPC** | Green Party of Canada |
| **PPC** | People's Party of Canada |

## Regions

| Region | Provinces |
|--------|-----------|
| **Atlantic** | Newfoundland and Labrador, Prince Edward Island, Nova Scotia, New Brunswick |
| **Quebec** | Quebec |
| **Ontario** | Ontario |
| **Prairies** | Manitoba, Saskatchewan |
| **Alberta** | Alberta |
| **British Columbia** | British Columbia |
| **North** | Yukon, Northwest Territories, Nunavut |

## Swing Model

- **Swing**: The change in vote share between two elections, used to project how current polling translates to riding-level results.
- **Proportional swing (ratio)**: `projected = baseline_share * (current_poll / baseline_avg)`. Multiplicative -- a party that doubles nationally doubles in every riding. Preserves relative differences but can amplify small-party projections.
- **Additive swing (uniform)**: `projected = baseline_share + (current_poll - baseline_avg)`. Adds the same absolute shift to every riding. Better for large parties; the default in this simulator (alpha=0.0).
- **Swing blend alpha**: Parameter controlling the mix of proportional and additive swing. `alpha=0.0` is pure additive, `alpha=1.0` is pure proportional.
- **Swing ratio**: `current_poll / baseline_regional_average` for a party-region pair.
- **Additive delta**: `current_poll - baseline_regional_average` for a party-region pair.

## Monte Carlo Simulation

- **Monte Carlo simulation**: Running thousands of randomized elections to build probability distributions. Each simulation draws random noise at national, regional, and riding levels, then determines riding winners via FPTP.
- **Sigma (standard deviation)**: Controls the amount of random noise added at each level. Higher sigma = more uncertainty = wider confidence intervals.
- **National sigma**: Noise shared across all ridings (e.g., a polling error that affects the whole country uniformly). Per-party values in `DefaultPartyUncertainty`.
- **Regional sigma**: Noise shared within a region but independent across regions. Scaled by per-region multipliers to reflect different volatility levels.
- **Riding sigma**: Noise unique to each riding, capturing local effects. Also scaled by per-region multipliers.
- **Student-t distribution**: A heavy-tailed distribution used instead of Gaussian (normal) to model election surprises. Controlled by degrees of freedom (df). Lower df = heavier tails = more extreme outcomes. Default df=5.
- **Degrees of freedom (df)**: Parameter of the Student-t distribution. df=5 produces kurtosis=6, partially capturing the empirical excess kurtosis observed in Canadian election residuals. As df approaches infinity, the distribution converges to Gaussian.
- **Correlated noise**: Noise draws that respect inter-party competition. When one party gets a positive shock, competing parties tend to get negative shocks. Implemented via Cholesky decomposition of the inter-party correlation matrix.
- **Cholesky decomposition**: Factoring a correlation matrix into L * L^T, where L is lower-triangular. Multiplying independent noise draws by L produces correlated draws with the desired correlation structure.

## Statistical Measures

- **Confidence interval (CI)**: Range within which a value is expected to fall with a given probability. P5-P95 means 90% of simulations fall within this range.
- **P5, P25, P50, P75, P95**: Percentiles of a distribution. P50 is the median.
- **Brier score**: Measures the accuracy of probabilistic predictions. Ranges from 0 (perfect) to 1 (worst). Computed as the mean squared error between predicted probabilities and actual outcomes (0 or 1). A no-skill baseline (random guessing among parties) scores approximately 0.25.
- **Log loss**: Average negative log-probability of the actual outcome. Heavily penalizes confident wrong predictions. Lower is better.
- **RMSE (Root Mean Square Error)**: Square root of the average squared difference between predicted and actual values. Used to compare swing model accuracy.
- **CI coverage**: Fraction of actual values that fall within the predicted confidence interval. For a P5-P95 interval, the target coverage is 90%. Below 90% indicates overconfidence; above indicates over-dispersion.
- **Calibration**: How well predicted probabilities match actual frequencies. A well-calibrated model predicting 70% win probability should see the predicted party win approximately 70% of the time.
- **Seat MAE (Mean Absolute Error)**: Average absolute difference between predicted mean seats and actual seats, across all parties.

## Demographic Prior

- **Demographic prior**: Census-based prediction of riding vote shares using ridge regression on 9 demographic variables. Blended with swing-projected shares to improve estimates, especially for ridings with no historical data.
- **Ridge regression**: Linear regression with an L2 penalty (lambda * ||beta||^2) on coefficients to prevent overfitting. The penalty shrinks coefficients toward zero, producing more stable predictions when predictors are correlated.
- **Blend weight**: Controls how much the demographic prior influences the final projection. Default 0.02 (2%). Ridings with no baseline data get weight=1.0 (100% demographic prior).

## Validation

- **Hindcast**: Predicting a known past election using only data available before it occurred. The simulator uses the prior election as baseline and the actual regional averages as "polls" to test prediction accuracy.
- **Election transition**: A pair of consecutive elections (e.g., 2021 to 2025) used in hindcasting and residual analysis.
- **Residual**: The difference between an actual riding-level vote share and the swing-projected value (before Monte Carlo noise). Used to calibrate sigma values and validate the swing model.
- **Variance decomposition**: Separating total residual variance into national, regional, and riding components to determine how much noise to apply at each level.

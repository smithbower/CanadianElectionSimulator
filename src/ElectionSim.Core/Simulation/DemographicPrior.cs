using ElectionSim.Core.Models;

namespace ElectionSim.Core.Simulation;

/// <summary>
/// Computes a demographic prior for riding-level vote shares using ridge regression.
/// For each party, fits: vote_share = beta_0 + beta_1*feature_1 + ... + beta_9*feature_9
/// using historical election data as training observations.
/// </summary>
public static class DemographicPrior
{
    private const double DefaultRidgeLambda = 1.0;

    /// <summary>
    /// Computes demographic prior vote shares for all ridings.
    /// Returns double[ridingIndex, partyIndex] with predicted vote shares.
    /// </summary>
    public static double[,] ComputePrior(
        IReadOnlyList<Riding> ridings,
        IReadOnlyList<RidingDemographics> demographics,
        IReadOnlyList<IReadOnlyList<RidingResult>> trainingElections,
        double ridgeLambda = DefaultRidgeLambda)
    {
        var parties = PartyColourProvider.MainParties;
        int numRidings = ridings.Count;
        int numParties = parties.Count;
        int numFeatures = RidingDemographics.FeatureCount + 1; // +1 for intercept

        var demoLookup = demographics.ToDictionary(d => d.RidingId);
        var ridingIndexLookup = new Dictionary<int, int>();
        for (int i = 0; i < numRidings; i++)
            ridingIndexLookup[ridings[i].Id] = i;

        // Build training data: collect (features, vote_shares) across all training elections
        var trainingRows = new List<(double[] Features, double[] VoteShares)>();

        foreach (var election in trainingElections)
        {
            foreach (var result in election)
            {
                if (!demoLookup.TryGetValue(result.RidingId, out var demo))
                    continue;

                var features = PrependIntercept(demo.ToFeatureVector());
                var voteShares = new double[numParties];
                for (int pi = 0; pi < numParties; pi++)
                {
                    var candidate = result.Candidates.FirstOrDefault(c => c.Party == parties[pi]);
                    voteShares[pi] = candidate?.VoteShare ?? 0;
                }
                trainingRows.Add((features, voteShares));
            }
        }

        if (trainingRows.Count < numFeatures)
        {
            // Not enough training data — return uniform regional averages
            return FallbackUniform(numRidings, numParties);
        }

        // Fit ridge regression for each party
        // beta[party] = (X^T X + lambda*I)^-1 X^T y
        var X = new double[trainingRows.Count, numFeatures];
        for (int i = 0; i < trainingRows.Count; i++)
            for (int j = 0; j < numFeatures; j++)
                X[i, j] = trainingRows[i].Features[j];

        var prior = new double[numRidings, numParties];

        for (int pi = 0; pi < numParties; pi++)
        {
            var y = new double[trainingRows.Count];
            for (int i = 0; i < trainingRows.Count; i++)
                y[i] = trainingRows[i].VoteShares[pi];

            var beta = FitRidgeRegression(X, y, ridgeLambda);

            // Predict for all ridings
            for (int ri = 0; ri < numRidings; ri++)
            {
                if (!demoLookup.TryGetValue(ridings[ri].Id, out var demo))
                {
                    prior[ri, pi] = 1.0 / numParties;
                    continue;
                }

                var features = PrependIntercept(demo.ToFeatureVector());
                double predicted = 0;
                for (int j = 0; j < numFeatures; j++)
                    predicted += beta[j] * features[j];

                prior[ri, pi] = Math.Max(predicted, 0);
            }
        }

        // Normalize each riding to sum to 1.0
        for (int ri = 0; ri < numRidings; ri++)
        {
            double sum = 0;
            for (int pi = 0; pi < numParties; pi++)
                sum += prior[ri, pi];

            if (sum > 0)
            {
                for (int pi = 0; pi < numParties; pi++)
                    prior[ri, pi] /= sum;
            }
            else
            {
                for (int pi = 0; pi < numParties; pi++)
                    prior[ri, pi] = 1.0 / numParties;
            }
        }

        return prior;
    }

    private static double[] PrependIntercept(double[] features)
    {
        var result = new double[features.Length + 1];
        result[0] = 1.0; // intercept
        Array.Copy(features, 0, result, 1, features.Length);
        return result;
    }

    /// <summary>
    /// Fits ridge regression: beta = (X^T X + lambda*I)^-1 X^T y
    /// Uses Cholesky decomposition to solve the normal equations.
    /// </summary>
    internal static double[] FitRidgeRegression(double[,] X, double[] y, double lambda)
    {
        int n = X.GetLength(0); // observations
        int p = X.GetLength(1); // features (including intercept)

        // Compute X^T X (p x p)
        var XtX = new double[p, p];
        for (int i = 0; i < p; i++)
        {
            for (int j = i; j < p; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += X[k, i] * X[k, j];
                XtX[i, j] = sum;
                XtX[j, i] = sum;
            }
        }

        // Add ridge penalty (skip intercept at index 0)
        for (int i = 1; i < p; i++)
            XtX[i, i] += lambda;

        // Compute X^T y (p x 1)
        var Xty = new double[p];
        for (int i = 0; i < p; i++)
        {
            double sum = 0;
            for (int k = 0; k < n; k++)
                sum += X[k, i] * y[k];
            Xty[i] = sum;
        }

        // Solve (X^T X + lambda*I) beta = X^T y via Cholesky
        return CholeskySolve(XtX, Xty);
    }

    /// <summary>
    /// Solves A*x = b where A is symmetric positive definite, using Cholesky decomposition.
    /// </summary>
    internal static double[] CholeskySolve(double[,] A, double[] b)
    {
        int n = A.GetLength(0);

        // Cholesky: A = L * L^T
        var L = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < j; k++)
                    sum += L[i, k] * L[j, k];

                if (i == j)
                {
                    double diag = A[i, i] - sum;
                    if (diag <= 0) diag = 1e-10; // numerical safety
                    L[i, j] = Math.Sqrt(diag);
                }
                else
                {
                    L[i, j] = (A[i, j] - sum) / L[j, j];
                }
            }
        }

        // Forward substitution: L * z = b
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < i; j++)
                sum += L[i, j] * z[j];
            z[i] = (b[i] - sum) / L[i, i];
        }

        // Back substitution: L^T * x = z
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int j = i + 1; j < n; j++)
                sum += L[j, i] * x[j];
            x[i] = (z[i] - sum) / L[i, i];
        }

        return x;
    }

    private static double[,] FallbackUniform(int numRidings, int numParties)
    {
        var result = new double[numRidings, numParties];
        double uniform = 1.0 / numParties;
        for (int ri = 0; ri < numRidings; ri++)
            for (int pi = 0; pi < numParties; pi++)
                result[ri, pi] = uniform;
        return result;
    }
}

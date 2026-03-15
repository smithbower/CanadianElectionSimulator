using ElectionSim.Core.Models;
using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class DemographicPriorTests
{
    private readonly List<Riding> _ridings = TestHelpers.CreateTestRidings();
    private readonly List<RidingDemographics> _demographics = TestHelpers.CreateTestDemographics();
    private readonly List<RidingResult> _baseline = TestHelpers.CreateTestBaseline();

    [Fact]
    public void ComputePrior_InsufficientData_ReturnsUniform()
    {
        int numParties = PartyColorProvider.MainParties.Count;
        double expectedUniform = 1.0 / numParties;

        // Pass empty training data — fewer rows than features (10 features including intercept)
        var prior = DemographicPrior.ComputePrior(
            _ridings, _demographics, trainingElections: []);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < numParties; pi++)
            {
                Assert.Equal(expectedUniform, prior[ri, pi], precision: 10);
            }
        }
    }

    [Fact]
    public void ComputePrior_NormalizedOutput()
    {
        int numParties = PartyColorProvider.MainParties.Count;

        // Provide enough training data (use baseline twice to get > 10 rows)
        var trainingElections = new List<IReadOnlyList<RidingResult>> { _baseline, _baseline };
        var prior = DemographicPrior.ComputePrior(
            _ridings, _demographics, trainingElections);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            double sum = 0;
            for (int pi = 0; pi < numParties; pi++)
                sum += prior[ri, pi];

            Assert.InRange(sum, 0.99, 1.01);
        }
    }

    [Fact]
    public void ComputePrior_MissingDemographics_FallsBackGracefully()
    {
        int numParties = PartyColorProvider.MainParties.Count;

        // Create ridings that include one without demographics
        var ridingsWithExtra = new List<Riding>(_ridings)
        {
            new Riding(9999, "NoDemoRiding", "CircSansDemo", "Ontario", Region.Ontario)
        };

        var trainingElections = new List<IReadOnlyList<RidingResult>> { _baseline, _baseline };
        var prior = DemographicPrior.ComputePrior(
            ridingsWithExtra, _demographics, trainingElections);

        // The riding without demographics should get uniform prior
        int extraRidingIdx = ridingsWithExtra.Count - 1;
        double expectedUniform = 1.0 / numParties;

        for (int pi = 0; pi < numParties; pi++)
        {
            Assert.Equal(expectedUniform, prior[extraRidingIdx, pi], precision: 10);
        }
    }

    [Fact]
    public void ComputePrior_NonNegativeShares()
    {
        int numParties = PartyColorProvider.MainParties.Count;
        var trainingElections = new List<IReadOnlyList<RidingResult>> { _baseline, _baseline };

        var prior = DemographicPrior.ComputePrior(
            _ridings, _demographics, trainingElections);

        for (int ri = 0; ri < _ridings.Count; ri++)
        {
            for (int pi = 0; pi < numParties; pi++)
            {
                Assert.True(prior[ri, pi] >= 0,
                    $"Prior share should be non-negative: riding={ri}, party={pi}, value={prior[ri, pi]}");
            }
        }
    }

    [Fact]
    public void CholeskySolve_KnownSystem_ReturnsCorrectSolution()
    {
        // Solve Ax = b where A is SPD:
        // A = [[4, 2], [2, 3]], b = [8, 7]
        // 4x + 2y = 8, 2x + 3y = 7 → y = 1.5, x = 1.25
        var A = new double[2, 2] { { 4, 2 }, { 2, 3 } };
        var b = new double[] { 8, 7 };

        var x = DemographicPrior.CholeskySolve(A, b);

        Assert.Equal(1.25, x[0], precision: 8);
        Assert.Equal(1.5, x[1], precision: 8);
    }

    [Fact]
    public void CholeskySolve_3x3_ReturnsCorrectSolution()
    {
        // A = [[2, -1, 0], [-1, 2, -1], [0, -1, 2]], b = [1, 0, 1]
        // Tridiagonal SPD matrix, solution: x = [1, 1, 1]
        var A = new double[3, 3]
        {
            { 2, -1, 0 },
            { -1, 2, -1 },
            { 0, -1, 2 }
        };
        var b = new double[] { 1, 0, 1 };

        var x = DemographicPrior.CholeskySolve(A, b);

        Assert.Equal(1.0, x[0], precision: 8);
        Assert.Equal(1.0, x[1], precision: 8);
        Assert.Equal(1.0, x[2], precision: 8);
    }

    [Fact]
    public void FitRidgeRegression_WithLambda_ProducesSmootherCoefficients()
    {
        // Simple 1-feature regression: y = 2*x with some noise
        int n = 20;
        var X = new double[n, 2]; // intercept + 1 feature
        var y = new double[n];
        var rng = new Random(42);

        for (int i = 0; i < n; i++)
        {
            double xi = i / (double)n;
            X[i, 0] = 1.0; // intercept
            X[i, 1] = xi;
            y[i] = 2.0 * xi + 0.1 * (rng.NextDouble() - 0.5); // y ≈ 2x
        }

        var betaLow = DemographicPrior.FitRidgeRegression(X, y, lambda: 0.001);
        var betaHigh = DemographicPrior.FitRidgeRegression(X, y, lambda: 100.0);

        // High regularization should shrink coefficients toward zero
        Assert.True(Math.Abs(betaHigh[1]) < Math.Abs(betaLow[1]),
            "Higher ridge lambda should shrink slope coefficient");

        // Low lambda should recover approximate slope of 2.0
        Assert.InRange(betaLow[1], 1.5, 2.5);
    }
}

using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class CorrelationDataTests
{
    [Fact]
    public void CholeskyFactor_MatchesExpectedDimensions()
    {
        Assert.Equal(6, CorrelationData.CholeskyFactor.GetLength(0));
        Assert.Equal(6, CorrelationData.CholeskyFactor.GetLength(1));
    }

    [Fact]
    public void CholeskyFactor_IsLowerTriangular()
    {
        int n = CorrelationData.CholeskyFactor.GetLength(0);

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                Assert.True(CorrelationData.CholeskyFactor[i, j] == 0.0,
                    $"Upper triangle should be zero at [{i},{j}], got {CorrelationData.CholeskyFactor[i, j]}");
            }
        }
    }

    [Fact]
    public void CholeskyFactor_ReconstructsValidCorrelationMatrix()
    {
        int n = CorrelationData.CholeskyFactor.GetLength(0);

        // Reconstruct R = L * L^T
        var R = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += CorrelationData.CholeskyFactor[i, k] * CorrelationData.CholeskyFactor[j, k];
                R[i, j] = sum;
            }
        }

        // Diagonal should be close to 1.0 (correlation matrix)
        for (int i = 0; i < n; i++)
        {
            Assert.InRange(R[i, i], 0.95, 1.05);
        }

        // All entries should be in [-1, 1] range (with small tolerance for rounding)
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.InRange(R[i, j], -1.1, 1.1);
            }
        }

        // Matrix should be symmetric
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                Assert.Equal(R[i, j], R[j, i], precision: 10);
            }
        }
    }

    [Fact]
    public void CholeskyFactor_DiagonalIsPositive()
    {
        int n = CorrelationData.CholeskyFactor.GetLength(0);

        for (int i = 0; i < n; i++)
        {
            Assert.True(CorrelationData.CholeskyFactor[i, i] > 0,
                $"Diagonal element [{i},{i}] should be positive, got {CorrelationData.CholeskyFactor[i, i]}");
        }
    }

    [Fact]
    public void CholeskyFactor_LPC_BQ_Anticorrelated()
    {
        // Reconstruct correlation matrix
        int n = 6;
        var R = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += CorrelationData.CholeskyFactor[i, k] * CorrelationData.CholeskyFactor[j, k];
                R[i, j] = sum;
            }

        // Party order: LPC=0, CPC=1, NDP=2, BQ=3, GPC=4, PPC=5
        // LPC-BQ correlation should be strongly negative
        Assert.True(R[0, 3] < -0.5,
            $"LPC-BQ correlation should be strongly negative, got {R[0, 3]:F3}");
    }
}

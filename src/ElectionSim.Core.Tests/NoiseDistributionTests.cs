using ElectionSim.Core.Simulation;

namespace ElectionSim.Core.Tests;

public class NoiseDistributionTests
{
    private const int LargeN = 100_000;
    private const double MeanTolerance = 0.02;
    private const double VarianceTolerance = 0.05;

    [Fact]
    public void NextGaussian_LargeN_HasMeanZero()
    {
        var rng = new Random(42);
        double sum = 0;
        for (int i = 0; i < LargeN; i++)
            sum += MonteCarloSimulator.NextGaussian(rng);

        double mean = sum / LargeN;
        Assert.InRange(mean, -MeanTolerance, MeanTolerance);
    }

    [Fact]
    public void NextGaussian_LargeN_HasUnitVariance()
    {
        var rng = new Random(42);
        var samples = new double[LargeN];
        for (int i = 0; i < LargeN; i++)
            samples[i] = MonteCarloSimulator.NextGaussian(rng);

        double mean = samples.Average();
        double variance = samples.Sum(x => (x - mean) * (x - mean)) / (LargeN - 1);

        Assert.InRange(variance, 1.0 - VarianceTolerance, 1.0 + VarianceTolerance);
    }

    [Fact]
    public void StudentT_Df5_HasHigherKurtosis()
    {
        var rng = new Random(42);
        var samples = new double[LargeN];
        for (int i = 0; i < LargeN; i++)
            samples[i] = MonteCarloSimulator.NextStudentT(rng, 5.0);

        double mean = samples.Average();
        double m2 = samples.Sum(x => Math.Pow(x - mean, 2)) / LargeN;
        double m4 = samples.Sum(x => Math.Pow(x - mean, 4)) / LargeN;
        double excessKurtosis = (m4 / (m2 * m2)) - 3.0;

        // Student-t with df=5 has excess kurtosis = 6/(5-4) = 6
        // Allow generous range due to sampling variance
        Assert.True(excessKurtosis > 3.0,
            $"Student-t df=5 should have excess kurtosis > 3 (Gaussian), got {excessKurtosis:F2}");
    }

    [Fact]
    public void StudentTScale_Df5_MatchesFormula()
    {
        double scale = MonteCarloSimulator.ComputeStudentTScale(5.0);
        double expected = Math.Sqrt(3.0 / 5.0); // sqrt((df-2)/df)

        Assert.Equal(expected, scale, precision: 10);
    }

    [Fact]
    public void ComputeStudentTScale_NullDf_ReturnsOne()
    {
        Assert.Equal(1.0, MonteCarloSimulator.ComputeStudentTScale(null));
    }

    [Fact]
    public void ComputeStudentTScale_InfinityDf_ReturnsOne()
    {
        Assert.Equal(1.0, MonteCarloSimulator.ComputeStudentTScale(double.PositiveInfinity));
    }

    [Fact]
    public void ComputeStudentTScale_DfLeq2_ReturnsOne()
    {
        Assert.Equal(1.0, MonteCarloSimulator.ComputeStudentTScale(2.0));
        Assert.Equal(1.0, MonteCarloSimulator.ComputeStudentTScale(1.0));
    }

    [Fact]
    public void NextNoise_WithDf_ReturnsScaledStudentT()
    {
        var rng = new Random(42);
        double tScale = MonteCarloSimulator.ComputeStudentTScale(5.0);

        var samples = new double[LargeN];
        for (int i = 0; i < LargeN; i++)
            samples[i] = MonteCarloSimulator.NextNoise(rng, 5.0, tScale);

        // After scaling, effective variance should be close to 1.0
        double mean = samples.Average();
        double variance = samples.Sum(x => (x - mean) * (x - mean)) / (LargeN - 1);

        Assert.InRange(mean, -MeanTolerance, MeanTolerance);
        // Variance tolerance is wider for Student-t due to heavy tails
        Assert.InRange(variance, 0.8, 1.3);
    }

    [Fact]
    public void NextNoise_NullDf_ReturnsGaussian()
    {
        var rng = new Random(42);
        var samples = new double[LargeN];
        for (int i = 0; i < LargeN; i++)
            samples[i] = MonteCarloSimulator.NextNoise(rng, null, 1.0);

        double mean = samples.Average();
        double variance = samples.Sum(x => (x - mean) * (x - mean)) / (LargeN - 1);

        // Should behave like standard Gaussian
        Assert.InRange(mean, -MeanTolerance, MeanTolerance);
        Assert.InRange(variance, 1.0 - VarianceTolerance, 1.0 + VarianceTolerance);
    }

    [Fact]
    public void StudentT_ProducesMoreExtremeValues_ThanGaussian()
    {
        var rng1 = new Random(42);
        var rng2 = new Random(42);
        double threshold = 3.0; // 3 sigma

        int gaussianExtremes = 0;
        int studentTExtremes = 0;

        for (int i = 0; i < LargeN; i++)
        {
            double g = MonteCarloSimulator.NextGaussian(rng1);
            double t = MonteCarloSimulator.NextStudentT(rng2, 5.0);

            if (Math.Abs(g) > threshold) gaussianExtremes++;
            if (Math.Abs(t) > threshold) studentTExtremes++;
        }

        Assert.True(studentTExtremes > gaussianExtremes,
            $"Student-t should produce more extreme values: t={studentTExtremes}, gaussian={gaussianExtremes}");
    }
}

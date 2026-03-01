namespace ElectionSim.Core.Simulation;

/// <summary>
/// Pre-computed Cholesky factor for correlated inter-party noise.
/// Derived from demeaned residuals across 5 election transitions
/// (2008→2011, 2011→2015, 2015→2019, 2019→2021, 2021→2025), 1431 riding observations.
/// Party order matches PartyColorProvider.MainParties: LPC, CPC, NDP, BQ, GPC, PPC.
/// Regularization epsilon=0.01 added to restore full rank (demeaned residuals are rank K-1).
/// </summary>
public static class CorrelationData
{
    /// <summary>
    /// Lower-triangular Cholesky factor L such that L * L^T ≈ correlation matrix.
    /// Used to transform independent noise draws into correlated draws:
    ///   correlated[p] = Σ_q L[p,q] * independent[q]
    /// </summary>
    public static readonly double[,] CholeskyFactor = new double[6, 6]
    {
        {  1.004988,  0.000000,  0.000000,  0.000000,  0.000000,  0.000000 }, // LPC
        { -0.177882,  0.989120,  0.000000,  0.000000,  0.000000,  0.000000 }, // CPC
        { -0.579099, -0.451840,  0.685919,  0.000000,  0.000000,  0.000000 }, // NDP
        { -0.828998, -0.286878, -0.332886,  0.360071,  0.000000,  0.000000 }, // BQ
        {  0.379865,  0.069987, -0.170346, -0.426719,  0.806038,  0.000000 }, // GPC
        { -0.923347, -0.206308, -0.019983, -0.043673, -0.249080,  0.224765 }, // PPC
    };
}

using ElectionSim.Core.Models;

namespace ElectionSim.Core.Tests;

/// <summary>
/// Factory methods for creating synthetic test data.
/// Uses small datasets (3-5 ridings, 2 regions) to keep tests fast and deterministic.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Creates a minimal set of ridings across two regions for testing.
    /// 3 Ontario ridings + 2 Quebec ridings = 5 total.
    /// </summary>
    public static List<Riding> CreateTestRidings() =>
    [
        new Riding(1001, "TestRiding-ON-1", "CircTest-ON-1", "Ontario", Region.Ontario),
        new Riding(1002, "TestRiding-ON-2", "CircTest-ON-2", "Ontario", Region.Ontario),
        new Riding(1003, "TestRiding-ON-3", "CircTest-ON-3", "Ontario", Region.Ontario),
        new Riding(2001, "TestRiding-QC-1", "CircTest-QC-1", "Quebec", Region.Quebec),
        new Riding(2002, "TestRiding-QC-2", "CircTest-QC-2", "Quebec", Region.Quebec),
    ];

    /// <summary>
    /// Creates baseline election results for the test ridings.
    /// Ontario: LPC-dominant. Quebec: BQ-dominant.
    /// </summary>
    public static List<RidingResult> CreateTestBaseline() =>
    [
        new RidingResult(1001, 2025,
        [
            new CandidateResult(Party.LPC, 15000, 0.45),
            new CandidateResult(Party.CPC, 10000, 0.30),
            new CandidateResult(Party.NDP, 5000, 0.15),
            new CandidateResult(Party.GPC, 1000, 0.03),
            new CandidateResult(Party.PPC, 1000, 0.03),
            new CandidateResult(Party.BQ, 0, 0.00),
            new CandidateResult(Party.Other, 1333, 0.04),
        ], 33333),

        new RidingResult(1002, 2025,
        [
            new CandidateResult(Party.LPC, 12000, 0.40),
            new CandidateResult(Party.CPC, 12000, 0.40),
            new CandidateResult(Party.NDP, 3000, 0.10),
            new CandidateResult(Party.GPC, 1500, 0.05),
            new CandidateResult(Party.PPC, 1000, 0.03),
            new CandidateResult(Party.BQ, 0, 0.00),
            new CandidateResult(Party.Other, 500, 0.02),
        ], 30000),

        new RidingResult(1003, 2025,
        [
            new CandidateResult(Party.LPC, 10000, 0.35),
            new CandidateResult(Party.CPC, 8000, 0.28),
            new CandidateResult(Party.NDP, 8000, 0.28),
            new CandidateResult(Party.GPC, 1000, 0.035),
            new CandidateResult(Party.PPC, 500, 0.018),
            new CandidateResult(Party.BQ, 0, 0.00),
            new CandidateResult(Party.Other, 500, 0.017),
        ], 28000),

        new RidingResult(2001, 2025,
        [
            new CandidateResult(Party.LPC, 8000, 0.25),
            new CandidateResult(Party.CPC, 5000, 0.15),
            new CandidateResult(Party.NDP, 3000, 0.10),
            new CandidateResult(Party.BQ, 14000, 0.44),
            new CandidateResult(Party.GPC, 1000, 0.03),
            new CandidateResult(Party.PPC, 500, 0.015),
            new CandidateResult(Party.Other, 500, 0.015),
        ], 32000),

        new RidingResult(2002, 2025,
        [
            new CandidateResult(Party.LPC, 10000, 0.30),
            new CandidateResult(Party.CPC, 3000, 0.10),
            new CandidateResult(Party.NDP, 5000, 0.15),
            new CandidateResult(Party.BQ, 12000, 0.36),
            new CandidateResult(Party.GPC, 1000, 0.03),
            new CandidateResult(Party.PPC, 1000, 0.03),
            new CandidateResult(Party.Other, 1000, 0.03),
        ], 33000),
    ];

    /// <summary>
    /// Creates regional polls that differ from baseline to test swing calculations.
    /// LPC down in Ontario, BQ up in Quebec relative to baseline averages.
    /// </summary>
    public static List<RegionalPoll> CreateTestPolls() =>
    [
        new RegionalPoll(Region.Ontario, new Dictionary<Party, double>
        {
            [Party.LPC] = 0.35,
            [Party.CPC] = 0.38,
            [Party.NDP] = 0.18,
            [Party.BQ] = 0.00,
            [Party.GPC] = 0.04,
            [Party.PPC] = 0.03,
            [Party.Other] = 0.02,
        }),
        new RegionalPoll(Region.Quebec, new Dictionary<Party, double>
        {
            [Party.LPC] = 0.22,
            [Party.CPC] = 0.15,
            [Party.NDP] = 0.10,
            [Party.BQ] = 0.45,
            [Party.GPC] = 0.03,
            [Party.PPC] = 0.02,
            [Party.Other] = 0.03,
        }),
    ];

    /// <summary>
    /// Creates demographics data for the test ridings.
    /// </summary>
    public static List<RidingDemographics> CreateTestDemographics() =>
    [
        new RidingDemographics(1001, 0.7, 0.5, 0.3, 0.2, 0.1, 0.6, 0.5, 0.02, 0.7),
        new RidingDemographics(1002, 0.5, 0.4, 0.4, 0.3, 0.05, 0.5, 0.45, 0.01, 0.6),
        new RidingDemographics(1003, 0.4, 0.6, 0.2, 0.15, 0.08, 0.3, 0.4, 0.03, 0.5),
        new RidingDemographics(2001, 0.3, 0.3, 0.1, 0.1, 0.9, 0.4, 0.5, 0.01, 0.55),
        new RidingDemographics(2002, 0.6, 0.5, 0.2, 0.2, 0.7, 0.5, 0.42, 0.02, 0.6),
    ];

    /// <summary>
    /// Creates a default SimulationConfig suitable for fast, deterministic tests.
    /// </summary>
    public static SimulationConfig CreateTestConfig(int seed = 42, int numSimulations = 1000) =>
        new(
            NumSimulations: numSimulations,
            Seed: seed,
            NationalSigma: 0.06,
            RegionalSigma: 0.026,
            RidingSigma: 0.065,
            DegreesOfFreedom: 5.0,
            UseCorrelatedNoise: true
        );
}

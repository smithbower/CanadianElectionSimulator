using ElectionSim.Core.Models;

namespace ElectionSim.Core.Tests;

public class ModelTests
{
    [Fact]
    public void SimulationConfig_DefaultValues_MatchDocumentation()
    {
        var config = new SimulationConfig();

        Assert.Equal(10_000, config.NumSimulations);
        Assert.Equal(0.06, config.NationalSigma);
        Assert.Equal(0.026, config.RegionalSigma);
        Assert.Equal(0.065, config.RidingSigma);
        Assert.Null(config.Seed);
        Assert.Null(config.PartyUncertainty);
        Assert.Equal(3.0, config.DegreesOfFreedom);
        Assert.Equal(0.0, config.SwingBlendAlpha);
        Assert.Null(config.RegionalSigmaMultipliers);
        Assert.True(config.UseCorrelatedNoise);
        Assert.False(config.UseDemographicPrior);
        Assert.Equal(0.02, config.DemographicBlendWeight);
        Assert.Equal(0.3, config.ByElectionBlendWeight);
    }

    [Fact]
    public void SimulationConfig_ForServer_HasTighterSigmas()
    {
        var serverConfig = SimulationConfig.ForServer();
        var defaultConfig = new SimulationConfig();

        Assert.True(serverConfig.NationalSigma < defaultConfig.NationalSigma,
            "Server national sigma should be tighter than default");
        Assert.True(serverConfig.RegionalSigma < defaultConfig.RegionalSigma,
            "Server regional sigma should be tighter than default");
        Assert.True(serverConfig.RidingSigma < defaultConfig.RidingSigma,
            "Server riding sigma should be tighter than default");

        // Verify specific server values
        Assert.Equal(0.025, serverConfig.NationalSigma);
        Assert.Equal(0.02, serverConfig.RegionalSigma);
        Assert.Equal(0.015, serverConfig.RidingSigma);
        Assert.Equal(1.0, serverConfig.SwingBlendAlpha);
    }

    [Fact]
    public void PartyColourProvider_MainParties_Has6Entries()
    {
        Assert.Equal(6, PartyColourProvider.MainParties.Count);
    }

    [Fact]
    public void PartyColourProvider_MainParties_ContainsExpectedParties()
    {
        var expected = new[] { Party.LPC, Party.CPC, Party.NDP, Party.BQ, Party.GPC, Party.PPC };
        foreach (var party in expected)
        {
            Assert.Contains(party, PartyColourProvider.MainParties);
        }

        // Other is NOT in MainParties
        Assert.DoesNotContain(Party.Other, PartyColourProvider.MainParties);
    }

    [Fact]
    public void PartyColourProvider_MainParties_OrderMatches()
    {
        // Order matters for array indexing throughout the simulation
        Assert.Equal(Party.LPC, PartyColourProvider.MainParties[0]);
        Assert.Equal(Party.CPC, PartyColourProvider.MainParties[1]);
        Assert.Equal(Party.NDP, PartyColourProvider.MainParties[2]);
        Assert.Equal(Party.BQ, PartyColourProvider.MainParties[3]);
        Assert.Equal(Party.GPC, PartyColourProvider.MainParties[4]);
        Assert.Equal(Party.PPC, PartyColourProvider.MainParties[5]);
    }

    [Fact]
    public void Region_Has7Values()
    {
        Assert.Equal(7, Enum.GetValues<Region>().Length);
    }

    [Fact]
    public void Party_Has7Values()
    {
        Assert.Equal(7, Enum.GetValues<Party>().Length);
    }

    [Fact]
    public void DefaultPartyUncertainty_ContainsAllParties()
    {
        foreach (var party in Enum.GetValues<Party>())
        {
            Assert.True(SimulationConfig.DefaultPartyUncertainty.ContainsKey(party),
                $"DefaultPartyUncertainty should contain {party}");
        }
    }

    [Fact]
    public void DefaultPartyUncertainty_LpcHasHighestVolatility()
    {
        double lpcUncertainty = SimulationConfig.DefaultPartyUncertainty[Party.LPC];
        foreach (var (party, uncertainty) in SimulationConfig.DefaultPartyUncertainty)
        {
            if (party != Party.LPC)
            {
                Assert.True(lpcUncertainty >= uncertainty,
                    $"LPC uncertainty ({lpcUncertainty}) should be >= {party} ({uncertainty})");
            }
        }
    }

    [Fact]
    public void DefaultRegionalSigmaMultipliers_ContainsAllRegions()
    {
        foreach (var region in Enum.GetValues<Region>())
        {
            Assert.True(SimulationConfig.DefaultRegionalSigmaMultipliers.ContainsKey(region),
                $"DefaultRegionalSigmaMultipliers should contain {region}");
        }
    }

    [Fact]
    public void DefaultRegionalSigmaMultipliers_AlbertaHighest()
    {
        double albertaMultiplier = SimulationConfig.DefaultRegionalSigmaMultipliers[Region.Alberta];
        foreach (var (region, multiplier) in SimulationConfig.DefaultRegionalSigmaMultipliers)
        {
            Assert.True(albertaMultiplier >= multiplier,
                $"Alberta multiplier ({albertaMultiplier}) should be >= {region} ({multiplier})");
        }
    }

    [Fact]
    public void RidingDemographics_FeatureCount_Is9()
    {
        Assert.Equal(9, RidingDemographics.FeatureCount);
    }

    [Fact]
    public void RidingDemographics_ToFeatureVector_HasCorrectLength()
    {
        var demo = new RidingDemographics(1, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);
        var features = demo.ToFeatureVector();

        Assert.Equal(RidingDemographics.FeatureCount, features.Length);
    }

    [Fact]
    public void RidingDemographics_FeatureNames_HasCorrectLength()
    {
        Assert.Equal(RidingDemographics.FeatureCount, RidingDemographics.FeatureNames.Length);
    }

    [Fact]
    public void PartyColourProvider_GetColour_ReturnsNonEmpty()
    {
        foreach (var party in Enum.GetValues<Party>())
        {
            string color = PartyColourProvider.GetColour(party);
            Assert.False(string.IsNullOrEmpty(color), $"Color for {party} should not be empty");
            Assert.StartsWith("#", color);
        }
    }

    [Fact]
    public void PartyColourProvider_GetRegionForProvince_CoversAllProvinces()
    {
        var provinces = new[]
        {
            "Newfoundland and Labrador", "Prince Edward Island", "Nova Scotia", "New Brunswick",
            "Quebec", "Ontario", "Manitoba", "Saskatchewan", "Alberta", "British Columbia",
            "Yukon", "Northwest Territories", "Nunavut"
        };

        foreach (var province in provinces)
        {
            var region = PartyColourProvider.GetRegionForProvince(province);
            Assert.True(Enum.IsDefined(region), $"Region for {province} should be a valid Region enum value");
        }
    }
}

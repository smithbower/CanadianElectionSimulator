using ElectionSim.Core.Models;
using ElectionSim.Web.Services;
using Xunit;

namespace ElectionSim.Server.Tests;

/// <summary>
/// Exhaustive tests for <see cref="ShareableLinkService"/> — URL generation, encoding/decoding
/// round-trip fidelity, query parameter extraction, and error handling for malformed inputs.
/// </summary>
public class ShareableLinkServiceTests
{
    /// <summary>Creates a minimal SimulationState suitable for testing (no HTTP dependency needed).</summary>
    private static SimulationState CreateState()
    {
        var http = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var dataService = new DataService(http);
        return new SimulationState(dataService);
    }

    /// <summary>Populates state with known polling, uncertainty, config, and baseline values.</summary>
    private static SimulationState CreatePopulatedState()
    {
        var state = CreateState();
        state.SetPolling(TestHelpers.MakeDominantPolling(Party.CPC));
        state.PartyUncertainty = new Dictionary<Party, double>
        {
            [Party.LPC] = 0.12,
            [Party.CPC] = 0.08,
            [Party.NDP] = 0.06,
            [Party.BQ] = 0.04,
            [Party.GPC] = 0.07,
            [Party.PPC] = 0.03,
            [Party.Other] = 0.03,
        };
        state.Config = new SimulationConfig(
            NumSimulations: 5000,
            NationalSigma: 0.05,
            RegionalSigma: 0.03,
            RidingSigma: 0.07,
            DegreesOfFreedom: 8.0,
            SwingBlendAlpha: 0.5,
            UseCorrelatedNoise: false,
            UseDemographicPrior: true,
            DemographicBlendWeight: 0.05,
            ByElectionBlendWeight: 0.4
        );
        state.BaselineYear = 2021;
        return state;
    }

    // ─────────────────────────────────────────────
    //  Round-trip: encode → decode preserves state
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesBaselineYear()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        var applied = ShareableLinkService.TryApply(param!, target);

        Assert.True(applied);
        Assert.Equal(source.BaselineYear, target.BaselineYear);
    }

    [Fact]
    public void RoundTrip_PreservesPollingData()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.CurrentPolling.Count, target.CurrentPolling.Count);
        foreach (var (region, shares) in source.CurrentPolling)
        {
            Assert.True(target.CurrentPolling.ContainsKey(region), $"Missing region: {region}");
            foreach (var (party, value) in shares)
            {
                Assert.True(target.CurrentPolling[region].ContainsKey(party),
                    $"Missing party {party} in region {region}");
                Assert.Equal(value, target.CurrentPolling[region][party], 6);
            }
        }
    }

    [Fact]
    public void RoundTrip_PreservesPartyUncertainty()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.PartyUncertainty.Count, target.PartyUncertainty.Count);
        foreach (var (party, value) in source.PartyUncertainty)
        {
            Assert.Equal(value, target.PartyUncertainty[party], 6);
        }
    }

    [Fact]
    public void RoundTrip_PreservesNumSimulations()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.NumSimulations, target.Config.NumSimulations);
    }

    [Fact]
    public void RoundTrip_PreservesNationalSigma()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.NationalSigma, target.Config.NationalSigma);
    }

    [Fact]
    public void RoundTrip_PreservesRegionalSigma()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.RegionalSigma, target.Config.RegionalSigma);
    }

    [Fact]
    public void RoundTrip_PreservesRidingSigma()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.RidingSigma, target.Config.RidingSigma);
    }

    [Fact]
    public void RoundTrip_PreservesDegreesOfFreedom()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.DegreesOfFreedom, target.Config.DegreesOfFreedom);
    }

    [Fact]
    public void RoundTrip_PreservesSwingBlendAlpha()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.SwingBlendAlpha, target.Config.SwingBlendAlpha);
    }

    [Fact]
    public void RoundTrip_PreservesUseCorrelatedNoise()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.UseCorrelatedNoise, target.Config.UseCorrelatedNoise);
    }

    [Fact]
    public void RoundTrip_PreservesUseDemographicPrior()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.UseDemographicPrior, target.Config.UseDemographicPrior);
    }

    [Fact]
    public void RoundTrip_PreservesDemographicBlendWeight()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.DemographicBlendWeight, target.Config.DemographicBlendWeight);
    }

    [Fact]
    public void RoundTrip_PreservesByElectionBlendWeight()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(source.Config.ByElectionBlendWeight, target.Config.ByElectionBlendWeight);
    }

    [Fact]
    public void RoundTrip_PreservesNullDegreesOfFreedom()
    {
        var source = CreatePopulatedState();
        source.Config = source.Config with { DegreesOfFreedom = null };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Null(target.Config.DegreesOfFreedom);
    }

    // ─────────────────────────────────────────────
    //  Round-trip with different baseline years
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData(2015)]
    [InlineData(2021)]
    [InlineData(2025)]
    public void RoundTrip_PreservesEachBaselineYear(int year)
    {
        var source = CreatePopulatedState();
        source.BaselineYear = year;

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(year, target.BaselineYear);
    }

    // ─────────────────────────────────────────────
    //  Round-trip with different polling scenarios
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesLPCDominantPolling()
    {
        var source = CreateState();
        source.SetPolling(TestHelpers.MakeDominantPolling(Party.LPC));
        source.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        foreach (var region in Enum.GetValues<Region>())
        {
            Assert.Equal(
                source.CurrentPolling[region][Party.LPC],
                target.CurrentPolling[region][Party.LPC], 6);
        }
    }

    [Fact]
    public void RoundTrip_PreservesAllRegions()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        foreach (var region in Enum.GetValues<Region>())
        {
            Assert.True(target.CurrentPolling.ContainsKey(region),
                $"Region {region} missing after round-trip");
        }
    }

    [Fact]
    public void RoundTrip_PreservesAllParties()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        foreach (var region in Enum.GetValues<Region>())
        {
            foreach (var (party, value) in source.CurrentPolling[region])
            {
                Assert.True(target.CurrentPolling[region].ContainsKey(party),
                    $"Party {party} missing in region {region}");
                Assert.Equal(value, target.CurrentPolling[region][party], 6);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Round-trip with edge-case config values
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesDefaultConfig()
    {
        var source = CreateState();
        source.SetPolling(TestHelpers.MakeDominantPolling(Party.CPC));
        source.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);
        // Config stays at default values

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        var defaults = new SimulationConfig();
        Assert.Equal(defaults.NumSimulations, target.Config.NumSimulations);
        Assert.Equal(defaults.NationalSigma, target.Config.NationalSigma);
        Assert.Equal(defaults.RegionalSigma, target.Config.RegionalSigma);
        Assert.Equal(defaults.RidingSigma, target.Config.RidingSigma);
        Assert.Equal(defaults.DegreesOfFreedom, target.Config.DegreesOfFreedom);
        Assert.Equal(defaults.SwingBlendAlpha, target.Config.SwingBlendAlpha);
        Assert.Equal(defaults.UseCorrelatedNoise, target.Config.UseCorrelatedNoise);
        Assert.Equal(defaults.UseDemographicPrior, target.Config.UseDemographicPrior);
        Assert.Equal(defaults.DemographicBlendWeight, target.Config.DemographicBlendWeight);
        Assert.Equal(defaults.ByElectionBlendWeight, target.Config.ByElectionBlendWeight);
    }

    [Fact]
    public void RoundTrip_PreservesExtremeUncertaintyValues()
    {
        var source = CreateState();
        source.SetPolling(TestHelpers.MakeDominantPolling(Party.CPC));
        source.PartyUncertainty = new Dictionary<Party, double>
        {
            [Party.LPC] = 0.0,
            [Party.CPC] = 0.30,
            [Party.NDP] = 0.001,
            [Party.BQ] = 0.299,
            [Party.GPC] = 0.15,
            [Party.PPC] = 0.0,
            [Party.Other] = 0.0,
        };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(0.0, target.PartyUncertainty[Party.LPC]);
        Assert.Equal(0.30, target.PartyUncertainty[Party.CPC]);
        Assert.Equal(0.001, target.PartyUncertainty[Party.NDP], 6);
        Assert.Equal(0.299, target.PartyUncertainty[Party.BQ], 6);
    }

    [Fact]
    public void RoundTrip_OverwritesExistingTargetState()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        // Pre-fill target with different values
        var target = CreateState();
        target.SetPolling(TestHelpers.MakeDominantPolling(Party.LPC));
        target.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.25);
        target.BaselineYear = 2015;
        target.Config = new SimulationConfig(NumSimulations: 50000);

        ShareableLinkService.TryApply(param!, target);

        // Source values should have replaced target values
        Assert.Equal(source.BaselineYear, target.BaselineYear);
        Assert.Equal(source.Config.NumSimulations, target.Config.NumSimulations);
        Assert.Equal(source.PartyUncertainty[Party.LPC], target.PartyUncertainty[Party.LPC]);
    }

    // ─────────────────────────────────────────────
    //  URL generation
    // ─────────────────────────────────────────────

    [Fact]
    public void GenerateShareableUrl_ContainsQueryParam()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        Assert.Contains("?s=", url);
    }

    [Fact]
    public void GenerateShareableUrl_StartsWithBaseUri()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        Assert.StartsWith("https://example.com", url);
    }

    [Fact]
    public void GenerateShareableUrl_UsesAmpersandWhenBaseHasQuery()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/?foo=bar");

        Assert.Contains("&s=", url);
        Assert.DoesNotContain("?s=", url);
    }

    [Fact]
    public void GenerateShareableUrl_TrimsTrailingSlash()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        Assert.DoesNotContain("/?s=", url);
        Assert.Contains("https://example.com?s=", url);
    }

    [Fact]
    public void GenerateShareableUrl_IsUrlSafe()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        // base64url encoding should not contain +, /, or =
        var paramValue = ShareableLinkService.ExtractShareParam(url)!;
        Assert.DoesNotContain("+", paramValue);
        Assert.DoesNotContain("/", paramValue);
        Assert.DoesNotContain("=", paramValue);
    }

    [Fact]
    public void GenerateShareableUrl_ProducesReasonableLength()
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        // Full state URL should be under 2048 characters (browser limit)
        Assert.True(url.Length < 2048,
            $"URL length {url.Length} exceeds 2048 character browser limit.");
    }

    [Fact]
    public void GenerateShareableUrl_ProducesConsistentOutput()
    {
        var state = CreatePopulatedState();
        var url1 = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");
        var url2 = ShareableLinkService.GenerateShareableUrl(state, "https://example.com/");

        Assert.Equal(url1, url2);
    }

    // ─────────────────────────────────────────────
    //  ExtractShareParam
    // ─────────────────────────────────────────────

    [Fact]
    public void ExtractShareParam_ReturnsNull_ForNoQueryString()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractShareParam_ReturnsNull_ForMissingSParam()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?foo=bar&baz=qux");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractShareParam_ExtractsValue_WhenFirst()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?s=abc123");
        Assert.Equal("abc123", result);
    }

    [Fact]
    public void ExtractShareParam_ExtractsValue_WhenNotFirst()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?foo=bar&s=abc123");
        Assert.Equal("abc123", result);
    }

    [Fact]
    public void ExtractShareParam_ExtractsValue_WithMultipleParams()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?foo=bar&s=abc123&baz=qux");
        Assert.Equal("abc123", result);
    }

    [Fact]
    public void ExtractShareParam_IgnoresParamWithNoEquals()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?noequals&s=abc123");
        Assert.Equal("abc123", result);
    }

    [Fact]
    public void ExtractShareParam_HandlesUrlEncodedValue()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?s=abc%20123");
        Assert.Equal("abc 123", result);
    }

    [Fact]
    public void ExtractShareParam_DoesNotMatchSimilarKey()
    {
        // "ss" and "as" should not match "s"
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?ss=wrong&as=wrong2");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractShareParam_MatchesExactKey()
    {
        var result = ShareableLinkService.ExtractShareParam("https://example.com/?ss=wrong&s=correct&as=wrong2");
        Assert.Equal("correct", result);
    }

    // ─────────────────────────────────────────────
    //  TryApply error handling
    // ─────────────────────────────────────────────

    [Fact]
    public void TryApply_ReturnsFalse_ForEmptyString()
    {
        var state = CreateState();
        Assert.False(ShareableLinkService.TryApply("", state));
    }

    [Fact]
    public void TryApply_ReturnsFalse_ForGarbageString()
    {
        var state = CreateState();
        Assert.False(ShareableLinkService.TryApply("not-valid-base64-at-all!!!", state));
    }

    [Fact]
    public void TryApply_ReturnsFalse_ForValidBase64ButNotCompressed()
    {
        var state = CreateState();
        // Valid base64url but not DEFLATE-compressed JSON
        var encoded = Convert.ToBase64String("hello world"u8.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.False(ShareableLinkService.TryApply(encoded, state));
    }

    [Fact]
    public void TryApply_ReturnsFalse_ForValidCompressedButNotJson()
    {
        // Compress non-JSON data
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(
            output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflate.Write("this is not json"u8);
        }
        var encoded = Convert.ToBase64String(output.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var state = CreateState();
        Assert.False(ShareableLinkService.TryApply(encoded, state));
    }

    [Fact]
    public void TryApply_DoesNotModifyState_OnFailure()
    {
        var state = CreateState();
        state.BaselineYear = 2021;
        state.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.10);

        ShareableLinkService.TryApply("garbage!!!", state);

        // State should be unchanged
        Assert.Equal(2021, state.BaselineYear);
        Assert.Equal(0.10, state.PartyUncertainty[Party.LPC]);
    }

    // ─────────────────────────────────────────────
    //  End-to-end: generate + extract + apply
    // ─────────────────────────────────────────────

    [Fact]
    public void EndToEnd_GeneratedUrlCanBeFullyRestored()
    {
        var source = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        Assert.NotNull(param);

        var target = CreateState();
        var success = ShareableLinkService.TryApply(param!, target);

        Assert.True(success);

        // Verify complete state match
        Assert.Equal(source.BaselineYear, target.BaselineYear);
        Assert.Equal(source.Config.NumSimulations, target.Config.NumSimulations);
        Assert.Equal(source.Config.NationalSigma, target.Config.NationalSigma);
        Assert.Equal(source.Config.RegionalSigma, target.Config.RegionalSigma);
        Assert.Equal(source.Config.RidingSigma, target.Config.RidingSigma);
        Assert.Equal(source.Config.DegreesOfFreedom, target.Config.DegreesOfFreedom);
        Assert.Equal(source.Config.SwingBlendAlpha, target.Config.SwingBlendAlpha);
        Assert.Equal(source.Config.UseCorrelatedNoise, target.Config.UseCorrelatedNoise);
        Assert.Equal(source.Config.UseDemographicPrior, target.Config.UseDemographicPrior);
        Assert.Equal(source.Config.DemographicBlendWeight, target.Config.DemographicBlendWeight);
        Assert.Equal(source.Config.ByElectionBlendWeight, target.Config.ByElectionBlendWeight);

        Assert.Equal(source.CurrentPolling.Count, target.CurrentPolling.Count);
        foreach (var (region, shares) in source.CurrentPolling)
        {
            Assert.Equal(shares.Count, target.CurrentPolling[region].Count);
            foreach (var (party, value) in shares)
            {
                Assert.Equal(value, target.CurrentPolling[region][party], 6);
            }
        }

        Assert.Equal(source.PartyUncertainty.Count, target.PartyUncertainty.Count);
        foreach (var (party, value) in source.PartyUncertainty)
        {
            Assert.Equal(value, target.PartyUncertainty[party], 6);
        }
    }

    [Fact]
    public void EndToEnd_TwoDistinctStatesProduceDifferentUrls()
    {
        var stateA = CreateState();
        stateA.SetPolling(TestHelpers.MakeDominantPolling(Party.CPC));
        stateA.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

        var stateB = CreateState();
        stateB.SetPolling(TestHelpers.MakeDominantPolling(Party.LPC));
        stateB.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

        var urlA = ShareableLinkService.GenerateShareableUrl(stateA, "https://example.com/");
        var urlB = ShareableLinkService.GenerateShareableUrl(stateB, "https://example.com/");

        Assert.NotEqual(urlA, urlB);
    }

    [Fact]
    public void EndToEnd_DifferentConfigsProduceDifferentUrls()
    {
        var stateA = CreatePopulatedState();
        var stateB = CreatePopulatedState();
        stateB.Config = stateB.Config with { NumSimulations = 1000 };

        var urlA = ShareableLinkService.GenerateShareableUrl(stateA, "https://example.com/");
        var urlB = ShareableLinkService.GenerateShareableUrl(stateB, "https://example.com/");

        Assert.NotEqual(urlA, urlB);
    }

    // ─────────────────────────────────────────────
    //  Precision and floating-point fidelity
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PreservesSixDecimalPlaces()
    {
        var source = CreateState();
        var polling = new Dictionary<Region, Dictionary<Party, double>>
        {
            [Region.Ontario] = new()
            {
                [Party.LPC] = 0.123456,
                [Party.CPC] = 0.654321,
                [Party.NDP] = 0.111111,
                [Party.GPC] = 0.012345,
                [Party.PPC] = 0.098765,
                [Party.Other] = 0.000002,
            }
        };
        source.SetPolling(polling);
        source.PartyUncertainty = new Dictionary<Party, double>
        {
            [Party.LPC] = 0.123456,
            [Party.CPC] = 0.000001,
        };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(0.123456, target.CurrentPolling[Region.Ontario][Party.LPC], 6);
        Assert.Equal(0.654321, target.CurrentPolling[Region.Ontario][Party.CPC], 6);
        Assert.Equal(0.000002, target.CurrentPolling[Region.Ontario][Party.Other], 6);
        Assert.Equal(0.123456, target.PartyUncertainty[Party.LPC], 6);
        Assert.Equal(0.000001, target.PartyUncertainty[Party.CPC], 6);
    }

    // ─────────────────────────────────────────────
    //  Minimal / sparse state
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WorksWithSingleRegion()
    {
        var source = CreateState();
        source.SetPolling(new Dictionary<Region, Dictionary<Party, double>>
        {
            [Region.Quebec] = new()
            {
                [Party.BQ] = 0.35,
                [Party.LPC] = 0.30,
                [Party.CPC] = 0.20,
                [Party.NDP] = 0.10,
                [Party.GPC] = 0.03,
                [Party.PPC] = 0.02,
            }
        });
        source.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        var success = ShareableLinkService.TryApply(param!, target);

        Assert.True(success);
        Assert.Single(target.CurrentPolling);
        Assert.Equal(0.35, target.CurrentPolling[Region.Quebec][Party.BQ]);
    }

    [Fact]
    public void RoundTrip_WorksWithEmptyPolling()
    {
        var source = CreateState();
        source.SetPolling(new Dictionary<Region, Dictionary<Party, double>>());
        source.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        target.SetPolling(TestHelpers.MakeDominantPolling(Party.CPC)); // Pre-fill to verify overwrite
        ShareableLinkService.TryApply(param!, target);

        Assert.Empty(target.CurrentPolling);
    }

    // ─────────────────────────────────────────────
    //  Boolean config toggle coverage
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RoundTrip_PreservesBooleanCombinations(bool correlatedNoise, bool demographicPrior)
    {
        var source = CreatePopulatedState();
        source.Config = source.Config with
        {
            UseCorrelatedNoise = correlatedNoise,
            UseDemographicPrior = demographicPrior
        };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(correlatedNoise, target.Config.UseCorrelatedNoise);
        Assert.Equal(demographicPrior, target.Config.UseDemographicPrior);
    }

    // ─────────────────────────────────────────────
    //  Base64url encoding edge cases
    // ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_HandlesPaddingVariants()
    {
        // Different state sizes produce base64 strings with different padding requirements
        // (mod 4 = 0, 2, 3). Run several to exercise all padding branches.
        foreach (var party in new[] { Party.LPC, Party.CPC, Party.NDP, Party.BQ })
        {
            var source = CreateState();
            source.SetPolling(TestHelpers.MakeDominantPolling(party));
            source.PartyUncertainty = TestHelpers.MakeUniformUncertainty(0.06);

            var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
            var param = ShareableLinkService.ExtractShareParam(url);

            var target = CreateState();
            var success = ShareableLinkService.TryApply(param!, target);
            Assert.True(success, $"Round-trip failed for {party}-dominant polling");
        }
    }

    // ─────────────────────────────────────────────
    //  Config variations round-trip
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(50000)]
    public void RoundTrip_PreservesVariousSimulationCounts(int numSims)
    {
        var source = CreatePopulatedState();
        source.Config = source.Config with { NumSimulations = numSims };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(numSims, target.Config.NumSimulations);
    }

    [Fact]
    public void RoundTrip_PreservesZeroSigmaValues()
    {
        var source = CreatePopulatedState();
        source.Config = source.Config with
        {
            NationalSigma = 0.0,
            RegionalSigma = 0.0,
            RidingSigma = 0.0,
            SwingBlendAlpha = 0.0,
            DemographicBlendWeight = 0.0,
            ByElectionBlendWeight = 0.0,
        };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(0.0, target.Config.NationalSigma);
        Assert.Equal(0.0, target.Config.RegionalSigma);
        Assert.Equal(0.0, target.Config.RidingSigma);
        Assert.Equal(0.0, target.Config.SwingBlendAlpha);
        Assert.Equal(0.0, target.Config.DemographicBlendWeight);
        Assert.Equal(0.0, target.Config.ByElectionBlendWeight);
    }

    [Fact]
    public void RoundTrip_PreservesLargeSigmaValues()
    {
        var source = CreatePopulatedState();
        source.Config = source.Config with
        {
            NationalSigma = 0.50,
            RegionalSigma = 0.50,
            RidingSigma = 0.50,
        };

        var url = ShareableLinkService.GenerateShareableUrl(source, "https://example.com/");
        var param = ShareableLinkService.ExtractShareParam(url);

        var target = CreateState();
        ShareableLinkService.TryApply(param!, target);

        Assert.Equal(0.50, target.Config.NationalSigma);
        Assert.Equal(0.50, target.Config.RegionalSigma);
        Assert.Equal(0.50, target.Config.RidingSigma);
    }

    // ─────────────────────────────────────────────
    //  Different base URIs
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://localhost:5000")]
    public void GenerateShareableUrl_WorksWithVariousBaseUris(string baseUri)
    {
        var state = CreatePopulatedState();
        var url = ShareableLinkService.GenerateShareableUrl(state, baseUri);

        Assert.Contains("s=", url);

        var param = ShareableLinkService.ExtractShareParam(url);
        Assert.NotNull(param);

        var target = CreateState();
        Assert.True(ShareableLinkService.TryApply(param!, target));
    }
}

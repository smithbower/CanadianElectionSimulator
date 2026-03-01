namespace ElectionSim.Core.Models;

/// <summary>
/// Canadian federal political parties. Values are used as dictionary keys throughout
/// the simulation and serialized as camelCase strings in JSON.
/// </summary>
public enum Party
{
    /// <summary>Liberal Party of Canada.</summary>
    LPC,
    /// <summary>Conservative Party of Canada.</summary>
    CPC,
    /// <summary>New Democratic Party.</summary>
    NDP,
    /// <summary>Bloc Quebecois (Quebec only).</summary>
    BQ,
    /// <summary>Green Party of Canada.</summary>
    GPC,
    /// <summary>People's Party of Canada.</summary>
    PPC,
    /// <summary>All other parties and independents, aggregated.</summary>
    Other
}

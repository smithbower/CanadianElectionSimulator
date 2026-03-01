namespace ElectionSim.Core.Models;

/// <summary>
/// Geographic regions grouping Canadian provinces for regional polling and sigma multipliers.
/// Alberta is separated from Prairies due to its distinctly higher electoral volatility.
/// </summary>
public enum Region
{
    Atlantic,
    Quebec,
    Ontario,
    Prairies,
    Alberta,
    BritishColumbia,
    North
}

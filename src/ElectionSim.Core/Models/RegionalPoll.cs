namespace ElectionSim.Core.Models;

/// <summary>
/// Regional polling averages used as simulation inputs. Vote shares are fractions (0.0-1.0).
/// </summary>
/// <param name="Region">Geographic region this poll covers.</param>
/// <param name="VoteShares">Party-to-vote-share mapping (values sum to approximately 1.0).</param>
public record RegionalPoll(Region Region, Dictionary<Party, double> VoteShares);

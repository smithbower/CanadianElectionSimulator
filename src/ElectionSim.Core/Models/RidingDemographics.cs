namespace ElectionSim.Core.Models;

/// <summary>
/// Census demographic profile for a riding (2021 Census, retabulated to 2023 Representation Order).
/// Values are normalized to 0-1 range. Used as features for the demographic prior ridge regression.
/// </summary>
public record RidingDemographics(
    int RidingId,
    double MedianIncome,
    double PctUniversityEducated,
    double PctVisibleMinority,
    double PctImmigrant,
    double PctFrancophone,
    double PctSingleDetached,
    double MedianAge,
    double PctIndigenous,
    double PctHomeowner
)
{
    public static int FeatureCount => 9;

    /// <summary>
    /// Returns the 9 demographic features as an array for regression.
    /// Order must match across all usages.
    /// </summary>
    public double[] ToFeatureVector() =>
    [
        MedianIncome, PctUniversityEducated, PctVisibleMinority,
        PctImmigrant, PctFrancophone, PctSingleDetached,
        MedianAge, PctIndigenous, PctHomeowner
    ];

    public static string[] FeatureNames =>
    [
        "Median Income", "University Educated", "Visible Minority",
        "Immigrant", "Francophone", "Single-Detached",
        "Median Age", "Indigenous", "Homeowner"
    ];
}

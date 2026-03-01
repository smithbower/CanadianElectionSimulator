namespace ElectionSim.Core.Models;

/// <summary>
/// A federal electoral district (riding) under the 2023 Representation Order (343 ridings).
/// </summary>
/// <param name="Id">Elections Canada riding number.</param>
/// <param name="Name">English riding name.</param>
/// <param name="NameFr">French riding name.</param>
/// <param name="Province">Full province name (e.g., "Ontario").</param>
/// <param name="Region">Geographic region for polling and sigma multipliers.</param>
/// <param name="Latitude">Approximate centroid latitude for cartogram ordering.</param>
/// <param name="Longitude">Approximate centroid longitude for cartogram ordering.</param>
public record Riding(int Id, string Name, string NameFr, string Province, Region Region, double Latitude = 0, double Longitude = 0);

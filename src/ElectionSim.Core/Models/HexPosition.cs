namespace ElectionSim.Core.Models;

/// <summary>
/// Position of a riding's hexagon in the grid cartogram (pointy-top, odd-r layout).
/// </summary>
/// <param name="Id">Riding ID matching <see cref="Riding.Id"/>.</param>
/// <param name="Col">Column index in the hex grid.</param>
/// <param name="Row">Row index in the hex grid.</param>
public record HexPosition(int Id, int Col, int Row);

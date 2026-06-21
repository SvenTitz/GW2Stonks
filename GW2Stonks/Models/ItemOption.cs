namespace GW2Stonks.Models;

/// <summary>A selectable item (id + name + icon), e.g. for the "requires ingredient" filter dropdown.</summary>
public sealed record ItemOption(int Id, string Name, string? IconUrl);

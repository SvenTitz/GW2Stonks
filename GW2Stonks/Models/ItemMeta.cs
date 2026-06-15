namespace GW2Stonks.Models;

/// <summary>Lightweight item metadata used by the profit solver and views.</summary>
public sealed record ItemMeta(int Id, string Name, string? Icon, string Type, string Rarity);

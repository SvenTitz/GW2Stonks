using GW2Stonks.Models;

namespace GW2Stonks.Services;

/// <summary>
/// Static reference data for the three Heart of Maguuma Faction Provisioners, compiled from the GW2
/// wiki (the per-tab item lists aren't in the GW2 API). Each faction tab accepts any of its items
/// (1 item → 1 token, 7 per week per tab); the page prices them live to find the cheapest to buy.
/// Plus the daily-rotation crafting materials shared by the SotO/Janthir provisioners.
/// </summary>
public static class ProvisionerData
{
    public static readonly IReadOnlyList<ProvisionerVendor> Vendors = new List<ProvisionerVendor>
    {
        new()
        {
            Name = "Quartermaster Natomi", Zone = "Verdant Brink", Limit = "7 per week",
            Waypoint = "Shipwreck Peak Waypoint", WaypointChatLink = "[&BN4HAAA=]",
            Tabs = new List<ProvisionerTab>
            {
                new() { Name = "Sylvari", Items = new[]
                {
                    "Assassin's Krait Machete", "Assassin's Krait Star", "Assassin's Krait Shooter",
                    "Assassin's Masquerade Leggings", "Assassin's Noble Pants", "Assassin's Gladiator Legplates",
                } },
                new() { Name = "Itzel", Items = new[]
                {
                    "Carrion Krait Slayer", "Carrion Krait Wand", "Carrion Krait Short Bow",
                    "Carrion Masquerade Boots", "Carrion Noble Boots", "Carrion Gladiator Boots",
                } },
                new() { Name = "Pact", Items = new[]
                {
                    "Valkyrie Krait Shell", "Valkyrie Krait Crook", "Valkyrie Krait Whelk",
                    "Valkyrie Masquerade Raiments", "Valkyrie Noble Coat", "Valkyrie Gladiator Chestplate",
                } },
                new() { Name = "Noble", Items = new[]
                {
                    "Cleric's Krait Warhammer", "Cleric's Krait Star", "Cleric's Krait Handgun",
                    "Cleric's Masquerade Gloves", "Cleric's Noble Gloves", "Cleric's Gladiator Gauntlets",
                } },
                new() { Name = "Quaggan", Items = new[]
                {
                    "Apothecary's Krait Ripper", "Apothecary's Krait Crook", "Apothecary's Krait Recurve Bow",
                    "Apothecary's Masquerade Mantle", "Apothecary's Noble Shoulders", "Apothecary's Gladiator Pauldrons",
                } },
            }
        },
        new()
        {
            Name = "Scavenger Rakatin", Zone = "Auric Basin", Limit = "7 per week",
            Waypoint = "Wanderer's Waypoint", WaypointChatLink = "[&BNYHAAA=]",
            Tabs = new List<ProvisionerTab>
            {
                new() { Name = "Priory", Items = new[]
                {
                    "Bringer's Krait Morning Star", "Bringer's Krait Wand", "Bringer's Krait Recurve Bow",
                    "Giver's Masquerade Mask", "Giver's Noble Mask", "Giver's Gladiator Helm",
                } },
                new() { Name = "Exalted", Items = new[]
                {
                    "Valkyrie Krait Morning Star", "Valkyrie Krait Crook", "Valkyrie Krait Brazier",
                    "Valkyrie Masquerade Raiments", "Valkyrie Noble Coat", "Valkyrie Gladiator Chestplate",
                } },
                new() { Name = "Skritt", Items = new[]
                {
                    "Rampager's Krait Battleaxe", "Rampager's Krait Wand", "Rampager's Krait Shooter",
                    "Rampager's Masquerade Leggings", "Rampager's Noble Pants", "Rampager's Gladiator Legplates",
                } },
            }
        },
        new()
        {
            Name = "Supply Assistant", Zone = "Tangled Depths", Limit = "7 per week",
            Waypoint = "Ogre Camp Waypoint", WaypointChatLink = "[&BMwHAAA=]",
            Tabs = new List<ProvisionerTab>
            {
                new() { Name = "Ogre", Items = new[]
                {
                    "Berserker's Krait Shell", "Berserker's Krait Star", "Berserker's Krait Brazier",
                    "Berserker's Masquerade Mantle", "Berserker's Noble Shoulders", "Berserker's Gladiator Pauldrons",
                } },
                new() { Name = "Rata Novus", Items = new[]
                {
                    "Carrion Krait Battleaxe", "Carrion Krait Star", "Carrion Krait Recurve Bow",
                    "Carrion Masquerade Gloves", "Carrion Noble Gloves", "Carrion Gladiator Gauntlets",
                } },
                new() { Name = "Nuhoch", Items = new[]
                {
                    "Knight's Krait Warhammer", "Knight's Krait Crook", "Knight's Krait Whelk",
                    "Knight's Masquerade Boots", "Knight's Noble Boots", "Knight's Gladiator Boots",
                } },
                new() { Name = "SCAR Camp", Items = new[]
                {
                    "Apothecary's Krait Ripper", "Apothecary's Krait Wand", "Apothecary's Krait Shooter",
                    "Apothecary's Masquerade Mask", "Apothecary's Noble Mask", "Apothecary's Gladiator Helm",
                } },
            }
        },
    };

    /// <summary>
    /// Crafting materials on the daily rotation at the SotO/Janthir Faction Provisioners — three of
    /// these appear each day; you trade the listed quantity for one token.
    /// </summary>
    public static readonly IReadOnlyList<ProvisionerOffer> DailyRotationMaterials = new List<ProvisionerOffer>
    {
        new("Silk Scrap", 500),
        new("Mithril Ore", 250),
        new("Elder Wood Log", 250),
        new("Thick Leather Section", 250),
        new("Research Note", 100),
        new("Gossamer Scrap", 100),
        new("Orichalcum Ore", 50),
        new("Ancient Wood Log", 50),
        new("Hardened Leather Section", 50),
        new("Glob of Ectoplasm", 5),
    };
}

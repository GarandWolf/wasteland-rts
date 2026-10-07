using System;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Mirrors the Lead/Designer MVP seat JSON shape (PR #17) without parsing
    /// that file. Catalogs are string ids; <c>autoResearch</c> is item plus
    /// <c>notBeforeTick</c>. Synthetic fixtures only.
    /// </summary>
    public readonly struct AiMvpAutoResearchItem
    {
        public readonly string Item;
        public readonly int NotBeforeTick;

        public AiMvpAutoResearchItem(string item, int notBeforeTick)
        {
            Item = item ?? string.Empty;
            NotBeforeTick = notBeforeTick;
        }
    }

    public readonly struct AiMvpSeatShape
    {
        public readonly string Faction;
        public readonly string AiPersonality;
        public readonly int HqTownId;
        public readonly int StartingManpower;
        public readonly string[] RaisableUnits;
        public readonly string[] BuildableBuildings;
        public readonly AiMvpAutoResearchItem[] AutoResearch;

        public AiMvpSeatShape(
            string faction,
            string aiPersonality,
            int hqTownId,
            int startingManpower,
            string[] raisableUnits,
            string[] buildableBuildings,
            AiMvpAutoResearchItem[] autoResearch)
        {
            Faction = faction;
            AiPersonality = aiPersonality;
            HqTownId = hqTownId;
            StartingManpower = startingManpower;
            RaisableUnits = raisableUnits;
            BuildableBuildings = buildableBuildings;
            AutoResearch = autoResearch ?? Array.Empty<AiMvpAutoResearchItem>();
        }
    }

    public readonly struct AiMvpMatchRulesShape
    {
        public readonly bool FuelEnabled;
        public readonly string SimultaneousHqLoss;

        public AiMvpMatchRulesShape(bool fuelEnabled, string simultaneousHqLoss)
        {
            FuelEnabled = fuelEnabled;
            SimultaneousHqLoss = simultaneousHqLoss ?? string.Empty;
        }
    }
}

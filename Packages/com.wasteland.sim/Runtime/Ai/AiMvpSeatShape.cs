using System;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Mirrors the Lead/Designer MVP seat JSON shape without parsing that
    /// file. Catalogs are string ids; <c>autoResearch</c> is item plus
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
        public readonly int Slot;
        public readonly string Faction;
        public readonly string? AiPersonality;
        public readonly ulong HomeTerritoryKey;
        public readonly int StartingManpower;
        public readonly string[] RaisableUnits;
        public readonly string[] BuildableBuildings;
        public readonly AiMvpAutoResearchItem[] AutoResearch;
        public readonly int HomeTownId;

        public AiMvpSeatShape(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            int startingManpower,
            string[] raisableUnits,
            string[] buildableBuildings,
            AiMvpAutoResearchItem[] autoResearch)
            : this(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                startingManpower,
                raisableUnits,
                buildableBuildings,
                autoResearch,
                0)
        {
        }

        public AiMvpSeatShape(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            int startingManpower,
            string[] raisableUnits,
            string[] buildableBuildings,
            AiMvpAutoResearchItem[] autoResearch,
            int homeTownId)
        {
            Slot = slot;
            Faction = faction;
            AiPersonality = aiPersonality;
            HomeTerritoryKey = homeTerritoryKey;
            StartingManpower = startingManpower;
            RaisableUnits = raisableUnits;
            BuildableBuildings = buildableBuildings;
            AutoResearch = autoResearch ?? Array.Empty<AiMvpAutoResearchItem>();
            HomeTownId = homeTownId;
        }
    }

    public readonly struct AiMvpMatchRulesShape
    {
        public readonly bool FuelEnabled;
        public readonly string SimultaneousHqLoss;
        public readonly ulong NeutralPrizeTerritoryKey;

        public AiMvpMatchRulesShape(bool fuelEnabled, string simultaneousHqLoss)
            : this(fuelEnabled, simultaneousHqLoss, 0UL)
        {
        }

        public AiMvpMatchRulesShape(
            bool fuelEnabled,
            string simultaneousHqLoss,
            ulong neutralPrizeTerritoryKey)
        {
            FuelEnabled = fuelEnabled;
            SimultaneousHqLoss = simultaneousHqLoss ?? string.Empty;
            NeutralPrizeTerritoryKey = neutralPrizeTerritoryKey;
        }
    }
}

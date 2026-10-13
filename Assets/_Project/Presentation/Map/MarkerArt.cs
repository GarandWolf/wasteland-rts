#nullable disable
using System.Collections.Generic;

namespace Wasteland.Presentation
{
    public enum BattalionGlyph
    {
        Plain = 0,
        Rifle = 1,
        Recon = 2,
        Mech = 3,
        Armour = 4,
        Artillery = 5,
        Drone = 6,
        Engineer = 7,
        Supply = 8,
        Militia = 9,
        AntiTank = 10,
        Towed = 11
    }

    /// <summary>
    /// Town and battalion marks. Seat towns come from the map; the HQ mark
    /// comes from the scenario's seat territories. Owner colours are not tinted.
    /// </summary>
    public static class MarkerArt
    {
        public const int Ink = 0x1E1F1D;
        public const int Remnant = 0x3A78BE;
        public const int Compact = 0xF2B418;
        public const int Neutral = 0xC9C4B8;
        public const int Bone = 0xE8E2D0;
        public const float OutlinePx = 1.5f;
        public const float StripePx = 4f;
        public const float SelectionBonePx = 2f;
        public const float SelectionInkPx = 1f;
        public const int BattalionWidthPx = 22;
        public const int BattalionHeightPx = 14;
        public const string RemnantFaction = "remnant_command";
        public const string CompactFaction = "homestead_compact";

        public static int TownDiameterPx(int populationClass)
        {
            if (populationClass == 1)
            {
                return 14;
            }

            if (populationClass == 2)
            {
                return 18;
            }

            if (populationClass == 3)
            {
                return 22;
            }

            return 10;
        }

        public static bool ShowsCaptureRing(bool isSeatTown)
        {
            return isSeatTown;
        }

        public static bool ShowsHq(bool isScenarioSeatTown)
        {
            return isScenarioSeatTown;
        }

        public static Rgb OwnerColor(int seatId, string factionKey)
        {
            if (seatId < 0 || factionKey == null)
            {
                return Rgb.FromHex(Neutral);
            }

            if (factionKey == RemnantFaction)
            {
                return Rgb.FromHex(Remnant);
            }

            if (factionKey == CompactFaction)
            {
                return Rgb.FromHex(Compact);
            }

            return Rgb.FromHex(Neutral);
        }

        public static bool TryGlyph(string unitId, out BattalionGlyph glyph)
        {
            switch (unitId)
            {
                case "remnant_motor_rifles":
                case "compact_rifle_battalion":
                    glyph = BattalionGlyph.Rifle;
                    return true;
                case "remnant_recon":
                case "compact_scouts":
                    glyph = BattalionGlyph.Recon;
                    return true;
                case "remnant_mech_battalion":
                    glyph = BattalionGlyph.Mech;
                    return true;
                case "remnant_armoured_battalion":
                    glyph = BattalionGlyph.Armour;
                    return true;
                case "remnant_sp_artillery":
                    glyph = BattalionGlyph.Artillery;
                    return true;
                case "compact_towed_artillery":
                    glyph = BattalionGlyph.Towed;
                    return true;
                case "remnant_drone_detachment":
                    glyph = BattalionGlyph.Drone;
                    return true;
                case "remnant_engineers":
                case "compact_engineers":
                    glyph = BattalionGlyph.Engineer;
                    return true;
                case "remnant_supply_convoy":
                case "compact_supply_convoy":
                    glyph = BattalionGlyph.Supply;
                    return true;
                case "compact_militia":
                    glyph = BattalionGlyph.Militia;
                    return true;
                case "compact_at_battalion":
                    glyph = BattalionGlyph.AntiTank;
                    return true;
                default:
                    glyph = BattalionGlyph.Plain;
                    return false;
            }
        }
    }

    /// <summary>One logged warning per unit id that has no glyph.</summary>
    public sealed class GlyphBook
    {
        readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        public BattalionGlyph Resolve(string unitId)
        {
            BattalionGlyph glyph;
            if (MarkerArt.TryGlyph(unitId, out glyph))
            {
                return glyph;
            }

            string key = unitId ?? string.Empty;
            for (int i = 0; i < _warnings.Count; i++)
            {
                if (_warnings[i] == key)
                {
                    return BattalionGlyph.Plain;
                }
            }

            _warnings.Add(key);
            return BattalionGlyph.Plain;
        }
    }
}

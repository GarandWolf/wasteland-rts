namespace Wasteland.Audio
{
    /// <summary>
    /// One placeholder event from MVP presentation spec §11.
    /// Values are stable: the committed sound bank stores them as integers.
    /// </summary>
    public enum SoundCue
    {
        /// <summary>HUD click. Rejected human orders use <see cref="SoundPlayer.PlayLow"/>.</summary>
        UiClick = 0,

        /// <summary>Human command accepted at issue time, not at the execute tick.</summary>
        OrderAck = 1,

        /// <summary>Battalion spawned. Play at the seat HQ.</summary>
        RaiseBattalion = 2,

        /// <summary>Depot reached its active tick. Not the site tick.</summary>
        DepotBuilt = 3,

        /// <summary>Looping combat bed. <see cref="CombatLoopManager"/> owns this cue.</summary>
        CombatLoop = 4,

        /// <summary>Viewer's side captured a town.</summary>
        TownCaptured = 5,

        /// <summary>Viewer's side lost a town.</summary>
        TownLost = 6,

        /// <summary>Win result screen.</summary>
        WinStinger = 7,

        /// <summary>Lose result screen.</summary>
        LoseStinger = 8,

        /// <summary>Draw result screen. Both HQs fell on the same tick.</summary>
        DrawStinger = 9,
    }

    /// <summary>
    /// File names for <see cref="SoundCue"/>. The bank asset holds the clip references.
    /// </summary>
    public static class SoundCues
    {
        public const int Count = 10;
        public const string PlaceholderDirectory = "Assets/_Project/Audio/Placeholder/";

        public static string FileName(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.UiClick: return "ui_click.wav";
                case SoundCue.OrderAck: return "order_ack.wav";
                case SoundCue.RaiseBattalion: return "raise_battalion.wav";
                case SoundCue.DepotBuilt: return "depot_built.wav";
                case SoundCue.CombatLoop: return "combat_loop.wav";
                case SoundCue.TownCaptured: return "town_captured.wav";
                case SoundCue.TownLost: return "town_lost.wav";
                case SoundCue.WinStinger: return "win_stinger.wav";
                case SoundCue.LoseStinger: return "lose_stinger.wav";
                case SoundCue.DrawStinger: return "draw_stinger.wav";
                default: return null;
            }
        }

        public static string ClipPath(SoundCue cue)
        {
            string fileName = FileName(cue);
            if (fileName == null)
            {
                return null;
            }

            return PlaceholderDirectory + fileName;
        }

        /// <summary>
        /// 3D cues take a world position. UI clicks, the order ack, and the result stingers are 2D.
        /// </summary>
        public static bool IsSpatial(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.RaiseBattalion:
                case SoundCue.DepotBuilt:
                case SoundCue.CombatLoop:
                case SoundCue.TownCaptured:
                case SoundCue.TownLost:
                    return true;
                default:
                    return false;
            }
        }
    }
}

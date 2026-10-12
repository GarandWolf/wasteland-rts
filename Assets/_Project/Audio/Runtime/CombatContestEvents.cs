using System.Collections.Generic;

namespace Wasteland.Audio
{
    /// <summary>
    /// Reads TownContestStarted (10) and TownContestEnded (11).
    /// The town id stays in the subject field. Seat A is aux and seat B is param0,
    /// with -1 when a garrison is one of the sides.
    /// </summary>
    public static class CombatContestEvents
    {
        public const int Started = 10;
        public const int Ended = 11;

        public static bool TryRead(
            int kind,
            int subjectId,
            int auxId,
            int param0,
            out int townId,
            out int seatA,
            out int seatB,
            out bool started)
        {
            townId = subjectId;
            seatA = auxId;
            seatB = param0;
            started = kind == Started;
            return kind == Started || kind == Ended;
        }
    }

    /// <summary>
    /// Towns whose combat bed should be playing. Started adds the town, ended removes it.
    /// </summary>
    public sealed class CombatBedLedger
    {
        private readonly List<int> _towns = new List<int>();

        public int Count
        {
            get { return _towns.Count; }
        }

        public void Clear()
        {
            _towns.Clear();
        }

        public bool Contains(int townId)
        {
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i] == townId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool Apply(int kind, int subjectId, int auxId, int param0)
        {
            int townId;
            int seatA;
            int seatB;
            bool started;
            if (!CombatContestEvents.TryRead(kind, subjectId, auxId, param0, out townId, out seatA, out seatB, out started))
            {
                return false;
            }

            if (started)
            {
                if (!Contains(townId))
                {
                    _towns.Add(townId);
                }

                return true;
            }

            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i] == townId)
                {
                    _towns.RemoveAt(i);
                    break;
                }
            }

            return true;
        }
    }
}

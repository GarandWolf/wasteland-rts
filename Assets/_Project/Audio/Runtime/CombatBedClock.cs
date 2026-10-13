#nullable disable
using System;
using System.Collections.Generic;

namespace Wasteland.Audio
{
    public readonly struct BedEvent
    {
        public readonly int Kind;
        public readonly int TownId;
        public readonly int XCm;
        public readonly int YCm;

        public BedEvent(int kind, int townId, int xCm, int yCm)
        {
            Kind = kind;
            TownId = townId;
            XCm = xCm;
            YCm = yCm;
        }
    }

    public readonly struct AudibleBed
    {
        public readonly int VoiceId;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int MemberCount;
        public readonly bool AnyHeld;

        public AudibleBed(int voiceId, int xCm, int yCm, int memberCount, bool anyHeld)
        {
            VoiceId = voiceId;
            XCm = xCm;
            YCm = yCm;
            MemberCount = memberCount;
            AnyHeld = anyHeld;
        }
    }

    /// <summary>
    /// Combat beds keyed by town. An event 11 holds the bed for
    /// <see cref="HoldTicks"/> ticks. A 10 in that window keeps the same bed.
    /// Towns within <see cref="SharedBedRadiusCm"/> share one bed. Held towns
    /// still count toward <see cref="VoiceCap"/> and toward that shared bed.
    /// The release fade is <see cref="ReleaseFadeSeconds"/>.
    /// </summary>
    public sealed class CombatBedClock
    {
        public const int HoldTicks = 40;
        public const float ReleaseFadeSeconds = 1.5f;
        public const int SharedBedRadiusCm = 300000;
        public const int VoiceCap = 3;

        readonly List<TownBed> _towns = new List<TownBed>();
        int _lastTick = int.MinValue;

        public static float FadeSeconds(float currentGain, float targetGain, float attackSeconds)
        {
            if (targetGain < currentGain)
            {
                return ReleaseFadeSeconds;
            }

            return attackSeconds;
        }

        public void Clear()
        {
            _towns.Clear();
            _lastTick = int.MinValue;
        }

        public void ApplyTick(int tick, BedEvent[] events)
        {
            if (_lastTick != int.MinValue && tick > _lastTick)
            {
                Age(tick - _lastTick);
            }

            _lastTick = tick;
            for (int i = 0; i < _towns.Count; i++)
            {
                _towns[i].Continued = false;
            }

            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Length; i++)
            {
                ApplyOne(events[i]);
            }
        }

        public bool IsAudible(int townId)
        {
            TownBed town = Find(townId);
            return town != null && town.Audible;
        }

        public int Generation(int townId)
        {
            TownBed town = Find(townId);
            return town == null ? 0 : town.Generation;
        }

        public int Layers(int townId)
        {
            TownBed town = Find(townId);
            return town != null && town.Audible ? 1 : 0;
        }

        public bool ContinuedWithoutRestart(int townId)
        {
            TownBed town = Find(townId);
            return town != null && town.Continued;
        }

        public void CopyAudible(List<AudibleBed> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            int count = 0;
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i].Audible)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return;
            }

            int[] ids = new int[count];
            int[] xs = new int[count];
            int[] ys = new int[count];
            bool[] held = new bool[count];
            int n = 0;
            for (int i = 0; i < _towns.Count; i++)
            {
                TownBed town = _towns[i];
                if (!town.Audible)
                {
                    continue;
                }

                ids[n] = town.Id;
                xs[n] = town.XCm;
                ys[n] = town.YCm;
                held[n] = !town.Contested;
                n++;
            }

            int[] group = new int[count];
            int groups = Cluster(ids, xs, ys, SharedBedRadiusCm, group);
            for (int g = 0; g < groups; g++)
            {
                long sumX = 0;
                long sumY = 0;
                int members = 0;
                int fresh = int.MaxValue;
                int voice = 0;
                bool anyHeld = false;
                for (int i = 0; i < count; i++)
                {
                    if (group[i] != g)
                    {
                        continue;
                    }

                    sumX += xs[i];
                    sumY += ys[i];
                    members++;
                    if (ids[i] < fresh)
                    {
                        fresh = ids[i];
                    }

                    TownBed town = Find(ids[i]);
                    if (town.StickyVoice != 0 && GroupHas(ids, group, g, town.StickyVoice) && (voice == 0 || town.StickyVoice < voice))
                    {
                        voice = town.StickyVoice;
                    }

                    if (held[i])
                    {
                        anyHeld = true;
                    }
                }

                if (voice == 0)
                {
                    voice = fresh;
                }

                for (int i = 0; i < count; i++)
                {
                    if (group[i] == g)
                    {
                        Find(ids[i]).StickyVoice = voice;
                    }
                }

                into.Add(new AudibleBed(
                    voice,
                    (int)(sumX / members),
                    (int)(sumY / members),
                    members,
                    anyHeld));
            }
        }

        public static int SelectNearest(AudibleBed[] beds, int listenerXCm, int listenerYCm, int cap, int[] selected)
        {
            if (beds == null || selected == null || cap < 1 || selected.Length < 1)
            {
                return 0;
            }

            int limit = cap < selected.Length ? cap : selected.Length;
            if (limit > VoiceCap)
            {
                limit = VoiceCap;
            }

            int count = 0;
            for (int i = 0; i < beds.Length; i++)
            {
                long distance = Distance2(beds[i].XCm, beds[i].YCm, listenerXCm, listenerYCm);
                int insertAt = count;
                for (int slot = 0; slot < count; slot++)
                {
                    AudibleBed other = beds[selected[slot]];
                    long otherDistance = Distance2(other.XCm, other.YCm, listenerXCm, listenerYCm);
                    if (distance < otherDistance || (distance == otherDistance && beds[i].VoiceId < other.VoiceId))
                    {
                        insertAt = slot;
                        break;
                    }
                }

                if (insertAt >= limit)
                {
                    continue;
                }

                int next = count < limit ? count + 1 : count;
                for (int slot = next - 1; slot > insertAt; slot--)
                {
                    selected[slot] = selected[slot - 1];
                }

                selected[insertAt] = i;
                count = next;
            }

            return count;
        }

        public static int Cluster(int[] ids, int[] xs, int[] ys, int radiusCm, int[] groupOf)
        {
            int n = ids.Length;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            long radius2 = (long)radiusCm * radiusCm;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    long dx = (long)xs[i] - xs[j];
                    long dy = (long)ys[i] - ys[j];
                    if (dx * dx + dy * dy <= radius2)
                    {
                        Union(parent, i, j);
                    }
                }
            }

            int groups = 0;
            int[] remap = new int[n];
            for (int i = 0; i < n; i++)
            {
                remap[i] = -1;
            }

            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (remap[root] < 0)
                {
                    remap[root] = groups;
                    groups++;
                }

                groupOf[i] = remap[root];
            }

            return groups;
        }

        void Age(int steps)
        {
            for (int i = 0; i < _towns.Count; i++)
            {
                TownBed town = _towns[i];
                if (!town.Audible || town.Contested || town.HoldLeft <= 0)
                {
                    continue;
                }

                if (steps >= town.HoldLeft)
                {
                    town.HoldLeft = 0;
                    town.Audible = false;
                }
                else
                {
                    town.HoldLeft -= steps;
                }
            }
        }

        void ApplyOne(BedEvent ev)
        {
            if (ev.Kind != CombatContestEvents.Started && ev.Kind != CombatContestEvents.Ended)
            {
                return;
            }

            TownBed town = Find(ev.TownId);
            if (town == null)
            {
                town = new TownBed { Id = ev.TownId };
                _towns.Add(town);
            }

            town.XCm = ev.XCm;
            town.YCm = ev.YCm;
            if (ev.Kind == CombatContestEvents.Started)
            {
                if (town.Audible)
                {
                    town.Continued = true;
                }
                else
                {
                    town.Generation++;
                    town.Audible = true;
                }

                town.Contested = true;
                town.HoldLeft = 0;
                return;
            }

            if (!town.Audible)
            {
                return;
            }

            town.Contested = false;
            town.HoldLeft = HoldTicks;
        }

        TownBed Find(int townId)
        {
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i].Id == townId)
                {
                    return _towns[i];
                }
            }

            return null;
        }

        static long Distance2(int x, int y, int ox, int oy)
        {
            long dx = (long)x - ox;
            long dy = (long)y - oy;
            return dx * dx + dy * dy;
        }

        static bool GroupHas(int[] ids, int[] group, int groupId, int townId)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (group[i] == groupId && ids[i] == townId)
                {
                    return true;
                }
            }

            return false;
        }

        static int Find(int[] parent, int index)
        {
            int root = index;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[index] != root)
            {
                int next = parent[index];
                parent[index] = root;
                index = next;
            }

            return root;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        sealed class TownBed
        {
            public int Id;
            public int XCm;
            public int YCm;
            public bool Contested;
            public bool Audible;
            public int HoldLeft;
            public int Generation;
            public bool Continued;
            public int StickyVoice;
        }
    }
}

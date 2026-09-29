using System;
using System.IO;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Random;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Per-faction AI memory. Other sim systems must not read this. Stored in
    /// faction-id order, hashed, and save/loadable.
    /// </summary>
    public sealed class AiFactionState
    {
        public readonly int FactionId;
        public readonly string PersonalityId;
        public readonly string DifficultyId;
        public readonly int ReactionExtraTicks;
        public readonly int StrategicCadenceTicks;
        public readonly int MaxActiveOrders;
        public readonly int MinGarrisonPerTown;
        public readonly int ManpowerReserveSeconds;
        public readonly int EconomyBonusPpt;

        public AiPosture Posture;
        public int NextOrderId;
        public int OrderCount;
        public int TaskForceCount;
        public int WorkResumeId;
        public int PathRequestsThisTick;
        public int WorkUnitsThisTick;
        public bool StrategicDeferred;
        public SimRandom StrategyRng;
        public SimRandom TacticalRng;
        public SimRandom MistakeRng;
        public SimRandom ScoutRng;

        public AiFactionState(
            int factionId,
            string personalityId,
            string difficultyId,
            int reactionExtraTicks,
            int strategicCadenceTicks,
            int maxActiveOrders,
            int minGarrisonPerTown,
            int manpowerReserveSeconds,
            int economyBonusPpt,
            AiPosture posture,
            SimRandom strategyRng,
            SimRandom tacticalRng,
            SimRandom mistakeRng,
            SimRandom scoutRng)
        {
            FactionId = factionId;
            PersonalityId = personalityId ?? throw new ArgumentNullException(nameof(personalityId));
            DifficultyId = difficultyId ?? throw new ArgumentNullException(nameof(difficultyId));
            ReactionExtraTicks = reactionExtraTicks;
            StrategicCadenceTicks = strategicCadenceTicks;
            MaxActiveOrders = maxActiveOrders;
            MinGarrisonPerTown = minGarrisonPerTown;
            ManpowerReserveSeconds = manpowerReserveSeconds;
            EconomyBonusPpt = economyBonusPpt;
            Posture = posture;
            NextOrderId = 1;
            StrategyRng = strategyRng;
            TacticalRng = tacticalRng;
            MistakeRng = mistakeRng;
            ScoutRng = scoutRng;
        }

        public bool PlanThisTick(int tick)
        {
            int cadence = StrategicCadenceTicks;
            int offset = checked(tick + (FactionId * AiConstants.FactionPlanOffsetTicks));
            return offset % cadence == 0;
        }
    }

    public sealed class AiOwnedState
    {
        private readonly AiFactionState[] _factions;

        public AiOwnedState(AiFactionState[] factions)
        {
            if (factions == null || factions.Length < 1)
            {
                throw new ArgumentException("At least one AI faction is required.", nameof(factions));
            }

            _factions = (AiFactionState[])factions.Clone();
            SortByFactionId(_factions);
            for (int i = 1; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == _factions[i - 1].FactionId)
                {
                    throw new ArgumentException("Duplicate AI faction id.");
                }
            }
        }

        public int FactionCount
        {
            get { return _factions.Length; }
        }

        public AiFactionState FactionAt(int index)
        {
            return _factions[index];
        }

        public AiFactionState GetFaction(int factionId)
        {
            for (int i = 0; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == factionId)
                {
                    return _factions[i];
                }
            }

            throw new InvalidOperationException("Unknown AI faction " + factionId + ".");
        }

        public void HashInto(XxHash64Builder hash)
        {
            hash.AddInt(_factions.Length);
            for (int i = 0; i < _factions.Length; i++)
            {
                AiFactionState faction = _factions[i];
                hash.AddInt(faction.FactionId);
                hash.AddULong(AiBalanceTables.HashUtf8(faction.PersonalityId));
                hash.AddULong(AiBalanceTables.HashUtf8(faction.DifficultyId));
                hash.AddInt(faction.ReactionExtraTicks);
                hash.AddInt(faction.StrategicCadenceTicks);
                hash.AddInt(faction.MaxActiveOrders);
                hash.AddInt(faction.MinGarrisonPerTown);
                hash.AddInt(faction.ManpowerReserveSeconds);
                hash.AddInt(faction.EconomyBonusPpt);
                hash.AddByte((byte)faction.Posture);
                hash.AddInt(faction.NextOrderId);
                hash.AddInt(faction.OrderCount);
                hash.AddInt(faction.TaskForceCount);
                hash.AddInt(faction.WorkResumeId);
                hash.AddInt(faction.PathRequestsThisTick);
                hash.AddInt(faction.WorkUnitsThisTick);
                hash.AddBool(faction.StrategicDeferred);
                hash.AddULong(faction.StrategyRng.State);
                hash.AddULong(faction.StrategyRng.Stream);
                hash.AddULong(faction.TacticalRng.State);
                hash.AddULong(faction.TacticalRng.Stream);
                hash.AddULong(faction.MistakeRng.State);
                hash.AddULong(faction.MistakeRng.Stream);
                hash.AddULong(faction.ScoutRng.State);
                hash.AddULong(faction.ScoutRng.Stream);
            }
        }

        public byte[] Save()
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(AiConstants.SchemaVersion);
            writer.Write(_factions.Length);
            for (int i = 0; i < _factions.Length; i++)
            {
                AiFactionState faction = _factions[i];
                writer.Write(faction.FactionId);
                writer.Write(faction.PersonalityId);
                writer.Write(faction.DifficultyId);
                writer.Write(faction.ReactionExtraTicks);
                writer.Write(faction.StrategicCadenceTicks);
                writer.Write(faction.MaxActiveOrders);
                writer.Write(faction.MinGarrisonPerTown);
                writer.Write(faction.ManpowerReserveSeconds);
                writer.Write(faction.EconomyBonusPpt);
                writer.Write((byte)faction.Posture);
                writer.Write(faction.NextOrderId);
                writer.Write(faction.OrderCount);
                writer.Write(faction.TaskForceCount);
                writer.Write(faction.WorkResumeId);
                writer.Write(faction.PathRequestsThisTick);
                writer.Write(faction.WorkUnitsThisTick);
                writer.Write(faction.StrategicDeferred);
                WriteRng(writer, faction.StrategyRng);
                WriteRng(writer, faction.TacticalRng);
                WriteRng(writer, faction.MistakeRng);
                WriteRng(writer, faction.ScoutRng);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void Load(byte[] blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException(nameof(blob));
            }

            BinaryReader reader = new BinaryReader(new MemoryStream(blob));
            int schema = reader.ReadInt32();
            if (schema != AiConstants.SchemaVersion)
            {
                throw new InvalidDataException("Unsupported AI state schema.");
            }

            int count = reader.ReadInt32();
            if (count != _factions.Length)
            {
                throw new InvalidDataException("AI faction count changed across save/load.");
            }

            for (int i = 0; i < count; i++)
            {
                int factionId = reader.ReadInt32();
                if (factionId != _factions[i].FactionId)
                {
                    throw new InvalidDataException("AI faction id order changed across save/load.");
                }

                string personalityId = reader.ReadString();
                string difficultyId = reader.ReadString();
                if (personalityId != _factions[i].PersonalityId || difficultyId != _factions[i].DifficultyId)
                {
                    throw new InvalidDataException("AI personality/difficulty changed across save/load.");
                }

                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                _factions[i].Posture = (AiPosture)reader.ReadByte();
                _factions[i].NextOrderId = reader.ReadInt32();
                _factions[i].OrderCount = reader.ReadInt32();
                _factions[i].TaskForceCount = reader.ReadInt32();
                _factions[i].WorkResumeId = reader.ReadInt32();
                _factions[i].PathRequestsThisTick = reader.ReadInt32();
                _factions[i].WorkUnitsThisTick = reader.ReadInt32();
                _factions[i].StrategicDeferred = reader.ReadBoolean();
                _factions[i].StrategyRng = ReadRng(reader);
                _factions[i].TacticalRng = ReadRng(reader);
                _factions[i].MistakeRng = ReadRng(reader);
                _factions[i].ScoutRng = ReadRng(reader);
            }
        }

        private static void WriteRng(BinaryWriter writer, SimRandom rng)
        {
            writer.Write(rng.State);
            writer.Write(rng.Stream);
        }

        private static SimRandom ReadRng(BinaryReader reader)
        {
            ulong state = reader.ReadUInt64();
            ulong stream = reader.ReadUInt64();
            return SimRandom.FromPersisted(state, stream);
        }

        private static void SortByFactionId(AiFactionState[] factions)
        {
            for (int i = 1; i < factions.Length; i++)
            {
                AiFactionState row = factions[i];
                int j = i - 1;
                while (j >= 0 && factions[j].FactionId > row.FactionId)
                {
                    factions[j + 1] = factions[j];
                    j--;
                }

                factions[j + 1] = row;
            }
        }
    }
}

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
                hash.AddBool(faction.StrategicDeferred);
                hash.AddULong(faction.StrategyRng.State);
                hash.AddULong(faction.StrategyRng.Increment);
                hash.AddULong(faction.TacticalRng.State);
                hash.AddULong(faction.TacticalRng.Increment);
                hash.AddULong(faction.MistakeRng.State);
                hash.AddULong(faction.MistakeRng.Increment);
                hash.AddULong(faction.ScoutRng.State);
                hash.AddULong(faction.ScoutRng.Increment);
            }
        }

        public byte[] Save(ulong matchSeed, ulong balanceKey, ulong mapKey)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(AiConstants.StateSchemaVersion);
            writer.Write(matchSeed);
            writer.Write(balanceKey);
            writer.Write(mapKey);
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
                writer.Write(faction.StrategicDeferred);
                WriteRng(writer, faction.StrategyRng);
                WriteRng(writer, faction.TacticalRng);
                WriteRng(writer, faction.MistakeRng);
                WriteRng(writer, faction.ScoutRng);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void Load(byte[] blob, ulong matchSeed, ulong balanceKey, ulong mapKey)
        {
            if (blob == null)
            {
                throw new ArgumentNullException(nameof(blob));
            }

            BinaryReader reader = new BinaryReader(new MemoryStream(blob));
            int schema = reader.ReadInt32();
            if (schema != AiConstants.StateSchemaVersion)
            {
                throw new InvalidDataException("Unsupported AI state schema.");
            }

            ulong savedSeed = reader.ReadUInt64();
            ulong savedBalance = reader.ReadUInt64();
            ulong savedMap = reader.ReadUInt64();
            if (savedSeed != matchSeed || savedBalance != balanceKey || savedMap != mapKey)
            {
                throw new InvalidDataException("AI save header does not match current match seed/balance/map.");
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

                RequireInt32(reader, _factions[i].ReactionExtraTicks, "ReactionExtraTicks");
                RequireInt32(reader, _factions[i].StrategicCadenceTicks, "StrategicCadenceTicks");
                RequireInt32(reader, _factions[i].MaxActiveOrders, "MaxActiveOrders");
                RequireInt32(reader, _factions[i].MinGarrisonPerTown, "MinGarrisonPerTown");
                RequireInt32(reader, _factions[i].ManpowerReserveSeconds, "ManpowerReserveSeconds");
                RequireInt32(reader, _factions[i].EconomyBonusPpt, "EconomyBonusPpt");
                _factions[i].Posture = (AiPosture)reader.ReadByte();
                _factions[i].NextOrderId = reader.ReadInt32();
                _factions[i].OrderCount = reader.ReadInt32();
                _factions[i].TaskForceCount = reader.ReadInt32();
                _factions[i].WorkResumeId = reader.ReadInt32();
                _factions[i].StrategicDeferred = reader.ReadBoolean();
                _factions[i].StrategyRng = ReadRng(reader);
                _factions[i].TacticalRng = ReadRng(reader);
                _factions[i].MistakeRng = ReadRng(reader);
                _factions[i].ScoutRng = ReadRng(reader);
                _factions[i].PathRequestsThisTick = 0;
                _factions[i].WorkUnitsThisTick = 0;
            }
        }

        private static void RequireInt32(BinaryReader reader, int expected, string name)
        {
            int actual = reader.ReadInt32();
            if (actual != expected)
            {
                throw new InvalidDataException("AI save " + name + " does not match current config.");
            }
        }

        private static void WriteRng(BinaryWriter writer, SimRandom rng)
        {
            writer.Write(rng.State);
            writer.Write(rng.Increment);
        }

        private static SimRandom ReadRng(BinaryReader reader)
        {
            ulong state = reader.ReadUInt64();
            ulong increment = reader.ReadUInt64();
            return SimRandom.FromPersisted(state, increment);
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

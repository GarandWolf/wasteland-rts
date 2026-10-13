#nullable disable
using System;
using System.Collections.Generic;
using Wasteland.Sim.Movement;

namespace Wasteland.Presentation.Tests
{
    static class ArtMapFixture
    {
        public const int ExtentCm = 10000;
        public const int ElevOffsetCm = 100;
        public const int ElevUnitCm = 20;
        public const int RawElevation = 3;
        public const ulong SeatKey = 0x111;
        public const ulong PrizeKey = 0x222;

        public static void WritePair(out byte[] main, out byte[] terrain)
        {
            byte[] strp = Bytes("Seat Alpha", "Prize", "Dallesport", "Celilo Village", "Wishram", "Bingen");
            int[] nameAt = NameOffsets("Seat Alpha", "Prize", "Dallesport", "Celilo Village", "Wishram", "Bingen");
            Buf towns = new Buf();
            WriteTown(towns, 41, 1000, 1000, 1, 2, 4, nameAt[0], "Seat Alpha".Length);
            WriteTown(towns, 42, 7000, 7000, 2, 3, 9, nameAt[1], "Prize".Length);
            WriteTown(towns, 43, 2000, 2000, 2, 1, 4, nameAt[2], "Dallesport".Length);
            WriteTown(towns, 44, 2200, 2400, 2, 0, 4, nameAt[3], "Celilo Village".Length);
            WriteTown(towns, 45, 2400, 2600, 2, 0, 4, nameAt[4], "Wishram".Length);
            WriteTown(towns, 46, 2600, 2800, 2, 0, 4, nameAt[5], "Bingen".Length);

            Buf terr = new Buf();
            WriteTerritory(terr, 1, 41, SeatKey);
            WriteTerritory(terr, 2, 42, PrizeKey);

            Buf nodes = new Buf();
            WriteNode(nodes, 4, 1100, 2100);
            WriteNode(nodes, 9, 8100, 6100);

            Buf points = new Buf();
            points.I32(1100);
            points.I32(2100);
            points.I32(4000);
            points.I32(4000);
            points.I32(8100);
            points.I32(6100);

            Buf edges = new Buf();
            WriteEdge(edges, 3, 4, 9, 2, 0, 3);

            main = WrmbFile.Encode(
                WrmbFile.FormatVersionV1,
                WrmbFile.FlagV1Final,
                new[]
                {
                    new WrmbSectionPayload("META", Meta(0, 2, 1, 2, 2, 0, 1), Wrmb.PipelineMetaElementSize),
                    new WrmbSectionPayload("TOWN", towns.ToArray(), MapDocument.TownRecordSize),
                    new WrmbSectionPayload("TERR", terr.ToArray(), MapDocument.TerritoryRecordSize),
                    new WrmbSectionPayload("RNOD", nodes.ToArray(), Wrmb.RnodElementSize),
                    new WrmbSectionPayload("REDG", edges.ToArray(), Wrmb.RedgElementSize),
                    new WrmbSectionPayload("PTSP", points.ToArray(), 8),
                    new WrmbSectionPayload("STRP", strp, 1)
                });

            int cells = 4;
            byte[] slope = new byte[cells * 2];
            byte[] elev = new byte[cells * 2];
            for (int i = 0; i < cells; i++)
            {
                elev[i * 2] = RawElevation;
            }

            byte[] water = new byte[24];
            water[0] = 2;
            water[4] = 2;
            water[8] = 2;
            water[16] = 1;
            water[17] = 2;
            water[18] = 3;
            water[19] = 4;

            terrain = WrmbFile.Encode(
                WrmbFile.FormatVersionV1,
                WrmbFile.FlagV1Final,
                new[]
                {
                    new WrmbSectionPayload("META", Meta(1, 0, 0, 2, 2, ElevOffsetCm, ElevUnitCm), Wrmb.PipelineMetaElementSize),
                    new WrmbSectionPayload("GRID", Grid("MVEL", 2, 2, ExtentCm), MapDocument.GridRecordSize),
                    new WrmbSectionPayload("MVSL", slope, 2),
                    new WrmbSectionPayload("MVLC", new byte[cells], 1),
                    new WrmbSectionPayload("MVFD", new byte[1], 1),
                    new WrmbSectionPayload("MVEL", elev, 2),
                    new WrmbSectionPayload("WTRA", water, 1)
                });
        }

        static byte[] Meta(int fileKind, int nodes, int edges, int moveW, int moveH, int elevOffset, int elevUnit)
        {
            Buf w = new Buf();
            w.I32(WrmbFile.SchemaVersion);
            w.I32(fileKind);
            w.I32(ExtentCm);
            w.I32(ExtentCm);
            w.I32(ExtentCm / 2);
            w.I32(2);
            w.I32(2);
            w.I32(0);
            w.I32(0);
            w.I32(nodes);
            w.I32(edges);
            w.Zero(60);
            w.I32(moveW);
            w.I32(moveH);
            w.I32(1);
            w.Zero(292 - w.Length);
            w.I32(elevOffset);
            w.I32(elevUnit);
            w.Zero(Wrmb.PipelineMetaElementSize - w.Length);
            if (w.Length != Wrmb.PipelineMetaElementSize)
            {
                throw new InvalidOperationException("META length " + w.Length);
            }

            return w.ToArray();
        }

        static byte[] Grid(string tag, int width, int height, int extent)
        {
            Buf b = new Buf();
            b.Ascii(tag);
            b.U8(2);
            b.U8(0);
            b.U16(0);
            b.I32(width);
            b.I32(height);
            b.I32(0);
            b.I32(0);
            b.I32(extent);
            b.I32(extent);
            b.I32(extent / width);
            b.I32(1);
            b.I32(2);
            b.I32(0);
            b.I32(1);
            b.I32(0);
            if (b.Length != MapDocument.GridRecordSize)
            {
                throw new InvalidOperationException("GRID length " + b.Length);
            }

            return b.ToArray();
        }

        static void WriteTown(Buf b, int id, int x, int y, int territory, int populationClass, int nodeId, int nameOff, int nameLen)
        {
            b.I32(id);
            b.I32(x);
            b.I32(y);
            b.I32(territory);
            b.I32(0);
            b.I32(100);
            b.I32(0);
            b.I32(nodeId);
            b.U16(0);
            b.U8((byte)populationClass);
            b.U8(0);
            b.U32(0);
            b.U32(0);
            b.U32(0);
            b.U32(0);
            b.U32((uint)nameOff);
            b.U32((uint)nameLen);
            b.U32(0);
        }

        static void WriteTerritory(Buf b, int id, int seatTownId, ulong key)
        {
            b.I32(id);
            b.I32(seatTownId);
            b.Zero(32);
            b.U64(key);
        }

        static void WriteNode(Buf b, int id, int x, int y)
        {
            b.I32(id);
            b.I32(x);
            b.I32(y);
            b.I32(0);
            b.U8(0);
            b.U8(0);
            b.U16(1);
            b.I32(0);
        }

        static void WriteEdge(Buf b, int id, int from, int to, int surface, int pointOffset, int pointCount)
        {
            int start = b.Length;
            b.I32(id);
            b.I32(from);
            b.I32(to);
            b.I32(500);
            b.I32(0);
            b.I32(0);
            b.I32(0);
            b.I32(0);
            b.I32(0);
            b.I32(1);
            b.U8(0);
            b.U8((byte)surface);
            b.U8(0);
            b.U8(2);
            b.U16(0);
            b.U16(0);
            b.U32((uint)pointOffset);
            b.U32((uint)pointCount);
            b.U32(0);
            b.U32(0);
            if (b.Length - start != Wrmb.RedgElementSize)
            {
                throw new InvalidOperationException("REDG length " + (b.Length - start));
            }
        }

        static byte[] Bytes(params string[] names)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < names.Length; i++)
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(names[i]));
            }

            return bytes.ToArray();
        }

        static int[] NameOffsets(params string[] names)
        {
            int[] at = new int[names.Length];
            int cursor = 0;
            for (int i = 0; i < names.Length; i++)
            {
                at[i] = cursor;
                cursor += System.Text.Encoding.UTF8.GetByteCount(names[i]);
            }

            return at;
        }
    }

    sealed class Buf
    {
        readonly List<byte> _bytes = new List<byte>();

        public int Length
        {
            get { return _bytes.Count; }
        }

        public void I32(int value)
        {
            U32((uint)value);
        }

        public void U32(uint value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }

        public void U16(ushort value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
        }

        public void U8(byte value)
        {
            _bytes.Add(value);
        }

        public void U64(ulong value)
        {
            U32((uint)value);
            U32((uint)(value >> 32));
        }

        public void Ascii(string tag)
        {
            for (int i = 0; i < tag.Length; i++)
            {
                _bytes.Add((byte)tag[i]);
            }
        }

        public void Zero(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _bytes.Add(0);
            }
        }

        public byte[] ToArray()
        {
            return _bytes.ToArray();
        }
    }
}

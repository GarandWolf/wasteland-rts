using System;
using System.IO;
using Wasteland.Sim.Movement;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class MapKeyTests
    {
        [Fact]
        public void For_ChangesWhenNamedSectionByteChanges_UnrelatedSectionIgnored()
        {
            byte[] extraA = { 1, 2, 3, 4 };
            byte[] extraB = { 5, 6, 7, 8 };
            byte[] blob = EncodeWithExtras(extraA, extraB);
            WrmbFile file = WrmbFile.Parse(blob);
            ulong keyA = MapKey.For(file, "AAAA");
            ulong keyB = MapKey.For(file, "BBBB");
            ulong keyBoth = MapKey.For(file, "BBBB", "AAAA");
            Assert.Equal(keyBoth, MapKey.For(file, "AAAA", "BBBB"));
            Assert.NotEqual(keyA, keyB);

            byte[] extraA2 = { 1, 2, 3, 5 };
            WrmbFile changedA = WrmbFile.Parse(EncodeWithExtras(extraA2, extraB));
            Assert.NotEqual(keyA, MapKey.For(changedA, "AAAA"));
            Assert.Equal(keyB, MapKey.For(changedA, "BBBB"));

            byte[] extraB2 = { 5, 6, 7, 9 };
            WrmbFile changedB = WrmbFile.Parse(EncodeWithExtras(extraA, extraB2));
            Assert.Equal(keyA, MapKey.For(changedB, "AAAA"));
            Assert.NotEqual(keyB, MapKey.For(changedB, "BBBB"));
        }

        [Fact]
        public void For_MixesTagAndCount_NotPayloadAlone()
        {
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8 };
            WrmbFile eight = WrmbFile.Parse(EncodeNamed("AAAA", payload, 8));
            WrmbFile four = WrmbFile.Parse(EncodeNamed("AAAA", payload, 4));
            Assert.NotEqual(MapKey.For(eight, "AAAA"), MapKey.For(four, "AAAA"));

            WrmbFile otherTag = WrmbFile.Parse(EncodeNamed("BBBB", payload, 8));
            Assert.NotEqual(MapKey.For(eight, "AAAA"), MapKey.For(otherTag, "BBBB"));
        }

        static byte[] EncodeWithExtras(byte[] aaaa, byte[] bbbb)
        {
            byte[] baseBlob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            return WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", SectionOf(baseBlob, "META"), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("RNOD", SectionOf(baseBlob, "RNOD"), Wrmb.RnodElementSize),
                    new WrmbSectionPayload("REDG", SectionOf(baseBlob, "REDG"), Wrmb.RedgElementSize),
                    new WrmbSectionPayload("AAAA", aaaa, aaaa.Length),
                    new WrmbSectionPayload("BBBB", bbbb, bbbb.Length)
                });
        }

        static byte[] EncodeNamed(string tag, byte[] data, int elementSize)
        {
            byte[] baseBlob = new RoadGraphBuilder().AddNode(1, 0, 0).ToBlob();
            return WrmbFile.Encode(
                WrmbFile.FlagDraftLayout,
                new[]
                {
                    new WrmbSectionPayload("META", SectionOf(baseBlob, "META"), Wrmb.MetaElementSize),
                    new WrmbSectionPayload("RNOD", SectionOf(baseBlob, "RNOD"), Wrmb.RnodElementSize),
                    new WrmbSectionPayload("REDG", SectionOf(baseBlob, "REDG"), Wrmb.RedgElementSize),
                    new WrmbSectionPayload(tag, data, elementSize)
                });
        }

        static byte[] SectionOf(byte[] blob, string tag)
        {
            WrmbFile file = WrmbFile.Parse(blob);
            return file.SectionBytes(file.RequireSection(tag));
        }
    }

    public sealed class BalanceKeyTests
    {
        [Fact]
        public void For_ChangesWhenFileByteChanges_UnrelatedFileIgnored()
        {
            string root = Path.Combine(Path.GetTempPath(), "wasteland-balance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            try
            {
                File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[] { 4, 5 });
                ulong original = BalanceKey.For(root);

                File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[] { 1, 2, 4 });
                ulong changedA = BalanceKey.For(root);
                Assert.NotEqual(original, changedA);

                File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[] { 1, 2, 3 });
                Assert.Equal(original, BalanceKey.For(root));

                File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[] { 4, 6 });
                Assert.NotEqual(original, BalanceKey.For(root));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        [Fact]
        public void For_MixesRelativePath()
        {
            string a = Path.Combine(Path.GetTempPath(), "wasteland-balance-a-" + Guid.NewGuid().ToString("N"));
            string b = Path.Combine(Path.GetTempPath(), "wasteland-balance-b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(Path.Combine(b, "nested"));
            try
            {
                byte[] data = { 9, 8, 7 };
                File.WriteAllBytes(Path.Combine(a, "x.bin"), data);
                File.WriteAllBytes(Path.Combine(b, "nested", "x.bin"), data);
                Assert.NotEqual(BalanceKey.For(a), BalanceKey.For(b));
            }
            finally
            {
                try
                {
                    Directory.Delete(a, true);
                    Directory.Delete(b, true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

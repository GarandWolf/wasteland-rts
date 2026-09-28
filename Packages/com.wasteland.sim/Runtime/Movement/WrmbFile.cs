using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Wasteland.Sim.Movement
{
    /// <summary>One WRMB directory entry. Sections are found by tag, never by fixed offset.</summary>
    public readonly struct WrmbDirEntry
    {
        public readonly string Tag;
        public readonly int Offset;
        public readonly int ByteLength;
        public readonly int Count;
        public readonly int ElementSize;
        public readonly uint Crc32;

        internal WrmbDirEntry(string tag, int offset, int byteLength, int count, int elementSize, uint crc32)
        {
            Tag = tag;
            Offset = offset;
            ByteLength = byteLength;
            Count = count;
            ElementSize = elementSize;
            Crc32 = crc32;
        }
    }

    /// <summary>Payload handed to <see cref="WrmbFile.Encode"/>.</summary>
    public readonly struct WrmbSectionPayload
    {
        public readonly string Tag;
        public readonly byte[] Data;
        public readonly int ElementSize;

        public WrmbSectionPayload(string tag, byte[] data, int elementSize)
        {
            if (tag == null || tag.Length != 4)
            {
                throw new ArgumentException("Section tag must be four ASCII characters.", nameof(tag));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (elementSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(elementSize));
            }

            if (data.Length % elementSize != 0)
            {
                throw new ArgumentException("Section byte length must be a multiple of elementSize.", nameof(data));
            }

            Tag = tag;
            Data = data;
            ElementSize = elementSize;
        }

        public int Count
        {
            get { return Data.Length / ElementSize; }
        }
    }

    /// <summary>
    /// WRMB container (layout v0, DRAFT). Header is 64 bytes; the section directory
    /// sits at <c>directoryOffset</c>. Unknown tags are retained and skipped by
    /// domain readers. See docs/formats/wrmb-v0.md.
    /// </summary>
    public sealed class WrmbFile
    {
        public const string Magic = "WRMB";
        public const ushort FormatVersion = 0;
        public const ushort SchemaVersion = 1;
        public const int HeaderSize = 64;
        public const int DirectoryEntrySize = 40;
        public const int Align = 8;
        public const uint FlagDraftLayout = 1;
        public const uint FlagHasDebugSections = 2;

        public static readonly string[] SectionOrder =
        {
            "META", "SRCS", "TOWN", "TDBG", "TERR", "RNOD", "REDG", "CHOK", "RIVR", "BRDG",
            "WATR", "WRNG", "MSET", "RSRC", "CELL", "CELX", "MVSL", "MVLC", "MVFD",
            "I32P", "PTSP", "STRP"
        };

        public readonly ushort FileFormatVersion;
        public readonly ushort FileSchemaVersion;
        public readonly uint Flags;
        public readonly int SectionCount;
        public readonly int DirectoryOffset;
        public readonly long FileSize;
        public readonly byte[] ContentHash;
        public readonly WrmbDirEntry[] Directory;
        internal readonly byte[] Bytes;

        private WrmbFile(
            byte[] bytes,
            ushort formatVersion,
            ushort schemaVersion,
            uint flags,
            int directoryOffset,
            long fileSize,
            byte[] contentHash,
            WrmbDirEntry[] directory)
        {
            Bytes = bytes;
            FileFormatVersion = formatVersion;
            FileSchemaVersion = schemaVersion;
            Flags = flags;
            SectionCount = directory.Length;
            DirectoryOffset = directoryOffset;
            FileSize = fileSize;
            ContentHash = contentHash;
            Directory = directory;
        }

        public static WrmbFile Parse(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length < HeaderSize)
            {
                throw new InvalidDataException("WRMB file shorter than header.");
            }

            BlobReader reader = new BlobReader(data);
            reader.ExpectMagic(Magic);
            ushort formatVersion = reader.U16();
            ushort schemaVersion = reader.U16();
            uint headerSize = reader.U32();
            uint flags = reader.U32();
            uint sectionCountU = reader.U32();
            uint directoryOffsetU = reader.U32();
            ulong fileSize = reader.U64();
            byte[] contentHash = reader.Bytes(32);

            if (headerSize != HeaderSize)
            {
                throw new InvalidDataException("WRMB headerSize must be 64.");
            }

            if (formatVersion != FormatVersion)
            {
                throw new InvalidDataException("Unsupported WRMB formatVersion " + formatVersion + ".");
            }

            if (schemaVersion != SchemaVersion)
            {
                throw new InvalidDataException("Unsupported WRMB schemaVersion " + schemaVersion + ".");
            }

            if (fileSize != (ulong)data.Length)
            {
                throw new InvalidDataException("WRMB fileSize does not match buffer length.");
            }

            if (sectionCountU > (uint)int.MaxValue / DirectoryEntrySize)
            {
                throw new InvalidDataException("WRMB sectionCount out of range.");
            }

            int sectionCount = (int)sectionCountU;
            if (directoryOffsetU > (uint)int.MaxValue)
            {
                throw new InvalidDataException("WRMB directoryOffset out of range.");
            }

            int directoryOffset = (int)directoryOffsetU;
            long dirEnd = (long)directoryOffset + (long)sectionCount * DirectoryEntrySize;
            if (directoryOffset < HeaderSize || dirEnd > data.Length)
            {
                throw new InvalidDataException("WRMB directory is out of bounds.");
            }

            byte[] hashInput = new byte[data.Length];
            Array.Copy(data, hashInput, data.Length);
            for (int i = 0; i < 32; i++)
            {
                hashInput[HeaderSize - 32 + i] = 0;
            }

            byte[] actualHash;
            using (SHA256 sha = SHA256.Create())
            {
                actualHash = sha.ComputeHash(hashInput);
            }

            if (!BytesEqual(actualHash, contentHash))
            {
                throw new InvalidDataException("WRMB contentHash mismatch.");
            }

            WrmbDirEntry[] directory = new WrmbDirEntry[sectionCount];
            reader.Seek(directoryOffset);
            for (int i = 0; i < sectionCount; i++)
            {
                string tag = reader.Ascii4();
                reader.U32();
                ulong offsetU = reader.U64();
                ulong lengthU = reader.U64();
                uint countU = reader.U32();
                uint elementSizeU = reader.U32();
                uint crc = reader.U32();
                reader.U32();

                if (offsetU > (ulong)int.MaxValue || lengthU > (ulong)int.MaxValue)
                {
                    throw new InvalidDataException(tag + ": section offset/length out of range.");
                }

                int offset = (int)offsetU;
                int length = (int)lengthU;
                if (offset % Align != 0)
                {
                    throw new InvalidDataException(tag + ": section offset is not 8-aligned.");
                }

                if (offset < 0 || (long)offset + length > data.Length)
                {
                    throw new InvalidDataException(tag + ": section is out of bounds.");
                }

                if (elementSizeU < 1)
                {
                    throw new InvalidDataException(tag + ": elementSize must be >= 1.");
                }

                long product = (long)countU * elementSizeU;
                if (product != length)
                {
                    throw new InvalidDataException(tag + ": count * elementSize != byteLength.");
                }

                uint actualCrc = WrmbCrc32.Compute(data, offset, length);
                if (actualCrc != crc)
                {
                    throw new InvalidDataException(tag + ": crc32 mismatch.");
                }

                for (int j = 0; j < i; j++)
                {
                    if (directory[j].Tag == tag)
                    {
                        throw new InvalidDataException("Duplicate WRMB section " + tag + ".");
                    }
                }

                directory[i] = new WrmbDirEntry(tag, offset, length, (int)countU, (int)elementSizeU, crc);
            }

            return new WrmbFile(
                data,
                formatVersion,
                schemaVersion,
                flags,
                directoryOffset,
                (long)fileSize,
                contentHash,
                directory);
        }

        public bool TryGetSection(string tag, out WrmbDirEntry entry)
        {
            if (tag == null)
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < Directory.Length; i++)
            {
                if (Directory[i].Tag == tag)
                {
                    entry = Directory[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        public WrmbDirEntry RequireSection(string tag)
        {
            if (!TryGetSection(tag, out WrmbDirEntry entry))
            {
                throw new InvalidDataException("WRMB missing section " + tag + ".");
            }

            return entry;
        }

        public byte[] SectionBytes(WrmbDirEntry entry)
        {
            byte[] slice = new byte[entry.ByteLength];
            Array.Copy(Bytes, entry.Offset, slice, 0, entry.ByteLength);
            return slice;
        }

        public static byte[] Encode(uint flags, WrmbSectionPayload[] sections)
        {
            if (sections == null)
            {
                throw new ArgumentNullException(nameof(sections));
            }

            List<WrmbSectionPayload> ordered = new List<WrmbSectionPayload>();
            bool[] used = new bool[sections.Length];
            for (int o = 0; o < SectionOrder.Length; o++)
            {
                string tag = SectionOrder[o];
                int found = -1;
                for (int i = 0; i < sections.Length; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    if (sections[i].Tag == tag)
                    {
                        if (found >= 0)
                        {
                            throw new InvalidDataException("Duplicate WRMB section " + tag + ".");
                        }

                        found = i;
                    }
                }

                if (found >= 0)
                {
                    used[found] = true;
                    ordered.Add(sections[found]);
                    continue;
                }

                if (tag == "I32P")
                {
                    ordered.Add(new WrmbSectionPayload(tag, Array.Empty<byte>(), 4));
                }
                else if (tag == "PTSP")
                {
                    ordered.Add(new WrmbSectionPayload(tag, Array.Empty<byte>(), 8));
                }
                else if (tag == "STRP")
                {
                    ordered.Add(new WrmbSectionPayload(tag, Array.Empty<byte>(), 1));
                }
            }

            for (int i = 0; i < sections.Length; i++)
            {
                if (used[i])
                {
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    if (!used[j] && sections[j].Tag == sections[i].Tag)
                    {
                        throw new InvalidDataException("Duplicate WRMB section " + sections[i].Tag + ".");
                    }
                }

                ordered.Add(sections[i]);
            }

            int n = ordered.Count;
            int dirBytes = n * DirectoryEntrySize;
            int start = HeaderSize + dirBytes;
            int rem = start % Align;
            if (rem != 0)
            {
                start += Align - rem;
            }

            BlobWriter body = new BlobWriter(256);
            int[] offsets = new int[n];
            uint[] crcs = new uint[n];
            int cursor = start;
            for (int i = 0; i < n; i++)
            {
                WrmbSectionPayload sec = ordered[i];
                offsets[i] = cursor;
                crcs[i] = WrmbCrc32.Compute(sec.Data);
                body.Bytes(sec.Data);
                int pad = sec.Data.Length % Align;
                if (pad != 0)
                {
                    body.Zeroes(Align - pad);
                    cursor += sec.Data.Length + (Align - pad);
                }
                else
                {
                    cursor += sec.Data.Length;
                }
            }

            int total = start + body.Length;
            BlobWriter file = new BlobWriter(total);
            file.Magic(Magic);
            file.U16(FormatVersion);
            file.U16(SchemaVersion);
            file.U32((uint)HeaderSize);
            file.U32(flags);
            file.U32((uint)n);
            file.U32((uint)HeaderSize);
            file.U64((ulong)total);
            file.Zeroes(32);

            for (int i = 0; i < n; i++)
            {
                WrmbSectionPayload sec = ordered[i];
                file.Magic(sec.Tag);
                file.U32(0);
                file.U64((ulong)offsets[i]);
                file.U64((ulong)sec.Data.Length);
                file.U32((uint)sec.Count);
                file.U32((uint)sec.ElementSize);
                file.U32(crcs[i]);
                file.U32(0);
            }

            file.Zeroes(start - HeaderSize - dirBytes);
            file.Bytes(body.ToArray());

            byte[] blob = file.ToArray();
            byte[] hashInput = new byte[blob.Length];
            Array.Copy(blob, hashInput, blob.Length);
            for (int i = 0; i < 32; i++)
            {
                hashInput[HeaderSize - 32 + i] = 0;
            }

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(hashInput);
            }

            Array.Copy(hash, 0, blob, HeaderSize - 32, 32);
            return blob;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

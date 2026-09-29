using System;
using System.Collections.Generic;
using System.Text;
using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Composite map identity: <c>terrainPayloadSha256</c> mixed with an
    /// xxHash64 over named WRMB sections. Each section contributes its FourCC
    /// tag, element count, and raw bytes (not payload-only). Tags are mixed in
    /// ordinal order so the caller's list order does not matter.
    /// </summary>
    public static class MapKey
    {
        public static ulong For(WrmbMap map, params string[] sections)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            return For(map.File, (IReadOnlyList<string>)sections);
        }

        public static ulong For(WrmbFile map, params string[] sections)
        {
            return For(map, (IReadOnlyList<string>)sections);
        }

        public static ulong For(WrmbFile map, IReadOnlyList<string> sections)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (sections == null)
            {
                throw new ArgumentNullException(nameof(sections));
            }

            string[] tags = new string[sections.Count];
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i] == null || sections[i].Length != 4)
                {
                    throw new ArgumentException("Section tags must be four ASCII characters.", nameof(sections));
                }

                tags[i] = sections[i];
            }

            Array.Sort(tags, StringComparer.Ordinal);

            XxHash64Builder hash = new XxHash64Builder();
            hash.AddBytes(map.TerrainPayloadSha256());
            for (int i = 0; i < tags.Length; i++)
            {
                if (i > 0 && tags[i] == tags[i - 1])
                {
                    continue;
                }

                WrmbDirEntry entry = map.RequireSection(tags[i]);
                byte[] tagBytes = Encoding.ASCII.GetBytes(tags[i]);
                hash.AddBytes(tagBytes);
                hash.AddUInt((uint)entry.Count);
                hash.AddBytes(map.SectionBytes(entry));
            }

            return hash.Digest();
        }
    }
}

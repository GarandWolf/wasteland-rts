using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Hashing;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// xxHash64 of every file under a balance directory (recursive). Files are
    /// visited in ordinal relative-path order; each file mixes that path (UTF-8,
    /// forward slashes) then its raw bytes. Any balance change changes the key.
    /// </summary>
    public static class BalanceKey
    {
        public const string DefaultRelativeDirectory = "data/balance";

        public static ulong For(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory);
            }

            string root = Path.GetFullPath(directory);
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            string[] relative = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                string rel = files[i].Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                relative[i] = rel.Replace('\\', '/');
            }

            int[] order = new int[files.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, delegate (int a, int b)
            {
                return string.CompareOrdinal(relative[a], relative[b]);
            });

            XxHash64Builder hash = new XxHash64Builder();
            for (int i = 0; i < order.Length; i++)
            {
                int idx = order[i];
                byte[] pathBytes = Encoding.UTF8.GetBytes(relative[idx]);
                byte[] data = File.ReadAllBytes(files[idx]);
                hash.AddInt(pathBytes.Length);
                hash.AddBytes(pathBytes);
                hash.AddInt(data.Length);
                hash.AddBytes(data);
            }

            return hash.Digest();
        }
    }
}

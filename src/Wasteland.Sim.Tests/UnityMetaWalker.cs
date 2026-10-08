using System;
using System.Collections.Generic;
using System.IO;

namespace Wasteland.Sim.Tests
{
    internal readonly struct UnityMetaWalkResult
    {
        public readonly string[] Missing;
        public readonly string[] Orphaned;

        public UnityMetaWalkResult(string[] missing, string[] orphaned)
        {
            Missing = missing;
            Orphaned = orphaned;
        }
    }

    /// <summary>
    /// Walks a Unity-imported root and reports files/folders that lack a
    /// sibling <c>.meta</c>, or <c>.meta</c> files whose source is gone.
    /// Skips the same names the Unity importer skips, and does not recurse
    /// into skipped folders.
    /// </summary>
    internal static class UnityMetaWalker
    {
        public static UnityMetaWalkResult Walk(string root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            List<string> missing = new List<string>();
            List<string> orphaned = new List<string>();
            Collect(root, root, missing, orphaned);
            return new UnityMetaWalkResult(ToSortedArray(missing), ToSortedArray(orphaned));
        }

        internal static bool IsSkipped(string name, bool isFolder)
        {
            if (name == null || name.Length < 1)
            {
                return false;
            }

            if (name[0] == '.')
            {
                return true;
            }

            if (name[name.Length - 1] == '~')
            {
                return true;
            }

            if (isFolder)
            {
                if (string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);
            }

            return EndsWithOrdinal(name, ".tmp");
        }

        private static void Collect(
            string root,
            string current,
            List<string> missing,
            List<string> orphaned)
        {
            string[] files = Directory.GetFiles(current);
            SortOrdinal(files);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);
                if (IsSkipped(name, false))
                {
                    continue;
                }

                if (EndsWithOrdinal(name, ".meta"))
                {
                    string source = path.Substring(0, path.Length - 5);
                    string sourceName = Path.GetFileName(source);
                    if (IsSkipped(sourceName, false) || IsSkipped(sourceName, true))
                    {
                        continue;
                    }

                    if (!File.Exists(source) && !Directory.Exists(source))
                    {
                        orphaned.Add(Rel(root, path));
                    }

                    continue;
                }

                if (!File.Exists(path + ".meta"))
                {
                    missing.Add(Rel(root, path));
                }
            }

            string[] dirs = Directory.GetDirectories(current);
            SortOrdinal(dirs);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (IsSkipped(name, true))
                {
                    continue;
                }

                if (!File.Exists(dir + ".meta"))
                {
                    missing.Add(Rel(root, dir) + "/");
                }

                Collect(root, dir, missing, orphaned);
            }
        }

        private static bool EndsWithOrdinal(string value, string suffix)
        {
            if (value.Length < suffix.Length)
            {
                return false;
            }

            return string.CompareOrdinal(
                value,
                value.Length - suffix.Length,
                suffix,
                0,
                suffix.Length) == 0;
        }

        private static string Rel(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string[] ToSortedArray(List<string> items)
        {
            string[] arr = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                arr[i] = items[i];
            }

            SortOrdinal(arr);
            return arr;
        }

        private static void SortOrdinal(string[] paths)
        {
            for (int i = 1; i < paths.Length; i++)
            {
                string row = paths[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(paths[j], row) > 0)
                {
                    paths[j + 1] = paths[j];
                    j--;
                }

                paths[j + 1] = row;
            }
        }
    }
}

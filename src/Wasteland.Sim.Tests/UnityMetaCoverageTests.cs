using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class UnityMetaCoverageTests
    {
        [Fact]
        public void PackageAndAssets_HaveMatchingMetas()
        {
            string repo = GoldenMapFiles.FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repo, "Assets"),
                Path.Combine(repo, "Packages", "com.wasteland.sim")
            };

            List<string> missing = new List<string>();
            List<string> orphaned = new List<string>();
            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                Assert.True(Directory.Exists(root), root);
                Collect(root, root, missing, orphaned);
            }

            missing.Sort();
            orphaned.Sort();
            Assert.True(
                missing.Count == 0 && orphaned.Count == 0,
                Format(missing, orphaned));
        }

        private static void Collect(
            string root,
            string current,
            List<string> missing,
            List<string> orphaned)
        {
            string[] files = Directory.GetFiles(current);
            SortPaths(files);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                if (path.EndsWith(".meta"))
                {
                    string source = path.Substring(0, path.Length - 5);
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
            SortPaths(dirs);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                if (!File.Exists(dir + ".meta"))
                {
                    missing.Add(Rel(root, dir) + "/");
                }

                Collect(root, dir, missing, orphaned);
            }
        }

        private static string Rel(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void SortPaths(string[] paths)
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

        private static string Format(List<string> missing, List<string> orphaned)
        {
            return "missing .meta (" + missing.Count + "): " + string.Join(", ", missing)
                + "; orphaned .meta (" + orphaned.Count + "): " + string.Join(", ", orphaned);
        }
    }
}

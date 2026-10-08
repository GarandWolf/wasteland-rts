using System;
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

            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                Assert.True(Directory.Exists(root), root);
                UnityMetaWalkResult result = UnityMetaWalker.Walk(root);
                Assert.True(
                    result.Missing.Length == 0 && result.Orphaned.Length == 0,
                    Format(result));
            }
        }

        [Fact]
        public void Walk_SkipsUnityIgnoredNames_AndReportsRealGaps()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "wasteland-unity-meta-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, ".secret"), "hidden-file");
                Directory.CreateDirectory(Path.Combine(root, ".hidden"));
                File.WriteAllText(Path.Combine(root, ".hidden", "inside.cs"), "hidden-folder");

                Directory.CreateDirectory(Path.Combine(root, "Foo~"));
                File.WriteAllText(Path.Combine(root, "Foo~", "x.cs"), "tilde-folder");

                Directory.CreateDirectory(Path.Combine(root, "bin", "Debug"));
                File.WriteAllText(Path.Combine(root, "bin", "Debug", "out.dll"), "bin-output");
                Directory.CreateDirectory(Path.Combine(root, "obj"));
                File.WriteAllText(Path.Combine(root, "obj", "x.cs"), "obj-output");
                Directory.CreateDirectory(Path.Combine(root, "CVS"));
                File.WriteAllText(Path.Combine(root, "CVS", "Entries"), "cvs-folder");

                File.WriteAllText(Path.Combine(root, "scratch.tmp"), "tmp-file");
                File.WriteAllText(Path.Combine(root, "Real.cs"), "needs-meta");
                File.WriteAllText(Path.Combine(root, "Gone.cs.meta"), "orphan-meta");

                File.WriteAllText(Path.Combine(root, ".ghost.meta"), "skipped-hidden-meta");
                File.WriteAllText(Path.Combine(root, "Missing~.meta"), "skipped-tilde-meta");
                File.WriteAllText(Path.Combine(root, "gone.tmp.meta"), "skipped-tmp-meta");
                File.WriteAllText(Path.Combine(root, "cvs.meta"), "skipped-cvs-meta");
                File.WriteAllText(Path.Combine(root, "bin.meta"), "skipped-bin-meta");

                UnityMetaWalkResult result = UnityMetaWalker.Walk(root);
                Assert.Equal(new[] { "Real.cs" }, result.Missing);
                Assert.Equal(new[] { "Gone.cs.meta" }, result.Orphaned);
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

        private static string Format(UnityMetaWalkResult result)
        {
            return "missing .meta (" + result.Missing.Length + "): "
                + string.Join(", ", result.Missing)
                + "; orphaned .meta (" + result.Orphaned.Length + "): "
                + string.Join(", ", result.Orphaned);
        }
    }
}

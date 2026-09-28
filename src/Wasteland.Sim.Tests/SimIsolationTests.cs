using System.IO;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class SimIsolationTests
    {
        [Fact]
        public void SimSources_DoNotReferencePresentationOrVat()
        {
            string root = RepoRoot();
            string simDir = Path.Combine(root, "Packages", "com.wasteland.sim");
            Assert.True(Directory.Exists(simDir), "sim package missing at " + simDir);

            string[] forbidden =
            {
                "UnityEngine",
                "TeamColorIndex",
                "VatClipState",
                "VatStartTime",
                "_TeamIndex",
                "_AnimIndex",
                "_AnimTime",
                "_VATTime",
                "Wasteland.Presentation",
                "Wasteland.Prototypes"
            };

            foreach (string file in Directory.GetFiles(simDir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string token in forbidden)
                {
                    Assert.DoesNotContain(token, text);
                }
            }
        }

        [Fact]
        public void PresentationAsmdef_DoesNotReferencePrototypes()
        {
            string root = RepoRoot();
            string presentation = File.ReadAllText(Path.Combine(root, "Assets", "_Project", "Presentation", "Wasteland.Presentation.asmdef"));
            Assert.DoesNotContain("Wasteland.Prototypes", presentation);

            string sim = File.ReadAllText(Path.Combine(root, "Packages", "com.wasteland.sim", "Runtime", "Wasteland.Sim.asmdef"));
            Assert.DoesNotContain("Wasteland.Prototypes", sim);
            Assert.DoesNotContain("Wasteland.Presentation", sim);
        }

        [Fact]
        public void PrototypesAsmdef_IsNotAutoReferenced()
        {
            string root = RepoRoot();
            string json = File.ReadAllText(Path.Combine(root, "Assets", "_Project", "Prototypes", "Wasteland.Prototypes.asmdef"));
            Assert.Contains("\"autoReferenced\": false", json);
        }

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Wasteland.Sim.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Wasteland.Sim.sln from " + Directory.GetCurrentDirectory());
        }
    }
}

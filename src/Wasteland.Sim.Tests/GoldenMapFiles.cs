using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Tests
{
    internal static class GoldenMapFiles
    {
        public static string FindDirectory(string fileName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "data", "golden");
                if (File.Exists(Path.Combine(candidate, fileName)))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), "data", "golden");
            if (File.Exists(Path.Combine(cwd, fileName)))
            {
                return cwd;
            }

            throw new InvalidOperationException(
                "Could not find data/golden/" + fileName + " by walking up from the test output directory.");
        }

        public static string FindRepoRoot()
        {
            string golden = FindDirectory(GoldenMapTests.MapFileName);
            DirectoryInfo? data = Directory.GetParent(golden);
            if (data == null || data.Name != "data")
            {
                throw new InvalidOperationException("Could not resolve repo root from " + golden + ".");
            }

            DirectoryInfo? root = data.Parent;
            if (root == null)
            {
                throw new InvalidOperationException("Could not resolve repo root from " + golden + ".");
            }

            return root.FullName;
        }

        public static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
        }

        public static byte[] ReadWrmb(string path, int expectedLength)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing WRMB golden (run git lfs pull --include=\"data/golden/*.wrmb\").", path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != expectedLength || IsLfsPointer(bytes) || !HasWrmbMagic(bytes))
            {
                throw new InvalidDataException(
                    path + " is not a materialized WRMB (" + bytes.Length
                    + " bytes). Run git lfs pull --include=\"data/golden/*.wrmb\".");
            }

            return bytes;
        }

        static bool HasWrmbMagic(byte[] bytes)
        {
            return bytes.Length >= 4
                && bytes[0] == (byte)'W'
                && bytes[1] == (byte)'R'
                && bytes[2] == (byte)'M'
                && bytes[3] == (byte)'B';
        }

        static bool IsLfsPointer(byte[] bytes)
        {
            const string prefix = "version https://git-lfs.github.com/spec/v1";
            if (bytes.Length < prefix.Length || bytes.Length >= 1024)
            {
                return false;
            }

            string head = Encoding.ASCII.GetString(bytes, 0, prefix.Length);
            return head == prefix;
        }
    }
}

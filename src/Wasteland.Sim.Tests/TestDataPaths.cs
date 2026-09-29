using System;
using System.IO;

namespace Wasteland.Sim.Tests
{
    internal static class TestDataPaths
    {
        public static string FindRepoFile(string relativePath)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(cwd))
            {
                return cwd;
            }

            throw new InvalidOperationException("Could not find " + relativePath + " by walking up from the test output directory.");
        }

        public static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(FindRepoFile(relativePath));
        }

        public static byte[] ReadRepoBytes(string relativePath)
        {
            return File.ReadAllBytes(FindRepoFile(relativePath));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsGSM.Functions
{
    public static class ProcessEnvironment
    {
        public static string PrependPathDirectories(string rootDirectory, string existingPath, params string[] relativeDirectories)
        {
            var paths = new List<string>();
            foreach (string relativeDirectory in relativeDirectories)
            {
                string directory = Path.Combine(rootDirectory, relativeDirectory);
                if (Directory.Exists(directory))
                {
                    paths.Add(directory);
                }
            }

            if (!string.IsNullOrWhiteSpace(existingPath))
            {
                paths.Add(existingPath);
            }

            return string.Join(Path.PathSeparator.ToString(), paths);
        }
    }
}

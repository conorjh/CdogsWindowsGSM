using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WindowsGSM.Functions
{
    public sealed class ServerGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> ServerIds { get; set; } = new List<string>();
    }

    public sealed class ServerGroupStore
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<ServerGroup> Groups { get; set; } = new List<ServerGroup>();

        public static ServerGroupStore Load(string path)
        {
            if (!File.Exists(path)) { return new ServerGroupStore(); }

            ServerGroupStore store = JsonConvert.DeserializeObject<ServerGroupStore>(File.ReadAllText(path));
            if (store == null || store.Groups == null)
            {
                throw new InvalidDataException("The server group file contains no group collection.");
            }
            if (store.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported server group schema version: {store.SchemaVersion}.");
            }

            var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerGroup group in store.Groups)
            {
                if (group == null || !Guid.TryParseExact(group.Id, "N", out _) ||
                    string.IsNullOrWhiteSpace(group.Name) || group.Name.Trim().Length > 64 ||
                    group.ServerIds == null || !groupIds.Add(group.Id) || !groupNames.Add(group.Name.Trim()))
                {
                    throw new InvalidDataException("The server group file contains an invalid or duplicate group.");
                }
                if (group.ServerIds.Any(string.IsNullOrWhiteSpace))
                {
                    throw new InvalidDataException($"Server group '{group.Name}' contains an invalid server ID.");
                }

                group.Name = group.Name.Trim();
                group.ServerIds = group.ServerIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            return store;
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A directory is required for the server group file.", nameof(path));
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}

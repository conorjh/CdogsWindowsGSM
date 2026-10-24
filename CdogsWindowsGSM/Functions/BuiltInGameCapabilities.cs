using System;
using System.Collections.Generic;
using System.IO;

namespace WindowsGSM.Functions
{
    internal static class BuiltInGameCapabilities
    {
        private static readonly HashSet<string> SourceGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GameServer.CSGO.FullName,
            GameServer.CSS.FullName,
            GameServer.DODS.FullName,
            GameServer.GMOD.FullName,
            GameServer.HL2DM.FullName,
            GameServer.INS.FullName,
            GameServer.L4D2.FullName,
            GameServer.NMRIH.FullName,
            GameServer.TF2.FullName,
            GameServer.ZPS.FullName
        };

        private static readonly HashSet<string> GoldSourceGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GameServer.CS.FullName,
            GameServer.CSCZ.FullName,
            GameServer.DOD.FullName,
            GameServer.DMC.FullName,
            GameServer.HLOF.FullName,
            GameServer.RCC.FullName,
            GameServer.TFC.FullName
        };

        private static readonly HashSet<string> SharedInstallationGames = CreateSharedInstallationGames();

        private static HashSet<string> CreateSharedInstallationGames()
        {
            var games = new HashSet<string>(SourceGames, StringComparer.OrdinalIgnoreCase);
            games.UnionWith(GoldSourceGames);
            games.Add(GameServer.COD1.FullName);
            games.Add(GameServer.MCBE.FullName);
            return games;
        }

        public static bool SupportsSharedInstallation(string gameKey)
        {
            return SharedInstallationGames.Contains(gameKey ?? string.Empty);
        }

        public static bool IsGoldSourceGame(string gameKey)
        {
            return GoldSourceGames.Contains(gameKey ?? string.Empty);
        }

        public static bool SupportsSharedInstallation(string gameKey, object gameServerObject)
        {
            if (!SupportsSharedInstallation(gameKey) ||
                gameServerObject == null ||
                gameServerObject.GetType().Assembly != typeof(GameServer.RUST).Assembly ||
                !(gameServerObject is GameServer.Engine.ISharedInstallationProvider))
            {
                return false;
            }

            string normalizedGameKey = gameKey ?? string.Empty;
            if (GoldSourceGames.Contains(normalizedGameKey) &&
                gameServerObject is GameServer.Engine.GoldSource goldSource)
            {
                return !string.IsNullOrWhiteSpace(goldSource.Game);
            }

            if (SourceGames.Contains(normalizedGameKey) &&
                gameServerObject is GameServer.Engine.Source source)
            {
                return !string.IsNullOrWhiteSpace(source.Game) &&
                    !string.IsNullOrWhiteSpace(source.AppId);
            }

            if (string.Equals(normalizedGameKey, GameServer.COD1.FullName, StringComparison.OrdinalIgnoreCase) &&
                gameServerObject is GameServer.COD1)
            {
                return true;
            }

            if (string.Equals(normalizedGameKey, GameServer.MCBE.FullName, StringComparison.OrdinalIgnoreCase) &&
                gameServerObject is GameServer.MCBE)
            {
                return true;
            }

            return false;
        }

        public static bool SupportsConfigurationBackup(string gameKey)
        {
            return string.Equals(gameKey, GameServer.RUST.FullName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameKey, GameServer.COD1.FullName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameKey, GameServer.MCBE.FullName, StringComparison.OrdinalIgnoreCase) ||
                IsGoldSourceGame(gameKey) ||
                SourceGames.Contains(gameKey ?? string.Empty);
        }

        public static List<ConfigurationBackupPath> GetConfigurationBackupPlan(
            ServerConfig serverConfig,
            IDictionary<string, string> roots)
        {
            if (serverConfig == null)
            {
                throw new ArgumentNullException(nameof(serverConfig));
            }
            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }
            if (!roots.ContainsKey("serverfiles"))
            {
                throw new ArgumentException("The per-instance serverfiles root is required.", nameof(roots));
            }

            string gameKey = serverConfig.ServerGame ?? string.Empty;
            if (string.Equals(gameKey, GameServer.RUST.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return new List<ConfigurationBackupPath>
                {
                    new ConfigurationBackupPath("serverfiles", "server.cfg")
                };
            }

            if (string.Equals(gameKey, GameServer.MCBE.FullName, StringComparison.OrdinalIgnoreCase))
            {
                var paths = new List<ConfigurationBackupPath>
                {
                    new ConfigurationBackupPath("serverfiles", "server.properties")
                };
                foreach (string fileName in GameServer.MCBE.BedrockInstanceConfigurationFiles)
                {
                    paths.Add(new ConfigurationBackupPath("serverfiles", fileName));
                }

                return paths;
            }

            if (IsGoldSourceGame(gameKey))
            {
                GameServer.Engine.GoldSource goldSource = GameServer.Data.Class.Get(gameKey, serverConfig) as GameServer.Engine.GoldSource;
                if (goldSource == null || string.IsNullOrWhiteSpace(goldSource.Game))
                {
                    throw new InvalidDataException($"The GoldSrc configuration paths for '{gameKey}' could not be resolved.");
                }

                string mapCycleRootId = "serverfiles";
                string mapCycleRelativePath = Path.Combine(goldSource.Game, "mapcycle.txt");
                if (!string.IsNullOrWhiteSpace(serverConfig.SharedInstallationId))
                {
                    string expectedSharedRoot = Path.GetFullPath(SharedInstallationStore.GetServerBinaryRoot(serverConfig.SharedInstallationId));
                    if (!roots.TryGetValue("sharedserverfiles", out string sharedServerFilesRoot) ||
                        !string.Equals(Path.GetFullPath(sharedServerFilesRoot), expectedSharedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The matching shared-install serverfiles root is required for GoldSrc configuration backups.");
                    }

                    mapCycleRootId = "sharedserverfiles";
                    mapCycleRelativePath = Path.Combine(
                        goldSource.Game,
                        GameServer.Engine.GoldSource.GetSharedMapCycleFileName(serverConfig.ServerID));
                }

                return new List<ConfigurationBackupPath>
                {
                    new ConfigurationBackupPath("serverfiles", Path.Combine(goldSource.Game, "server.cfg")),
                    new ConfigurationBackupPath(mapCycleRootId, mapCycleRelativePath)
                };
            }

            if (SourceGames.Contains(gameKey))
            {
                GameServer.Engine.Source source = GameServer.Data.Class.Get(gameKey, serverConfig) as GameServer.Engine.Source;
                if (source == null || string.IsNullOrWhiteSpace(source.Game))
                {
                    throw new InvalidDataException($"The Source configuration paths for '{gameKey}' could not be resolved.");
                }

                return new List<ConfigurationBackupPath>
                {
                    new ConfigurationBackupPath("serverfiles", Path.Combine(source.Game, "cfg", "server.cfg")),
                    new ConfigurationBackupPath("serverfiles", Path.Combine(source.Game, "mapcycle.txt"))
                };
            }

            if (string.Equals(gameKey, GameServer.COD1.FullName, StringComparison.OrdinalIgnoreCase))
            {
                var paths = new List<ConfigurationBackupPath>
                {
                    new ConfigurationBackupPath("serverfiles", Path.Combine("main", "server.cfg"))
                };

                if (serverConfig.Cod1MeatBots)
                {
                    paths.Add(new ConfigurationBackupPath(
                        "serverfiles",
                        Path.Combine(GameServer.COD1.ModDirectoryName, "server.cfg")));
                }

                return paths;
            }

            throw new InvalidOperationException($"Configuration-only backups are not enabled for '{gameKey}'.");
        }
    }
}

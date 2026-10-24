using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WindowsGSM.Functions;

namespace WindowsGSM.Plugins
{
    public class SkeletonV2
    {
        public Plugin Plugin = new Plugin
        {
            name = "",
            author = "",
            description = "",
            version = "",
            url = "",
            color = "#ffffff"
        };

        public SkeletonV2(ServerConfig serverData)
        {
            _serverData = serverData;
        }

        private readonly ServerConfig _serverData;
        public string Error, Notice;
        public string StartPath = "";
        public string FullName = "";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 1;
        public object QueryMethod = null;
        public string Port = "";
        public string QueryPort = "";
        public string Defaultmap = "";
        public string Maxplayers = "";
        public string Additional = "";

        public async void CreateServerCFG()
        {
        }

        public async Task<Process> Start()
        {
            return null;
        }

        public async Task Stop(Process process)
        {
        }

        public async Task<Process> Install()
        {
            return null;
        }

        public async Task<Process> Update()
        {
            return null;
        }

        public bool IsInstallValid()
        {
            return false;
        }

        public bool IsImportValid(string path)
        {
            return false;
        }

        public string GetLocalBuild()
        {
            return "";
        }

        public async Task<string> GetRemoteBuild()
        {
            return "";
        }

        public PluginBackupPlan GetConfigOnlyBackupPlan(PluginInstanceContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return new PluginBackupPlan(new[]
            {
                new ConfigurationBackupPath("serverfiles", "server.cfg")
            });
        }
    }
}
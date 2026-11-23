namespace WindowsGSM.GameServer
{
    class INS : Engine.Source
    {
        public const string FullName = "Insurgency Dedicated Server";
        public override string Defaultmap { get { return "market skirmish"; } }
        public override bool UseConsoleArgument { get { return false; } }
        public override string Additional { get { return base.Additional + " -usercon"; } }
        public override string Game { get { return "insurgency"; } }
        public override string AppId { get { return "237410"; } }

        public INS(Functions.ServerConfig serverData) : base(serverData)
        {
            base.serverData = serverData;
        }
    }
}

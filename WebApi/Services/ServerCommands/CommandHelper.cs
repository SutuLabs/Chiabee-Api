namespace WebApi.Services.ServerCommands
{
    using Renci.SshNet;
    using WebApi.Models;

    public static class CommandHelper
    {
        internal static void EnsureConnected(this TargetMachine client)
        {
            if (!client.IsConnected)
            {
                client.Connect();
            }
        }
    }
}
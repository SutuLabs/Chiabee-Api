namespace WebApi.Services.ServerCommands
{
    using Renci.SshNet;

    public static class CommandHelper
    {
        internal static void EnsureConnected(this SshClient client)
        {
            if (!client.IsConnected)
            {
                client.Connect();
            }
        }
    }
}
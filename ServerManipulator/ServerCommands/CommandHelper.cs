namespace WebApi.Services.ServerCommands
{
    using System;
    using Renci.SshNet.Common;
    using WebApi.Models;

    public static class CommandHelper
    {
        internal static bool EnsureConnected(this TargetMachine client)
        {
            try
            {
                if (!client.IsConnected)
                {
                    client.Connect();
                }

                return true;
            }
            catch (SshOperationTimeoutException)
            {
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
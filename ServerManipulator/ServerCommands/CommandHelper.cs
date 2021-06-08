namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Sockets;
    using Renci.SshNet.Common;
    using WebApi.Models;

    public static class CommandHelper
    {
        public static bool EnsureConnected(this TargetMachine client)
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
            catch (SocketException)// remote cannot connect
            {
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static T TryGet<T>(Func<T> func)
        {
            try
            {
                return func();
            }
            catch (SshOperationTimeoutException)
            {
                return default;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public static string[] CleanSplit(this string str, string separator = "\n") => str
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(_ => _.Trim())
            .ToArray();

        public static IEnumerable<StatusPair> ParsePairs(string output, params StatusDefinition[] defs)
        {
            var lines = output.CleanSplit();
            foreach (var line in lines)
            {
                var pos = line.IndexOf(":");
                if (pos > -1)
                {
                    var key = line[..pos].Trim();
                    var def = defs.FirstOrDefault(_ => _.Text == key);
                    if (def != null)
                        yield return new StatusPair(def.Key, line[(pos + 1)..]);
                }
            }
        }
    }

    public record StatusPair(string Key, string Value);
    public record StatusDefinition(string Key, string Text);
}
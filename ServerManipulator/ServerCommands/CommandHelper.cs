namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using Microsoft.Extensions.Logging;
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

        public static T TryGet<T>(Func<T> func, ILogger logger = null, [CallerMemberName] string callerName = "")
        {
            try
            {
                return func();
            }
            catch (SshOperationTimeoutException)
            {
                return default;
            }
            catch (SshException sex) when (sex.Message == "Message type 52 is not valid in the current context.")
            {
                return default;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"error when try get {callerName}");
                return default;
            }
        }

        public static string[] CleanSplit(this string str, string separator = "\n", StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries) => str
            .Split(separator, options)
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
                        yield return new StatusPair(def.Key, line[(pos + 1)..].Trim());
                }
            }
        }

#nullable enable
        public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> enumerable) where T : class
        {
            return enumerable.Where(e => e != null).Select(e => e!);
        }
    }

    public record StatusPair(string Key, string Value);
    public record StatusDefinition(string Key, string Text);
}
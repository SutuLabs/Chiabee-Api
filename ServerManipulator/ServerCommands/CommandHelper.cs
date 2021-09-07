namespace WebApi.Services.ServerCommands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Microsoft.Extensions.Logging;
    using Renci.SshNet;
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

        public static SshCommand ExecuteCommand(this TargetMachine client, string commandText, TimeSpan? timeout = null)
        {
            try
            {
                using var sshcmd = client.CreateCommand(commandText);
                sshcmd.CommandTimeout = timeout ?? new TimeSpan(0, 1, 0);
                var extendTimeout = sshcmd.CommandTimeout.Add(new TimeSpan(0, 0, 5));
                using (sshcmd.CreateTimeoutScope(extendTimeout))
                {
                    try
                    {
                        sshcmd.Execute();
                    }
                    catch (NullReferenceException nex) when (nex.TargetSite?.Name == "SendMessage")
                    {
                        client.Logger.LogWarning($"failed to execute due to NullReference in SendMessage(due to null Session), reconnect and execute again.");
                        client.Disconnect();
                        client.Connect();
                        sshcmd.Execute();
                    }

                    return sshcmd;
                }
            }
            catch (ObjectDisposedException)
            {
                client.Logger.LogWarning($"timeout executing command[{timeout}]: {commandText}");
                throw new TimeoutException();
            }
        }

        public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> enumerable) where T : class
        {
            return enumerable.Where(e => e != null).Select(e => e!);
        }

        public static void SetSshNetConcurrency(int concurrency)
        {
            var field = typeof(Renci.SshNet.Session).GetField("AuthenticationConnection",
                         BindingFlags.Static |
                         BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, new SemaphoreLight(concurrency));
            }
        }

        public static IDisposable CreateTimeoutScope(this IDisposable disposable, TimeSpan timeSpan)
        {
            var cancellationTokenSource = new CancellationTokenSource(timeSpan);
            var cancellationTokenRegistration = cancellationTokenSource.Token.Register(disposable.Dispose);
            return new DisposableScope(
                () =>
                {
                    cancellationTokenRegistration.Dispose();
                    cancellationTokenSource.Dispose();
                    disposable.Dispose();
                });
        }
    }
    public sealed class DisposableScope : IDisposable
    {
        private readonly Action _closeScopeAction;
        public DisposableScope(Action closeScopeAction)
        {
            _closeScopeAction = closeScopeAction;
        }
        public void Dispose()
        {
            _closeScopeAction();
        }
    }

    public record StatusPair(string Key, string Value);
    public record StatusDefinition(string Key, string Text);
}
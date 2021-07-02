namespace WebApi.Services.ServerCommands
{
    using System.IO;
    using System.Text;
    using Microsoft.Extensions.Logging;
    using Renci.SshNet;
    using WebApi.Models;

    public static class TmuxCommand
    {
        public static bool StartPlotterDaemon(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;
            var ses = "mon";
            var script = @$"tmux kill-session -t {ses}
sleep 0.5
tmux new-session -d -s {ses}
tmux send-keys 'chia' 'C-m'
# tmux send-keys 'chia && pmi' 'C-m'
tmux select-window -t {ses}:1
tmux rename-window 'Monitor'
tmux split-window -v
tmux send-keys 'nmon <<< Cdn.' 'C-m'
tmux split-window -h
tmux send-keys 'watch -n 2 ""df -h | grep /data""' 'C-m'
tmux split-window -v
# tmux send-keys 'chia && pm plot' 'C-m'
tmux split-window -h
# tmux send-keys 'chia && pm archive' 'C-m'
tmux select-pane -t 0";
            var (exit, _) = client.ExecuteScript(script);
            if (exit <= 1) return true;
            return false;
        }

        public static bool StartHarvesterDaemon(this TargetMachine client)
        {
            if (!client.EnsureConnected()) return default;
            var ses = "mon";
            var log = "~/.chia/mainnet/log/debug.log";
            var script = @$"tmux kill-session -t {ses}
sleep 0.5
tmux new-session -d -s {ses}
tmux select-window -t {ses}:1
tmux rename-window 'Monitor'
tmux send-keys 'tail -n 10000 -F {log} | grep WARNING' 'C-m'
tmux split-window -h
tmux send-keys 'nmon <<< dn.' 'C-m'
tmux split-window -v
tmux send-keys 'watch -n 5 ""df -h | grep /farm""' 'C-m'
tmux split-window -v
tmux send-keys 'chia' 'C-m'
tmux send-keys 'chia start harvester' 'C-m'
tmux split-window -v -t 0
tmux send-keys 'tail -n 10 -F {log}' 'C-m'
tmux split-window -v -t 0
tmux send-keys 'watch -n 5 ""tail -n 10000 {log} | grep -E -o \""Looking up qualities on /farm/.*\/\"" | sort -u""' 'C-m'
tmux split-window -h
tmux send-keys 'chia' 'C-m'
tmux send-keys 'watch -n 5 ""chia plots show | grep /farm |sort""' 'C-m'";
            var (exit, _) = client.ExecuteScript(script);
            if (exit <= 1) return true;
            return false;
        }

        public static (int exitStatus, string result) ExecuteScript(this TargetMachine client, string script, bool sudo = false)
        {
            var tempfile = "tempremoteexecution.sh";
            if (!client.EnsureConnected()) return default;
            client.Logger.LogInformation($"Upload and executing script: {script}");
            var cmds = "#!/bin/bash\n\n" + script;
            cmds = cmds.Replace("\r", "");
            using var ms = new MemoryStream(Encoding.ASCII.GetBytes(cmds));

            using var scp = new ScpClient(client.ConnectionInfo);
            scp.Connect();
            scp.Upload(ms, $"/home/{client.ConnectionInfo.Username}/{tempfile}");
            scp.Disconnect();
            var pass = "sutu";
            cmds = $"echo {pass} | sudo -S sudo chmod +x {tempfile};" +
                (sudo ? $"echo {pass} | sudo -S bash ./{tempfile};" : $"./{tempfile};") +
                $"rm {tempfile};";
            using var cmd = client.RunCommand(cmds);
            client.Logger.LogInformation($"Result[{cmd.ExitStatus}]: {cmd.Result}");
            return (cmd.ExitStatus, cmd.Result);
        }
    }
}
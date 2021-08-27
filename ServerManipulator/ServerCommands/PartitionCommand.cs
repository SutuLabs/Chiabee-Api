namespace WebApi.Services.ServerCommands
{
    using System.IO;
    using System.Text;
    using Renci.SshNet;
    using WebApi.Models;

    public static class PartitionCommand
    {
        public static bool CreatePartition(this TargetMachine m, string dname, string label)
        {
            var cmds = @$"
plabel={label}
disk=/dev/{dname}
echo Working on $disk ...

sudo parted $disk mklabel gpt
sudo parted $disk mkpart primary ext4 0% 100%

echo Waiting {dname}1 to appear
while true ; do
    result=$(lsblk | grep {dname}1)
    if [ ! -z ""$result"" ] ; then
        echo Found {dname}1, continue
        break
    fi
    sleep 1
done" +
@"

sudo mkfs.ext4 ${disk}1
puuid=$(lsblk -no PARTUUID ${disk}1)
echo Got PARTUUID $puuid from ${disk}1, Applying label: $plabel

sudo e2label /dev/disk/by-partuuid/$puuid $plabel
sudo tune2fs -r 100000 /dev/disk/by-partuuid/$puuid
echo /dev/disk/by-partuuid/$puuid /farm/$plabel ext4 defaults 0 0 | sudo tee -a /etc/fstab
sudo mkdir -p /farm/$plabel
sudo mount -a
sudo chown sutu /farm/$plabel/
";
            m.PerformScript(cmds, true);
            using var cmd = m.ExecuteCommand($". ./chia-blockchain/activate && chia plots add -d /farm/{label}");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }

        public static bool RenamePartition(this TargetMachine m, string dname, string oldLabel, string newLabel)
        {
            var cmds = @$"
oldLabel={oldLabel}
newLabel={newLabel}
disk=/dev/{dname}
" +
@"
echo Working on $disk ...
puuid=$(lsblk -no PARTUUID ${disk}1)
echo Got PARTUUID $puuid from ${disk}1, Applying label: $oldLabel -\> $newLabel

if [ ! ""$oldLabel"" == """" ]; then
    sudo umount /farm/$oldLabel
    sudo mv /farm/$oldLabel /farm/legacy-$oldLabel
fi

sudo sed -i ""/\/dev\/disk\/by-partuuid\/$puuid/d"" /etc/fstab

sudo e2label /dev/disk/by-partuuid/$puuid $newLabel
echo /dev/disk/by-partuuid/$puuid /farm/$newLabel ext4 defaults 0 0 | sudo tee -a /etc/fstab
sudo mkdir -p /farm/$newLabel
sudo mount -a
sudo chown sutu /farm/$newLabel/
";
            var (d, g) = m.PerformScript(cmds, true);
            using var cmd = m.ExecuteCommand($". ./chia-blockchain/activate;"
                + (string.IsNullOrEmpty(oldLabel) ? "" : $"chia plots remove -d /farm/{oldLabel};")
                + $"chia plots add -d /farm/{newLabel};");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }

        public static bool MountPartition(this TargetMachine m, string dname, string label)
        {
            var cmds = @$"
plabel={label}
disk=/dev/{dname}
" +
@"
echo Mounting $disk ...

puuid=$(lsblk -no PARTUUID ${disk}1)
echo Got PARTUUID $puuid from ${disk}1, mounting: $plabel


if ! grep -q $puuid ""/etc/fstab""; then
    sudo sed -i ""/\/farm\/$plabel ext4/d"" /etc/fstab
    echo /dev/disk/by-partuuid/$puuid /farm/$plabel ext4 defaults 0 0 | sudo tee -a /etc/fstab
fi

sudo mkdir -p /farm/$plabel
sudo mount -a
sudo chown sutu /farm/$plabel/
";
            m.PerformScript(cmds, true);
            using var cmd = m.ExecuteCommand($". ./chia-blockchain/activate && chia plots add -d /farm/{label}");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }

        public static bool UnmountPartition(this TargetMachine m, string label)
        {
            var pass = "sutu";
            using var cmd = m.ExecuteCommand($"echo {pass} | sudo -S sudo umount -l /farm/{label};" +
                $". ./chia-blockchain/activate && chia plots remove -d /farm/{label}");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }

        public static bool RemovePlotDir(this TargetMachine m, string path)
        {
            var prefix = "/farm/";
            if (path.StartsWith(prefix))
            {
                var legacyPath = $"{path[..prefix.Length]}legacy{path[prefix.Length..]}";
                var cmds = @$"
sudo umount {path}
sudo mv {path} {legacyPath}

sudo sed -i ""/{path.Replace("/", "\\/")} ext4/d"" /etc/fstab
";
                m.PerformScript(cmds, true);
            }

            using var cmd = m.ExecuteCommand($". ./chia-blockchain/activate && chia plots remove -d {path}");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }

        public static bool RemoveNtfsPartition(this TargetMachine m, string dname)
        {
            var cmds = @$"
disk=/dev/{dname}
" +
@"
fstype=$(lsblk -no fstype ${disk}1)
if [ ""$fstype"" = ""ntfs"" ] ; then
    echo removing ${fstype} partition on ${disk} ...
    sudo parted ${disk} rm 1
else
    echo not removing ${fstype} partition on ${disk} ...
fi
";
            m.PerformScript(cmds, true);
            return true;
        }
    }
}
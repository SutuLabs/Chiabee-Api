﻿namespace WebApi.Services.ServerCommands
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
            m.ExecuteScript(cmds, true);
            using var cmd = m.RunCommand($"./chia-blockchain/activate && chia plots add -d /farm/{label}");
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
echo Got PARTUUID $puuid from ${disk}1, Applying label: $oldLabel -> $newLabel

sudo umount /farm/$oldLabel
sudo rm -r /farm/$oldLabel

sudo sed -i ""/\/dev\/disk\/by-partuuid\/$puuid/d"" /etc/fstab

sudo e2label /dev/disk/by-partuuid/$puuid $newLabel
echo /dev/disk/by-partuuid/$puuid /farm/$newLabel ext4 defaults 0 0 | sudo tee -a /etc/fstab
sudo mkdir -p /farm/$newLabel
sudo mount -a
sudo chown sutu /farm/$newLabel/
";
            var (d, g) = m.ExecuteScript(cmds, true);
            using var cmd = m.RunCommand($@". ./chia-blockchain/activate;
chia plots remove -d /farm/{oldLabel};
chia plots add -d /farm/{newLabel};");
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }
    }
}
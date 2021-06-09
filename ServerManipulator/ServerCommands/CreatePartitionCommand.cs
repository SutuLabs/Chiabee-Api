namespace WebApi.Services.ServerCommands
{
    using System.IO;
    using System.Text;
    using Renci.SshNet;
    using WebApi.Models;

    public static class CreatePartitionCommand
    {
        public static bool CreatePartition(this TargetMachine m, string block, string label)
        {
            var dname = block;
            var cmds = @$"#!/bin/bash
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
            cmds = cmds.Replace("\r", "");
            using var scp = new ScpClient(m.ConnectionInfo);
            scp.Connect();
            using var ms = new MemoryStream(Encoding.ASCII.GetBytes(cmds));
            scp.Upload(ms, "/home/sutu/temp.sh");
            scp.Disconnect();
            cmds = $@"echo sutu | sudo -S sudo chmod +x temp.sh;
echo sutu | sudo -S bash ./temp.sh;
. ./chia-blockchain/activate && chia plots add -d /farm/{label};
rm temp.sh;";
            using var cmd = m.RunCommand(cmds);
            var result = cmd.Result;
            if (cmd.ExitStatus <= 1) return true;
            return false;
        }
    }
}
using System.Text.Json.Serialization;

namespace SimpleMirrorBackup;

public sealed class RemoteDeviceSettings
{
    private int _wakePort = 9;
    private int _sshPort = 22;
    private int _startupDelaySeconds = 0;

    public string DisplayName { get; set; } = "NestDisk";
    public string MacAddress { get; set; } = string.Empty;
    public string BroadcastAddress { get; set; } = "255.255.255.255";

    public int WakePort
    {
        get => _wakePort;
        set => _wakePort = value is >= 1 and <= 65535 ? value : 9;
    }

    public int StartupDelaySeconds
    {
        get => _startupDelaySeconds;
        set => _startupDelaySeconds = value is >= 0 and <= 86400 ? value : 0;
    }

    public string SshHost { get; set; } = string.Empty;

    public int SshPort
    {
        get => _sshPort;
        set => _sshPort = value is >= 1 and <= 65535 ? value : 22;
    }

    public string SshUsername { get; set; } = "root";
    public string SshPassword { get; set; } = string.Empty;

    public string ShutdownCommand { get; set; } =
        "nohup sudo /sbin/shutdown -h now >/dev/null 2>&1 &";

    [JsonIgnore]
    public bool CanSendWakeOnLan => !string.IsNullOrWhiteSpace(MacAddress);

    [JsonIgnore]
    public bool CanUseSsh =>
        !string.IsNullOrWhiteSpace(SshHost) &&
        !string.IsNullOrWhiteSpace(SshUsername);

    public RemoteDeviceSettings Clone()
    {
        return new RemoteDeviceSettings
        {
            DisplayName = DisplayName,
            MacAddress = MacAddress,
            BroadcastAddress = BroadcastAddress,
            WakePort = WakePort,
            StartupDelaySeconds = StartupDelaySeconds,
            SshHost = SshHost,
            SshPort = SshPort,
            SshUsername = SshUsername,
            SshPassword = SshPassword,
            ShutdownCommand = ShutdownCommand
        };
    }
}
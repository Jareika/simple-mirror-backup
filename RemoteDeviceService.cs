using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SimpleMirrorBackup;

public static class RemoteDeviceService
{
    public static async Task SendWakeOnLanAsync(
        RemoteDeviceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.CanSendWakeOnLan)
            throw new InvalidOperationException("Wake-on-LAN ist nicht konfiguriert.");

        cancellationToken.ThrowIfCancellationRequested();

        var macBytes = ParseMacAddress(settings.MacAddress);
        var packet = BuildMagicPacket(macBytes);

        var broadcastAddress = string.IsNullOrWhiteSpace(settings.BroadcastAddress)
            ? "255.255.255.255"
            : settings.BroadcastAddress.Trim();

        var endpoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), settings.WakePort);

        using var client = new UdpClient();
        client.EnableBroadcast = true;

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendAsync(packet, packet.Length, endpoint);
    }

    public static Task<string> ExecuteSshCommandAsync(
        RemoteDeviceSettings settings,
        string command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.CanUseSsh)
            throw new InvalidOperationException("SSH ist nicht vollständig konfiguriert.");

        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("SSH-Befehl fehlt.");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var client = CreateSshClient(settings);
            client.Connect();

            using var sshCommand = client.CreateCommand(command);
            var stdout = sshCommand.Execute();
            var stderr = sshCommand.Error;

            var outputParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(stdout))
                outputParts.Add(stdout.TrimEnd());

            if (!string.IsNullOrWhiteSpace(stderr))
                outputParts.Add(stderr.TrimEnd());

            if (sshCommand.ExitStatus != 0)
            {
                var details = outputParts.Count == 0
                    ? string.Empty
                    : Environment.NewLine + string.Join(Environment.NewLine, outputParts);

                throw new InvalidOperationException(
                    $"SSH-Befehl fehlgeschlagen (Exit-Code {sshCommand.ExitStatus}).{details}");
            }

            return string.Join(Environment.NewLine, outputParts);
        }, cancellationToken);
    }

    public static SshClient CreateSshClient(RemoteDeviceSettings settings)
    {
        return new SshClient(CreateConnectionInfo(settings));
    }

    public static string NormalizeMacAddress(string macAddress)
    {
        var bytes = ParseMacAddress(macAddress);
        return string.Join(":", bytes.Select(x => x.ToString("X2")));
    }

    private static ConnectionInfo CreateConnectionInfo(RemoteDeviceSettings settings)
    {
        var host = settings.SshHost?.Trim() ?? string.Empty;
        var username = settings.SshUsername?.Trim() ?? string.Empty;
        var password = settings.SshPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("SSH-Host fehlt.");

        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("SSH-Benutzer fehlt.");

        var passwordAuth = new PasswordAuthenticationMethod(username, password);
        var keyboardAuth = new KeyboardInteractiveAuthenticationMethod(username);

        keyboardAuth.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
                prompt.Response = password;
        };

        var connectionInfo = new ConnectionInfo(
            host,
            settings.SshPort,
            username,
            passwordAuth,
            keyboardAuth);

        connectionInfo.Timeout = TimeSpan.FromSeconds(10);
        return connectionInfo;
    }

    private static byte[] ParseMacAddress(string macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
            throw new FormatException("MAC-Adresse fehlt.");

        var cleaned = new string(macAddress.Where(Uri.IsHexDigit).ToArray());

        if (cleaned.Length != 12)
            throw new FormatException("MAC-Adresse muss 12 Hex-Zeichen enthalten.");

        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch (Exception ex)
        {
            throw new FormatException("MAC-Adresse ist ungültig.", ex);
        }
    }

    private static byte[] BuildMagicPacket(byte[] macBytes)
    {
        var packet = new byte[6 + 16 * macBytes.Length];

        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;

        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(macBytes, 0, packet, 6 + i * macBytes.Length, macBytes.Length);

        return packet;
    }
}
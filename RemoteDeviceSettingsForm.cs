using System.Drawing;
using System.Net;

namespace SimpleMirrorBackup;

public sealed class RemoteDeviceSettingsForm : Form
{
    private sealed class LanguageComboItem
    {
        public string Code { get; init; } = "de";
        public string DisplayName { get; init; } = "Deutsch";
        public override string ToString() => DisplayName;
    }

    private readonly ComboBox cboLanguage = new();
    private readonly TextBox txtDisplayName = new();
    private readonly TextBox txtMacAddress = new();
    private readonly TextBox txtBroadcastAddress = new();
    private readonly NumericUpDown numWakePort = new();
    private readonly NumericUpDown numStartupDelaySeconds = new();
    private readonly TextBox txtSshHost = new();
    private readonly NumericUpDown numSshPort = new();
    private readonly TextBox txtSshUsername = new();
    private readonly TextBox txtSshPassword = new();
    private readonly TextBox txtShutdownCommand = new();

    public RemoteDeviceSettings ResultSettings { get; private set; }
    public string SelectedLanguageCode { get; private set; } = "de";

    public RemoteDeviceSettingsForm(
        RemoteDeviceSettings settings,
        IReadOnlyList<AppLanguageInfo> availableLanguages,
        string? selectedLanguageCode)
    {
        ResultSettings = settings.Clone();
        SelectedLanguageCode = string.IsNullOrWhiteSpace(selectedLanguageCode)
            ? AppLanguage.CurrentCode
            : selectedLanguageCode.Trim();

        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppLanguage.T("Settings.Title", "Einstellungen");
        ClientSize = new Size(780, 760);
        MinimumSize = new Size(780, 760);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        cboLanguage.Dock = DockStyle.Fill;

        var languageItems = (availableLanguages ?? Array.Empty<AppLanguageInfo>())
            .Select(x => new LanguageComboItem
            {
                Code = string.IsNullOrWhiteSpace(x.Code) ? "de" : x.Code.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.Code : x.DisplayName.Trim()
            })
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (languageItems.Count == 0)
        {
            languageItems.Add(new LanguageComboItem
            {
                Code = "de",
                DisplayName = "Deutsch"
            });
        }

        cboLanguage.Items.AddRange(languageItems.Cast<object>().ToArray());

        var selectedItem = languageItems.FirstOrDefault(x =>
                               string.Equals(x.Code, SelectedLanguageCode, StringComparison.OrdinalIgnoreCase))
                           ?? languageItems[0];

        cboLanguage.SelectedItem = selectedItem;

        txtDisplayName.Text = ResultSettings.DisplayName;
        txtMacAddress.Text = ResultSettings.MacAddress;
        txtBroadcastAddress.Text = string.IsNullOrWhiteSpace(ResultSettings.BroadcastAddress)
            ? "255.255.255.255"
            : ResultSettings.BroadcastAddress;

        numWakePort.Minimum = 1;
        numWakePort.Maximum = 65535;
        numWakePort.Value = ResultSettings.WakePort;

        numStartupDelaySeconds.Minimum = 0;
        numStartupDelaySeconds.Maximum = 86400;
        numStartupDelaySeconds.Value = ResultSettings.StartupDelaySeconds;

        txtSshHost.Text = ResultSettings.SshHost;

        numSshPort.Minimum = 1;
        numSshPort.Maximum = 65535;
        numSshPort.Value = ResultSettings.SshPort;

        txtSshUsername.Text = ResultSettings.SshUsername;
        txtSshPassword.Text = ResultSettings.SshPassword;
        txtSshPassword.UseSystemPasswordChar = true;

        txtShutdownCommand.Text = string.IsNullOrWhiteSpace(ResultSettings.ShutdownCommand)
            ? "nohup sudo /sbin/shutdown -h now >/dev/null 2>&1 &"
            : ResultSettings.ShutdownCommand;

        txtDisplayName.Dock = DockStyle.Fill;
        txtMacAddress.Dock = DockStyle.Fill;
        txtBroadcastAddress.Dock = DockStyle.Fill;
        numWakePort.Width = 120;
        numStartupDelaySeconds.Width = 120;
        txtSshHost.Dock = DockStyle.Fill;
        numSshPort.Width = 120;
        txtSshUsername.Dock = DockStyle.Fill;
        txtSshPassword.Dock = DockStyle.Fill;
        txtShutdownCommand.Dock = DockStyle.Fill;

        txtDisplayName.PlaceholderText = "NestDisk";
        txtMacAddress.PlaceholderText = "AA:BB:CC:DD:EE:FF";
        txtBroadcastAddress.PlaceholderText = "255.255.255.255";
        txtSshHost.PlaceholderText = "192.168.178.50 oder nestdisk.local";
        txtSshUsername.PlaceholderText = "root";
        txtShutdownCommand.PlaceholderText = "nohup sudo /sbin/shutdown -h now >/dev/null 2>&1 &";

        var btnOk = new Button
        {
            Text = AppLanguage.T("Common.Ok", "OK"),
            AutoSize = true,
            MinimumSize = new Size(96, 34)
        };

        var btnCancel = new Button
        {
            Text = AppLanguage.T("Common.Cancel", "Abbrechen"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(96, 34)
        };

        btnOk.Click += (_, _) => SaveAndClose();

        var lblLanguageHeader = new Label
        {
            Text = AppLanguage.T("Settings.Section.Language", "Sprache"),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };

        var noteLanguage = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = AppLanguage.T(
                "Settings.Note.Language",
                "Die Sprachänderung wird nach dem Speichern sofort auf die Hauptoberfläche angewendet."),
            Margin = new Padding(0, 6, 0, 10)
        };

        var lblSshHeader = new Label
        {
            Text = AppLanguage.T("Settings.Section.Ssh", "SSH"),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 4)
        };

        var noteSsh = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = AppLanguage.T(
                "Settings.Note.Ssh",
                "Hinweis: Für Shut Down per SSH am einfachsten root verwenden oder sudo ohne Passwort für den Shutdown-Befehl erlauben."),
            Margin = new Padding(0, 12, 0, 0)
        };

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttonBar.Controls.Add(btnCancel);
        buttonBar.Controls.Add(btnOk);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 17
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < 17; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(lblLanguageHeader, 0, 0);
        layout.SetColumnSpan(lblLanguageHeader, 2);

        AddRow(layout, 1, AppLanguage.T("Settings.Label.Language", "Sprache"), cboLanguage);

        layout.Controls.Add(noteLanguage, 0, 2);
        layout.SetColumnSpan(noteLanguage, 2);

        AddRow(layout, 3, AppLanguage.T("Settings.Label.DeviceName", "Gerätename"), txtDisplayName);
        AddRow(layout, 4, AppLanguage.T("Settings.Label.WolMac", "WoL MAC-Adresse"), txtMacAddress);
        AddRow(layout, 5, AppLanguage.T("Settings.Label.Broadcast", "Broadcast-Adresse"), txtBroadcastAddress);
        AddRow(layout, 6, AppLanguage.T("Settings.Label.WolPort", "WoL Port"), numWakePort);
        AddRow(layout, 7, AppLanguage.T("Settings.Label.StartupDelaySeconds", "NAS Startzeit (Sek.)"), numStartupDelaySeconds);

        layout.Controls.Add(lblSshHeader, 0, 8);
        layout.SetColumnSpan(lblSshHeader, 2);

        AddRow(layout, 9, AppLanguage.T("Settings.Label.SshHost", "SSH Host"), txtSshHost);
        AddRow(layout, 10, AppLanguage.T("Settings.Label.SshPort", "SSH Port"), numSshPort);
        AddRow(layout, 11, AppLanguage.T("Settings.Label.SshUser", "SSH Benutzer"), txtSshUsername);
        AddRow(layout, 12, AppLanguage.T("Settings.Label.SshPassword", "SSH Passwort"), txtSshPassword);
        AddRow(layout, 13, AppLanguage.T("Settings.Label.ShutdownCommand", "Shut Down Befehl"), txtShutdownCommand);

        layout.Controls.Add(noteSsh, 0, 14);
        layout.SetColumnSpan(noteSsh, 2);

        layout.Controls.Add(buttonBar, 0, 15);
        layout.SetColumnSpan(buttonBar, 2);

        Controls.Add(layout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void SaveAndClose()
    {
        try
        {
            var broadcastAddress = string.IsNullOrWhiteSpace(txtBroadcastAddress.Text)
                ? "255.255.255.255"
                : txtBroadcastAddress.Text.Trim();

            if (!IPAddress.TryParse(broadcastAddress, out _))
            {
                MessageBox.Show(
                    this,
                    AppLanguage.T("Settings.Error.InvalidBroadcast", "Die Broadcast-Adresse ist ungültig."),
                    AppLanguage.T("Error.Input", "Eingabefehler"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var normalizedMac = string.Empty;
            if (!string.IsNullOrWhiteSpace(txtMacAddress.Text))
                normalizedMac = RemoteDeviceService.NormalizeMacAddress(txtMacAddress.Text);

            var sshHost = txtSshHost.Text.Trim();
            var sshUser = txtSshUsername.Text.Trim();

            if (string.IsNullOrWhiteSpace(sshHost))
            {
                sshUser = string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(sshUser))
            {
                MessageBox.Show(
                    this,
                    AppLanguage.T("Settings.Error.MissingSshUser", "Bitte einen SSH-Benutzer angeben."),
                    AppLanguage.T("Error.Input", "Eingabefehler"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SelectedLanguageCode = (cboLanguage.SelectedItem as LanguageComboItem)?.Code ?? AppLanguage.CurrentCode;

            ResultSettings = new RemoteDeviceSettings
            {
                DisplayName = string.IsNullOrWhiteSpace(txtDisplayName.Text)
                    ? "NestDisk"
                    : txtDisplayName.Text.Trim(),
                MacAddress = normalizedMac,
                BroadcastAddress = broadcastAddress,
                WakePort = (int)numWakePort.Value,
                StartupDelaySeconds = (int)numStartupDelaySeconds.Value,
                SshHost = sshHost,
                SshPort = (int)numSshPort.Value,
                SshUsername = sshUser,
                SshPassword = txtSshPassword.Text,
                ShutdownCommand = txtShutdownCommand.Text.Trim()
            };

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                AppLanguage.T("Error.Input", "Eingabefehler"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void AddRow(TableLayoutPanel layout, int rowIndex, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6)
        };

        control.Margin = new Padding(0, 3, 0, 3);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        layout.Controls.Add(label, 0, rowIndex);
        layout.Controls.Add(control, 1, rowIndex);
    }
}
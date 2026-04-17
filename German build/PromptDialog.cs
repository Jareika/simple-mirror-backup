using System.Drawing;

namespace SimpleMirrorBackup;

public static class PromptDialog
{
    public static string? Show(IWin32Window? owner, string title, string label, string initialValue = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(480, 0)
        };

        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        var txt = new TextBox
        {
            Text = initialValue,
            Width = 420,
            Margin = new Padding(0, 0, 0, 12)
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(6, 0, 0, 0)
        };

        var cancel = new Button
        {
            Text = "Abbrechen",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(6, 0, 0, 0)
        };

        var buttonBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };
        buttonBar.Controls.Add(cancel);
        buttonBar.Controls.Add(ok);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(lbl, 0, 0);
        layout.Controls.Add(txt, 0, 1);
        layout.Controls.Add(buttonBar, 0, 2);

        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) =>
        {
            txt.Focus();
            txt.SelectAll();
        };

        var result = form.ShowDialog(owner);
        return result == DialogResult.OK ? txt.Text : null;
    }
}
using System.Drawing;

namespace SimpleMirrorBackup;

public sealed class PlanPreviewForm : Form
{
    private readonly DataGridView grid = new();

    public PlanPreviewForm(BackupPlan plan)
    {
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Vergleich - {GetModeText(plan.Mode)}";
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var lblSummary = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = BuildSummary(plan),
            Margin = new Padding(0, 0, 0, 8)
        };

        var txtWarnings = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Visible = plan.Warnings.Count > 0,
            Height = 92,
            MinimumSize = new Size(0, 92),
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(255, 248, 225),
            Text = string.Join(Environment.NewLine, plan.Warnings.Select(x => "• " + x))
        };

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
		grid.AllowUserToResizeColumns = true;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Aktion",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Pfad",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 58,
            MinimumWidth = 220,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Details",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 42,
            MinimumWidth = 260,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        PopulateGrid(plan);

        var btnClose = new Button
        {
            Text = "Schließen",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(110, 34),
            Padding = new Padding(10, 4, 10, 4)
        };

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttonBar.Controls.Add(btnClose);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(lblSummary, 0, 0);
        layout.Controls.Add(grid, 0, 1);

        if (plan.Warnings.Count > 0)
            layout.Controls.Add(txtWarnings, 0, 2);

        layout.Controls.Add(buttonBar, 0, 3);

        Controls.Add(layout);
        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private void PopulateGrid(BackupPlan plan)
    {
        grid.SuspendLayout();
        try
        {
            foreach (var entry in plan.Entries)
            {
                var rowIndex = grid.Rows.Add(
                    entry.ActionText,
                    string.IsNullOrWhiteSpace(entry.RelativePath) ? "." : entry.RelativePath,
                    entry.DetailsText);

                var row = grid.Rows[rowIndex];
                var isDelete = entry.IsDelete;
                var foreColor = isDelete ? Color.Firebrick : Color.DarkGreen;
                var backColor = isDelete ? Color.MistyRose : Color.Honeydew;
                var selectionColor = isDelete
                    ? Color.FromArgb(255, 225, 225)
                    : Color.FromArgb(225, 245, 225);

                row.DefaultCellStyle.ForeColor = foreColor;
                row.DefaultCellStyle.SelectionForeColor = foreColor;
                row.DefaultCellStyle.BackColor = backColor;
                row.DefaultCellStyle.SelectionBackColor = selectionColor;
            }
        }
        finally
        {
            grid.ResumeLayout();
        }
    }

    private static string BuildSummary(BackupPlan plan)
    {
        var modeText = GetModeText(plan.Mode);
        var baseText = plan.TotalCount == 0
            ? $"{modeText}: keine Änderungen erforderlich."
            : $"{modeText}: {plan.CopyCount} Kopieraktionen, {plan.DeleteCount} Löschaktionen, insgesamt {plan.TotalCount} Änderungen.";

        return plan.Warnings.Count == 0
            ? baseText
            : baseText + $" Warnungen: {plan.Warnings.Count}.";
    }

    private static string GetModeText(BackupMode mode)
    {
        return mode switch
        {
            BackupMode.Mirror => "Spiegel-Vergleich",
            BackupMode.Synchronize => "Synchronisations-Vergleich",
            BackupMode.Backup => "Backup-Vergleich",
            _ => "Vergleich"
        };
    }
}
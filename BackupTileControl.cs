using System.Drawing;

namespace SimpleMirrorBackup;

public sealed class BackupTileControl : Control
{
    private string _titleText = string.Empty;
    private string _infoText = string.Empty;
    private bool _isSelected;

    public string TitleText
    {
        get => _titleText;
        set
        {
            _titleText = value ?? string.Empty;
            Invalidate();
        }
    }

    public string InfoText
    {
        get => _infoText;
        set
        {
            _infoText = value ?? string.Empty;
            Invalidate();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            Invalidate();
        }
    }

    public BackupTileControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        TabStop = false;
        Cursor = Cursors.Hand;
        Size = new Size(220, 84);
        Margin = new Padding(6);
        Padding = new Padding(10);
        BackColor = Color.White;
        ForeColor = SystemColors.ControlText;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var rect = ClientRectangle;

        if (rect.Width <= 1 || rect.Height <= 1)
            return;

        var backColor = _isSelected
            ? Color.FromArgb(221, 238, 255)
            : Color.White;

        var borderColor = _isSelected
            ? Color.SteelBlue
            : Color.Silver;

        using (var backBrush = new SolidBrush(backColor))
            g.FillRectangle(backBrush, rect);

        var borderRect = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        using (var borderPen = new Pen(borderColor, _isSelected ? 2f : 1f))
        {
            if (_isSelected)
                borderRect.Inflate(-1, -1);

            g.DrawRectangle(borderPen, borderRect);
        }

        var titleRect = new Rectangle(
            Padding.Left,
            Padding.Top,
            Math.Max(0, Width - Padding.Horizontal),
            26);

        var infoRect = new Rectangle(
            Padding.Left,
            Padding.Top + 30,
            Math.Max(0, Width - Padding.Horizontal),
            Math.Max(0, Height - Padding.Vertical - 30));

        var titleColor = Enabled ? ForeColor : SystemColors.GrayText;
        var infoColor = Enabled ? Color.DimGray : SystemColors.GrayText;

        TextRenderer.DrawText(
            g,
            _titleText,
            Font,
            titleRect,
            titleColor,
            TextFormatFlags.Left |
            TextFormatFlags.Top |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        using var infoFont = new Font(
            Font.FontFamily,
            Math.Max(8f, Font.Size - 0.5f),
            FontStyle.Regular,
            GraphicsUnit.Point);

        TextRenderer.DrawText(
            g,
            _infoText,
            infoFont,
            infoRect,
            infoColor,
            TextFormatFlags.Left |
            TextFormatFlags.Top |
            TextFormatFlags.WordBreak |
            TextFormatFlags.NoPrefix);
    }
}
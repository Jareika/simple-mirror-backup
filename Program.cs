namespace SimpleMirrorBackup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Simple Mirror Backup - Startfehler",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
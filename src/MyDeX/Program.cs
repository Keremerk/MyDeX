namespace MyDeX;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\MyDeX.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("MyDeX is already running. Look for its icon in the system tray.",
                "MyDeX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        bool startInTray = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(startInTray));
    }
}

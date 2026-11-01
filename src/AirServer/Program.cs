namespace AirServer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Local\AirServer.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("AirServer est déjà lancé (voir la zone de notification).", "AirServer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

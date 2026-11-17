namespace ComicArchiveConverter;

internal static class Program
{
    /// <summary>The first code that runs when the .exe starts.</summary>
    [STAThread] // Windows dialogs and drag-and-drop require a "single-threaded apartment" UI thread.
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // sharp text on 4K / scaled screens
        Application.EnableVisualStyles();                     // modern Windows look for buttons etc.
        Application.SetCompatibleTextRenderingDefault(false);

        // Last line of defence: show unexpected errors instead of silently closing.
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }
}

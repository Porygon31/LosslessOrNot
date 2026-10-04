using System.Globalization;
using LosslessOrNot.Core;

namespace LosslessOrNot;

static class Program
{
    /// <summary>
    ///  The main entry point for the application. Files or folders passed on the
    ///  command line (dropped onto the exe, "Open with"…) are queued and analysed
    ///  straight away.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // The interface is French-only: numbers must read "16,05 kHz" even on a
        // machine with other regional settings.
        CultureInfo culture = TextFormat.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }
}

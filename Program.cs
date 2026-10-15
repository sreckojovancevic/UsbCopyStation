using System.Security.Principal;
using System.Text;
using UsbCopyStation.UI;

namespace UsbCopyStation;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // OEM kodna strana za izlaz diskpart-a
        ApplicationConfiguration.Initialize();

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("USB Copy Station mora da se pokrene kao administrator (raw upis na diskove).",
                "USB Copy Station", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm());
    }
}

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Windows.Forms;

namespace PolarH10.PreviewInstaller;

internal static class Program
{
    private const string AppInstallerDownloadUri = "https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.appinstaller";
    private const string CertificateDownloadUri = "https://github.com/MesmerPrism/PolarH10/releases/latest/download/PolarH10.cer";
    private const string ReleasePageUri = "https://github.com/MesmerPrism/PolarH10/releases";
    private const string DownloadDirectoryName = "PolarH10PreviewSetup";

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            EnsureAdministrator();
            InstallPreviewAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return 1;
        }
    }

    private static async Task InstallPreviewAsync()
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), DownloadDirectoryName);
        Directory.CreateDirectory(downloadDirectory);

        var certificatePath = Path.Combine(downloadDirectory, "PolarH10.cer");
        var appInstallerPath = Path.Combine(downloadDirectory, "PolarH10.appinstaller");

        using var httpClient = new HttpClient();

        await DownloadFileAsync(httpClient, CertificateDownloadUri, certificatePath);
        await DownloadFileAsync(httpClient, AppInstallerDownloadUri, appInstallerPath);

        TrustCertificate(certificatePath);
        LaunchAppInstaller(appInstallerPath);
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException(
                "Administrator permission is required to trust the preview signing certificate. " +
                "Run the setup again and accept the Windows UAC prompt.");
        }
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string sourceUri, string destinationPath)
    {
        using var response = await httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output);
    }

    private static void TrustCertificate(string certificatePath)
    {
        using var certificate = new X509Certificate2(certificatePath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);

        store.Open(OpenFlags.ReadWrite);

        var alreadyTrusted = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Count > 0;

        if (!alreadyTrusted)
        {
            // Research preview packages are self-signed, so the public cert must be trusted explicitly.
            store.Add(certificate);
        }
    }

    private static void LaunchAppInstaller(string appInstallerPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appInstallerPath,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private static void ShowError(Exception exception)
    {
        var message =
            "PolarH10 Preview Setup could not finish.\n\n" +
            $"{exception.Message}\n\n" +
            "If the public preview release is not available yet, open the release page or use the source-build path instead.\n" +
            $"{ReleasePageUri}";

        MessageBox.Show(
            message,
            "PolarH10 Preview Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

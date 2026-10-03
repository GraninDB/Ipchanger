using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IPChanger.Forms;

public partial class IpconfigWindow : Window
{
    // OEM-кодировка консоли Windows (866 для ru, 437/850 для en и т.д.).
    // На .NET Core кодовые страницы, кроме UTF-*, могут быть недоступны без
    // CodePagesEncodingProvider — поэтому есть фоллбэк в UTF-8.
    private static readonly Encoding OemEncoding = ResolveOemEncoding();

    public IpconfigWindow()
    {
        InitializeComponent();
        Title = L.T("WindowIpconfigTitle");
        CopyButton.Content = L.T("BtnCopyToClipboard");

        Loaded += OnLoaded;
    }

    private static Encoding ResolveOemEncoding()
    {
        try
        {
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (oem > 0)
                return Encoding.GetEncoding(oem);
        }
        catch
        {
            // CodePagesEncodingProvider не зарегистрирован либо кодовая
            // страница недоступна — используем UTF-8.
        }

        return Encoding.UTF8;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Окно может многократно переоткрываться; грузим только один раз.
        Loaded -= OnLoaded;
        await LoadIpconfigAsync();
    }

    private async Task LoadIpconfigAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/all",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = OemEncoding,
                    StandardErrorEncoding = OemEncoding
                }
            };

            if (!process.Start())
            {
                OutputBox.Text = "Failed to start ipconfig.";
                return;
            }

            // Читаем ОБА потока параллельно — иначе возможен дедлок, если
            // дочерний процесс упрётся в заполненный буфер stderr.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            bool timedOut = false;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Процесс уже мог завершиться.
                    }
                }
            }

            // После завершения/убийства процесса пайпы закрываются — таски
            // гарантированно завершаются.
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (timedOut)
            {
                OutputBox.Text = "ipconfig timed out.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr))
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(stdout))
                    sb.Append(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(stderr);
                }
                OutputBox.Text = sb.ToString();
            }
            else
            {
                OutputBox.Text = "No output.";
            }
        }
        catch (Exception ex)
        {
            OutputBox.Text = $"Error: {ex.Message}";
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = !string.IsNullOrEmpty(OutputBox.SelectedText)
                ? OutputBox.SelectedText
                : OutputBox.Text;

            if (string.IsNullOrEmpty(text))
                return;

            // SetDataObject с copy: true — данные остаются в буфере после
            // закрытия приложения.
            Clipboard.SetDataObject(text, true);
        }
        catch
        {
            // Буфер обмена может быть заблокирован другим приложением —
            // молча игнорируем.
        }
    }
}
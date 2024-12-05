using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace YoutubeDL
{
    public sealed partial class MainWindow : Window
    {
        private Microsoft.UI.Windowing.AppWindow? m_AppWindow;
        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            m_AppWindow = GetAppWindowForCurrentWindow();

            var titleBar = m_AppWindow.TitleBar;

            var appWindow = this.AppWindow;
            appWindow.Resize(new SizeInt32(900, 600));
            SystemBackdrop = new MicaBackdrop()
            { Kind = MicaKind.Base };
        }
        private Microsoft.UI.Windowing.AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(wndId);
        }
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text;
            string quality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(quality))
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Invalid Input",
                    Content = "Please enter a valid YouTube URL and select a quality.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            // Clear the output TextBox before starting a new download
            OutputTextBox.Text = string.Empty;

            // Determine the format extension based on the quality
            string selectedExtension = quality == "Audio-only" ? "mp3" : "mp4"; // Default to mp4 for video

            // Build yt-dlp command arguments
            string formatCode = GetFormatCode(quality, selectedExtension);
            string arguments = $"-f {formatCode} -o \"%(title)s.{selectedExtension}\" {url}";

            // If audio is selected (mp3), force audio extraction and conversion
            if (selectedExtension == "mp3")
            {
                arguments = $"-f bestaudio -x --audio-format mp3 -o \"%(title)s.%(ext)s\" {url}";
            }

            // Start the download process
            DownloadProgressBar.Visibility = Visibility.Visible;
            await RunYtDlpAsync(arguments, selectedExtension);
            DownloadProgressBar.Visibility = Visibility.Collapsed;
        }

        private string GetFormatCode(string quality, string extension)
        {
            if (extension == "mp3")
            {
                return "bestaudio";
            }
            else if (extension == "mp4")
            {
                return quality switch
                {
                    "1080p" => "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
                    "720p" => "bestvideo[height<=720]+bestaudio/best[height<=720]",
                    "Audio-only" => "bestaudio",
                    _ => "best"
                };
            }
            return "best";
        }

        private async Task RunYtDlpAsync(string arguments, string selectedExtension)
        {
            try
            {
                string binariesPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Binaries");
                string ytDlpPath = Path.Combine(binariesPath, "yt-dlp.exe");

                // Get the user's home directory and append 'Downloads'
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloadPath = Path.Combine(userHome, "Downloads");

                // Ensure the download directory exists
                Directory.CreateDirectory(downloadPath);

                // Add output path and specify the location of ffmpeg (if needed)
                if (selectedExtension == "mp4")
                {
                    string ffmpegPath = Path.Combine(binariesPath, "ffmpeg.exe");
                    arguments += $" --ffmpeg-location \"{ffmpegPath}\" --merge-output-format {selectedExtension} -o \"{Path.Combine(downloadPath, "%(title)s.%(ext)s")}\"";
                }
                else
                {
                    arguments += $" -o \"{Path.Combine(downloadPath, "%(title)s.%(ext)s")}\"";
                }

                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ytDlpPath,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += (sender, args) => AppendOutput(args.Data);
                process.ErrorDataReceived += (sender, args) => AppendOutput(args.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}");
            }
        }

        private void AppendOutput(string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                OutputTextBox.DispatcherQueue.TryEnqueue(() =>
                {
                    OutputTextBox.Text += text + "\n";

                    // Scroll to the bottom by setting the VerticalOffset
                    if (OutputScrollViewer != null)
                    {
                        OutputScrollViewer.ChangeView(null, OutputScrollViewer.ExtentHeight, null);
                    }
                });
            }
        }
    }
}

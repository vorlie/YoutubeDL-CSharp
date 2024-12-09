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
            var selectedItem = QualityComboBox.SelectedItem as ComboBoxItem;
            string quality = selectedItem != null ? selectedItem.Content?.ToString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(url) || QualityComboBox.SelectedItem == null)
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
            string formatCode = GetFormatCode(quality);
            string arguments = $"-f {formatCode} -o \"%(title)s.{selectedExtension}\" {url}";

            // If audio is selected (mp3), force audio extraction and conversion
            if (selectedExtension == "mp3")
            {
                arguments = $"-f bestaudio -x --audio-format mp3 -o \"%(title)s.%(ext)s\" {url}";
            }

            if (quality == "best")
            {
                AppendOutput("Warning: Defaulting to 'best' quality as no valid quality was selected.");
            }

            // Start the download process
            DownloadProgressBar.Visibility = Visibility.Visible;
            await RunYtDlpAsync(arguments, selectedExtension);
            DownloadProgressBar.Visibility = Visibility.Collapsed;

            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadPath = Path.Combine(userHome, "Downloads");
            var mostRecentFile = Directory.GetFiles(downloadPath)
                .Select(file => new FileInfo(file))
                .OrderByDescending(fileInfo => fileInfo.CreationTime)
                .FirstOrDefault();

            if (mostRecentFile != null)
            {
                // Show the popup with the file path
                await ShowDownloadCompleteDialog(mostRecentFile.FullName);
            }
        }
        private async void CheckFormatsButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text;

            if (string.IsNullOrWhiteSpace(url))
            {
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Invalid Input",
                    Content = "Please enter a valid YouTube URL.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            // Display a message indicating that the formats are being fetched
            AppendOutput("Fetching available formats... Please wait.");

            // Fetch the available formats using yt-dlp
            string arguments = $"-F {url}";  // -F option lists available formats
            DownloadProgressBar.Visibility = Visibility.Visible;
            await RunYtDlpFormatAsync(arguments);
            DownloadProgressBar.Visibility = Visibility.Collapsed;

            DownloadButton.IsEnabled = true;
            QualityComboBox.IsEnabled = true;
        }

        private string GetFormatCode(string quality)
        {
            return quality switch
            {
                "2160p" => "bestvideo[width<=3840]+bestaudio/best[width<=3840]",
                "1440p" => "bestvideo[width<=2560]+bestaudio/best[width<=2560]",
                "1080p" => "bestvideo[width<=1920]+bestaudio/best[width<=1920]",
                "720p" => "bestvideo[width<=1280]+bestaudio/best[width<=1280]",
                "Audio-only" => "bestaudio",
                _ => "best"
            };

        }

        private async Task RunYtDlpFormatAsync(string arguments)
        {
            try
            {
                string binariesPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Binaries");
                string ytDlpPath = Path.Combine(binariesPath, "yt-dlp.exe");

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
                    arguments += $" --add-metadata --write-thumbnail --embed-thumbnail --ffmpeg-location \"{ffmpegPath}\" --merge-output-format {selectedExtension} -o \"{Path.Combine(downloadPath, "%(title)s.%(ext)s")}\"";
                }
                else
                {
                    arguments += $" --add-metadata --write-thumbnail --embed-thumbnail -o \"{Path.Combine(downloadPath, "%(title)s.%(ext)s")}\"";
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

        private async Task ShowDownloadCompleteDialog(string downloadedFilePath)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Download Complete",
                Content = "Your download has finished. What would you like to do?",
                PrimaryButtonText = "Show File",
                CloseButtonText = "Ok",
                XamlRoot = this.Content.XamlRoot // Ensure the dialog is shown in the correct context
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Open File Explorer and select the file
                Process.Start("explorer.exe", $"/select,\"{downloadedFilePath}\"");
            }
        }

    }
}

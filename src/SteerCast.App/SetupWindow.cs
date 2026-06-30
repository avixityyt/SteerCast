using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SteerCast.App;

public sealed class SetupWindow : Form
{
    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(18, 16, 14)
    };

    private readonly string _iconPath;
    private string _targetUrl;
    private bool _initialized;

    public SetupWindow(string setupUrl, string iconPath)
    {
        _targetUrl = setupUrl;
        _iconPath = iconPath;

        Text = "SteerCast Setup";
        Name = "SteerCastSetupWindow";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 640);
        Size = new Size(1280, 820);
        BackColor = Color.FromArgb(18, 16, 14);

        if (File.Exists(_iconPath))
        {
            Icon = new Icon(_iconPath);
        }

        Controls.Add(_webView);
        Shown += async (_, _) => await InitializeAsync();
    }

    public void Navigate(string setupUrl)
    {
        _targetUrl = setupUrl;
        if (_initialized && _webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(_targetUrl);
        }
    }

    private async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteerCast",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            _webView.CoreWebView2.Navigate(_targetUrl);
            _initialized = true;
        }
        catch (Exception exception)
        {
            ShowWebViewError(exception);
        }
    }

    private void ShowWebViewError(Exception exception)
    {
        Controls.Clear();

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 16, 14),
            Padding = new Padding(32),
            ColumnCount = 1,
            RowCount = 4
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(235, 252, 251),
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Text = "SteerCast setup could not open inside the app."
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Padding = new Padding(0, 14, 0, 8),
            ForeColor = Color.FromArgb(190, 205, 200),
            Font = new Font("Segoe UI", 10),
            Text = "Install or repair the Microsoft Edge WebView2 Runtime, then reopen setup from the tray icon."
        });

        var openButton = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(150, 36),
            Text = "Open setup URL"
        };
        openButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_targetUrl) { UseShellExecute = true });
        panel.Controls.Add(openButton);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Padding = new Padding(0, 16, 0, 0),
            ForeColor = Color.FromArgb(150, 170, 165),
            Font = new Font("Consolas", 9),
            Text = $"{_targetUrl}{Environment.NewLine}{exception.Message}"
        });

        Controls.Add(panel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}

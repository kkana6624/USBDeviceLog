using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using USBDeviceLogApp.Services;

namespace USBDeviceLogApp;

public partial class MainForm : Form
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

    private readonly UsbDeviceService _usbService;

    public MainForm()
    {
        Text = "USB Device Log - Blazor Hybrid";
        Width = 1000;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        _usbService = new UsbDeviceService();

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(_usbService);

        var blazor = new BlazorWebView()
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot\\index.html",
            Services = services.BuildServiceProvider(),
        };

        blazor.RootComponents.Add<Components.App>("#app");

        Controls.Add(blazor);
    }

    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private DateTime _lastScanTime = DateTime.MinValue;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WM_DEVICECHANGE)
        {
            int wParam = m.WParam.ToInt32();
            // GUI apps receive DBT_DEVNODES_CHANGED without registering for device notifications
            if (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE || wParam == DBT_DEVNODES_CHANGED)
            {
                // Simple debounce (prevent multiple overlapping scans)
                if ((DateTime.Now - _lastScanTime).TotalSeconds > 2)
                {
                    _lastScanTime = DateTime.Now;

                    // Offload to background thread and slightly delay so WMI PnP state is finalized
                    Task.Run(async () => 
                    {
                        await Task.Delay(1500);
                        _usbService.CheckDeviceChanges();
                    });
                }
            }
        }
    }
}

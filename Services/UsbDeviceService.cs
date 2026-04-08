using System.Management;

namespace USBDeviceLogApp.Services;

public class UsbDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public string DeviceID { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum UsbActionType
{
    Connected,
    Disconnected
}

public class UsbLogEntry
{
    public DateTime Timestamp { get; set; }
    public UsbActionType Action { get; set; }
    public UsbDeviceInfo Device { get; set; } = new();
}

public class UsbDeviceService
{
    public List<UsbDeviceInfo> CurrentDevices { get; private set; } = new();
    public List<UsbLogEntry> HistoryLogs { get; private set; } = new();

    public event Action? OnHistoryUpdated;

    public UsbDeviceService()
    {
        // Initialize current state without generating history logs for the first load
        CurrentDevices = GetConnectedUsbDevices();
    }

    public void CheckDeviceChanges()
    {
        var updatedDevices = GetConnectedUsbDevices();
        bool changed = false;

        // Check for new devices (Connected)
        foreach (var newDevice in updatedDevices)
        {
            if (!CurrentDevices.Any(d => d.DeviceID == newDevice.DeviceID))
            {
                HistoryLogs.Insert(0, new UsbLogEntry
                {
                    Timestamp = DateTime.Now,
                    Action = UsbActionType.Connected,
                    Device = newDevice
                });
                changed = true;
            }
        }

        // Check for removed devices (Disconnected)
        foreach (var oldDevice in CurrentDevices)
        {
            if (!updatedDevices.Any(d => d.DeviceID == oldDevice.DeviceID))
            {
                HistoryLogs.Insert(0, new UsbLogEntry
                {
                    Timestamp = DateTime.Now,
                    Action = UsbActionType.Disconnected,
                    Device = oldDevice
                });
                changed = true;
            }
        }

        if (changed)
        {
            CurrentDevices = updatedDevices;
            OnHistoryUpdated?.Invoke();
        }
    }

    private string GetVidPid(string deviceId)
    {
        var parts = deviceId.Split('\\');
        if (parts.Length >= 2)
        {
            var identifiers = parts[1].Split('&');
            if (identifiers.Length >= 2 && identifiers[0].StartsWith("VID") && identifiers[1].StartsWith("PID"))
            {
                return $"{identifiers[0]}&{identifiers[1]}";
            }
        }
        return deviceId; // Fallback to raw ID if not standard USB VID/PID
    }

    public List<UsbDeviceInfo> GetConnectedUsbDevices()
    {
        var devices = new List<UsbDeviceInfo>();
        
        try
        {
            var genericNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USB 入力デバイス", "USB Input Device", "USB Composite Device", 
                "汎用 USB ハブ", "Generic USB Hub", "汎用 SuperSpeed USB ハブ", 
                "USB ルート ハブ (USB 3.0)", "USB Root Hub (USB 3.0)", "USB 大容量記憶装置", 
                "USB Mass Storage Device", "USB 接続の SCSI (UAS) マス ストレージ デバイス"
            };

            using var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity Where PNPDeviceID LIKE 'USB\\%'");
            using var collection = searcher.Get();

            var rawDevices = new List<UsbDeviceInfo>();

            foreach (var device in collection)
            {
                var name = device["Name"]?.ToString() ?? "Unknown USB Device";
                var id = device["DeviceID"]?.ToString() ?? "";
                var status = device["Status"]?.ToString() ?? "Unknown";
                var desc = device["Description"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(id) && (status.Equals("OK", StringComparison.OrdinalIgnoreCase)))
                {
                    rawDevices.Add(new UsbDeviceInfo
                    {
                        Name = name,
                        DeviceID = id,
                        Status = status,
                        Description = desc
                    });
                }
            }

            var grouped = rawDevices.GroupBy(d => GetVidPid(d.DeviceID));

            foreach (var group in grouped)
            {
                // Find the most descriptive name in the group
                var bestDevice = group.FirstOrDefault(d => !genericNames.Contains(d.Name)) ?? group.First();
                
                // Count how many physical devices this actually represents by counting parent nodes (no MI_ interface tag)
                int physicalCount = group.Count(d => !d.DeviceID.Contains("MI_"));
                // If it's a weird device with no parent node visible, fallback to 1
                if (physicalCount == 0) physicalCount = 1; 

                for (int i = 0; i < physicalCount; i++)
                {
                    // If multiple same devices, give them unique ID suffixes for history tracking difference
                    string uniqueId = physicalCount > 1 ? $"{group.Key}#{i}" : group.Key;
                    
                    devices.Add(new UsbDeviceInfo
                    {
                        Name = bestDevice.Name,
                        DeviceID = uniqueId,
                        Status = "OK",
                        Description = bestDevice.Description
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving USB devices: {ex.Message}");
        }

        // Sort to show specific named items first
        return devices.OrderBy(d => d.Name.StartsWith("USB") ? 1 : 0).ThenBy(d => d.Name).ToList();
    }
}

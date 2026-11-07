using System.Runtime.InteropServices;
using System.Windows;
using PCL.Core.App.Localization;
using PCL.Core.Utils.OS;

namespace PCL;

public partial class PageToolsDeviceInfo
{
    public PageToolsDeviceInfo()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshDeviceInfo();
            // 硬件信息在启动时于后台线程获取，若此时尚未就绪则再获取一次
            if (HardwareInfo.CPUName == "Unknown" || HardwareInfo.GPUs.Count == 0)
                ModBase.RunInNewThread(() =>
                {
                    HardwareInfo.GetHardwareInfo();
                    ModBase.RunInUi(RefreshDeviceInfo);
                }, "RefreshDeviceInfo");
        };
    }

    private void RefreshDeviceInfo()
    {
        var arch = SystemInfo.Is32BitSystem ? "x86" : SystemInfo.IsArm64System ? "ARM64" : "x64";
        ItemDeviceOs.Info = $"{RuntimeInformation.OSDescription} ({arch})";

        ItemDeviceCpu.Info = HardwareInfo.CPUName == "Unknown"
            ? Lang.Text("Tools.Device.Unknown")
            : $"{HardwareInfo.CPUName} ({Lang.Text("Tools.Device.Cores", HardwareInfo.CPUPhysicalCores)})";

        ItemDeviceMemory.Info = Lang.Text("Tools.Device.SystemMemory",
            Lang.Number(HardwareInfo.SystemMemorySize / 1024d, "N1"));

        PanDeviceGpu.Children.Clear();
        if (HardwareInfo.GPUs.Count == 0)
        {
            PanDeviceGpu.Children.Add(BuildDeviceItem(Lang.Text("Tools.Device.GPU", 1),
                Lang.Text("Tools.Device.Unknown")));
            return;
        }

        for (var i = 0; i < HardwareInfo.GPUs.Count; i++)
        {
            var gpu = HardwareInfo.GPUs[i];
            var info = string.IsNullOrWhiteSpace(gpu.DriverVersion)
                ? gpu.Name
                : $"{gpu.Name} ({gpu.DriverVersion})";
            PanDeviceGpu.Children.Add(BuildDeviceItem(Lang.Text("Tools.Device.GPU", i + 1), info));
        }
    }

    private static MyListItem BuildDeviceItem(string title, string info) => new()
    {
        SvgIcon = "lucide/circuit-board",
        Title = title,
        Info = info,
        Height = 40d,
        IsHitTestVisible = false
    };
}

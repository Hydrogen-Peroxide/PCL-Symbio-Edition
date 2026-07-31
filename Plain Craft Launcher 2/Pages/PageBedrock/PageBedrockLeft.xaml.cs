using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCL.Core.Minecraft.Bedrock;

namespace PCL;

public partial class PageBedrockLeft
{
    private List<BedrockInstance> _instances = [];
    private BedrockInstance? _selectedInstance;

    public PageBedrockLeft()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshInstances();
    }

    public void RefreshInstances()
    {
        var installRoot = System.IO.Path.Combine(ModBase.pathAppdata, "Bedrock");
        _instances = BedrockDownloadService.GetInstalledInstances(installRoot);

        ListInstances.ItemsSource = null;
        ListInstances.ItemsSource = _instances;

        if (_instances.Count > 0)
        {
            ListInstances.SelectedIndex = 0;
            LabVersion.Text = $"已安装 {_instances.Count} 个版本";
        }
        else
        {
            LabVersion.Text = "未安装任何版本";
            BtnLaunch.IsEnabled = false;
        }
    }

    private void ListInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListInstances.SelectedItem is BedrockInstance instance)
        {
            _selectedInstance = instance;
            LabVersion.Text = instance.VersionName;
            BtnLaunch.IsEnabled = true;
        }
        else
        {
            _selectedInstance = null;
            LabVersion.Text = "未选择版本";
            BtnLaunch.IsEnabled = false;
        }
    }

    private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance == null) return;

        BtnLaunch.IsEnabled = false;
        BtnLaunch.Text = "正在启动...";

        try
        {
            var launchService = new BedrockLaunchService(_selectedInstance)
            {
                UpdateProgressText = text => ModBase.RunInUi(() =>
                {
                    BtnLaunch.Text = text;
                }),
                LaunchCompleted = () => ModBase.RunInUi(() =>
                {
                    BtnLaunch.Text = "启动基岩版";
                    BtnLaunch.IsEnabled = true;
                }),
                Launched = process => ModBase.RunInUi(() =>
                {
                    BtnLaunch.Text = "游戏已启动";
                })
            };

            await launchService.LaunchAsync();
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "启动基岩版失败", ModBase.LogLevel.Feedback, userSummary: "启动基岩版失败");
            BtnLaunch.Text = "启动失败";
            BtnLaunch.IsEnabled = true;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        ModMain.frmMain.PageChange(FormMain.PageType.Launch);
    }
}
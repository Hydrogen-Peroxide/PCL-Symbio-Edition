using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PCL.Core.Minecraft.Bedrock;

namespace PCL;

public partial class PageBedrockRight
{
    private List<BedrockBuildInfo> _versions = [];
    private bool _isLoading;

    public PageBedrockRight()
    {
        InitializeComponent();
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        PanBack.ScrollToHome();
        _ = LoadVersionsAsync();
    }

    private async System.Threading.Tasks.Task LoadVersionsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        TextStatus.Text = "正在获取版本列表...";
        TextStatus.Visibility = Visibility.Visible;
        BtnRefresh.IsEnabled = false;
        PanVersionList.Children.Clear();

        try
        {
            _versions = await BedrockVersionManifest.GetVersionsAsync();

            if (_versions.Count == 0)
            {
                TextStatus.Text = "无法获取版本列表，请检查网络连接后重试。";
                return;
            }

            TextStatus.Visibility = Visibility.Collapsed;
            DisplayVersions();
        }
        catch (Exception ex)
        {
            TextStatus.Text = $"获取版本列表失败：{ex.Message}";
        }
        finally
        {
            _isLoading = false;
            BtnRefresh.IsEnabled = true;
        }
    }

    private void DisplayVersions()
    {
        PanVersionList.Children.Clear();

        // 按类型分组：Release 优先
        var sorted = _versions
            .OrderByDescending(v => v.Type == "Release")
            .ThenByDescending(v => v.ID)
            .ToList();

        foreach (var version in sorted)
        {
            var card = CreateVersionCard(version);
            PanVersionList.Children.Add(card);
        }
    }

    private FrameworkElement CreateVersionCard(BedrockBuildInfo version)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource("ColorBrush1") ?? Brushes.Transparent,
            BorderBrush = (Brush)FindResource("ColorBrushGray1") ?? Brushes.LightGray,
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 版本名称
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Text = $"版本 {version.ID}",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("ColorBrush3") ?? Brushes.Black
        };
        nameStack.Children.Add(nameText);

        var infoText = new TextBlock
        {
            Text = $"{version.Type} | {version.BuildType} | {version.Variations.Count} 个变体",
            FontSize = 12,
            Foreground = (Brush)FindResource("ColorBrushGray2") ?? Brushes.Gray
        };
        nameStack.Children.Add(infoText);
        Grid.SetColumn(nameStack, 0);
        grid.Children.Add(nameStack);

        // 安装按钮
        var installBtn = new Button
        {
            Content = "安装",
            Width = 80,
            Height = 30,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        installBtn.Click += (_, _) => InstallVersion(version);
        Grid.SetColumn(installBtn, 2);
        grid.Children.Add(installBtn);

        card.Child = grid;
        return card;
    }

    private async void InstallVersion(BedrockBuildInfo buildInfo)
    {
        var installRoot = System.IO.Path.Combine(ModBase.pathAppdata, "Bedrock");
        var service = new BedrockDownloadService(installRoot)
        {
            StatusText = text => ModBase.RunInUi(() =>
            {
                TextStatus.Text = text;
                TextStatus.Visibility = Visibility.Visible;
            }),
            Completed = instance =>
            {
                ModBase.RunInUi(() =>
                {
                    TextStatus.Text = $"安装完成：{instance.VersionName}";
                    // 通知左侧面板刷新
                    ModMain.frmBedrockLeft?.RefreshInstances();
                });
            },
            ErrorOccurred = (title, message, ex) =>
            {
                ModBase.RunInUi(() =>
                {
                    TextStatus.Text = $"{title}：{message}";
                    TextStatus.Visibility = Visibility.Visible;
                });
            }
        };

        try
        {
            await service.InstallAsync(buildInfo, $"基岩版 {buildInfo.ID}");
        }
        catch (Exception ex)
        {
            TextStatus.Text = $"安装失败：{ex.Message}";
            TextStatus.Visibility = Visibility.Visible;
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadVersionsAsync();
    }
}
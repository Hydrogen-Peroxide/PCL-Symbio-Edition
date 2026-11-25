using System.IO;
using System.Security;
using System.Text.Json;
using System.Windows;
using PCL.Core.App.Localization;

namespace PCL;

public partial class HelpDocumentView : IRefreshable
{

    private HelpContent _content = null!;

    public string Title => _content.Title;

    internal HelpDocumentView(HelpContent content)
    {
        InitializeComponent();
        Loaded += (_, _) => PanBack.ScrollToHome();
        ApplyContent(content);
    }

    private void ApplyContent(HelpContent content)
    {
        // 修改时应同时修改 HomeContentView.LoadContent。
        var xaml = ModMain.ArgumentReplace(content.Xaml);
        while (xaml.Contains("xmlns"))
            xaml = xaml.RegexReplace("xmlns[^\"']*(\"|')[^\"']*(\"|')", "").Replace("xmlns", "");
        xaml =
            $"<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:sys=\"clr-namespace:System;assembly=System.Runtime\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\">{xaml}</StackPanel>";

        var element = (UIElement)ModBase.GetObjectFromXML(xaml, out var sanitizeResult);
        foreach (var unsupported in sanitizeResult.UnsupportedTypesFound)
            HintService.Hint(Lang.Text("Event.Sanitize.UnsupportedTypeHint", unsupported), HintType.Error);
        foreach (var unknown in sanitizeResult.UnrecognizedTypes)
            HintService.Hint(Lang.Text("Event.Sanitize.UnknownTypeHint", unknown), HintType.Error);

        _content = content;
        PanCustom.Children.Clear();
        PanCustom.Children.Add(element);
        if (ModMain.frmMain?.pageCurrent.page == FormMain.PageType.HelpDetail)
            ModMain.frmMain.PageNameRefresh();
    }

    public void Refresh()
    {
        try
        {
            ApplyContent(HelpDocuments.LoadContent(_content.SourcePath));
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "刷新帮助详情页失败",
                ModBase.LogLevel.Msgbox,
                userSummary: Lang.Text("Event.Error.ExecutionFailed", EventType.OpenHelp, _content.SourcePath));
        }
    }
}

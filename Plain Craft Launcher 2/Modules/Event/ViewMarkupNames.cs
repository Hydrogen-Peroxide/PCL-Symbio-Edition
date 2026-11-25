using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PCL;

/// <summary>Translate saved page markup to current control types before normal XAML validation.</summary>
internal static partial class ViewMarkupNames
{
    private static bool IsLauncherNamespace(XNamespace? ns) =>
        ns?.NamespaceName == "clr-namespace:PCL" ||
        ns?.NamespaceName == "clr-namespace:PCL;assembly=Plain Craft Launcher 2";

    private static readonly Dictionary<string, string> Names = new()
    {
        ["MyButton"] = nameof(SymbioButton),
        ["MyTextButton"] = nameof(SymbioButton),
        ["MyTextBox"] = nameof(SymbioTextField),
        ["MyComboBox"] = nameof(SymbioSelect),
        ["MyComboBoxItem"] = nameof(SelectOption),
        ["MyMenuItem"] = nameof(SymbioMenuAction),
        ["MyIconButton"] = nameof(SymbioIconButton),
        ["MyExtraButton"] = nameof(FloatingIconAction),
        ["MyExtraTextButton"] = nameof(ProminentAction),
        ["MyIconTextButton"] = nameof(SymbioIconTextAction),
        ["MySlider"] = nameof(SymbioSlider),
        ["MyLoading"] = nameof(LoadingIndicator),
        ["MyCard"] = nameof(WorkspaceCard),
        ["MyListItem"] = nameof(SelectionRow),
        ["MyCompItem"] = nameof(ResourceProjectRow),
        ["MyLocalCompItem"] = nameof(LocalProjectRow),
        ["CompLaunchingStatus"] = nameof(LaunchProgressOverlay),
        ["FontSelector"] = nameof(FontPicker),
        ["MinecraftServer"] = nameof(ServerStatusView),
        ["MinecraftServerQuery"] = nameof(ServerQueryView),
        ["MapEditorView"] = nameof(SchematicEditorView),
        ["SkinViewer3D"] = nameof(SkinPreviewView),
        ["MySearchBox"] = nameof(SymbioSearchField),
        ["MyHint"] = nameof(SymbioNotice),
        ["MyImage"] = nameof(AssetImage),
        ["MyToast"] = nameof(NotificationToast),
        ["MyCollapseBar"] = nameof(ExpandableSection),
        ["MyDropShadow"] = nameof(SoftShadow),
        ["MyRadioButton"] = nameof(SymbioSegment),
        ["MyCheckBox"] = nameof(SymbioCheck),
        ["MyRadioBox"] = nameof(SymbioRadio),
        ["MyPageLeft"] = nameof(WorkspaceSidebar),
        ["MyPageRight"] = nameof(WorkspacePage),
        ["MyScrollViewer"] = "ScrollViewer",
        ["MyScrollBar"] = "ScrollBar"
    };

    [GeneratedRegex(@"(?<![\w.-])(?<prefix>[\w.-]+):(?<type>[A-Za-z_]\w*)(?=[.},\s]|$)")]
    private static partial Regex LegacyTypeReference();

    internal static string Upgrade(string markup)
    {
        if (!Names.Keys.Any(name => markup.Contains(name, StringComparison.Ordinal))) return markup;
        using var input = XmlReader.Create(new StringReader(markup), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        });
        var doc = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string nativePrefix = "symbioWpf";
        var declared = doc.Root!.DescendantsAndSelf().Attributes().Where(a => a.IsNamespaceDeclaration)
            .Select(a => a.Name.LocalName).ToHashSet();
        while (declared.Contains(nativePrefix)) nativePrefix += "_";
        doc.Root.SetAttributeValue(XNamespace.Xmlns + nativePrefix, presentation.NamespaceName);
        static bool Native(string typeName) => typeName is "ScrollViewer" or "ScrollBar";
        foreach (var element in doc.Root!.DescendantsAndSelf())
        {
            var name = element.Name.LocalName;
            var typeName = name.Split('.')[0];
            if (IsLauncherNamespace(element.Name.Namespace) && Names.TryGetValue(typeName, out var current))
            {
                var oldNamespace = element.Name.Namespace;
                element.Name = (Native(current) ? presentation : oldNamespace) + (current + name[typeName.Length..]);
                if ((name is "MyCheckBox" or "MyRadioBox" or "MyRadioButton") && element.Attribute("Checked") is { } state)
                {
                    element.SetAttributeValue("IsChecked", state.Value);
                    state.Remove();
                }
                if (name == "MyListItem.Buttons") element.Name = oldNamespace + nameof(SelectionRow) + ".ButtonItems";
                if (name == "MyImage.Source") element.Name = oldNamespace + nameof(AssetImage) + ".Address";
                if (name == "MyImage" && element.Attribute("Source") is { } source)
                {
                    element.SetAttributeValue(nameof(AssetImage.Address), source.Value);
                    source.Remove();
                }
                if (name == "MyTextButton" && element.Attribute("IsTextAction") is null)
                    element.SetAttributeValue("IsTextAction", true);
                if (name == "MyScrollViewer")
                {
                    element.SetAttributeValue(oldNamespace + "ScrollInput.Enabled", true);
                    if (element.Attribute("DeltaMult") is { } mult)
                    {
                        element.SetAttributeValue(oldNamespace + "ScrollInput.DeltaMultiplier", mult.Value);
                        mult.Remove();
                    }
                }
                if (Native(current) && name == typeName && element.Attribute("Style") is null)
                    element.SetAttributeValue("Style", "{" + nativePrefix + ":DynamicResource Sy" + current + "}");
            }
            foreach (var attr in element.Attributes())
            {
                if (attr.IsNamespaceDeclaration || !Names.Keys.Any(legacy => attr.Value.Contains(legacy, StringComparison.Ordinal))) continue;
                if (attr.Name.LocalName is not ("TargetType" or "DataType" or "Property" or "TypeName" or "TypeArguments") &&
                    !attr.Value.StartsWith('{')) continue;
                if (attr.Value is "{StaticResource MyComboBoxItem}" or "{DynamicResource MyComboBoxItem}")
                    attr.Value = attr.Value.Replace("MyComboBoxItem", "SySelectOption", StringComparison.Ordinal);
                attr.Value = LegacyTypeReference().Replace(attr.Value, match =>
                {
                    var prefix = match.Groups["prefix"].Value;
                    return IsLauncherNamespace(element.GetNamespaceOfPrefix(prefix)) && Names.TryGetValue(match.Groups["type"].Value, out var newType)
                        ? (Native(newType) ? nativePrefix : prefix) + ":" + newType : match.Value;
                });
            }
        }
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}

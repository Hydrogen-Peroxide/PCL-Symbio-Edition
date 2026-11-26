using System.Windows;

namespace PCL;

/// <summary>统一的联机大厅，负责在 Java（陶瓦）与基岩版（PaperConnect）之间切换。</summary>
public partial class MultiplayerLobbyView
{
    private readonly LanWorkspaceView javaView;
    private readonly PaperConnectWorkspaceView bedrockView;

    public MultiplayerLobbyView(bool initialBedrock)
    {
        InitializeComponent();
        javaView = new LanWorkspaceView();
        bedrockView = new PaperConnectWorkspaceView();
        SelectEdition(initialBedrock);
    }

    private void BtnJava_Click(object sender, RoutedEventArgs e) => SelectEdition(false);

    private void BtnBedrock_Click(object sender, RoutedEventArgs e) => SelectEdition(true);

    private void SelectEdition(bool bedrock)
    {
        ContentHost.Content = bedrock ? bedrockView : javaView;
        BtnJava.ColorType = bedrock ? SymbioButton.ColorState.Normal : SymbioButton.ColorState.Highlight;
        BtnBedrock.ColorType = bedrock ? SymbioButton.ColorState.Highlight : SymbioButton.ColorState.Normal;
        LabModeHint.Text = bedrock
            ? "基岩版使用 PaperConnect。开房前请先在 Minecraft 世界中选择「对局域网开放」。"
            : "Java 版使用陶瓦联机。开房前请先启动 Minecraft 并进入世界。";
    }
}

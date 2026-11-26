using System.Windows;
using System.Windows.Controls;

namespace PCL;

/// <summary>
///     联机大厅外壳：左栏选 Java / 基岩版，右栏承载对应大厅。
///     两个大厅（陶瓦 / PaperConnect）各自保留自己的左侧操作面板，这里是版本维度的外层导航，
///     切换只做淡入、不重建，因此两边的定时器与连接状态都不会因为切走再切回而丢。
/// </summary>
public partial class MultiplayerWorkspaceView
{
    private LanWorkspaceView? javaLobby;
    private PaperConnectWorkspaceView? bedrockLobby;
    private bool suppressCheck;

    /// <summary>首次进入时默认选中的版本（跟随当前启动器版本模式）。</summary>
    public bool StartOnBedrock { get; set; }

    public MultiplayerWorkspaceView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Host.Content is not null) return;
            SwitchTo(StartOnBedrock, animate: false);
        };
    }

    private void Segment_Check(SymbioSegment segment, bool isChecked)
    {
        if (!isChecked || suppressCheck) return;
        SwitchTo(ReferenceEquals(segment, SegBedrock), animate: true);
    }

    private void SwitchTo(bool bedrock, bool animate)
    {
        FrameworkElement target;
        if (bedrock)
        {
            bedrockLobby ??= new PaperConnectWorkspaceView();
            target = bedrockLobby;
        }
        else
        {
            javaLobby ??= new LanWorkspaceView();
            target = javaLobby;
        }

        if (!ReferenceEquals(Host.Content, target))
        {
            Host.Content = target;
            if (animate && IsLoaded)
            {
                Host.Opacity = 0;
                SymbioMotion.Fade(Host, 1);
            }
        }

        LabRail.Text = bedrock
            ? "基岩版大厅：联机码 / 局域网发现 / 我的服务器。"
            : "Java 版大厅：陶瓦联机，房间码加入与房间状态。";
        SyncSegments(bedrock);
    }

    private void SyncSegments(bool bedrock)
    {
        // 程序化切换时不要再回调 Segment_Check，否则会递归
        suppressCheck = true;
        try
        {
            SegJava.IsChecked = !bedrock;
            SegBedrock.IsChecked = bedrock;
        }
        finally
        {
            suppressCheck = false;
        }
    }
}

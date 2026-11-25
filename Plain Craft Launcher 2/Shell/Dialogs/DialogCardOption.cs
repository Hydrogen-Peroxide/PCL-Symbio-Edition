namespace PCL;

/// <summary>
///     卡片选择框中的单个选项。
/// </summary>
public class MyMsgCardOption
{
    /// <summary>
    ///     卡片标题。
    /// </summary>
    public string Title = "";

    /// <summary>
    ///     卡片副标题说明。
    /// </summary>
    public string Description = "";

    /// <summary>
    ///     卡片图标资源路径。
    /// </summary>
    public string SvgIcon = "lucide/box";

    /// <summary>
    ///     标题旁的小标签，留空则不显示。
    /// </summary>
    public string Tag = "";
}

using System.Windows.Media.Imaging;

namespace Tessalume.App;

public static class BrandInfo
{
    public static string ProductName { get; } =
        typeof(BrandInfo).Assembly.GetName().Name ?? "Application";

    public const string ChineseName = "万棱流光";
    public const string RepositoryOwner = "lyc1uckYoo";
    public const string RepositoryName = "tessalume";
    public static string Version { get; } =
        typeof(BrandInfo).Assembly.GetName().Version?.ToString(3) ?? "2.1.0";
    public static string VersionLabel { get; } = "v" + Version;
    public static string WindowTitle { get; } = ProductName + " · " + ChineseName;
    public static string QuickSwitchTitle { get; } = ProductName + " 浮窗";
    public static string ProtocolClientName { get; } = ProductName.ToLowerInvariant();
    public static BitmapFrame AppIcon { get; } = LoadLargestIconFrame();
    public static BitmapImage AppLogo { get; } = LoadImage("png");
    public static string OpenMainWindowTooltip { get; } = "打开 " + ProductName + " 主界面";
    public static string RuntimeSettingsDescription { get; } = "管理 " + ProductName + " 在当前 Windows 用户下的运行方式。";
    public static string StartupDescription { get; } = "默认关闭；只有主动开启后，登录 Windows 才会自动打开 " + ProductName + "。此项仅写入当前用户的启动项，不需要管理员权限。";
    public static string UpdateDescription { get; } = "通过官方 GitHub Releases 检查软件和兼容规则；完整更新保留一个可恢复的上一版本，主题运行仍只连接本机。";
    public static string PortableDataDescription { get; } = ProductName + " 不会修改 Codex 安装文件；主题和界面设置都保存在程序旁边的便携目录。";

    private static BitmapFrame LoadLargestIconFrame()
    {
        var decoder = BitmapDecoder.Create(
            GetAssetUri("ico"),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("The application icon contains no image frames.");
        }
        var frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).First();
        if (frame.CanFreeze && !frame.IsFrozen)
        {
            frame.Freeze();
        }
        return frame;
    }

    private static BitmapImage LoadImage(string extension)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = GetAssetUri(extension);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Uri GetAssetUri(string extension) => new(
        $"pack://application:,,,/{ProductName};component/Assets/{ProductName}.{extension}",
        UriKind.Absolute);
}

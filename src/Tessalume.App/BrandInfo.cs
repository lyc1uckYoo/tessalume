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
        typeof(BrandInfo).Assembly.GetName().Version?.ToString(3) ?? "1.4.1";
    public static string VersionLabel { get; } = "v" + Version;
    public static string WindowTitle { get; } = ProductName + " · " + ChineseName;
    public static string QuickSwitchTitle { get; } = ProductName + " 浮窗";
    public static string ProtocolClientName { get; } = ProductName.ToLowerInvariant();
    public static BitmapImage AppIcon { get; } = LoadImage("ico");
    public static BitmapImage AppLogo { get; } = LoadImage("png");
    public static string OpenMainWindowTooltip { get; } = "打开 " + ProductName + " 主界面";
    public static string RuntimeSettingsDescription { get; } = "管理 " + ProductName + " 在当前 Windows 用户下的运行方式。";
    public static string StartupDescription { get; } = "默认关闭；只有主动开启后，登录 Windows 才会自动打开 " + ProductName + "。此项仅写入当前用户的启动项，不需要管理员权限。";
    public static string UpdateDescription { get; } = "通过官方 GitHub Releases 自动检查、校验并安装新版本；主题运行仍只连接本机。";
    public static string PortableDataDescription { get; } = ProductName + " 不会修改 Codex 安装文件；主题和界面设置都保存在程序旁边的便携目录。";

    private static BitmapImage LoadImage(string extension)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(
            $"pack://application:,,,/{ProductName};component/Assets/{ProductName}.{extension}",
            UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

using System;
using System.IO;
using PCL.Core.Utils.OS;
using Special = System.Environment.SpecialFolder;

namespace PCL.Core.App;

/// <summary>
/// Application global paths.<br/>
/// <b>NOTE</b>: The behaviors of all path strings depends on <see cref="Path"/> API provided by
/// .NET standard library. You should use <see cref="Path"/> and other APIs relative to it to process
/// any path string from this service, rather than concat paths manually.
/// </summary>
public static class Paths
{
    /// <summary>
    /// The default directory used for relative path combining.
    /// </summary>
    public static string DefaultDirectory => Basics.ExecutableDirectory;

    private static string _data;
    private static string _sharedData;
    private static string _sharedLocalData;
    private static string _temp;
    
    /// <summary>
    /// Per-instance data directory.
    /// </summary>
    public static string Data { get => _data; set => _data = value; }

    /// <summary>
    /// Shared synchronized data directory.
    /// </summary>
    public static string SharedData { get => _sharedData; set => _sharedData = value; }
    
    /// <summary>
    /// Shared synchronized data directory of old versions.<br/>
    /// Keep the value just for migration, DO NOT USE IT.
    /// </summary>
    public static string OldSharedData { get; set; }

    /// <summary>
    /// Shared local data directory, used to put some large files that can be released or downloaded back anytime.
    /// </summary>
    public static string SharedLocalData { get => _sharedLocalData; set => _sharedLocalData = value; }
    
    /// <summary>
    /// Temporary files directory (can be deleted anytime, except when the program is running).
    /// </summary>
    public static string Temp { get => _temp; set => _temp = value; }

    /// <summary>
    /// Get path string relative to a special folder.
    /// </summary>
    /// <param name="folder">the special folder</param>
    /// <param name="relative">the relative path</param>
    /// <returns>the path string relative to the special folder</returns>
    public static string GetSpecialPath(Special folder, string relative)
    {
        var folderPath = Environment.GetFolderPath(folder);
        return Path.Combine(folderPath, relative);
    }

    static Paths()
    {
#if DEBUG
        const string name = "PCLSE_Debug";
        const string oldName = ".PCLSEDebug";
#else
        const string name = "PCLSE";
        const string oldName = ".PCLSE";
#endif
        // fill paths
        _data = Path.Combine(DefaultDirectory, "PCL");
        _sharedData = GetSpecialPath(Special.ApplicationData, name);
        _sharedLocalData = GetSpecialPath(Special.LocalApplicationData, name);
        _temp = Path.Combine(Path.GetTempPath(), name);
        OldSharedData = GetSpecialPath(Special.ApplicationData, oldName);
#if DEBUG
        // read environment variables
        EnvironmentInterop.ReadVariable("PCL_PATH", ref _data);
        EnvironmentInterop.ReadVariable("PCL_PATH_SHARED", ref _sharedData);
        EnvironmentInterop.ReadVariable("PCL_PATH_LOCAL", ref _sharedLocalData);
        EnvironmentInterop.ReadVariable("PCL_PATH_TEMP", ref _temp);
#endif
        // 受限目录（只读 ACL、沙箱、只读同步盘）下 AppData 无法写入时，
        // 把共享配置与临时文件挪到 exe 旁边，避免每次保存配置都弹错误。
        if (!CanWriteDirectory(_sharedData))
        {
            var oldShared = _sharedData;
            _sharedData = Path.Combine(_data, "portable", "shared");
            _sharedLocalData = Path.Combine(_data, "portable", "local");
            Directory.CreateDirectory(_sharedData);

            // 加密密钥必须沿用旧位置的那一份，否则已有档案 / 配置会解密失败
            try
            {
                var oldKey = Path.Combine(oldShared, "UserKey.bin");
                if (File.Exists(oldKey))
                    File.Copy(oldKey, Path.Combine(_sharedData, "UserKey.bin"), true);
            }
            catch (Exception)
            {
                // 读取不到旧密钥时只能使用新密钥，后续档案读取失败会由上层提示
            }

            // 尽量保留旧配置，避免回退后丢失设置
            try
            {
                var oldConfig = Path.Combine(oldShared, "config.v1.json");
                var newConfig = Path.Combine(_sharedData, "config.v1.json");
                if (File.Exists(oldConfig) && !File.Exists(newConfig))
                    File.Copy(oldConfig, newConfig);
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        if (!CanWriteDirectory(_temp))
            _temp = Path.Combine(_data, "temp");

        // create directories
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_sharedData);
        Directory.CreateDirectory(_sharedLocalData);
        Directory.CreateDirectory(_temp);
    }

    /// <summary>
    ///     检查目录是否可写（能创建并写入一个探测文件）。
    /// </summary>
    private static bool CanWriteDirectory(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".pcl_write_probe");
            using (var stream = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.Read))
                stream.WriteByte(0);
            try
            {
                File.Delete(probe);
            }
            catch (Exception)
            {
                // 无法删除探测文件时忽略，不影响配置保存
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

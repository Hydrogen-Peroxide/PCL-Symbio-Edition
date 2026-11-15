using System;
using System.IO;
using PCL.Core.Utils;

namespace PCL.Core.App.Configuration.Storage;

public abstract class CommonFileProvider(string path) : IKeyValueFileProvider
{
    public string FilePath { get; set; } = path;

    public abstract T Get<T>(string key);
    public abstract void Set<T>(string key, T value);
    public abstract bool Exists(string key);
    public abstract void Remove(string key);

    protected abstract void WriteToStream(Stream stream);

    public void Sync()
    {
        if (!File.Exists(FilePath)) Directory.CreateDirectory(Basics.GetParentPath(FilePath)!);
        var tmpFile = $"{FilePath}.tmp{RandomUtils.NextInt(1, 99999):00000}";
        var bakFile = $"{FilePath}.bak";
        using (var stream = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            WriteToStream(stream);
            stream.Flush(true);
        }

        try
        {
            if (File.Exists(FilePath)) File.Replace(tmpFile, FilePath, bakFile);
            else File.Move(tmpFile, FilePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 部分目录（只读 ACL、同步盘、沙箱）不允许删除或重命名其中的文件，
            // 此时退化为直接覆盖目标文件，避免整个配置保存失败。
            using (var source = new FileStream(tmpFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                source.CopyTo(target);
            }

            try
            {
                File.Delete(tmpFile);
            }
            catch (Exception)
            {
                // 临时文件无法删除时忽略，下次保存会重新生成
            }
        }
    }
}

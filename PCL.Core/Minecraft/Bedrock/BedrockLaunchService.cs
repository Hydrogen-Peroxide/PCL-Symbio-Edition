using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCL.Core.Minecraft.Bedrock;

/// <summary>
/// 基岩版启动服务。
/// </summary>
public class BedrockLaunchService
{
    private readonly BedrockInstance _instance;
    private Stopwatch? _gameplayStopwatch;
    private DateTime _gameStartTime;

    public Process? MinecraftProcess { get; private set; }

    /// <summary>
    /// 启动完成回调。
    /// </summary>
    public Action? LaunchCompleted { get; set; }

    /// <summary>
    /// 游戏进程启动回调。
    /// </summary>
    public Action<Process>? Launched { get; set; }

    /// <summary>
    /// 进度文本更新。
    /// </summary>
    public Action<string>? UpdateProgressText { get; set; }

    public BedrockLaunchService(BedrockInstance instance)
    {
        _instance = instance;
        _gameplayStopwatch = new Stopwatch();
    }

    /// <summary>
    /// 启动基岩版。
    /// </summary>
    public async Task LaunchAsync()
    {
        try
        {
            _gameplayStopwatch!.Reset();
            _gameStartTime = DateTime.Now;

            if (_instance.IsUwp)
            {
                UpdateProgressText?.Invoke("正在启动 UWP 版本...");
                LaunchUwp();
            }
            else if (_instance.IsGdk)
            {
                UpdateProgressText?.Invoke("正在启动 GDK 版本...");
                LaunchGdk();
            }
            else
            {
                Debug.WriteLine($"[Bedrock] 未知的构建类型: {_instance.BuildType}");
                LaunchCompleted?.Invoke();
                return;
            }

            if (MinecraftProcess != null && !MinecraftProcess.HasExited)
            {
                _gameplayStopwatch.Start();
                UpdateProgressText?.Invoke("游戏已启动");
                Launched?.Invoke(MinecraftProcess);

                await WaitForProcessExitAsync(MinecraftProcess);
            }
            else
            {
                // 对于 UWP 版本，尝试通过进程名查找
                var uwpProcess = FindUwpProcess();
                if (uwpProcess != null)
                {
                    MinecraftProcess = uwpProcess;
                    _gameplayStopwatch.Start();
                    UpdateProgressText?.Invoke("游戏已启动");
                    Launched?.Invoke(uwpProcess);
                    await WaitForProcessExitAsync(uwpProcess);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bedrock] 启动失败: {ex.Message}");
        }
        finally
        {
            if (_gameplayStopwatch!.IsRunning)
                _gameplayStopwatch.Stop();

            UpdatePlayTime();
            LaunchCompleted?.Invoke();
        }
    }

    private void LaunchUwp()
    {
        // 通过 minecraft:// 协议 URI 启动 UWP 版本
        MinecraftProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "minecraft://",
                Verb = "open"
            }
        };
        MinecraftProcess.Start();
    }

    private void LaunchGdk()
    {
        var exePath = _instance.ExecutablePath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            UpdateProgressText?.Invoke("未找到游戏可执行文件");
            MinecraftProcess = null;
            return;
        }

        MinecraftProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _instance.InstallPath,
                UseShellExecute = true,
                Arguments = _instance.OtherCommand ?? string.Empty
            },
            EnableRaisingEvents = true
        };
        MinecraftProcess.Start();
    }

    /// <summary>
    /// 查找已运行的 UWP Minecraft 进程。
    /// </summary>
    private static Process? FindUwpProcess()
    {
        return Process.GetProcessesByName("Minecraft.Windows")
            .Concat(Process.GetProcessesByName("Minecraft.Windows.exe"))
            .FirstOrDefault();
    }

    private static async Task WaitForProcessExitAsync(Process process)
    {
        try
        {
            await Task.Run(() => process.WaitForExit());
        }
        catch
        {
            // 进程已退出或无法访问
        }
    }

    private void UpdatePlayTime()
    {
        if (!_gameplayStopwatch!.IsRunning) return;

        var elapsed = _gameplayStopwatch.Elapsed;
        _instance.TotalPlayTime += (long)elapsed.TotalSeconds;
        _instance.LastPlayTime = DateTime.Now;
        _instance.TotalSessions++;

        SaveInstanceConfig();
    }

    private void SaveInstanceConfig()
    {
        var configDir = Path.Combine(
            Path.GetDirectoryName(_instance.InstallPath) ?? string.Empty,
            ".bedrock");
        var configPath = Path.Combine(configDir, $"{_instance.Version}.json");
        var json = JsonSerializer.Serialize(_instance, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }
}
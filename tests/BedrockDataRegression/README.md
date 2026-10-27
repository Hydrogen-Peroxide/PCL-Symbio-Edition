# 基岩版数据安全回归测试

使用仓库 `global.json` 指定的 .NET 10 SDK，在 **Windows** 仓库根目录执行：

```powershell
dotnet run --project tests/BedrockDataRegression -c Release
```

此宿主直接编译生产代码 `BedrockFileSystem.cs`、`BedrockIsolationManager.cs`、`BedrockInstallTransaction.cs` 与 `BedrockPackageRegistration.cs`，不引用启动器或任何第三方 NuGet 包，也不链接读取 Secrets、真实 APPDATA 或探测游戏进程的生产包装层。游戏运行状态由测试注入。

测试在一个 GUID 命名的临时根目录中创建真实目录、文件和 `mklink /J` junction，覆盖：

- 安装包相对路径、Windows 保留名、ADS、目录越界和重解析点拒绝。
- 隔离目录的归属、重启恢复、旧会话回调、目标冲突、待启动会话保护、游戏运行保护与失败回滚。
- 新安装与版本替换、校验失败和取消、运行中拒绝提交、用户目录保留和旧版本备份。
- 包身份与安装位置的精确归属、保留数据注销、失败中止和注销后的状态复查。

包注册用例在独立 PowerShell 进程中预先定义 `Get-AppxPackage` / `Remove-AppxPackage` mock 函数，并用 `Get-Command` 确认它们是当前解析到的 `Function`，然后直接执行生产代码生成的脚本。包列表、注销调用和验证结果均为内存 fixture，不查询真实包注册，也不执行系统 Appx cmdlet。测试核验路径中的单引号、`$`、`%`、`&` 按字面保留，以及仅注销身份和 `InstallLocation` 均匹配的对象，并带上 `-PreserveApplicationData`。

Windows CI 将两组测试作为独立作业执行，本地也可单独运行：

```powershell
dotnet run --project tests/BedrockDataRegression -c Release -- --suite data
dotnet run --project tests/BedrockDataRegression -c Release -- --suite registration
```

测试不需要 Minecraft、账户或管理员权限。仅允许在临时根中创建 junction，清理前再次校验绝对路径，并逐层删除；遇到 junction 只删除联接本身，不遍历目标。Linux 不运行本宿主。测试通过不代表已完成真实游戏启动、真实包注册或真实 GDK 安装包解密验收。

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BedrockDataRegression;

internal sealed record PackageFixture(string Name, string PackageFullName, string InstallLocation);
internal sealed record PackageRemoval(string PackageFullName, bool PreserveApplicationData, string ErrorAction);

internal sealed class PackageScriptResult
{
    public string[] Output { get; set; } = [];
    public PackageFixture[] Remaining { get; set; } = [];
    public PackageRemoval[] Removed { get; set; } = [];
    public int QueryCount { get; set; }
    public string? Error { get; set; }
}

internal sealed class PowerShellPackageFixture(PackageFixture[] packages, string failure = "")
{
    internal PackageScriptResult? LastResult { get; private set; }

    internal string Execute(string productionScript)
    {
        var fixture = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { Packages = packages, Failure = failure }));
        // All package state is in this new process's memory. The real Appx cmdlets are never invoked.
        var script = """
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $fixture = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__FIXTURE__')) | ConvertFrom-Json
            $script:Packages = @($fixture.Packages)
            $script:Failure = [string]$fixture.Failure
            $script:Removed = [Collections.ArrayList]::new()
            $script:QueryCount = 0
            function Get-AppxPackage {
                [CmdletBinding()]
                param([string]$Name)
                $script:QueryCount++
                if ($script:Failure -eq 'Query') { throw 'Mock package query failed' }
                @($script:Packages | Where-Object { $_.Name -eq $Name })
            }
            function Remove-AppxPackage {
                [CmdletBinding()]
                param([string]$Package, [switch]$PreserveApplicationData)
                [void]$script:Removed.Add([pscustomobject]@{
                    PackageFullName = $Package
                    PreserveApplicationData = [bool]$PreserveApplicationData
                    ErrorAction = [string]$PSBoundParameters['ErrorAction']
                })
                if (-not $PreserveApplicationData) { throw 'Mock refuses removal without data preservation' }
                if ($script:Failure -eq 'Remove') { throw 'Mock package removal failed' }
                if ($script:Failure -eq 'KeepRegistration') { return }
                $script:Packages = @($script:Packages | Where-Object { $_.PackageFullName -ne $Package })
            }
            foreach ($name in @('Get-AppxPackage', 'Remove-AppxPackage')) {
                $command = Get-Command -Name $name -ErrorAction Stop
                if ($command.CommandType -ne [Management.Automation.CommandTypes]::Function -or $command.ModuleName) {
                    throw 'Package mocks are not the active command bindings'
                }
            }
            $result = @()
            $errorMessage = $null
            try { $result = @(& { __PRODUCTION__ }) }
            catch { $errorMessage = $_.Exception.Message }
            [ordered]@{
                Output = @($result | ForEach-Object { [string]$_ })
                Remaining = @($script:Packages)
                Removed = @($script:Removed)
                QueryCount = $script:QueryCount
                Error = $errorMessage
            } | ConvertTo-Json -Depth 8 -Compress
            """.Replace("__FIXTURE__", fixture).Replace("__PRODUCTION__", productionScript);

        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start controlled package-script test.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Controlled package-script test timed out.");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new IOException("Controlled package-script harness failed: " + error.Result);
        LastResult = JsonSerializer.Deserialize<PackageScriptResult>(output.Result.Trim())
            ?? throw new IOException("Controlled package-script test did not return state.");
        if (LastResult.Error is not null) throw new IOException(LastResult.Error);
        return string.Join(Environment.NewLine, LastResult.Output);
    }
}

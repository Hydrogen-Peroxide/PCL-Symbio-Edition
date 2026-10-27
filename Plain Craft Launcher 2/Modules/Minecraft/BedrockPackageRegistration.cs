using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PCL;

/// <summary>Limits package queries/removal to the identity and exact location of one portable version.</summary>
internal sealed class BedrockPackageRegistration(Func<string, string> execute)
{
    internal bool IsRegisteredFromDirectory(string packageName, string directory)
    {
        var query = OwnedPackageQuery(packageName, directory);
        var result = execute(query + "; if (@(Get-OwnedPackages).Count -gt 0) { 'Owned' } else { 'Absent' }").Trim();
        return result switch
        {
            "Owned" => true,
            "Absent" => false,
            _ => throw new IOException("未能确认基岩版包的注册位置。")
        };
    }

    internal void UnregisterFromDirectory(string packageName, string directory)
    {
        var query = OwnedPackageQuery(packageName, directory);
        var result = execute(query +
            "; foreach ($package in @(Get-OwnedPackages)) { " +
            "Remove-AppxPackage -Package $package.PackageFullName -PreserveApplicationData -ErrorAction Stop }; " +
            "if (@(Get-OwnedPackages).Count -ne 0) { throw 'Package registration still points to this directory' }; 'Cleared'").Trim();
        if (result != "Cleared") throw new IOException("未能确认已安全注销此基岩版，已保留安装目录。");
    }

    internal static string? ReadIdentity(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;
        var document = XDocument.Load(manifestPath);
        var name = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "Identity")
            ?.Attribute("Name")?.Value;
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z"))
            throw new InvalidDataException("安装清单未包含有效的基岩版包标识。");
        return name;
    }

    private static string OwnedPackageQuery(string packageName, string directory)
    {
        if (!Regex.IsMatch(packageName, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z"))
            throw new ArgumentException("无效的基岩版包标识。", nameof(packageName));
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return "$ErrorActionPreference = 'Stop'; $expected = " + Literal(expected) +
               "; function Get-OwnedPackages { @(Get-AppxPackage -Name " + Literal(packageName) +
               " -ErrorAction Stop | Where-Object { $_.InstallLocation -and " +
               "[string]::Equals([IO.Path]::GetFullPath([string]$_.InstallLocation).TrimEnd([char]'\\'), " +
               "$expected, [StringComparison]::OrdinalIgnoreCase) }) }";
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}

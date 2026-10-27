[简体中文](README.md) | **English** | [繁體中文](README-ZH_TW.md)

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Symbio Edition

[![Stars](https://img.shields.io/github/stars/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&logo=github&label=stars&labelColor=444444&color=eac54f)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/stargazers)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Hydrogen-Peroxide/PCL-Symbio-Edition/build-test.yml?style=for-the-badge)

[![Pull requests](https://img.shields.io/github/issues-pr/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&label=pull%20requests&labelColor=444444&color=1F883D&logo=github)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/pulls)

[Download PCL SE](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases) |
[Upstream repository](https://github.com/Meloong-Git/PCL)

[Submit issues](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/issues) |
[Contribution guidelines](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/CONTRIBUTING.md)

</div>

PCL SE (Plain Craft Launcher Symbio Edition) is a third-party Minecraft launcher maintained by Hydrogen-Peroxide. Built on PCL's open-source code, it keeps the familiar interaction while adding Bedrock edition support, instance management and resource downloads.

- Current version: `2.17.11`
- Upstream baseline: PCL `2.12.1`
- Tech stack: C# / WPF / .NET 10

PCL SE has no affiliation with the official PCL, so please do not report PCL SE issues to the official repository.

## Features

### Java Edition

- One-click installation of official releases, snapshots and historical versions, with automatic dependency completion and Java runtime selection
- 8 installation options: Forge, NeoForge, Fabric, Legacy Fabric, OptiFine, LiteLoader, Cleanroom, LabyMod
- Modpack installation and mod loader switching

### Bedrock Edition

- Detects the Minecraft Bedrock editions installed on your system, distinguishing legacy UWP builds from GDK builds
- Imports installation packages and unpacks them into a separate directory without affecting other versions
- Built-in Xbox account sign-in, so you can launch right after logging in
- Filter by version type, search by version number and manage local installs

### Resource downloads

- Dual-source search across CurseForge and Modrinth
- Covers mods, modpacks, shaders, resource packs, datapacks and worlds, placing downloads into the correct directories automatically

### Instance management

- Each instance keeps its own mods, saves, screenshots and configuration
- Disable individual mods at any time to troubleshoot conflicts
- Browse and back up saves and screenshots, and open their folders in one click
- Keep a list of frequently used servers per instance and export the whole instance as an archive

### User experience

- Complete interface resources for Simplified Chinese and English (US); Traditional Chinese, English (UK), Japanese, French and Spanish currently contain only language metadata, with interface text falling back to Simplified Chinese
- Customizable color theme and background, with transition animations for page switching and list scrolling
- Automatic Java scanning to reduce manual configuration
- Log analysis on failed launches, with readable causes and suggestions

> This fork does not include the multiplayer lobby; the related modules have been removed from the codebase, and there is no confirmed schedule for restoring them.

## Supported platforms

| Operating System | Support Status | Requirements |
|---|---|---|
| Windows 10 1809 (17763) or later | Fully supported | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 to Windows 10 1809 (17763) | Expected to run; support offered at discretion | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 or earlier | Not supported | N/A |
| macOS / Linux / Other OS | Cross-compilation only; cannot run directly | [.NET 10 SDK](https://get.dot.net/10) |

**Fully supported**: We will provide as much support as possible, but you must ensure you are using the latest version of the launcher.

**Expected to run; support offered at discretion**: PCL SE is expected to run on these platforms, but full functionality is not guaranteed. You may need to upgrade to a fully supported OS version to receive further technical support.

**Not supported**: PCL SE may not be usable on these platforms and might not even launch at all. Please upgrade your OS to use PCL SE.

**Cross-compilation only**: The source code can be built on macOS and Linux but cannot run there directly. As a developer, you can develop on these platforms and move the build artifacts to Windows for testing.

**Note**: Only the latest version of the launcher is supported. Depending on the nature of certain issues (e.g., an incomplete system environment), you may still need to upgrade your OS to continue receiving support. You can still try using PCL SE on unsupported platforms, but you may encounter many additional problems.

## Download and usage

1. Download the latest release from [Releases](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases)
2. Extract it to any directory (the launcher is portable and does not write to system directories)
3. Run `Plain Craft Launcher 2.exe`, pick Java or Bedrock edition and launch the game

Official release builds are framework-dependent and require the [.NET 10 Desktop Runtime](https://get.dot.net/10); self-contained builds you compile yourself do not.

## Building from source

Requires the [.NET 10 SDK](https://get.dot.net/10). [global.json](global.json) selects SDK `10.0.403` and allows patch updates within the same feature band; CI installs that baseline version. Run the following in the repository root:

```powershell
# Framework-dependent publish, matching CI
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained

# Self-contained single-file publish (bundles the runtime, much larger output)
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true --self-contained true -o ./publish
```

Replace `-p:Platform=x64` with `-p:Platform=ARM64` to build for ARM64.

Available configurations (`-c`): `Release`, `Beta`, `Debug`, `CI`. Available platforms (`-p:Platform`): `x64`, `ARM64`, `AnyCPU`.

See the [contribution guide](CONTRIBUTING.md#本地构建与测试) for test commands, the offline test scope and release configuration. The solution does not build the core test project by default; run that project explicitly.

## Project structure

| Directory | Description |
|---|---|
| `Plain Craft Launcher 2/` | WPF application: UI, pages and Minecraft launching logic |
| `PCL.Core/` | Core library: configuration, networking and downloads, Java, saves, animation, localization, logging |
| `PCL.Core.SourceGenerators/` | Source generators for configuration and dependency injection |
| `PCL.Core.Test/` | Unit tests for PCL.Core |
| `.github/` | CI build and release workflows |

## Differences from upstream

- Added Bedrock edition management: version detection, package import, Xbox sign-in and local filtering
- Removed the multiplayer lobby and its modules; no lobby or multiplayer entry points are provided
- Uses an independent version number (`2.17.11`) based on PCL `2.12.1`

## Legal notice

> These notices are also shown in the "About this version" dialog after the launcher updates.

- **Project nature**: PCL Symbio Edition is an independent fork derived from PCL Community Edition (PCL CE) and is maintained by an individual developer for the international edition of Minecraft. It is not affiliated with the official PCL2 project maintained by LTCatt (龙腾猫跃), nor with the official PCL CE project maintained by PCL-Community.
- **Scope**: PCL Symbio Edition does not support Minecraft China Edition (「我的世界中国版」), which is operated by Mojang Studios' agent in mainland China, and provides no features or adaptations for it.
- **APIs**: PCL Symbio Edition accesses Minecraft international services only through APIs approved by Mojang Studios.
- **Code origin**: This project contains no Minecraft code (including Minecraft China Edition) and no code from Mojang Studios or its agents in China.
- **Name usage**: The word "Minecraft" appears in documentation and demonstrations solely to describe the game this project supports, which constitutes fair use under the Copyright Law of the People's Republic of China. Copyright in related screenshots and videos belongs to their respective developers.
- **Relationships**: PCL Symbio Edition has no affiliation, partnership, maintenance or licensing relationship with PCL2, PCL CE, Mojang Studios or its agents in China.
- **Genuine copies**: We encourage buying and playing genuine copies of Minecraft through official channels.
- **Multiplayer lobby**: PCL Symbio Edition currently does not provide the PCL CE multiplayer lobby. The related modules have been removed, and there is no confirmed schedule for restoring them.

## License

- `Plain Craft Launcher 2/` uses a [custom license](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- All other directories use [Apache License 2.0](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/LICENSE)

## Acknowledgements

- [PCL](https://github.com/Meloong-Git/PCL) — original author 龙腾猫跃 (LTCat)
- PCL CE — community-maintained branch and the direct upstream of this project
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) and [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Bedrock projects by Round Studio (GPL-3.0), porting reference for this project's Bedrock installation, decryption and launching; see [NOTICE](NOTICE) for scope
- Plus open-source projects such as Dapper, Downloader, Markdig.Wpf, NAudio, Microsoft.Data.Sqlite, protobuf-net and Lucide. See [metadata.json](Plain%20Craft%20Launcher%202/metadata.json) and the launcher's "About" page for the complete list of third-party components and licenses

## Statistics

[![Star History Chart](https://api.star-history.com/svg?repos=Hydrogen-Peroxide/PCL-Symbio-Edition&type=Date)](https://www.star-history.com/#Hydrogen-Peroxide/PCL-Symbio-Edition&Date)

**Views** (Total / Today): [![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition)](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition&web=true)

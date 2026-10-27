[简体中文](README.md) | [English](README-EN.md) | **繁體中文**

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Symbio Edition

[![Stars](https://img.shields.io/github/stars/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&logo=github&label=stars&labelColor=444444&color=eac54f)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/stargazers)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Hydrogen-Peroxide/PCL-Symbio-Edition/build-test.yml?style=for-the-badge)

[![Pull requests](https://img.shields.io/github/issues-pr/Hydrogen-Peroxide/PCL-Symbio-Edition?style=for-the-badge&label=pull%20requests&labelColor=444444&color=1F883D&logo=github)](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/pulls)

[下載 PCL SE](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases) |
[上游儲存庫](https://github.com/Meloong-Git/PCL)

[提交問題](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/issues) |
[貢獻指南](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/CONTRIBUTING.md)

</div>

PCL SE（Plain Craft Launcher Symbio Edition）是由 Hydrogen-Peroxide 維護的第三方 Minecraft 啟動器，基於 PCL 開源程式碼開發，在保留原有互動習慣的基礎上補齊了基岩版支援、實例管理與資源下載等能力。

- 目前版本：`2.17.11`
- 上游基線：PCL `2.12.1`
- 技術棧：C# / WPF / .NET 10

PCL SE 與 PCL 官方版沒有隸屬關係，請不要向官方儲存庫回報 PCL SE 的問題。

## 功能概覽

### Java 版

- 官方正式版、快照與歷史版本一鍵安裝，自動補全依賴並選擇合適的 Java 執行環境
- 8 種安裝選項：Forge、NeoForge、Fabric、Legacy Fabric、OptiFine、LiteLoader、Cleanroom、LabyMod
- 支援整合包安裝與模組載入器切換

### 基岩版

- 讀取系統中已安裝的 Minecraft 基岩版，區分 UWP 舊版與 GDK 新版
- 支援匯入安裝包並解包到獨立目錄，不影響其他版本
- 內建 Xbox 帳號登入，登入後可直接啟動
- 依版本類型篩選，支援依版本號搜尋與本機管理

### 資源下載

- CurseForge 與 Modrinth 雙來源檢索
- 涵蓋模組、整合包、光影、資源包、資料包與世界存檔，下載後自動放入對應目錄

### 實例管理

- 每個實例擁有獨立的模組、存檔、截圖與設定
- 單一模組可隨時停用，便於排查衝突
- 存檔與截圖集中瀏覽、備份，並可一鍵開啟所在目錄
- 實例內維護常用伺服器清單，支援整體打包匯出

### 使用體驗

- 簡體中文與 English (US) 提供完整介面資源；繁體中文、English (UK)、日本語、Français、Español 目前僅有語言名稱等預留資源，介面文字回退為簡體中文
- 主題配色與背景可自訂，頁面切換與清單捲動帶有過場動畫
- 自動掃描本機 Java，減少手動設定
- 啟動失敗時分析日誌，給出可讀的原因與建議

> 本分支不包含多人遊戲大廳功能，相關模組已從程式碼庫中移除，目前沒有已確認的恢復時程。

## 支援平台

| 作業系統 | 支援情況 | 環境需求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | 完整支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | 理論可執行，酌情提供支援 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | 不支援 | / |
| macOS / Linux / 其他作業系統 | 僅支援交叉編譯開發，無法直接執行 | [.NET 10 SDK](https://get.dot.net/10) |

**完整支援**：盡可能提供一切相關支援，但必須確保啟動器為最新版本。

**理論可執行，酌情提供支援**：PCL SE 應該可以在這些平台上執行，但不保證功能完全可用。你可能需要升級到完整支援的系統版本以獲得進一步技術支援。

**不支援**：PCL SE 在這些平台的可用性較低，甚至無法啟動。請升級作業系統以使用 PCL SE。

**僅支援交叉編譯開發**：原始碼可以在 macOS 與 Linux 上編譯，但無法直接執行。作為開發者，你可以在這些平台上開發，再將編譯產物轉移到 Windows 測試。

**註**：僅對最新版本的啟動器提供支援。取決於部分問題的特殊性（如系統不完整），有時你仍然必須升級作業系統以繼續獲得支援。你仍然可以嘗試在不受支援的系統上使用 PCL SE，但可能會遇到許多額外問題。

## 下載與使用

1. 從 [Releases](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/releases) 下載最新版本的發行包
2. 解壓縮到任意目錄（啟動器為免安裝版，不會寫入系統目錄）
3. 執行 `Plain Craft Launcher 2.exe`，選擇 Java 版或基岩版後即可啟動遊戲

官方發行包為框架依賴版本，需要預先安裝 [.NET 10 Desktop Runtime](https://get.dot.net/10)；若使用自包含方式自行建置，則無需額外安裝執行環境。

## 從原始碼建置

需要 [.NET 10 SDK](https://get.dot.net/10)。SDK 基線由 [global.json](global.json) 固定為 `10.0.403`，允許同一功能帶的修補更新；CI 安裝該基線版本。請在儲存庫根目錄執行：

```powershell
# 與 CI 一致的框架依賴發行
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true `
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained

# 自包含單檔發行（內建執行環境，產物體積更大）
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" `
  -c Release -p:Platform=x64 -p:PublishSingleFile=true --self-contained true -o ./publish
```

將 `-p:Platform=x64` 換成 `-p:Platform=ARM64` 即可建置 ARM64 版本。

可用設定（`-c`）：`Release`、`Beta`、`Debug`、`CI`；可用平台（`-p:Platform`）：`x64`、`ARM64`、`AnyCPU`。

測試命令、離線測試範圍與發行設定見 [貢獻指南](CONTRIBUTING.md#本地构建与测试)。解決方案預設不建置核心測試專案，需要單獨執行該專案。

## 專案結構

| 目錄 | 說明 |
|---|---|
| `Plain Craft Launcher 2/` | WPF 主程式：介面、頁面與 Minecraft 啟動邏輯 |
| `PCL.Core/` | 核心程式庫：設定、網路與下載、Java、存檔、動畫、在地化、日誌等 |
| `PCL.Core.SourceGenerators/` | 設定與相依性注入相關的來源產生器 |
| `PCL.Core.Test/` | PCL.Core 的單元測試 |
| `.github/` | CI 建置與發行工作流程 |

## 與上游的差異

- 新增基岩版管理：版本偵測、安裝包匯入、Xbox 帳號登入與本機篩選
- 移除多人遊戲大廳及配套模組，不再提供大廳與多人遊戲相關入口
- 使用獨立版本號（`2.17.11`），上游基線為 PCL `2.12.1`

## 聲明

> 以下聲明同時會在啟動器更新後的「版本說明」彈窗中展示。

- **專案性質**：PCL Symbio Edition 是從 PCL Community Edition（PCL CE）分支衍生而來的獨立版本，由個人開發者面向 Minecraft 國際版維護，與 LTCatt（龍騰貓躍）維護的 PCL2 官方專案、PCL-Community 維護的 PCL CE 官方專案均無隸屬關係。
- **支援範圍**：PCL Symbio Edition 不支援 Minecraft 遊戲開發商 Mojang Studios 在中國大陸的代理公司營運的「我的世界中國版」，也不提供與該版本相關的任何功能或適配。
- **API 使用**：PCL Symbio Edition 僅透過經 Mojang Studios 批准的 API 存取 Minecraft 國際版的相關服務。
- **程式碼來源**：本專案不包含任何 Minecraft（含「我的世界中國版」）的程式碼，也不包含 Mojang Studios 及其中國代理商的任何程式碼。
- **名稱使用**：文件與示範中出現的「Minecraft」字樣僅用於描述本專案所支援的遊戲，屬於《中華人民共和國著作權法》規定的合理使用；相關截圖與影片的著作權歸其各自開發者所有。
- **關係說明**：PCL Symbio Edition 與 PCL2、PCL CE、Mojang Studios 及其中國代理商之間不存在任何隸屬、合作、維護或授權關係。
- **正版倡議**：我們鼓勵透過官方管道購買並遊玩正版 Minecraft。
- **多人遊戲大廳**：PCL Symbio Edition 目前不提供 PCL CE 的多人遊戲大廳，相關模組已移除，目前沒有已確認的恢復時程。

## 授權條款

- `Plain Craft Launcher 2/` 使用 [自訂授權條款](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- 其餘所有目錄使用 [Apache License 2.0](https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition/blob/dev/LICENSE)

## 致謝

- [PCL](https://github.com/Meloong-Git/PCL) — 原作者 龍騰貓躍
- PCL CE — 社群維護分支，本專案的直接上游
- [BedrockBoot](https://github.com/Round-Studio/BedrockBoot) 與 [BedrockLauncher.Core](https://github.com/Round-Studio/BedrockLauncher.Core) — Round Studio 開發的基岩版專案（GPL-3.0），本專案基岩版安裝、解密與啟動方案的移植參考，來源與適用範圍見 [NOTICE](NOTICE)
- 以及 Dapper、Downloader、Markdig.Wpf、NAudio、Microsoft.Data.Sqlite、protobuf-net、Lucide 等開源專案，完整的第三方元件與授權資訊見 [metadata.json](Plain%20Craft%20Launcher%202/metadata.json) 與啟動器「關於」頁面

## 統計資料

[![Star History Chart](https://api.star-history.com/svg?repos=Hydrogen-Peroxide/PCL-Symbio-Edition&type=Date)](https://www.star-history.com/#Hydrogen-Peroxide/PCL-Symbio-Edition&Date)

**本頁瀏覽量**（總計 / 今日）：[![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition)](https://hits.zkitefly.eu.org/?tag=https://github.com/Hydrogen-Peroxide/PCL-Symbio-Edition&web=true)

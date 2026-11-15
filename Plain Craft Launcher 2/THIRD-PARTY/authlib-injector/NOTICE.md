# authlib-injector

- 版本：1.2.8（构建号 56，Git 提交 `5437f5b465ee3f5be0a1e5e1ae3c50978114f6fb`，以 jar 内 `META-INF/MANIFEST.MF` 为准）
- 来源：https://github.com/yushijinhun/authlib-injector
- 随启动器分发的文件：`Plain Craft Launcher 2/Resources/authlib-injector.jar`（未经修改的原始二进制，以 `<Resource Include="Resources\**" />` 嵌入启动器，运行时释放到启动器目录）
- 发行包内的位置：`THIRD-PARTY/authlib-injector/NOTICE.md`
- 许可证：GNU Affero General Public License v3.0（AGPL-3.0）

## 用途

自 2.17.15 起，离线档案可以使用本地皮肤与披风：启动时由内置的本地 Yggdrasil 服务
（`PCL.Core/Minecraft/Yggdrasil/LocalSkinServer.cs`）提供皮肤数据，`authlib-injector`
以 `-javaagent:` 参数注入游戏 JVM，把游戏的皮肤请求重定向到该本地服务。第三方登录
（authlib-injector 源）同样依赖该 jar。

相关文件：

- `Plain Craft Launcher 2/Resources/authlib-injector.jar` — 随包二进制，未被修改
- `Plain Craft Launcher 2/Modules/Minecraft/ModLocalSkin.cs` — 本地皮肤与披风的读写、模型选择
- `PCL.Core/Minecraft/Yggdrasil/LocalSkinServer.cs` — 内置本地 Yggdrasil 服务（HTTP）
- `Plain Craft Launcher 2/Modules/Minecraft/ModLaunch.cs` — 拼接 `-javaagent:` 启动参数

## 关于 AGPL 的例外条款

上游在 AGPL-3.0 基础上提供了以下例外（原文摘录）：

> 作为特例，如果您的程序通过以下方式利用本作品，则相应的行为不会导致您的作品被 AGPL 协议涵盖。
>
> - 您的程序通过打包的方式包含本作品未经修改的二进制形式，而没有静态或动态地链接到本作品；或
> - 您的程序通过本作品提供的进程间通信接口（如 HTTP API）进行交互；或
> - 您的程序将本作品作为 Java Agent 加载进 Java 虚拟机。

PCL Symbio Edition 的分发方式同时满足以上三项：

1. `Resources/authlib-injector.jar` 是未经修改的原始二进制，仅作为 WPF 资源嵌入，启动器没有静态或动态链接到它；
2. 启动器与本地皮肤服务之间通过 Yggdrasil（HTTP）协议交互，不依赖其内部实现；
3. 该 jar 仅通过 `-javaagent:` 参数作为 Java Agent 加载进游戏 JVM。

因此启动器本体不因打包该文件而受 AGPL 约束。完整的 AGPL-3.0 文本见上游仓库的
[LICENSE](https://github.com/yushijinhun/authlib-injector/blob/develop/LICENSE)，或
https://www.gnu.org/licenses/agpl-3.0.html 。

## 更新方式

启动器会优先使用随包版本；联网可用时按上游发布信息更新运行目录下的
`authlib-injector.jar`，随包版本用于首次离线启动等无法联网的场景。若你替换了该文件，
请自行确认替换后的版本仍符合上述例外条款，并同步更新本文件中的版本号。

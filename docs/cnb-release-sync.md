# Release 实时同步到 CNB

每次在 GitHub 发布 Release（稳定版或 Beta），附件传完后会自动镜像一份到
[CNB](https://cnb.cool/PCL-Symbio/PCL-Symbio-Edition)。不轮询、不定时任务，跟着发布流水线走。

## 一次性准备

1. **建 CNB 访问令牌**：<https://cnb.cool/profile/token/create>，授权范围勾选两项：

   | 授权范围 | 用途 |
   |---|---|
   | `代码仓库 repo-code` 读写 | `git push` 推 tag |
   | `Release repo-release` 读写 | 创建 Release、申请上传地址、确认上传、清理附件 |

   只给 `repo-code` 会在创建 Release 时报 403
   `Missing required scopes: repo-release:rw`。
2. **加到 GitHub 仓库**：Settings → Secrets and variables → Actions

   | 类型 | 名称 | 值 |
   |---|---|---|
   | Secret | `CNB_TOKEN` | 上一步的令牌 |
   | Variable（可选） | `CNB_REPO` | 目标仓库，默认 `PCL-Symbio/PCL-Symbio-Edition` |

3. 目标 CNB 仓库要先存在。`PCL-Symbio/PCL-Symbio-Edition` 已建好，但当前是空仓库
   （0 tag / 0 Release），第一次同步会自动把 tag 连同历史推上去。

## 触发时机

`release-stable_publish.yml` / `release-beta_publish.yml` 里，`rename_and_release`
（把 exe 和 `.asc` 签名传到 GitHub Release）跑完之后，`sync_cnb` 作业接着执行：

1. 确认 CNB 仓库里有这个 tag，没有就从本地推过去；
2. 按 tag 创建或更新 CNB Release —— 标题、说明、`prerelease` 标记、`make_latest` 都跟着 GitHub 走；
3. 从 GitHub 拉下该 Release 的**全部**附件，逐个上传到 CNB；
4. 删掉 CNB 上 GitHub 已经没有的附件（`prune` 默认开）。

整个流程幂等：重跑不会产生重复 Release，同名附件直接覆盖 —— 这点对「小修小补不升版本号、
直接重发同一版本」的习惯是必须的。

## 手动补同步

Actions → **Sync (CNB Release)** → Run workflow：

- 填 `tag`（如 `v2.17.15`）：只同步这一个；
- 留空：同步最近 `limit` 个（默认 3）；
- `prune` 关掉就只增不删。

历史版本一次性补齐时，把 `limit` 调大（比如 20）跑一次即可。

## 实现位置

| 文件 | 作用 |
|---|---|
| `.github/workflows/sync-cnb-release.yml` | 可复用工作流 + 手动触发入口 |
| `.github/scripts/sync_cnb_release.py` | 实际同步逻辑（只用 Python 标准库 + git / gh） |

## 常见问题

**401 / 403**：令牌无效或权限不够。403 时响应里会写
`Missing required scopes: xxx:rw`，照着去令牌里补对应授权范围即可
（同步至少要 `repo-code:rw` + `repo-release:rw`）。
**404**：`CNB_REPO` 写错，或令牌无权访问该仓库。
**推送 tag 失败**：CNB 仓库未初始化，或 tag 名包含非法字符。脚本会自动重试 3 次，
新仓库首次初始化可能要等几十秒。
**第一次同步很慢**：要把 tag 指向的历史（约 160 MB）推一次，之后每次只传增量。
**只想要文件不要代码**：tag 推送会顺带带上该 tag 的历史对象，这是 Git 的机制，
但 CNB 上不会出现分支，只有 tag 和 Release。

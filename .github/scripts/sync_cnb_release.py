#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 GitHub Release 同步到 CNB（cnb.cool）。

通常由 .github/workflows/sync-cnb-release.yml 调用，也可以本地手动跑：

    export CNB_TOKEN=...
    export CNB_REPO=PCL-Symbio/PCL-Symbio-Edition
    export GITHUB_REPOSITORY=Hydrogen-Peroxide/PCL-Symbio-Edition
    export TAG=v2.17.15            # 留空 = 批量同步最近几个
    python3 .github/scripts/sync_cnb_release.py

做的事：
  1. 确保 CNB 仓库里存在对应 tag（没有就从本地推过去）
  2. 按 tag 创建或更新 CNB Release（标题 / 说明 / 预发布标记）
  3. 下载 GitHub Release 的全部附件，逐个上传（同名覆盖）
  4. 可选：删掉 CNB 上 GitHub 已经没有的附件
  5. 生成启动器用的更新清单（正式版 latest.json / 预发布版 latest-beta.json），
     挂到固定 tag（默认 updates）的 Release 上

只依赖 Python 标准库 + git / gh 命令。
"""

from __future__ import annotations

import hashlib
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

ACCEPT = "application/vnd.cnb.api+json"
USER_AGENT = "pcl-cnb-release-sync/1.0"


class SyncError(RuntimeError):
    """同步过程中的可预期错误。"""


# ---------------------------------------------------------------------------
# 配置
# ---------------------------------------------------------------------------


def _env(name: str, default: str = "") -> str:
    return (os.environ.get(name) or default).strip()


def _normalize_repo(value: str) -> str:
    for prefix in ("http://", "https://"):
        if value.startswith(prefix):
            value = value[len(prefix):]
    host = _env("CNB_HOST", "cnb.cool")
    if value.startswith(host + "/"):
        value = value[len(host) + 1:]
    if value.endswith(".git"):
        value = value[: -len(".git")]
    return value.strip("/")


CNB_API = _env("CNB_API", "https://api.cnb.cool").rstrip("/")
CNB_HOST = _env("CNB_HOST", "cnb.cool")
CNB_REPO = _normalize_repo(_env("CNB_REPO", "PCL-Symbio/PCL-Symbio-Edition"))
CNB_TOKEN = _env("CNB_TOKEN")
GH_REPO = _env("GITHUB_REPOSITORY")
TAG = _env("TAG")
try:
    BATCH_LIMIT = max(1, int(_env("BATCH_LIMIT", "3") or "3"))
except ValueError:
    BATCH_LIMIT = 3
PRUNE = _env("PRUNE", "true").lower() == "true"
WRITE_MANIFEST = _env("WRITE_MANIFEST", "true").lower() == "true"

RELEASES_URL = f"https://{CNB_HOST}/{CNB_REPO}/-/releases"


# ---------------------------------------------------------------------------
# 日志 / 命令
# ---------------------------------------------------------------------------


def log(message: str) -> None:
    print(f"[cnb-sync] {message}", file=sys.stderr, flush=True)


def die(message: str):
    log(f"ERROR {message}")
    raise SystemExit(1)


def redact(text: str) -> str:
    """日志里不能出现令牌。"""
    if CNB_TOKEN:
        text = text.replace(CNB_TOKEN, "***")
    return text


def run(cmd: list[str], *, check: bool = True, echo: bool = True) -> subprocess.CompletedProcess:
    pretty = redact(" ".join(cmd))
    if echo:
        log(f"$ {pretty}")
    proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    for stream in (proc.stdout, proc.stderr):
        text = (stream or "").strip()
        if echo and text:
            log(redact(text))
    if check and proc.returncode != 0:
        raise SyncError(f"命令失败（退出码 {proc.returncode}）：{pretty}")
    return proc


# ---------------------------------------------------------------------------
# HTTP
# ---------------------------------------------------------------------------


class Response:
    __slots__ = ("status", "body")

    def __init__(self, status: int, body: bytes) -> None:
        self.status = status
        self.body = body

    def json(self):
        if not self.body:
            return None
        try:
            return json.loads(self.body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise SyncError(f"CNB 返回的不是 JSON（HTTP {self.status}）：{self.preview()}") from exc

    def preview(self, limit: int = 400) -> str:
        return self.body[:limit].decode("utf-8", "replace")


def http(
    method: str,
    url: str,
    *,
    body: bytes | None = None,
    headers: dict[str, str] | None = None,
    attempts: int = 3,
    timeout: int = 1800,
) -> Response:
    """发请求。5xx / 连接失败重试三次，4xx 直接返回。"""
    merged = {"User-Agent": USER_AGENT, "Accept": "*/*"}
    merged.update(headers or {})

    last = Response(0, b"")
    for attempt in range(1, attempts + 1):
        try:
            request = urllib.request.Request(url, data=body, method=method, headers=merged)
            with urllib.request.urlopen(request, timeout=timeout) as resp:
                return Response(resp.status, resp.read())
        except urllib.error.HTTPError as exc:
            last = Response(exc.code, exc.read())
            if exc.code < 500:
                return last
        except Exception as exc:  # 连接重置 / 超时 / DNS
            last = Response(0, str(exc).encode("utf-8"))

        if attempt < attempts:
            delay = attempt * 5
            log(f"{method} {redact(url)} 没成功（HTTP {last.status or '连接错误'}），{delay}s 后重试")
            time.sleep(delay)

    return last


def cnb(method: str, path: str, payload: dict | None = None) -> Response:
    headers = {"Accept": ACCEPT, "Authorization": f"Bearer {CNB_TOKEN}"}
    body = None
    if payload is not None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = "application/json"
    return http(method, CNB_API + path, body=body, headers=headers)


def expect(resp: Response, what: str, ok: tuple[int, ...] = (200,)) -> Response:
    if resp.status in ok:
        return resp
    detail = resp.preview()
    hint = ""
    if resp.status in (401, 403) and "scope" in detail.lower():
        hint = (
            "\n  → CNB 令牌权限不足：请到 https://cnb.cool/profile/token 编辑令牌，"
            "授权范围同时勾选「代码仓库 repo-code 读写」和「Release repo-release 读写」。"
        )
    raise SyncError(f"{what}失败：HTTP {resp.status} {detail}{hint}")


# ---------------------------------------------------------------------------
# 步骤
# ---------------------------------------------------------------------------


def check_auth() -> None:
    log(f"校验 CNB 访问权限（{CNB_REPO}）")
    resp = cnb("GET", f"/{CNB_REPO}/-/releases?page=1&page_size=1")
    if resp.status in (200, 404):
        # 404 只说明仓库还是空的（未初始化），不挡后续流程
        return
    if resp.status == 401:
        raise SyncError("CNB_TOKEN 无效或已过期（HTTP 401）")
    if resp.status == 403:
        raise SyncError(
            "CNB_TOKEN 没有该仓库权限（HTTP 403）。"
            "授权范围需要同时勾选「代码仓库 repo-code 读写」和「Release repo-release 读写」"
        )
    raise SyncError(f"访问 CNB 失败：HTTP {resp.status} {resp.preview()}")


def _tag_exists_on_cnb(tag: str, remote: str) -> bool:
    proc = run(["git", "ls-remote", "--tags", remote, f"refs/tags/{tag}"], check=False)
    if proc.returncode != 0:
        log(f"ls-remote 没成功（仓库为空或网络问题），直接尝试推送：{(proc.stderr or '').strip()[:200]}")
        return False
    return f"refs/tags/{tag}" in proc.stdout.split()


def ensure_tag(tag: str) -> None:
    remote = f"https://cnb:{CNB_TOKEN}@{CNB_HOST}/{CNB_REPO}"
    if _tag_exists_on_cnb(tag, remote):
        log(f"CNB 上已有 tag {tag}")
        return

    log(f"CNB 上没有 tag {tag}，先从 GitHub 推过去（首次会带上这段历史，之后都是增量）")
    if run(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}"], check=False).returncode != 0:
        run(
            ["git", "fetch", "--no-tags", "origin", f"refs/tags/{tag}:refs/tags/{tag}"],
            check=False,
        )
    last_error = ""
    for attempt in range(1, 4):
        proc = run(["git", "push", "--quiet", remote, f"refs/tags/{tag}:refs/tags/{tag}"], check=False)
        if proc.returncode == 0:
            log(f"tag {tag} 已推送到 CNB")
            time.sleep(3)
            return
        last_error = (proc.stderr or proc.stdout or "").strip()
        if attempt < 3:
            log(f"推送失败（第 {attempt} 次），{attempt * 10}s 后重试 —— 新仓库可能还在初始化")
            time.sleep(attempt * 10)
    raise SyncError(f"推送 tag {tag} 到 CNB 失败：{last_error}")


def upsert_release(tag: str, name: str, body: str, prerelease: bool) -> str:
    common = {
        "name": name,
        "body": body,
        "draft": False,
        "prerelease": prerelease,
        "make_latest": "false" if prerelease else "true",
    }

    resp = cnb("GET", f"/{CNB_REPO}/-/releases/tags/{urllib.parse.quote(tag, safe='')}")
    if resp.status == 200:
        release_id = (resp.json() or {}).get("id")
        log(f"CNB 上已有 Release {tag}（id={release_id}），更新标题和说明")
        expect(
            cnb("PATCH", f"/{CNB_REPO}/-/releases/{release_id}", common),
            f"更新 CNB Release {tag}",
            ok=(200, 204),
        )
    elif resp.status == 404:
        log(f"CNB 上还没有 Release {tag}，创建")
        payload = {"tag_name": tag, **common}
        created = cnb("POST", f"/{CNB_REPO}/-/releases", payload)
        if created.status not in (200, 201):
            # 刚推完 tag 时 CNB 可能还没建好索引，稍等再试一次
            log(f"创建返回 HTTP {created.status}，5s 后重试一次")
            time.sleep(5)
            created = cnb("POST", f"/{CNB_REPO}/-/releases", payload)
        expect(created, f"创建 CNB Release {tag}", ok=(200, 201))
        release_id = (created.json() or {}).get("id")
    else:
        raise SyncError(f"查询 CNB Release {tag} 失败：HTTP {resp.status} {resp.preview()}")

    if not release_id:
        raise SyncError(f"没有拿到 Release {tag} 的 id")
    return str(release_id)


def upload_asset(release_id: str, path: pathlib.Path) -> None:
    log(f"上传 {path.name}（{path.stat().st_size} 字节）")
    info = expect(
        cnb(
            "POST",
            f"/{CNB_REPO}/-/releases/{release_id}/asset-upload-url",
            {"asset_name": path.name, "overwrite": True, "size": path.stat().st_size, "ttl": 0},
        ),
        f"获取 {path.name} 的上传地址",
        ok=(200, 201),
    ).json() or {}

    upload_url = info.get("upload_url")
    verify_url = info.get("verify_url")
    if not upload_url or not verify_url:
        raise SyncError(f"CNB 返回的上传地址不完整：{info}")

    put = http(
        "PUT",
        upload_url,
        body=path.read_bytes(),
        headers={"Content-Type": "application/octet-stream"},
    )
    if put.status not in (200, 201):
        raise SyncError(f"上传 {path.name} 到对象存储失败：HTTP {put.status} {put.preview()}")

    confirm = http(
        "POST",
        urllib.parse.unquote(verify_url),
        headers={"Accept": ACCEPT, "Authorization": f"Bearer {CNB_TOKEN}"},
    )
    expect(confirm, f"确认 {path.name} 上传")

    log(f"✓ {path.name}")


def prune_assets(release_id: str, files: list[pathlib.Path]) -> None:
    keep = {p.name for p in files}
    resp = cnb("GET", f"/{CNB_REPO}/-/releases/{release_id}")
    if resp.status != 200:
        log(f"读取 CNB Release 附件列表失败（HTTP {resp.status}），跳过清理")
        return

    removed = 0
    for asset in (resp.json() or {}).get("assets") or []:
        name = asset.get("name")
        if name in keep:
            continue
        log(f"删除 CNB 上 GitHub 已没有的附件 {name}")
        try:
            expect(
                cnb("DELETE", f"/{CNB_REPO}/-/releases/{release_id}/assets/{asset.get('id')}"),
                f"删除 {name}",
                ok=(200, 204),
            )
            removed += 1
        except SyncError as exc:
            log(f"WARN {exc}")

    if removed:
        log(f"清理了 {removed} 个过期附件")


def download_assets(tag: str, dest: pathlib.Path) -> list[pathlib.Path]:
    dest.mkdir(parents=True, exist_ok=True)
    run(["gh", "release", "download", tag, "--repo", GH_REPO, "--dir", str(dest), "--clobber"])
    return sorted(p for p in dest.iterdir() if p.is_file())


# ---------------------------------------------------------------------------
# 更新清单（供启动器的 CNB 更新源读取）
# ---------------------------------------------------------------------------

# 清单挂在一个固定 tag 的 Release 上，客户端地址因此永远是同一个：
#   https://cnb.cool/{repo}/-/releases/download/updates/latest.json
MANIFEST_TAG = _env("CNB_MANIFEST_TAG", "updates")
MANIFEST_STABLE = "latest.json"
MANIFEST_BETA = "latest-beta.json"


def _sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def _pick_installer(files: list[pathlib.Path]) -> pathlib.Path | None:
    """从附件里挑出安装包。

    CI 产出的附件名是 PCL2_CE_<配置>_<架构>.exe，与手工命名的
    PCL-Symbio-Edition-<版本>-x64.exe 并存，因此不硬编码文件名，
    只按扩展名与架构标记判断。
    """
    candidates = [
        p for p in files
        if p.suffix.lower() == ".exe"
        and "x64" in p.name.lower()
        and "arm64" not in p.name.lower()
    ]
    return sorted(candidates)[0] if candidates else None


def _asset_url(asset: dict) -> str:
    # CNB 的字段拼写是 brower_download_url（少一个 s），按原样取
    return (asset.get("brower_download_url") or asset.get("url") or "").strip()


def write_manifest(tag: str, name: str, body: str, prerelease: bool, files: list[pathlib.Path]) -> None:
    """把本次发布的版本信息写进 CNB 的更新清单。失败由调用方决定是否算致命。"""
    installer = _pick_installer(files)
    if installer is None:
        log(f"WARN {tag} 没有找到 x64 安装包附件，跳过更新清单")
        return

    release_id = upsert_release(
        MANIFEST_TAG,
        "更新清单",
        "启动器 CNB 更新源读取的版本清单，由 CI 自动维护，请勿手动修改。",
        False,
    )

    # 先上传、再从 CNB 侧读回真实下载地址，避免猜测 URL 形态
    upload_asset(release_id, installer)
    listing = expect(
        cnb("GET", f"/{CNB_REPO}/-/releases/{release_id}"),
        "读取清单 Release 的附件列表",
    ).json() or {}
    matched = next(
        (a for a in (listing.get("assets") or []) if a.get("name") == installer.name),
        None,
    )
    url = _asset_url(matched) if matched else ""
    if not url:
        raise SyncError(f"CNB 未返回 {installer.name} 的下载地址，无法生成清单")

    version_name = tag.strip().lstrip("vV")
    manifest = {
        "version": version_name,
        "channel": "beta" if prerelease else "stable",
        "changelog": body,
        "sha256": _sha256_file(installer),
        "downloads": {"x64": url},
    }
    file_name = MANIFEST_BETA if prerelease else MANIFEST_STABLE

    manifest_path = pathlib.Path(tempfile.mkdtemp(prefix="cnb-manifest-")) / file_name
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    upload_asset(release_id, manifest_path)
    log(
        f"✓ 更新清单 {file_name} → {version_name}"
        f"（{RELEASES_URL}/download/{MANIFEST_TAG}/{file_name}）"
    )



def sync_one(tag: str) -> None:
    meta_proc = run(
        [
            "gh", "release", "view", tag,
            "--repo", GH_REPO,
            "--json", "tagName,name,body,isDraft,isPrerelease,assets",
        ],
        echo=False,
    )
    try:
        meta = json.loads(meta_proc.stdout)
    except json.JSONDecodeError as exc:
        raise SyncError(f"解析 GitHub Release {tag} 信息失败：{exc}") from exc

    if meta.get("isDraft"):
        log(f"GitHub 上 {tag} 仍是草稿，跳过")
        return

    name = (meta.get("name") or "").strip() or tag
    body = meta.get("body") or ""
    prerelease = bool(meta.get("isPrerelease"))

    ensure_tag(tag)
    release_id = upsert_release(tag, name, body, prerelease)

    with tempfile.TemporaryDirectory(prefix="cnb-assets-") as tmp:
        if meta.get("assets"):
            files = download_assets(tag, pathlib.Path(tmp))
        else:
            files = []
            log(f"GitHub Release {tag} 没有附件")
        for path in files:
            upload_asset(release_id, path)
        if PRUNE:
            prune_assets(release_id, files)

    log(f"=== {tag} 同步完成：{len(files)} 个附件 → {RELEASES_URL} ===")

    if tag == MANIFEST_TAG:
        log("这是更新清单自身的 Release，跳过清单生成")
        return
    # 清单只是给启动器提速的附属产物，写失败不应让整次发布失败
    if not prerequisites_ok():
        return
    try:
        write_manifest(tag, name, body, prerelease, files)
    except SyncError as exc:
        log(f"WARN 更新清单生成失败，本次发布不受影响：{exc}")


def prerequisites_ok() -> bool:
    if WRITE_MANIFEST:
        return True
    log("已关闭更新清单生成（WRITE_MANIFEST=false）")
    return False


def main() -> int:
    if not CNB_TOKEN:
        die("缺少 CNB_TOKEN（CNB 访问令牌，需要 repo-code 读写权限）")
    if not GH_REPO:
        die("缺少 GITHUB_REPOSITORY（例如 Hydrogen-Peroxide/PCL-Symbio-Edition）")

    check_auth()

    if TAG:
        tags = [TAG]
    else:
        log(f"没有指定 tag，同步最近 {BATCH_LIMIT} 个 Release")
        listing = run(
            [
                "gh", "release", "list",
                "--repo", GH_REPO,
                "--limit", str(BATCH_LIMIT),
                "--json", "tagName",
                "--jq", ".[].tagName",
            ],
            echo=False,
        )
        tags = [line.strip() for line in listing.stdout.splitlines() if line.strip()]
        if not tags:
            die("GitHub 上没有可同步的 Release")

    failures = 0
    for tag in tags:
        log(f"──────── {tag} ────────")
        try:
            sync_one(tag)
        except SyncError as exc:
            failures += 1
            log(f"ERROR {tag}：{exc}")

    if failures:
        die(f"{failures} 个 Release 同步失败")
    log("全部完成")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)

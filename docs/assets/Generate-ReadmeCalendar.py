#!/usr/bin/env python3
"""Generate dual-theme (Light / Dark) Release & Commit Calendar SVGs for PCL SE's README.md.

Adapted from the README calendar generator used by DDZmumo/PCL-Living-Fork.
"""
from __future__ import annotations

import argparse
import base64
import datetime as dt
import html
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
CST = dt.timezone(dt.timedelta(hours=8))


def text_width(text: str, font_size: float) -> float:
    width = 0.0
    for ch in text:
        if ch in " ·•":
            width += font_size * 0.45
        elif ord(ch) > 127:
            width += font_size * 1.0
        else:
            width += font_size * 0.58
    return width


def collect_commits(repo: Path) -> dict[str, int]:
    result = subprocess.run(
        ["git", "-C", str(repo), "log", "--date=format-local:%Y-%m-%d", "--pretty=format:%ad"],
        env={**os.environ, "TZ": "Asia/Shanghai"},
        capture_output=True,
        text=True,
        check=True,
    )
    counts: dict[str, int] = {}
    for line in result.stdout.splitlines():
        day = line.strip()
        if day:
            counts[day] = counts.get(day, 0) + 1
    return counts


def extract_release_summary(body: str, fallback: str) -> str:
    lines = [line.strip() for line in (body or "").splitlines() if line.strip()]
    headings: list[str] = []
    first_para = ""
    for line in lines:
        if line.startswith("#"):
            heading = line.lstrip("#").strip()
            if heading and heading not in ("下载与运行", "校验与附件", "本次更新内容", "本次更新", "更新内容"):
                headings.append(heading)
            continue
        if line.startswith(("|", "- ", "* ")):
            continue
        clean = re.sub(r"\[([^\]]+)\]\([^\)]+\)", r"\1", line)
        clean = clean.replace("**", "").replace("`", "").strip()
        clean = re.sub(r"^PCL (?:SE|Symbio Edition)[^。]*。", "", clean).strip()
        if clean:
            first_para = clean
            break
    if first_para:
        summary = first_para.split("。")[0]
        summary = re.sub(r"^[0-9.a-zA-Z-]+\s*是\s*", "", summary)
        summary = re.sub(r"^这是\s*[^，]+，(?:主要)?", "", summary)
        summary = re.sub(r"^这是\s*", "", summary)
        summary = re.sub(r"^本次(?:更新)?", "", summary)
        summary = summary.strip("，。 ")
        if headings and len(summary) < 18:
            extra = " · ".join(h for h in headings[:2] if h not in summary)
            if extra:
                summary = f"{summary}（{extra}）"
        return summary[:42] + "…" if len(summary) > 43 else summary
    if headings:
        summary = " · ".join(headings[:3])
        return summary[:42] + "…" if len(summary) > 43 else summary
    return fallback[:42] + "…" if len(fallback) > 43 else fallback


def collect_releases_from_git(repo: Path) -> list[dict]:
    result = subprocess.run(
        [
            "git",
            "-C",
            str(repo),
            "for-each-ref",
            "refs/tags",
            "--sort=-creatordate",
            "--format=%(refname:strip=2)|%(creatordate:short)|%(contents:subject)",
        ],
        capture_output=True,
        text=True,
        check=True,
    )
    releases: list[dict] = []
    for line in result.stdout.splitlines():
        parts = line.split("|", 2)
        if len(parts) != 3:
            continue
        tag, day, subject = parts[0].strip(), parts[1].strip(), parts[2].strip()
        if not tag.startswith("v"):
            continue
        releases.append({
            "tag": tag,
            "date": day,
            "prerelease": "-" in tag,
            "title": extract_release_summary(subject, tag),
        })
    return releases


def get_github_token() -> str:
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
    if token:
        return token.strip()
    try:
        res = subprocess.run(["gh", "auth", "token"], capture_output=True, text=True, timeout=4, check=True)
        return res.stdout.strip()
    except Exception:
        return ""


def collect_releases(repo_slug: str, repo: Path) -> list[dict]:
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "PCLSE-Calendar-Generator"}
    token = get_github_token()
    if token:
        headers["Authorization"] = f"Bearer {token}"
    url = f"https://api.github.com/repos/{repo_slug}/releases?per_page=30"
    try:
        req = urllib.request.Request(url, headers=headers)
        with urllib.request.urlopen(req, timeout=8) as resp:
            items = json.loads(resp.read().decode("utf-8"))
        releases: list[dict] = []
        for item in items:
            if item.get("draft"):
                continue
            tag = item.get("tag_name", "")
            if not tag.startswith("v"):
                continue
            published = item.get("published_at") or item.get("created_at") or ""
            if not published:
                continue
            when = dt.datetime.fromisoformat(published.replace("Z", "+00:00")).astimezone(CST)
            day = when.strftime("%Y-%m-%d")
            releases.append({
                "tag": tag,
                "date": day,
                "prerelease": bool(item.get("prerelease")),
                "title": extract_release_summary(item.get("body") or "", item.get("name") or tag),
            })
        if releases:
            return releases
    except Exception:
        pass
    return collect_releases_from_git(repo)


def collect_stars(repo_slug: str) -> dict[str, int]:
    """Return daily star additions {YYYY-MM-DD: count} from GitHub Stargazers API."""
    headers = {"Accept": "application/vnd.github.star+json", "User-Agent": "PCLSE-Calendar-Generator"}
    token = get_github_token()
    if token:
        headers["Authorization"] = f"Bearer {token}"
    daily: dict[str, int] = {}
    for page in range(1, 15):
        url = f"https://api.github.com/repos/{repo_slug}/stargazers?per_page=100&page={page}"
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=8) as resp:
                items = json.loads(resp.read().decode("utf-8"))
            if not items:
                break
            for item in items:
                starred_at = item.get("starred_at") or ""
                if not starred_at:
                    continue
                when = dt.datetime.fromisoformat(starred_at.replace("Z", "+00:00")).astimezone(CST)
                day = when.strftime("%Y-%m-%d")
                daily[day] = daily.get(day, 0) + 1
            if len(items) < 100:
                break
        except Exception:
            break
    return daily


def render_timeline(
    commits: dict[str, int],
    releases: list[dict],
    today: dt.date,
    dark: bool,
) -> str:
    if dark:
        card_bg = "#0d1117"
        inner_bg = "#161b22"
        border = "#30363d"
        divider = "#21262d"
        text_main = "#e6edf3"
        text_sub = "#8b949e"
        text_muted = "#6e7681"
        accent = "#58a6ff"
        pill_bg = "#1f2937"
        pill_border = "#30363d"
        heat = ("#161b22", "#0e4429", "#1d4ed8", "#2563eb", "#58a6ff")
        heat = ("#21262d", "#17325c", "#1d4ed8", "#3b82f6", "#60a5fa")
        stable_bg, stable_border, stable_fg = "#1f6feb", "#388bfd", "#ffffff"
        pre_bg, pre_border, pre_fg = "#21262d", "#30363d", "#58a6ff"
        release_ring = "#58a6ff"
    else:
        card_bg = "#ffffff"
        inner_bg = "#f6f8fa"
        border = "#d0d7de"
        divider = "#eaeef2"
        text_main = "#1f2328"
        text_sub = "#59636e"
        text_muted = "#8c959f"
        accent = "#0969da"
        pill_bg = "#eff6ff"
        pill_border = "#bfdbfe"
        heat = ("#ebedf0", "#dbeafe", "#93c5fd", "#3b82f6", "#1d4ed8")
        stable_bg, stable_border, stable_fg = "#0969da", "#0969da", "#ffffff"
        pre_bg, pre_border, pre_fg = "#eff6ff", "#93c5fd", "#0969da"
        release_ring = "#0969da"

    def level(count: int) -> int:
        if count <= 0:
            return 0
        if count <= 2:
            return 1
        if count <= 8:
            return 2
        if count <= 25:
            return 3
        return 4

    release_dates = {r["date"] for r in releases}
    last_30_commits = sum(
        count
        for day_str, count in commits.items()
        if 0 <= (today - dt.date.fromisoformat(day_str)).days <= 30
    )
    total_commits = sum(commits.values())
    latest_stable = next((r["tag"] for r in releases if not r["prerelease"]), releases[0]["tag"] if releases else "v1.4.0")

    shown_releases = releases[:6]
    width = 860
    height = 468

    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" fill="none">',
        "<style>",
        "  .sans { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', sans-serif; }",
        "  .mono { font-family: ui-monospace, SFMono-Regular, 'Cascadia Mono', Consolas, monospace; }",
        "</style>",
        f'<rect x="0.75" y="0.75" width="{width - 1.5}" height="{height - 1.5}" rx="12" fill="{card_bg}" stroke="{border}" stroke-width="1.5"/>',
    ]

    # --- Section 1: 52-Week Commit & Release Heatmap ---
    lines.append(f'<text x="24" y="32" class="sans" font-size="14" font-weight="600" fill="{text_main}">Commit 与版本发布热力日历</text>')

    stats_text = f"近 30 日 {last_30_commits} 次提交  ·  累计 {len(releases)} 个版本  ·  最新正式版 {latest_stable}"
    lines.append(f'<text x="{width - 24}" y="32" text-anchor="end" class="sans" font-size="12" fill="{text_sub}">{html.escape(stats_text)}</text>')

    # 52 weeks grid (Sunday to Saturday, matching GitHub native contribution graph)
    weeks = 51
    cell = 11
    gap = 3
    step = cell + gap
    grid_x = 56
    grid_y = 62

    # Align end_sunday so the last column contains `today`
    days_since_sunday = (today.weekday() + 1) % 7
    current_sunday = today - dt.timedelta(days=days_since_sunday)
    start_sunday = current_sunday - dt.timedelta(weeks=weeks - 1)

    # Weekday labels
    for row_idx, w_label in ((1, "Mon"), (3, "Wed"), (5, "Fri")):
        wy = grid_y + row_idx * step + 9
        lines.append(f'<text x="24" y="{wy}" class="sans" font-size="10" fill="{text_muted}">{w_label}</text>')

    # Month labels
    last_month = None
    last_label_x = -40
    month_names = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    for col in range(weeks):
        col_date = start_sunday + dt.timedelta(weeks=col)
        if col_date.month != last_month:
            mx = grid_x + col * step
            if mx - last_label_x >= 26:
                lines.append(f'<text x="{mx}" y="{grid_y - 8}" class="sans" font-size="10" fill="{text_sub}">{month_names[col_date.month - 1]}</text>')
                last_label_x = mx
            last_month = col_date.month

    for col in range(weeks):
        for row in range(7):
            day = start_sunday + dt.timedelta(weeks=col, days=row)
            if day > today:
                continue
            day_str = day.strftime("%Y-%m-%d")
            cnt = commits.get(day_str, 0)
            lvl = level(cnt)
            cx = grid_x + col * step
            cy = grid_y + row * step
            fill = heat[lvl]
            if day_str in release_dates:
                lines.append(f'<rect x="{cx}" y="{cy}" width="{cell}" height="{cell}" rx="2.5" fill="{fill}" stroke="{release_ring}" stroke-width="1.4"/>')
            else:
                lines.append(f'<rect x="{cx}" y="{cy}" width="{cell}" height="{cell}" rx="2.5" fill="{fill}"/>')

    # Heatmap footer legend
    leg_y = grid_y + 7 * step + 16
    lines.append(f'<rect x="24" y="{leg_y - 9}" width="10" height="10" rx="2.5" fill="{heat[2]}" stroke="{release_ring}" stroke-width="1.4"/>')
    lines.append(f'<text x="40" y="{leg_y}" class="sans" font-size="11" fill="{text_sub}">描边方格代表当日有版本发布</text>')

    lx_start = width - 24 - (5 * 14 + 72)
    lines.append(f'<text x="{lx_start}" y="{leg_y}" class="sans" font-size="11" fill="{text_sub}">Less</text>')
    for idx, c in enumerate(heat):
        bx = lx_start + 30 + idx * 14
        lines.append(f'<rect x="{bx}" y="{leg_y - 9}" width="10" height="10" rx="2.5" fill="{c}"/>')
    lines.append(f'<text x="{lx_start + 30 + 5 * 14 + 4}" y="{leg_y}" class="sans" font-size="11" fill="{text_sub}">More</text>')

    # Divider
    div_y = leg_y + 18
    lines.append(f'<line x1="24" y1="{div_y}" x2="{width - 24}" y2="{div_y}" stroke="{divider}" stroke-width="1"/>')

    # --- Section 2: Recent Releases Timeline ---
    sec2_y = div_y + 26
    lines.append(f'<text x="24" y="{sec2_y}" class="sans" font-size="14" font-weight="600" fill="{text_main}">近期版本更新记录</text>')
    lines.append(f'<text x="{width - 24}" y="{sec2_y}" text-anchor="end" class="sans" font-size="11" fill="{text_muted}">自动提取自 GitHub Releases 正文</text>')

    row_start_y = sec2_y + 18
    row_h = 38
    rail_x = 114

    lines.append(
        f'<line x1="{rail_x}" y1="{row_start_y + 14}" x2="{rail_x}" y2="{row_start_y + (len(shown_releases) - 1) * row_h + 14}" stroke="{border}" stroke-width="1.5"/>'
    )

    for idx, rel in enumerate(shown_releases):
        ry = row_start_y + idx * row_h
        cy = ry + 14
        is_pre = rel["prerelease"]

        # Date on the left
        lines.append(f'<text x="24" y="{cy + 4}" class="mono" font-size="11.5" fill="{text_sub}">{rel["date"]}</text>')

        # Node dot on timeline rail
        dot_fill = card_bg if is_pre else stable_bg
        dot_stroke = accent if is_pre else stable_bg
        lines.append(f'<circle cx="{rail_x}" cy="{cy}" r="4.5" fill="{dot_fill}" stroke="{dot_stroke}" stroke-width="2"/>')

        # Row background card
        row_x = 132
        row_w = width - row_x - 24
        lines.append(f'<rect x="{row_x}" y="{ry}" width="{row_w}" height="30" rx="6" fill="{inner_bg}" stroke="{divider}" stroke-width="1"/>')

        # Version tag pill
        tag = rel["tag"]
        badge_label = "预发布" if is_pre else "正式版"
        b_bg = pre_bg if is_pre else stable_bg
        b_border = pre_border if is_pre else stable_border
        b_fg = pre_fg if is_pre else stable_fg

        lines.append(f'<rect x="{row_x + 10}" y="{ry + 5}" width="44" height="20" rx="4" fill="{b_bg}" stroke="{b_border}" stroke-width="1"/>')
        lines.append(f'<text x="{row_x + 32}" y="{ry + 18.5}" text-anchor="middle" class="sans" font-size="10.5" font-weight="600" fill="{b_fg}">{badge_label}</text>')

        # Version number
        tag_x = row_x + 64
        lines.append(f'<text x="{tag_x}" y="{ry + 19.5}" class="mono" font-size="12.5" font-weight="700" fill="{text_main}">{html.escape(tag)}</text>')

        # Summary text aligned neatly
        summary_x = row_x + 178
        lines.append(f'<text x="{summary_x}" y="{ry + 19}" class="sans" font-size="12" fill="{text_sub}">{html.escape(rel["title"])}</text>')

    lines.append("</svg>")
    return "\n".join(lines) + "\n"


def wrap_text(text: str, font_size: float, max_width: float, max_lines: int) -> list[str]:
    tokens = re.findall(r"[A-Za-z0-9_./+#-]+|\s+|.", text)
    lines: list[str] = []
    current = ""
    for tok in tokens:
        if not current and tok.isspace():
            continue
        if current and text_width(current + tok, font_size) > max_width:
            lines.append(current.rstrip())
            current = "" if tok.isspace() else tok
            if len(lines) == max_lines:
                last = lines[-1]
                while last and text_width(last + "…", font_size) > max_width:
                    last = last[:-1].rstrip()
                lines[-1] = last + "…"
                return lines
        else:
            current += tok
    if current.strip():
        lines.append(current.rstrip())
    return lines or [""]


def mix(color_a: str, color_b: str, ratio: float) -> str:
    a = [int(color_a[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(color_b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(x + (y - x) * ratio):02x}" for x, y in zip(a, b))


def load_launcher_icon_data_uri(repo: Path, target_size: int = 40) -> str:
    ico_path = repo / "Plain Craft Launcher 2" / "Images" / "icon.ico"
    if not ico_path.exists():
        return ""
    data = ico_path.read_bytes()
    count = struct.unpack_from("<H", data, 4)[0]
    best_bytes = b""
    best_diff = 9999
    for idx in range(count):
        w, h, _, _, _, _, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + idx * 16)
        dim = w or 256
        chunk = data[offset:offset + size]
        if chunk.startswith(b"\x89PNG") and abs(dim - target_size) < best_diff:
            best_diff = abs(dim - target_size)
            best_bytes = chunk
    if not best_bytes:
        return ""
    return "data:image/png;base64," + base64.b64encode(best_bytes).decode("ascii")


# Theme tokens mirroring PCL SE's Application.xaml, LegacyThemeService.cs, and PageSetupUpdate.xaml.cs
PULSE_THEMES = {
    False: {
        # PanTitle (LegacyThemeService.CreateTitleBackground: FromHSL2(200/225/250, 100, 68/74/68))
        "title_0": "#0aadff",
        "title_1": "#85a4ff",
        "title_2": "#947fff",
        "title_fg": "#ffffff",
        "title_sel_bg": "#ffffff",
        "title_sel_fg": "#1370f3",
        # PanForm background (LegacyThemeService.ApplyBackground Colorful + Background)
        "body_0": "#e7f1fd",
        "body_1": "#ecf1fd",
        "body_2": "#f1effe",
        # MyCard & surfaces
        "card_bg": "#ffffff",
        "card_border": "#e3eaf6",
        "card_shadow": "#343d4a",
        "card_shadow_op": "0.09",
        # Color1..Color8 & Gray1..Gray8
        "c1": "#343d4a",
        "c2": "#0b5bcb",
        "c3": "#1370f3",
        "c4": "#4890f5",
        "c5": "#96c0f9",
        "c6": "#d5e6fd",
        "c7": "#e0eafd",
        "c8": "#eaf2fe",
        "g1": "#404040",
        "g2": "#737373",
        "g3": "#8c8c8c",
        "g4": "#a6a6a6",
        "g5": "#cccccc",
        "g6": "#ebebeb",
        "g7": "#f0f0f0",
        "g8": "#f5f5f5",
        # Heat levels (Gray8 -> Color7 -> Color6 -> Color4 -> Color3)
        "heat": ("#f2f5fa", "#dbeafe", "#b6d4fe", "#5b9cf6", "#1370f3"),
        # PageSetupUpdate.xaml.cs channel & current-version badges
        "stable_bg": "#eaf2fe",
        "stable_fg": "#1370f3",
        "stable_border": "#96c0f9",
        "pre_bg": "#fff1d8",
        "pre_fg": "#975908",
        "pre_border": "#f57a00",
        "cur_bg": "#e5f6ec",
        "cur_fg": "#278b54",
        "cur_border": "#278b54",
        "cur_row_bg": "#f8fcfa",
        "row_bg": "#f7f9fc",
        "row_border": "#e6ecf5",
        "btn_bg": "#ffffff",
        # MyHint Blue (InfoBack / InfoBorder / InfoText)
        "hint_bg": "#d9ecff",
        "hint_border": "#1172d4",
        "hint_fg": "#0f64b8",
    },
    True: {
        # PanTitle (LegacyThemeService.CreateTitleBackground Dark: FromHSL2(212.5/225/237.5, 72, 20/24/20))
        "title_0": "#0d2c50",
        "title_1": "#122a71",
        "title_2": "#121671",
        "title_fg": "#dce2eb",
        "title_sel_bg": "#303b52",
        "title_sel_fg": "#91aaff",
        # PanForm background (LegacyThemeService Dark Background #171B22)
        "body_0": "#171b22",
        "body_1": "#1b2029",
        "body_2": "#181b24",
        # MyCard & surfaces (#252932)
        "card_bg": "#252932",
        "card_border": "#303846",
        "card_shadow": "#000000",
        "card_shadow_op": "0.28",
        # Color1..Color8 & Gray1..Gray8 (LegacyThemeService.cs lines 96, 104)
        "c1": "#dce2eb",
        "c2": "#aabfff",
        "c3": "#91aaff",
        "c4": "#adc2ff",
        "c5": "#6583b7",
        "c6": "#394a69",
        "c7": "#2b3851",
        "c8": "#252f44",
        "g1": "#dce2eb",
        "g2": "#b4becd",
        "g3": "#a1acbd",
        "g4": "#7f8b9e",
        "g5": "#586275",
        "g6": "#3c4656",
        "g7": "#303846",
        "g8": "#272f3b",
        # Heat levels (Gray8 -> Color7 -> Color6 -> Color5 -> Color3)
        "heat": ("#2b313d", "#2b3851", "#394a69", "#5472ae", "#91aaff"),
        # PageSetupUpdate dark badges
        "stable_bg": "#252f44",
        "stable_fg": "#91aaff",
        "stable_border": "#6583b7",
        "pre_bg": "#413421",
        "pre_fg": "#f4ce8a",
        "pre_border": "#daae58",
        "cur_bg": "#1e3529",
        "cur_fg": "#8ed8af",
        "cur_border": "#3e9b67",
        "cur_row_bg": "#212c29",
        "row_bg": "#2a303c",
        "row_border": "#343d4c",
        "btn_bg": "#292e38",
        # MyHint Blue Dark
        "hint_bg": "#24364a",
        "hint_border": "#6aaff3",
        "hint_fg": "#a9d5ff",
    },
}


def render_pulse(
    commits: dict[str, int],
    releases: list[dict],
    today: dt.date,
    dark: bool,
    stars: dict[str, int] | None = None,
) -> str:
    """Adopt PCL SE's MyCard/MyHint design language and theme palette without fake interactive controls."""
    t = PULSE_THEMES[dark]
    width, height = 920, 556
    days = 35
    window = [today - dt.timedelta(days=days - 1 - i) for i in range(days)]
    counts = [commits.get(day.isoformat(), 0) for day in window]
    peak = max(counts, default=0)
    scale = max(1, peak)
    last_30 = sum(count for day, count in commits.items() if 0 <= (today - dt.date.fromisoformat(day)).days < 30)
    total_commits = sum(commits.values())
    latest_stable = next((rel["tag"] for rel in releases if not rel["prerelease"]), releases[0]["tag"] if releases else "—")
    by_day: dict[str, list[dict]] = {}
    for release in releases:
        by_day.setdefault(release["date"], []).append(release)
    stars = stars or {}

    out = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" fill="none" role="img" aria-labelledby="calendar-title calendar-description">',
        '<title id="calendar-title">PCL SE · 开发脉搏与更新日历</title>',
        '<desc id="calendar-description">采用 PCL SE 卡片设计风格与深浅主题配色的近 5 周提交日历、每日提交脉冲、最近发布动态与 Star 增长折线图，由 GitHub Actions 自动同步。</desc>',
        '<defs>',
        f'<linearGradient id="panFormGrad" x1="0.9" y1="0" x2="0.1" y2="1">'
        f'<stop offset="0%" stop-color="{t["body_0"]}"/>'
        f'<stop offset="45%" stop-color="{t["body_1"]}"/>'
        f'<stop offset="100%" stop-color="{t["body_2"]}"/>'
        f'</linearGradient>',
        f'<linearGradient id="pulseAreaGrad" x1="0" y1="0" x2="0" y2="1">'
        f'<stop offset="0%" stop-color="{t["c3"]}" stop-opacity="0.24"/>'
        f'<stop offset="100%" stop-color="{t["c3"]}" stop-opacity="0.02"/>'
        f'</linearGradient>',
        f'<linearGradient id="starAreaGrad" x1="0" y1="0" x2="0" y2="1">'
        f'<stop offset="0%" stop-color="{t["c3"]}" stop-opacity="0.28"/>'
        f'<stop offset="100%" stop-color="{t["c3"]}" stop-opacity="0.02"/>'
        f'</linearGradient>',
        f'<filter id="myCardShadow" x="-4%" y="-6%" width="108%" height="116%">'
        f'<feDropShadow dx="0" dy="1.5" stdDeviation="2.5" flood-color="{t["card_shadow"]}" flood-opacity="{t["card_shadow_op"]}"/>'
        f'</filter>',
        '<clipPath id="windowClip"><rect x="2" y="2" width="916" height="552" rx="6"/></clipPath>',
        '</defs>',
        '<style>',
        "  .sans { font-family: 'Microsoft YaHei UI', 'Microsoft YaHei', 'PingFang SC', 'Segoe UI', -apple-system, sans-serif; }",
        "  .mono { font-family: 'Cascadia Mono', Consolas, 'Segoe UI', ui-monospace, monospace; font-variant-numeric: tabular-nums; }",
        '</style>',
    ]

    def txt(x: float, y: float, value: str, size: float = 13, color: str | None = None, weight: int = 400, mono: bool = False, anchor: str = "start") -> None:
        out.append(
            f'<text x="{x:.1f}" y="{y:.1f}" class="{"mono" if mono else "sans"}" '
            f'font-size="{size}" font-weight="{weight}" fill="{color or t["c1"]}" text-anchor="{anchor}">{html.escape(str(value))}</text>'
        )

    def my_card(x: float, y: float, w: float, h: float, title: str) -> None:
        out.append(
            f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="5" '
            f'fill="{t["card_bg"]}" stroke="{t["card_border"]}" stroke-width="1" filter="url(#myCardShadow)"/>'
        )
        txt(x + 15, y + 23, title, 13, t["c1"], 700)

    # --- Outer Container ---
    out.append('<g clip-path="url(#windowClip)">')
    out.append('<rect x="2" y="2" width="916" height="552" fill="url(#panFormGrad)"/>')

    # --- Row 1 Left: MyCard Title="近 5 周提交日历" ---
    cal_x, cal_y, cal_w, cal_h = 18, 16, 286, 216
    my_card(cal_x, cal_y, cal_w, cal_h, "近 5 周提交日历")
    monday = today - dt.timedelta(days=today.weekday(), weeks=4)
    txt(cal_x + cal_w - 15, cal_y + 23, f"{monday:%m/%d} - {today:%m/%d}", 11, t["g3"], mono=True, anchor="end")

    gx, gy, cw, ch, step_x, step_y = 33, 64, 32, 22, 37, 26
    for col, name in enumerate("一二三四五六日"):
        txt(gx + col * step_x + cw / 2, 56, name, 11, t["g3"], anchor="middle")

    for row in range(5):
        for col in range(7):
            day = monday + dt.timedelta(days=row * 7 + col)
            count = commits.get(day.isoformat(), 0)
            future = day > today
            level = 0 if (future or count <= 0) else 1 if count <= 2 else 2 if count <= 8 else 3 if count <= 25 else 4
            cx, cy = gx + col * step_x, gy + row * step_y
            fill = t["heat"][level]
            stroke = t["c3"] if day == today else (t["c5"] if level >= 2 else t["row_border"])
            sw = 1.6 if day == today else 0.8
            out.append(f'<g opacity="{0.38 if future else 1}"><title>{day.isoformat()} · {0 if future else count} 次提交</title>')
            out.append(f'<rect x="{cx:.1f}" y="{cy:.1f}" width="{cw}" height="{ch}" rx="3.5" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>')
            fg = "#ffffff" if (level == 4 and not dark) or (level == 4 and dark and t["heat"][4] == "#1370f3") else (
                "#141b2b" if (level == 4 and dark) else (t["c1"] if (count or day == today) else t["g3"])
            )
            txt(cx + cw / 2, cy + 15, day.strftime("%d"), 11.5, fg, 700 if (count or day == today) else 400, mono=True, anchor="middle")
            events = by_day.get(day.isoformat(), [])
            if events and not future:
                has_stable = any(not rel["prerelease"] for rel in events)
                dot_fill = t["cur_border"] if has_stable else t["pre_border"]
                out.append(f'<circle cx="{cx + cw - 4.5:.1f}" cy="{cy + 4.5:.1f}" r="2.6" fill="{dot_fill}" stroke="{t["card_bg"]}" stroke-width="0.9"/>')
            out.append('</g>')

    # Legend inside Left MyCard
    out.append(f'<line x1="{cal_x + 15}" y1="199" x2="{cal_x + cal_w - 15}" y2="199" stroke="{t["g6"]}" stroke-width="1"/>')
    out.append(f'<circle cx="38" cy="215" r="3" fill="{t["cur_border"]}"/>')
    txt(46, 219, "正式版", 11, t["g2"])
    out.append(f'<circle cx="96" cy="215" r="3" fill="{t["pre_border"]}"/>')
    txt(104, 219, "预发布", 11, t["g2"])
    txt(178, 219, "少", 10.5, t["g3"])
    for idx, hcol in enumerate(t["heat"]):
        out.append(f'<rect x="{194 + idx * 14}" y="209.5" width="11" height="11" rx="2" fill="{hcol}" stroke="{t["row_border"]}" stroke-width="0.6"/>')
    txt(268, 219, "多", 10.5, t["g3"])

    # --- Row 1 Right: MyCard Title="每日提交脉冲与版本节点" ---
    px, py, pw, ph = 316, 16, 586, 216
    my_card(px, py, pw, ph, "每日提交脉冲与版本节点")
    txt(px + pw - 15, py + 23, f"近 30 日 {last_30} 次提交 · 累计 {total_commits} 次 · 峰值 {peak}/日", 11, t["g3"], anchor="end")

    left, right, bottom = 358.0, 882.0, 198.0
    slot = (right - left) / days
    center = lambda idx: left + slot * (idx + 0.5)
    flags, previews = [], []
    for idx, day in enumerate(window):
        events = by_day.get(day.isoformat(), [])
        stable = next((rel for rel in events if not rel["prerelease"]), None)
        if stable:
            label = stable["tag"]
            w = text_width(label, 10) + 12
            flags.append({"i": idx, "cx": center(idx), "x": min(max(center(idx) - w / 2, left), right - w), "w": w, "label": label})
        elif events:
            previews.append(idx)

    lane_ends: list[float] = []
    for flag in flags:
        for lane, end in enumerate(lane_ends):
            if flag["x"] > end + 5:
                lane_ends[lane] = flag["x"] + flag["w"]
                break
        else:
            lane = len(lane_ends)
            lane_ends.append(flag["x"] + flag["w"])
        flag["y"] = 50 + lane * 20

    top = max(104.0, 50.0 + len(lane_ends) * 20 + 14)
    plot_h = max(28.0, bottom - top)
    bar_h = lambda count: max(4.0, (count / scale) ** 0.5 * plot_h) if count else 0.0

    last_grid_y = -1000.0
    for tick in sorted((n for n in (10, 30, 100, 300) if n <= peak), reverse=True):
        gy_line = bottom - (tick / scale) ** 0.5 * plot_h
        if bottom - gy_line < 12 or gy_line - last_grid_y < 18:
            continue
        last_grid_y = gy_line
        out.append(f'<line x1="{left:.1f}" y1="{gy_line:.1f}" x2="{right:.1f}" y2="{gy_line:.1f}" stroke="{t["g6"]}" stroke-width="1" stroke-dasharray="3 3"/>')
        txt(left - 8, gy_line + 3.5, str(tick), 10, t["g3"], mono=True, anchor="end")

    out.append(f'<line x1="{left:.1f}" y1="{bottom:.1f}" x2="{right:.1f}" y2="{bottom:.1f}" stroke="{t["g5"]}" stroke-width="1"/>')

    # Smooth area + polyline
    pts = [(center(i), bottom - bar_h(c)) for i, c in enumerate(counts)]
    area_d = f"M {pts[0][0]:.1f},{bottom:.1f} " + " ".join(f"L {x:.1f},{y:.1f}" for x, y in pts) + f" L {pts[-1][0]:.1f},{bottom:.1f} Z"
    line_d = "M " + " L ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    out.append(f'<path d="{area_d}" fill="url(#pulseAreaGrad)"/>')
    out.append(f'<path d="{line_d}" fill="none" stroke="{t["c4"]}" stroke-width="1.3" stroke-opacity="0.65"/>')

    # Connector lines for stable release badges
    for flag in flags:
        by = bottom - bar_h(counts[flag["i"]])
        out.append(
            f'<line x1="{flag["cx"]:.1f}" y1="{flag["y"] + 16:.1f}" x2="{flag["cx"]:.1f}" y2="{by - 3:.1f}" '
            f'stroke="{t["c3"]}" stroke-width="1" stroke-dasharray="2 2" opacity="0.75"/>'
        )

    # Rounded bars
    bar_w = 6.2
    for idx, count in enumerate(counts):
        cx = center(idx)
        out.append(f'<g><title>{window[idx].isoformat()} · {count} 次提交</title>')
        if count:
            h = bar_h(count)
            b_fill = mix(t["c4"], t["c2"] if not dark else t["c3"], (count / scale) ** 0.5)
            out.append(f'<rect x="{cx - bar_w / 2:.1f}" y="{bottom - h:.1f}" width="{bar_w}" height="{h:.1f}" rx="2.2" fill="{b_fill}"/>')
        else:
            out.append(f'<rect x="{cx - bar_w / 2:.1f}" y="{bottom - 2.2:.1f}" width="{bar_w}" height="2.2" rx="1.1" fill="{t["g6"]}"/>')
        out.append('</g>')

    # Pre-release amber rings on bar tips
    for idx in previews:
        cx = center(idx)
        cy = bottom - bar_h(counts[idx]) - 5.5
        out.append(f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="3" fill="{t["pre_bg"]}" stroke="{t["pre_border"]}" stroke-width="1.5"/>')

    # Stable release badges (PageSetupUpdate [正式版] badge style rx="3")
    for flag in flags:
        out.append(
            f'<rect x="{flag["x"]:.1f}" y="{flag["y"]:.1f}" width="{flag["w"]:.1f}" height="16" rx="3" '
            f'fill="{t["stable_bg"]}" stroke="{t["stable_border"]}" stroke-width="1"/>'
        )
        txt(flag["x"] + flag["w"] / 2, flag["y"] + 11.5, flag["label"], 10, t["stable_fg"], 700, mono=True, anchor="middle")

    # X-axis dates
    for idx, day in enumerate(window):
        if day.weekday() == 0 and abs(center(idx) - center(days - 1)) > 38:
            txt(center(idx), bottom + 17, day.strftime("%m/%d"), 10.5, t["g3"], mono=True, anchor="middle")
    txt(center(days - 1), bottom + 17, "今天", 10.5, t["c3"], 700, anchor="middle")

    # --- Row 2: 最近发布动态 (PCL SE MyCard + 3 clean release sub-cards with MyHint-style accent bars) ---
    vx, vy, vw, vh = 18, 244, 884, 144
    my_card(vx, vy, vw, vh, "最近发布动态")
    txt(vx + vw - 15, vy + 23, f"最新正式版 {latest_stable} · 共发布 {len(releases)} 个版本 · 自动提取 GitHub Releases 摘要", 11, t["g3"], anchor="end")

    gap = 12.0
    sub_w = (vw - 30.0 - gap * 2) / 3.0
    sub_h = 92.0
    sub_y = vy + 38.0
    for idx, rel in enumerate(releases[:3]):
        sx = vx + 15.0 + idx * (sub_w + gap)
        stable = not rel["prerelease"]
        is_current = idx == 0
        bg = t["cur_row_bg"] if is_current else t["row_bg"]
        border = t["cur_border"] if is_current else t["row_border"]
        bar_col = t["cur_border"] if is_current else (t["c3"] if stable else t["pre_border"])

        out.append(f'<g><title>{html.escape(rel["tag"] + " · " + rel["title"])}</title>')
        out.append(
            f'<rect x="{sx:.1f}" y="{sub_y:.1f}" width="{sub_w:.1f}" height="{sub_h:.1f}" rx="4" '
            f'fill="{bg}" stroke="{border}" stroke-width="1"/>'
        )
        # MyHint-style left vertical accent strip
        out.append(
            f'<rect x="{sx + 0.5:.1f}" y="{sub_y + 10:.1f}" width="3" height="{sub_h - 20:.1f}" rx="1.5" fill="{bar_col}"/>'
        )

        # Line 1: Version Tag + Channel Badge
        txt(sx + 14, sub_y + 22, rel["tag"], 14, t["c1"], 700, mono=True)
        ch_label = "正式版" if stable else "预发布版"
        ch_w = text_width(ch_label, 10.5) + 12.0
        ch_bg = t["stable_bg"] if stable else t["pre_bg"]
        ch_fg = t["stable_fg"] if stable else t["pre_fg"]
        bx = sx + sub_w - 12.0 - ch_w
        out.append(f'<rect x="{bx:.1f}" y="{sub_y + 9:.1f}" width="{ch_w:.1f}" height="17" rx="3" fill="{ch_bg}"/>')
        txt(bx + ch_w / 2, sub_y + 21.2, ch_label, 10.5, ch_fg, 600, anchor="middle")

        # Line 2: Release Date
        txt(sx + 14, sub_y + 40, rel["date"], 11, t["g3"], mono=True)

        # Lines 3-4: Word-wrapped Release Summary (never splits English words like PCL or .NET)
        for line_idx, line_str in enumerate(wrap_text(rel["title"], 12, sub_w - 26, 2)):
            txt(sx + 14, sub_y + 60 + line_idx * 17, line_str, 12, t["g2"])
        out.append('</g>')

    # --- Row 3: Star 增长趋势 (Cumulative Star Line Chart in PCL SE MyCard style) ---
    st_x, st_y, st_w, st_h = 18, 400, 884, 140
    my_card(st_x, st_y, st_w, st_h, "Star 增长趋势")

    window_start_iso = window[0].isoformat()
    base_stars = sum(cnt for d_iso, cnt in stars.items() if d_iso < window_start_iso)
    cum_stars: list[int] = []
    running = base_stars
    for d in window:
        running += stars.get(d.isoformat(), 0)
        cum_stars.append(running)
    total_stars = running
    recent_30_stars = sum(cnt for d_iso, cnt in stars.items() if 0 <= (today - dt.date.fromisoformat(d_iso)).days < 30)

    # Header pill badge next to "Star 增长趋势" title
    star_badge_label = f"★ {total_stars}"
    star_badge_w = text_width(star_badge_label, 10.5) + 14.0
    star_badge_x = st_x + 108.0
    star_badge_y = st_y + 10.0
    out.append(
        f'<rect x="{star_badge_x:.1f}" y="{star_badge_y:.1f}" width="{star_badge_w:.1f}" height="17" rx="3" '
        f'fill="{t["stable_bg"]}" stroke="{t["stable_border"]}" stroke-width="1"/>'
    )
    txt(
        star_badge_x + star_badge_w / 2,
        star_badge_y + 12.0,
        star_badge_label,
        10.5,
        t["stable_fg"],
        700,
        mono=True,
        anchor="middle",
    )

    txt(
        st_x + st_w - 15,
        st_y + 23,
        f"近 30 日 +{recent_30_stars} ★ · 随 GitHub Actions 自动同步",
        11,
        t["g3"],
        anchor="end",
    )

    s_left, s_right, s_top, s_bottom = 60.0, 868.0, 452.0, 514.0
    s_plot_h = s_bottom - s_top
    s_max = max(5, total_stars)
    s_slot = (s_right - s_left) / (days - 1)
    s_x = lambda idx: s_left + s_slot * idx
    s_y = lambda val: s_bottom - (val / s_max) * s_plot_h

    # Y-axis gridlines (0, mid, max)
    mid_tick = max(1, round(s_max / 2))
    for tick in (0, mid_tick, s_max):
        gy_s = s_y(tick)
        if tick > 0:
            out.append(
                f'<line x1="{s_left:.1f}" y1="{gy_s:.1f}" x2="{s_right:.1f}" y2="{gy_s:.1f}" '
                f'stroke="{t["g6"]}" stroke-width="1" stroke-dasharray="3 3"/>'
            )
        txt(s_left - 10, gy_s + 3.5, str(tick), 10, t["g3"], mono=True, anchor="end")

    out.append(f'<line x1="{s_left:.1f}" y1="{s_bottom:.1f}" x2="{s_right:.1f}" y2="{s_bottom:.1f}" stroke="{t["g5"]}" stroke-width="1"/>')

    # Area + Line for cumulative stars
    s_pts = [(s_x(i), s_y(v)) for i, v in enumerate(cum_stars)]
    s_area = f"M {s_pts[0][0]:.1f},{s_bottom:.1f} " + " ".join(f"L {x:.1f},{y:.1f}" for x, y in s_pts) + f" L {s_pts[-1][0]:.1f},{s_bottom:.1f} Z"
    s_line = "M " + " L ".join(f"{x:.1f},{y:.1f}" for x, y in s_pts)
    out.append(f'<path d="{s_area}" fill="url(#starAreaGrad)"/>')
    out.append(f'<path d="{s_line}" fill="none" stroke="{t["c3"]}" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>')

    # Points on days where stars increased + value labels
    for idx, d in enumerate(window):
        gained = stars.get(d.isoformat(), 0)
        val = cum_stars[idx]
        px_i, py_i = s_pts[idx]
        out.append(f'<g><title>{d.isoformat()} · 累计 {val} ★（当日 +{gained}）</title>')
        if idx == days - 1:
            out.append(f'<circle cx="{px_i:.1f}" cy="{py_i:.1f}" r="6.2" fill="{t["c3"]}" fill-opacity="0.20"/>')
            out.append(f'<circle cx="{px_i:.1f}" cy="{py_i:.1f}" r="3.4" fill="{t["c3"]}" stroke="{t["card_bg"]}" stroke-width="1.5"/>')
            txt(px_i, py_i - 8.5, str(val), 9.5, t["c2"], 700, mono=True, anchor="middle")
        elif gained > 0:
            out.append(f'<circle cx="{px_i:.1f}" cy="{py_i:.1f}" r="3.2" fill="{t["card_bg"]}" stroke="{t["c3"]}" stroke-width="1.8"/>')
            txt(px_i, py_i - 7.5, str(val), 9.5, t["g3"], 600, mono=True, anchor="middle")
        out.append('</g>')

    # X-axis dates
    for idx, d in enumerate(window):
        if d.weekday() == 0 and abs(s_x(idx) - s_x(days - 1)) > 38:
            txt(s_x(idx), s_bottom + 16, d.strftime("%m/%d"), 10.5, t["g3"], mono=True, anchor="middle")
    txt(s_x(days - 1), s_bottom + 16, "今天", 10.5, t["c3"], 700, anchor="middle")

    out.append('</g>')
    out.append(f'<rect x="2" y="2" width="916" height="552" rx="6" fill="none" stroke="{t["card_border"]}" stroke-width="1"/>')
    out.append('</svg>')
    return "\n".join(out) + "\n"


RENDERERS = {"pulse": render_pulse, "timeline": render_timeline}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", "Hydrogen-Peroxide/PCL-Symbio-Edition"))
    parser.add_argument("--output-dir", type=Path, default=ROOT / "docs/assets")
    parser.add_argument("--style", choices=sorted(RENDERERS), default="pulse")
    parser.add_argument("--date", help="Override today's YYYY-MM-DD date")
    args = parser.parse_args()

    today = dt.date.fromisoformat(args.date) if args.date else dt.datetime.now(CST).date()
    commits = collect_commits(ROOT)
    releases = collect_releases(args.repo, ROOT)
    stars = collect_stars(args.repo)
    render = RENDERERS[args.style]

    args.output_dir.mkdir(parents=True, exist_ok=True)
    if args.style == "pulse":
        light_svg = render(commits, releases, today, dark=False, stars=stars)
        dark_svg = render(commits, releases, today, dark=True, stars=stars)
    else:
        light_svg = render(commits, releases, today, dark=False)
        dark_svg = render(commits, releases, today, dark=True)
    (args.output_dir / "calendar-light.svg").write_text(light_svg, encoding="utf-8")
    (args.output_dir / "calendar-dark.svg").write_text(dark_svg, encoding="utf-8")
    print(f"Generated {args.style} calendar-light.svg and calendar-dark.svg in {args.output_dir}")


if __name__ == "__main__":
    main()


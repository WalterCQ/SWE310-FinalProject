#!/usr/bin/env python3
"""TaskFlow adapter for the vendored PPT Master runtime.

Input: markdown text from TaskFlow Agent.
Output: a PPTX generated from simple editable SVG slides through ppt-master's
svg_to_pptx runtime.
"""

from __future__ import annotations

import argparse
import html
import re
import subprocess
import sys
import textwrap
from pathlib import Path


def split_markdown(markdown: str) -> list[tuple[str, list[str]]]:
    lines = [line.rstrip() for line in markdown.splitlines()]
    slides: list[tuple[str, list[str]]] = []
    title = "TaskFlow AI Deck"
    body: list[str] = []

    for line in lines:
        heading = re.match(r"^#{1,3}\s+(.+)$", line)
        if heading:
            if body or slides:
                slides.append((title, body))
                body = []
            title = heading.group(1).strip()
            continue

        stripped = line.strip()
        if stripped:
            body.append(stripped)

    slides.append((title, body))
    return [(item_title, item_body[:8]) for item_title, item_body in slides[:10]]


def svg_text_lines(text: str, max_chars: int) -> list[str]:
    normalized = re.sub(r"\s+", " ", text).strip()
    return textwrap.wrap(normalized, width=max_chars) or [""]


def write_svg_slide(path: Path, title: str, body: list[str], index: int) -> None:
    title_lines = svg_text_lines(title, 36)[:2]
    content_lines: list[str] = []
    for item in body:
        marker = "• " if item.startswith(("-", "*")) else ""
        clean = re.sub(r"^[-*]\s+", "", item)
        wrapped = svg_text_lines(clean, 68)
        if wrapped:
            content_lines.append(marker + wrapped[0])
            content_lines.extend("  " + line for line in wrapped[1:3])
    content_lines = content_lines[:14]

    title_tspans = "\n".join(
        f'<tspan x="80" dy="{0 if idx == 0 else 48}">{html.escape(line)}</tspan>'
        for idx, line in enumerate(title_lines)
    )
    content_tspans = "\n".join(
        f'<tspan x="96" dy="{0 if idx == 0 else 34}">{html.escape(line)}</tspan>'
        for idx, line in enumerate(content_lines or ["No content was generated."])
    )

    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720">
  <rect x="0" y="0" width="1280" height="720" fill="#f8fafc"/>
  <rect x="0" y="0" width="1280" height="90" fill="#1f2937"/>
  <text x="80" y="58" font-family="Aptos, Arial, sans-serif" font-size="24" fill="#e5e7eb">TaskFlow AI</text>
  <text x="1180" y="58" text-anchor="end" font-family="Aptos, Arial, sans-serif" font-size="18" fill="#cbd5e1">{index:02d}</text>
  <text id="title" x="80" y="170" font-family="Aptos, Arial, sans-serif" font-size="42" font-weight="700" fill="#111827">{title_tspans}</text>
  <rect x="80" y="245" width="1120" height="370" rx="18" fill="#ffffff" stroke="#d1d5db"/>
  <text id="body" x="96" y="305" font-family="Aptos, Arial, sans-serif" font-size="25" fill="#374151">{content_tspans}</text>
</svg>
'''
    path.write_text(svg, encoding="utf-8")


def build_project(markdown_path: Path, project_dir: Path) -> None:
    markdown = markdown_path.read_text(encoding="utf-8")
    svg_dir = project_dir / "svg_output"
    notes_dir = project_dir / "notes"
    svg_dir.mkdir(parents=True, exist_ok=True)
    notes_dir.mkdir(parents=True, exist_ok=True)

    for index, (title, body) in enumerate(split_markdown(markdown), start=1):
        stem = f"{index:02d}_{re.sub(r'[^a-zA-Z0-9]+', '_', title).strip('_')[:40] or 'slide'}"
        write_svg_slide(svg_dir / f"{stem}.svg", title, body, index)
        notes_dir.joinpath(f"{stem}.md").write_text(
            "\n".join([f"# {title}", "", *body]),
            encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate a PPTX from TaskFlow Agent markdown.")
    parser.add_argument("input_markdown")
    parser.add_argument("output_pptx")
    parser.add_argument("--workdir", default=None)
    args = parser.parse_args()

    skill_dir = Path(__file__).resolve().parent
    markdown_path = Path(args.input_markdown).resolve()
    output_path = Path(args.output_pptx).resolve()
    project_dir = Path(args.workdir).resolve() if args.workdir else output_path.parent / "ppt-master-project"
    project_dir.mkdir(parents=True, exist_ok=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    build_project(markdown_path, project_dir)

    command = [
        sys.executable,
        str(skill_dir / "scripts" / "svg_to_pptx.py"),
        str(project_dir),
        "-s",
        "output",
        "-f",
        "ppt169",
        "--no-compat",
        "--only",
        "native",
        "-o",
        str(output_path),
        "-q",
    ]
    completed = subprocess.run(command, text=True, capture_output=True, check=False)
    if completed.returncode != 0:
        sys.stderr.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        return completed.returncode

    if not output_path.exists() or output_path.stat().st_size == 0:
        sys.stderr.write(f"ppt-master did not create output: {output_path}\n")
        return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

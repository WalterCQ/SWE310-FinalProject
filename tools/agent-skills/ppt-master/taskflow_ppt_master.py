#!/usr/bin/env python3
"""TaskFlow adapter for PPT Master.

The worker passes markdown from DeckSubAgent. This adapter builds a PPT Master
project (svg_output + notes) and delegates the final editable deck generation to
the upstream svg_to_pptx runtime.
"""

from __future__ import annotations

import argparse
import html
import re
import subprocess
import sys
import textwrap
from pathlib import Path
from zipfile import ZipFile


CANVAS_WIDTH = 1280
CANVAS_HEIGHT = 720
MAX_SLIDES = 12
MAX_BULLETS = 4


def strip_front_matter(lines: list[str]) -> list[str]:
    if not lines or lines[0].strip() != "---":
        return lines

    for index, line in enumerate(lines[1:], start=1):
        if line.strip() == "---":
            return lines[index + 1 :]

    return lines


def split_markdown(markdown: str) -> list[dict[str, object]]:
    lines = strip_front_matter([line.rstrip().lstrip("\ufeff") for line in markdown.splitlines()])
    slides: list[dict[str, object]] = []
    current_title: str | None = None
    current_body: list[str] = []

    def flush_slide() -> None:
        nonlocal current_title, current_body
        if current_title is None:
            return

        bullets, sources, notes = parse_body(current_body)
        slides.append({
            "title": current_title,
            "bullets": bullets,
            "sources": sources,
            "notes": notes,
        })

    for line in lines:
        heading = re.match(r"^#{1,3}\s+(.+)$", line)
        if heading:
            flush_slide()
            current_title = heading.group(1).strip()
            current_body = []
            continue

        stripped = line.strip()
        if stripped:
            current_body.append(stripped)

    flush_slide()
    if not slides:
        raise ValueError("Deck markdown did not contain any non-empty slides.")

    titles = [str(slide["title"]) for slide in slides]
    if len(titles) != len(set(titles)):
        raise ValueError("Deck markdown contains duplicate slide titles.")

    return slides[:MAX_SLIDES]


def parse_body(lines: list[str]) -> tuple[list[str], str, str]:
    bullets: list[str] = []
    sources = ""
    notes = ""

    for line in lines:
        clean = re.sub(r"^[-*]\s+", "", line).strip()
        if not clean:
            continue

        lowered = clean.lower()
        if lowered.startswith("notes:"):
            notes = clean.partition(":")[2].strip()
            continue
        if lowered.startswith("sources:"):
            sources = clean.partition(":")[2].strip()
            continue

        bullets.append(clean)

    if not bullets:
        raise ValueError("Each slide must include at least one visible bullet.")

    return bullets[:MAX_BULLETS], sources or "Source list unavailable", notes


def wrap_lines(text: str, width: int, max_lines: int) -> list[str]:
    normalized = re.sub(r"\s+", " ", text).strip()
    return textwrap.wrap(normalized, width=width)[:max_lines] or [""]


def safe_stem(index: int, title: str) -> str:
    slug = re.sub(r"[^a-zA-Z0-9]+", "_", title).strip("_").lower()[:46] or "slide"
    return f"{index:02d}_{slug}"


def svg_text(x: int, y: int, text: str, size: int, fill: str, weight: int = 400, anchor: str = "start") -> str:
    return (
        f'<text x="{x}" y="{y}" text-anchor="{anchor}" '
        f'font-family="Aptos, Arial, sans-serif" font-size="{size}" '
        f'font-weight="{weight}" fill="{fill}">{html.escape(text)}</text>'
    )


def write_svg_slide(path: Path, slide: dict[str, object], index: int) -> None:
    title = str(slide["title"])
    bullets = [str(item) for item in slide["bullets"]]
    sources = str(slide["sources"])

    title_lines = wrap_lines(title, 36, 2)
    bullet_lines: list[tuple[str, bool]] = []
    for bullet in bullets:
        wrapped = wrap_lines(bullet, 70, 3)
        bullet_lines.append((wrapped[0], True))
        bullet_lines.extend((line, False) for line in wrapped[1:])

    bullet_lines = bullet_lines[:12]
    title_svg = "\n  ".join(
        svg_text(86, 162 + offset * 48, line, 42, "#101828", 760)
        for offset, line in enumerate(title_lines)
    )
    bullet_svg = "\n  ".join(
        (
            f'<circle cx="106" cy="{302 + offset * 37 - 8}" r="5" fill="#0e9384"/>'
            if is_first
            else ""
        )
        + svg_text(126 if is_first else 140, 302 + offset * 37, line, 25, "#344054", 430)
        for offset, (line, is_first) in enumerate(bullet_lines)
    )
    sources_text = "; ".join(wrap_lines(sources, 82, 2))

    path.write_text(
        f'''<svg xmlns="http://www.w3.org/2000/svg" width="{CANVAS_WIDTH}" height="{CANVAS_HEIGHT}" viewBox="0 0 {CANVAS_WIDTH} {CANVAS_HEIGHT}">
  <rect id="background" x="0" y="0" width="1280" height="720" fill="#fbfaf7"/>
  <rect id="header" x="0" y="0" width="1280" height="88" fill="#123f3c"/>
  <rect id="accent" x="86" y="112" width="78" height="8" rx="4" fill="#0e9384"/>
  {svg_text(86, 56, "TaskFlow AI", 23, "#e6fffb", 760)}
  {svg_text(1188, 56, f"{index:02d}", 18, "#b8e5df", 600, "end")}
  <g id="title">
  {title_svg}
  </g>
  <g id="content-card">
    <rect x="86" y="244" width="1108" height="360" rx="16" fill="#ffffff" stroke="#d8d5ce" stroke-width="2"/>
  </g>
  <g id="bullet-list">
  {bullet_svg}
  </g>
  <g id="source-band">
    <rect x="106" y="556" width="650" height="34" rx="17" fill="#e6f7f5" stroke="#b3e1dc" stroke-width="1"/>
    {svg_text(126, 579, "Sources: " + sources_text, 14, "#123f3c", 700)}
  </g>
  {svg_text(86, 674, "Generated from approved TaskFlow context", 17, "#667085", 500)}
</svg>
''',
        encoding="utf-8",
    )


def build_project(markdown_path: Path, project_dir: Path) -> int:
    slides = split_markdown(markdown_path.read_text(encoding="utf-8"))
    svg_dir = project_dir / "svg_output"
    notes_dir = project_dir / "notes"
    svg_dir.mkdir(parents=True, exist_ok=True)
    notes_dir.mkdir(parents=True, exist_ok=True)

    for index, slide in enumerate(slides, start=1):
        stem = safe_stem(index, str(slide["title"]))
        write_svg_slide(svg_dir / f"{stem}.svg", slide, index)
        notes = str(slide["notes"]) or "\n".join(str(item) for item in slide["bullets"])
        notes_dir.joinpath(f"{stem}.md").write_text(notes, encoding="utf-8")

    return len(slides)


def run_svg_to_pptx(skill_dir: Path, project_dir: Path, output_path: Path) -> str:
    script = skill_dir / "scripts" / "svg_to_pptx.py"
    if not script.exists():
        raise ValueError("ppt-master svg_to_pptx.py is missing.")

    command = [
        sys.executable,
        str(script),
        str(project_dir),
        "-o",
        str(output_path),
        "-s",
        "output",
        "-f",
        "ppt169",
        "--only",
        "native",
        "--no-compat",
        "-a",
        "none",
        "-t",
        "none",
    ]
    completed = subprocess.run(command, cwd=str(skill_dir), text=True, capture_output=True, check=False)
    output = "\n".join(part for part in [completed.stdout.strip(), completed.stderr.strip()] if part)
    if completed.returncode != 0:
        raise ValueError(f"ppt-master svg_to_pptx failed with exit code {completed.returncode}: {output[-2000:]}")

    return output


def count_pptx_slides(path: Path) -> int:
    with ZipFile(path) as archive:
        return sum(
            1
            for name in archive.namelist()
            if name.startswith("ppt/slides/slide") and name.endswith(".xml")
        )


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate editable PPTX from TaskFlow Agent markdown.")
    parser.add_argument("input_markdown")
    parser.add_argument("output_pptx")
    parser.add_argument("--workdir", default=None)
    args = parser.parse_args()

    skill_dir = Path(__file__).resolve().parent
    markdown_path = Path(args.input_markdown).resolve()
    output_path = Path(args.output_pptx).resolve()
    project_dir = Path(args.workdir).resolve() if args.workdir else output_path.parent / "taskflow_ppt169"
    project_dir.mkdir(parents=True, exist_ok=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        expected_slides = build_project(markdown_path, project_dir)
        execution_log = run_svg_to_pptx(skill_dir, project_dir, output_path)
    except ValueError as exc:
        sys.stderr.write(f"{exc}\n")
        return 3

    if not output_path.exists() or output_path.stat().st_size == 0:
        sys.stderr.write(f"ppt-master did not create output: {output_path}\n")
        return 2

    actual_slides = count_pptx_slides(output_path)
    if actual_slides != expected_slides:
        sys.stderr.write(f"ppt-master rendered {actual_slides} slides from {expected_slides} markdown slides.\n")
        return 4

    if execution_log:
        print(execution_log)
    print(f"TaskFlow PPT Master generated {actual_slides} editable slides.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

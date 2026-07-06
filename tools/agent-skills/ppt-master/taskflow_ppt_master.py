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
import sys
import textwrap
from zipfile import ZipFile
from pathlib import Path

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import PP_ALIGN
from pptx.util import Inches, Pt


def strip_front_matter(lines: list[str]) -> list[str]:
    if not lines or lines[0].strip() != "---":
        return lines

    for index, line in enumerate(lines[1:], start=1):
        if line.strip() == "---":
            return lines[index + 1 :]

    return lines


def split_markdown(markdown: str) -> list[tuple[str, list[str]]]:
    lines = strip_front_matter([line.rstrip().lstrip("\ufeff") for line in markdown.splitlines()])
    slides: list[tuple[str, list[str]]] = []
    title: str | None = None
    body: list[str] = []

    def flush_slide() -> None:
        if title is None:
            return

        if not body:
            raise ValueError(f"Slide '{title}' has no content.")

        slides.append((title, body.copy()))

    for line in lines:
        heading = re.match(r"^#{1,3}\s+(.+)$", line)
        if heading:
            flush_slide()
            body = []
            title = heading.group(1).strip()
            continue

        stripped = line.strip()
        if stripped:
            body.append(stripped)

    flush_slide()
    if not slides:
        raise ValueError("No non-empty slides were generated from markdown.")

    return [(item_title, item_body[:8]) for item_title, item_body in slides[:10]]


def svg_text_lines(text: str, max_chars: int) -> list[str]:
    normalized = re.sub(r"\s+", " ", text).strip()
    return textwrap.wrap(normalized, width=max_chars) or [""]


def write_svg_slide(path: Path, title: str, body: list[str], index: int) -> None:
    title_lines = svg_text_lines(title, 36)[:2]
    content_lines: list[str] = []
    visible_body = [item for item in body if not item.lower().startswith("notes:")]
    for item in visible_body:
        marker = "• " if item.startswith(("-", "*")) else ""
        clean = re.sub(r"^[-*]\s+", "", item)
        wrapped = svg_text_lines(clean, 68)
        if wrapped:
            content_lines.append(marker + wrapped[0])
            content_lines.extend("  " + line for line in wrapped[1:3])
    content_lines = content_lines[:14]
    if not content_lines:
        raise ValueError(f"Slide '{title}' has no visible content.")

    title_text = "\n".join(
        f'<text x="80" y="{170 + idx * 48}" font-family="Aptos, Arial, sans-serif" font-size="42" font-weight="700" fill="#111827">{html.escape(line)}</text>'
        for idx, line in enumerate(title_lines)
    )
    content_text = "\n".join(
        f'<text x="96" y="{305 + idx * 34}" font-family="Aptos, Arial, sans-serif" font-size="25" fill="#374151">{html.escape(line)}</text>'
        for idx, line in enumerate(content_lines)
    )

    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720">
  <rect x="0" y="0" width="1280" height="720" fill="#fbfaf7"/>
  <rect x="0" y="0" width="1280" height="88" fill="#123f3c"/>
  <rect x="80" y="126" width="74" height="8" rx="4" fill="#00a99d"/>
  <text x="80" y="56" font-family="Aptos, Arial, sans-serif" font-size="23" font-weight="700" fill="#e6fffb">TaskFlow AI</text>
  <text x="1180" y="56" text-anchor="end" font-family="Aptos, Arial, sans-serif" font-size="18" fill="#b8e5df">{index:02d}</text>
  {title_text}
  <rect x="80" y="245" width="1120" height="370" rx="12" fill="#ffffff" stroke="#d8d5ce"/>
  {content_text}
  <text x="80" y="668" font-family="Aptos, Arial, sans-serif" font-size="17" fill="#6b7280">Generated from approved TaskFlow context</text>
</svg>
'''
    path.write_text(svg, encoding="utf-8")


def parse_slide_body(body: list[str]) -> tuple[list[str], str, str]:
    bullets: list[str] = []
    sources = ""
    notes = ""

    for item in body:
        clean = re.sub(r"^[-*]\s+", "", item).strip()
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
        raise ValueError("Slide has no visible bullet content.")
    if len(bullets) > 4:
        raise ValueError("Slide has more than 4 visible bullets.")

    return bullets, sources or "S1", notes


def add_textbox(
    slide,
    left,
    top,
    width,
    height,
    text: str,
    font_size: int,
    color: RGBColor,
    bold: bool = False,
    align=PP_ALIGN.LEFT,
):
    shape = slide.shapes.add_textbox(left, top, width, height)
    frame = shape.text_frame
    frame.clear()
    paragraph = frame.paragraphs[0]
    paragraph.alignment = align
    run = paragraph.add_run()
    run.text = text
    run.font.name = "Aptos"
    run.font.size = Pt(font_size)
    run.font.bold = bold
    run.font.color.rgb = color
    return shape


def build_pptx(markdown_path: Path, output_path: Path) -> None:
    slides = split_markdown(markdown_path.read_text(encoding="utf-8"))
    titles = [title for title, _ in slides]
    if len(titles) != len(set(titles)):
        raise ValueError("Deck contains duplicate slide titles.")

    prs = Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    blank_layout = prs.slide_layouts[6]

    teal = RGBColor(18, 63, 60)
    accent = RGBColor(0, 169, 157)
    ink = RGBColor(17, 24, 39)
    muted = RGBColor(75, 85, 99)
    surface = RGBColor(255, 255, 255)
    background = RGBColor(251, 250, 247)
    border = RGBColor(216, 213, 206)

    for index, (title, body) in enumerate(slides, start=1):
        bullets, sources, notes = parse_slide_body(body)
        slide = prs.slides.add_slide(blank_layout)
        slide.background.fill.solid()
        slide.background.fill.fore_color.rgb = background

        header = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, 0, 0, prs.slide_width, Inches(0.92))
        header.fill.solid()
        header.fill.fore_color.rgb = teal
        header.line.fill.background()

        add_textbox(slide, Inches(0.8), Inches(0.24), Inches(3.0), Inches(0.35), "TaskFlow AI", 18, RGBColor(230, 255, 251), bold=True)
        add_textbox(slide, Inches(11.65), Inches(0.24), Inches(0.9), Inches(0.35), f"{index:02d}", 14, RGBColor(184, 229, 223), align=PP_ALIGN.RIGHT)

        accent_bar = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(1.3), Inches(0.76), Inches(0.08))
        accent_bar.fill.solid()
        accent_bar.fill.fore_color.rgb = accent
        accent_bar.line.fill.background()

        add_textbox(slide, Inches(0.8), Inches(1.62), Inches(11.7), Inches(0.9), title, 34, ink, bold=True)

        card = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(0.8), Inches(2.55), Inches(11.7), Inches(3.75))
        card.fill.solid()
        card.fill.fore_color.rgb = surface
        card.line.color.rgb = border
        card.line.width = Pt(1)

        bullet_box = slide.shapes.add_textbox(Inches(1.15), Inches(2.9), Inches(10.95), Inches(2.65))
        frame = bullet_box.text_frame
        frame.clear()
        frame.word_wrap = True
        for bullet_index, bullet in enumerate(bullets):
            paragraph = frame.paragraphs[0] if bullet_index == 0 else frame.add_paragraph()
            paragraph.text = f"- {bullet}"
            paragraph.font.name = "Aptos"
            paragraph.font.size = Pt(22)
            paragraph.font.color.rgb = muted
            paragraph.space_after = Pt(10)

        source_box = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(1.15), Inches(5.6), Inches(4.6), Inches(0.38))
        source_box.fill.solid()
        source_box.fill.fore_color.rgb = RGBColor(230, 247, 245)
        source_box.line.color.rgb = RGBColor(179, 225, 220)
        add_textbox(slide, Inches(1.28), Inches(5.68), Inches(4.3), Inches(0.22), f"Sources: {sources}", 11, teal, bold=True)

        add_textbox(slide, Inches(0.8), Inches(6.85), Inches(6.8), Inches(0.25), "Generated from approved TaskFlow context", 12, RGBColor(107, 114, 128))

        notes_frame = slide.notes_slide.notes_text_frame
        notes_frame.clear()
        notes_frame.text = notes or "\n".join(bullets)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    prs.save(output_path)

    actual_slide_count = count_pptx_slides(output_path)
    if actual_slide_count != len(slides):
        raise ValueError(f"ppt-master rendered {actual_slide_count} slides from {len(slides)} markdown slides.")


def count_pptx_slides(path: Path) -> int:
    with ZipFile(path) as archive:
        return sum(
            1
            for name in archive.namelist()
            if name.startswith("ppt/slides/slide") and name.endswith(".xml")
        )


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

    try:
        build_project(markdown_path, project_dir)
        build_pptx(markdown_path, output_path)
    except ValueError as exc:
        sys.stderr.write(f"{exc}\n")
        return 3

    if not output_path.exists() or output_path.stat().st_size == 0:
        sys.stderr.write(f"ppt-master did not create output: {output_path}\n")
        return 2

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

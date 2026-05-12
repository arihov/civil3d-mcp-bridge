"""Render USER_MANUAL.md -> USER_MANUAL.pdf using markdown-pdf.

Run:
    python docs/render_manual.py
"""
from pathlib import Path
from markdown_pdf import MarkdownPdf, Section

HERE = Path(__file__).resolve().parent
MD = HERE / "USER_MANUAL.md"
OUT = HERE / "USER_MANUAL.pdf"

CSS = """
body { font-family: 'Segoe UI', Calibri, sans-serif; line-height: 1.45; color: #1a1a1a; }
h1 { color: #0b3d91; border-bottom: 2px solid #0b3d91; padding-bottom: 4px; font-size: 22pt; }
h2 { color: #0b3d91; margin-top: 1.2em; font-size: 16pt; }
h3 { color: #555; font-size: 13pt; }
code, pre { font-family: 'Consolas', 'Courier New', monospace; }
pre { background: #f4f4f4; padding: 10px; border-left: 3px solid #0b3d91; font-size: 9pt; overflow-wrap: anywhere; }
code { background: #f4f4f4; padding: 1px 4px; border-radius: 3px; font-size: 9pt; }
table { border-collapse: collapse; width: 100%; margin: 8px 0; font-size: 9pt; }
th, td { border: 1px solid #ccc; padding: 4px 8px; text-align: left; vertical-align: top; }
th { background: #eef; }
blockquote { border-left: 3px solid #999; margin: 0 0 0 4px; padding: 0 12px; color: #444; }
a { color: #0b3d91; }
"""


def main() -> None:
    text = MD.read_text(encoding="utf-8")
    # Drop the YAML frontmatter — markdown-pdf doesn't parse it.
    if text.startswith("---"):
        end = text.find("\n---", 3)
        if end > 0:
            text = text[end + 4 :].lstrip()

    pdf = MarkdownPdf(toc_level=2, optimize=True)
    pdf.meta["title"] = "civil3d-mcp — User Manual"
    pdf.meta["author"] = "civil3d-mcp 0.2.0"
    # markdown-pdf splits on Sections; one big section is simplest
    # (the manual already uses \newpage hints which markdown-pdf ignores
    # but does flow well on letter-sized pages).
    pdf.add_section(Section(text, toc=True), user_css=CSS)
    pdf.save(str(OUT))
    print(f"wrote {OUT} ({OUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()

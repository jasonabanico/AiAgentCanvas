"""
Generate AI Agent Canvas Guide PDF from markdown documentation files.
Reads docs/guide-*.md, appendix-*.md, reference-*.md and produces a single PDF.
"""
import os
import re
import html
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
    KeepTogether,
)
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY

WIDTH, HEIGHT = letter
MARGIN = 0.75 * inch
DOCS_DIR = os.path.dirname(os.path.abspath(__file__))

styles = getSampleStyleSheet()

styles.add(ParagraphStyle('CoverTitle', parent=styles['Title'], fontSize=32, leading=40,
    textColor=HexColor('#1a1a2e'), spaceAfter=12, alignment=TA_CENTER))
styles.add(ParagraphStyle('CoverSubtitle', parent=styles['Normal'], fontSize=14, leading=20,
    textColor=HexColor('#4a4a6a'), alignment=TA_CENTER, spaceAfter=6))
styles.add(ParagraphStyle('SectionTitle', parent=styles['Heading1'], fontSize=24, leading=30,
    textColor=HexColor('#1a1a2e'), spaceBefore=0, spaceAfter=20))
styles.add(ParagraphStyle('ChapterTitle', parent=styles['Heading1'], fontSize=18, leading=24,
    textColor=HexColor('#2d2d4e'), spaceBefore=16, spaceAfter=10))
styles.add(ParagraphStyle('Sub', parent=styles['Heading2'], fontSize=14, leading=18,
    textColor=HexColor('#3d3d5c'), spaceBefore=14, spaceAfter=8))
styles.add(ParagraphStyle('Sub3', parent=styles['Heading3'], fontSize=12, leading=16,
    textColor=HexColor('#4a4a6a'), spaceBefore=10, spaceAfter=6))
styles.add(ParagraphStyle('Sub4', parent=styles['Normal'], fontSize=11, leading=15,
    textColor=HexColor('#4a4a6a'), spaceBefore=8, spaceAfter=6, fontName='Helvetica-Bold'))
styles.add(ParagraphStyle('Body', parent=styles['Normal'], fontSize=10, leading=14,
    alignment=TA_JUSTIFY, spaceAfter=8))
styles.add(ParagraphStyle('CodeBlock', parent=styles['Code'], fontSize=8.5, leading=11,
    backColor=HexColor('#f5f5f5'), borderColor=HexColor('#e0e0e0'),
    borderWidth=0.5, borderPadding=6, spaceAfter=10, leftIndent=12))
styles.add(ParagraphStyle('BulletItem', parent=styles['Normal'], fontSize=10, leading=14,
    leftIndent=24, bulletIndent=12, spaceAfter=4))
styles.add(ParagraphStyle('TocSection', parent=styles['Normal'], fontSize=12, leading=18,
    textColor=HexColor('#1a1a2e'), fontName='Helvetica-Bold', spaceAfter=4, spaceBefore=10))
styles.add(ParagraphStyle('TocItem', parent=styles['Normal'], fontSize=10, leading=15,
    textColor=HexColor('#4a4a6a'), leftIndent=20, spaceAfter=2))


def header_footer(canvas, doc):
    canvas.saveState()
    canvas.setFont('Helvetica', 8)
    canvas.setFillColor(HexColor('#999999'))
    canvas.drawString(MARGIN, 0.5 * inch, f"{doc.page}")
    canvas.drawRightString(WIDTH - MARGIN, 0.5 * inch, "AI Agent Canvas")
    canvas.restoreState()


def escape(text):
    return text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def inline_format(text):
    text = escape(text)
    text = re.sub(r'\*\*(.+?)\*\*', r'<b>\1</b>', text)
    text = re.sub(r'`([^`]+)`', r'<font face="Courier" size="9">\1</font>', text)
    text = re.sub(r'\[([^\]]+)\]\([^)]+\)', r'\1', text)
    return text


def P(text, style='Body'):
    return Paragraph(text, styles[style])


def code(text):
    return Paragraph(escape(text), styles['CodeBlock'])


def bullet(text):
    return Paragraph(f"•  {text}", styles['BulletItem'])


def make_table(headers, rows, col_widths=None):
    header_paras = [Paragraph(f"<b>{escape(h)}</b>", styles['Normal']) for h in headers]
    data_rows = []
    for row in rows:
        data_rows.append([Paragraph(escape(str(c)), styles['Normal']) for c in row])
    data = [header_paras] + data_rows
    if not col_widths:
        w = (WIDTH - 2 * MARGIN - 20) / len(headers)
        col_widths = [w] * len(headers)
    t = Table(data, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), HexColor('#2d2d4e')),
        ('TEXTCOLOR', (0, 0), (-1, 0), HexColor('#ffffff')),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, 0), 9),
        ('FONTSIZE', (0, 1), (-1, -1), 9),
        ('ALIGN', (0, 0), (-1, -1), 'LEFT'),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('GRID', (0, 0), (-1, -1), 0.5, HexColor('#dddddd')),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [HexColor('#ffffff'), HexColor('#f9f9f9')]),
        ('TOPPADDING', (0, 0), (-1, -1), 6),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ('RIGHTPADDING', (0, 0), (-1, -1), 8),
    ]))
    return t


def md_to_story(md_path, is_first=False):
    """Convert a markdown file to a list of reportlab flowables."""
    with open(md_path, 'r', encoding='utf-8') as f:
        lines = f.read().strip().split('\n')

    elements = []
    if not is_first:
        elements.append(PageBreak())

    in_code = False
    code_lines = []
    table_rows = []
    list_items = []

    def flush_code():
        nonlocal in_code, code_lines
        if code_lines:
            elements.append(code('\n'.join(code_lines)))
            code_lines = []
        in_code = False

    def flush_table():
        nonlocal table_rows
        if not table_rows:
            return
        headers = [c.strip() for c in table_rows[0]]
        data = []
        for row in table_rows[2:]:
            data.append([c.strip() for c in row])
        if data:
            elements.append(make_table(headers, data))
            elements.append(Spacer(1, 6))
        table_rows = []

    def flush_list():
        nonlocal list_items
        for item in list_items:
            elements.append(bullet(inline_format(item)))
        list_items = []

    for line in lines:
        stripped = line.strip()

        # Code block boundaries
        if stripped.startswith('```'):
            if in_code:
                flush_code()
            else:
                flush_table()
                flush_list()
                in_code = True
            continue

        if in_code:
            code_lines.append(line)
            continue

        # Empty lines
        if not stripped:
            flush_table()
            flush_list()
            continue

        # Table rows
        if '|' in stripped and stripped.startswith('|'):
            flush_list()
            cells = [c for c in stripped.split('|')[1:-1]]
            table_rows.append(cells)
            continue
        elif table_rows:
            flush_table()

        # List items
        if re.match(r'^[-*] ', stripped):
            flush_table()
            list_items.append(stripped[2:])
            continue
        elif re.match(r'^\d+\. ', stripped):
            flush_table()
            list_items.append(re.sub(r'^\d+\. ', '', stripped))
            continue
        elif list_items and (stripped.startswith('  ') or stripped.startswith('\t')):
            list_items[-1] += ' ' + stripped.strip()
            continue
        else:
            flush_list()

        # Headings
        if stripped.startswith('# '):
            text = re.sub(r'\s*\{#[^}]+\}', '', stripped[2:])
            elements.append(P(inline_format(text), 'SectionTitle'))
        elif stripped.startswith('## '):
            text = re.sub(r'\s*\{#[^}]+\}', '', stripped[3:])
            elements.append(P(inline_format(text), 'ChapterTitle'))
        elif stripped.startswith('### '):
            text = re.sub(r'\s*\{#[^}]+\}', '', stripped[4:])
            elements.append(P(inline_format(text), 'Sub'))
        elif stripped.startswith('#### '):
            text = re.sub(r'\s*\{#[^}]+\}', '', stripped[5:])
            elements.append(P(inline_format(text), 'Sub4'))
        elif stripped.startswith('> '):
            elements.append(P(f"<i>{inline_format(stripped[2:])}</i>", 'Body'))
        elif stripped.startswith('---'):
            pass
        else:
            elements.append(P(inline_format(stripped)))

    flush_code()
    flush_table()
    flush_list()

    return elements


def main():
    SECTIONS = [
        ('guide-01-introduction.md', '1. Introduction'),
        ('guide-02-ai-agents.md', '2. AI Agents'),
        ('guide-03-features.md', '3. Features'),
        ('guide-04-libraries.md', '4. Libraries'),
        ('guide-05-building-an-agent.md', '5. Building an Agent'),
        ('guide-06-context-domains.md', '6. Context Domains'),
        ('guide-07-mcp-and-rag.md', '7. MCP and RAG'),
        ('guide-08-multi-agent.md', '8. Multi-Agent Setup'),
        ('guide-09-architecture.md', '9. Architecture'),
        ('guide-10-behavior-patterns.md', '10. Behavior Patterns'),
        ('appendix-user-guide.md', 'Appendix: User Guide'),
        ('reference-agui-protocol.md', 'Reference: AG-UI Protocol'),
        ('reference-security.md', 'Reference: Security and Governance'),
        ('reference-internals.md', 'Reference: Platform Internals'),
    ]

    story = []

    # Cover page
    story.append(Spacer(1, 2.5 * inch))
    story.append(P("AI Agent Canvas", 'CoverTitle'))
    story.append(P("Complete Reference Guide", 'CoverSubtitle'))
    story.append(Spacer(1, 0.5 * inch))
    story.append(P("Build intelligent AI agents with .NET 9 and Microsoft Agent Framework.", 'CoverSubtitle'))
    story.append(P("From a single standalone agent to a coordinated multi-agent ecosystem.", 'CoverSubtitle'))
    story.append(PageBreak())

    # Table of contents
    story.append(P("Table of Contents", 'SectionTitle'))
    story.append(Spacer(1, 12))
    story.append(P("Guide", 'TocSection'))
    for filename, title in SECTIONS[:10]:
        story.append(P(title, 'TocItem'))
    story.append(P("Appendix", 'TocSection'))
    story.append(P("User Guide", 'TocItem'))
    story.append(P("Reference", 'TocSection'))
    for filename, title in SECTIONS[11:]:
        story.append(P(title.replace('Reference: ', ''), 'TocItem'))
    story.append(PageBreak())

    # Process each section
    for i, (filename, title) in enumerate(SECTIONS):
        md_path = os.path.join(DOCS_DIR, filename)
        if not os.path.exists(md_path):
            print(f"  SKIP {filename} (not found)")
            continue
        print(f"  Processing {filename}")
        elements = md_to_story(md_path, is_first=(i == 0))
        story.extend(elements)

    # Build PDF
    output_path = os.path.join(DOCS_DIR, 'guides', 'AI-Agent-Canvas-Guide.pdf')
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    doc = SimpleDocTemplate(
        output_path,
        pagesize=letter,
        leftMargin=MARGIN, rightMargin=MARGIN,
        topMargin=MARGIN, bottomMargin=MARGIN,
        title="AI Agent Canvas - Complete Reference Guide",
        author="AI Agent Canvas"
    )
    doc.build(story, onFirstPage=header_footer, onLaterPages=header_footer)
    print(f"\nPDF generated: {output_path}")
    print(f"Pages: {doc.page}")


if __name__ == '__main__':
    main()

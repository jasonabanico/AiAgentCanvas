import os
import re
from bs4 import BeautifulSoup, NavigableString, Tag
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch, cm
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
    KeepTogether, Flowable, Preformatted
)
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_JUSTIFY
from reportlab.pdfgen import canvas as pdfcanvas

WEBSITE_DIR = os.path.join(os.path.dirname(__file__), "website")
OUTPUT_PATH = os.path.join(os.path.dirname(__file__), "AI-Agent-Canvas-Guide.pdf")

COLOR_PRIMARY = HexColor("#3b82f6")
COLOR_ACCENT = HexColor("#8b5cf6")
COLOR_TEXT = HexColor("#1e293b")
COLOR_MUTED = HexColor("#475569")
COLOR_LIGHT = HexColor("#f1f5f9")
COLOR_BORDER = HexColor("#cbd5e1")
COLOR_CODE_BG = HexColor("#f8fafc")
COLOR_WHITE = HexColor("#ffffff")

SECTIONS = [
    {
        "title": "AI Agents",
        "dir": "ai-agents",
        "files": [
            "what-are-ai-agents.html",
            "microsoft-agent-framework.html",
            "tools-and-skills.html",
            "model-context-protocol.html",
            "context-providers.html",
            "ag-ui-protocol.html",
        ]
    },
    {
        "title": "Use Cases",
        "dir": "use-cases",
        "files": [
            "index.html",
            "financial-services.html",
            "healthcare.html",
            "legal.html",
            "e-commerce.html",
            "it-operations.html",
        ]
    },
    {
        "title": "User Guide",
        "dir": "user-guide",
        "files": [
            "getting-started.html",
            "configuration.html",
            "chat-interface.html",
            "personas.html",
            "skills-and-tools.html",
            "scheduling.html",
            "workflows.html",
            "security.html",
        ]
    },
    {
        "title": "Developer Guide",
        "dir": "developer-guide",
        "files": [
            "architecture-overview.html",
            "project-structure.html",
            "core-framework.html",
            "agent-data.html",
            "skills-and-mcp.html",
            "security.html",
            "custom-agents.html",
            "custom-mcp-connections.html",
        ]
    },
]

styles = getSampleStyleSheet()

styles.add(ParagraphStyle(
    'BookTitle', parent=styles['Title'],
    fontSize=36, leading=44, textColor=COLOR_PRIMARY,
    alignment=TA_CENTER, spaceAfter=20
))
styles.add(ParagraphStyle(
    'BookSubtitle', parent=styles['Normal'],
    fontSize=16, leading=22, textColor=COLOR_MUTED,
    alignment=TA_CENTER, spaceAfter=40
))
styles.add(ParagraphStyle(
    'SectionTitle', parent=styles['Heading1'],
    fontSize=28, leading=34, textColor=COLOR_PRIMARY,
    spaceBefore=0, spaceAfter=20, alignment=TA_CENTER
))
styles.add(ParagraphStyle(
    'ChapterTitle', parent=styles['Heading1'],
    fontSize=22, leading=28, textColor=COLOR_TEXT,
    spaceBefore=24, spaceAfter=12
))
styles.add(ParagraphStyle(
    'H2', parent=styles['Heading2'],
    fontSize=16, leading=22, textColor=COLOR_TEXT,
    spaceBefore=18, spaceAfter=8,
    borderWidth=0
))
styles.add(ParagraphStyle(
    'H3', parent=styles['Heading3'],
    fontSize=13, leading=18, textColor=COLOR_TEXT,
    spaceBefore=14, spaceAfter=6
))
styles.add(ParagraphStyle(
    'BodyText2', parent=styles['Normal'],
    fontSize=10, leading=15, textColor=COLOR_MUTED,
    spaceAfter=8, alignment=TA_JUSTIFY
))
styles.add(ParagraphStyle(
    'BulletItem', parent=styles['Normal'],
    fontSize=10, leading=15, textColor=COLOR_MUTED,
    leftIndent=20, bulletIndent=8, spaceAfter=4,
    bulletFontSize=10
))
styles.add(ParagraphStyle(
    'NumberedItem', parent=styles['Normal'],
    fontSize=10, leading=15, textColor=COLOR_MUTED,
    leftIndent=20, bulletIndent=8, spaceAfter=4
))
styles.add(ParagraphStyle(
    'CodeBlock', parent=styles['Code'],
    fontSize=8, leading=11, textColor=COLOR_TEXT,
    backColor=COLOR_CODE_BG,
    borderColor=COLOR_BORDER, borderWidth=0.5, borderPadding=8,
    leftIndent=0, spaceAfter=12, spaceBefore=6
))
styles.add(ParagraphStyle(
    'TOCSection', parent=styles['Normal'],
    fontSize=14, leading=20, textColor=COLOR_PRIMARY,
    spaceBefore=16, spaceAfter=4, fontName='Helvetica-Bold'
))
styles.add(ParagraphStyle(
    'TOCItem', parent=styles['Normal'],
    fontSize=10, leading=16, textColor=COLOR_MUTED,
    leftIndent=20, spaceAfter=2
))


def clean_text(text):
    if not text:
        return ""
    text = re.sub(r'\s+', ' ', text).strip()
    return text


def escape_xml(text):
    text = text.replace("&", "&amp;")
    text = text.replace("<", "&lt;")
    text = text.replace(">", "&gt;")
    text = text.replace('"', "&quot;")
    return text


def inline_to_markup(element):
    if isinstance(element, NavigableString):
        return escape_xml(str(element))
    if not isinstance(element, Tag):
        return ""

    tag = element.name
    inner = "".join(inline_to_markup(c) for c in element.children)

    if tag in ("strong", "b"):
        return f"<b>{inner}</b>"
    if tag in ("em", "i"):
        return f"<i>{inner}</i>"
    if tag == "code":
        return f'<font face="Courier" size="9" color="#1e293b">{inner}</font>'
    if tag == "a":
        return f'<font color="#3b82f6">{inner}</font>'
    if tag == "br":
        return "<br/>"
    return inner


def element_to_flowables(el, story):
    if isinstance(el, NavigableString):
        text = clean_text(str(el))
        if text:
            story.append(Paragraph(escape_xml(text), styles['BodyText2']))
        return

    if not isinstance(el, Tag):
        return

    tag = el.name

    if tag == "h1":
        text = clean_text(el.get_text())
        story.append(Paragraph(escape_xml(text), styles['ChapterTitle']))
    elif tag == "h2":
        text = clean_text(el.get_text())
        story.append(Paragraph(escape_xml(text), styles['H2']))
    elif tag == "h3":
        text = clean_text(el.get_text())
        story.append(Paragraph(escape_xml(text), styles['H3']))
    elif tag == "p":
        markup = "".join(inline_to_markup(c) for c in el.children).strip()
        if markup:
            story.append(Paragraph(markup, styles['BodyText2']))
    elif tag == "ul":
        for li in el.find_all("li", recursive=False):
            markup = "".join(inline_to_markup(c) for c in li.children).strip()
            if markup:
                story.append(Paragraph(markup, styles['BulletItem'], bulletText="•"))
        story.append(Spacer(1, 4))
    elif tag == "ol":
        for i, li in enumerate(el.find_all("li", recursive=False), 1):
            markup = "".join(inline_to_markup(c) for c in li.children).strip()
            if markup:
                story.append(Paragraph(markup, styles['NumberedItem'], bulletText=f"{i}."))
        story.append(Spacer(1, 4))
    elif tag == "pre":
        code_el = el.find("code")
        text = code_el.get_text() if code_el else el.get_text()
        text = text.rstrip()
        lines = text.split('\n')
        escaped_lines = [escape_xml(line) for line in lines]
        code_text = "<br/>".join(escaped_lines)
        try:
            story.append(Paragraph(code_text, styles['CodeBlock']))
        except Exception:
            story.append(Preformatted(text[:500], styles['Code']))
    elif tag == "div" and "diagram" in el.get("class", []):
        pre_el = el.find("pre")
        text = pre_el.get_text() if pre_el else el.get_text()
        text = text.rstrip()
        lines = text.split('\n')
        escaped_lines = [escape_xml(line) for line in lines]
        code_text = "<br/>".join(escaped_lines)
        try:
            story.append(Paragraph(code_text, styles['CodeBlock']))
        except Exception:
            story.append(Preformatted(text[:500], styles['Code']))
    elif tag == "table":
        try:
            build_table(el, story)
        except Exception:
            story.append(Paragraph(escape_xml(el.get_text()[:200]), styles['BodyText2']))
    elif tag == "hr":
        story.append(Spacer(1, 12))
    elif tag == "div" and "callout" in el.get("class", []):
        markup = "".join(inline_to_markup(c) for c in el.children).strip()
        if markup:
            story.append(Paragraph(markup, styles['BodyText2']))
    else:
        for child in el.children:
            element_to_flowables(child, story)


def build_table(table_el, story):
    headers = []
    thead = table_el.find("thead")
    if thead:
        for th in thead.find_all("th"):
            headers.append(clean_text(th.get_text()))

    rows = []
    if headers:
        rows.append(headers)

    tbody = table_el.find("tbody") or table_el
    for tr in tbody.find_all("tr", recursive=False):
        cells = tr.find_all(["td", "th"])
        row = [clean_text(c.get_text()) for c in cells]
        if row and row != headers:
            rows.append(row)

    if not rows:
        return

    col_count = max(len(r) for r in rows)
    for r in rows:
        while len(r) < col_count:
            r.append("")

    avail_width = A4[0] - 2 * inch
    col_width = avail_width / col_count

    para_rows = []
    for row in rows:
        para_row = []
        for cell in row:
            para_row.append(Paragraph(
                escape_xml(cell),
                ParagraphStyle('Cell', parent=styles['Normal'], fontSize=8, leading=11,
                               textColor=COLOR_MUTED)
            ))
        para_rows.append(para_row)

    t = Table(para_rows, colWidths=[col_width] * col_count)

    style_cmds = [
        ('BACKGROUND', (0, 0), (-1, 0), COLOR_LIGHT),
        ('TEXTCOLOR', (0, 0), (-1, 0), COLOR_TEXT),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, -1), 8),
        ('GRID', (0, 0), (-1, -1), 0.5, COLOR_BORDER),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('TOPPADDING', (0, 0), (-1, -1), 4),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
    ]

    for i in range(1, len(para_rows)):
        if i % 2 == 0:
            style_cmds.append(('BACKGROUND', (0, i), (-1, i), HexColor("#f8fafc")))

    t.setStyle(TableStyle(style_cmds))
    story.append(t)
    story.append(Spacer(1, 8))


def parse_html_content(filepath):
    with open(filepath, "r", encoding="utf-8") as f:
        soup = BeautifulSoup(f.read(), "html.parser")
    main = soup.find("main", class_="main-content")
    if not main:
        return []
    flowables = []
    for child in main.children:
        element_to_flowables(child, flowables)
    return flowables


class FooterCanvas(pdfcanvas.Canvas):
    def __init__(self, *args, **kwargs):
        pdfcanvas.Canvas.__init__(self, *args, **kwargs)
        self.pages = []

    def showPage(self):
        self.pages.append(dict(self.__dict__))
        self._startPage()

    def save(self):
        num_pages = len(self.pages)
        for i, page in enumerate(self.pages):
            self.__dict__.update(page)
            if i > 0:
                self.setFont("Helvetica", 8)
                self.setFillColor(COLOR_MUTED)
                self.drawCentredString(A4[0] / 2, 0.5 * inch, f"{i + 1}")
                self.drawString(inch, 0.5 * inch, "AI Agent Canvas")
            pdfcanvas.Canvas.showPage(self)
        pdfcanvas.Canvas.save(self)


def build_pdf():
    doc = SimpleDocTemplate(
        OUTPUT_PATH,
        pagesize=A4,
        topMargin=inch,
        bottomMargin=inch,
        leftMargin=inch,
        rightMargin=inch,
        title="AI Agent Canvas - Complete Guide",
        author="AI Agent Canvas",
        subject="Multi-Agent Enterprise Copilot Framework"
    )

    story = []

    # Title page
    story.append(Spacer(1, 2 * inch))
    story.append(Paragraph("AI Agent Canvas", styles['BookTitle']))
    story.append(Paragraph("Multi-Agent Enterprise Copilot Framework", styles['BookSubtitle']))
    story.append(Spacer(1, 0.5 * inch))
    story.append(Paragraph(
        "Build intelligent AI copilots with .NET and CopilotKit.<br/>"
        "Orchestrate specialized agents that reason, plan, and act<br/>"
        "through a shared tool registry.",
        ParagraphStyle('Intro', parent=styles['Normal'],
                       fontSize=12, leading=18, textColor=COLOR_MUTED,
                       alignment=TA_CENTER, spaceAfter=40)
    ))
    story.append(Spacer(1, 1 * inch))
    story.append(Paragraph(
        "Complete Reference Guide",
        ParagraphStyle('Edition', parent=styles['Normal'],
                       fontSize=11, textColor=COLOR_MUTED,
                       alignment=TA_CENTER)
    ))
    story.append(PageBreak())

    # Table of Contents
    story.append(Paragraph("Table of Contents", styles['ChapterTitle']))
    story.append(Spacer(1, 12))

    for section in SECTIONS:
        story.append(Paragraph(section["title"], styles['TOCSection']))
        for f in section["files"]:
            name = f.replace(".html", "").replace("-", " ").replace("index", "Overview").title()
            story.append(Paragraph(name, styles['TOCItem']))

    story.append(PageBreak())

    # Content sections
    for section in SECTIONS:
        # Section divider page
        story.append(Spacer(1, 2.5 * inch))
        story.append(Paragraph(section["title"], styles['SectionTitle']))
        story.append(Spacer(1, 0.25 * inch))

        desc = {
            "AI Agents": "Key concepts behind AI agents, the Microsoft Agent Framework, tools, MCP, context providers, and the AG-UI streaming protocol.",
            "Use Cases": "Real-world scenarios showing how AI Agent Canvas applies to financial services, healthcare, legal, e-commerce, and IT operations.",
            "User Guide": "Installation, configuration, and usage of every feature including chat, personas, skills, workflows, scheduling, and security.",
            "Developer Guide": "Architecture deep-dive, project structure, and how to extend the system with custom agents and MCP connections."
        }

        story.append(Paragraph(
            desc.get(section["title"], ""),
            ParagraphStyle('SectionDesc', parent=styles['Normal'],
                           fontSize=11, leading=16, textColor=COLOR_MUTED,
                           alignment=TA_CENTER)
        ))
        story.append(PageBreak())

        for filename in section["files"]:
            filepath = os.path.join(WEBSITE_DIR, section["dir"], filename)
            if not os.path.exists(filepath):
                print(f"  MISSING: {filepath}")
                continue

            print(f"  Processing: {section['dir']}/{filename}")
            flowables = parse_html_content(filepath)
            story.extend(flowables)
            story.append(PageBreak())

    doc.build(story, canvasmaker=FooterCanvas)
    print(f"\nPDF created: {OUTPUT_PATH}")


if __name__ == "__main__":
    build_pdf()

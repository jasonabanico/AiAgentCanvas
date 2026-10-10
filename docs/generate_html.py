"""
Generate HTML documentation pages from markdown guide files.
Converts docs/*.md -> docs/website/{section}/*.html
"""
import os
import re
import html

DOCS_DIR = os.path.dirname(os.path.abspath(__file__))
WEBSITE_DIR = os.path.join(DOCS_DIR, 'website')

FILE_MAP = {
    'guide': [
        ('guide-01-introduction.md', 'introduction.html', 'Introduction'),
        ('guide-02-ai-agents.md', 'ai-agents.html', 'AI Agents'),
        ('guide-03-features.md', 'features.html', 'Features'),
        ('guide-04-libraries.md', 'libraries.html', 'Libraries'),
        ('guide-05-building-an-agent.md', 'building-an-agent.html', 'Building an Agent'),
        ('guide-06-context-domains.md', 'context-domains.html', 'Context Domains'),
        ('guide-07-mcp-and-rag.md', 'mcp-and-rag.html', 'MCP and RAG'),
        ('guide-08-multi-agent.md', 'multi-agent.html', 'Multi-Agent Setup'),
        ('guide-09-architecture.md', 'architecture.html', 'Architecture'),
        ('guide-10-behavior-patterns.md', 'behavior-patterns.html', 'Behavior Patterns'),
        ('guide-11-operations-and-connectors.md', 'operations-and-connectors.html', 'Operations and Connectors'),
        ('guide-12-typed-output-vision-orchestration-and-mcp-server.md', 'typed-output-vision-orchestration-and-mcp-server.html', 'Typed Output, Vision, Orchestration and MCP Server'),
    ],
    'appendix': [
        ('appendix-user-guide.md', 'user-guide.html', 'User Guide'),
    ],
    'reference': [
        ('reference-agui-protocol.md', 'agui-protocol.html', 'AG-UI Protocol'),
        ('reference-security.md', 'security.html', 'Security and Governance'),
        ('reference-internals.md', 'internals.html', 'Platform Internals'),
    ],
}


def md_to_html_content(md_text):
    lines = md_text.strip().split('\n')
    out = []
    in_code = False
    in_table = False
    in_list = False
    code_lang = ''
    table_rows = []
    list_items = []

    def flush_table():
        nonlocal in_table, table_rows
        if not table_rows:
            return
        out.append('      <table>')
        out.append('        <thead><tr>')
        for cell in table_rows[0]:
            out.append(f'          <th>{inline(cell.strip())}</th>')
        out.append('        </tr></thead>')
        out.append('        <tbody>')
        for row in table_rows[2:]:  # skip separator row
            out.append('        <tr>')
            for cell in row:
                out.append(f'          <td>{inline(cell.strip())}</td>')
            out.append('        </tr>')
        out.append('        </tbody>')
        out.append('      </table>')
        in_table = False
        table_rows = []

    def flush_list():
        nonlocal in_list, list_items
        if not list_items:
            return
        out.append('      <ul>')
        for item in list_items:
            out.append(f'        <li>{inline(item)}</li>')
        out.append('      </ul>')
        in_list = False
        list_items = []

    def inline(text):
        text = html.escape(text)
        text = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', text)
        text = re.sub(r'`([^`]+)`', r'<code>\1</code>', text)
        text = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'<a href="\2">\1</a>', text)
        return text

    for line in lines:
        # Code blocks
        if line.strip().startswith('```'):
            if in_code:
                out.append('</code></pre>')
                in_code = False
            else:
                flush_table()
                flush_list()
                code_lang = line.strip()[3:]
                cls = f' class="language-{code_lang}"' if code_lang else ''
                out.append(f'      <pre><code{cls}>')
                in_code = True
            continue

        if in_code:
            out.append(html.escape(line))
            continue

        stripped = line.strip()

        # Empty line
        if not stripped:
            flush_table()
            flush_list()
            continue

        # Table rows
        if '|' in stripped and stripped.startswith('|'):
            if not in_table:
                flush_list()
                in_table = True
            cells = [c for c in stripped.split('|')[1:-1]]
            table_rows.append(cells)
            continue
        elif in_table:
            flush_table()

        # List items
        if re.match(r'^[-*] ', stripped):
            if not in_list:
                flush_table()
                in_list = True
            list_items.append(stripped[2:])
            continue
        elif re.match(r'^\d+\. ', stripped):
            if not in_list:
                flush_table()
                in_list = True
            list_items.append(re.sub(r'^\d+\. ', '', stripped))
            continue
        elif in_list:
            if stripped.startswith('  ') or stripped.startswith('\t'):
                list_items[-1] += ' ' + stripped.strip()
                continue
            else:
                flush_list()

        # Headings
        if stripped.startswith('# '):
            flush_list()
            out.append(f'      <h1>{inline(stripped[2:])}</h1>')
        elif stripped.startswith('## '):
            flush_list()
            text = re.sub(r'\s*\{#[^}]+\}', '', stripped[3:])
            out.append(f'      <h2>{inline(text)}</h2>')
        elif stripped.startswith('### '):
            flush_list()
            out.append(f'      <h3>{inline(stripped[4:])}</h3>')
        elif stripped.startswith('#### '):
            flush_list()
            out.append(f'      <h4>{inline(stripped[5:])}</h4>')
        elif stripped.startswith('> '):
            flush_list()
            out.append(f'      <blockquote><p>{inline(stripped[2:])}</p></blockquote>')
        elif stripped.startswith('---'):
            flush_list()
            out.append('      <hr>')
        else:
            out.append(f'      <p>{inline(stripped)}</p>')

    flush_table()
    flush_list()
    if in_code:
        out.append('</code></pre>')

    return '\n'.join(out)


def generate_page(md_file, html_file, title, section_dir):
    md_path = os.path.join(DOCS_DIR, md_file)
    if not os.path.exists(md_path):
        print(f'  SKIP {md_file} (not found)')
        return False

    with open(md_path, 'r', encoding='utf-8') as f:
        md_content = f.read()

    html_content = md_to_html_content(md_content)

    page = f'''<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{title} - AI Agent Canvas</title>
  <link rel="stylesheet" href="../styles.css">
  <script src="../layout.js"></script>
</head>
<body>
  <div class="page-layout">
    <main class="main-content">
{html_content}
    </main>
  </div>
</body>
</html>
'''

    out_dir = os.path.join(WEBSITE_DIR, section_dir)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, html_file)
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(page)

    print(f'  OK {section_dir}/{html_file}')
    return True


def main():
    count = 0
    for section, files in FILE_MAP.items():
        print(f'\n{section}/')
        for md_file, html_file, title in files:
            if generate_page(md_file, html_file, title, section):
                count += 1

    print(f'\nGenerated {count} HTML pages.')


if __name__ == '__main__':
    main()

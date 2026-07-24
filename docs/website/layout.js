const NAV_SECTIONS = [
  {
    title: 'Guide',
    basePath: 'guide',
    items: [
      { title: '1. Introduction', file: 'introduction.html' },
      { title: '2. AI Agents', file: 'ai-agents.html' },
      { title: '3. Features', file: 'features.html' },
      { title: '4. Libraries', file: 'libraries.html' },
      { title: '5. Building an Agent', file: 'building-an-agent.html' },
      { title: '6. Context Domains', file: 'context-domains.html' },
      { title: '7. MCP and RAG', file: 'mcp-and-rag.html' },
      { title: '8. Multi-Agent Setup', file: 'multi-agent.html' },
      { title: '9. Architecture', file: 'architecture.html' },
      { title: '10. Behavior Patterns', file: 'behavior-patterns.html' },
    ]
  },
  {
    title: 'Appendix',
    basePath: 'appendix',
    items: [
      { title: 'User Guide', file: 'user-guide.html' },
    ]
  },
  {
    title: 'Reference',
    basePath: 'reference',
    items: [
      { title: 'AG-UI Protocol', file: 'agui-protocol.html' },
      { title: 'Security & Governance', file: 'security.html' },
      { title: 'Platform Internals', file: 'internals.html' },
    ]
  }
];

const TOP_NAV = [
  { title: 'Guide', href: 'guide/introduction.html' },
  { title: 'Appendix', href: 'appendix/user-guide.html' },
  { title: 'Reference', href: 'reference/agui-protocol.html' },
];

function getBasePath() {
  const path = window.location.pathname;
  const knownSections = NAV_SECTIONS.map(s => s.basePath);
  const parts = path.split('/').filter(Boolean);
  for (let i = 0; i < parts.length; i++) {
    if (knownSections.includes(parts[i])) {
      const depth = parts.length - i - 1;
      return depth > 0 ? '../'.repeat(depth) : '';
    }
  }
  return '';
}

function getCurrentSection() {
  const path = window.location.pathname;
  for (const section of NAV_SECTIONS) {
    if (path.includes('/' + section.basePath + '/')) return section.basePath;
  }
  return null;
}

function getCurrentFile() {
  const path = window.location.pathname;
  const parts = path.split('/');
  return parts[parts.length - 1];
}

function buildHeader() {
  const base = getBasePath();
  const currentSection = getCurrentSection();
  const header = document.createElement('header');
  header.className = 'site-header';
  header.innerHTML = `
    <a href="${base}../index.html" class="logo"><span>AI Agent Canvas</span></a>
    <button class="menu-toggle" onclick="toggleMobileNav()" aria-label="Toggle menu">&#9776;</button>
    <nav>
      ${TOP_NAV.map(n => `<a href="${base}${n.href}" class="${currentSection && n.href.includes(currentSection) ? 'active' : ''}">${n.title}</a>`).join('')}
    </nav>
  `;
  document.body.prepend(header);
}

function buildSidebar() {
  const currentSection = getCurrentSection();
  if (!currentSection) return;
  const base = getBasePath();
  const currentFile = getCurrentFile();

  const sidebar = document.createElement('aside');
  sidebar.className = 'sidebar';

  let html = '';
  for (const section of NAV_SECTIONS) {
    html += `<div class="sidebar-section"><h3>${section.title}</h3>`;
    for (const item of section.items) {
      const href = `${base}${section.basePath}/${item.file}`;
      const isActive = section.basePath === currentSection && item.file === currentFile;
      html += `<a href="${href}" class="${isActive ? 'active' : ''}">${item.title}</a>`;
    }
    html += '</div>';
  }
  sidebar.innerHTML = html;

  const layout = document.querySelector('.page-layout');
  if (layout) layout.prepend(sidebar);
}

function toggleMobileNav() {
  const nav = document.querySelector('.site-header nav');
  if (nav) nav.classList.toggle('open');
  const sidebar = document.querySelector('.sidebar');
  if (sidebar) sidebar.classList.toggle('open');
}

document.addEventListener('DOMContentLoaded', () => {
  buildHeader();
  buildSidebar();
});

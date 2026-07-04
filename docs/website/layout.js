const NAV_SECTIONS = [
  {
    title: 'AI Agents',
    basePath: 'ai-agents',
    items: [
      { title: 'What Are AI Agents', file: 'what-are-ai-agents.html' },
      { title: 'Microsoft Agent Framework', file: 'microsoft-agent-framework.html' },
      { title: 'Tools and Skills', file: 'tools-and-skills.html' },
      { title: 'Model Context Protocol', file: 'model-context-protocol.html' },
      { title: 'Context Providers', file: 'context-providers.html' },
      { title: 'AG-UI Protocol', file: 'ag-ui-protocol.html' },
    ]
  },
  {
    title: 'Use Cases',
    basePath: 'use-cases',
    items: [
      { title: 'Overview', file: 'index.html' },
      { title: 'Financial Services', file: 'financial-services.html' },
      { title: 'Healthcare', file: 'healthcare.html' },
      { title: 'Legal', file: 'legal.html' },
      { title: 'E-Commerce', file: 'e-commerce.html' },
      { title: 'IT Operations', file: 'it-operations.html' },
    ]
  },
  {
    title: 'User Guide',
    basePath: 'user-guide',
    items: [
      { title: 'Getting Started', file: 'getting-started.html' },
      { title: 'Configuration', file: 'configuration.html' },
      { title: 'Chat Interface', file: 'chat-interface.html' },
      { title: 'Personas', file: 'personas.html' },
      { title: 'Skills & Tools', file: 'skills-and-tools.html' },
      { title: 'Scheduling', file: 'scheduling.html' },
      { title: 'Workflows', file: 'workflows.html' },
      { title: 'Security', file: 'security.html' },
    ]
  },
  {
    title: 'Developer Guide',
    basePath: 'developer-guide',
    items: [
      { title: 'Architecture Overview', file: 'architecture-overview.html' },
      { title: 'Project Structure', file: 'project-structure.html' },
      { title: 'Core Platform', file: 'core-framework.html' },
      { title: 'Agent Data', file: 'agent-data.html' },
      { title: 'Skills & MCP', file: 'skills-and-mcp.html' },
      { title: 'RAG Pipeline', file: 'rag-pipeline.html' },
      { title: 'Security', file: 'security.html' },
      { title: 'Custom Agents', file: 'custom-agents.html' },
      { title: 'Custom MCP Connections', file: 'custom-mcp-connections.html' },
      { title: 'Behavior Patterns', file: 'behavior-patterns.html' },
    ]
  }
];

const TOP_NAV = [
  { title: 'AI Agents', href: 'ai-agents/what-are-ai-agents.html' },
  { title: 'Use Cases', href: 'use-cases/index.html' },
  { title: 'User Guide', href: 'user-guide/getting-started.html' },
  { title: 'Developer Guide', href: 'developer-guide/architecture-overview.html' },
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

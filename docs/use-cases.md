# Use Cases

AI Agent Canvas is a multi-agent enterprise copilot platform that adapts to any industry. The following scenarios demonstrate how organizations use tool-calling agents, MCP data connections, scheduled tasks, skill authoring, personas, guardrails, entity management, user profiles, and security governance to solve real operational challenges.

Each use case presents a hypothetical company, a specific persona, the problem they face, and how AI Agent Canvas delivers measurable value.

## Table of Contents

- [Financial Services: Market Intelligence Copilot](#financial-services-market-intelligence-copilot)
- [Healthcare: Clinical Research Assistant](#healthcare-clinical-research-assistant)
- [Legal: Contract Review Agent](#legal-contract-review-agent)
- [E-Commerce: Customer Operations Copilot](#e-commerce-customer-operations-copilot)
- [IT Operations: Incident Response Agent](#it-operations-incident-response-agent)

---

## Financial Services: Market Intelligence Copilot

**Meridian Capital** --- A mid-size hedge fund uses AI Agent Canvas to give junior analysts instant access to market data, SEC filings, and technical indicators while enforcing compliance guardrails that prevent unauthorized trading signals.

### Company Profile

**Meridian Capital** is a mid-size hedge fund managing $2.4 billion in assets across equity, fixed-income, and alternatives strategies. Based in Boston, the firm employs 45 investment professionals and prides itself on data-driven decision-making. Their edge depends on how fast analysts can synthesize market signals and translate them into actionable research.

### The Persona

**Priya Sharma, Junior Portfolio Analyst.** Two years out of her MBA, Priya covers the industrials sector. Her day starts at 6:30 AM reviewing overnight market moves and ends with a stack of research notes that feed the senior portfolio managers' weekly allocation meeting. She juggles Bloomberg terminals, SEC filings, internal models, and Slack threads --- often copying data between systems manually.

### The Problem

Priya spends 60% of her day on data gathering rather than analysis. Pulling a single stock's fundamentals, technicals, and recent SEC filings requires switching between four applications. When the PM asks "What changed overnight in our watchlist?", Priya needs 45 minutes to compile an answer. During earnings season, the backlog becomes unmanageable.

The compliance team adds another layer: analysts must not share raw trading signals outside approved channels, and every piece of external data used in a recommendation must be traceable to its source.

### How AI Agent Canvas Solves It

Meridian deploys AI Agent Canvas as an internal copilot accessible through a CopilotKit-powered web interface embedded in their research portal.

#### MCP Data Connections

The team configures **MCP.HelloWorldData** with three core tools:

- **stock_quote** --- Retrieves real-time and historical price data for any ticker. Priya asks "What's the current price and 30-day performance of CAT?" and gets a structured response with price, change, volume, and moving averages.
- **edgar_company_facts** --- Queries SEC EDGAR for company filings, financial statements, and key metrics. "Pull CAT's latest 10-K revenue breakdown by segment" returns structured data extracted from the filing.
- **stock_history** --- Retrieves historical stock data from Yahoo Finance with a configurable range. "Show me the price history for CAT over the last 90 days" returns historical data points for trend analysis.

#### Scheduled Tasks

Meridian configures two scheduled scans:

- **Morning Watchlist Briefing (6:00 AM ET)** --- The agent iterates through the firm's 85-stock watchlist, pulls overnight price changes, flags any stock that moved more than 2%, and checks for new SEC filings posted after market close. The summary lands in Priya's inbox before she arrives.
- **Earnings Calendar Alert (Weekly, Monday 7:00 AM)** --- Scans the upcoming week's earnings calendar against the watchlist and generates a preparation checklist for each reporting company.

#### Guardrails

Compliance requirements are enforced through two custom guardrails:

- **No Trading Signals** --- The agent is prohibited from generating buy/sell/hold recommendations or price targets. It can present data and flag anomalies, but the investment thesis must come from the analyst. If Priya asks "Should I buy CAT?", the agent responds with relevant data points and declines to make a recommendation.
- **Source Attribution** --- Every data point in the agent's response includes a source tag (e.g., "SEC EDGAR 10-K filed 2026-02-14" or "Market data as of 2026-06-21 09:31 ET"). This creates an audit trail that satisfies the compliance team's traceability requirements.

#### Personas

The agent supports two personas:

- **Research Mode** (default) --- Verbose responses with full data tables, charts descriptions, and contextual notes. Designed for deep analysis sessions.
- **Briefing Mode** --- Concise bullet-point summaries optimized for the morning standup. Priya switches to this mode when preparing materials for the PM meeting.

### A Day in the Life

**6:15 AM** --- Priya opens her laptop to find the morning watchlist briefing already in her inbox. Three stocks moved more than 2% overnight: Caterpillar (CAT) dropped 3.1% on a supplier disruption report, Deere (DE) rose 2.4% on strong Asia-Pacific orders, and United Rentals (URI) filed an 8-K disclosing a new credit facility.

**6:30 AM** --- She opens the copilot and asks: "Deep dive on CAT's supplier disruption. Pull the latest 10-K supply chain risk disclosures and compare current RSI to the last three drawdowns." The agent retrieves the relevant 10-K section, computes the RSI comparison, and presents a structured analysis --- without suggesting whether the dip is a buying opportunity.

**8:00 AM** --- During the morning standup, the PM asks about URI's new credit facility. Priya switches the agent to Briefing Mode and asks: "Summarize URI's 8-K filed yesterday. What are the key terms?" She gets a five-bullet summary she can read aloud.

**10:30 AM** --- Priya is building a sector comparison. She asks: "Compare P/E, EV/EBITDA, and revenue growth for CAT, DE, URI, and PCAR using the most recent quarterly data." The agent pulls EDGAR facts for all four companies and presents a comparison table with source citations.

**2:00 PM** --- She drafts her weekly sector note and asks the agent to verify three specific data points against SEC filings. The agent confirms two and flags that one revenue figure she cited is from the prior quarter, not the most recent.

### Key Configuration

| Component | Configuration |
|---|---|
| **Agents** | MarketIntelligenceAgent with `CanHandle` matching financial queries |
| **MCP Connections** | MCP.HelloWorldData (stock_quote, stock_history, edgar_company_facts) |
| **Scheduled Tasks** | Morning Watchlist Briefing (daily 6:00 AM), Earnings Calendar Alert (weekly Monday 7:00 AM) |
| **Personas** | Research Mode (verbose analysis), Briefing Mode (concise bullets) |
| **Guardrails** | NoTradingSignals, SourceAttribution |
| **User Profiles** | Per-analyst watchlist configuration, sector assignment |
| **Entity Management** | Stock tickers, SEC filing references, internal model identifiers |

---

## Healthcare: Clinical Research Assistant

**BioNova Therapeutics** --- A biotech startup streamlines clinical trial coordination by connecting their trial database through MCP, managing drug compound entities, and switching between regulatory and research personas on the fly.

### Company Profile

**BioNova Therapeutics** is a 90-person biotech startup in Cambridge, Massachusetts, developing next-generation immunotherapy treatments for autoimmune diseases. The company has three compounds in clinical trials --- two in Phase II and one entering Phase III. With a lean team and aggressive timelines, every hour of manual data work delays their path to regulatory submission.

### The Persona

**Dr. Marcus Chen, Clinical Research Coordinator.** Marcus manages the day-to-day operations of BioNova's Phase II trial for BNV-204, a monoclonal antibody targeting lupus. He coordinates across 12 clinical sites, tracks adverse events, manages regulatory submissions, and ensures protocol compliance. His background is in clinical pharmacology, but he spends most of his time wrangling spreadsheets and chasing site coordinators for overdue data.

### The Problem

BioNova's clinical trial data lives in a purpose-built trial management system, but extracting insights requires SQL queries that Marcus cannot write himself. He submits data requests to the biostatistics team, waits 2-3 days for results, and often discovers the query didn't capture exactly what he needed. Meanwhile, regulatory deadlines are fixed.

Compound information is scattered across internal wikis, lab notebooks, and regulatory submission documents. When the FDA requests a specific detail about BNV-204's pharmacokinetic profile, Marcus spends hours locating the authoritative source.

The team also needs to operate in two distinct modes: exploratory research conversations (where speculation is acceptable) and regulatory-grade reporting (where every statement must be traceable and precise).

### How AI Agent Canvas Solves It

BioNova deploys AI Agent Canvas as an internal research assistant accessible to the clinical operations team.

#### MCP Data Connections

A custom **MCP.ClinicalTrials** connector exposes the trial management database through three tools:

- **query_enrollment** --- Returns enrollment status by site, cohort, and time period. Marcus asks "How many patients completed the Week 12 visit at the Boston and Philadelphia sites?" and gets a precise count with enrollment dates.
- **query_adverse_events** --- Retrieves adverse event reports filtered by severity, system organ class, and relatedness assessment. "List all Grade 3+ adverse events in Cohort B reported in the last 30 days" returns structured records with patient identifiers redacted to study IDs.
- **query_endpoints** --- Pulls primary and secondary endpoint data for interim analysis. "What is the mean change in SLEDAI score from baseline across all active patients?" returns the computed statistic with confidence intervals.

#### Entity Management

BioNova uses entity management to maintain a canonical registry of their drug compounds:

- **BNV-204** --- Monoclonal antibody, anti-BAFF, Phase II lupus trial (NCT-XXXXX)
- **BNV-118** --- Small molecule, JAK1 selective inhibitor, Phase II rheumatoid arthritis
- **BNV-330** --- Bispecific antibody, Phase III psoriatic arthritis

Each entity includes structured metadata: mechanism of action, molecular target, current trial phase, IND number, and links to key regulatory documents. When Marcus asks about "the lupus compound," the agent resolves it to BNV-204 and pulls the correct context.

#### Personas

The agent operates in two personas, switchable by Marcus or triggered by context:

- **Research Mode** --- Allows exploratory analysis. The agent can suggest hypotheses, note trends in the data, and speculate about potential correlations. Responses include caveats marking speculative content. Used during internal team discussions and brainstorming sessions.
- **Regulatory Mode** --- Strict, citation-heavy responses. Every data point must reference its source (database query, document section, or filing). The agent declines to speculate and flags when a question cannot be answered from available data alone. Used when preparing FDA submissions, responding to information requests, or drafting study reports.

#### Guardrails

- **Patient Privacy** --- The agent never surfaces individual patient identifiers. All patient references use study IDs. If Marcus asks a question that would require revealing protected health information, the agent explains the restriction and suggests an aggregate alternative.
- **No Diagnostic Claims** --- The agent cannot state that a compound is effective, safe, or superior. It presents data and statistical results, but therapeutic conclusions must come from the clinical team and the Data Safety Monitoring Board.

### A Day in the Life

**7:30 AM** --- Marcus reviews the weekly enrollment dashboard the agent compiled overnight. Two sites are behind their enrollment targets. The agent has flagged specific screen failure reasons at each site, pulling data from the trial database.

**9:00 AM** --- The medical monitor asks Marcus for a summary of all serious adverse events (SAEs) reported across sites in the last quarter. Marcus opens the copilot in Regulatory Mode and asks: "List all SAEs for BNV-204 in Q1 2026, grouped by system organ class, with relatedness assessment and outcome." The agent queries the database and presents a structured table with source references to each adverse event report.

**11:00 AM** --- During a team meeting, a scientist asks whether there's a correlation between baseline disease severity and response at Week 12. Marcus switches to Research Mode and asks: "Is there a trend between baseline SLEDAI score and Week 12 SLEDAI change in Cohort A?" The agent runs the query, presents a summary, and notes: "This is an exploratory observation and has not been adjusted for confounders. Formal analysis should be performed by biostatistics."

**2:00 PM** --- The FDA sends an information request about BNV-204's pharmacokinetic profile in hepatic impairment. Marcus asks the agent: "What PK data do we have for BNV-204 in patients with mild hepatic impairment?" The agent searches the entity registry for BNV-204, locates the relevant Phase I PK study report, and extracts the relevant section --- all in Regulatory Mode with full document citations.

**4:00 PM** --- Marcus needs to update the site monitoring visit schedule. He asks: "Which sites have overdue monitoring visits based on the protocol-specified frequency?" The agent cross-references the visit log against the protocol schedule and identifies three sites that need attention.

### Key Configuration

| Component | Configuration |
|---|---|
| **Agents** | ClinicalResearchAgent with `CanHandle` matching trial, compound, and regulatory queries |
| **MCP Connections** | MCP.ClinicalTrials (query_enrollment, query_adverse_events, query_endpoints) |
| **Personas** | Research Mode (exploratory, allows speculation), Regulatory Mode (strict, citation-required) |
| **Entity Management** | Drug compound registry (BNV-204, BNV-118, BNV-330) with structured metadata |
| **Guardrails** | PatientPrivacy (no individual identifiers), NoDiagnosticClaims (no efficacy/safety conclusions) |
| **User Profiles** | Role-based access: clinical ops (full query access), research scientists (endpoint data only), regulatory affairs (all data + submission tools) |
| **Scheduled Tasks** | Weekly enrollment dashboard (Monday 6:00 AM), overdue visit alerts (daily 8:00 AM) |

---

## Legal: Contract Review Agent

**Sterling & Associates** --- A law firm accelerates contract review with custom skills for clause extraction, practice-area-specific user profiles, and confidentiality guardrails that prevent sensitive terms from leaking across client matters.

### Company Profile

**Sterling & Associates** is a 120-attorney law firm headquartered in Chicago with offices in New York and San Francisco. The firm specializes in corporate transactions, intellectual property, and commercial litigation. Their clients range from mid-market companies to Fortune 500 enterprises, and the volume of contracts flowing through the firm at any given time exceeds 400 active matters.

### The Persona

**Elena Vasquez, Associate Attorney.** Five years into her practice, Elena works in the corporate transactions group. She reviews 15-20 contracts per week --- supplier agreements, licensing deals, NDAs, and merger-related ancillary documents. She is sharp, efficient, and frustrated. The firm's document management system holds thousands of precedent agreements, but finding the right clause from a prior deal requires searching through dozens of files manually.

### The Problem

Contract review at Sterling follows a predictable but time-consuming pattern. Elena receives a draft agreement from opposing counsel, reads it cover to cover, identifies problematic clauses, cross-references the firm's standard positions, checks for client-specific requirements, and redlines the document. A routine supplier agreement takes 3-4 hours. A complex licensing deal can take two full days.

Three specific bottlenecks slow her down. First, locating precedent language: "How did we handle the limitation of liability in the Acme Industries deal last year?" requires emailing colleagues or manually searching the DMS. Second, client-specific requirements are tracked in internal memos that are not linked to the DMS, so Elena must remember or re-discover each client's preferences. Third, confidentiality walls between practice groups mean she must be careful not to access or reference documents from matters she is not staffed on.

### How AI Agent Canvas Solves It

Sterling deploys AI Agent Canvas as a contract review assistant integrated into their attorney workspace.

#### MCP Data Connections

A custom **MCP.DocumentManagement** connector interfaces with the firm's iManage DMS:

- **search_precedents** --- Searches the DMS for contracts matching specified criteria: clause type, client, industry, deal type, and date range. Elena asks "Find indemnification clauses from technology licensing agreements closed in the last 18 months" and receives a ranked list of matching clauses with document references.
- **retrieve_clause** --- Extracts a specific clause from a document by section heading or clause type. "Pull the limitation of liability from the Acme Industries MSA (matter 2025-1847)" returns the exact language with its document citation.
- **check_client_standards** --- Queries the client standards database to retrieve the firm's approved positions for a specific client. "What is our standard position on consequential damages for TechCorp?" returns the approved language and any client-specific notes.

#### Skill Authoring

The firm creates custom skills that encode their contract review methodology:

- **Clause Extraction** --- Given a contract document, identifies and categorizes every material clause: indemnification, limitation of liability, termination, assignment, change of control, IP ownership, confidentiality, and governing law. Each extracted clause is tagged with a risk assessment (standard, requires review, non-standard).
- **Redline Comparison** --- Compares a received draft against the firm's standard template for that contract type and highlights deviations. The output is a structured list of differences with the firm's preferred alternative language.
- **Position Reconciliation** --- Cross-references extracted clauses against client-specific standards and flags any clause where the draft deviates from the client's approved position.

#### Guardrails

- **Matter-Based Access Control** --- The agent enforces confidentiality walls by restricting DMS searches to matters where the requesting attorney is listed as a team member. If Elena searches for precedents, she only sees results from her own matters and from the firm's general precedent library. Documents from walled-off matters are excluded from results.
- **No Legal Advice Framing** --- The agent presents its analysis as draft work product for attorney review. Every output includes a disclaimer that the analysis has not been reviewed by a supervising attorney and does not constitute the firm's legal opinion.
- **Privilege Protection** --- The agent will not include attorney-client privileged content in any output that could be shared externally. If Elena asks the agent to draft a clause summary for opposing counsel, it strips internal notes and work product references.

#### User Profiles

User profiles map each attorney to their practice area, seniority level, and matter assignments:

- **Practice Area** determines which standard templates and clause libraries are loaded by default. Elena's corporate transactions profile loads commercial contract templates. An IP attorney's profile loads licensing and patent agreement templates.
- **Seniority Level** controls the depth of analysis. Associates receive detailed explanations and citations to precedent. Partners receive concise summaries with only flagged deviations.
- **Matter Assignments** define the DMS access perimeter for confidentiality wall enforcement.

### A Day in the Life

**8:00 AM** --- Elena receives a draft Master Services Agreement from opposing counsel for a new TechCorp engagement. She uploads the document to the copilot and asks: "Extract all material clauses and assess against our standard MSA template."

**8:15 AM** --- The agent returns a structured analysis. Fourteen clauses extracted. Three flagged as non-standard: the indemnification cap is set at contract value (the firm's standard is 2x), the termination for convenience requires 90-day notice (standard is 30), and the IP ownership clause contains a broad license-back provision not present in the template.

**8:30 AM** --- Elena asks: "Check these three flagged clauses against TechCorp's client standards." The agent queries the client standards database and reports: TechCorp has previously accepted a 1.5x indemnification cap, requires no more than 60-day termination notice, and has no documented position on license-back provisions. Elena now knows exactly where she has room to negotiate and where she needs partner guidance.

**10:00 AM** --- For the license-back provision, Elena needs precedent. She asks: "Find license-back clauses from technology MSAs where we successfully negotiated narrower scope, last two years." The agent returns four examples from her accessible matters, each with the original draft language and the final negotiated version.

**11:30 AM** --- Elena drafts her redline and asks the agent to verify consistency: "Check my redlined indemnification clause against the TechCorp standard and flag any conflicts." The agent confirms alignment and notes that her proposed cap of 1.5x matches the client's documented threshold.

**3:00 PM** --- A partner asks Elena to prepare a summary of open issues for the client call tomorrow. Elena asks the agent: "Summarize the three non-standard clauses, our proposed positions, and the precedent supporting each." The agent produces a one-page brief with citations to the precedent agreements --- all from matters Elena is authorized to access.

### Key Configuration

| Component | Configuration |
|---|---|
| **Agents** | ContractReviewAgent with `CanHandle` matching contract, clause, and agreement queries |
| **MCP Connections** | MCP.DocumentManagement (search_precedents, retrieve_clause, check_client_standards) |
| **Skills** | ClauseExtraction, RedlineComparison, PositionReconciliation |
| **Guardrails** | MatterBasedAccessControl, NoLegalAdviceFraming, PrivilegeProtection |
| **User Profiles** | Per-attorney: practice area, seniority level, matter assignments |
| **Personas** | Associate Mode (detailed analysis with citations), Partner Mode (concise flagged deviations) |
| **Entity Management** | Client registry, matter database, standard clause library |

---

## E-Commerce: Customer Operations Copilot

**Bloom & Vine** --- A direct-to-consumer home goods brand automates order monitoring, returns processing, and escalation notifications by connecting Shopify and Zendesk through MCP and orchestrating multi-step workflows.

### Company Profile

**Bloom & Vine** is a direct-to-consumer home goods brand based in Portland, Oregon. Founded four years ago, the company sells handcrafted ceramics, textiles, and sustainable home decor through their Shopify storefront. Annual revenue hit $18 million last year, driven by a loyal customer base and strong social media presence. The operations team is small --- eight people handling everything from order fulfillment to customer support.

### The Persona

**Aisha Thompson, Customer Operations Manager.** Aisha oversees the end-to-end customer experience: order processing, shipping logistics, returns, and support ticket resolution. She manages a team of three support agents and personally handles escalated issues. Her tools include Shopify for orders, Zendesk for support tickets, ShipStation for fulfillment tracking, and a spreadsheet she maintains for tracking recurring issues. She is the connective tissue between the warehouse, the support team, and the founders.

### The Problem

Bloom & Vine's growth has outpaced their operational tooling. Aisha's team handles 200+ support tickets per week across email, chat, and social channels. The most common issues --- order status inquiries, return requests, and shipping delays --- are straightforward but time-consuming because each requires switching between Shopify, Zendesk, and ShipStation to gather context.

Returns processing is particularly painful. The company's return policy varies by product category (ceramics have a different breakage policy than textiles), and the support agents frequently need to check the policy, verify the order, assess the reason, and determine whether to issue a refund, replacement, or store credit. Each return takes 12-15 minutes to process manually.

Peak seasons are worse. During the holiday rush, the ticket backlog grows to 500+ and response times stretch beyond 48 hours. Aisha spends her evenings triaging tickets instead of improving processes.

### How AI Agent Canvas Solves It

Bloom & Vine deploys AI Agent Canvas as an internal operations copilot that Aisha and her support agents use alongside their existing tools.

#### MCP Data Connections

Two custom MCP connectors bridge the key operational systems:

**MCP.Shopify** exposes order and product data:

- **lookup_order** --- Retrieves complete order details by order number, email, or customer name. Includes line items, payment status, fulfillment status, shipping tracking, and order history. "Pull up order #BV-28491" returns everything Aisha needs in one response.
- **lookup_customer** --- Returns customer profile with order history, lifetime value, return history, and any internal notes. "Show me the customer profile for sarah.chen@email.com" reveals that Sarah is a repeat buyer with $2,400 in lifetime purchases and zero prior returns.
- **process_return** --- Initiates a return in Shopify: creates the return record, generates a shipping label, and triggers the appropriate refund type (original payment, store credit, or replacement). Requires explicit confirmation before executing.

**MCP.Zendesk** connects the support ticket system:

- **get_ticket** --- Retrieves a Zendesk ticket with full conversation history, tags, and internal notes. "What's the status of ticket #41872?" returns the complete thread.
- **update_ticket** --- Adds internal notes, changes priority, assigns to a team member, or updates tags. Used by the agent to enrich tickets with order context automatically.
- **search_tickets** --- Finds tickets matching criteria: status, assignee, creation date, tags, or keyword. "Show me all open tickets tagged 'shipping-delay' from the last 7 days" returns a filtered list.

#### Scheduled Tasks

Automated monitoring runs continuously in the background:

- **Order Anomaly Scan (Every 2 hours)** --- Checks all orders placed in the last 48 hours for anomalies: payment failures, fulfillment delays beyond SLA, duplicate orders, and address validation warnings. Flagged orders appear in a daily digest or trigger immediate notifications for high-severity issues.
- **Shipping Delay Monitor (Every 4 hours)** --- Cross-references ShipStation tracking data against expected delivery dates. Orders that are delayed beyond the carrier's estimate by more than 2 days are flagged, and the agent pre-drafts a proactive customer communication.
- **Weekly Ticket Trends (Monday 7:00 AM)** --- Analyzes the prior week's ticket volume, resolution time, and category distribution. Identifies emerging patterns --- for example, a spike in "item arrived damaged" tickets from a specific carrier.

#### Workflows

Multi-step workflows handle common operations end to end:

**Returns Processing Workflow:**

1. Support agent receives a return request and asks the copilot: "Process a return for order #BV-28491, reason: item damaged in shipping."
2. The agent looks up the order, identifies the product category (ceramics), retrieves the applicable return policy (full refund or replacement for damaged ceramics within 30 days), and verifies the order date is within the return window.
3. The agent presents the recommended action: "Order is eligible for full refund or free replacement under the ceramics breakage policy. Customer has zero prior returns. Recommended: offer both options to the customer."
4. Upon confirmation, the agent creates the return in Shopify, generates the prepaid return label, and drafts a response for the support agent to send through Zendesk.

#### Notifications and Escalations

The agent monitors for conditions that require immediate human attention:

- **VIP Customer Issues** --- Tickets from customers with lifetime value above $1,000 are automatically escalated to Aisha with full order context attached.
- **Repeat Complaints** --- If the same customer opens a third ticket within 30 days, the agent flags the pattern and recommends a personal outreach from Aisha.
- **Inventory-Impacting Returns** --- If returns for a specific product exceed 5% of units sold in a rolling 30-day window, the agent alerts the product team with a summary of return reasons.

### A Day in the Life

**7:30 AM** --- Aisha opens the copilot to find the overnight anomaly scan results. Two orders flagged: one payment retry failed after three attempts, and one order shipped to an undeliverable address (carrier returned the package). The agent has already pre-drafted a customer email for the address issue.

**8:15 AM** --- A support agent asks the copilot: "Customer says her ceramic vase arrived broken. Order #BV-31205." The agent pulls the order (placed 8 days ago, ceramics category, first-time buyer), confirms it falls within the 30-day breakage policy, and recommends a full replacement with expedited shipping to recover the customer experience. The agent drafts the Zendesk response and waits for the support agent to approve before sending.

**10:00 AM** --- The shipping delay monitor flags 12 orders stuck at a regional carrier hub in Dallas for 3+ days. The agent groups them by carrier route, identifies the likely cause (weather-related delays affecting the Southwest corridor), and drafts a batch notification for affected customers offering a 15% discount on their next order.

**1:00 PM** --- Aisha asks: "Show me the ticket trends from last week." The agent presents the weekly analysis: ticket volume was up 22% from the prior week, driven by a 40% increase in "where is my order" inquiries. The root cause correlates with the Dallas carrier delays. Average resolution time held steady at 4.2 hours because the agent automated initial order lookups for the support team.

**3:30 PM** --- A VIP escalation arrives. A customer with $3,200 in lifetime purchases received the wrong item. The agent surfaces the full context: order details, what was ordered vs. what shipped, warehouse batch information, and the customer's previous positive interactions. Aisha handles the call personally, armed with everything she needs.

### Key Configuration

| Component | Configuration |
|---|---|
| **Agents** | CustomerOpsAgent with `CanHandle` matching order, return, shipping, and ticket queries |
| **MCP Connections** | MCP.Shopify (lookup_order, lookup_customer, process_return), MCP.Zendesk (get_ticket, update_ticket, search_tickets) |
| **Scheduled Tasks** | Order Anomaly Scan (every 2 hours), Shipping Delay Monitor (every 4 hours), Weekly Ticket Trends (Monday 7:00 AM) |
| **Workflows** | ReturnsProcessing (policy check, return creation, label generation, response drafting) |
| **Guardrails** | RequireConfirmation (all refunds and replacements require human approval), SpendLimit (refunds above $200 escalate to Aisha) |
| **User Profiles** | Support agents (ticket and order access), Aisha (full access + escalation management), warehouse team (fulfillment data only) |
| **Entity Management** | Product catalog with category-specific return policies, carrier routing rules |

---

## IT Operations: Incident Response Agent

**CloudScale Systems** --- A SaaS platform empowers site reliability engineers with an agent that correlates alerts from Datadog and PagerDuty, runs scheduled health checks, and enforces guardrails that prevent destructive remediation actions.

### Company Profile

**CloudScale Systems** is a B2B SaaS platform providing real-time inventory management to mid-market retailers. The platform processes 2.3 million API requests per hour across three AWS regions, serving 340 enterprise customers who depend on sub-second response times for their point-of-sale and e-commerce integrations. The engineering team is 55 people, with a 6-person SRE team responsible for platform reliability.

### The Persona

**Jordan Park, Site Reliability Engineer.** Jordan has been an SRE at CloudScale for three years. They are on the on-call rotation every third week and responsible for maintaining the platform's 99.95% uptime SLA. Their toolkit includes Datadog for observability, PagerDuty for alerting, Terraform for infrastructure, and a growing collection of runbooks in Confluence that are perpetually half-updated. Jordan is technically strong but drowning in alert noise --- on a typical on-call shift, they receive 60-80 alerts, of which fewer than 10 require human action.

### The Problem

CloudScale's monitoring stack generates high-quality telemetry, but correlating signals across systems during an incident is entirely manual. When a PagerDuty alert fires at 2 AM, Jordan's workflow looks like this: acknowledge the alert, open Datadog, check the relevant dashboard, correlate with recent deployments, check dependent services, look at the database metrics, review the last similar incident's postmortem, and decide whether to page the application team or handle it directly.

This triage process takes 10-20 minutes per incident --- time that matters when the SLA clock is ticking. Worse, during cascading failures, multiple alerts fire simultaneously, and Jordan must mentally model dependencies to determine root cause versus symptom.

The team also struggles with runbook discipline. Runbooks exist for the 15 most common incident types, but they drift out of date as the architecture evolves. During an incident, Jordan often discovers the runbook references a service that was renamed or a metric that was replaced.

Finally, some remediation actions are dangerous. Restarting a service is safe; scaling down a database cluster is not. The team needs guardrails that prevent well-intentioned but destructive actions during the pressure of a live incident.

### How AI Agent Canvas Solves It

CloudScale deploys AI Agent Canvas as an incident response copilot accessible to the SRE team through their internal tools portal and a dedicated Slack integration.

#### MCP Data Connections

A custom **MCP.Observability** connector bridges the monitoring stack:

- **query_metrics** --- Retrieves time-series metrics from Datadog by service, metric name, and time range. "Show me the p99 latency for the inventory-api service over the last 2 hours" returns the metric data with trend analysis and anomaly detection.
- **get_alerts** --- Pulls active and recent alerts from PagerDuty, enriched with Datadog context. "What alerts fired in the last 30 minutes?" returns a correlated view: the PagerDuty alert, the triggering Datadog monitor, the affected service, and related metrics.
- **get_deployment_history** --- Queries the deployment pipeline for recent releases. "What was deployed to production in the last 6 hours?" returns a chronological list of deployments with service name, version, deployer, and commit hash.
- **query_logs** --- Searches centralized logs by service, severity, time range, and keyword. "Show me error logs from order-processor in the last 15 minutes" returns matching log entries with context lines.
- **get_service_dependencies** --- Returns the dependency graph for a given service. "What depends on the inventory-database service?" maps upstream and downstream dependencies, helping Jordan understand blast radius.

#### Scheduled Health Checks

Proactive monitoring runs on automated schedules:

- **SLA Budget Check (Every 15 minutes)** --- Calculates remaining error budget for the current month based on actual downtime and degraded performance windows. When the budget drops below 30%, the agent sends a notification to the SRE channel with a breakdown of budget-consuming incidents.
- **Deployment Correlation Scan (Every 30 minutes)** --- After each production deployment, monitors the deployed service's error rate and latency for 30 minutes. If metrics deviate beyond baseline thresholds, the agent alerts the deployer and the on-call SRE with a pre-built correlation report.
- **Dependency Health Sweep (Every hour)** --- Checks health endpoints and key metrics for all critical-path services. Surfaces degradation trends before they trigger customer-facing alerts.
- **Certificate and Secret Expiry (Daily 6:00 AM)** --- Scans for TLS certificates and API keys approaching expiration within 30 days and creates tickets in the backlog.

#### Guardrails

Safety controls prevent destructive actions during high-stress incidents:

- **Read-Only by Default** --- The agent can query any metric, log, or alert but cannot execute remediation actions (restart services, scale infrastructure, modify configurations) without explicit human approval. Every proposed action is presented as a recommendation, not an execution.
- **Destructive Action Blocklist** --- Certain actions are permanently blocked regardless of approval: scaling down database clusters, modifying network security groups, deleting persistent volumes, and rotating production credentials. These actions must be performed through the standard change management process.
- **Blast Radius Assessment** --- Before recommending any remediation action, the agent queries the service dependency graph and reports the blast radius. "Restarting inventory-api will affect 3 downstream services (order-processor, fulfillment-engine, analytics-pipeline) serving approximately 800 requests/minute. Proceed?"
- **Incident Escalation Rules** --- If the agent detects a Severity 1 incident (multiple services affected, SLA impact), it automatically pages the incident commander and creates a structured incident channel --- but cannot acknowledge the page on Jordan's behalf.

#### Entity Management

The service catalog is maintained as managed entities:

- Each service has a registered entry: name, team owner, tier (critical/standard/best-effort), SLA target, deployment pipeline, runbook link, and dependency list.
- The agent uses this catalog to contextualize every interaction. When Jordan asks about "the inventory service," the agent resolves it to `inventory-api` (Tier 1, owned by the Platform team, 99.99% SLA target) and pulls the relevant runbook.
- When runbook steps reference outdated service names or deprecated metrics, the agent flags the discrepancy and suggests an update.

### A Day in the Life

**2:14 AM** --- Jordan's phone buzzes. PagerDuty alert: "High error rate on order-processor (>5% 5xx responses, 8-minute duration)." Jordan opens the copilot from their phone and asks: "What's happening with order-processor?"

**2:15 AM** --- The agent correlates in real time. It pulls the PagerDuty alert, the Datadog error rate metric (currently 7.2%, baseline 0.3%), recent logs (connection timeout errors to inventory-database), the dependency graph (order-processor depends on inventory-database), and inventory-database metrics (connection pool saturation at 98%). The agent presents: "Root cause appears to be connection pool exhaustion on inventory-database. Order-processor is failing because it cannot acquire database connections. Three other services (fulfillment-engine, analytics-pipeline, pricing-service) also depend on inventory-database but are not yet showing errors."

**2:17 AM** --- Jordan asks: "Was anything deployed recently?" The agent reports: "inventory-database connection pool configuration was updated 47 minutes ago by deploy pipeline. The max_connections parameter was changed from 200 to 100. This correlates with the onset of connection pool saturation."

**2:19 AM** --- Jordan asks: "Can we roll back that configuration change?" The agent responds: "I can recommend the rollback but cannot execute it directly. The rollback would restore max_connections to 200. Blast radius assessment: this change affects inventory-database only. No downstream services would be disrupted by the rollback --- in fact, it should resolve the current incident. Recommended: execute rollback through the deployment pipeline."

**2:22 AM** --- Jordan triggers the rollback through the standard pipeline. The agent monitors the recovery: connection pool utilization drops to 45% within 3 minutes, order-processor error rate returns to baseline within 5 minutes. The agent confirms: "Incident resolved. Error rate normalized. No SLA impact --- total elevated error window was 19 minutes against a 43-minute monthly budget remaining."

**2:30 AM** --- The agent drafts a preliminary postmortem with timeline, root cause, remediation steps, and a suggested action item: "Add a guardrail to the deployment pipeline that prevents connection pool reductions greater than 25% without SRE approval."

**9:00 AM** --- Jordan reviews the overnight health check results. The certificate expiry scan flagged two API keys expiring in 12 days. The dependency health sweep shows a gradual increase in p99 latency on the pricing-service over the last week --- not yet alerting, but trending toward the threshold. Jordan opens an investigation ticket.

### Key Configuration

| Component | Configuration |
|---|---|
| **Agents** | IncidentResponseAgent with `CanHandle` matching alert, incident, deployment, and service queries |
| **MCP Connections** | MCP.Observability (query_metrics, get_alerts, get_deployment_history, query_logs, get_service_dependencies) |
| **Scheduled Tasks** | SLA Budget Check (every 15 min), Deployment Correlation Scan (every 30 min), Dependency Health Sweep (hourly), Certificate/Secret Expiry (daily 6:00 AM) |
| **Guardrails** | ReadOnlyDefault (no auto-remediation), DestructiveActionBlocklist (permanent blocks on dangerous operations), BlastRadiusAssessment (dependency impact before any action), IncidentEscalationRules (auto-page for Sev1) |
| **Entity Management** | Service catalog with tier, owner, SLA target, dependencies, and runbook links |
| **User Profiles** | SRE team (full observability access + remediation recommendations), application developers (service-scoped metrics and logs), management (SLA dashboards and incident summaries) |
| **Personas** | Incident Mode (terse, action-oriented, correlates signals), Analysis Mode (detailed, trend-focused, for post-incident review) |

---

> **[Download the complete PDF guide](guides/AI-Agent-Canvas-Guide.pdf)** | **[AI-First Company Guide](guides/AI-First-Company-Guide.pdf)**

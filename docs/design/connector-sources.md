# Connector Sources

A list of the services AiAgentCanvas agents are most likely to connect to, with where to learn how to build each connector. It supports the [connector standard](connectors.md). Checked on 2026-10-03.

## How to read this list

- **API docs** is the vendor's own documentation. Links marked † responded to an automated check with a block or an error (Meta, Salesforce and OpenAI do this). Open them in a browser to confirm.
- **Reference code** lists, where they exist: an official or widely used .NET SDK, the vendor's OpenAPI spec (for client generation with Kiota), the matching Activepieces piece (TypeScript, MIT outside its `ee/` folders), and the matching FounderOS connector (TypeScript, MIT). The text in brackets is the license GitHub reports. Read the license file before copying code.
- **MCP server** is an endpoint taken from the MCP registry listing. Each one is untested. Check who operates the server, what data it sees and which scopes it requests before connecting it. For Meta Marketing API and Google Ads the listed servers are third-party aggregators, not the platform vendors. The Twilio server searches Twilio's documentation only and does not send messages.
- "Build from the docs" means no SDK, spec or open-source implementation was found in this check.

## Choosing an approach for each connector

1. Use the official or widely used .NET SDK when one exists and is maintained.
2. If the vendor publishes an OpenAPI spec, generate a typed client with Kiota and plug its bearer provider into the token store.
3. If a vendor-hosted MCP server fits the use, wrap it with the MCP client adapter.
4. Otherwise write a typed `HttpClient` connector from the vendor docs, using the Activepieces piece or the FounderOS connector to see which endpoints and auth quirks to expect.

## General resources

- [Model Context Protocol specification](https://modelcontextprotocol.io/specification)
- [OAuth 2.0 (RFC 6749)](https://www.rfc-editor.org/rfc/rfc6749)
- [PKCE (RFC 7636)](https://www.rfc-editor.org/rfc/rfc7636)
- [Kiota: generate API clients from OpenAPI](https://learn.microsoft.com/en-us/openapi/kiota/overview)
- [HTTP resilience in .NET](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [Duende.AccessTokenManagement documentation](https://docs.duendesoftware.com/foss/accesstokenmanagement/)
- [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction)

## Email and calendar

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Gmail | Read, search, draft and send email | OAuth 2.0. Sensitive scopes need app verification | [Docs](https://developers.google.com/gmail/api) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0)<br>[Activepieces: gmail](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/gmail) | `https://gmailmcp.googleapis.com/mcp/v1` |
| Microsoft 365 mail and calendar (Outlook) | Mail, calendar and availability through Microsoft Graph | OAuth 2.0 with Entra ID, delegated or app-only | [Docs](https://learn.microsoft.com/en-us/graph/outlook-mail-concept-overview) | [microsoftgraph/msgraph-sdk-dotnet](https://github.com/microsoftgraph/msgraph-sdk-dotnet) (see LICENSE file)<br>[microsoft/semantic-kernel](https://github.com/microsoft/semantic-kernel) (MIT)<br>[OpenAPI spec: microsoftgraph/msgraph-metadata](https://github.com/microsoftgraph/msgraph-metadata) (MIT)<br>[Activepieces: microsoft-outlook](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/microsoft-outlook) | `https://microsoft365.mcp.claude.com/mcp` |
| IMAP and SMTP | Mailbox access for any provider | Password or app password, or OAuth 2.0 (XOAUTH2) for Gmail and Outlook | [Docs](https://www.rfc-editor.org/rfc/rfc9051) | [jstedfast/MailKit](https://github.com/jstedfast/MailKit) (MIT)<br>[Activepieces: imap](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/imap)<br>[FounderOS: email.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/email.ts) | none found |
| Google Calendar | Read and create events, find free time | OAuth 2.0 | [Docs](https://developers.google.com/calendar/api) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0)<br>[Activepieces: google-calendar](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/google-calendar)<br>[FounderOS: gcal.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/gcal.ts)<br>[FounderOS: gcal-write.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/gcal-write.ts) | `https://calendarmcp.googleapis.com/mcp/v1` |
| Calendly | Booking links, availability, scheduled events | OAuth 2.0 or personal access token | [Docs](https://developer.calendly.com/) | [Activepieces: calendly](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/calendly) | `https://mcp.calendly.com/` |

## Messaging and voice

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Slack | Read channels, post messages, receive events | OAuth 2.0 bot token. Events need a public https endpoint | [Docs](https://docs.slack.dev/) | [soxtoby/SlackNet](https://github.com/soxtoby/SlackNet) (MIT)<br>[korotovsky/slack-mcp-server](https://github.com/korotovsky/slack-mcp-server) (MIT)<br>[Activepieces: slack](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/slack)<br>[FounderOS: slack.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/slack.ts) | `https://mcp.slack.com/mcp` |
| Microsoft Teams | Chat and channel messages through Microsoft Graph | OAuth 2.0 with Entra ID | [Docs](https://learn.microsoft.com/en-us/graph/teams-concept-overview) | [microsoftgraph/msgraph-sdk-dotnet](https://github.com/microsoftgraph/msgraph-sdk-dotnet) (see LICENSE file)<br>[OpenAPI spec: microsoftgraph/msgraph-metadata](https://github.com/microsoftgraph/msgraph-metadata) (MIT)<br>[Activepieces: microsoft-teams](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/microsoft-teams) | `https://microsoft365.mcp.claude.com/mcp` |
| WhatsApp Business Platform (Cloud API) | Send and receive customer messages, templates, webhooks | Meta access token. Business verification and template approval needed | [Docs](https://developers.facebook.com/docs/whatsapp/cloud-api) † | [Activepieces: whatsapp](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/whatsapp)<br>[FounderOS: whatsapp.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/whatsapp.ts) | none found |
| Twilio SMS and Voice | Send SMS, receive messages, call status and missed calls | API key and secret, or account SID and auth token | [Docs](https://www.twilio.com/docs/messaging) | [twilio/twilio-csharp](https://github.com/twilio/twilio-csharp) (MIT)<br>[OpenAPI spec: twilio/twilio-oai](https://github.com/twilio/twilio-oai) (MIT)<br>[Activepieces: twilio](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/twilio) | `https://mcp.twilio.com/docs` |
| Instagram and Messenger messaging | Read and answer direct messages | Meta OAuth. App review needed for messaging permissions | [Docs](https://developers.facebook.com/docs/instagram-platform) † | [Activepieces: instagram-business](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/instagram-business)<br>[Activepieces: facebook-pages](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/facebook-pages) | none found |
| ManyChat | DM automation and lead capture | API key | [Docs](https://api.manychat.com/) | [Activepieces: manychat](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/manychat)<br>[FounderOS: manychat.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/manychat.ts)<br>[FounderOS: manychat-webhook.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/manychat-webhook.ts) | none found |
| LinkedIn | Profile, posting and messaging APIs | OAuth 2.0. Most products are restricted and need approval | [Docs](https://learn.microsoft.com/en-us/linkedin/) | [Activepieces: linkedin](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/linkedin) | none found |
| Telegram Bot API | Bots for alerts and approvals | Bot token | [Docs](https://core.telegram.org/bots/api) | [TelegramBots/Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) (MIT)<br>[Activepieces: telegram-bot](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/telegram-bot) | none found |
| Discord | Community bots and channels | Bot token or OAuth 2.0 | [Docs](https://discord.com/developers/docs/intro) | [discord-net/Discord.Net](https://github.com/discord-net/Discord.Net) (MIT)<br>[Activepieces: discord](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/discord) | none found |
| ElevenLabs agents | Hosted voice agents and call transcripts | API key | [Docs](https://elevenlabs.io/docs) | build from the docs | `https://api.elevenlabs.io/v1/mcp` |

## CRM, sales and meetings

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| HubSpot | Contacts, deals, tickets, marketing email | OAuth 2.0 or private app token | [Docs](https://developers.hubspot.com/docs/api/overview) | [OpenAPI spec: HubSpot/HubSpot-public-api-spec-collection](https://github.com/HubSpot/HubSpot-public-api-spec-collection) (no license file)<br>[Activepieces: hubspot](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/hubspot) | `https://mcp.hubspot.com/anthropic` |
| Pipedrive | Deals, people, activities | OAuth 2.0 or API token | [Docs](https://developers.pipedrive.com/docs/api/v1) | [Activepieces: pipedrive](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/pipedrive) | `https://mcp.pipedrive.ai/mcp` |
| Salesforce | Objects, queries, workflows | OAuth 2.0 with a connected app | [Docs](https://developer.salesforce.com/docs/apis) † | [developerforce/Force.com-Toolkit-for-NET](https://github.com/developerforce/Force.com-Toolkit-for-NET) (BSD-3-Clause)<br>[Activepieces: salesforce](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/salesforce) | `https://api.salesforce.com/platform/mcp/v1/platform/headless-360` |
| Zoho CRM | Leads, contacts, deals | OAuth 2.0, data-center specific | [Docs](https://www.zoho.com/crm/developer/docs/api/v7/) | [Activepieces: zoho-crm](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/zoho-crm) | none found |
| Attio | Records, lists and deals | OAuth 2.0 or API key | [Docs](https://developers.attio.com/) | [FounderOS: attio.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/attio.ts) | none found |
| GoHighLevel | Funnels, contacts, pipelines | OAuth 2.0 or API key | [Docs](https://marketplace.gohighlevel.com/docs/) | [FounderOS: ghl.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/ghl.ts) | none found |
| ZoomInfo | B2B contact and company enrichment | Enterprise subscription. JWT or OAuth client credentials | [Docs](https://api-docs.zoominfo.com/) | build from the docs | `https://mcp.zoominfo.com/mcp` |
| Apollo | Prospecting and enrichment | API key | [Docs](https://docs.apollo.io/) | [Activepieces: apollo](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/apollo) | none found |
| Fathom | Meeting recordings, summaries, transcripts | API key. See docs | [Docs](https://developers.fathom.ai/) | [Activepieces: fathom](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/fathom)<br>[FounderOS: fathom.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/fathom.ts) | `https://api.fathom.ai/mcp` |
| Zoom | Meetings, recordings, transcripts | OAuth 2.0, including server-to-server | [Docs](https://developers.zoom.us/docs/api/) | [Activepieces: zoom](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/zoom) | `https://mcp.zoom.us/mcp/zoom/streamable` |
| Webex | Meetings, recordings, transcripts | OAuth 2.0 integration | [Docs](https://developer.webex.com/) | build from the docs | `https://mcp.webexapis.com/mcp/webex-meeting` |
| Otter.ai | Meeting notes and transcripts | No public API docs found. Use the MCP server | none found | build from the docs | `https://mcp.otter.ai/mcp` |
| Plaud | In-person recording transcripts and summaries | No official public API docs found. FounderOS reverse-engineers one, so check the terms | none found | [FounderOS: plaud.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/plaud.ts) | none found |
| DocuSign | Send and track agreements | OAuth 2.0, JWT grant for services | [Docs](https://developers.docusign.com/docs/esign-rest-api/) | [docusign/docusign-esign-csharp-client](https://github.com/docusign/docusign-esign-csharp-client) (MIT)<br>[OpenAPI spec: docusign/OpenAPI-Specifications](https://github.com/docusign/OpenAPI-Specifications) (MIT)<br>[Activepieces: docusign](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/docusign)<br>[FounderOS: docusign.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/docusign.ts) | `https://mcp.docusign.com/mcp` |
| PandaDoc | Proposals and contracts | API key or OAuth 2.0 | [Docs](https://developers.pandadoc.com/) | [Activepieces: pandadoc](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/pandadoc) | `https://mcp.pandadoc.com/v1/mcp` |

## Marketing and content

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| beehiiv | Newsletter posts, subscribers, performance | API key | [Docs](https://developers.beehiiv.com/) | [Activepieces: beehiiv](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/beehiiv)<br>[FounderOS: beehiiv.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/beehiiv.ts) | `https://mcp.beehiiv.com/mcp` |
| Kit (ConvertKit) | Email marketing, sequences, subscribers | API key or OAuth 2.0 | [Docs](https://developers.kit.com/) | build from the docs | `https://app.kit.com/mcp` |
| Mailchimp | Audiences and campaigns | OAuth 2.0 or API key | [Docs](https://mailchimp.com/developer/) | [Activepieces: mailchimp](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/mailchimp) | none found |
| Typefully | Draft and schedule posts for X, LinkedIn, Threads, Bluesky | API key. See docs | [Docs](https://typefully.com/docs/api) | build from the docs | `https://mcp.typefully.com/mcp` |
| Metricool | Schedule posts and analytics across networks | API token | [Docs](https://metricool.com/api/) | build from the docs | `https://ai.metricool.com/mcp` |
| Instagram publishing | Publish posts and read insights | Meta OAuth. App review needed | [Docs](https://developers.facebook.com/docs/instagram-platform/content-publishing) † | [Activepieces: instagram-business](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/instagram-business) | none found |
| YouTube Data API | Channel analytics, uploads, comments | OAuth 2.0 or API key | [Docs](https://developers.google.com/youtube/v3) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0)<br>[Activepieces: youtube](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/youtube) | none found |
| TikTok | Content posting and the Marketing API for ads | OAuth 2.0. Approval needed | [Docs](https://developers.tiktok.com/doc/overview) | build from the docs | `https://business-api.tiktok.com/open_mcp/tt-ads-mcp-layer-anthropic` |
| Meta Marketing API | Ad accounts, campaigns, performance | Meta OAuth or system user token | [Docs](https://developers.facebook.com/docs/marketing-apis) † | [FounderOS: meta-ads.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/meta-ads.ts) | `https://mcp.adspirer.com/mcp` |
| Google Ads API | Campaigns and reporting | OAuth 2.0 plus a developer token | [Docs](https://developers.google.com/google-ads/api/docs/start) | [googleads/google-ads-dotnet](https://github.com/googleads/google-ads-dotnet) (Apache-2.0) | `https://mcp.supermetrics.com/mcp` |
| Ahrefs | SEO research and rank data | API token | [Docs](https://docs.ahrefs.com/) | build from the docs | `https://api.ahrefs.com/mcp/mcp` |
| Semrush | Keyword and competitor research | API key | [Docs](https://developer.semrush.com/api/) | build from the docs | `https://mcp.semrush.com/claude/v1/mcp` |
| Google Search Console | Search queries and page performance | OAuth 2.0 | [Docs](https://developers.google.com/webmaster-tools) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0)<br>[Activepieces: google-search-console](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/google-search-console) | none found |
| Google Analytics (GA4 Data API) | Traffic and conversion reporting | OAuth 2.0 or service account | [Docs](https://developers.google.com/analytics/devguides/reporting/data/v1) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0) | `https://mcp.windsor.ai/` |
| vidIQ | YouTube, Instagram and TikTok keyword and outlier research | Account-based. Use the MCP server | none found | build from the docs | `https://mcp.vidiq.com/mcp` |
| Local Falcon | Local search ranking scans | API key. Use the MCP server | none found | build from the docs | `https://mcp.localfalcon.com/` |
| WordPress REST API | Posts, pages, media | Application passwords or OAuth plugin | [Docs](https://developer.wordpress.org/rest-api/) | [Activepieces: wordpress](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/wordpress) | none found |
| Webflow | CMS items and sites | OAuth 2.0 or site token | [Docs](https://developers.webflow.com/) | [Activepieces: webflow](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/webflow) | none found |
| Typeform | Forms, responses, webhooks | OAuth 2.0 or personal token | [Docs](https://www.typeform.com/developers/) | [Activepieces: typeform](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/typeform) | none found |
| Playwright (browser automation) | Drive websites that have no API | None (runs locally) | [Docs](https://playwright.dev/dotnet/) | [microsoft/playwright-dotnet](https://github.com/microsoft/playwright-dotnet) (MIT) | none found |

## Finance and accounting

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Stripe | Payments, balances, customers, invoices | Secret or restricted API key. OAuth for Stripe Connect | [Docs](https://docs.stripe.com/api) | [stripe/stripe-dotnet](https://github.com/stripe/stripe-dotnet) (Apache-2.0)<br>[stripe/ai](https://github.com/stripe/ai) (MIT)<br>[OpenAPI spec: stripe/openapi](https://github.com/stripe/openapi) (MIT)<br>[Activepieces: stripe](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/stripe)<br>[FounderOS: payments.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/payments.ts) | `https://mcp.stripe.com/` |
| PayPal | Orders, payouts, disputes | OAuth 2.0 client credentials | [Docs](https://developer.paypal.com/docs/api/overview/) | [FounderOS: payments.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/payments.ts) | none found |
| Square | Payments, orders, invoices | OAuth 2.0 or access token | [Docs](https://developer.squareup.com/docs) | [square/square-dotnet-sdk](https://github.com/square/square-dotnet-sdk) (MIT)<br>[OpenAPI spec: square/connect-api-specification](https://github.com/square/connect-api-specification) (Apache-2.0)<br>[Activepieces: square](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/square) | none found |
| Xero | Invoices, contacts, bank transactions, reports | OAuth 2.0 with granular scopes | [Docs](https://developer.xero.com/documentation/) | [XeroAPI/Xero-NetStandard](https://github.com/XeroAPI/Xero-NetStandard) (MIT)<br>[OpenAPI spec: XeroAPI/Xero-OpenAPI](https://github.com/XeroAPI/Xero-OpenAPI) (MIT)<br>[Activepieces: xero](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/xero) | `https://mcp.xero.com/mcp` |
| QuickBooks Online | Invoices, customers, expenses, reports | OAuth 2.0 | [Docs](https://developer.intuit.com/app/developer/qbo/docs/get-started) | [intuit/QuickBooks-V3-DotNET-SDK](https://github.com/intuit/QuickBooks-V3-DotNET-SDK) (Apache-2.0) | `https://ai-inc.quickbooks.intuit.com/v1/mcp` |
| FreshBooks | Invoices, clients, expenses | OAuth 2.0 | [Docs](https://www.freshbooks.com/api/start) | build from the docs | `https://mcp.freshbooks.com/v1` |
| MYOB | Australian accounting and payroll | OAuth 2.0 | [Docs](https://developer.myob.com/) | build from the docs | none found |
| Plaid | Bank accounts and transactions (US, Canada, Europe) | Client ID and secret, plus a Link flow for each user | [Docs](https://plaid.com/docs/) | [viceroypenguin/Going.Plaid](https://github.com/viceroypenguin/Going.Plaid) (MIT)<br>[OpenAPI spec: plaid/plaid-openapi](https://github.com/plaid/plaid-openapi) (no license file) | none found |
| Basiq | Australian open banking data | API key exchanged for a token. See docs | [Docs](https://api.basiq.io/docs) | build from the docs | none found |
| OpenAI usage and cost | Model spend by project | Admin API key | [Docs](https://platform.openai.com/docs/api-reference/usage) † | [FounderOS: codex-usage.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/codex-usage.ts) | none found |
| Anthropic usage and cost | Model spend by workspace | Admin API key | [Docs](https://docs.anthropic.com/en/api/admin-api/usage-cost/get-cost-report) | [FounderOS: claude-usage.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/claude-usage.ts) | none found |

## Knowledge and tasks

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Google Drive | Search, read and write files | OAuth 2.0 | [Docs](https://developers.google.com/drive/api) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0)<br>[taylorwilsdon/google_workspace_mcp](https://github.com/taylorwilsdon/google_workspace_mcp) (MIT)<br>[Activepieces: google-drive](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/google-drive) | `https://drivemcp.googleapis.com/mcp/v1` |
| Google Docs | Read and edit documents | OAuth 2.0 | [Docs](https://developers.google.com/docs/api) | [googleapis/google-api-dotnet-client](https://github.com/googleapis/google-api-dotnet-client) (Apache-2.0) | `https://docsmcp.googleapis.com/mcp/v1` |
| OneDrive and SharePoint | Files and sites through Microsoft Graph | OAuth 2.0 with Entra ID | [Docs](https://learn.microsoft.com/en-us/graph/onedrive-concept-overview) | [microsoftgraph/msgraph-sdk-dotnet](https://github.com/microsoftgraph/msgraph-sdk-dotnet) (see LICENSE file)<br>[OpenAPI spec: microsoftgraph/msgraph-metadata](https://github.com/microsoftgraph/msgraph-metadata) (MIT) | `https://microsoft365.mcp.claude.com/mcp` |
| Dropbox | Files and shared links | OAuth 2.0 with PKCE and short-lived tokens | [Docs](https://www.dropbox.com/developers/documentation/http/overview) | [dropbox/dropbox-sdk-dotnet](https://github.com/dropbox/dropbox-sdk-dotnet) (MIT)<br>[Activepieces: dropbox](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/dropbox) | `https://mcp.dropbox.com/claude_app_mcp` |
| Notion | Pages, databases, search | OAuth 2.0 or internal integration token | [Docs](https://developers.notion.com/) | [notion-dotnet/notion-sdk-net](https://github.com/notion-dotnet/notion-sdk-net) (MIT)<br>[makenotion/notion-mcp-server](https://github.com/makenotion/notion-mcp-server) (MIT)<br>[Activepieces: notion](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/notion) | `https://mcp.notion.com/mcp` |
| Jira and Confluence | Issues, projects, pages | OAuth 2.0 (3LO) or API token | [Docs](https://developer.atlassian.com/cloud/jira/platform/rest/v3/intro/) | [Activepieces: jira-cloud](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/jira-cloud) | none found |
| Asana | Tasks and projects | OAuth 2.0 or personal access token | [Docs](https://developers.asana.com/docs) | [Activepieces: asana](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/asana) | none found |
| ClickUp | Tasks, lists, comments | OAuth 2.0 or personal token | [Docs](https://developer.clickup.com/) | [Activepieces: clickup](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/clickup) | `https://mcp.clickup.com/mcp` |
| Linear | Issues and projects (GraphQL) | OAuth 2.0 or personal API key | [Docs](https://linear.app/developers) | [Activepieces: linear](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/linear) | none found |
| Trello | Boards, lists, cards | API key and token | [Docs](https://developer.atlassian.com/cloud/trello/rest/) | [Activepieces: trello](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/trello) | none found |
| Airtable | Bases and records | OAuth 2.0 or personal access token | [Docs](https://airtable.com/developers/web/api/introduction) | [Activepieces: airtable](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/airtable) | none found |
| Wispr Flow | Dictated notes and meetings | Account-based. Use the MCP server | none found | [FounderOS: wispr.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/wispr.ts) | `https://api.wisprflow.ai/connect/mcp` |
| Obsidian and local markdown | Local notes as a knowledge source | None (local files) | none found | [FounderOS: obsidian.ts](https://github.com/brodyautomates/FounderOS-DEMO/blob/main/lib/connectors/obsidian.ts) | none found |

## Clients and community

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Circle | Community spaces, members, posts | API token. See docs | [Docs](https://api.circle.so/) | [Activepieces: circle](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/circle) | none found |
| Kajabi | Courses, offers, customers | See docs | [Docs](https://developers.kajabi.com/) | build from the docs | none found |

## Field service and local business

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Jobber | Clients, quotes, jobs, invoices (GraphQL) | OAuth 2.0 | [Docs](https://developer.getjobber.com/docs) | build from the docs | `https://mcp.getjobber.com/mcp` |
| ServiceTitan | Jobs, dispatch, customers | OAuth 2.0 client credentials plus an app key | [Docs](https://developer.servicetitan.io/) | build from the docs | none found |
| Housecall Pro | Jobs, estimates, customers | See docs | [Docs](https://docs.housecallpro.com/) | build from the docs | none found |
| Google Business Profile | Listings and review replies | OAuth 2.0. Access request approval needed | [Docs](https://developers.google.com/my-business) | build from the docs | none found |
| Yelp Fusion | Business search and review excerpts | API key. Review access is limited | [Docs](https://docs.developer.yelp.com/) | build from the docs | none found |
| Google Maps Platform (Routes) | Travel times and routing | API key or OAuth 2.0 | [Docs](https://developers.google.com/maps/documentation/routes) | build from the docs | none found |

## Platform and operations

| Service | Used for | Auth | API docs | Reference code | MCP server |
|---|---|---|---|---|---|
| Datadog | Logs, metrics, monitors | API key plus application key | [Docs](https://docs.datadoghq.com/api/latest/) | [Activepieces: datadog](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/datadog) | none found |
| PagerDuty | Incidents and on-call | REST API key or OAuth 2.0 | [Docs](https://developer.pagerduty.com/api-reference/) | [OpenAPI spec: PagerDuty/api-schema](https://github.com/PagerDuty/api-schema) (no license file)<br>[Activepieces: pagerduty](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/pagerduty) | `https://mcp.pagerduty.com/mcp` |
| GitHub | Repositories, pull requests, issues | OAuth, GitHub App, or personal token | [Docs](https://docs.github.com/en/rest) | [octokit/octokit.net](https://github.com/octokit/octokit.net) (MIT)<br>[OpenAPI spec: github/rest-api-description](https://github.com/github/rest-api-description) (MIT)<br>[Activepieces: github](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/github) | none found |
| Azure DevOps | Repos, pipelines, work items | Entra ID OAuth or personal access token | [Docs](https://learn.microsoft.com/en-us/rest/api/azure/devops/) | [Activepieces: azure-devops](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/azure-devops) | none found |
| Azure Key Vault | Secret and certificate expiry | Entra ID with managed identity | [Docs](https://learn.microsoft.com/en-us/rest/api/keyvault/) | [Azure/azure-sdk-for-net](https://github.com/Azure/azure-sdk-for-net) (MIT) | none found |
| Azure Blob Storage | Backups and file storage | Entra ID or SAS token | [Docs](https://learn.microsoft.com/en-us/rest/api/storageservices/) | [Azure/azure-sdk-for-net](https://github.com/Azure/azure-sdk-for-net) (MIT)<br>[Activepieces: azure-blob-storage](https://github.com/activepieces/activepieces/tree/main/packages/pieces/community/azure-blob-storage) | none found |
| Amazon S3 | Backups and file storage | AWS Signature V4 | [Docs](https://docs.aws.amazon.com/AmazonS3/latest/API/Welcome.html) | [aws/aws-sdk-net](https://github.com/aws/aws-sdk-net) (Apache-2.0) | none found |

## Notes and gaps

- No official .NET SDK was found for HubSpot or PandaDoc. HubSpot publishes OpenAPI specs.
- The Slack OpenAPI spec repository is archived and last changed in 2021, so it is not listed. Use SlackNet or the Web API docs.
- Sources with no license file (HubSpot's spec collection, Plaid's and PagerDuty's specs) can be read to learn the API but may not be copied without permission.
- Not checked: Kajabi and Circle documentation depth, Housecall Pro and ServiceTitan terms, and the quality of the vendor MCP servers.
- Services that restrict access need approval before a connector can run for real users: WhatsApp (business verification), Instagram and Messenger (app review), LinkedIn (restricted products), TikTok, Google Business Profile (access request), and Google sensitive scopes (app verification).

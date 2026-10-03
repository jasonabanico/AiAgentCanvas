"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

interface Connector {
  id: string;
  displayName: string;
  category: string;
  auth: string;
  scopes: string[];
  capabilities: string;
  oAuthProvider: string | null;
}

interface RuntimeInfo {
  connectionId: string;
  connectorId: string;
  label: string;
  toolPrefix: string;
  state: string;
  detail: string;
  toolCount: number;
  capabilities: string;
  checkedAt: string | null;
  webhookUrl: string | null;
}

interface Provider {
  slug: string;
  name: string;
  appConfigured: boolean;
}

const STATE_COLORS: Record<string, { bg: string; fg: string }> = {
  Connected: { bg: "#f0fdf4", fg: "#166534" },
  NeedsReauth: { bg: "#fffbeb", fg: "#92400e" },
  Error: { bg: "#fef2f2", fg: "#991b1b" },
  NotConfigured: { bg: "#f3f4f6", fg: "#374151" },
};

/** Reads lines such as "from_number=+14155550100" into an object. */
function parsePairs(text: string): Record<string, string> {
  const result: Record<string, string> = {};
  for (const line of text.split("\n")) {
    const index = line.indexOf("=");
    if (index > 0) result[line.slice(0, index).trim()] = line.slice(index + 1).trim();
  }
  return result;
}

export default function ConnectionsView() {
  const [catalog, setCatalog] = useState<Connector[]>([]);
  const [connections, setConnections] = useState<RuntimeInfo[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [banner, setBanner] = useState<{ ok: boolean; text: string } | null>(null);
  const [disabled, setDisabled] = useState<string | null>(null);

  const [connectorId, setConnectorId] = useState("");
  const [label, setLabel] = useState("");
  const [settings, setSettings] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [keyId, setKeyId] = useState("");
  const [keySecret, setKeySecret] = useState("");
  const [extras, setExtras] = useState("");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [busy, setBusy] = useState(false);

  const selected = catalog.find((c) => c.id === connectorId);
  const provider = providers.find((p) => p.slug === selected?.oAuthProvider);

  const load = useCallback(async () => {
    try {
      const [cat, conns, provs] = await Promise.all([
        fetch("/api/connectors"),
        fetch("/api/connectors/connections"),
        fetch("/api/connections/oauth/providers"),
      ]);

      if (cat.status === 404 || conns.status === 404) {
        setDisabled("Connectors are off. Set Features:Connections and Features:Connectors to true.");
        return;
      }
      if (cat.status === 401 || cat.status === 403) {
        setDisabled("Connections require authentication.");
        return;
      }

      setCatalog(await cat.json());
      setConnections(await conns.json());
      if (provs.ok) setProviders(await provs.json());
      setDisabled(null);
    } catch {
      setDisabled("Could not reach the server.");
    }
  }, []);

  useEffect(() => {
    load();
    const timer = setInterval(load, 15_000);
    return () => clearInterval(timer);
  }, [load]);

  // The OAuth callback returns here with the result in the address.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const result = params.get("connection");
    if (result) {
      setBanner({ ok: result === "ok", text: params.get("message") ?? "" });
      window.history.replaceState(null, "", window.location.pathname);
    }
  }, []);

  async function call(url: string, init: RequestInit): Promise<Response> {
    setBusy(true);
    try {
      return await fetch(url, init);
    } finally {
      setBusy(false);
    }
  }

  const json = { "Content-Type": "application/json" };

  async function startOAuth() {
    if (!selected?.oAuthProvider) return;
    const res = await call(`/api/connections/oauth/${selected.oAuthProvider}/start`, {
      method: "POST",
      headers: json,
      body: JSON.stringify({
        connectorId: selected.id,
        label: label || selected.displayName,
        scopes: null,
        returnUrl: "/",
        reconnectConnectionId: null,
      }),
    });
    const body = await res.json();
    if (res.ok) window.location.href = body.authorizeUrl;
    else setBanner({ ok: false, text: body.error ?? "Could not start the connection." });
  }

  async function saveApp(e: FormEvent) {
    e.preventDefault();
    if (!selected?.oAuthProvider) return;
    const res = await call(`/api/connections/oauth/apps/${selected.oAuthProvider}`, {
      method: "PUT",
      headers: json,
      body: JSON.stringify({ clientId, clientSecret }),
    });
    if (res.ok) {
      setClientId("");
      setClientSecret("");
      setBanner({ ok: true, text: "Saved the application credentials." });
      load();
    } else {
      const body = await res.json();
      setBanner({ ok: false, text: body.error ?? "Could not save." });
    }
  }

  async function createKey(e: FormEvent) {
    e.preventDefault();
    if (!selected) return;
    const isPair = selected.auth === "KeyPair";
    const res = await call("/api/connections/key", {
      method: "POST",
      headers: json,
      body: JSON.stringify({
        connectorId: selected.id,
        label: label || selected.displayName,
        kind: selected.auth,
        apiKey: isPair ? null : apiKey,
        keyId: isPair ? keyId : null,
        keySecret: isPair ? keySecret : null,
        extras: parsePairs(extras),
        settings: parsePairs(settings),
      }),
    });
    if (res.ok) {
      setApiKey("");
      setKeyId("");
      setKeySecret("");
      setExtras("");
      setSettings("");
      setLabel("");
      setBanner({ ok: true, text: "Saved. The connection starts within a few seconds." });
      await call("/api/connectors/reconcile", { method: "POST" });
      load();
    } else {
      const body = await res.json();
      setBanner({ ok: false, text: body.error ?? "Could not save." });
    }
  }

  async function check(id: string) {
    await call(`/api/connectors/connections/${id}/check`, { method: "POST" });
    load();
  }

  async function remove(c: RuntimeInfo) {
    if (!window.confirm(`Remove "${c.label}"? Its stored credentials are deleted and its tools stop.`)) return;
    await call(`/api/connections/${c.connectionId}`, { method: "DELETE" });
    await call("/api/connectors/reconcile", { method: "POST" });
    load();
  }

  function reconnect(c: RuntimeInfo) {
    const entry = catalog.find((k) => k.id === c.connectorId);
    if (!entry?.oAuthProvider) return;
    call(`/api/connections/oauth/${entry.oAuthProvider}/start`, {
      method: "POST",
      headers: json,
      body: JSON.stringify({
        connectorId: entry.id,
        label: c.label,
        scopes: null,
        returnUrl: "/",
        reconnectConnectionId: c.connectionId,
      }),
    })
      .then((r) => r.json().then((body) => ({ ok: r.ok, body })))
      .then(({ ok, body }) => {
        if (ok) window.location.href = body.authorizeUrl;
        else setBanner({ ok: false, text: body.error ?? "Could not start the connection." });
      });
  }

  if (disabled) {
    return (
      <div style={styles.wrap}>
        <div style={styles.notice}>{disabled}</div>
      </div>
    );
  }

  return (
    <div style={styles.wrap}>
      {banner && (
        <div style={{ ...styles.banner, ...(banner.ok ? styles.ok : styles.bad) }}>
          {banner.text}
          <button onClick={() => setBanner(null)} style={styles.dismiss}>×</button>
        </div>
      )}

      <h2 style={styles.h2}>Connected accounts</h2>
      {connections.length === 0 && <div style={styles.empty}>No accounts are connected yet.</div>}
      <div style={styles.cards}>
        {connections.map((c) => {
          const color = STATE_COLORS[c.state] ?? STATE_COLORS.NotConfigured;
          return (
            <div key={c.connectionId} style={styles.card}>
              <div style={styles.cardTop}>
                <strong>{c.label}</strong>
                <span style={{ ...styles.badge, background: color.bg, color: color.fg }}>{c.state}</span>
              </div>
              <div style={styles.muted}>
                {c.connectorId} · {c.toolCount} tools · prefix <code>{c.toolPrefix}_</code>
              </div>
              <div style={styles.detail}>{c.detail}</div>
              {c.webhookUrl && (
                <div style={styles.muted}>
                  Webhook: <code>{c.webhookUrl}</code>
                </div>
              )}
              <div style={styles.actions}>
                <button style={styles.button} disabled={busy} onClick={() => check(c.connectionId)}>Check now</button>
                {c.state === "NeedsReauth" && catalog.find((k) => k.id === c.connectorId)?.oAuthProvider && (
                  <button style={styles.primary} disabled={busy} onClick={() => reconnect(c)}>Reconnect</button>
                )}
                <button style={styles.danger} disabled={busy} onClick={() => remove(c)}>Remove</button>
              </div>
            </div>
          );
        })}
      </div>

      <h2 style={styles.h2}>Add an account</h2>
      <select value={connectorId} onChange={(e) => setConnectorId(e.target.value)} style={styles.field}>
        <option value="">Choose a connector…</option>
        {catalog.map((c) => (
          <option key={c.id} value={c.id}>{c.displayName} ({c.category})</option>
        ))}
      </select>

      {selected && (
        <div style={styles.form}>
          <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder={`Label (default: ${selected.displayName})`} style={styles.field} />

          {selected.auth === "OAuth2" && (
            <>
              {provider && !provider.appConfigured && (
                <form onSubmit={saveApp} style={styles.form}>
                  <div style={styles.muted}>
                    {provider.name} needs an OAuth application. Register one with the provider, then enter its client id and secret here. The secret is stored encrypted.
                  </div>
                  <input value={clientId} onChange={(e) => setClientId(e.target.value)} placeholder="Client id" style={styles.field} />
                  <input value={clientSecret} onChange={(e) => setClientSecret(e.target.value)} placeholder="Client secret" type="password" autoComplete="off" style={styles.field} />
                  <button type="submit" style={styles.button} disabled={busy || !clientId || !clientSecret}>Save application</button>
                </form>
              )}
              {selected.scopes.length > 0 && (
                <div style={styles.muted}>Requests: {selected.scopes.join(", ")}</div>
              )}
              <button style={styles.primary} disabled={busy || (provider ? !provider.appConfigured : false)} onClick={startOAuth}>
                Connect with {provider?.name ?? "the provider"}
              </button>
            </>
          )}

          {(selected.auth === "ApiKey" || selected.auth === "KeyPair") && (
            <form onSubmit={createKey} style={styles.form}>
              {selected.auth === "ApiKey" ? (
                <input value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder="API key" type="password" autoComplete="off" style={styles.field} />
              ) : (
                <>
                  <input value={keyId} onChange={(e) => setKeyId(e.target.value)} placeholder="Key id (for Twilio, an API key SID or the account SID)" style={styles.field} />
                  <input value={keySecret} onChange={(e) => setKeySecret(e.target.value)} placeholder="Key secret" type="password" autoComplete="off" style={styles.field} />
                </>
              )}
              <textarea value={extras} onChange={(e) => setExtras(e.target.value)} rows={2} placeholder={"Further secrets, one per line\nAuthToken=…"} style={styles.field} />
              <textarea value={settings} onChange={(e) => setSettings(e.target.value)} rows={4} placeholder={"Settings, one per line\naccount_sid=AC…\nfrom_number=+14155550100\nendpoint=https://… (MCP servers)"} style={styles.field} />
              <button type="submit" style={styles.primary} disabled={busy}>Save connection</button>
            </form>
          )}
        </div>
      )}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: { flex: 1, overflow: "auto", padding: "16px 24px", background: "#f9fafb" },
  h2: { fontSize: "1rem", margin: "16px 0 8px", color: "#111827" },
  notice: { padding: "12px 16px", background: "#fffbeb", border: "1px solid #fde68a", borderRadius: "8px", color: "#92400e" },
  banner: { display: "flex", justifyContent: "space-between", padding: "10px 16px", borderRadius: "8px", marginBottom: "12px", fontSize: "0.875rem" },
  ok: { background: "#f0fdf4", color: "#166534", border: "1px solid #86efac" },
  bad: { background: "#fef2f2", color: "#991b1b", border: "1px solid #fecaca" },
  dismiss: { border: "none", background: "none", cursor: "pointer", fontSize: "1rem", color: "inherit" },
  empty: { color: "#6b7280", fontSize: "0.875rem" },
  cards: { display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))", gap: "12px" },
  card: { background: "#fff", border: "1px solid #e5e7eb", borderRadius: "8px", padding: "12px 16px", display: "flex", flexDirection: "column", gap: "6px" },
  cardTop: { display: "flex", justifyContent: "space-between", alignItems: "center" },
  badge: { padding: "2px 8px", borderRadius: "999px", fontSize: "0.75rem", fontWeight: 500 },
  muted: { fontSize: "0.8125rem", color: "#6b7280", wordBreak: "break-all" },
  detail: { fontSize: "0.8125rem", color: "#374151" },
  actions: { display: "flex", gap: "8px", marginTop: "4px" },
  form: { display: "flex", flexDirection: "column", gap: "8px", maxWidth: "520px", marginTop: "8px" },
  field: { padding: "8px", border: "1px solid #d1d5db", borderRadius: "6px", fontFamily: "inherit", fontSize: "0.875rem", background: "#fff" },
  button: { padding: "6px 12px", border: "1px solid #d1d5db", borderRadius: "6px", background: "#fff", cursor: "pointer" },
  primary: { padding: "6px 12px", border: "1px solid #2563eb", borderRadius: "6px", background: "#2563eb", color: "#fff", cursor: "pointer" },
  danger: { padding: "6px 12px", border: "1px solid #fecaca", borderRadius: "6px", background: "#fff", color: "#991b1b", cursor: "pointer" },
};

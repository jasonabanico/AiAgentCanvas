"use client";

import { useCallback, useEffect, useState } from "react";

interface Pending {
  requestId: string;
  kind: string;
  summary: string;
}

interface TranscriptEntry {
  agent: string;
  text: string;
}

interface Spec {
  kind: string;
  task: string;
  agents: string[];
  lead: string | null;
  maxRounds: number | null;
}

interface OrchestrationRun {
  id: string;
  spec: Spec;
  status: string;
  pending: Pending | null;
  result: string | null;
  transcript: TranscriptEntry[];
  error: string | null;
  createdAt: string;
  updatedAt: string;
}

const STATUS_COLORS: Record<string, { bg: string; fg: string }> = {
  WaitingForInput: { bg: "#fffbeb", fg: "#92400e" },
  Running: { bg: "#eff6ff", fg: "#1e40af" },
  Completed: { bg: "#f0fdf4", fg: "#166534" },
  Failed: { bg: "#fef2f2", fg: "#991b1b" },
  Cancelled: { bg: "#f3f4f6", fg: "#374151" },
  Interrupted: { bg: "#fff7ed", fg: "#9a3412" },
};

const STATUSES = ["", "WaitingForInput", "Running", "Completed", "Failed", "Cancelled", "Interrupted"];

export default function OrchestrationsView() {
  const [runs, setRuns] = useState<OrchestrationRun[]>([]);
  const [status, setStatus] = useState("WaitingForInput");
  const [selected, setSelected] = useState<OrchestrationRun | null>(null);
  const [feedback, setFeedback] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch(`/api/orchestrations${status ? `?status=${status}` : ""}`);
      if (res.status === 404) {
        setMessage("Agent orchestration is off. Set Features:AgentOrchestration to true.");
        return;
      }
      if (res.status === 401 || res.status === 403) {
        setMessage("Orchestration runs require authentication.");
        return;
      }
      if (!res.ok) {
        setMessage(`The server answered ${res.status}.`);
        return;
      }
      setRuns(await res.json());
      setMessage(null);
    } catch {
      setMessage("Could not reach the server.");
    }
  }, [status]);

  useEffect(() => {
    load();
    const timer = setInterval(load, 15_000);
    return () => clearInterval(timer);
  }, [load]);

  async function open(id: string) {
    const res = await fetch(`/api/orchestrations/${encodeURIComponent(id)}`);
    if (res.ok) {
      setSelected(await res.json());
      setFeedback("");
      setNotice(null);
    }
  }

  async function post(path: string, body?: unknown) {
    if (!selected) return;
    setBusy(true);
    try {
      const res = await fetch(`/api/orchestrations/${encodeURIComponent(selected.id)}/${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) {
        setNotice({ ok: false, text: data.error ?? "That did not work." });
        return;
      }
      setSelected(data);
      setFeedback("");
      setNotice({ ok: true, text: `The run is now ${data.status}.` });
      load();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={styles.wrap}>
      <div style={styles.main}>
        <div style={styles.filters}>
          <select value={status} onChange={(e) => setStatus(e.target.value)} style={styles.select}>
            {STATUSES.map((s) => (
              <option key={s} value={s}>{s || "Any status"}</option>
            ))}
          </select>
          <button onClick={load} style={styles.button}>Refresh</button>
        </div>

        {message && <div style={styles.notice}>{message}</div>}
        {!message && runs.length === 0 && <div style={styles.empty}>No runs match.</div>}

        <div style={styles.list}>
          {runs.map((r) => (
            <div
              key={r.id}
              onClick={() => open(r.id)}
              style={{ ...styles.card, ...(selected?.id === r.id ? styles.cardActive : {}) }}
            >
              <div style={styles.cardTop}>
                <strong>{r.spec.kind}</strong>
                <Badge status={r.status} />
              </div>
              <div style={styles.task}>{r.spec.task}</div>
              <div style={styles.muted}>
                {r.spec.agents.join(", ")} · {new Date(r.updatedAt).toLocaleString()}
              </div>
            </div>
          ))}
        </div>
      </div>

      {selected && (
        <aside style={styles.detail}>
          <div style={styles.detailHeader}>
            <strong>{selected.spec.kind}</strong>
            <button onClick={() => setSelected(null)} style={styles.close} title="Close">×</button>
          </div>
          <div style={styles.meta}>
            <Badge status={selected.status} /> {selected.spec.agents.join(", ")}
            {selected.spec.lead && <> · lead {selected.spec.lead}</>}
          </div>

          <div style={styles.blockTitle}>Task</div>
          <pre style={styles.pre}>{selected.spec.task}</pre>

          {selected.status === "WaitingForInput" && selected.pending && (
            <>
              <div style={styles.blockTitle}>
                {selected.pending.kind === "plan_review" ? "The manager proposes this plan" : "Waiting for your decision"}
              </div>
              <pre style={{ ...styles.pre, background: "#fffbeb" }}>{selected.pending.summary}</pre>
              <textarea
                value={feedback}
                onChange={(e) => setFeedback(e.target.value)}
                rows={3}
                placeholder="What should change? Needed to ask for a revision."
                style={styles.textarea}
              />
              <div style={styles.actions}>
                <button style={styles.primary} disabled={busy} onClick={() => post("respond", { approve: true })}>
                  Approve
                </button>
                <button
                  style={styles.button}
                  disabled={busy || !feedback.trim()}
                  onClick={() => post("respond", { approve: false, feedback })}
                >
                  Ask for changes
                </button>
                <button style={styles.danger} disabled={busy} onClick={() => post("cancel")}>
                  Cancel run
                </button>
              </div>
            </>
          )}

          {selected.status === "Interrupted" && (
            <div style={styles.actions}>
              <button style={styles.primary} disabled={busy} onClick={() => post("resume")}>
                Resume from checkpoint
              </button>
              <button style={styles.danger} disabled={busy} onClick={() => post("cancel")}>
                Cancel run
              </button>
            </div>
          )}

          {notice && (
            <div style={{ ...styles.banner, ...(notice.ok ? styles.ok : styles.bad) }}>{notice.text}</div>
          )}

          {selected.error && (
            <>
              <div style={styles.blockTitle}>Error</div>
              <pre style={{ ...styles.pre, background: "#fef2f2", color: "#991b1b" }}>{selected.error}</pre>
            </>
          )}

          {selected.result && (
            <>
              <div style={styles.blockTitle}>Result</div>
              <pre style={styles.pre}>{selected.result}</pre>
            </>
          )}

          {selected.transcript.length > 0 && (
            <>
              <div style={styles.blockTitle}>Transcript</div>
              {selected.transcript.map((t, i) => (
                <div key={i} style={styles.turn}>
                  <div style={styles.agent}>{t.agent}</div>
                  <div style={styles.turnText}>{t.text}</div>
                </div>
              ))}
            </>
          )}
        </aside>
      )}
    </div>
  );
}

function Badge({ status }: { status: string }) {
  const color = STATUS_COLORS[status] ?? STATUS_COLORS.Cancelled;
  return <span style={{ ...styles.badge, background: color.bg, color: color.fg }}>{status}</span>;
}

const styles: Record<string, React.CSSProperties> = {
  wrap: { flex: 1, display: "flex", overflow: "hidden", background: "#f9fafb" },
  main: { flex: 1, overflow: "auto", padding: "16px 24px", minWidth: 0 },
  filters: { display: "flex", gap: "8px", alignItems: "center", marginBottom: "16px" },
  select: { padding: "6px 8px", border: "1px solid #d1d5db", borderRadius: "6px", background: "#fff" },
  button: { padding: "6px 12px", border: "1px solid #d1d5db", borderRadius: "6px", background: "#fff", cursor: "pointer" },
  primary: { padding: "6px 12px", border: "1px solid #2563eb", borderRadius: "6px", background: "#2563eb", color: "#fff", cursor: "pointer" },
  danger: { padding: "6px 12px", border: "1px solid #fecaca", borderRadius: "6px", background: "#fff", color: "#991b1b", cursor: "pointer" },
  notice: { padding: "12px 16px", background: "#fffbeb", border: "1px solid #fde68a", borderRadius: "8px", color: "#92400e" },
  empty: { color: "#6b7280", padding: "24px 0" },
  list: { display: "flex", flexDirection: "column", gap: "8px" },
  card: { background: "#fff", border: "1px solid #e5e7eb", borderRadius: "8px", padding: "10px 14px", cursor: "pointer" },
  cardActive: { borderColor: "#2563eb", background: "#eff6ff" },
  cardTop: { display: "flex", justifyContent: "space-between", alignItems: "center" },
  task: { fontSize: "0.875rem", margin: "4px 0", color: "#111827" },
  muted: { fontSize: "0.75rem", color: "#6b7280" },
  badge: { padding: "2px 8px", borderRadius: "999px", fontSize: "0.75rem", fontWeight: 500 },
  detail: { width: "460px", maxWidth: "50%", borderLeft: "1px solid #e5e7eb", background: "#fff", overflow: "auto", padding: "16px" },
  detailHeader: { display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" },
  close: { border: "none", background: "none", fontSize: "1.25rem", cursor: "pointer", color: "#6b7280" },
  meta: { fontSize: "0.8125rem", color: "#4b5563", marginBottom: "8px" },
  blockTitle: { margin: "12px 0 4px", fontSize: "0.75rem", fontWeight: 600, color: "#6b7280", textTransform: "uppercase" },
  pre: { margin: 0, padding: "8px", background: "#f9fafb", borderRadius: "6px", whiteSpace: "pre-wrap", wordBreak: "break-word", fontSize: "0.8125rem", maxHeight: "260px", overflow: "auto" },
  textarea: { width: "100%", marginTop: "8px", padding: "8px", border: "1px solid #d1d5db", borderRadius: "6px", fontFamily: "inherit", fontSize: "0.875rem", boxSizing: "border-box" },
  actions: { display: "flex", gap: "8px", marginTop: "8px", flexWrap: "wrap" },
  banner: { padding: "8px 12px", borderRadius: "6px", marginTop: "10px", fontSize: "0.8125rem" },
  ok: { background: "#f0fdf4", color: "#166534", border: "1px solid #86efac" },
  bad: { background: "#fef2f2", color: "#991b1b", border: "1px solid #fecaca" },
  turn: { borderTop: "1px solid #f3f4f6", padding: "8px 0" },
  agent: { fontSize: "0.75rem", fontWeight: 600, color: "#2563eb" },
  turnText: { fontSize: "0.8125rem", whiteSpace: "pre-wrap", wordBreak: "break-word" },
};

"use client";

import { useCallback, useEffect, useState } from "react";

interface ToolCall {
  name: string;
  outcome: string;
  durationMs: number;
}

interface Run {
  id: string;
  parentId: string | null;
  source: string;
  agentName: string;
  triggerId: string | null;
  taskId: string | null;
  startedAt: string;
  endedAt: string | null;
  status: string;
  input: string | null;
  output: string | null;
  error: string | null;
  inputTokens: number;
  outputTokens: number;
  modelCalls: number;
  estimatedCost: number;
  toolCallCount: number;
  termination: string | null;
  toolCalls: ToolCall[];
}

interface Totals {
  runs: number;
  failed: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCost: number;
}

interface Detail {
  run: Run;
  children: Run[];
}

const STATUSES = ["", "Running", "Succeeded", "Failed", "Cancelled", "Abandoned"];
const SOURCES = ["", "Interactive", "Scheduled", "Trigger", "Handoff", "Job"];
const WINDOWS = [
  { label: "Last hour", hours: 1 },
  { label: "Last 24 hours", hours: 24 },
  { label: "Last 7 days", hours: 168 },
  { label: "Last 30 days", hours: 720 },
];

const STATUS_COLORS: Record<string, { bg: string; fg: string }> = {
  Succeeded: { bg: "#f0fdf4", fg: "#166534" },
  Failed: { bg: "#fef2f2", fg: "#991b1b" },
  Running: { bg: "#eff6ff", fg: "#1e40af" },
  Cancelled: { bg: "#f3f4f6", fg: "#374151" },
  Abandoned: { bg: "#fffbeb", fg: "#92400e" },
};

function duration(run: Run): string {
  if (!run.endedAt) return "running";
  const ms = new Date(run.endedAt).getTime() - new Date(run.startedAt).getTime();
  if (ms < 1000) return `${ms} ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
  return `${Math.floor(ms / 60_000)} min ${Math.round((ms % 60_000) / 1000)} s`;
}

function cost(value: number): string {
  return `$${value.toFixed(value < 1 ? 4 : 2)}`;
}

export default function RunsView() {
  const [runs, setRuns] = useState<Run[]>([]);
  const [totals, setTotals] = useState<Totals | null>(null);
  const [status, setStatus] = useState("");
  const [source, setSource] = useState("");
  const [agent, setAgent] = useState("");
  const [hours, setHours] = useState(24);
  const [selected, setSelected] = useState<Detail | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [auto, setAuto] = useState(true);

  const load = useCallback(async () => {
    const query = new URLSearchParams({ limit: "100", hours: String(hours) });
    if (status) query.set("status", status);
    if (source) query.set("source", source);
    if (agent.trim()) query.set("agent", agent.trim());

    try {
      const [list, sum] = await Promise.all([
        fetch(`/api/runs?${query}`),
        fetch(`/api/runs/totals?hours=${hours}${agent.trim() ? `&agent=${encodeURIComponent(agent.trim())}` : ""}`),
      ]);

      if (list.status === 404) {
        setMessage("The run ledger is off. Set Features:RunLedger to true to record runs.");
        return;
      }
      if (list.status === 401 || list.status === 403) {
        setMessage("The run ledger requires authentication.");
        return;
      }
      if (!list.ok) {
        setMessage(`The run ledger answered ${list.status}.`);
        return;
      }

      setRuns(await list.json());
      if (sum.ok) setTotals(await sum.json());
      setMessage(null);
    } catch {
      setMessage("Could not reach the server.");
    }
  }, [status, source, agent, hours]);

  useEffect(() => {
    load();
    if (!auto) return;
    const timer = setInterval(load, 15_000);
    return () => clearInterval(timer);
  }, [load, auto]);

  async function open(id: string) {
    const res = await fetch(`/api/runs/${encodeURIComponent(id)}`);
    if (res.ok) setSelected(await res.json());
  }

  return (
    <div style={styles.wrap}>
      <div style={styles.main}>
        <div style={styles.filters}>
          <select value={hours} onChange={(e) => setHours(Number(e.target.value))} style={styles.select}>
            {WINDOWS.map((w) => (
              <option key={w.hours} value={w.hours}>{w.label}</option>
            ))}
          </select>
          <select value={status} onChange={(e) => setStatus(e.target.value)} style={styles.select}>
            {STATUSES.map((s) => (
              <option key={s} value={s}>{s || "Any status"}</option>
            ))}
          </select>
          <select value={source} onChange={(e) => setSource(e.target.value)} style={styles.select}>
            {SOURCES.map((s) => (
              <option key={s} value={s}>{s || "Any source"}</option>
            ))}
          </select>
          <input
            value={agent}
            onChange={(e) => setAgent(e.target.value)}
            placeholder="Agent name"
            style={styles.input}
          />
          <label style={styles.auto}>
            <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} /> Refresh every 15 s
          </label>
          <button onClick={load} style={styles.button}>Refresh</button>
        </div>

        {totals && (
          <div style={styles.totals}>
            <Stat label="Runs" value={String(totals.runs)} />
            <Stat label="Failed" value={String(totals.failed)} danger={totals.failed > 0} />
            <Stat label="Input tokens" value={totals.inputTokens.toLocaleString()} />
            <Stat label="Output tokens" value={totals.outputTokens.toLocaleString()} />
            <Stat label="Estimated cost" value={cost(totals.estimatedCost)} />
          </div>
        )}

        {message && <div style={styles.notice}>{message}</div>}

        {!message && runs.length === 0 && <div style={styles.empty}>No runs match these filters.</div>}

        {runs.length > 0 && (
          <table style={styles.table}>
            <thead>
              <tr>
                {["Started", "Agent", "Source", "Status", "Duration", "Tools", "Tokens in/out", "Cost"].map((h) => (
                  <th key={h} style={styles.th}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {runs.map((r) => (
                <tr
                  key={r.id}
                  onClick={() => open(r.id)}
                  style={{ ...styles.row, background: selected?.run.id === r.id ? "#eff6ff" : undefined }}
                >
                  <td style={styles.td}>{new Date(r.startedAt).toLocaleString()}</td>
                  <td style={styles.td}>{r.agentName}</td>
                  <td style={styles.td}>{r.source}</td>
                  <td style={styles.td}><Badge status={r.status} /></td>
                  <td style={styles.td}>{duration(r)}</td>
                  <td style={styles.td}>{r.toolCallCount}</td>
                  <td style={styles.td}>{r.inputTokens.toLocaleString()} / {r.outputTokens.toLocaleString()}</td>
                  <td style={styles.td}>{cost(r.estimatedCost)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {selected && (
        <aside style={styles.detail}>
          <div style={styles.detailHeader}>
            <strong>{selected.run.agentName}</strong>
            <button onClick={() => setSelected(null)} style={styles.close} title="Close">×</button>
          </div>
          <div style={styles.meta}>
            <Badge status={selected.run.status} /> {selected.run.source} · {duration(selected.run)}
            {selected.run.triggerId && <> · trigger {selected.run.triggerId}</>}
            {selected.run.termination && <> · ended by {selected.run.termination}</>}
          </div>

          {selected.run.error && <Block title="Error" text={selected.run.error} danger />}
          <Block title="Input" text={selected.run.input} />
          <Block title="Output" text={selected.run.output} />

          {selected.run.toolCalls.length > 0 && (
            <>
              <div style={styles.blockTitle}>Tool calls ({selected.run.toolCallCount})</div>
              <ul style={styles.list}>
                {selected.run.toolCalls.map((t, i) => (
                  <li key={i}>
                    <code>{t.name}</code> {t.outcome} · {Math.round(t.durationMs)} ms
                  </li>
                ))}
              </ul>
            </>
          )}

          {selected.children.length > 0 && (
            <>
              <div style={styles.blockTitle}>Delegated runs</div>
              <ul style={styles.list}>
                {selected.children.map((c) => (
                  <li key={c.id}>
                    <a href="#" onClick={(e) => { e.preventDefault(); open(c.id); }}>{c.agentName}</a>{" "}
                    <Badge status={c.status} /> {duration(c)}
                  </li>
                ))}
              </ul>
            </>
          )}

          {selected.run.parentId && (
            <button onClick={() => open(selected.run.parentId!)} style={styles.button}>Open parent run</button>
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

function Stat({ label, value, danger }: { label: string; value: string; danger?: boolean }) {
  return (
    <div style={styles.stat}>
      <div style={styles.statLabel}>{label}</div>
      <div style={{ ...styles.statValue, color: danger ? "#991b1b" : "#111827" }}>{value}</div>
    </div>
  );
}

function Block({ title, text, danger }: { title: string; text: string | null; danger?: boolean }) {
  if (!text) return null;
  return (
    <>
      <div style={styles.blockTitle}>{title}</div>
      <pre style={{ ...styles.pre, ...(danger ? { background: "#fef2f2", color: "#991b1b" } : {}) }}>{text}</pre>
    </>
  );
}

const styles: Record<string, React.CSSProperties> = {
  wrap: { flex: 1, display: "flex", overflow: "hidden", background: "#f9fafb" },
  main: { flex: 1, overflow: "auto", padding: "16px 24px", minWidth: 0 },
  filters: { display: "flex", gap: "8px", flexWrap: "wrap", alignItems: "center", marginBottom: "16px" },
  select: { padding: "6px 8px", border: "1px solid #d1d5db", borderRadius: "6px", background: "#fff" },
  input: { padding: "6px 8px", border: "1px solid #d1d5db", borderRadius: "6px" },
  auto: { fontSize: "0.8125rem", color: "#4b5563" },
  button: { padding: "6px 12px", border: "1px solid #d1d5db", borderRadius: "6px", background: "#fff", cursor: "pointer" },
  totals: { display: "flex", gap: "12px", flexWrap: "wrap", marginBottom: "16px" },
  stat: { background: "#fff", border: "1px solid #e5e7eb", borderRadius: "8px", padding: "10px 16px", minWidth: "110px" },
  statLabel: { fontSize: "0.75rem", color: "#6b7280" },
  statValue: { fontSize: "1.25rem", fontWeight: 600 },
  notice: { padding: "12px 16px", background: "#fffbeb", border: "1px solid #fde68a", borderRadius: "8px", color: "#92400e" },
  empty: { color: "#6b7280", padding: "24px 0" },
  table: { width: "100%", borderCollapse: "collapse", background: "#fff", border: "1px solid #e5e7eb", fontSize: "0.875rem" },
  th: { textAlign: "left", padding: "8px 12px", borderBottom: "1px solid #e5e7eb", color: "#6b7280", fontWeight: 500 },
  td: { padding: "8px 12px", borderBottom: "1px solid #f3f4f6" },
  row: { cursor: "pointer" },
  badge: { padding: "2px 8px", borderRadius: "999px", fontSize: "0.75rem", fontWeight: 500 },
  detail: { width: "420px", maxWidth: "45%", borderLeft: "1px solid #e5e7eb", background: "#fff", overflow: "auto", padding: "16px" },
  detailHeader: { display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" },
  close: { border: "none", background: "none", fontSize: "1.25rem", cursor: "pointer", color: "#6b7280" },
  meta: { fontSize: "0.8125rem", color: "#4b5563", marginBottom: "12px" },
  blockTitle: { margin: "12px 0 4px", fontSize: "0.75rem", fontWeight: 600, color: "#6b7280", textTransform: "uppercase" },
  pre: { margin: 0, padding: "8px", background: "#f9fafb", borderRadius: "6px", whiteSpace: "pre-wrap", wordBreak: "break-word", fontSize: "0.8125rem", maxHeight: "260px", overflow: "auto" },
  list: { margin: 0, paddingLeft: "18px", fontSize: "0.8125rem", lineHeight: 1.7 },
};

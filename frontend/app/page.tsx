"use client";

import { useState, useRef, useEffect, useCallback, FormEvent } from "react";

interface Message {
  id: string;
  role: "user" | "assistant";
  content: string;
  isError?: boolean;
}

interface ToolCall {
  id: string;
  name: string;
  status: "running" | "completed";
}

interface StatePanel {
  snapshot: unknown;
  deltas: unknown[];
  timestamp: number;
}

interface StepInfo {
  id: string;
  name: string;
  status: "running" | "completed";
  startedAt: number;
}

interface ReasoningBlock {
  content: string;
}

interface InterruptInfo {
  id: string;
  reason: string;
  message?: string;
  toolCallId?: string;
  threadId: string;
  runId: string;
}

function useThreadId() {
  const [threadId] = useState(() => crypto.randomUUID());
  return threadId;
}

export default function Home() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [healthStatus, setHealthStatus] = useState<{
    status: string;
    message: string;
    details?: string;
  } | null>(null);
  const [activeTools, setActiveTools] = useState<ToolCall[]>([]);
  const [statePanel, setStatePanel] = useState<StatePanel | null>(null);
  const [steps, setSteps] = useState<StepInfo[]>([]);
  const [reasoning, setReasoning] = useState<ReasoningBlock | null>(null);
  const [interrupt, setInterrupt] = useState<InterruptInfo | null>(null);
  const [showReasoning, setShowReasoning] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const threadId = useThreadId();
  const currentRunId = useRef<string>("");

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  useEffect(() => {
    const eventSource = new EventSource("/api/notifications");
    eventSource.addEventListener("notification", (e) => {
      try {
        const data = JSON.parse(e.data);
        setMessages((prev) => [
          ...prev,
          {
            id: crypto.randomUUID(),
            role: "assistant",
            content: `**${data.title}**\n\n${data.body}`,
          },
        ]);
      } catch {
        // skip malformed events
      }
    });
    return () => eventSource.close();
  }, []);

  useEffect(() => {
    setHealthStatus({ status: "checking", message: "Running health checks..." });
    fetch("/api/health")
      .then((r) => r.json())
      .then((data) => {
        if (data.status === "Healthy") {
          const durationMs = Math.round(data.duration_ms);
          setHealthStatus({
            status: "healthy",
            message: `All systems operational (${durationMs}ms)`,
          });
          setTimeout(() => setHealthStatus(null), 5000);
        } else {
          const check = data.checks?.[0];
          const checkData = check?.data || {};
          const parts: string[] = [];
          if (checkData.ai_connectivity === "fail")
            parts.push("AI endpoint is not responding");
          if (checkData.agent_pipeline === "timeout")
            parts.push("Agent pipeline timed out");
          if (checkData.agent_pipeline === "fail")
            parts.push("Agent pipeline error: " + (checkData.agent_error || "unknown"));
          if (checkData.agent_pipeline === "no_output")
            parts.push("Agent pipeline returned no output");

          setHealthStatus({
            status: data.status === "Degraded" ? "degraded" : "unhealthy",
            message: check?.description || "System is not healthy",
            details: parts.join(". ") || check?.error || undefined,
          });
        }
      })
      .catch(() => {
        setHealthStatus({
          status: "unhealthy",
          message: "Cannot reach backend. Make sure the server is running.",
        });
      });
  }, []);

  const handleAGUIEvent = useCallback(
    (data: Record<string, unknown>, assistantId: string) => {
      const eventType = data.type as string;

      switch (eventType) {
        case "TEXT_MESSAGE_CONTENT": {
          const delta = data.delta as string;
          if (delta) {
            setMessages((prev) =>
              prev.map((m) =>
                m.id === assistantId
                  ? { ...m, content: m.content + delta }
                  : m
              )
            );
          }
          break;
        }

        case "TOOL_CALL_START": {
          const toolName = data.toolCallName as string;
          const toolCallId = data.toolCallId as string;
          if (toolName && toolCallId) {
            setActiveTools((prev) => [
              ...prev.filter((t) => t.id !== toolCallId),
              { id: toolCallId, name: toolName, status: "running" },
            ]);
          }
          break;
        }

        case "TOOL_CALL_END": {
          const toolCallId = data.toolCallId as string;
          if (toolCallId) {
            setActiveTools((prev) =>
              prev.map((t) =>
                t.id === toolCallId ? { ...t, status: "completed" as const } : t
              )
            );
            setTimeout(() => {
              setActiveTools((prev) => prev.filter((t) => t.id !== toolCallId));
            }, 1500);
          }
          break;
        }

        case "STATE_SNAPSHOT": {
          const snapshot = data.snapshot;
          if (snapshot !== undefined) {
            setStatePanel({ snapshot, deltas: [], timestamp: Date.now() });
          }
          break;
        }

        case "STATE_DELTA": {
          const delta = data.delta;
          if (delta !== undefined) {
            setStatePanel((prev) => {
              if (!prev) return { snapshot: null, deltas: [delta], timestamp: Date.now() };
              return { ...prev, deltas: [...prev.deltas, delta], timestamp: Date.now() };
            });
          }
          break;
        }

        case "STEP_STARTED": {
          const stepId = data.stepId as string;
          const stepName = data.stepName as string;
          if (stepId) {
            setSteps((prev) => [
              ...prev.filter((s) => s.id !== stepId),
              { id: stepId, name: stepName || stepId, status: "running", startedAt: Date.now() },
            ]);
          }
          break;
        }

        case "STEP_FINISHED": {
          const stepId = data.stepId as string;
          if (stepId) {
            setSteps((prev) =>
              prev.map((s) =>
                s.id === stepId ? { ...s, status: "completed" as const } : s
              )
            );
            setTimeout(() => {
              setSteps((prev) => prev.filter((s) => s.id !== stepId));
            }, 3000);
          }
          break;
        }

        case "REASONING_MESSAGE_CONTENT": {
          const delta = data.delta as string;
          if (delta) {
            setReasoning((prev) => ({
              content: (prev?.content || "") + delta,
            }));
          }
          break;
        }

        case "REASONING_START": {
          setReasoning({ content: "" });
          break;
        }

        case "REASONING_END": {
          setTimeout(() => setReasoning(null), 5000);
          break;
        }

        case "RUN_FINISHED": {
          const outcome = data.outcome as Record<string, unknown> | undefined;
          if (outcome?.type === "interrupt") {
            const interruptData = outcome.interrupt as Record<string, unknown>;
            if (interruptData) {
              setInterrupt({
                id: (interruptData.id as string) || crypto.randomUUID(),
                reason: (interruptData.reason as string) || "Action requires approval",
                message: interruptData.message as string | undefined,
                toolCallId: interruptData.toolCallId as string | undefined,
                threadId,
                runId: currentRunId.current,
              });
            }
          }
          break;
        }

        case "RUN_ERROR": {
          const message = (data.message as string) || "An error occurred during the run.";
          setMessages((prev) =>
            prev.map((m) =>
              m.id === assistantId
                ? { ...m, content: m.content || message, isError: !m.content }
                : m
            )
          );
          break;
        }
      }
    },
    [threadId]
  );

  async function sendRequest(
    allMessages: Message[],
    assistantId: string,
    resume?: Array<{ interruptId: string; response: unknown }>
  ) {
    const runId = crypto.randomUUID();
    currentRunId.current = runId;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 300000);

    try {
      const body: Record<string, unknown> = {
        threadId,
        runId,
        messages: allMessages.map((m) => ({
          id: m.id,
          role: m.role,
          content: m.content,
        })),
      };
      if (resume) {
        body.resume = resume;
      }

      const res = await fetch("/api/copilotkit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: controller.signal,
      });

      clearTimeout(timeout);

      if (!res.ok) {
        const errorBody = await res.json().catch(() => null);
        const errorMsg =
          errorBody?.error ||
          errorBody?.details ||
          `Server error (${res.status})`;
        throw new Error(errorMsg);
      }

      const reader = res.body?.getReader();
      const decoder = new TextDecoder();
      if (!reader) throw new Error("No response stream");

      let buffer = "";
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split("\n");
        buffer = lines.pop() || "";

        for (const line of lines) {
          if (line.startsWith("data: ")) {
            try {
              const data = JSON.parse(line.slice(6));
              if (data.type) {
                handleAGUIEvent(data, assistantId);
              } else if (data.delta) {
                setMessages((prev) =>
                  prev.map((m) =>
                    m.id === assistantId
                      ? { ...m, content: m.content + data.delta }
                      : m
                  )
                );
              }
            } catch {
              // skip non-JSON lines
            }
          }
        }
      }
    } catch (err) {
      clearTimeout(timeout);
      const message =
        err instanceof DOMException && err.name === "AbortError"
          ? "Request timed out after 5 minutes."
          : err instanceof Error
            ? err.message
            : "Something went wrong";

      setMessages((prev) =>
        prev.map((m) =>
          m.id === assistantId
            ? { ...m, content: message, isError: true }
            : m
        )
      );
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const text = input.trim();
    if (!text || isLoading) return;

    const userMessage: Message = {
      id: crypto.randomUUID(),
      role: "user",
      content: text,
    };

    const allMessages = [...messages, userMessage];
    setMessages(allMessages);
    setInput("");
    setIsLoading(true);
    setActiveTools([]);
    setSteps([]);
    setReasoning(null);

    const assistantId = crypto.randomUUID();
    setMessages((prev) => [
      ...prev,
      { id: assistantId, role: "assistant", content: "" },
    ]);

    await sendRequest(allMessages, assistantId);
    setIsLoading(false);
    setActiveTools([]);
  }

  async function handleInterruptResponse(approved: boolean) {
    if (!interrupt) return;

    setIsLoading(true);
    setInterrupt(null);

    const assistantId = crypto.randomUUID();
    setMessages((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        role: "user",
        content: approved ? "Approved" : "Denied",
      },
      { id: assistantId, role: "assistant", content: "" },
    ]);

    const resume = [
      {
        interruptId: interrupt.id,
        response: { approved },
      },
    ];

    await sendRequest(messages, assistantId, resume);
    setIsLoading(false);
    setActiveTools([]);
  }

  return (
    <div style={styles.container}>
      <style>{`
        @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.3; } }
        @keyframes spin { to { transform: rotate(360deg); } }
      `}</style>
      <header style={styles.header}>
        <h1 style={styles.title}>AI Agent Canvas</h1>
        <p style={styles.subtitle}>Multi-agent enterprise copilot</p>
      </header>

      {healthStatus && (
        <div
          style={{
            ...styles.banner,
            ...(healthStatus.status === "healthy"
              ? styles.bannerHealthy
              : healthStatus.status === "checking"
                ? styles.bannerChecking
                : styles.bannerUnhealthy),
          }}
        >
          <strong>
            {healthStatus.status === "healthy"
              ? "✓"
              : healthStatus.status === "checking"
                ? "⟳"
                : "✗"}
          </strong>{" "}
          {healthStatus.message}
          {healthStatus.details && (
            <span style={{ display: "block", marginTop: "4px", opacity: 0.85 }}>
              {healthStatus.details}
            </span>
          )}
        </div>
      )}

      <div style={styles.mainArea}>
        <div style={styles.chatColumn}>
          <div style={styles.chatArea}>
            {messages.length === 0 && (
              <div style={styles.empty}>
                <p style={styles.emptyTitle}>Welcome to AI Agent Canvas</p>
                <p style={styles.emptyText}>
                  Ask me anything -- I can analyze stocks, manage schedules, and
                  more.
                </p>
              </div>
            )}

            {messages.map((m) => (
              <div
                key={m.id}
                style={{
                  ...styles.messageBubble,
                  ...(m.role === "user"
                    ? styles.userBubble
                    : m.isError
                      ? styles.errorBubble
                      : styles.assistantBubble),
                }}
              >
                <div style={styles.roleLabel}>
                  {m.role === "user" ? "You" : "Agent"}
                </div>
                <div style={styles.messageContent}>
                  {m.content || (isLoading ? "Thinking..." : "")}
                </div>
              </div>
            ))}
            <div ref={messagesEndRef} />
          </div>

          {reasoning && (
            <div style={styles.reasoningBar}>
              <button
                onClick={() => setShowReasoning((v) => !v)}
                style={styles.reasoningToggle}
              >
                {showReasoning ? "▾" : "▸"} Reasoning
              </button>
              {showReasoning && reasoning.content && (
                <div style={styles.reasoningContent}>{reasoning.content}</div>
              )}
            </div>
          )}

          {steps.length > 0 && (
            <div style={styles.stepsBar}>
              {steps.map((step) => (
                <div key={step.id} style={styles.stepItem}>
                  <span
                    style={{
                      ...styles.stepIcon,
                      color: step.status === "running" ? "#2563eb" : "#16a34a",
                    }}
                  >
                    {step.status === "running" ? "⟳" : "✓"}
                  </span>
                  <span>{step.name}</span>
                </div>
              ))}
            </div>
          )}

          {activeTools.length > 0 && (
            <div style={styles.toolStatusBar}>
              {activeTools.map((tool) => (
                <div key={tool.id} style={styles.toolStatusItem}>
                  <span
                    style={{
                      ...styles.toolStatusDot,
                      background:
                        tool.status === "running" ? "#2563eb" : "#16a34a",
                      animation:
                        tool.status === "running" ? "pulse 1s infinite" : "none",
                    }}
                  />
                  <span>
                    {tool.status === "running"
                      ? `Calling ${tool.name}...`
                      : `${tool.name} completed`}
                  </span>
                </div>
              ))}
            </div>
          )}

          {interrupt && (
            <div style={styles.interruptBar}>
              <div style={styles.interruptContent}>
                <div style={styles.interruptTitle}>Action Requires Approval</div>
                <div style={styles.interruptReason}>{interrupt.reason}</div>
                {interrupt.message && (
                  <div style={styles.interruptMessage}>{interrupt.message}</div>
                )}
              </div>
              <div style={styles.interruptActions}>
                <button
                  onClick={() => handleInterruptResponse(true)}
                  style={styles.approveButton}
                >
                  Approve
                </button>
                <button
                  onClick={() => handleInterruptResponse(false)}
                  style={styles.denyButton}
                >
                  Deny
                </button>
              </div>
            </div>
          )}

          <form onSubmit={handleSubmit} style={styles.inputArea}>
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Type a message..."
              style={styles.input}
              disabled={isLoading || !!interrupt}
            />
            <button type="submit" style={styles.sendButton} disabled={isLoading || !!interrupt}>
              {isLoading ? "..." : "Send"}
            </button>
          </form>
        </div>

        {statePanel && (
          <div style={styles.statePanel}>
            <div style={styles.statePanelHeader}>
              <span style={styles.statePanelTitle}>Data</span>
              <button
                onClick={() => setStatePanel(null)}
                style={styles.statePanelClose}
                title="Close panel"
              >
                ×
              </button>
            </div>
            <pre style={styles.statePanelContent}>
              {statePanel.snapshot && JSON.stringify(statePanel.snapshot, null, 2)}
              {statePanel.deltas.length > 0 && (
                <>
                  {statePanel.snapshot && "\n\n--- Deltas ---\n\n"}
                  {statePanel.deltas.map((d, i) => JSON.stringify(d, null, 2) + (i < statePanel.deltas.length - 1 ? "\n\n" : "")).join("")}
                </>
              )}
            </pre>
          </div>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    height: "100vh",
    display: "flex",
    flexDirection: "column",
    background: "#f9fafb",
  },
  header: {
    padding: "16px 24px",
    borderBottom: "1px solid #e5e7eb",
    background: "#fff",
    flexShrink: 0,
  },
  title: {
    margin: 0,
    fontSize: "1.25rem",
    fontWeight: 600,
    color: "#111827",
  },
  subtitle: {
    margin: "4px 0 0",
    fontSize: "0.875rem",
    color: "#6b7280",
  },
  banner: {
    padding: "12px 24px",
    borderBottom: "1px solid",
    fontSize: "0.875rem",
    lineHeight: 1.5,
    flexShrink: 0,
  },
  bannerHealthy: {
    background: "#f0fdf4",
    color: "#166534",
    borderBottomColor: "#86efac",
  },
  bannerChecking: {
    background: "#f0f9ff",
    color: "#1e40af",
    borderBottomColor: "#93c5fd",
  },
  bannerUnhealthy: {
    background: "#fef2f2",
    color: "#991b1b",
    borderBottomColor: "#fecaca",
  },
  mainArea: {
    flex: 1,
    display: "flex",
    overflow: "hidden",
  },
  chatColumn: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    minWidth: 0,
  },
  chatArea: {
    flex: 1,
    overflow: "auto",
    padding: "24px",
    display: "flex",
    flexDirection: "column",
    gap: "16px",
  },
  empty: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    color: "#9ca3af",
  },
  emptyTitle: {
    fontSize: "1.125rem",
    fontWeight: 600,
    color: "#6b7280",
    margin: "0 0 8px",
  },
  emptyText: {
    fontSize: "0.875rem",
    margin: 0,
  },
  messageBubble: {
    maxWidth: "75%",
    padding: "12px 16px",
    borderRadius: "12px",
    fontSize: "0.9375rem",
    lineHeight: 1.5,
  },
  userBubble: {
    alignSelf: "flex-end",
    background: "#2563eb",
    color: "#fff",
  },
  assistantBubble: {
    alignSelf: "flex-start",
    background: "#fff",
    color: "#111827",
    border: "1px solid #e5e7eb",
  },
  errorBubble: {
    alignSelf: "flex-start",
    background: "#fef2f2",
    color: "#991b1b",
    border: "1px solid #fecaca",
  },
  roleLabel: {
    fontSize: "0.75rem",
    fontWeight: 600,
    marginBottom: "4px",
    opacity: 0.7,
  },
  messageContent: {
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
  },
  inputArea: {
    display: "flex",
    gap: "8px",
    padding: "16px 24px",
    borderTop: "1px solid #e5e7eb",
    background: "#fff",
    flexShrink: 0,
  },
  input: {
    flex: 1,
    padding: "12px 16px",
    borderRadius: "8px",
    border: "1px solid #d1d5db",
    fontSize: "0.9375rem",
    outline: "none",
    fontFamily: "inherit",
  },
  sendButton: {
    padding: "12px 24px",
    borderRadius: "8px",
    border: "none",
    background: "#2563eb",
    color: "#fff",
    fontWeight: 600,
    fontSize: "0.9375rem",
    cursor: "pointer",
    fontFamily: "inherit",
  },
  toolStatusBar: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    padding: "8px 24px",
    fontSize: "0.8125rem",
    color: "#6b7280",
    background: "#f9fafb",
    borderTop: "1px solid #e5e7eb",
    flexShrink: 0,
  },
  toolStatusItem: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  toolStatusDot: {
    width: "6px",
    height: "6px",
    borderRadius: "50%",
    flexShrink: 0,
  },
  statePanel: {
    width: "360px",
    borderLeft: "1px solid #e5e7eb",
    background: "#fff",
    display: "flex",
    flexDirection: "column",
    flexShrink: 0,
  },
  statePanelHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: "12px 16px",
    borderBottom: "1px solid #e5e7eb",
    flexShrink: 0,
  },
  statePanelTitle: {
    fontSize: "0.875rem",
    fontWeight: 600,
    color: "#111827",
  },
  statePanelClose: {
    background: "none",
    border: "none",
    fontSize: "1.25rem",
    cursor: "pointer",
    color: "#6b7280",
    padding: "0 4px",
    lineHeight: 1,
  },
  statePanelContent: {
    flex: 1,
    overflow: "auto",
    padding: "16px",
    margin: 0,
    fontSize: "0.8125rem",
    lineHeight: 1.6,
    color: "#374151",
    fontFamily: "'SF Mono', 'Cascadia Code', 'Fira Code', monospace",
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
  },
  reasoningBar: {
    padding: "8px 24px",
    fontSize: "0.8125rem",
    color: "#6b7280",
    background: "#fefce8",
    borderTop: "1px solid #fde68a",
    flexShrink: 0,
  },
  reasoningToggle: {
    background: "none",
    border: "none",
    cursor: "pointer",
    fontSize: "0.8125rem",
    color: "#92400e",
    fontWeight: 600,
    padding: 0,
    fontFamily: "inherit",
  },
  reasoningContent: {
    marginTop: "6px",
    padding: "8px 12px",
    background: "#fffbeb",
    borderRadius: "6px",
    color: "#78350f",
    lineHeight: 1.5,
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
    maxHeight: "200px",
    overflow: "auto",
  },
  stepsBar: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
    padding: "8px 24px",
    fontSize: "0.8125rem",
    color: "#6b7280",
    background: "#f0f9ff",
    borderTop: "1px solid #bfdbfe",
    flexShrink: 0,
  },
  stepItem: {
    display: "flex",
    alignItems: "center",
    gap: "6px",
  },
  stepIcon: {
    fontSize: "0.75rem",
    fontWeight: 700,
  },
  interruptBar: {
    padding: "16px 24px",
    background: "#fffbeb",
    borderTop: "2px solid #f59e0b",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: "16px",
  },
  interruptContent: {
    flex: 1,
    minWidth: 0,
  },
  interruptTitle: {
    fontSize: "0.875rem",
    fontWeight: 700,
    color: "#92400e",
    marginBottom: "4px",
  },
  interruptReason: {
    fontSize: "0.8125rem",
    color: "#78350f",
  },
  interruptMessage: {
    fontSize: "0.8125rem",
    color: "#92400e",
    marginTop: "4px",
    opacity: 0.85,
  },
  interruptActions: {
    display: "flex",
    gap: "8px",
    flexShrink: 0,
  },
  approveButton: {
    padding: "8px 20px",
    borderRadius: "6px",
    border: "none",
    background: "#16a34a",
    color: "#fff",
    fontWeight: 600,
    fontSize: "0.8125rem",
    cursor: "pointer",
    fontFamily: "inherit",
  },
  denyButton: {
    padding: "8px 20px",
    borderRadius: "6px",
    border: "1px solid #d1d5db",
    background: "#fff",
    color: "#374151",
    fontWeight: 600,
    fontSize: "0.8125rem",
    cursor: "pointer",
    fontFamily: "inherit",
  },
};

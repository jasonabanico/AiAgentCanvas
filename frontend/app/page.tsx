"use client";

import { useState, useRef, useEffect, FormEvent } from "react";

interface Message {
  id: string;
  role: "user" | "assistant";
  content: string;
  isError?: boolean;
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
  const [toolStatus, setToolStatus] = useState<string | null>(null);
  const toolStatusTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  useEffect(() => {
    const eventSource = new EventSource("/api/notifications");
    eventSource.onopen = () => console.log("[SSE] Connected to /api/notifications");
    eventSource.onerror = (e) => console.error("[SSE] Error:", e);
    eventSource.addEventListener("notification", (e) => {
      console.log("[SSE] Received notification:", e.data);
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
            parts.push("Azure AI endpoint is not responding");
          if (checkData.agent_pipeline === "timeout")
            parts.push("Agent pipeline timed out — too many tools or slow model");
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

    const assistantId = crypto.randomUUID();
    setMessages((prev) => [
      ...prev,
      { id: assistantId, role: "assistant", content: "" },
    ]);

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 300000);

    try {
      const res = await fetch("/api/copilotkit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          threadId: "default",
          messages: allMessages.map((m) => ({
            role: m.role,
            content: m.content,
          })),
        }),
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
      let currentEvent = "";
      console.log("[SSE] Reader started, reading stream...");
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        console.log("[SSE] raw chunk:", JSON.stringify(chunk.slice(0, 200)));
        buffer += chunk;
        const lines = buffer.split("\n");
        buffer = lines.pop() || "";

        for (const line of lines) {
          if (line.startsWith("event: ")) {
            currentEvent = line.slice(7).trim();
            console.log("[SSE] event type:", currentEvent);
          } else if (line.startsWith("data: ")) {
            try {
              const data = JSON.parse(line.slice(6));
              if (currentEvent === "tool.status") {
                if (toolStatusTimerRef.current) clearTimeout(toolStatusTimerRef.current);
                if (!data.isComplete && data.toolName) {
                  setToolStatus(`Calling ${data.toolName}...`);
                } else if (data.isComplete && data.toolName) {
                  setToolStatus(`${data.toolName} completed`);
                }
                toolStatusTimerRef.current = setTimeout(() => setToolStatus(null), 2000);
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
          } else if (line === "") {
            currentEvent = "";
          }
        }
      }
    } catch (err) {
      clearTimeout(timeout);
      const message =
        err instanceof DOMException && err.name === "AbortError"
          ? "Request timed out after 5 minutes. The AI service may be slow or not configured correctly."
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
    } finally {
      setIsLoading(false);
      setToolStatus(null);
    }
  }

  return (
    <div style={styles.container}>
      <style>{`@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.3; } }`}</style>
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

      <div style={styles.chatArea}>
        {messages.length === 0 && (
          <div style={styles.empty}>
            <p style={styles.emptyTitle}>Welcome to AI Agent Canvas</p>
            <p style={styles.emptyText}>
              Ask me anything — I can analyze stocks, manage schedules, and
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

      {toolStatus && (
        <div style={styles.toolStatusBar}>
          <span style={styles.toolStatusDot} />
          {toolStatus}
        </div>
      )}

      <form onSubmit={handleSubmit} style={styles.inputArea}>
        <input
          type="text"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Type a message..."
          style={styles.input}
          disabled={isLoading}
        />
        <button type="submit" style={styles.sendButton} disabled={isLoading}>
          {isLoading ? "..." : "Send"}
        </button>
      </form>
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
    alignItems: "center",
    gap: "8px",
    padding: "8px 24px",
    fontSize: "0.8125rem",
    color: "#6b7280",
    background: "#f9fafb",
    borderTop: "1px solid #e5e7eb",
  },
  toolStatusDot: {
    width: "6px",
    height: "6px",
    borderRadius: "50%",
    background: "#2563eb",
    animation: "pulse 1s infinite",
  },
};

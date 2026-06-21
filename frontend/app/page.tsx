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
  const [configError, setConfigError] = useState<string | null>(null);
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
    fetch("/api/health")
      .then((r) => r.json())
      .then((data) => {
        if (!data.ai) {
          setConfigError(
            data.message ||
              "AI service is not configured. Update appsettings.json with valid Azure AI Foundry credentials."
          );
        }
      })
      .catch(() => {
        setConfigError(
          "Cannot reach backend. Make sure the server is running."
        );
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
    const timeout = setTimeout(() => controller.abort(), 30000);

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
              if (data.delta) {
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
          ? "Request timed out after 30 seconds. The AI service may not be configured correctly."
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
    }
  }

  return (
    <div style={styles.container}>
      <header style={styles.header}>
        <h1 style={styles.title}>AI Agent Canvas</h1>
        <p style={styles.subtitle}>Multi-agent enterprise copilot</p>
      </header>

      {configError && (
        <div style={styles.banner}>
          <strong>Configuration required:</strong> {configError}
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
    background: "#fef3c7",
    color: "#92400e",
    borderBottom: "1px solid #fcd34d",
    fontSize: "0.875rem",
    lineHeight: 1.5,
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
};

import { useEffect, useRef, useState } from "react";
import { Hash, Send, Wifi, WifiOff } from "lucide-react";
import { createChatConnection, chatConnectionState } from "../api/chatConnection.js";
import {
  channels as channelsApi,
  formatApiError,
  messages as messagesApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapChannel, mapMessage, mapWorkspace, selectPrimaryWorkspace } from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";

const connectionLabels = {
  connecting: "Connecting",
  connected: "Live",
  reconnecting: "Reconnecting",
  offline: "Offline",
  error: "Realtime unavailable",
};

function normalizeRealtimeMessage(message) {
  return {
    id: message?.id ?? message?.Id,
    channelId: message?.channelId ?? message?.ChannelId,
    senderId: message?.senderId ?? message?.SenderId,
    senderName: message?.senderName ?? message?.SenderName,
    content: message?.content ?? message?.Content,
    isDeleted: message?.isDeleted ?? message?.IsDeleted,
    createdAtUtc: message?.createdAtUtc ?? message?.CreatedAtUtc,
    editedAtUtc: message?.editedAtUtc ?? message?.EditedAtUtc,
  };
}

function getPayloadChannelId(payload) {
  return payload?.channelId ?? payload?.ChannelId;
}

function mergeMessage(currentMessages, incomingMessage) {
  const mappedMessage = mapMessage(normalizeRealtimeMessage(incomingMessage));
  const existingIndex = currentMessages.findIndex((message) => message.id === mappedMessage.id);

  if (existingIndex === -1) {
    return [...currentMessages, mappedMessage];
  }

  return currentMessages.map((message, index) => (index === existingIndex ? mappedMessage : message));
}

function formatRealtimeError(error) {
  return error?.message || "Realtime chat request failed.";
}

export default function Channels() {
  const { t } = useI18n();
  const [workspaceName, setWorkspaceName] = useState("");
  const [channels, setChannels] = useState([]);
  const [activeChannelId, setActiveChannelId] = useState("");
  const [messages, setMessages] = useState([]);
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [messageLoading, setMessageLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [chatError, setChatError] = useState("");
  const [connection, setConnection] = useState(null);
  const [connectionStatus, setConnectionStatus] = useState("connecting");
  const [typingUserId, setTypingUserId] = useState("");
  const activeChannelRef = useRef("");
  const messageEndRef = useRef(null);
  const typingClearTimerRef = useRef(null);
  const typingStopTimerRef = useRef(null);
  const lastTypingSentRef = useRef(0);

  useEffect(() => {
    activeChannelRef.current = activeChannelId;
  }, [activeChannelId]);

  useEffect(() => {
    let active = true;

    async function loadChannels() {
      setLoading(true);
      setLoadError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list()).map(mapWorkspace);
        const workspace = selectPrimaryWorkspace(workspaceItems);

        if (!workspace) {
          if (active) {
            setWorkspaceName("");
            setChannels([]);
            setActiveChannelId("");
          }
          return;
        }

        const channelItems = asArray(await channelsApi.listByWorkspace(workspace.id)).map(mapChannel);

        if (active) {
          setWorkspaceName(workspace.name);
          setChannels(channelItems);
          setActiveChannelId(channelItems[0]?.id || "");
        }
      } catch (apiError) {
        if (active) setLoadError(formatApiError(apiError));
      } finally {
        if (active) setLoading(false);
      }
    }

    loadChannels();

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    let active = true;

    async function loadMessages() {
      if (!activeChannelId) {
        setMessages([]);
        return;
      }

      setMessageLoading(true);
      setChatError("");

      try {
        const messageItems = asArray(await messagesApi.listByChannel(activeChannelId)).map(mapMessage);
        if (active) setMessages(messageItems);
      } catch (apiError) {
        if (active) setChatError(formatApiError(apiError));
      } finally {
        if (active) setMessageLoading(false);
      }
    }

    loadMessages();

    return () => {
      active = false;
    };
  }, [activeChannelId]);

  useEffect(() => {
    let cancelled = false;
    const chatConnection = createChatConnection();

    const handleMessageReceived = (message) => {
      const channelId = getPayloadChannelId(message);
      if (channelId !== activeChannelRef.current) return;

      setMessages((currentMessages) => mergeMessage(currentMessages, message));
    };

    const handleUserTyping = (payload) => {
      const channelId = getPayloadChannelId(payload);
      if (channelId !== activeChannelRef.current) return;

      setTypingUserId(payload?.userId ?? payload?.UserId ?? "teammate");
      clearTimeout(typingClearTimerRef.current);
      typingClearTimerRef.current = setTimeout(() => setTypingUserId(""), 2400);
    };

    const handleUserStoppedTyping = (payload) => {
      const channelId = getPayloadChannelId(payload);
      if (channelId === activeChannelRef.current) setTypingUserId("");
    };

    chatConnection.on("MessageReceived", handleMessageReceived);
    chatConnection.on("UserTyping", handleUserTyping);
    chatConnection.on("UserStoppedTyping", handleUserStoppedTyping);
    chatConnection.onreconnecting(() => {
      if (!cancelled) setConnectionStatus("reconnecting");
    });
    chatConnection.onreconnected(async () => {
      if (cancelled) return;
      setConnectionStatus("connected");

      if (activeChannelRef.current) {
        try {
          await chatConnection.invoke("JoinChannel", activeChannelRef.current);
        } catch (hubError) {
          setChatError(formatRealtimeError(hubError));
        }
      }
    });
    chatConnection.onclose(() => {
      if (!cancelled) setConnectionStatus("offline");
    });

    async function startConnection() {
      setConnectionStatus("connecting");

      try {
        await chatConnection.start();
        if (cancelled) return;

        setConnection(chatConnection);
        setConnectionStatus("connected");
      } catch (hubError) {
        if (cancelled) return;
        setConnectionStatus("error");
        setChatError(formatRealtimeError(hubError));
      }
    }

    startConnection();

    return () => {
      cancelled = true;
      clearTimeout(typingClearTimerRef.current);
      clearTimeout(typingStopTimerRef.current);
      chatConnection.off("MessageReceived", handleMessageReceived);
      chatConnection.off("UserTyping", handleUserTyping);
      chatConnection.off("UserStoppedTyping", handleUserStoppedTyping);
      chatConnection.stop().catch(() => {});
    };
  }, []);

  useEffect(() => {
    if (!connection || !activeChannelId || connection.state !== chatConnectionState.connected) return undefined;

    let leaving = false;

    async function joinChannel() {
      setTypingUserId("");
      setChatError("");

      try {
        await connection.invoke("JoinChannel", activeChannelId);
      } catch (hubError) {
        if (!leaving) setChatError(formatRealtimeError(hubError));
      }
    }

    joinChannel();

    return () => {
      leaving = true;
      if (connection.state === chatConnectionState.connected) {
        connection.invoke("LeaveChannel", activeChannelId).catch(() => {});
      }
    };
  }, [activeChannelId, connection]);

  useEffect(() => {
    messageEndRef.current?.scrollIntoView({ block: "end" });
  }, [messages, activeChannelId]);

  function notifyTyping(nextValue) {
    if (!connection || !activeChannelId || connection.state !== chatConnectionState.connected) return;

    clearTimeout(typingStopTimerRef.current);

    if (!nextValue.trim()) {
      connection.invoke("StopTyping", activeChannelId).catch(() => {});
      return;
    }

    const now = Date.now();
    if (now - lastTypingSentRef.current > 1800) {
      lastTypingSentRef.current = now;
      connection.invoke("Typing", activeChannelId).catch(() => {});
    }

    typingStopTimerRef.current = setTimeout(() => {
      if (connection.state === chatConnectionState.connected) {
        connection.invoke("StopTyping", activeChannelId).catch(() => {});
      }
    }, 1200);
  }

  function handleDraftChange(event) {
    const nextValue = event.target.value;
    setDraft(nextValue);
    notifyTyping(nextValue);
  }

  async function sendMessage(event) {
    event.preventDefault();
    const content = draft.trim();
    if (!content || !activeChannelId) return;

    if (!connection || connection.state !== chatConnectionState.connected) {
      setChatError(t("channel.readyError"));
      return;
    }

    setChatError("");

    try {
      await connection.invoke("SendMessage", activeChannelId, content);
      setDraft("");
      clearTimeout(typingStopTimerRef.current);
      await connection.invoke("StopTyping", activeChannelId);
    } catch (hubError) {
      setChatError(formatRealtimeError(hubError));
    }
  }

  const activeChannel = channels.find((channel) => channel.id === activeChannelId);
  const connectionLabel = t(`channel.${connectionStatus}`);
  const isConnected = connectionStatus === "connected";
  const canSend = Boolean(activeChannel && draft.trim() && isConnected);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">{t("channel.eyebrow")}</p>
          <h1>{t("channel.title")}</h1>
        </div>
      </div>

      {loading && <section className="panel">{t("channel.loading")}</section>}
      {loadError && <section className="panel"><strong>{t("channel.unableLoad")}</strong><p>{loadError}</p></section>}
      {!loading && !loadError && !workspaceName && (
        <section className="panel">{t("channel.emptyWorkspace")}</section>
      )}

      {!loading && !loadError && workspaceName && (
        <section className="chat-layout">
          <aside className="panel channel-list" aria-label={t("channel.workspaceList")}>
            {channels.length === 0 && (
              <div className="chat-empty compact">
                <strong>{t("channel.empty")}</strong>
                <p>{t("channel.emptyHelp")}</p>
              </div>
            )}
            {channels.map((channel) => (
              <button
                key={channel.id}
                className={channel.id === activeChannelId ? "active" : ""}
                onClick={() => setActiveChannelId(channel.id)}
                type="button"
              >
                <span className="channel-button-text"><Hash size={16} /> {channel.name}</span>
                <span className="channel-meta">{channel.memberCount}</span>
              </button>
            ))}
          </aside>

          <article className="panel chat-panel">
            <div className="panel-header chat-header">
              <div>
                <h3>{activeChannel ? `# ${activeChannel.name}` : t("channel.noneSelected")}</h3>
                <span>{activeChannel ? t("channel.members", { count: activeChannel.memberCount }) : workspaceName}</span>
              </div>
              <span className={`connection-pill ${connectionStatus}`} role="status" aria-live="polite">
                {isConnected ? <Wifi size={14} /> : <WifiOff size={14} />} {connectionLabel}
              </span>
            </div>

            {chatError && <div className="chat-alert" role="alert">{chatError}</div>}

            <div className="message-list" aria-label={t("channel.messages")} aria-live="polite" aria-busy={messageLoading}>
              {messageLoading && (
                <div className="chat-loading">
                  <span />
                  <span />
                  <span />
                </div>
              )}
              {!messageLoading && activeChannel && messages.length === 0 && (
                <div className="chat-empty">
                  <strong>{t("channel.noMessages")}</strong>
                  <p>{t("channel.noMessagesHelp")}</p>
                </div>
              )}
              {!messageLoading && messages.map((message, index) => (
                <div className="message-row" key={message.id}>
                  <div className="activity-index message-index">{String(index + 1).padStart(2, "0")}</div>
                  <div className="message-body">
                    <strong>{message.sender} <span className="message-time">{message.time}</span></strong>
                    <p>{message.text}</p>
                  </div>
                </div>
              ))}
              <div ref={messageEndRef} />
            </div>

            <div className="typing-indicator" aria-live="polite">
              {typingUserId ? t("channel.typing") : ""}
            </div>

            <form className="message-input" onSubmit={sendMessage}>
              <label className="sr-only" htmlFor="channel-message">{t("channel.message")}</label>
              <input
                id="channel-message"
                placeholder={activeChannel ? t("channel.messagePlaceholder", { name: activeChannel.name }) : t("channel.selectChannel")}
                value={draft}
                onChange={handleDraftChange}
                disabled={!activeChannel || !isConnected}
                aria-describedby="chat-helper"
              />
              <button disabled={!canSend} aria-label={t("channel.send")} type="submit"><Send size={18} /></button>
            </form>
            <p className="chat-helper" id="chat-helper">
              {isConnected ? t("channel.liveHelper") : t("channel.waitingHelper")}
            </p>
          </article>
        </section>
      )}
    </div>
  );
}

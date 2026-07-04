import { useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  AtSign,
  Bot,
  Code2,
  FileText,
  Hash,
  Image,
  ListTodo,
  Paperclip,
  Send,
  Sparkles,
  Wifi,
  WifiOff,
  X,
} from "lucide-react";
import { createChatConnection, chatConnectionState } from "../api/chatConnection.js";
import Avatar from "../components/Avatar.jsx";
import {
  ai as aiApi,
  channels as channelsApi,
  formatApiError,
  messages as messagesApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import {
  asArray,
  formatDateTime,
  mapChannel,
  mapMessage,
  mapWorkspace,
  selectPrimaryWorkspace,
} from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";

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

function normalizeAttachment(attachment) {
  return {
    ...attachment,
    id: attachment.id,
    fileName: attachment.fileName || "attachment",
    contentType: attachment.contentType || "application/octet-stream",
    sizeBytes: Number(attachment.sizeBytes || 0),
    summary: attachment.summary || "",
    time: formatDateTime(attachment.createdAtUtc),
  };
}

function formatFileSize(bytes) {
  if (!bytes) return "0 KB";
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function isAiMention(content) {
  return content.includes("@TaskFlow AI") || content.toLowerCase().includes("@taskflow ai");
}

function getMessageTimestamp(message) {
  const date = new Date(message?.createdAtUtc || 0);
  return Number.isNaN(date.getTime()) ? 0 : date.getTime();
}

export default function Channels() {
  const { t } = useI18n();
  const [searchParams] = useSearchParams();
  const selectedChannelId = searchParams.get("channelId") || "";
  const [workspaceName, setWorkspaceName] = useState("");
  const [channels, setChannels] = useState([]);
  const [activeChannelId, setActiveChannelId] = useState("");
  const [messages, setMessages] = useState([]);
  const [aiMessages, setAiMessages] = useState([]);
  const [attachments, setAttachments] = useState([]);
  const [selectedAttachmentId, setSelectedAttachmentId] = useState("");
  const [selectedFile, setSelectedFile] = useState(null);
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [messageLoading, setMessageLoading] = useState(false);
  const [attachmentLoading, setAttachmentLoading] = useState(false);
  const [uploadingAttachment, setUploadingAttachment] = useState(false);
  const [aiLoading, setAiLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [chatError, setChatError] = useState("");
  const [aiError, setAiError] = useState("");
  const [connection, setConnection] = useState(null);
  const [connectionStatus, setConnectionStatus] = useState("connecting");
  const [typingUserId, setTypingUserId] = useState("");
  const activeChannelRef = useRef("");
  const messageEndRef = useRef(null);
  const fileInputRef = useRef(null);
  const typingClearTimerRef = useRef(null);
  const typingStopTimerRef = useRef(null);
  const lastTypingSentRef = useRef(0);

  const quickActions = [
    { key: "summary", label: t("channel.quickSummary"), command: t("channel.commandSummary"), icon: Sparkles },
    { key: "tasks", label: t("channel.quickTasks"), command: t("channel.commandTasks"), icon: ListTodo },
    { key: "report", label: t("channel.quickReport"), command: t("channel.commandReport"), icon: FileText },
    { key: "ppt", label: t("channel.quickPpt"), command: t("channel.commandPpt"), icon: FileText },
    { key: "code", label: t("channel.quickCode"), command: t("channel.commandCode"), icon: Code2 },
  ];

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
          setActiveChannelId(
            channelItems.some((channel) => channel.id === selectedChannelId)
              ? selectedChannelId
              : channelItems[0]?.id || ""
          );
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
  }, [selectedChannelId]);

  useEffect(() => {
    if (!selectedChannelId || channels.length === 0) return;
    if (channels.some((channel) => channel.id === selectedChannelId)) {
      setActiveChannelId(selectedChannelId);
    }
  }, [selectedChannelId, channels]);

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
    let active = true;

    async function loadAttachments() {
      if (!activeChannelId) {
        setAttachments([]);
        setSelectedAttachmentId("");
        return;
      }

      setAttachmentLoading(true);
      setAiError("");

      try {
        const attachmentItems = asArray(await aiApi.channelAttachments(activeChannelId)).map(normalizeAttachment);
        if (active) {
          setAttachments(attachmentItems);
          setSelectedAttachmentId((current) =>
            attachmentItems.some((attachment) => attachment.id === current) ? current : attachmentItems[0]?.id || ""
          );
        }
      } catch (apiError) {
        if (active) setAiError(formatApiError(apiError));
      } finally {
        if (active) setAttachmentLoading(false);
      }
    }

    loadAttachments();

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
  }, [messages, aiMessages, activeChannelId]);

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

  function insertMention() {
    setDraft((current) => {
      if (isAiMention(current)) return current;
      return `${current.trim() ? `${current.trim()} ` : ""}@TaskFlow AI `;
    });
  }

  function handleComposerKeyDown(event) {
    if (event.key !== "Enter" || event.shiftKey || event.nativeEvent?.isComposing) return;

    event.preventDefault();
    if (canSubmit) event.currentTarget.form?.requestSubmit();
  }

  function handleFileChange(event) {
    const file = event.target.files?.[0] || null;
    setSelectedFile(file);
    setAiError("");
  }

  function clearSelectedFile() {
    setSelectedFile(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function uploadSelectedAttachment() {
    if (!selectedFile || !activeChannelId) return null;

    setUploadingAttachment(true);
    setAiError("");

    try {
      const formData = new FormData();
      formData.append("file", selectedFile);
      const attachment = normalizeAttachment(await aiApi.uploadChannelAttachment(activeChannelId, formData));
      setAttachments((current) => [attachment, ...current.filter((item) => item.id !== attachment.id)]);
      setSelectedAttachmentId(attachment.id);
      clearSelectedFile();
      return attachment;
    } catch (apiError) {
      setAiError(formatApiError(apiError));
      throw apiError;
    } finally {
      setUploadingAttachment(false);
    }
  }

  async function runAiCommand(command, attachmentId = "") {
    if (!activeChannelId || !command.trim()) return null;

    setAiLoading(true);
    setAiError("");

    try {
      const response = await aiApi.channelCommand(activeChannelId, {
        command: command.trim(),
        attachmentId: attachmentId || null,
      });
      const createdAtUtc = response?.createdAtUtc || new Date().toISOString();
      const aiMessage = {
        id: `ai-${activeChannelId}-${Date.now()}`,
        channelId: activeChannelId,
        sender: "TaskFlow AI",
        text: response?.result || t("channel.aiEmpty"),
        createdAtUtc,
        time: formatDateTime(createdAtUtc),
        isAi: true,
        artifactType: response?.artifactType || "answer",
        sources: response?.sources || [],
        suggestedTasks: response?.suggestedTasks || [],
        createdTaskTitle: response?.createdTaskTitle || "",
      };
      setAiMessages((current) => [...current, aiMessage]);
      return response;
    } catch (apiError) {
      setAiError(formatApiError(apiError));
      return null;
    } finally {
      setAiLoading(false);
    }
  }

  async function sendMessage(event) {
    event.preventDefault();
    const content = draft.trim();
    if ((!content && !selectedFile) || !activeChannelId) return;

    setChatError("");
    setAiError("");

    let uploadedAttachment = null;
    try {
      if (selectedFile) {
        uploadedAttachment = await uploadSelectedAttachment();
      }

      if (content) {
        if (!connection || connection.state !== chatConnectionState.connected) {
          setChatError(t("channel.readyError"));
          return;
        }

        await connection.invoke("SendMessage", activeChannelId, content);
        setDraft("");
        clearTimeout(typingStopTimerRef.current);
        await connection.invoke("StopTyping", activeChannelId);
      }

      if (isAiMention(content) || (!content && uploadedAttachment)) {
        const command = content || t("channel.commandAttachment");
        await runAiCommand(command, uploadedAttachment?.id || selectedAttachmentId);
      }
    } catch (error) {
      if (error?.errors || error?.statusCode) {
        setAiError(formatApiError(error));
      } else {
        setChatError(formatRealtimeError(error));
      }
    }
  }

  const activeChannel = channels.find((channel) => channel.id === activeChannelId);
  const selectedAttachment = attachments.find((attachment) => attachment.id === selectedAttachmentId);
  const connectionLabel = t(`channel.${connectionStatus}`);
  const isConnected = connectionStatus === "connected";
  const combinedMessages = [
    ...messages,
    ...aiMessages.filter((message) => message.channelId === activeChannelId),
  ].sort((left, right) => getMessageTimestamp(left) - getMessageTimestamp(right));
  const canSubmit = Boolean(
    activeChannel
      && (draft.trim() || selectedFile)
      && !uploadingAttachment
      && !aiLoading
      && (isConnected || !draft.trim())
  );

  return (
    <div className="page-stack">
      {loading && <section className="panel">{t("channel.loading")}</section>}
      {loadError && <section className="panel"><strong>{t("channel.unableLoad")}</strong><p>{loadError}</p></section>}
      {!loading && !loadError && !workspaceName && (
        <section className="panel">{t("channel.emptyWorkspace")}</section>
      )}

      {!loading && !loadError && workspaceName && (
        <section className="channel-workspace">
          <aside className="panel channel-list" aria-label={t("channel.workspaceList")}>
            <div className="channel-list-header">
              <span>{workspaceName}</span>
              <strong>{channels.length}</strong>
            </div>
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
              <div className="chat-header-actions">
                <span className="ai-member-pill"><Bot size={14} /> {t("channel.aiMember")}</span>
                <span className={`connection-pill ${connectionStatus}`} role="status" aria-live="polite">
                  {isConnected ? <Wifi size={14} /> : <WifiOff size={14} />} {connectionLabel}
                </span>
              </div>
            </div>

            {chatError && <div className="chat-alert" role="alert">{chatError}</div>}
            {aiError && <div className="chat-alert ai-error" role="alert">{aiError}</div>}

            <div className="message-list" aria-label={t("channel.messages")} aria-live="polite" aria-busy={messageLoading || aiLoading}>
              {messageLoading && (
                <div className="chat-loading">
                  <span />
                  <span />
                  <span />
                </div>
              )}
              {!messageLoading && activeChannel && combinedMessages.length === 0 && (
                <div className="chat-empty">
                  <strong>{t("channel.noMessages")}</strong>
                  <p>{t("channel.noMessagesHelp")}</p>
                </div>
              )}
              {!messageLoading && combinedMessages.map((message) => (
                <div className={`message-row ${message.isAi ? "ai-message-row" : ""}`} key={message.id}>
                  {message.isAi ? (
                    <div className="message-avatar" aria-hidden="true"><Bot size={17} /></div>
                  ) : (
                    <Avatar className="message-avatar" seed={message.senderId || message.sender} name={message.sender} ariaHidden />
                  )}
                  <div className="message-body">
                    <strong>{message.sender} <span className="message-time">{message.time}</span></strong>
                    <p>{message.text}</p>
                    {message.isAi && message.sources.length > 0 && (
                      <div className="message-sources">
                        <span>{t("channel.sources")}</span>
                        {message.sources.slice(0, 4).map((source) => <small key={source}>{source}</small>)}
                      </div>
                    )}
                    {message.isAi && message.createdTaskTitle && (
                      <div className="created-task-chip">
                        <ListTodo size={14} /> {t("channel.createdTask", { title: message.createdTaskTitle })}
                      </div>
                    )}
                  </div>
                </div>
              ))}
              {aiLoading && (
                <div className="message-row ai-message-row">
                  <div className="message-avatar" aria-hidden="true"><Bot size={17} /></div>
                  <div className="message-body">
                    <strong>TaskFlow AI</strong>
                    <p>{t("channel.aiThinking")}</p>
                  </div>
                </div>
              )}
              <div ref={messageEndRef} />
            </div>

            <div className="typing-indicator" aria-live="polite">
              {typingUserId ? t("channel.typing") : ""}
            </div>

            <form className="message-composer" onSubmit={sendMessage}>
              <div className="composer-actions" role="toolbar" aria-label={t("channel.composeActions")}>
                <button type="button" onClick={insertMention} disabled={!activeChannel} aria-label={t("channel.mentionAi")} title={t("channel.mentionAi")}>
                  <AtSign size={16} />
                  <span>{t("channel.mentionAi")}</span>
                </button>
                <button type="button" onClick={() => fileInputRef.current?.click()} disabled={!activeChannel || uploadingAttachment} aria-label={t("channel.attach")} title={t("channel.attach")}>
                  <Paperclip size={16} />
                  <span>{t("channel.attach")}</span>
                </button>
                <input ref={fileInputRef} className="sr-only" type="file" onChange={handleFileChange} />
              </div>
              <label className="sr-only" htmlFor="channel-message">{t("channel.message")}</label>
              <textarea
                id="channel-message"
                placeholder={activeChannel ? t("channel.messagePlaceholder", { name: activeChannel.name }) : t("channel.selectChannel")}
                value={draft}
                onChange={handleDraftChange}
                onKeyDown={handleComposerKeyDown}
                disabled={!activeChannel}
                aria-describedby="chat-helper"
                rows={2}
              />
              {selectedFile && (
                <div className="selected-file-chip">
                  <FileText size={16} />
                  <span>{selectedFile.name} · {formatFileSize(selectedFile.size)}</span>
                  <button type="button" onClick={clearSelectedFile} aria-label={t("channel.clearAttachment")}><X size={14} /></button>
                </div>
              )}
              <div className="composer-footer">
                <p className="chat-helper" id="chat-helper">
                  {isConnected ? t("channel.enterHelper") : t("channel.waitingHelper")}
                </p>
                <button
                  disabled={!canSubmit}
                  className="composer-send"
                  type="submit"
                  aria-label={uploadingAttachment ? t("channel.uploading") : t("channel.send")}
                  title={uploadingAttachment ? t("channel.uploading") : t("channel.send")}
                >
                  <Send size={17} />
                  <span className="sr-only">{uploadingAttachment ? t("channel.uploading") : t("channel.send")}</span>
                </button>
              </div>
            </form>
          </article>

          <aside className="panel channel-inspector" aria-label={t("channel.aiPanel")}>
            <div className="ai-member-card">
              <div className="ai-member-icon"><Bot size={19} /></div>
              <div>
                <strong>{t("channel.aiMember")}</strong>
                <p>{t("channel.aiMemberHelp")}</p>
              </div>
            </div>

            <div className="quick-action-grid">
              {quickActions.map((action) => {
                const Icon = action.icon;
                return (
                  <button
                    key={action.key}
                    type="button"
                    onClick={() => runAiCommand(action.command, selectedAttachmentId)}
                    disabled={!activeChannel || aiLoading}
                  >
                    <Icon size={16} />
                    <span>{action.label}</span>
                  </button>
                );
              })}
            </div>

            <div className="inspector-section">
              <div className="inspector-heading">
                <strong>{t("channel.attachments")}</strong>
                <span>{attachments.length}</span>
              </div>
              {attachmentLoading && <p className="muted-small">{t("channel.loadingAttachments")}</p>}
              {!attachmentLoading && attachments.length === 0 && <p className="muted-small">{t("channel.noAttachments")}</p>}
              <div className="attachment-list">
                {attachments.map((attachment) => {
                  const isImage = attachment.contentType.startsWith("image/");
                  return (
                    <button
                      key={attachment.id}
                      type="button"
                      className={attachment.id === selectedAttachmentId ? "active" : ""}
                      onClick={() => setSelectedAttachmentId(attachment.id)}
                    >
                      {isImage ? <Image size={16} /> : <FileText size={16} />}
                      <span>
                        <strong>{attachment.fileName}</strong>
                        <small>{formatFileSize(attachment.sizeBytes)} · {attachment.time}</small>
                      </span>
                    </button>
                  );
                })}
              </div>
            </div>

            <div className="inspector-section selected-context">
              <div className="inspector-heading">
                <strong>{t("channel.selectedContext")}</strong>
              </div>
              {selectedAttachment ? (
                <div className="attachment-summary-card">
                  <strong>{selectedAttachment.fileName}</strong>
                  <p>{selectedAttachment.summary || t("channel.noAttachmentSummary")}</p>
                </div>
              ) : (
                <p className="muted-small">{t("channel.noSelectedContext")}</p>
              )}
            </div>
          </aside>
        </section>
      )}
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import { motion } from "motion/react";
import { useSearchParams } from "react-router-dom";
import {
  AtSign,
  Bot,
  Code2,
  Download,
  FileText,
  Hash,
  Image,
  ListTodo,
  Paperclip,
  Plus,
  Send,
  Sparkles,
  Trash2,
  UserPlus,
  Wifi,
  WifiOff,
  X,
} from "lucide-react";
import { createChatConnection, chatConnectionState } from "../api/chatConnection.js";
import Avatar from "../components/Avatar.jsx";
import CreateActionButton from "../components/CreateActionButton.jsx";
import LinearModal from "../components/LinearModal.jsx";
import MarkdownContent from "../components/MarkdownContent.jsx";
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
  mapAttachment,
  mapChannel,
  mapChannelMember,
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
    attachments: message?.attachments ?? message?.Attachments ?? [],
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
  return mapAttachment(attachment);
}

function formatFileSize(bytes) {
  if (!bytes) return "0 KB";
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function isAiMention(content) {
  return content.includes("@TaskFlow AI") || content.toLowerCase().includes("@taskflow ai");
}

function isPdfAttachment(attachment) {
  return attachment.contentType === "application/pdf" || attachment.fileName.toLowerCase().endsWith(".pdf");
}

function getAttachmentLabel(attachment) {
  if (isPdfAttachment(attachment)) return "PDF";
  if (attachment.contentType.startsWith("image/")) return "Image";

  const extension = attachment.fileName.split(".").pop();
  return extension && extension !== attachment.fileName ? extension.toUpperCase() : "File";
}

function getMessageTimestamp(message) {
  const date = new Date(message?.createdAtUtc || 0);
  return Number.isNaN(date.getTime()) ? 0 : date.getTime();
}

const blankChannelForm = { name: "", description: "", isPrivate: false };

export default function Channels() {
  const { t } = useI18n();
  const [searchParams] = useSearchParams();
  const selectedChannelId = searchParams.get("channelId") || "";
  const [workspaceId, setWorkspaceId] = useState("");
  const [workspaceName, setWorkspaceName] = useState("");
  const [channels, setChannels] = useState([]);
  const [activeChannelId, setActiveChannelId] = useState("");
  const [channelForm, setChannelForm] = useState(blankChannelForm);
  const [channelSaving, setChannelSaving] = useState(false);
  const [channelCreateError, setChannelCreateError] = useState("");
  const [channelMembers, setChannelMembers] = useState([]);
  const [memberEmail, setMemberEmail] = useState("");
  const [memberLoading, setMemberLoading] = useState(false);
  const [memberSaving, setMemberSaving] = useState(false);
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
  const channelNameRef = useRef(null);
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
            setWorkspaceId("");
            setWorkspaceName("");
            setChannels([]);
            setActiveChannelId("");
          }
          return;
        }

        const channelItems = asArray(await channelsApi.listByWorkspace(workspace.id)).map(mapChannel);

        if (active) {
          setWorkspaceId(workspace.id);
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
    let active = true;

    async function loadMembers() {
      if (!activeChannelId) {
        setChannelMembers([]);
        return;
      }

      setMemberLoading(true);
      setChatError("");

      try {
        const memberItems = asArray(await channelsApi.members(activeChannelId)).map(mapChannelMember);
        if (active) setChannelMembers(memberItems);
      } catch (apiError) {
        if (active) setChatError(formatApiError(apiError));
      } finally {
        if (active) setMemberLoading(false);
      }
    }

    loadMembers();

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

  async function sendSelectedAttachmentMessage(content) {
    if (!selectedFile || !activeChannelId) return null;

    setUploadingAttachment(true);
    setChatError("");

    try {
      const formData = new FormData();
      formData.append("content", content);
      formData.append("file", selectedFile);
      const createdMessage = await messagesApi.createAttachment(activeChannelId, formData);
      const uploadedAttachment = normalizeAttachment(createdMessage?.attachments?.[0] || {});

      setMessages((currentMessages) => mergeMessage(currentMessages, createdMessage));
      if (uploadedAttachment.id) {
        setAttachments((current) => [uploadedAttachment, ...current.filter((item) => item.id !== uploadedAttachment.id)]);
        setSelectedAttachmentId(uploadedAttachment.id);
      }

      setDraft("");
      clearSelectedFile();
      clearTimeout(typingStopTimerRef.current);
      if (connection?.state === chatConnectionState.connected) {
        await connection.invoke("StopTyping", activeChannelId);
      }

      return uploadedAttachment.id ? uploadedAttachment : null;
    } catch (apiError) {
      setChatError(formatApiError(apiError));
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
        uploadedAttachment = await sendSelectedAttachmentMessage(content);
      } else if (content) {
        if (!connection || connection.state !== chatConnectionState.connected) {
          setChatError(t("channel.readyError"));
          return;
        }

        await connection.invoke("SendMessage", activeChannelId, content);
        setDraft("");
        clearTimeout(typingStopTimerRef.current);
        await connection.invoke("StopTyping", activeChannelId);
      }

      if (isAiMention(content)) {
        const command = content;
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

  async function downloadAttachment(attachment) {
    if (!attachment?.downloadUrl) return;

    setChatError("");

    try {
      const response = await messagesApi.downloadAttachment(attachment.downloadUrl);
      const url = URL.createObjectURL(response.data);
      const link = document.createElement("a");
      link.href = url;
      link.download = attachment.fileName || "attachment";
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (apiError) {
      setChatError(formatApiError(apiError));
    }
  }

  async function reloadChannelMembers(channelId = activeChannelId) {
    if (!channelId) return;

    setMemberLoading(true);
    setChatError("");

    try {
      const memberItems = asArray(await channelsApi.members(channelId)).map(mapChannelMember);
      setChannelMembers(memberItems);
    } catch (apiError) {
      setChatError(formatApiError(apiError));
    } finally {
      setMemberLoading(false);
    }
  }

  function updateChannelForm(updates) {
    setChannelForm((current) => ({ ...current, ...updates }));
    setChannelCreateError("");
  }

  function closeCreateChannelModal() {
    setChannelForm({ ...blankChannelForm });
    setChannelCreateError("");
  }

  async function createChannel(event, closeModal) {
    event.preventDefault();
    if (!workspaceId || !channelForm.name.trim()) return;

    setChannelSaving(true);
    setChannelCreateError("");

    try {
      const createdChannel = await channelsApi.create(workspaceId, {
        name: channelForm.name.trim(),
        description: channelForm.description.trim() || null,
        isPrivate: channelForm.isPrivate,
      });
      const mappedChannel = mapChannel(createdChannel);
      setChannels((current) => [...current, mappedChannel].sort((left, right) => left.name.localeCompare(right.name)));
      setActiveChannelId(mappedChannel.id);
      setChannelForm({ ...blankChannelForm });
      closeModal?.();
    } catch (apiError) {
      setChannelCreateError(formatApiError(apiError));
    } finally {
      setChannelSaving(false);
    }
  }

  async function addChannelMember(event) {
    event.preventDefault();
    if (!activeChannelId || !memberEmail.trim()) return;

    setMemberSaving(true);
    setChatError("");

    try {
      await channelsApi.addMember(activeChannelId, { email: memberEmail.trim() });
      setMemberEmail("");
      await reloadChannelMembers();
      setChannels((current) => current.map((channel) => (
        channel.id === activeChannelId
          ? { ...channel, memberCount: channel.memberCount + 1 }
          : channel
      )));
    } catch (apiError) {
      setChatError(formatApiError(apiError));
    } finally {
      setMemberSaving(false);
    }
  }

  async function removeChannelMember(member) {
    setMemberSaving(true);
    setChatError("");

    try {
      await channelsApi.removeMember(activeChannelId, member.userId);
      await reloadChannelMembers();
      setChannels((current) => current.map((channel) => (
        channel.id === activeChannelId
          ? { ...channel, memberCount: Math.max(0, channel.memberCount - 1) }
          : channel
      )));
    } catch (apiError) {
      setChatError(formatApiError(apiError));
    } finally {
      setMemberSaving(false);
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
      && (selectedFile || isConnected)
  );

  return (
    <div className="page-stack">
      {loading && <section className="panel">{t("channel.loading")}</section>}
      {loadError && <section className="panel"><strong>{t("channel.unableLoad")}</strong><p>{loadError}</p></section>}
      {!loading && !loadError && !workspaceName && (
        <section className="panel">{t("channel.emptyWorkspace")}</section>
      )}

      {!loading && !loadError && workspaceName && (
        <section className="chat-layout">
          <aside className="panel channel-sidebar" aria-label={t("channel.workspaceList")}>
            <div className="member-manager-header">
              <strong>{workspaceName}</strong>
              <LinearModal
                closeLabel={t("channel.closeCreate")}
                description={t("channel.createHelp")}
                icon={Plus}
                initialFocusRef={channelNameRef}
                layoutId="channel-create-modal"
                onClose={closeCreateChannelModal}
                size="sm"
                title={t("channel.createTitle")}
                trigger={({ iconLayoutId, layoutId, open, titleLayoutId }) => (
                  <CreateActionButton
                    as={motion.button}
                    ariaLabel={t("channel.new")}
                    className="compact"
                    iconLayoutId={iconLayoutId}
                    layoutId={layoutId}
                    titleLayoutId={titleLayoutId}
                    onClick={() => {
                      setChannelCreateError("");
                      open();
                    }}
                  >
                    {t("channel.new")}
                  </CreateActionButton>
                )}
              >
                {({ close }) => (
                  <>
                    {channelCreateError && <div className="error-text"><strong>{t("channel.unableCreate")}</strong> {channelCreateError}</div>}

                    <form className="task-form" onSubmit={(event) => createChannel(event, close)} noValidate>
                      <label>
                        {t("channel.name")}
                        <input
                          ref={channelNameRef}
                          value={channelForm.name}
                          onChange={(event) => updateChannelForm({ name: event.target.value })}
                          placeholder={t("channel.placeholder.name")}
                        />
                      </label>
                      <label>
                        {t("channel.description")}
                        <input
                          value={channelForm.description}
                          onChange={(event) => updateChannelForm({ description: event.target.value })}
                          placeholder={t("channel.placeholder.description")}
                        />
                      </label>
                      <label className="checkbox-row">
                        <input
                          type="checkbox"
                          checked={channelForm.isPrivate}
                          onChange={(event) => updateChannelForm({ isPrivate: event.target.checked })}
                        />
                        {t("channel.private")}
                      </label>
                      <div className="button-row">
                        <button className="primary-button" type="submit" disabled={channelSaving || !channelForm.name.trim()}>
                          <Plus size={16} /> {channelSaving ? t("channel.creating") : t("channel.create")}
                        </button>
                        <button className="secondary-button" type="button" onClick={close}>
                          {t("workspace.cancel")}
                        </button>
                      </div>
                    </form>
                  </>
                )}
              </LinearModal>
            </div>

            <div className="channel-list">
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
            </div>

            {activeChannel && (
              <div className="member-manager compact-members">
                <div className="member-manager-header">
                  <strong>Channel members</strong>
                  <button className="secondary-button compact" type="button" onClick={() => reloadChannelMembers()}>
                    Refresh
                  </button>
                </div>
                {memberLoading && <p>Loading members...</p>}
                {!memberLoading && channelMembers.length === 0 && <p>No members loaded.</p>}
                <div className="member-list">
                  {channelMembers.map((member) => (
                    <div className="member-row compact" key={member.userId}>
                      <div>
                        <strong>{member.name}</strong>
                        <span>{member.email}</span>
                      </div>
                      <button
                        className="danger-button icon-only"
                        type="button"
                        aria-label={`Remove ${member.name}`}
                        onClick={() => removeChannelMember(member)}
                        disabled={memberSaving}
                      >
                        <Trash2 size={13} />
                      </button>
                    </div>
                  ))}
                </div>
                <form className="member-add-row compact-add" onSubmit={addChannelMember}>
                  <input
                    value={memberEmail}
                    onChange={(event) => setMemberEmail(event.target.value)}
                    placeholder="member@email.com"
                  />
                  <button className="primary-button compact" type="submit" disabled={memberSaving || !memberEmail.trim()}>
                    <UserPlus size={15} /> Add
                  </button>
                </form>
              </div>
            )}
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
                    {message.isAi ? (
                      <MarkdownContent>{message.text}</MarkdownContent>
                    ) : (
                      message.text && <p>{message.text}</p>
                    )}
                    {message.attachments?.length > 0 && (
                      <div className="message-attachments">
                        {message.attachments.map((attachment) => {
                          const isPdf = isPdfAttachment(attachment);
                          const isImage = attachment.contentType.startsWith("image/");
                          return (
                            <button
                              key={attachment.id}
                              type="button"
                              className={`message-attachment-card ${isPdf ? "pdf-card" : ""}`}
                              onClick={() => downloadAttachment(attachment)}
                              aria-label={`Download ${attachment.fileName}`}
                            >
                              <span className="message-attachment-icon" aria-hidden="true">
                                {isImage ? <Image size={22} /> : <FileText size={22} />}
                              </span>
                              <span className="message-attachment-copy">
                                <strong>{attachment.fileName}</strong>
                                <small>{getAttachmentLabel(attachment)}</small>
                              </span>
                              <Download className="message-attachment-download" size={16} aria-hidden="true" />
                            </button>
                          );
                        })}
                      </div>
                    )}
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
                  <span>{selectedFile.name} - {formatFileSize(selectedFile.size)}</span>
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
                        <small>{formatFileSize(attachment.sizeBytes)} - {attachment.time}</small>
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
                  {selectedAttachment.downloadUrl && (
                    <button
                      className="secondary-button compact"
                      type="button"
                      onClick={() => downloadAttachment(selectedAttachment)}
                    >
                      <Download size={14} /> Download
                    </button>
                  )}
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

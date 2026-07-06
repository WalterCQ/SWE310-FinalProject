import { useEffect, useRef, useState } from "react";
import { motion } from "motion/react";
import { useSearchParams } from "react-router-dom";
import {
  ArrowDownToLine,
  Bot,
  ClipboardCheck,
  Code2,
  Download,
  FileText,
  Hash,
  Image,
  ListTodo,
  MoreHorizontal,
  Paperclip,
  Plus,
  RefreshCw,
  Send,
  Sparkles,
  Layers3,
  Trash2,
  UserPlus,
  Users,
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
  agent as agentApi,
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
  mapWorkspaceMember,
  selectPrimaryWorkspace,
} from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";
import { usePageRoleContext } from "../pageRoleContext.jsx";

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

function formatRealtimeError(error, t) {
  const message = String(error?.message || "").toLowerCase();

  if (
    message.includes("no connection with that id")
    || message.includes("status code")
    || message.includes("websocket")
    || message.includes("negotiate")
    || message.includes("failed to start")
  ) {
    return t("channel.realtimeEndpointMismatch");
  }

  if (
    message.includes("access denied")
    || message.includes("unauthorized")
    || message.includes("forbidden")
  ) {
    return t("channel.realtimeAccessDenied");
  }

  return t("channel.realtimeGeneric");
}

function normalizeAttachment(attachment) {
  return mapAttachment(attachment);
}

function selectContextAttachmentId(attachmentItems, currentId = "") {
  if (attachmentItems.some((attachment) => attachment.id === currentId && attachment.isAiIndexed)) {
    return currentId;
  }

  return attachmentItems.find((attachment) => attachment.isAiIndexed)?.id || currentId || "";
}

function isPdfAttachment(attachment) {
  return attachment.contentType === "application/pdf" || attachment.fileName.toLowerCase().endsWith(".pdf");
}

function getAttachmentLabel(attachment, t) {
  if (isPdfAttachment(attachment)) return t("channel.attachmentType.pdf");
  if (attachment.contentType.startsWith("image/")) return t("channel.attachmentType.image");

  const extension = attachment.fileName.split(".").pop();
  return extension && extension !== attachment.fileName ? extension.toUpperCase() : t("channel.attachmentType.file");
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

function formatAiPreview(message, fallback) {
  const lines = String(message?.text || "")
    .split("\n")
    .map((line) => line.replace(/^#+\s*/, "").replace(/^[-*]\s*/, "").trim())
    .filter(Boolean);
  const title = lines[0] || fallback;
  const excerpt = lines.slice(1).join(" ") || title;

  return {
    title: title.length > 96 ? `${title.slice(0, 93).trim()}...` : title,
    excerpt: excerpt.length > 180 ? `${excerpt.slice(0, 177).trim()}...` : excerpt,
  };
}

const AGENT_JOB_STATUS_KEYS = {
  0: "planning",
  1: "awaiting-approval",
  2: "running",
  3: "needs-approval",
  4: "paused",
  5: "completed",
  6: "failed",
  7: "canceled",
};

const AGENT_APPROVAL_STATUS_KEYS = {
  0: "pending",
  1: "approved",
  2: "rejected",
  3: "expired",
};

const TERMINAL_AGENT_STATUSES = new Set(["completed", "failed", "canceled"]);

function normalizeStatusKey(value, numericMap) {
  if (value === null || typeof value === "undefined" || value === "") return "unknown";
  const raw = String(value).trim();
  if (Object.prototype.hasOwnProperty.call(numericMap, raw)) return numericMap[raw];

  return raw
    .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
    .replace(/[_\s]+/g, "-")
    .toLowerCase();
}

function getStatusClassName(statusKey) {
  return statusKey.replace(/[^a-z0-9]+/g, "");
}

function getStatusLabel(statusKey) {
  return statusKey
    .split("-")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function isPendingApproval(approval) {
  return normalizeStatusKey(approval?.status, AGENT_APPROVAL_STATUS_KEYS) === "pending";
}

function isAgentJobActive(job) {
  if (!job) return true;
  const statusKey = normalizeStatusKey(job.status, AGENT_JOB_STATUS_KEYS);
  return !TERMINAL_AGENT_STATUSES.has(statusKey) || asArray(job.approvals).some(isPendingApproval);
}

function parseJsonPreview(value) {
  if (!value) return null;
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function formatAgentJobId(jobId) {
  return jobId ? String(jobId).slice(0, 8) : "";
}

const blankChannelForm = { name: "", description: "", isPrivate: false };
const AI_PANEL_OPEN_KEY = "taskflow.aiPanelOpen";
const AI_MESSAGES_STORAGE_KEY = "taskflow.channelAiMessages";

function readStoredAiMessages() {
  if (typeof window === "undefined") return [];

  try {
    const parsed = JSON.parse(window.sessionStorage.getItem(AI_MESSAGES_STORAGE_KEY) || "[]");
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function writeStoredAiMessages(messages) {
  if (typeof window === "undefined") return;
  window.sessionStorage.setItem(AI_MESSAGES_STORAGE_KEY, JSON.stringify(messages.slice(-50)));
}

export default function Channels() {
  const { t } = useI18n();
  const { setContextRole } = usePageRoleContext();
  const [searchParams] = useSearchParams();
  const selectedChannelId = searchParams.get("channelId") || "";
  const currentUserId = localStorage.getItem("userId") || "";
  const [workspaceId, setWorkspaceId] = useState("");
  const [workspaceName, setWorkspaceName] = useState("");
  const [currentWorkspaceRole, setCurrentWorkspaceRole] = useState("");
  const [channels, setChannels] = useState([]);
  const [activeChannelId, setActiveChannelId] = useState("");
  const [channelForm, setChannelForm] = useState(blankChannelForm);
  const [channelSaving, setChannelSaving] = useState(false);
  const [channelCreateError, setChannelCreateError] = useState("");
  const [channelMembers, setChannelMembers] = useState([]);
  const [memberEmail, setMemberEmail] = useState("");
  const [memberLoading, setMemberLoading] = useState(false);
  const [memberSaving, setMemberSaving] = useState(false);
  const [isMembersPanelOpen, setIsMembersPanelOpen] = useState(false);
  const [openMemberMenuId, setOpenMemberMenuId] = useState("");
  const [messages, setMessages] = useState([]);
  const [aiMessages, setAiMessages] = useState(readStoredAiMessages);
  const [attachments, setAttachments] = useState([]);
  const [selectedAttachmentId, setSelectedAttachmentId] = useState("");
  const [selectedFile, setSelectedFile] = useState(null);
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [messageLoading, setMessageLoading] = useState(false);
  const [attachmentLoading, setAttachmentLoading] = useState(false);
  const [uploadingAttachment, setUploadingAttachment] = useState(false);
  const [reindexingAttachmentId, setReindexingAttachmentId] = useState("");
  const [aiLoading, setAiLoading] = useState(false);
  const [agentJobsById, setAgentJobsById] = useState({});
  const [agentJobLoadingIds, setAgentJobLoadingIds] = useState({});
  const [agentJobErrors, setAgentJobErrors] = useState({});
  const [agentApprovalActionIds, setAgentApprovalActionIds] = useState({});
  const [loadError, setLoadError] = useState("");
  const [chatError, setChatError] = useState("");
  const [aiError, setAiError] = useState("");
  const [isAiPanelOpen, setIsAiPanelOpen] = useState(() => {
    return localStorage.getItem(AI_PANEL_OPEN_KEY) === "true";
  });
  const [selectedAiMessageId, setSelectedAiMessageId] = useState("");
  const [isMessageListAtBottom, setIsMessageListAtBottom] = useState(true);
  const [unseenMessages, setUnseenMessages] = useState(0);
  const [connection, setConnection] = useState(null);
  const [connectionStatus, setConnectionStatus] = useState("connecting");
  const [typingUserId, setTypingUserId] = useState("");
  const activeChannelRef = useRef("");
  const messageListRef = useRef(null);
  const messageEndRef = useRef(null);
  const fileInputRef = useRef(null);
  const channelNameRef = useRef(null);
  const typingClearTimerRef = useRef(null);
  const typingStopTimerRef = useRef(null);
  const lastTypingSentRef = useRef(0);
  const previousMessageCountRef = useRef(0);
  const forceScrollToBottomRef = useRef(false);
  const membersDrawerRef = useRef(null);
  const membersPanelTriggerRef = useRef(null);

  const aiActionGroups = [
    {
      key: "understand",
      label: t("channel.aiGroupUnderstand"),
      actions: [
        {
          key: "summary",
          tone: "teal",
          label: t("channel.quickSummary"),
          command: t("channel.commandSummary"),
          icon: Sparkles,
          output: t("channel.outputChat"),
          approval: t("channel.approvalNo"),
        },
        {
          key: "requirements",
          tone: "green",
          label: t("channel.quickRequirements"),
          command: t("channel.commandRequirements"),
          icon: ClipboardCheck,
          output: t("channel.outputChat"),
          approval: t("channel.approvalNo"),
        },
      ],
    },
    {
      key: "produce",
      label: t("channel.aiGroupProduce"),
      actions: [
        {
          key: "tasks",
          tone: "blue",
          label: t("channel.quickTasks"),
          command: t("channel.commandTasks"),
          icon: ListTodo,
          output: t("channel.outputTasks"),
          approval: t("channel.approvalYes"),
        },
        {
          key: "report",
          tone: "purple",
          label: t("channel.quickReport"),
          command: t("channel.commandReport"),
          icon: FileText,
          output: t("channel.outputDocx"),
          approval: t("channel.approvalYes"),
          requiresIndexedAttachment: true,
        },
        {
          key: "ppt",
          tone: "orange",
          label: t("channel.quickPpt"),
          command: t("channel.commandPpt"),
          icon: Layers3,
          output: t("channel.outputPptx"),
          approval: t("channel.approvalYes"),
          requiresIndexedAttachment: true,
        },
      ],
    },
    {
      key: "engineering",
      label: t("channel.aiGroupEngineering"),
      actions: [
        {
          key: "code",
          tone: "dark",
          label: t("channel.quickCode"),
          command: t("channel.commandCode"),
          icon: Code2,
          output: t("channel.outputCodePlan"),
          approval: t("channel.approvalYes"),
          requiresIndexedAttachment: true,
        },
      ],
    },
  ];
  const activeChannel = channels.find((channel) => channel.id === activeChannelId);
  const selectedAttachment = attachments.find((attachment) => attachment.id === selectedAttachmentId);
  const selectedAttachmentIndexed = Boolean(selectedAttachment?.isAiIndexed);
  const selectedAttachmentFailed = Boolean(selectedAttachment && !selectedAttachment.isAiIndexed);
  const recentChannelOutputs = messages
    .filter((message) => message.channelId === activeChannelId && message.isAi)
    .flatMap((message) => asArray(message.attachments))
    .sort((left, right) => new Date(right.createdAtUtc || 0).getTime() - new Date(left.createdAtUtc || 0).getTime())
    .slice(0, 6);
  const connectionLabel = t(`channel.${connectionStatus}`);
  const isConnected = connectionStatus === "connected";
  const activeAiMessages = aiMessages.filter((message) => message.channelId === activeChannelId);
  const messageIds = new Set(messages.map((message) => message.id));
  const aiDetailsById = new Map(activeAiMessages.map((message) => [message.id, message]));
  const mergedMessages = messages.map((message) => {
    const details = aiDetailsById.get(message.id);
    if (!message.isAi || !details) return message;

    return {
      ...message,
      ...details,
      attachments: message.attachments,
      text: message.text || details.text,
      time: message.time || details.time,
      createdAtUtc: message.createdAtUtc || details.createdAtUtc,
    };
  });
  const combinedMessages = [
    ...mergedMessages,
    ...activeAiMessages.filter((message) => !messageIds.has(message.id)),
  ].sort((left, right) => getMessageTimestamp(left) - getMessageTimestamp(right));
  const selectedAiMessage = combinedMessages.find((message) => message.id === selectedAiMessageId && message.isAi) || null;
  const canSubmit = Boolean(
    activeChannel
      && (draft.trim() || selectedFile)
      && !uploadingAttachment
      && !aiLoading
      && (selectedFile || isConnected)
  );

  function isCurrentUser(userId) {
    return Boolean(userId && currentUserId && String(userId).toLowerCase() === currentUserId.toLowerCase());
  }

  useEffect(() => {
    activeChannelRef.current = activeChannelId;
  }, [activeChannelId]);

  useEffect(() => {
    setContextRole(currentWorkspaceRole || "");
    return () => setContextRole("");
  }, [currentWorkspaceRole, setContextRole]);

  useEffect(() => {
    setOpenMemberMenuId("");
  }, [activeChannelId]);

  useEffect(() => {
    writeStoredAiMessages(aiMessages);
  }, [aiMessages]);

  useEffect(() => {
    if (!isMembersPanelOpen) return;
    requestAnimationFrame(() => membersDrawerRef.current?.focus());
  }, [isMembersPanelOpen]);

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
            setCurrentWorkspaceRole("");
            setChannels([]);
            setActiveChannelId("");
          }
          return;
        }

        let workspaceRole = "";
        try {
          const workspaceMembers = asArray(await workspacesApi.members(workspace.id)).map(mapWorkspaceMember);
          workspaceRole = workspaceMembers.find((member) => isCurrentUser(member.userId))?.role ?? "";
        } catch {
          workspaceRole = "";
        }

        const channelItems = asArray(await channelsApi.listByWorkspace(workspace.id)).map(mapChannel);

        if (active) {
          setWorkspaceId(workspace.id);
          setWorkspaceName(workspace.name);
          setCurrentWorkspaceRole(workspaceRole);
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
            selectContextAttachmentId(attachmentItems, current)
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
          setChatError(formatRealtimeError(hubError, t));
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
        setChatError(formatRealtimeError(hubError, t));
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
  }, [t]);

  useEffect(() => {
    if (!connection || !activeChannelId || connection.state !== chatConnectionState.connected) return undefined;

    let leaving = false;

    async function joinChannel() {
      setTypingUserId("");
      setChatError("");

      try {
        await connection.invoke("JoinChannel", activeChannelId);
      } catch (hubError) {
        if (!leaving) setChatError(formatRealtimeError(hubError, t));
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

  function isNearMessageListBottom() {
    const list = messageListRef.current;
    if (!list) return true;
    return list.scrollHeight - list.scrollTop - list.clientHeight < 80;
  }

  function updateMessageListBottomState() {
    const isNearBottom = isNearMessageListBottom();
    setIsMessageListAtBottom(isNearBottom);
    if (isNearBottom) setUnseenMessages(0);
  }

  function scrollToLatestMessage(behavior = "smooth") {
    messageEndRef.current?.scrollIntoView({ block: "end", behavior });
    setIsMessageListAtBottom(true);
    setUnseenMessages(0);
  }

  function openAiPanel() {
    setIsAiPanelOpen(true);
    localStorage.setItem(AI_PANEL_OPEN_KEY, "true");
  }

  function openAiMessage(message) {
    setSelectedAiMessageId(message.id);
    openAiPanel();
  }

  function closeAiPanel() {
    setIsAiPanelOpen(false);
    localStorage.setItem(AI_PANEL_OPEN_KEY, "false");
  }

  function openMembersPanel(event) {
    if (event?.currentTarget) membersPanelTriggerRef.current = event.currentTarget;
    setIsMembersPanelOpen(true);
    setOpenMemberMenuId("");
  }

  function closeMembersPanel() {
    setIsMembersPanelOpen(false);
    setOpenMemberMenuId("");
    requestAnimationFrame(() => membersPanelTriggerRef.current?.focus());
  }

  function handleMembersDrawerKeyDown(event) {
    if (event.key === "Escape") {
      closeMembersPanel();
      return;
    }

    if (event.key !== "Tab") return;

    const focusableItems = Array.from(
      membersDrawerRef.current?.querySelectorAll(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
      ) || []
    ).filter((element) => element.offsetParent !== null);

    if (focusableItems.length === 0) {
      event.preventDefault();
      membersDrawerRef.current?.focus();
      return;
    }

    const firstItem = focusableItems[0];
    const lastItem = focusableItems[focusableItems.length - 1];

    if (event.shiftKey && document.activeElement === firstItem) {
      event.preventDefault();
      lastItem.focus();
      return;
    }

    if (!event.shiftKey && document.activeElement === lastItem) {
      event.preventDefault();
      firstItem.focus();
    }
  }

  useEffect(() => {
    previousMessageCountRef.current = 0;
    forceScrollToBottomRef.current = true;
    setUnseenMessages(0);
    setIsMessageListAtBottom(true);
  }, [activeChannelId]);

  useEffect(() => {
    const messageCount = combinedMessages.length + (aiLoading ? 1 : 0);
    const previousCount = previousMessageCountRef.current;
    const hasNewMessages = messageCount > previousCount;
    previousMessageCountRef.current = messageCount;

    if (!activeChannel || messageCount === 0) return;

    const shouldScroll = forceScrollToBottomRef.current || isMessageListAtBottom || previousCount === 0;
    if (shouldScroll) {
      const behavior = forceScrollToBottomRef.current ? "smooth" : "auto";
      forceScrollToBottomRef.current = false;
      requestAnimationFrame(() => scrollToLatestMessage(behavior));
      return;
    }

    if (hasNewMessages) {
      setUnseenMessages((current) => current + (messageCount - previousCount));
    }
  }, [activeChannel, activeChannelId, aiLoading, combinedMessages.length, isMessageListAtBottom]);

  useEffect(() => {
    const activeJobIds = aiMessages
      .filter((message) => message.channelId === activeChannelId && message.agentJobId)
      .map((message) => message.agentJobId)
      .filter((jobId, index, allJobIds) => allJobIds.indexOf(jobId) === index)
      .filter((jobId) => isAgentJobActive(agentJobsById[jobId]));

    if (activeJobIds.length === 0) return undefined;

    activeJobIds.forEach((jobId) => {
      if (!agentJobsById[jobId]) loadAgentJob(jobId, { silent: true });
    });

    const refreshTimer = window.setInterval(() => {
      activeJobIds.forEach((jobId) => loadAgentJob(jobId, { silent: true }));
    }, 5000);

    return () => window.clearInterval(refreshTimer);
  }, [activeChannelId, agentJobsById, aiMessages]);

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

  function setAgentJobLoading(jobId, isLoading) {
    setAgentJobLoadingIds((current) => {
      const next = { ...current };
      if (isLoading) next[jobId] = true;
      else delete next[jobId];
      return next;
    });
  }

  function setAgentJobError(jobId, errorMessage) {
    setAgentJobErrors((current) => {
      const next = { ...current };
      if (errorMessage) next[jobId] = errorMessage;
      else delete next[jobId];
      return next;
    });
  }

  async function loadAgentJob(jobId, options = {}) {
    if (!jobId) return null;
    const silent = Boolean(options.silent);
    if (!silent) setAgentJobLoading(jobId, true);

    try {
      const job = await agentApi.getJob(jobId);
      setAgentJobsById((current) => ({ ...current, [jobId]: job }));
      setAgentJobError(jobId, "");
      return job;
    } catch (apiError) {
      const errorMessage = formatApiError(apiError);
      setAgentJobError(jobId, errorMessage);
      if (!silent) setAiError(errorMessage);
      return null;
    } finally {
      if (!silent) setAgentJobLoading(jobId, false);
    }
  }

  async function decideAgentApproval(jobId, approval, decision) {
    if (!jobId || !approval?.id) return;
    const actionId = `${decision}-${approval.id}`;
    setAgentApprovalActionIds((current) => ({ ...current, [actionId]: true }));
    setAiError("");

    try {
      if (decision === "approve") {
        await agentApi.approve(approval.id);
      } else {
        await agentApi.reject(approval.id);
      }
      await loadAgentJob(jobId);
    } catch (apiError) {
      setAiError(formatApiError(apiError));
    } finally {
      setAgentApprovalActionIds((current) => {
        const next = { ...current };
        delete next[actionId];
        return next;
      });
    }
  }

  async function downloadAgentArtifact(artifact) {
    if (!artifact?.id) return;
    setAiError("");

    try {
      const response = await agentApi.downloadArtifact(artifact.id);
      const url = URL.createObjectURL(response.data);
      const link = document.createElement("a");
      link.href = url;
      link.download = artifact.name || "agent-artifact";
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (apiError) {
      setAiError(formatApiError(apiError));
    }
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

  async function deleteAttachment(attachmentId) {
    if (!activeChannelId || !attachmentId) return;

    setChatError("");
    setAiError("");

    try {
      await channelsApi.removeAttachment(activeChannelId, attachmentId);
      setAttachments((current) => current.filter((item) => item.id !== attachmentId));
      if (selectedAttachmentId === attachmentId) {
        setSelectedAttachmentId("");
      }
    } catch (apiError) {
      setAiError(formatApiError(apiError));
    }
  }

  async function reindexAttachment(attachmentId) {
    if (!activeChannelId || !attachmentId || reindexingAttachmentId) return;

    setReindexingAttachmentId(attachmentId);
    setAiError("");

    try {
      const attachment = normalizeAttachment(await aiApi.reindexChannelAttachment(activeChannelId, attachmentId));
      setAttachments((current) => {
        const hasAttachment = current.some((item) => item.id === attachment.id);
        return hasAttachment
          ? current.map((item) => (item.id === attachment.id ? attachment : item))
          : [attachment, ...current];
      });
      setSelectedAttachmentId(attachment.id);
    } catch (apiError) {
      setAiError(formatApiError(apiError));
    } finally {
      setReindexingAttachmentId("");
    }
  }

  async function runAiCommand(command, attachmentId = "") {
    if (!activeChannelId || !command.trim()) return null;

    forceScrollToBottomRef.current = true;
    setAiLoading(true);
    setAiError("");

    try {
      const response = await aiApi.channelCommand(activeChannelId, {
        command: command.trim(),
        attachmentId: attachmentId || null,
      });
      if (response?.sharedToChannel) {
        const messageItems = asArray(await messagesApi.listByChannel(activeChannelId)).map(mapMessage);
        setMessages(messageItems);
        if (response?.messageId) {
          const createdAtUtc = response?.createdAtUtc || new Date().toISOString();
          const aiMessage = {
            id: response.messageId,
            channelId: activeChannelId,
            sender: t("channel.aiMember"),
            text: response?.result || t("channel.aiEmpty"),
            createdAtUtc,
            time: formatDateTime(createdAtUtc),
            isAi: true,
            artifactType: response?.artifactType || "answer",
            sources: response?.sources || [],
            suggestedTasks: response?.suggestedTasks || [],
            createdTaskTitle: response?.createdTaskTitle || "",
            agentJobId: response?.agentJobId || "",
            requiresApproval: Boolean(response?.requiresApproval),
          };
          setAiMessages((current) => [...current.filter((message) => message.id !== aiMessage.id), aiMessage]);
          setSelectedAiMessageId(aiMessage.id);
        }
        const attachmentItems = asArray(await aiApi.channelAttachments(activeChannelId)).map(normalizeAttachment);
        setAttachments(attachmentItems);
        setSelectedAttachmentId((current) => selectContextAttachmentId(attachmentItems, current));
        return response;
      }

      const createdAtUtc = response?.createdAtUtc || new Date().toISOString();
      const aiMessage = {
        id: `ai-${activeChannelId}-${Date.now()}`,
        channelId: activeChannelId,
        sender: t("channel.aiMember"),
        text: response?.result || t("channel.aiEmpty"),
        createdAtUtc,
        time: formatDateTime(createdAtUtc),
        isAi: true,
        artifactType: response?.artifactType || "answer",
        sources: response?.sources || [],
        suggestedTasks: response?.suggestedTasks || [],
        createdTaskTitle: response?.createdTaskTitle || "",
        agentJobId: response?.agentJobId || "",
        requiresApproval: Boolean(response?.requiresApproval),
      };
      setAiMessages((current) => [...current, aiMessage]);
      if (response?.agentJobId) {
        loadAgentJob(response.agentJobId, { silent: true });
      }
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

        forceScrollToBottomRef.current = true;
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
        setChatError(formatRealtimeError(error, t));
      }
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

  function confirmRemoveChannelMember(member) {
    const confirmed = window.confirm(t("channel.removeMemberConfirm", { name: member.name }));
    if (!confirmed) return;

    setOpenMemberMenuId("");
    removeChannelMember(member);
  }

  function handleMemberMenuButtonKeyDown(event, member) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setOpenMemberMenuId(member.userId);
      requestAnimationFrame(() => {
        document.getElementById(`member-menu-remove-${member.userId}`)?.focus();
      });
    }

    if (event.key === "Escape") {
      setOpenMemberMenuId("");
    }
  }

  function renderAgentJobPanel(message) {
    const jobId = message.agentJobId;
    const job = agentJobsById[jobId];
    const statusKey = normalizeStatusKey(job?.status ?? (message.requiresApproval ? "AwaitingApproval" : "Planning"), AGENT_JOB_STATUS_KEYS);
    const pendingApprovals = asArray(job?.approvals).filter(isPendingApproval);
    const artifacts = asArray(job?.artifacts);
    const errorMessage = agentJobErrors[jobId];
    const isRefreshing = Boolean(agentJobLoadingIds[jobId]);

    return (
      <div className="channel-agent-job" data-agent-job-id={jobId}>
        <div className="channel-agent-job-header">
          <div>
            <span className="channel-agent-job-kicker">{t("channel.agentJob")}</span>
            <strong>{formatAgentJobId(jobId)}</strong>
          </div>
          <span className={`status-badge ${getStatusClassName(statusKey)}`}>
            {getStatusLabel(statusKey)}
          </span>
          <button
            className="secondary-button compact channel-agent-refresh"
            type="button"
            onClick={() => loadAgentJob(jobId)}
            disabled={isRefreshing}
          >
            <RefreshCw size={13} /> {isRefreshing ? t("channel.agentLoading") : t("ai.refresh")}
          </button>
        </div>

        {errorMessage && <p className="channel-agent-error" role="alert">{errorMessage}</p>}

        <div className="channel-agent-section">
          <div className="channel-agent-section-title">
            <strong>{t("ai.approvalQueue")}</strong>
            <span>{pendingApprovals.length}</span>
          </div>
          {pendingApprovals.length === 0 ? (
            <p className="muted-small">{t("ai.noApprovals")}</p>
          ) : (
            <div className="channel-agent-approval-list">
              {pendingApprovals.map((approval) => {
                const approveActionId = `approve-${approval.id}`;
                const rejectActionId = `reject-${approval.id}`;
                const preview = parseJsonPreview(approval.previewJson);
                const previewText = preview
                  ? JSON.stringify(preview, null, 2)
                  : approval.previewJson;

                return (
                  <div className="channel-agent-approval" key={approval.id}>
                    <div>
                      <strong>{approval.title || t("ai.reviewAction")}</strong>
                      <small>{approval.actionName || approval.approvalType || t("ai.needsConfirmation")}</small>
                    </div>
                    {previewText && (
                      <details className="raw-json channel-agent-preview">
                        <summary>{t("ai.viewRawJson")}</summary>
                        <pre>{previewText}</pre>
                      </details>
                    )}
                    <div className="channel-agent-actions">
                      <button
                        className="primary-button compact"
                        type="button"
                        onClick={() => decideAgentApproval(jobId, approval, "approve")}
                        disabled={Boolean(agentApprovalActionIds[approveActionId]) || Boolean(agentApprovalActionIds[rejectActionId])}
                      >
                        <ClipboardCheck size={13} /> {t("ai.approve")}
                      </button>
                      <button
                        className="secondary-button compact danger"
                        type="button"
                        onClick={() => decideAgentApproval(jobId, approval, "reject")}
                        disabled={Boolean(agentApprovalActionIds[approveActionId]) || Boolean(agentApprovalActionIds[rejectActionId])}
                      >
                        <X size={13} /> {t("ai.reject")}
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <div className="channel-agent-section">
          <div className="channel-agent-section-title">
            <strong>{t("ai.artifacts")}</strong>
            <span>{artifacts.length}</span>
          </div>
          {artifacts.length === 0 ? (
            <p className="muted-small">{t("ai.noArtifacts")}</p>
          ) : (
            <div className="channel-agent-artifact-list">
              {artifacts.map((artifact) => (
                <button
                  className="channel-agent-artifact"
                  type="button"
                  key={artifact.id}
                  onClick={() => downloadAgentArtifact(artifact)}
                  disabled={!artifact.isDownloadable}
                >
                  <FileText size={16} />
                  <span>
                    <strong>{artifact.name}</strong>
                    <small>{artifact.contentType || formatFileSize(artifact.sizeBytes)}</small>
                  </span>
                  <Download size={14} />
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    );
  }

  function isAiActionDisabled(action) {
    return !activeChannel
      || aiLoading
      || Boolean(action.requiresIndexedAttachment && selectedAttachmentFailed);
  }

  function getAiActionDisabledReason(action) {
    if (!action.requiresIndexedAttachment || !selectedAttachmentFailed) return "";
    return t("channel.actionNeedsIndexedAttachment");
  }

  function renderSourceReadiness() {
    if (!selectedAttachment) {
      return (
        <div className="source-readiness-card empty">
          <strong>{t("channel.noSelectedContext")}</strong>
          <p>{t("channel.sourceChannelFallback")}</p>
        </div>
      );
    }

    const isImage = selectedAttachment.contentType.startsWith("image/");
    const statusKey = selectedAttachmentIndexed ? "ready" : "blocked";
    const summary = selectedAttachment.summary || (selectedAttachmentIndexed
      ? t("channel.sourceReadyNoSummary")
      : t("channel.sourceNotIndexed"));
    const isReindexingSelectedAttachment = reindexingAttachmentId === selectedAttachment.id;

    return (
      <div className={`source-readiness-card ${statusKey}`}>
        <div className="source-readiness-main">
          {isImage ? <Image size={17} /> : <FileText size={17} />}
          <span>
            <strong>{selectedAttachment.fileName}</strong>
            <small>{getAttachmentLabel(selectedAttachment, t)} · {formatFileSize(selectedAttachment.sizeBytes)}</small>
          </span>
        </div>
        <div className={`source-readiness-status ${statusKey}`}>
          {selectedAttachmentIndexed ? t("channel.sourceReady") : t("channel.sourceBlocked")}
        </div>
        <p>{summary}</p>
        {!selectedAttachmentIndexed && (
          <button
            type="button"
            className="source-reindex-button"
            onClick={() => reindexAttachment(selectedAttachment.id)}
            disabled={Boolean(reindexingAttachmentId)}
          >
            <RefreshCw size={14} />
            {isReindexingSelectedAttachment ? t("channel.sourceIndexing") : t("channel.retryIndex")}
          </button>
        )}
      </div>
    );
  }

  function renderAiMessageCard(message) {
    const preview = formatAiPreview(message, t("channel.aiEmpty"));
    const sourceCount = asArray(message.sources).length;

    return (
      <button
        type="button"
        className="ai-result-card"
        onClick={() => openAiMessage(message)}
        aria-label={t("channel.openAiResult")}
      >
        <span className="ai-result-icon" aria-hidden="true"><Sparkles size={16} /></span>
        <span className="ai-result-copy">
          <strong>{preview.title}</strong>
          <small>{preview.excerpt}</small>
        </span>
        <span className="ai-result-meta">
          {sourceCount > 0 ? t("channel.aiSourceCount", { count: sourceCount }) : t("channel.noAiSources")}
        </span>
      </button>
    );
  }

  return (
    <div className="channel-page">
      {loading && <section className="panel">{t("channel.loading")}</section>}
      {loadError && <section className="panel"><strong>{t("channel.unableLoad")}</strong><p>{loadError}</p></section>}
      {!loading && !loadError && !workspaceName && (
        <section className="panel">{t("channel.emptyWorkspace")}</section>
      )}

      {!loading && !loadError && workspaceName && (
        <section className={`chat-layout ${isAiPanelOpen ? "ai-open" : ""}`}>
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

          </aside>

          <article className="panel chat-panel">
            <div className="panel-header chat-header">
              <h3 className="chat-title">
                {activeChannel ? (
                  <>
                    <span># {activeChannel.name}</span>
                    <span className="chat-title-count">({activeChannel.memberCount})</span>
                  </>
                ) : (
                  t("channel.noneSelected")
                )}
              </h3>
              <div className="chat-header-actions">
                <span className={`connection-pill ${connectionStatus}`} role="status" aria-live="polite">
                  {isConnected ? <Wifi size={14} /> : <WifiOff size={14} />} {connectionLabel}
                </span>
                <button
                  aria-controls="channel-members-drawer"
                  aria-expanded={isMembersPanelOpen}
                  aria-label={t("channel.manageMembers")}
                  className={`channel-more-button ${isMembersPanelOpen ? "active" : ""}`}
                  disabled={!activeChannel}
                  onClick={openMembersPanel}
                  title={t("channel.manageMembers")}
                  type="button"
                >
                  <MoreHorizontal size={18} />
                </button>
              </div>
            </div>

            {chatError && <div className="chat-alert" role="alert">{chatError}</div>}
            {aiError && <div className="chat-alert ai-error" role="alert">{aiError}</div>}

            <div
              ref={messageListRef}
              className="message-list"
              aria-label={t("channel.messages")}
              aria-live="polite"
              aria-busy={messageLoading || aiLoading}
              onScroll={updateMessageListBottomState}
            >
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
                      renderAiMessageCard(message)
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
                              aria-label={t("channel.downloadAttachment", { name: attachment.fileName })}
                            >
                              <span className="message-attachment-icon" aria-hidden="true">
                                {isImage ? <Image size={22} /> : <FileText size={22} />}
                              </span>
                              <span className="message-attachment-copy">
                                <strong>{attachment.fileName}</strong>
                                <small>{getAttachmentLabel(attachment, t)} · {formatFileSize(attachment.sizeBytes)}</small>
                              </span>
                              <span className="message-attachment-download">
                                <Download size={14} />
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    )}
                    {message.isAi && message.createdTaskTitle && (
                      <div className="created-task-chip">
                        <ListTodo size={14} /> {t("channel.createdTask", { title: message.createdTaskTitle })}
                      </div>
                    )}
                    {message.isAi && message.agentJobId && (
                      renderAgentJobPanel(message)
                    )}
                  </div>
                </div>
              ))}
              {aiLoading && (
                <div className="message-row ai-message-row">
                  <div className="message-avatar" aria-hidden="true"><Bot size={17} /></div>
                  <div className="message-body">
                    <strong>{t("channel.aiMember")}</strong>
                    <p>{t("channel.aiThinking")}</p>
                  </div>
                </div>
              )}
              <div ref={messageEndRef} />
            </div>

            {!isMessageListAtBottom && combinedMessages.length > 0 && (
              <button
                className="scroll-to-latest-button"
                onClick={() => scrollToLatestMessage("smooth")}
                type="button"
              >
                <ArrowDownToLine size={15} />
                <span>
                  {unseenMessages > 0
                    ? t("channel.newMessages", { count: unseenMessages })
                    : t("channel.scrollLatest")}
                </span>
              </button>
            )}

            <div className="typing-indicator" aria-live="polite">
              {typingUserId ? t("channel.typing") : ""}
            </div>

            <form className="message-composer" onSubmit={sendMessage}>
              <div className="composer-actions" role="toolbar" aria-label={t("channel.composeActions")}>
                <button
                  className={isAiPanelOpen ? "active" : ""}
                  type="button"
                  onClick={openAiPanel}
                  disabled={!activeChannel}
                  aria-label={t("channel.showAiPanel")}
                  title={t("channel.showAiPanel")}
                >
                  <Bot size={16} />
                  <span>{t("channel.aiMember")}</span>
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

          {isAiPanelOpen && (
            <>
              <button
                aria-label={t("channel.hideAiPanel")}
                className="drawer-backdrop channel-drawer-backdrop"
                onClick={closeAiPanel}
                type="button"
              />
              <aside id="channel-ai-drawer" className="panel channel-inspector channel-drawer" aria-label={t("channel.aiPanel")}>
                <div className="drawer-title-row">
                  <div className="ai-member-card">
                    <div className="ai-member-icon"><Bot size={19} /></div>
                    <div>
                      <strong>{t("channel.aiMember")}</strong>
                      <p>{activeChannel ? `#${activeChannel.name}` : t("channel.selectChannel")}</p>
                    </div>
                  </div>
                  <div className="ai-workbench-status" aria-label={t("channel.aiWorkbenchStatus")}>
                    <span className={selectedAttachmentIndexed ? "ready" : "neutral"}>
                      {selectedAttachment
                        ? (selectedAttachmentIndexed ? t("channel.sourceReady") : t("channel.sourceBlocked"))
                        : t("channel.sourceChannel")}
                    </span>
                    <span>{t("channel.recentOutputCount", { count: recentChannelOutputs.length })}</span>
                  </div>
                  <button
                    aria-label={t("channel.hideAiPanel")}
                    className="icon-button drawer-close-button"
                    onClick={closeAiPanel}
                    title={t("channel.hideAiPanel")}
                    type="button"
                  >
                    <X size={16} />
                  </button>
                </div>

                <div className="ai-workbench-intro">
                  <strong>{t("channel.aiWorkbenchTitle")}</strong>
                  <p>{t("channel.aiMemberHelp")}</p>
                </div>

                {selectedAiMessage && (
                  <div className="inspector-section ai-response-section">
                    <div className="inspector-heading">
                      <strong>{t("channel.fullAiResponse")}</strong>
                      <span>{asArray(selectedAiMessage.sources).length}</span>
                    </div>
                    <div className="ai-response-detail">
                      <MarkdownContent>{selectedAiMessage.text}</MarkdownContent>
                      {asArray(selectedAiMessage.sources).length > 0 && (
                        <div className="message-sources expanded">
                          <span>{t("channel.sources")}</span>
                          {asArray(selectedAiMessage.sources).map((source) => <small key={source}>{source}</small>)}
                        </div>
                      )}
                    </div>
                  </div>
                )}

                <div className="ai-action-groups">
                  {aiActionGroups.map((group) => (
                    <section className="ai-action-group" key={group.key}>
                      <div className="inspector-heading">
                        <strong>{group.label}</strong>
                      </div>
                      <div className="ai-command-card-grid">
                        {group.actions.map((action) => {
                          const Icon = action.icon;
                          const disabledReason = getAiActionDisabledReason(action);
                          return (
                            <button
                              key={action.key}
                              type="button"
                              className={`ai-command-card ai-command-card-${action.tone || action.key}`}
                              data-ai-action={action.key}
                              onClick={() => runAiCommand(action.command, selectedAttachmentId)}
                              disabled={isAiActionDisabled(action)}
                              title={disabledReason || action.label}
                            >
                              <span className="ai-command-icon"><Icon size={16} /></span>
                              <span className="ai-command-copy">
                                <strong>{action.label}</strong>
                                <small>{action.output} · {action.approval}</small>
                                {disabledReason && <em>{disabledReason}</em>}
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    </section>
                  ))}
                </div>

                <div className="inspector-section source-readiness-section">
                  <div className="inspector-heading">
                    <strong>{t("channel.sourceReadiness")}</strong>
                  </div>
                  {renderSourceReadiness()}
                </div>

                <div className="inspector-section recent-output-section">
                  <div className="inspector-heading">
                    <strong>{t("channel.recentOutputs")}</strong>
                    <span>{recentChannelOutputs.length}</span>
                  </div>
                  {recentChannelOutputs.length === 0 ? (
                    <p className="muted-small">{t("channel.noRecentOutputs")}</p>
                  ) : (
                    <div className="recent-output-list">
                      {recentChannelOutputs.map((artifact) => (
                        <button
                          className="recent-output-item"
                          type="button"
                          key={artifact.id}
                          onClick={() => downloadAttachment(artifact)}
                        >
                          <FileText size={15} />
                          <span>
                            <strong>{artifact.fileName}</strong>
                            <small>{artifact.contentType || formatFileSize(artifact.sizeBytes)}</small>
                          </span>
                          <Download size={14} />
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                <div className="inspector-section attachment-context-section">
                  <div className="inspector-heading">
                    <strong>{t("channel.attachments")}</strong>
                    <span>{attachments.length}</span>
                  </div>
                  {attachmentLoading && <p className="muted-small">{t("channel.loadingAttachments")}</p>}
                  {!attachmentLoading && attachments.length === 0 && <p className="muted-small">{t("channel.noAttachments")}</p>}
                  <div className="attachment-list polished-attachment-list">
                    {attachments.map((attachment) => {
                      const isImage = attachment.contentType.startsWith("image/");
                      const isSelected = attachment.id === selectedAttachmentId;
                      return (
                        <div
                          key={attachment.id}
                          className={`attachment-item ai-attachment-card ${isSelected ? "selected" : ""}`}
                        >
                          <button
                            type="button"
                            className="attachment-select-button"
                            onClick={() => setSelectedAttachmentId(attachment.id)}
                          >
                            <span className="attachment-file-badge" aria-hidden="true">
                              {isImage ? <Image size={16} /> : <FileText size={16} />}
                            </span>
                            <span className="attachment-copy">
                              <strong>{attachment.fileName}</strong>
                              <small>
                                {getAttachmentLabel(attachment, t)} · {formatFileSize(attachment.sizeBytes)} · {attachment.time}
                              </small>
                            </span>
                            {isSelected && <em className="selected-attachment-label">Selected</em>}
                          </button>
                          <button
                            type="button"
                            className="delete-attachment-button polished-delete-button"
                            onClick={(e) => {
                              e.stopPropagation();
                              deleteAttachment(attachment.id);
                            }}
                            aria-label={t("channel.deleteFile")}
                            title={t("channel.deleteFile")}
                          >
                            <Trash2 size={14} />
                          </button>
                        </div>
                      );
                    })}
                  </div>
                </div>
              </aside>
            </>
          )}

          {isMembersPanelOpen && (
            <>
              <button
                aria-label={t("channel.closeMembers")}
                className="drawer-backdrop member-drawer-backdrop"
                onClick={closeMembersPanel}
                type="button"
              />
              <aside
                id="channel-members-drawer"
                aria-label={t("channel.channelMembers")}
                aria-modal="true"
                className="panel member-drawer"
                onKeyDown={handleMembersDrawerKeyDown}
                ref={membersDrawerRef}
                role="dialog"
                tabIndex={-1}
              >
                <div className="drawer-title-row">
                  <div>
                    <p className="drawer-kicker">{activeChannel ? `# ${activeChannel.name}` : workspaceName}</p>
                    <h3>{t("channel.channelMembers")}</h3>
                  </div>
                  <button
                    aria-label={t("channel.closeMembers")}
                    className="icon-button drawer-close-button"
                    onClick={closeMembersPanel}
                    title={t("channel.closeMembers")}
                    type="button"
                  >
                    <X size={16} />
                  </button>
                </div>

                <div className="member-manager-header drawer-toolbar">
                  <strong>{activeChannel ? t("channel.members", { count: activeChannel.memberCount }) : workspaceName}</strong>
                  <button className="secondary-button compact" type="button" onClick={() => reloadChannelMembers()}>
                    <RefreshCw size={14} /> {t("channel.refreshMembers")}
                  </button>
                </div>

                {memberLoading && <p className="muted-small">{t("channel.loadingMembers")}</p>}
                {!memberLoading && channelMembers.length === 0 && <p className="muted-small">{t("channel.noMembersLoaded")}</p>}
                <div className="member-list drawer-member-list">
                  {channelMembers.map((member) => {
                    const isMenuOpen = openMemberMenuId === member.userId;
                    const menuId = `member-menu-${member.userId}`;

                    return (
                      <div className="member-row compact" key={member.userId}>
                        <Avatar
                          className="mini-avatar member-row-avatar"
                          seed={member.userId || member.email}
                          name={member.name}
                          ariaHidden
                        />
                        <div className="member-row-copy">
                          <strong>{member.name}</strong>
                          <span>{member.email}</span>
                        </div>
                        <div className="member-actions">
                          <button
                            aria-controls={menuId}
                            aria-expanded={isMenuOpen}
                            aria-haspopup="menu"
                            aria-label={t("channel.memberActions", { name: member.name })}
                            className="icon-button member-menu-button"
                            disabled={memberSaving}
                            onClick={() => setOpenMemberMenuId((current) => (current === member.userId ? "" : member.userId))}
                            onKeyDown={(event) => handleMemberMenuButtonKeyDown(event, member)}
                            title={t("channel.memberActions", { name: member.name })}
                            type="button"
                          >
                            <MoreHorizontal size={16} />
                          </button>
                          {isMenuOpen && (
                            <div className="member-menu" id={menuId} role="menu">
                              <button
                                className="member-menu-danger"
                                id={`member-menu-remove-${member.userId}`}
                                onClick={() => confirmRemoveChannelMember(member)}
                                role="menuitem"
                                type="button"
                              >
                                <Trash2 size={14} /> {t("channel.removeMember")}
                              </button>
                            </div>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>

                <form className="member-add-row compact-add" onSubmit={addChannelMember}>
                  <label className="sr-only" htmlFor="channel-member-email">{t("channel.memberEmail")}</label>
                  <input
                    id="channel-member-email"
                    value={memberEmail}
                    onChange={(event) => setMemberEmail(event.target.value)}
                    placeholder={t("channel.memberEmailPlaceholder")}
                  />
                  <button className="primary-button compact" type="submit" disabled={memberSaving || !memberEmail.trim()}>
                    <UserPlus size={15} /> {memberSaving ? t("channel.savingMember") : t("channel.addMember")}
                  </button>
                </form>
              </aside>
            </>
          )}
        </section>
      )}
    </div>
  );
}

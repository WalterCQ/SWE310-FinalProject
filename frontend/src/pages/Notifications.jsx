import { useEffect, useMemo, useState } from "react";
import { BellRing, CheckCircle2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { formatApiError, notifications as notificationsApi } from "../api/taskflowApi.js";
import { asArray, mapNotification } from "../api/mappers.js";
import { enumNotificationKey, useI18n } from "../i18n.jsx";

function getNotificationDestination(notification) {
  const type = String(notification.typeLabel || notification.type || "").trim().toLowerCase();

  if (type === "message") return "/channels";
  if (type === "task" || type === "reminder") return "/tasks?view=mine";

  return "/dashboard";
}

const notificationTitleKeys = {
  "Task overdue": "notifications.system.taskOverdueTitle",
  "Task reminder": "notifications.system.taskReminderTitle",
  "Project deadline risk": "notifications.system.projectDeadlineRiskTitle",
  "AI risk summary available": "notifications.seed.aiRiskSummaryTitle",
  "Presentation reminder": "notifications.seed.presentationReminderTitle",
  "Task assigned for final demo": "notifications.seed.finalDemoTaskTitle",
  "New message in #demo-chat": "notifications.seed.newMessageDemoChatTitle",
  "Workspace demo data loaded": "notifications.seed.workspaceDemoDataLoadedTitle",
  "Demo workspace is ready": "notifications.seed.demoWorkspaceReadyTitle",
  "Presentation checklist due soon": "notifications.seed.presentationChecklistTitle",
  "AI project summary has new context": "notifications.seed.aiProjectSummaryContextTitle",
};

const notificationMessageKeys = {
  "The AI assistant can summarize project progress and overdue risk from current task data.":
    "notifications.seed.aiRiskSummaryMessage",
  "Record dashboard, tasks, channels, AI assistant, and notifications before submission.":
    "notifications.seed.presentationReminderMessage",
  "A teammate posted the next validation step in the demo chat channel.":
    "notifications.seed.newMessageDemoChatMessage",
  "Projects, task board, channels, and dashboard charts are ready for the frontend demo.":
    "notifications.seed.workspaceDemoDataLoadedMessage",
  "Open Dashboard, Projects, Tasks, Channels, AI Assistant, and Notifications to show live Azure data.":
    "notifications.seed.demoWorkspaceReadyMessage",
  "Record the dashboard, task creation, chat, notification read state, and AI project summary.":
    "notifications.seed.presentationChecklistMessage",
  "The AI demo project now includes backend, task, and channel records for grounded summaries.":
    "notifications.seed.aiProjectSummaryContextMessage",
};

function translateNotificationTitle(title, t) {
  const value = String(title || "").trim();
  const key = notificationTitleKeys[value];
  return key ? t(key) : value;
}

function translateNotificationMessage(message, t) {
  const value = String(message || "").trim();
  if (!value) return "";

  const key = notificationMessageKeys[value];
  if (key) return t(key);

  const assignedMatch = value.match(/^A walkthrough task is assigned to (.+)\.$/);
  if (assignedMatch) {
    return t("notifications.seed.finalDemoTaskMessage", { email: assignedMatch[1] });
  }

  const needsAttentionMatch = value.match(/^(.+) in (.+) needs attention\.$/);
  if (needsAttentionMatch) {
    return t("notifications.system.taskNeedsAttentionMessage", {
      task: needsAttentionMatch[1],
      project: needsAttentionMatch[2],
    });
  }

  const deadlineRiskMatch = value.match(/^(.+) is near its deadline with ([0-9.]+)% completion\.$/);
  if (deadlineRiskMatch) {
    return t("notifications.system.projectDeadlineRiskMessage", {
      project: deadlineRiskMatch[1],
      completion: deadlineRiskMatch[2],
    });
  }

  return value;
}

function compactNotifications(notifications) {
  const groups = new Map();

  notifications.forEach((notification) => {
    const key = [
      notification.workspaceId || "",
      notification.typeLabel,
      notification.title,
      notification.message,
    ].join("|");
    const existing = groups.get(key);

    if (!existing) {
      groups.set(key, {
        ...notification,
        ids: [notification.id],
        unreadIds: notification.isRead ? [] : [notification.id],
        isRead: Boolean(notification.isRead),
      });
      return;
    }

    existing.ids.push(notification.id);

    if (!notification.isRead) {
      existing.unreadIds.push(notification.id);
      existing.isRead = false;
    }
  });

  return Array.from(groups.values());
}

export default function Notifications() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [openingId, setOpeningId] = useState("");
  const [error, setError] = useState("");
  const compactedNotifications = useMemo(() => compactNotifications(notifications), [notifications]);
  const unreadCount = useMemo(
    () => compactedNotifications.filter((notification) => !notification.isRead).length,
    [compactedNotifications]
  );

  useEffect(() => {
    let active = true;

    async function load() {
      setLoading(true);
      setError("");

      try {
        const data = await notificationsApi.list();
        if (active) setNotifications(asArray(data).map(mapNotification));
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoading(false);
      }
    }

    load();

    return () => {
      active = false;
    };
  }, []);

  async function markAllAsRead() {
    if (unreadCount === 0) return;

    setSaving(true);
    setError("");

    try {
      await notificationsApi.markAllRead();
      setNotifications((current) => current.map((notification) => ({ ...notification, isRead: true })));
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  async function markNotificationAsRead(notification) {
    const unreadIds = notification.unreadIds?.length > 0
      ? notification.unreadIds
      : notification.isRead ? [] : [notification.id];

    if (unreadIds.length === 0) return true;

    setOpeningId(notification.id);
    setError("");

    try {
      await Promise.all(unreadIds.map((id) => notificationsApi.markRead(id)));
      setNotifications((current) => current.map((item) => unreadIds.includes(item.id)
        ? { ...item, isRead: true }
        : item));
      return true;
    } catch (apiError) {
      setError(formatApiError(apiError));
      return false;
    } finally {
      setOpeningId("");
    }
  }

  async function openNotification(notification) {
    const read = await markNotificationAsRead(notification);
    if (!read) return;

    navigate(getNotificationDestination(notification));
  }

  return (
    <div className="page-stack">
      <div className="page-actions notification-actions">
        <div className="notification-summary">
          <strong>{t("notifications.unreadCount", { count: unreadCount })}</strong>
          <span>{t("notifications.totalCount", { count: compactedNotifications.length })}</span>
        </div>
        <button
          className="secondary-button"
          onClick={markAllAsRead}
          disabled={loading || saving || unreadCount === 0}
        >
          {saving ? t("notifications.saving") : t("notifications.markAll")}
        </button>
      </div>

      <section className="panel">
        {loading && <p>{t("notifications.loading")}</p>}
        {error && <div><strong>{t("notifications.unableLoad")}</strong><p>{error}</p></div>}
        {!loading && !error && notifications.length === 0 && <p>{t("notifications.empty")}</p>}

        {!loading && !error && notifications.length > 0 && (
          <div className="notification-list">
            {compactedNotifications.map((notification) => {
              const title = translateNotificationTitle(notification.title, t);
              const message = translateNotificationMessage(notification.message, t)
                || t(enumNotificationKey(notification.typeLabel));

              return (
                <button
                  aria-label={t("notifications.openAria", { title })}
                  className={`notification-row ${notification.isRead ? "read" : "unread"}`}
                  disabled={openingId === notification.id}
                  key={notification.id}
                  onClick={() => openNotification(notification)}
                  type="button"
                >
                  <div className="notification-icon">
                    {notification.isRead ? <CheckCircle2 size={18} /> : <BellRing size={18} />}
                  </div>
                  <div className="notification-copy">
                    <h4>{title}</h4>
                    <p>{message}</p>
                  </div>
                  <div className="notification-meta">
                    <span className="notification-type">{t(enumNotificationKey(notification.typeLabel))}</span>
                    <span className="notification-status">
                      {notification.isRead ? t("notifications.read") : t("notifications.unread")}
                    </span>
                    <time>{notification.time || t("notifications.openHint")}</time>
                  </div>
                </button>
              );
            })}
          </div>
        )}
      </section>
    </div>
  );
}

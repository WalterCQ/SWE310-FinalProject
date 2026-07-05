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
            {compactedNotifications.map((notification) => (
              <button
                aria-label={t("notifications.openAria", { title: notification.title })}
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
                  <h4>{notification.title}</h4>
                  <p>{notification.message || t(enumNotificationKey(notification.typeLabel))}</p>
                </div>
                <div className="notification-meta">
                  <span className="notification-type">{t(enumNotificationKey(notification.typeLabel))}</span>
                  <span className="notification-status">
                    {notification.isRead ? t("notifications.read") : t("notifications.unread")}
                  </span>
                  <time>{notification.time || t("notifications.openHint")}</time>
                </div>
              </button>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

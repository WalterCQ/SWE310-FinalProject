import { useEffect, useState } from "react";
import { BellRing } from "lucide-react";
import { formatApiError, notifications as notificationsApi } from "../api/taskflowApi.js";
import { asArray, mapNotification } from "../api/mappers.js";
import { enumNotificationKey, useI18n } from "../i18n.jsx";

export default function Notifications() {
  const { t } = useI18n();
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  async function loadNotifications() {
    setLoading(true);
    setError("");

    try {
      const data = await notificationsApi.list();
      setNotifications(asArray(data).map(mapNotification));
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setLoading(false);
    }
  }

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
    const unreadNotifications = notifications.filter((notification) => !notification.isRead);
    if (unreadNotifications.length === 0) return;

    setSaving(true);
    setError("");

    try {
      await Promise.all(unreadNotifications.map((notification) => notificationsApi.markRead(notification.id)));
      await loadNotifications();
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">{t("notifications.eyebrow")}</p>
          <h1>{t("notifications.title")}</h1>
        </div>
        <button
          className="secondary-button"
          onClick={markAllAsRead}
          disabled={loading || saving || notifications.length === 0}
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
            {notifications.map((notification) => (
              <div className="notification-row" key={notification.id}>
                <div className="notification-icon"><BellRing size={18} /></div>
                <div>
                  <h4>{notification.title}</h4>
                  <p>{notification.message || t(enumNotificationKey(notification.typeLabel))}</p>
                </div>
                <span>{notification.time || t(enumNotificationKey(notification.typeLabel))}</span>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

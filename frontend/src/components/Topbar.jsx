import { Bell, CalendarDays, ClipboardList, Globe2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import GlobalSearch from "./GlobalSearch.jsx";
import { LOCALES, useI18n } from "../i18n.jsx";

export default function Topbar({ pageMeta }) {
  const { locale, setLocale, t } = useI18n();
  const navigate = useNavigate();

  return (
    <header className="topbar">
      <div className="topbar-title">
        <p className="eyebrow">{pageMeta?.eyebrow || t("topbar.eyebrow")}</p>
        <h2>{pageMeta?.title || t("topbar.title")}</h2>
      </div>

      <div className="topbar-actions">
        <GlobalSearch />
        <label className="language-control" aria-label={t("topbar.language")}>
          <Globe2 size={18} />
          <select value={locale} onChange={(event) => setLocale(event.target.value)}>
            <option value={LOCALES.en}>{t("topbar.language.english")}</option>
            <option value={LOCALES.zh}>{t("topbar.language.chinese")}</option>
            <option value={LOCALES.tg}>{t("topbar.language.tajik")}</option>
          </select>
        </label>
        <button
          className="icon-button"
          aria-label={t("topbar.myTasks")}
          title={t("topbar.myTasks")}
          type="button"
          onClick={() => navigate("/tasks?view=mine")}
        >
          <ClipboardList size={18} />
        </button>
        <button
          className="icon-button notification-dot"
          aria-label={t("topbar.notifications")}
          title={t("topbar.notifications")}
          type="button"
          onClick={() => navigate("/notifications")}
        >
          <Bell size={18} />
        </button>
        <button className="week-button" type="button" onClick={() => navigate("/dashboard")}>
          <CalendarDays size={18} /> {t("topbar.week")}
        </button>
      </div>
    </header>
  );
}

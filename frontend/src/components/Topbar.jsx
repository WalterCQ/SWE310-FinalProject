import { Bell, CalendarDays, Search, ClipboardList, Globe2 } from "lucide-react";
import { LOCALES, useI18n } from "../i18n.jsx";

export default function Topbar() {
  const { locale, setLocale, t } = useI18n();

  return (
    <header className="topbar">
      <div>
        <p className="eyebrow">{t("topbar.eyebrow")}</p>
        <h2>{t("topbar.title")}</h2>
      </div>

      <div className="topbar-actions">
        <div className="search-box">
          <Search size={18} />
          <input placeholder={t("topbar.search")} />
        </div>
        <label className="language-control" aria-label={t("topbar.language")}>
          <Globe2 size={18} />
          <select value={locale} onChange={(event) => setLocale(event.target.value)}>
            <option value={LOCALES.en}>English</option>
            <option value={LOCALES.zh}>中文</option>
          </select>
        </label>
        <button className="icon-button" aria-label={t("topbar.checklist")}><ClipboardList size={18} /></button>
        <button className="icon-button notification-dot" aria-label={t("topbar.notifications")}><Bell size={18} /></button>
        <button className="week-button"><CalendarDays size={18} /> {t("topbar.week")}</button>
      </div>
    </header>
  );
}

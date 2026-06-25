import { Bell, CalendarDays, Search, ClipboardList } from "lucide-react";

export default function Topbar() {
  return (
    <header className="topbar">
      <div>
        <p className="eyebrow">TaskFlow / SWE310</p>
        <h2>Project management tool for the Week 15 demo.</h2>
      </div>

      <div className="topbar-actions">
        <div className="search-box">
          <Search size={18} />
          <input placeholder="Search task, owner, channel..." />
        </div>
        <button className="icon-button" aria-label="Checklist"><ClipboardList size={18} /></button>
        <button className="icon-button notification-dot" aria-label="Notifications"><Bell size={18} /></button>
        <button className="week-button"><CalendarDays size={18} /> Week 15</button>
      </div>
    </header>
  );
}

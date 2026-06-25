import { BellRing } from "lucide-react";
import { notifications } from "../data/mockData.js";

export default function Notifications() {
  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Notifications</p>
          <h1>Only the alerts that change the plan</h1>
        </div>
        <button className="secondary-button">Mark all as read</button>
      </div>

      <section className="panel">
        <div className="notification-list">
          {notifications.map((notification) => (
            <div className="notification-row" key={notification.id}>
              <div className="notification-icon"><BellRing size={18} /></div>
              <div>
                <h4>{notification.title}</h4>
                <p>{notification.type}</p>
              </div>
              <span>{notification.time}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

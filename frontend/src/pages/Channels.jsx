import { Hash, Send } from "lucide-react";
import { messages } from "../data/mockData.js";

export default function Channels() {
  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Channels</p>
          <h1>Team chat</h1>
        </div>
      </div>

      <section className="chat-layout">
        <aside className="panel channel-list">
          {["general", "frontend", "backend", "ai-plugin", "demo-video"].map((channel) => (
            <button key={channel} className={channel === "general" ? "active" : ""}>
              <Hash size={16} /> {channel}
            </button>
          ))}
        </aside>

        <article className="panel chat-panel">
          <div className="panel-header">
            <h3># general</h3>
            <span>4 members online</span>
          </div>
          <div className="message-list">
            {messages.map((message, index) => (
              <div className="message-row" key={message.id}>
                <div className="activity-index message-index">{String(index + 1).padStart(2, "0")}</div>
                <div>
                  <strong>{message.sender} <span>{message.time}</span></strong>
                  <p>{message.text}</p>
                </div>
              </div>
            ))}
          </div>
          <div className="message-input">
            <input placeholder="Message #general" />
            <button><Send size={18} /></button>
          </div>
        </article>
      </section>
    </div>
  );
}

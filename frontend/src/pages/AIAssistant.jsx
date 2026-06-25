import { Bot, Send, ClipboardCheck } from "lucide-react";

export default function AIAssistant() {
  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">AI Assistant</p>
          <h1>AI project summary</h1>
        </div>
      </div>

      <section className="ai-layout staggered-ai-layout">
        <article className="panel ai-card ai-summary-card">
          <div className="ai-icon"><Bot size={42} /></div>
          <h3>Current summary</h3>
          <p>
            TaskFlow Connect has 3 active workstreams. The main risk is not design; it is field mismatch between frontend forms and backend response names.
          </p>
          <div className="recommendation-box">
            <ClipboardCheck size={18} />
            <span>Next action: confirm status, priority, dueDate, projectId, and assigneeId before renaming frontend fields.</span>
          </div>
        </article>

        <article className="panel ai-chat ai-chat-card">
          <div className="message-row assistant">
            <div className="notification-icon ai-chat-icon">AI</div>
            <div>
              <strong>TaskFlow AI</strong>
              <p>Ask for project summaries, overdue work, risk lists, and demo talking points.</p>
            </div>
          </div>
          <div className="message-row user-message">
            <p>Summarize active projects and list the blockers before the presentation.</p>
          </div>
          <div className="message-input">
            <input placeholder="Ask about projects, tasks, owners, or deadlines..." />
            <button><Send size={18} /></button>
          </div>
        </article>
      </section>
    </div>
  );
}

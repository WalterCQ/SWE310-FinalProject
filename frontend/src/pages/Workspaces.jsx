import { Plus, FolderKanban, Users } from "lucide-react";
import { workspaces } from "../data/mockData.js";

export default function Workspaces() {
  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Workspaces</p>
          <h1>Workspaces</h1>
        </div>
        <button className="primary-button small"><Plus size={18} /> New Workspace</button>
      </div>

      <section className="workspace-grid">
        {workspaces.map((workspace, index) => (
          <article className={`panel workspace-card ${workspace.color}`} key={workspace.id}>
            <div className="workspace-card-top">
              <div className="workspace-icon">{String(index + 1).padStart(2, "0")}</div>
              <span className="workspace-tag">Active</span>
            </div>
            <h3>{workspace.name}</h3>
            <p>{workspace.projects} projects • {workspace.members} members</p>
            <div className="workspace-meta-row">
              <span><FolderKanban size={14} /> {workspace.projects} projects</span>
              <span><Users size={14} /> {workspace.members} members</span>
            </div>
          </article>
        ))}
      </section>
    </div>
  );
}

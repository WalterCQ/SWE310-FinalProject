import { useEffect, useState } from "react";
import { Plus, FolderKanban, Users } from "lucide-react";
import { formatApiError, workspaces as workspacesApi } from "../api/taskflowApi.js";
import { asArray, mapWorkspace } from "../api/mappers.js";

const colors = ["amber", "green", "red", "yellow"];

export default function Workspaces() {
  const [workspaces, setWorkspaces] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;

    async function loadWorkspaces() {
      setLoading(true);
      setError("");

      try {
        const data = await workspacesApi.list();
        if (active) {
          setWorkspaces(asArray(data).map(mapWorkspace));
        }
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoading(false);
      }
    }

    loadWorkspaces();

    return () => {
      active = false;
    };
  }, []);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Workspaces</p>
          <h1>Workspaces</h1>
        </div>
        <button className="primary-button small"><Plus size={18} /> New Workspace</button>
      </div>

      {loading && <section className="panel">Loading workspaces from Azure...</section>}
      {error && <section className="panel"><strong>Unable to load workspaces.</strong><p>{error}</p></section>}
      {!loading && !error && workspaces.length === 0 && (
        <section className="panel">No workspaces found.</section>
      )}

      {!loading && !error && workspaces.length > 0 && (
        <section className="workspace-grid">
          {workspaces.map((workspace, index) => (
            <article className={`panel workspace-card ${colors[index % colors.length]}`} key={workspace.id}>
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
      )}
    </div>
  );
}

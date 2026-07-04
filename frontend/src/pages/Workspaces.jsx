import { useEffect, useState } from "react";
import { Plus, FolderKanban, Users } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import { formatApiError, workspaces as workspacesApi } from "../api/taskflowApi.js";
import { asArray, mapWorkspace } from "../api/mappers.js";

const colors = ["amber", "green", "red", "yellow"];
const blankForm = { name: "", description: "" };

export default function Workspaces() {
  const [workspaces, setWorkspaces] = useState([]);
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState(blankForm);
  const [formErrors, setFormErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [createError, setCreateError] = useState("");

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

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setFormErrors({ ...formErrors, [event.target.name]: "" });
    setCreateError("");
  }

  function validateForm() {
    const nextErrors = {};
    if (!form.name.trim()) nextErrors.name = "Please enter a workspace name.";

    setFormErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function createWorkspace(event) {
    event.preventDefault();
    if (!validateForm()) return;

    setSaving(true);
    setCreateError("");

    try {
      const createdWorkspace = await workspacesApi.create({
        name: form.name.trim(),
        description: form.description.trim() || null,
      });

      setWorkspaces((current) => [...current, mapWorkspace(createdWorkspace)]
        .sort((left, right) => left.name.localeCompare(right.name)));
      setForm(blankForm);
      setFormOpen(false);
    } catch (apiError) {
      setCreateError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Workspaces</p>
          <h1>Workspaces</h1>
        </div>
        <button
          className="primary-button small"
          type="button"
          onClick={() => {
            setFormOpen((open) => !open);
            setCreateError("");
          }}
        >
          <Plus size={18} /> New Workspace
        </button>
      </div>

      {formOpen && (
        <section className="panel create-panel">
          <div className="panel-header">
            <h3>Create workspace</h3>
            <span>Creates a real Azure workspace for the signed-in account.</span>
          </div>

          {createError && <div className="error-text"><strong>Unable to create workspace.</strong> {createError}</div>}

          <form className="task-form" onSubmit={createWorkspace} noValidate>
            <label>
              Workspace name
              <input
                name="name"
                value={form.name}
                onChange={updateField}
                placeholder="Example: Coursework team"
              />
              <ErrorMessage>{formErrors.name}</ErrorMessage>
            </label>

            <label>
              Description
              <textarea
                name="description"
                value={form.description}
                onChange={updateField}
                placeholder="What is this workspace for?"
              />
            </label>

            <div className="button-row">
              <button className="primary-button" type="submit" disabled={saving}>
                <Plus size={18} /> {saving ? "Creating..." : "Create workspace"}
              </button>
              <button
                className="secondary-button"
                type="button"
                onClick={() => {
                  setFormOpen(false);
                  setForm(blankForm);
                  setFormErrors({});
                  setCreateError("");
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        </section>
      )}

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

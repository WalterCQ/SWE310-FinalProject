import { useEffect, useMemo, useState } from "react";
import { Plus, Search } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import StatusBadge from "../components/StatusBadge.jsx";
import {
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject, mapWorkspace } from "../api/mappers.js";

const blankForm = {
  workspaceId: "",
  name: "",
  description: "",
  dueDate: "",
};

function toDeadlineUtc(dateValue) {
  if (!dateValue) return null;
  return new Date(`${dateValue}T00:00:00.000Z`).toISOString();
}

export default function Projects() {
  const [workspaces, setWorkspaces] = useState([]);
  const [projects, setProjects] = useState([]);
  const [search, setSearch] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState(blankForm);
  const [formErrors, setFormErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [createError, setCreateError] = useState("");

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setLoading(true);
      setError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list());
        const mappedWorkspaces = workspaceItems.map(mapWorkspace);
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));

        if (active) {
          setWorkspaces(mappedWorkspaces);
          setProjects(mappedProjects);
          setForm((current) => {
            const hasWorkspace = mappedWorkspaces.some((workspace) => workspace.id === current.workspaceId);
            return hasWorkspace ? current : { ...current, workspaceId: mappedWorkspaces[0]?.id || "" };
          });
        }
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoading(false);
      }
    }

    loadProjects();

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
    if (!form.workspaceId) nextErrors.workspaceId = "Please select a workspace.";
    if (!form.name.trim()) nextErrors.name = "Please enter a project name.";

    setFormErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function createProject(event) {
    event.preventDefault();
    if (!validateForm()) return;

    setSaving(true);
    setCreateError("");

    try {
      const createdProject = await projectsApi.create(form.workspaceId, {
        name: form.name.trim(),
        description: form.description.trim() || null,
        deadlineUtc: toDeadlineUtc(form.dueDate),
      });
      const workspaceId = form.workspaceId;

      setProjects((current) => [...current, mapProject(createdProject)]
        .sort((left, right) => left.name.localeCompare(right.name)));
      setForm({ ...blankForm, workspaceId });
      setFormOpen(false);
    } catch (apiError) {
      setCreateError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  const visibleProjects = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return projects;

    return projects.filter((project) => {
      return [project.name, project.owner, project.statusLabel]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(query));
    });
  }, [projects, search]);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Projects</p>
          <h1>Every deadline needs an owner</h1>
        </div>
        <button
          className="primary-button small"
          type="button"
          onClick={() => {
            setFormOpen((open) => !open);
            setCreateError("");
          }}
        >
          <Plus size={18} /> New Project
        </button>
      </div>

      {formOpen && (
        <section className="panel create-panel">
          <div className="panel-header">
            <h3>Create project</h3>
            <span>Projects are created in the selected Azure workspace.</span>
          </div>

          {createError && <div className="error-text"><strong>Unable to create project.</strong> {createError}</div>}

          <form className="task-form" onSubmit={createProject} noValidate>
            <div className="form-grid-2">
              <label>
                Workspace
                <select
                  name="workspaceId"
                  value={form.workspaceId}
                  onChange={updateField}
                  disabled={loading || workspaces.length === 0}
                >
                  {workspaces.length === 0 && <option value="">No workspace available</option>}
                  {workspaces.map((workspace) => (
                    <option key={workspace.id} value={workspace.id}>{workspace.name}</option>
                  ))}
                </select>
                <ErrorMessage>{formErrors.workspaceId}</ErrorMessage>
              </label>

              <label>
                Due date
                <input name="dueDate" type="date" value={form.dueDate} onChange={updateField} />
              </label>
            </div>

            <label>
              Project name
              <input
                name="name"
                value={form.name}
                onChange={updateField}
                placeholder="Example: Presentation prep"
              />
              <ErrorMessage>{formErrors.name}</ErrorMessage>
            </label>

            <label>
              Description
              <textarea
                name="description"
                value={form.description}
                onChange={updateField}
                placeholder="What should this project deliver?"
              />
            </label>

            <div className="button-row">
              <button className="primary-button" type="submit" disabled={saving || loading || workspaces.length === 0}>
                <Plus size={18} /> {saving ? "Creating..." : "Create project"}
              </button>
              <button
                className="secondary-button"
                type="button"
                onClick={() => {
                  setFormOpen(false);
                  setForm({ ...blankForm, workspaceId: form.workspaceId });
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

      <section className="panel">
        <div className="toolbar">
          <div className="search-box inline">
            <Search size={18} />
            <input
              placeholder="Search by project or owner..."
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <button className="secondary-button">Filter</button>
        </div>

        {loading && <p>Loading projects from Azure...</p>}
        {error && <div><strong>Unable to load projects.</strong><p>{error}</p></div>}
        {!loading && !error && projects.length === 0 && <p>No projects found.</p>}
        {!loading && !error && projects.length > 0 && visibleProjects.length === 0 && <p>No projects match your search.</p>}

        {!loading && !error && visibleProjects.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Project</th>
                  <th>Status</th>
                  <th>Progress</th>
                  <th>Owner</th>
                  <th>Due date</th>
                </tr>
              </thead>
              <tbody>
                {visibleProjects.map((project) => (
                  <tr key={project.id}>
                    <td>{project.name}</td>
                    <td><StatusBadge>{project.statusLabel}</StatusBadge></td>
                    <td>
                      <div className="progress-cell">
                        <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                        <span>{project.progress}%</span>
                      </div>
                    </td>
                    <td>{project.owner}</td>
                    <td>{project.deadlineLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

import { useEffect, useState } from "react";
import { Plus, FolderKanban, Users } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import { formatApiError, workspaces as workspacesApi } from "../api/taskflowApi.js";
import { asArray, mapWorkspace } from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";

const colors = ["amber", "green", "red", "yellow"];
const blankForm = { name: "", description: "" };

export default function Workspaces() {
  const { t } = useI18n();
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
    if (!form.name.trim()) nextErrors.name = t("workspace.nameRequired");

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
          <p className="eyebrow">{t("workspace.eyebrow")}</p>
          <h1>{t("workspace.title")}</h1>
        </div>
        <button
          className="primary-button small"
          type="button"
          onClick={() => {
            setFormOpen((open) => !open);
            setCreateError("");
          }}
        >
          <Plus size={18} /> {t("workspace.new")}
        </button>
      </div>

      {formOpen && (
        <section className="panel create-panel">
          <div className="panel-header">
            <h3>{t("workspace.createTitle")}</h3>
            <span>{t("workspace.createHelp")}</span>
          </div>

          {createError && <div className="error-text"><strong>{t("workspace.unableCreate")}</strong> {createError}</div>}

          <form className="task-form" onSubmit={createWorkspace} noValidate>
            <label>
              {t("workspace.name")}
              <input
                name="name"
                value={form.name}
                onChange={updateField}
                placeholder={t("workspace.placeholder.name")}
              />
              <ErrorMessage>{formErrors.name}</ErrorMessage>
            </label>

            <label>
              {t("workspace.description")}
              <textarea
                name="description"
                value={form.description}
                onChange={updateField}
                placeholder={t("workspace.placeholder.description")}
              />
            </label>

            <div className="button-row">
              <button className="primary-button" type="submit" disabled={saving}>
                <Plus size={18} /> {saving ? t("workspace.creating") : t("workspace.create")}
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
                {t("workspace.cancel")}
              </button>
            </div>
          </form>
        </section>
      )}

      {loading && <section className="panel">{t("workspace.loading")}</section>}
      {error && <section className="panel"><strong>{t("workspace.unableLoad")}</strong><p>{error}</p></section>}
      {!loading && !error && workspaces.length === 0 && (
        <section className="panel">{t("workspace.empty")}</section>
      )}

      {!loading && !error && workspaces.length > 0 && (
        <section className="workspace-grid">
          {workspaces.map((workspace, index) => (
            <article className={`panel workspace-card ${colors[index % colors.length]}`} key={workspace.id}>
              <div className="workspace-card-top">
                <div className="workspace-icon">{String(index + 1).padStart(2, "0")}</div>
                <span className="workspace-tag">{t("workspace.active")}</span>
              </div>
              <h3>{workspace.name}</h3>
              <p>{t("workspace.projectsMembers", { projects: workspace.projects, members: workspace.members })}</p>
              <div className="workspace-meta-row">
                <span><FolderKanban size={14} /> {t("workspace.projects", { count: workspace.projects })}</span>
                <span><Users size={14} /> {t("workspace.members", { count: workspace.members })}</span>
              </div>
            </article>
          ))}
        </section>
      )}
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import { Plus, FolderKanban, Trash2, UserPlus, Users } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import CreateActionButton from "../components/CreateActionButton.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import FormModal from "../components/FormModal.jsx";
import { formatApiError, workspaces as workspacesApi } from "../api/taskflowApi.js";
import { asArray, mapWorkspace, mapWorkspaceMember } from "../api/mappers.js";
import { canDeleteWorkspace, canManageWorkspaceMembers, isWorkspaceAdmin } from "../api/permissions.js";
import { useI18n } from "../i18n.jsx";

const colors = ["amber", "green", "red", "yellow"];
const blankForm = { name: "", description: "" };
const roleOptions = [
  { value: "0", label: "Owner" },
  { value: "1", label: "Admin" },
  { value: "2", label: "Member" },
];

function blankMemberState() {
  return {
    items: [],
    email: "",
    role: "2",
    loading: false,
    saving: false,
    error: "",
  };
}

export default function Workspaces() {
  const { t } = useI18n();
  const [searchParams] = useSearchParams();
  const selectedWorkspaceId = searchParams.get("workspaceId") || "";
  const currentUserId = localStorage.getItem("userId") || "";
  const [workspaces, setWorkspaces] = useState([]);
  const [membersByWorkspace, setMembersByWorkspace] = useState({});
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState(blankForm);
  const [formErrors, setFormErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [createError, setCreateError] = useState("");
  const workspaceNameRef = useRef(null);

  useEffect(() => {
    let active = true;

    async function loadWorkspaces() {
      setLoading(true);
      setError("");

      try {
        const data = await workspacesApi.list();
        const mappedWorkspaces = asArray(data).map(mapWorkspace);
        const memberEntries = await Promise.all(
          mappedWorkspaces.map(async (workspace) => {
            try {
              const members = await workspacesApi.members(workspace.id);
              return [workspace.id, { ...blankMemberState(), items: asArray(members).map(mapWorkspaceMember) }];
            } catch (apiError) {
              return [workspace.id, { ...blankMemberState(), error: formatApiError(apiError) }];
            }
          })
        );

        if (active) {
          setWorkspaces(mappedWorkspaces);
          setMembersByWorkspace(Object.fromEntries(memberEntries));
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

  useEffect(() => {
    if (!selectedWorkspaceId || loading) return;

    document.getElementById(`workspace-${selectedWorkspaceId}`)?.scrollIntoView({
      block: "center",
      behavior: "smooth",
    });
  }, [selectedWorkspaceId, loading, workspaces]);

  function isCurrentUser(userId) {
    return Boolean(userId && currentUserId && String(userId).toLowerCase() === currentUserId.toLowerCase());
  }

  function getCurrentWorkspaceRole(workspaceId) {
    const state = membersByWorkspace[workspaceId] || blankMemberState();
    return state.items.find((member) => isCurrentUser(member.userId))?.role;
  }

  const canCreateWorkspace = Object.values(membersByWorkspace).some((state) =>
    state.items.some((member) => isCurrentUser(member.userId) && isWorkspaceAdmin(member.role))
  );

  useEffect(() => {
    if (!loading && !canCreateWorkspace && formOpen) {
      setFormOpen(false);
    }
  }, [canCreateWorkspace, formOpen, loading]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setFormErrors({ ...formErrors, [event.target.name]: "" });
    setCreateError("");
  }

  function closeCreateWorkspaceModal() {
    setFormOpen(false);
    setForm(blankForm);
    setFormErrors({});
    setCreateError("");
  }

  function updateMemberState(workspaceId, updates) {
    setMembersByWorkspace((current) => ({
      ...current,
      [workspaceId]: {
        ...blankMemberState(),
        ...(current[workspaceId] || {}),
        ...updates,
      },
    }));
  }

  async function reloadMembers(workspaceId) {
    updateMemberState(workspaceId, { loading: true, error: "" });

    try {
      const members = await workspacesApi.members(workspaceId);
      updateMemberState(workspaceId, { items: asArray(members).map(mapWorkspaceMember), loading: false });
    } catch (apiError) {
      updateMemberState(workspaceId, { error: formatApiError(apiError), loading: false });
    }
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

      const mappedWorkspace = mapWorkspace(createdWorkspace);
      setWorkspaces((current) => [...current, mappedWorkspace]
        .sort((left, right) => left.name.localeCompare(right.name)));
      setMembersByWorkspace((current) => ({ ...current, [mappedWorkspace.id]: blankMemberState() }));
      setForm(blankForm);
      setFormOpen(false);
      await reloadMembers(mappedWorkspace.id);
    } catch (apiError) {
      setCreateError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  async function addMember(workspaceId) {
    const state = membersByWorkspace[workspaceId] || blankMemberState();
    if (!state.email.trim()) {
      updateMemberState(workspaceId, { error: "Enter the email of a registered user." });
      return;
    }

    updateMemberState(workspaceId, { saving: true, error: "" });

    try {
      await workspacesApi.addMember(workspaceId, {
        email: state.email.trim(),
        role: Number(state.role),
      });
      updateMemberState(workspaceId, { email: "", role: "2" });
      await reloadMembers(workspaceId);
    } catch (apiError) {
      updateMemberState(workspaceId, { error: formatApiError(apiError) });
    } finally {
      updateMemberState(workspaceId, { saving: false });
    }
  }

  async function updateMemberRole(workspaceId, member, role) {
    updateMemberState(workspaceId, { saving: true, error: "" });

    try {
      await workspacesApi.updateMember(workspaceId, member.userId, { role: Number(role) });
      await reloadMembers(workspaceId);
    } catch (apiError) {
      updateMemberState(workspaceId, { error: formatApiError(apiError) });
    } finally {
      updateMemberState(workspaceId, { saving: false });
    }
  }

  async function removeMember(workspaceId, member) {
    updateMemberState(workspaceId, { saving: true, error: "" });

    try {
      await workspacesApi.removeMember(workspaceId, member.userId);
      await reloadMembers(workspaceId);
    } catch (apiError) {
      updateMemberState(workspaceId, { error: formatApiError(apiError) });
    } finally {
      updateMemberState(workspaceId, { saving: false });
    }
  }

  async function deleteWorkspace(workspace) {
    const confirmed = window.confirm(`Delete "${workspace.name}" and all of its projects, channels, tasks, and members?`);
    if (!confirmed) return;

    let deleted = false;
    updateMemberState(workspace.id, { saving: true, error: "" });

    try {
      await workspacesApi.remove(workspace.id);
      deleted = true;
      setWorkspaces((current) => current.filter((item) => item.id !== workspace.id));
      setMembersByWorkspace((current) => {
        const next = { ...current };
        delete next[workspace.id];
        return next;
      });
    } catch (apiError) {
      updateMemberState(workspace.id, { error: formatApiError(apiError) });
    } finally {
      if (!deleted) updateMemberState(workspace.id, { saving: false });
    }
  }

  return (
    <div className="page-stack">
      {canCreateWorkspace && (
        <div className="page-actions">
          <CreateActionButton
            ariaLabel={t("workspace.new")}
            onClick={() => {
              setFormOpen(true);
              setCreateError("");
            }}
          >
            {t("workspace.new")}
          </CreateActionButton>
        </div>
      )}

      <FormModal
        open={canCreateWorkspace && formOpen}
        title={t("workspace.createTitle")}
        description={t("workspace.createHelp")}
        onClose={closeCreateWorkspaceModal}
        closeLabel={t("workspace.closeCreate")}
        initialFocusRef={workspaceNameRef}
        size="lg"
      >
        {createError && <div className="error-text"><strong>{t("workspace.unableCreate")}</strong> {createError}</div>}

        <form className="task-form" onSubmit={createWorkspace} noValidate>
          <label>
            {t("workspace.name")}
            <input
              ref={workspaceNameRef}
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
            <button className="secondary-button" type="button" onClick={closeCreateWorkspaceModal}>
              {t("workspace.cancel")}
            </button>
          </div>
        </form>
      </FormModal>

      {loading && <section className="panel">{t("workspace.loading")}</section>}
      {error && <section className="panel"><strong>{t("workspace.unableLoad")}</strong><p>{error}</p></section>}
      {!loading && !error && workspaces.length === 0 && (
        <section className="panel">{t("workspace.empty")}</section>
      )}

      {!loading && !error && workspaces.length > 0 && (
        <section className="workspace-grid">
          {workspaces.map((workspace, index) => {
            const memberState = membersByWorkspace[workspace.id] || blankMemberState();
            const memberCount = memberState.items.length || workspace.members;
            const currentWorkspaceRole = getCurrentWorkspaceRole(workspace.id);
            const canManageMembers = canManageWorkspaceMembers(currentWorkspaceRole);
            const canDeleteCurrentWorkspace = canDeleteWorkspace(currentWorkspaceRole);

            return (
              <article
                className={`panel workspace-card ${colors[index % colors.length]} ${workspace.id === selectedWorkspaceId ? "target-highlight" : ""}`}
                id={`workspace-${workspace.id}`}
                key={workspace.id}
              >
                <div className="workspace-card-top">
                  <div className="workspace-icon">{String(index + 1).padStart(2, "0")}</div>
                  <span className="workspace-tag">{t("workspace.active")}</span>
                </div>
                <h3>{workspace.name}</h3>
                <p>{t("workspace.projectsMembers", { projects: workspace.projects, members: memberCount })}</p>
                <div className="workspace-meta-row">
                  <span><FolderKanban size={14} /> {t("workspace.projects", { count: workspace.projects })}</span>
                  <span><Users size={14} /> {t("workspace.members", { count: memberCount })}</span>
                </div>

                <div className="member-manager">
                  <div className="member-manager-header">
                    <strong>Workspace members</strong>
                    <div className="member-manager-actions">
                      {canDeleteCurrentWorkspace && (
                        <button
                          className="danger-button compact"
                          type="button"
                          onClick={() => deleteWorkspace(workspace)}
                          disabled={memberState.saving}
                        >
                          <Trash2 size={14} /> Delete workspace
                        </button>
                      )}
                      <button className="secondary-button compact" type="button" onClick={() => reloadMembers(workspace.id)}>
                        Refresh
                      </button>
                    </div>
                  </div>

                  {memberState.error && <div className="error-text">{memberState.error}</div>}
                  {memberState.loading && <p>Loading members...</p>}
                  {!memberState.loading && memberState.items.length === 0 && <p>No members loaded.</p>}

                  <div className="member-list">
                    {memberState.items.map((member) => (
                      <div className="member-row" key={member.userId}>
                        <div>
                          <strong>{member.name}</strong>
                          <span>{member.email}</span>
                        </div>
                        {canManageMembers ? (
                          <>
                            <select
                              value={String(member.role)}
                              onChange={(event) => updateMemberRole(workspace.id, member, event.target.value)}
                              disabled={memberState.saving}
                            >
                              {roleOptions.map((option) => (
                                <option key={option.value} value={option.value}>{option.label}</option>
                              ))}
                            </select>
                            <button
                              className="danger-button icon-only"
                              type="button"
                              aria-label={`Remove ${member.name}`}
                              onClick={() => removeMember(workspace.id, member)}
                              disabled={memberState.saving}
                            >
                              <Trash2 size={14} />
                            </button>
                          </>
                        ) : (
                          <span className="member-role-badge">{member.roleLabel}</span>
                        )}
                      </div>
                    ))}
                  </div>

                  {canManageMembers && (
                    <div className="member-add-row">
                      <input
                        value={memberState.email}
                        onChange={(event) => updateMemberState(workspace.id, { email: event.target.value, error: "" })}
                        placeholder="member@email.com"
                      />
                      <select
                        value={memberState.role}
                        onChange={(event) => updateMemberState(workspace.id, { role: event.target.value })}
                      >
                        {roleOptions.map((option) => (
                          <option key={option.value} value={option.value}>{option.label}</option>
                        ))}
                      </select>
                      <button
                        className="primary-button compact"
                        type="button"
                        onClick={() => addMember(workspace.id)}
                        disabled={memberState.saving}
                      >
                        <UserPlus size={16} /> Add
                      </button>
                    </div>
                  )}
                </div>
              </article>
            );
          })}
        </section>
      )}
    </div>
  );
}

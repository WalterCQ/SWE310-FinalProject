import { useEffect, useMemo, useRef, useState } from "react";
import { Plus, Search, Trash2, UserPlus } from "lucide-react";
import { motion } from "motion/react";
import { useSearchParams } from "react-router-dom";
import CreateActionButton from "../components/CreateActionButton.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import LinearModal from "../components/LinearModal.jsx";
import Avatar from "../components/Avatar.jsx";
import StatusBadge from "../components/StatusBadge.jsx";
import {
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject, mapProjectMember, mapWorkspace, mapWorkspaceMember } from "../api/mappers.js";
import { canManageProjectMembers, canManageWorkspaceMembers } from "../api/permissions.js";
import { enumProjectRoleKey, enumProjectStatusKey, useI18n } from "../i18n.jsx";
import { usePageRoleContext } from "../pageRoleContext.jsx";

const blankForm = {
  workspaceId: "",
  name: "",
  description: "",
  dueDate: "",
};
const memberBlankForm = { email: "", roleInProject: "1" };
const projectRoleOptions = [
  { value: "0", labelKey: "enum.projectRole.administrator" },
  { value: "1", labelKey: "enum.projectRole.manager" },
  { value: "2", labelKey: "enum.projectRole.member" },
];
const statusFilterOptions = [
  { value: "all", labelKey: "project.status.all" },
  { value: "0", labelKey: "enum.projectStatus.planned" },
  { value: "1", labelKey: "enum.projectStatus.active" },
  { value: "2", labelKey: "enum.projectStatus.completed" },
  { value: "3", labelKey: "enum.projectStatus.archived" },
];

function toDeadlineUtc(dateValue) {
  if (!dateValue) return null;
  return new Date(`${dateValue}T00:00:00.000Z`).toISOString();
}

export default function Projects() {
  const { t } = useI18n();
  const { setContextRole } = usePageRoleContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedProjectParam = searchParams.get("projectId") || "";
  const currentUserId = localStorage.getItem("userId") || "";
  const [workspaces, setWorkspaces] = useState([]);
  const [workspaceRolesById, setWorkspaceRolesById] = useState({});
  const [projects, setProjects] = useState([]);
  const [selectedProjectId, setSelectedProjectId] = useState(selectedProjectParam);
  const [members, setMembers] = useState([]);
  const [memberForm, setMemberForm] = useState(memberBlankForm);
  const [memberLoading, setMemberLoading] = useState(false);
  const [memberSaving, setMemberSaving] = useState(false);
  const [memberError, setMemberError] = useState("");
  const [search, setSearch] = useState(searchParams.get("search") || "");
  const [statusFilter, setStatusFilter] = useState(searchParams.get("status") || "all");
  const [form, setForm] = useState(blankForm);
  const [formErrors, setFormErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [createError, setCreateError] = useState("");
  const projectNameRef = useRef(null);

  const selectedProject = useMemo(() => {
    return projects.find((project) => project.id === selectedProjectId);
  }, [projects, selectedProjectId]);

  function isCurrentUser(userId) {
    return Boolean(userId && currentUserId && String(userId).toLowerCase() === currentUserId.toLowerCase());
  }

  const manageableWorkspaces = useMemo(() => {
    return workspaces.filter((workspace) => canManageWorkspaceMembers(workspaceRolesById[workspace.id]));
  }, [workspaces, workspaceRolesById]);

  const canCreateProject = manageableWorkspaces.length > 0;

  const currentProjectRole = useMemo(() => {
    return members.find((member) => isCurrentUser(member.userId))?.roleInProject;
  }, [members, currentUserId]);

  const canManageSelectedProjectMembers = Boolean(selectedProject) && canManageProjectMembers(
    workspaceRolesById[selectedProject.workspaceId],
    currentProjectRole
  );
  const currentContextRole = currentProjectRole || (selectedProject ? workspaceRolesById[selectedProject.workspaceId] : "");

  useEffect(() => {
    const nextSearch = searchParams.get("search") || "";
    const nextStatus = searchParams.get("status") || "all";
    const nextProjectId = searchParams.get("projectId") || "";
    if (nextSearch !== search) setSearch(nextSearch);
    if (nextStatus !== statusFilter) setStatusFilter(nextStatus);
    if (nextProjectId) setSelectedProjectId(nextProjectId);
  }, [searchParams]);

  useEffect(() => {
    setContextRole(currentContextRole || "");
    return () => setContextRole("");
  }, [currentContextRole, setContextRole]);

  useEffect(() => {
    const nextParams = {};
    if (selectedProjectId) nextParams.projectId = selectedProjectId;
    if (search.trim()) nextParams.search = search.trim();
    if (statusFilter !== "all") nextParams.status = statusFilter;
    setSearchParams(nextParams, { replace: true });
  }, [search, selectedProjectId, statusFilter, setSearchParams]);

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setLoading(true);
      setError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list());
        const mappedWorkspaces = workspaceItems.map(mapWorkspace);
        const workspaceRoleEntries = await Promise.all(
          mappedWorkspaces.map(async (workspace) => {
            try {
              const workspaceMembers = asArray(await workspacesApi.members(workspace.id)).map(mapWorkspaceMember);
              const currentMember = workspaceMembers.find((member) => isCurrentUser(member.userId));
              return [workspace.id, currentMember?.role ?? ""];
            } catch {
              return [workspace.id, ""];
            }
          })
        );
        const workspaceRoleMap = Object.fromEntries(workspaceRoleEntries);
        const manageableWorkspaceItems = mappedWorkspaces.filter((workspace) =>
          canManageWorkspaceMembers(workspaceRoleMap[workspace.id])
        );
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));

        if (active) {
          setWorkspaces(mappedWorkspaces);
          setWorkspaceRolesById(workspaceRoleMap);
          setProjects(mappedProjects);
          setSelectedProjectId((current) => mappedProjects.some((project) => project.id === current)
            ? current
            : mappedProjects[0]?.id || "");
          setForm((current) => {
            const hasWorkspace = manageableWorkspaceItems.some((workspace) => workspace.id === current.workspaceId);
            return hasWorkspace ? current : { ...current, workspaceId: manageableWorkspaceItems[0]?.id || "" };
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
  }, [currentUserId]);

  useEffect(() => {
    if (!selectedProjectId || loading) return;

    document.getElementById(`project-${selectedProjectId}`)?.scrollIntoView({
      block: "center",
      behavior: "smooth",
    });
  }, [selectedProjectId, loading, projects]);

  useEffect(() => {
    let active = true;

    async function loadMembers() {
      if (!selectedProjectId) {
        setMembers([]);
        return;
      }

      setMembers([]);
      setMemberLoading(true);
      setMemberError("");

      try {
        const data = await projectsApi.members(selectedProjectId);
        if (active) setMembers(asArray(data).map(mapProjectMember));
      } catch (apiError) {
        if (active) setMemberError(formatApiError(apiError));
      } finally {
        if (active) setMemberLoading(false);
      }
    }

    loadMembers();

    return () => {
      active = false;
    };
  }, [selectedProjectId]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setFormErrors({ ...formErrors, [event.target.name]: "" });
    setCreateError("");
  }

  function closeCreateProjectModal() {
    setForm({ ...blankForm, workspaceId: form.workspaceId });
    setFormErrors({});
    setCreateError("");
  }

  function selectProject(projectId) {
    setSelectedProjectId(projectId);
    setMembers([]);
    setMemberForm(memberBlankForm);

    const nextParams = {};
    if (projectId) nextParams.projectId = projectId;
    if (search.trim()) nextParams.search = search.trim();
    if (statusFilter !== "all") nextParams.status = statusFilter;
    setSearchParams(nextParams, { replace: true });
  }

  function validateForm() {
    const nextErrors = {};
    if (!form.workspaceId) nextErrors.workspaceId = t("project.workspaceRequired");
    if (!form.name.trim()) nextErrors.name = t("project.nameRequired");

    setFormErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function loadProjectMembers(projectId = selectedProjectId) {
    if (!projectId) return;

    setMemberLoading(true);
    setMemberError("");

    try {
      const data = await projectsApi.members(projectId);
      setMembers(asArray(data).map(mapProjectMember));
    } catch (apiError) {
      setMemberError(formatApiError(apiError));
    } finally {
      setMemberLoading(false);
    }
  }

  async function createProject(event, closeModal) {
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
      const mappedProject = mapProject(createdProject);

      setProjects((current) => [...current, mappedProject]
        .sort((left, right) => left.name.localeCompare(right.name)));
      setSelectedProjectId(mappedProject.id);
      setForm({ ...blankForm, workspaceId });
      closeModal?.();
    } catch (apiError) {
      setCreateError(formatApiError(apiError));
    } finally {
      setSaving(false);
    }
  }

  async function addProjectMember(event) {
    event.preventDefault();
    const projectId = selectedProject?.id || selectedProjectId;

    if (!projectId || !memberForm.email.trim()) {
      setMemberError(t("project.memberEmailRequired"));
      return;
    }

    setMemberSaving(true);
    setMemberError("");

    try {
      await projectsApi.addMember(projectId, {
        email: memberForm.email.trim(),
        roleInProject: Number(memberForm.roleInProject),
      });
      setMemberForm(memberBlankForm);
      await loadProjectMembers(projectId);
    } catch (apiError) {
      setMemberError(formatApiError(apiError));
    } finally {
      setMemberSaving(false);
    }
  }

  async function updateProjectMemberRole(member, roleInProject) {
    const projectId = selectedProject?.id || selectedProjectId;
    if (!projectId) return;

    setMemberSaving(true);
    setMemberError("");

    try {
      await projectsApi.updateMember(projectId, member.userId, {
        roleInProject: Number(roleInProject),
      });
      await loadProjectMembers(projectId);
    } catch (apiError) {
      setMemberError(formatApiError(apiError));
    } finally {
      setMemberSaving(false);
    }
  }

  async function removeProjectMember(member) {
    const projectId = selectedProject?.id || selectedProjectId;
    if (!projectId) return;

    setMemberSaving(true);
    setMemberError("");

    try {
      await projectsApi.removeMember(projectId, member.userId);
      await loadProjectMembers(projectId);
    } catch (apiError) {
      setMemberError(formatApiError(apiError));
    } finally {
      setMemberSaving(false);
    }
  }

  const visibleProjects = useMemo(() => {
    const query = search.trim().toLowerCase();

    return projects.filter((project) => {
      const matchesQuery = !query
        || [project.name, project.owner, project.statusLabel]
          .filter(Boolean)
          .some((value) => String(value).toLowerCase().includes(query));
      const matchesStatus = statusFilter === "all" || String(project.status) === statusFilter;

      return matchesQuery && matchesStatus;
    });
  }, [projects, search, statusFilter]);

  return (
    <div className="page-stack">
      {canCreateProject && (
        <div className="page-actions">
          <LinearModal
            closeLabel={t("project.closeCreate")}
            description={t("project.createHelp")}
            icon={Plus}
            initialFocusRef={projectNameRef}
            layoutId="project-create-modal"
            onClose={closeCreateProjectModal}
            size="lg"
            title={t("project.createTitle")}
            trigger={({ iconLayoutId, layoutId, open, titleLayoutId }) => (
              <CreateActionButton
                as={motion.button}
                ariaLabel={t("project.new")}
                iconLayoutId={iconLayoutId}
                layoutId={layoutId}
                titleLayoutId={titleLayoutId}
                onClick={() => {
                  setCreateError("");
                  open();
                }}
              >
                {t("project.new")}
              </CreateActionButton>
            )}
          >
            {({ close }) => (
              <>
                {createError && <div className="error-text"><strong>{t("project.unableCreate")}</strong> {createError}</div>}

                <form className="task-form" onSubmit={(event) => createProject(event, close)} noValidate>
                  <div className="form-grid-2">
                    <label>
                      {t("project.workspace")}
                      <select
                        name="workspaceId"
                        value={form.workspaceId}
                        onChange={updateField}
                        disabled={loading || workspaces.length === 0}
                      >
                        {manageableWorkspaces.length === 0 && <option value="">{t("project.noWorkspace")}</option>}
                        {manageableWorkspaces.map((workspace) => (
                          <option key={workspace.id} value={workspace.id}>{workspace.name}</option>
                        ))}
                      </select>
                      <ErrorMessage>{formErrors.workspaceId}</ErrorMessage>
                    </label>

                    <label>
                      {t("project.dueDate")}
                      <input name="dueDate" type="date" value={form.dueDate} onChange={updateField} />
                    </label>
                  </div>

                  <label>
                    {t("project.name")}
                    <input
                      ref={projectNameRef}
                      name="name"
                      value={form.name}
                      onChange={updateField}
                      placeholder={t("project.placeholder.name")}
                    />
                    <ErrorMessage>{formErrors.name}</ErrorMessage>
                  </label>

                  <label>
                    {t("project.description")}
                    <textarea
                      name="description"
                      value={form.description}
                      onChange={updateField}
                      placeholder={t("project.placeholder.description")}
                    />
                  </label>

                  <div className="button-row">
                    <button className="primary-button" type="submit" disabled={saving || loading || manageableWorkspaces.length === 0}>
                      <Plus size={18} /> {saving ? t("project.creating") : t("project.create")}
                    </button>
                    <button className="secondary-button" type="button" onClick={close}>
                      {t("workspace.cancel")}
                    </button>
                  </div>
                </form>
              </>
            )}
          </LinearModal>
        </div>
      )}

      <section className="panel">
        <div className="toolbar">
          <div className="search-box inline">
            <Search size={18} />
            <input
              placeholder={t("project.search")}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <select
            className="control-select"
            value={statusFilter}
            onChange={(event) => setStatusFilter(event.target.value)}
          >
            {statusFilterOptions.map((option) => (
              <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
            ))}
          </select>
        </div>

        {loading && <p>{t("project.loading")}</p>}
        {error && <div><strong>{t("project.unableLoad")}</strong><p>{error}</p></div>}
        {!loading && !error && projects.length === 0 && <p>{t("project.empty")}</p>}
        {!loading && !error && projects.length > 0 && visibleProjects.length === 0 && <p>{t("project.noMatch")}</p>}

        {!loading && !error && visibleProjects.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("project.column.project")}</th>
                  <th>{t("project.column.status")}</th>
                  <th>{t("project.column.progress")}</th>
                  <th>{t("project.column.owner")}</th>
                  <th>{t("project.column.members")}</th>
                  <th>{t("project.dueDate")}</th>
                </tr>
              </thead>
              <tbody>
                {visibleProjects.map((project) => (
                  <tr
                    id={`project-${project.id}`}
                    key={project.id}
                    className={project.id === selectedProjectId ? "selected-row target-highlight" : ""}
                    onClick={() => selectProject(project.id)}
                  >
                    <td>{project.name}</td>
                    <td><StatusBadge variant={project.statusLabel}>{t(enumProjectStatusKey(project.statusLabel))}</StatusBadge></td>
                    <td>
                      <div className="progress-cell">
                        <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                        <span>{project.progress}%</span>
                      </div>
                    </td>
                    <td>
                      <span className="person-cell">
                        <Avatar className="mini-avatar" seed={project.createdByUserId || project.owner} name={project.owner} ariaHidden />
                        <span className="person-name">{project.owner}</span>
                      </span>
                    </td>
                    <td>{project.memberCount || 0}</td>
                    <td>{project.deadlineLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {selectedProject && (
        <section className="panel">
          <div className="panel-header">
            <div>
              <h3>{t("project.membersTitle")}</h3>
              <span>{selectedProject.name}</span>
            </div>
            <button className="secondary-button compact" type="button" onClick={() => loadProjectMembers()}>
              {t("project.refreshMembers")}
            </button>
          </div>

          {memberError && <div className="error-text">{memberError}</div>}
          {memberLoading && <p>{t("project.loadingMembers")}</p>}

          <div className="member-list wide project-member-list">
            {!memberLoading && members.length === 0 && <p>{t("project.emptyMembers")}</p>}
            {members.map((member) => (
              <div className="member-row project-member-row" key={member.userId}>
                <Avatar
                  className="mini-avatar member-row-avatar"
                  seed={member.userId || member.email}
                  name={member.name}
                  ariaHidden
                />
                <div className="member-row-copy">
                  <strong>{member.name}</strong>
                  <span>{member.email}</span>
                </div>
                {canManageSelectedProjectMembers ? (
                  <>
                    <select
                      value={String(member.roleInProject)}
                      onChange={(event) => updateProjectMemberRole(member, event.target.value)}
                      disabled={memberSaving}
                    >
                      {projectRoleOptions.map((option) => (
                        <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
                      ))}
                    </select>
                    <button
                      className="danger-button icon-only"
                      type="button"
                      aria-label={t("project.removeMemberAria", { name: member.name })}
                      onClick={() => removeProjectMember(member)}
                      disabled={memberSaving}
                    >
                      <Trash2 size={14} />
                    </button>
                  </>
                ) : (
                  <span className="member-role-badge">{t(enumProjectRoleKey(member.roleLabel))}</span>
                )}
              </div>
            ))}
          </div>

          {canManageSelectedProjectMembers && (
            <form className="member-add-row wide" onSubmit={addProjectMember}>
              <input
                value={memberForm.email}
                onChange={(event) => {
                  setMemberForm({ ...memberForm, email: event.target.value });
                  setMemberError("");
                }}
                placeholder={t("project.memberEmailPlaceholder")}
              />
              <select
                value={memberForm.roleInProject}
                onChange={(event) => setMemberForm({ ...memberForm, roleInProject: event.target.value })}
              >
                {projectRoleOptions.map((option) => (
                  <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
                ))}
              </select>
              <button className="primary-button compact" type="submit" disabled={memberSaving}>
                <UserPlus size={16} /> {t("project.addMember")}
              </button>
            </form>
          )}
        </section>
      )}
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import { ChevronDown, FolderKanban, GitPullRequest, KeyRound, Plus, Save, Trash2, UserPlus, Users } from "lucide-react";
import { motion } from "motion/react";
import { useSearchParams } from "react-router-dom";
import CreateActionButton from "../components/CreateActionButton.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import LinearModal from "../components/LinearModal.jsx";
import Avatar from "../components/Avatar.jsx";
import { auth, formatApiError, workspaces as workspacesApi } from "../api/taskflowApi.js";
import { asArray, mapWorkspace, mapWorkspaceMember } from "../api/mappers.js";
import { canDeleteWorkspace, canManageWorkspaceMembers, isWorkspaceAdmin } from "../api/permissions.js";
import { enumWorkspaceRoleKey, useI18n } from "../i18n.jsx";

const colors = ["amber", "green", "red", "yellow"];
const blankForm = { name: "", description: "" };
const blankAiProviderForm = {
  providerName: "OpenAICompatible",
  baseUrl: "",
  model: "deepseek-ai/DeepSeek-V4-Flash",
  apiKey: "",
  supportsToolCalls: true,
};
const GITHUB_SETUP_WORKSPACE_KEY = "taskflow.githubSetupWorkspaceId";
const roleOptions = [
  { value: "0", labelKey: "enum.workspaceRole.owner" },
  { value: "1", labelKey: "enum.workspaceRole.admin" },
  { value: "2", labelKey: "enum.workspaceRole.member" },
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

function blankAiProviderState() {
  return {
    ...blankAiProviderForm,
    hasProvider: false,
    hasApiKey: false,
    loading: false,
    saving: false,
    deleting: false,
    loaded: false,
    error: "",
    message: "",
  };
}

function blankGitHubState() {
  return {
    repositories: [],
    loading: false,
    setupLoading: false,
    completing: false,
    deletingId: "",
    loaded: false,
    error: "",
    message: "",
  };
}

export default function Workspaces() {
  const { t } = useI18n();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedWorkspaceId = searchParams.get("workspaceId") || "";
  const [currentUserId, setCurrentUserId] = useState(localStorage.getItem("userId") || "");
  const [workspaces, setWorkspaces] = useState([]);
  const [membersByWorkspace, setMembersByWorkspace] = useState({});
  const [aiProvidersByWorkspace, setAiProvidersByWorkspace] = useState({});
  const [githubByWorkspace, setGithubByWorkspace] = useState({});
  const [form, setForm] = useState(blankForm);
  const [formErrors, setFormErrors] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [createError, setCreateError] = useState("");
  const workspaceNameRef = useRef(null);
  const githubSetupHandledRef = useRef("");

  useEffect(() => {
    let active = true;

    async function loadWorkspaces() {
      setLoading(true);
      setError("");

      try {
        const user = await auth.me().catch(() => null);
        if (active && user?.userId) {
          localStorage.setItem("userId", user.userId);
          setCurrentUserId(user.userId);
        }

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

  useEffect(() => {
    if (loading) return;

    const installationId = searchParams.get("installation_id");
    if (!installationId || githubSetupHandledRef.current === installationId) return;

    const workspaceId = searchParams.get("state")
      || selectedWorkspaceId
      || localStorage.getItem(GITHUB_SETUP_WORKSPACE_KEY)
      || "";
    if (!workspaceId) return;

    const currentWorkspaceRole = getCurrentWorkspaceRole(workspaceId);
    if (!canManageWorkspaceMembers(currentWorkspaceRole)) return;

    githubSetupHandledRef.current = installationId;
    completeGitHubInstallation(workspaceId, installationId);
  }, [currentUserId, loading, membersByWorkspace, searchParams, selectedWorkspaceId]);

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

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setFormErrors({ ...formErrors, [event.target.name]: "" });
    setCreateError("");
  }

  function closeCreateWorkspaceModal() {
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

  function updateAiProviderState(workspaceId, updates) {
    setAiProvidersByWorkspace((current) => ({
      ...current,
      [workspaceId]: {
        ...blankAiProviderState(),
        ...(current[workspaceId] || {}),
        ...updates,
      },
    }));
  }

  function updateAiProviderField(workspaceId, field, value) {
    updateAiProviderState(workspaceId, { [field]: value, error: "", message: "" });
  }

  function updateGitHubState(workspaceId, updates) {
    setGithubByWorkspace((current) => ({
      ...current,
      [workspaceId]: {
        ...blankGitHubState(),
        ...(current[workspaceId] || {}),
        ...updates,
      },
    }));
  }

  function formatGitHubError(error) {
    if (error?.statusCode === 404) return t("workspace.githubApiUnavailable");
    return formatApiError(error);
  }

  async function loadAiProvider(workspaceId) {
    updateAiProviderState(workspaceId, { loading: true, error: "", message: "" });

    try {
      const provider = await workspacesApi.getAiProvider(workspaceId);
      updateAiProviderState(workspaceId, {
        providerName: provider.providerName || blankAiProviderForm.providerName,
        baseUrl: provider.baseUrl || "",
        model: provider.model || blankAiProviderForm.model,
        apiKey: "",
        supportsToolCalls: Boolean(provider.supportsToolCalls),
        hasProvider: true,
        hasApiKey: Boolean(provider.hasApiKey),
        loading: false,
        loaded: true,
      });
    } catch (apiError) {
      if (apiError.statusCode === 404) {
        updateAiProviderState(workspaceId, {
          ...blankAiProviderForm,
          hasProvider: false,
          hasApiKey: false,
          loading: false,
          loaded: true,
        });
        return;
      }

      updateAiProviderState(workspaceId, {
        error: formatApiError(apiError),
        loading: false,
        loaded: true,
      });
    }
  }

  async function saveAiProvider(workspaceId) {
    const state = aiProvidersByWorkspace[workspaceId] || blankAiProviderState();

    if (!state.hasApiKey && !state.apiKey.trim()) {
      updateAiProviderState(workspaceId, { error: t("workspace.aiKeyRequired") });
      return;
    }

    updateAiProviderState(workspaceId, { saving: true, error: "", message: "" });

    try {
      const savedProvider = await workspacesApi.saveAiProvider(workspaceId, {
        providerName: (state.providerName || blankAiProviderForm.providerName).trim(),
        baseUrl: state.baseUrl.trim() || null,
        model: (state.model || blankAiProviderForm.model).trim(),
        apiKey: state.apiKey.trim() || null,
        supportsToolCalls: state.supportsToolCalls,
      });

      updateAiProviderState(workspaceId, {
        providerName: savedProvider.providerName || (state.providerName || blankAiProviderForm.providerName).trim(),
        baseUrl: savedProvider.baseUrl || "",
        model: savedProvider.model || (state.model || blankAiProviderForm.model).trim(),
        apiKey: "",
        supportsToolCalls: Boolean(savedProvider.supportsToolCalls),
        hasProvider: true,
        hasApiKey: Boolean(savedProvider.hasApiKey),
        saving: false,
        loaded: true,
        message: t("workspace.aiKeySaved"),
      });
    } catch (apiError) {
      updateAiProviderState(workspaceId, { error: formatApiError(apiError), saving: false });
    }
  }

  async function deleteAiProvider(workspaceId) {
    const confirmed = window.confirm(t("workspace.removeAiProviderConfirm"));
    if (!confirmed) return;

    updateAiProviderState(workspaceId, { deleting: true, error: "", message: "" });

    try {
      await workspacesApi.deleteAiProvider(workspaceId);
      updateAiProviderState(workspaceId, {
        ...blankAiProviderForm,
        hasProvider: false,
        hasApiKey: false,
        deleting: false,
        loaded: true,
        message: t("workspace.aiProviderRemoved"),
      });
    } catch (apiError) {
      updateAiProviderState(workspaceId, { error: formatApiError(apiError), deleting: false });
    }
  }

  async function loadGitHubRepositories(workspaceId) {
    updateGitHubState(workspaceId, { loading: true, error: "", message: "" });

    try {
      const repositories = await workspacesApi.githubRepositories(workspaceId);
      updateGitHubState(workspaceId, {
        repositories: asArray(repositories),
        loading: false,
        loaded: true,
      });
    } catch (apiError) {
      updateGitHubState(workspaceId, {
        error: formatGitHubError(apiError),
        loading: false,
        loaded: true,
      });
    }
  }

  async function startGitHubSetup(workspaceId) {
    updateGitHubState(workspaceId, { setupLoading: true, error: "", message: "" });

    try {
      const setup = await workspacesApi.githubSetup(workspaceId);
      if (!setup?.installUrl) {
        updateGitHubState(workspaceId, {
          setupLoading: false,
          error: t("workspace.githubSetupMissing"),
        });
        return;
      }

      localStorage.setItem(GITHUB_SETUP_WORKSPACE_KEY, workspaceId);
      window.location.assign(setup.installUrl);
    } catch (apiError) {
      updateGitHubState(workspaceId, { error: formatGitHubError(apiError), setupLoading: false });
    }
  }

  async function completeGitHubInstallation(workspaceId, installationId) {
    const parsedInstallationId = Number(installationId);
    if (!parsedInstallationId) {
      updateGitHubState(workspaceId, { error: t("workspace.githubInvalidInstallation") });
      return;
    }

    updateGitHubState(workspaceId, { completing: true, error: "", message: "" });

    try {
      const repositories = await workspacesApi.completeGithubInstallation(workspaceId, {
        installationId: parsedInstallationId,
      });
      localStorage.removeItem(GITHUB_SETUP_WORKSPACE_KEY);
      setSearchParams({ workspaceId }, { replace: true });
      updateGitHubState(workspaceId, {
        repositories: asArray(repositories),
        completing: false,
        loaded: true,
        message: t("workspace.githubConnected"),
      });
    } catch (apiError) {
      updateGitHubState(workspaceId, { error: formatGitHubError(apiError), completing: false });
    }
  }

  async function deleteGitHubRepository(workspaceId, repository) {
    const repositoryName = repository.fullName || repository.name;
    const confirmed = window.confirm(t("workspace.githubDisconnectConfirm", { name: repositoryName }));
    if (!confirmed) return;

    updateGitHubState(workspaceId, { deletingId: repository.id, error: "", message: "" });

    try {
      await workspacesApi.deleteGithubRepository(workspaceId, repository.id);
      updateGitHubState(workspaceId, { message: t("workspace.githubRepositoryDisconnected"), deletingId: "" });
      await loadGitHubRepositories(workspaceId);
    } catch (apiError) {
      updateGitHubState(workspaceId, { error: formatGitHubError(apiError), deletingId: "" });
    }
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

  async function createWorkspace(event, closeModal) {
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
      setSearchParams({ workspaceId: mappedWorkspace.id });
      closeModal?.();
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
      updateMemberState(workspaceId, { error: t("workspace.memberEmailRequired") });
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
    const confirmed = window.confirm(t("workspace.deleteConfirm", { name: workspace.name }));
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
          <LinearModal
            closeLabel={t("workspace.closeCreate")}
            description={t("workspace.createHelp")}
            icon={Plus}
            initialFocusRef={workspaceNameRef}
            layoutId="workspace-create-modal"
            onClose={closeCreateWorkspaceModal}
            size="lg"
            title={t("workspace.createTitle")}
            trigger={({ iconLayoutId, layoutId, open, titleLayoutId }) => (
              <CreateActionButton
                as={motion.button}
                ariaLabel={t("workspace.new")}
                iconLayoutId={iconLayoutId}
                layoutId={layoutId}
                titleLayoutId={titleLayoutId}
                onClick={() => {
                  setCreateError("");
                  open();
                }}
              >
                {t("workspace.new")}
              </CreateActionButton>
            )}
          >
            {({ close }) => (
              <>
                {createError && <div className="error-text"><strong>{t("workspace.unableCreate")}</strong> {createError}</div>}

                <form className="task-form" onSubmit={(event) => createWorkspace(event, close)} noValidate>
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
            const aiProviderState = aiProvidersByWorkspace[workspace.id] || blankAiProviderState();
            const aiProviderBusy = aiProviderState.loading || aiProviderState.saving || aiProviderState.deleting;
            const githubState = githubByWorkspace[workspace.id] || blankGitHubState();
            const githubBusy = githubState.loading
              || githubState.setupLoading
              || githubState.completing
              || Boolean(githubState.deletingId);

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
                    <strong>{t("workspace.membersTitle")}</strong>
                    <div className="member-manager-actions">
                      {canDeleteCurrentWorkspace && (
                        <button
                          className="danger-button compact"
                          type="button"
                          onClick={() => deleteWorkspace(workspace)}
                          disabled={memberState.saving}
                        >
                          <Trash2 size={14} /> {t("workspace.delete")}
                        </button>
                      )}
                      <button className="secondary-button compact" type="button" onClick={() => reloadMembers(workspace.id)}>
                        {t("workspace.refresh")}
                      </button>
                    </div>
                  </div>

                  {memberState.error && <div className="error-text">{memberState.error}</div>}
                  {memberState.loading && <p>{t("workspace.loadingMembers")}</p>}
                  {!memberState.loading && memberState.items.length === 0 && <p>{t("workspace.noMembersLoaded")}</p>}

                  <div className="member-list">
                    {memberState.items.map((member) => (
                      <div className="member-row workspace-member-row" key={member.userId}>
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
                        {canManageMembers ? (
                          <>
                            <select
                              value={String(member.role)}
                              onChange={(event) => updateMemberRole(workspace.id, member, event.target.value)}
                              disabled={memberState.saving}
                            >
                              {roleOptions.map((option) => (
                                <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
                              ))}
                            </select>
                            <button
                              className="danger-button icon-only"
                              type="button"
                              aria-label={t("workspace.removeMemberAria", { name: member.name })}
                              onClick={() => removeMember(workspace.id, member)}
                              disabled={memberState.saving}
                            >
                              <Trash2 size={14} />
                            </button>
                          </>
                        ) : (
                          <span className="member-role-badge">{t(enumWorkspaceRoleKey(member.roleLabel))}</span>
                        )}
                      </div>
                    ))}
                  </div>

                  {canManageMembers && (
                    <div className="member-add-row">
                      <input
                        value={memberState.email}
                        onChange={(event) => updateMemberState(workspace.id, { email: event.target.value, error: "" })}
                        placeholder={t("workspace.memberEmailPlaceholder")}
                      />
                      <select
                        value={memberState.role}
                        onChange={(event) => updateMemberState(workspace.id, { role: event.target.value })}
                      >
                        {roleOptions.map((option) => (
                          <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
                        ))}
                      </select>
                      <button
                        className="primary-button compact"
                        type="button"
                        onClick={() => addMember(workspace.id)}
                        disabled={memberState.saving}
                      >
                        <UserPlus size={16} /> {t("workspace.addMember")}
                      </button>
                    </div>
                  )}
                </div>

                {canManageMembers && (
                  <div className="workspace-settings-grid">
                    <details className="settings-disclosure">
                      <summary>
                        <span className="settings-summary-copy">
                          <KeyRound size={15} />
                          <span>
                            <strong>{t("workspace.aiApiKey")}</strong>
                            <small>{aiProviderState.hasApiKey ? t("workspace.configured") : t("workspace.notConfigured")}</small>
                          </span>
                        </span>
                        <ChevronDown className="settings-chevron" size={16} />
                      </summary>

                      <div className="settings-disclosure-body">
                        <div className="member-manager-actions left">
                          <button
                            className="secondary-button compact"
                            type="button"
                            onClick={() => loadAiProvider(workspace.id)}
                            disabled={aiProviderState.loading}
                          >
                            {aiProviderState.loaded ? t("workspace.refresh") : t("workspace.load")}
                          </button>
                          {aiProviderState.hasProvider && (
                            <button
                              className="danger-button compact"
                              type="button"
                              onClick={() => deleteAiProvider(workspace.id)}
                              disabled={aiProviderBusy}
                            >
                              <Trash2 size={14} /> {t("workspace.remove")}
                            </button>
                          )}
                        </div>

                        {aiProviderState.error && <div className="error-text">{aiProviderState.error}</div>}
                        {aiProviderState.message && <div className="success-text">{aiProviderState.message}</div>}
                        {aiProviderState.loading && <p>{t("workspace.aiProviderLoading")}</p>}

                        <div className="ai-key-status">
                          {aiProviderState.hasApiKey ? t("workspace.aiKeyConfiguredHelp") : t("workspace.aiKeyMissingHelp")}
                        </div>
                        <p className="muted-small">{t("workspace.channelRagHelp")}</p>

                        <div className="ai-key-field">
                          <label>
                            {t("workspace.apiKey")}
                            <input
                              value={aiProviderState.apiKey}
                              onChange={(event) => updateAiProviderField(workspace.id, "apiKey", event.target.value)}
                              disabled={aiProviderBusy}
                              placeholder={aiProviderState.hasApiKey ? t("workspace.keepCurrentKeyPlaceholder") : "sk-..."}
                              type="password"
                            />
                          </label>
                        </div>

                        <button
                          className="primary-button compact"
                          type="button"
                          onClick={() => saveAiProvider(workspace.id)}
                          disabled={aiProviderBusy}
                        >
                          <Save size={15} /> {aiProviderState.saving ? t("ai.saving") : t("workspace.saveApiKey")}
                        </button>
                      </div>
                    </details>

                    <details className="settings-disclosure">
                      <summary>
                        <span className="settings-summary-copy">
                          <GitPullRequest size={15} />
                          <span>
                            <strong>{t("workspace.githubApp")}</strong>
                            <small>
                              {githubState.loaded
                                ? t("workspace.githubRepositoriesCount", { count: githubState.repositories.length })
                                : t("workspace.githubConnectionSettings")}
                            </small>
                          </span>
                        </span>
                        <ChevronDown className="settings-chevron" size={16} />
                      </summary>

                      <div className="settings-disclosure-body">
                        <div className="member-manager-actions left">
                          <button
                            className="secondary-button compact"
                            type="button"
                            onClick={() => loadGitHubRepositories(workspace.id)}
                            disabled={githubBusy}
                          >
                            {githubState.loaded ? t("workspace.refresh") : t("workspace.load")}
                          </button>
                        </div>

                        {githubState.error && <div className="error-text">{githubState.error}</div>}
                        {githubState.message && <div className="success-text">{githubState.message}</div>}
                        {githubState.loading && <p>{t("workspace.githubLoadingRepositories")}</p>}
                        {githubState.completing && <p>{t("workspace.githubSyncingInstallation")}</p>}

                        <p className="muted-small">{t("workspace.githubHelp")}</p>

                        <button
                          className="primary-button compact"
                          type="button"
                          onClick={() => startGitHubSetup(workspace.id)}
                          disabled={githubBusy}
                        >
                          <GitPullRequest size={15} /> {githubState.setupLoading ? t("workspace.githubOpening") : t("workspace.githubConnect")}
                        </button>

                        {githubState.loaded && githubState.repositories.length === 0 && (
                          <p className="muted-small">{t("workspace.githubNoRepositories")}</p>
                        )}

                        {githubState.repositories.length > 0 && (
                          <div className="member-list">
                            {githubState.repositories.map((repository) => (
                              <div className="member-row" key={repository.id}>
                                <div>
                                  <strong>{repository.fullName}</strong>
                                  <span>{repository.defaultBranch} · {repository.permissionStatus}</span>
                                </div>
                                <button
                                  className="danger-button icon-only"
                                  type="button"
                                  aria-label={t("workspace.githubDisconnectAria", { name: repository.fullName })}
                                  onClick={() => deleteGitHubRepository(workspace.id, repository)}
                                  disabled={githubBusy}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </details>
                  </div>
                )}
              </article>
            );
          })}
        </section>
      )}
    </div>
  );
}

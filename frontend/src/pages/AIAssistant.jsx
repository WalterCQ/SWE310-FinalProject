import { useEffect, useMemo, useState } from "react";
import {
  Activity,
  Bot,
  Check,
  ClipboardCheck,
  FileText,
  KeyRound,
  ListChecks,
  Play,
  RefreshCw,
  Send,
  Settings2,
  ShieldCheck,
  X,
} from "lucide-react";
import {
  agent,
  ai,
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, formatDateTime, mapProject, selectPrimaryWorkspace } from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";

const jobStatusLabels = ["Planning", "AwaitingApproval", "Running", "NeedsApproval", "Paused", "Completed", "Failed", "Canceled"];
const approvalStatusLabels = ["Pending", "Approved", "Rejected", "Expired"];
const eventTypeLabels = [
  "Created",
  "StatusChanged",
  "Planning",
  "ApprovalRequested",
  "ApprovalApproved",
  "ApprovalRejected",
  "StepStarted",
  "StepCompleted",
  "ArtifactCreated",
  "ActionExecuted",
  "Error",
  "Canceled",
];
const artifactKindLabels = ["Summary", "CodePatch", "Deck", "Report", "TaskFlowAction", "Other"];
const finalJobStatuses = ["Completed", "Failed", "Canceled"];
const pollingStopStatuses = [...finalJobStatuses, "Paused"];
const lifecycleSteps = [
  { key: "Planning", labelKey: "ai.lifecycle.plan", descriptionKey: "ai.lifecycle.planDesc" },
  { key: "AwaitingApproval", labelKey: "ai.lifecycle.confirm", descriptionKey: "ai.lifecycle.confirmDesc" },
  { key: "Running", labelKey: "ai.lifecycle.run", descriptionKey: "ai.lifecycle.runDesc" },
  { key: "NeedsApproval", labelKey: "ai.lifecycle.approve", descriptionKey: "ai.lifecycle.approveDesc" },
  { key: "Completed", labelKey: "ai.lifecycle.done", descriptionKey: "ai.lifecycle.doneDesc" },
];

export default function AIAssistant() {
  const { t } = useI18n();
  const [workspaces, setWorkspaces] = useState([]);
  const [projects, setProjects] = useState([]);
  const [selectedWorkspaceId, setSelectedWorkspaceId] = useState("");
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [summary, setSummary] = useState(null);
  const [riskAnalysis, setRiskAnalysis] = useState(null);
  const [command, setCommand] = useState(() => t("ai.defaultCommand") === "ai.defaultCommand" ? "Summarize current workspace priorities for the presentation." : t("ai.defaultCommand"));
  const [agentGoal, setAgentGoal] = useState(() => t("ai.defaultGoal") === "ai.defaultGoal" ? "Summarize this workspace, create follow-up tasks and reminders for the demo." : t("ai.defaultGoal"));
  const [commandResult, setCommandResult] = useState(null);
  const [providers, setProviders] = useState([]);
  const [selectedProviderId, setSelectedProviderId] = useState("");
  const [providerForm, setProviderForm] = useState({
    providerName: "OpenAICompatible",
    baseUrl: "",
    model: "gpt-4o-mini",
    apiKey: "",
    supportsToolCalls: false,
  });
  const [currentJob, setCurrentJob] = useState(null);
  const [jobEvents, setJobEvents] = useState([]);
  const [loadingProjects, setLoadingProjects] = useState(true);
  const [loadingAi, setLoadingAi] = useState(false);
  const [commandLoading, setCommandLoading] = useState(false);
  const [savingProvider, setSavingProvider] = useState(false);
  const [creatingJob, setCreatingJob] = useState(false);
  const [decidingApprovalId, setDecidingApprovalId] = useState("");
  const [error, setError] = useState("");

  const selectedProject = useMemo(() => {
    return projects.find((project) => project.id === selectedProjectId);
  }, [projects, selectedProjectId]);

  const activeProvider = useMemo(() => {
    return providers.find((provider) => provider.id === selectedProviderId) || null;
  }, [providers, selectedProviderId]);

  const currentJobStatus = enumLabel(currentJob?.status, jobStatusLabels);
  const hasActiveJob = currentJob && !finalJobStatuses.includes(currentJobStatus);
  const pendingApprovals = asArray(currentJob?.approvals)
    .filter((approval) => enumLabel(approval.status, approvalStatusLabels) === "Pending");
  const artifacts = asArray(currentJob?.artifacts);
  const artifactsByKind = groupArtifacts(artifacts);
  const canCreateJob = selectedWorkspaceId && agentGoal.trim();

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setLoadingProjects(true);
      setError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list());
        const primaryWorkspace = selectPrimaryWorkspace(workspaceItems);
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));
        const providerItems = asArray(await ai.providers().catch(() => []));

        if (active) {
          setWorkspaces(workspaceItems);
          setProjects(mappedProjects);
          setProviders(providerItems);
          setSelectedProviderId(providerItems.find((provider) => provider.isDefault)?.id || providerItems[0]?.id || "");
          setSelectedWorkspaceId(primaryWorkspace?.id || workspaceItems[0]?.id || "");
          setSelectedProjectId(mappedProjects[0]?.id || "");
        }
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoadingProjects(false);
      }
    }

    loadProjects();

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    let active = true;

    async function loadAiResult() {
      if (!selectedProjectId) {
        setSummary(null);
        setRiskAnalysis(null);
        return;
      }

      setLoadingAi(true);
      setError("");

      try {
        const [summaryResult, riskResult] = await Promise.all([
          ai.projectSummary(selectedProjectId),
          ai.riskAnalysis(selectedProjectId),
        ]);

        if (active) {
          setSummary(summaryResult);
          setRiskAnalysis(riskResult);
        }
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoadingAi(false);
      }
    }

    loadAiResult();

    return () => {
      active = false;
    };
  }, [selectedProjectId]);

  useEffect(() => {
    if (!currentJob?.id || pollingStopStatuses.includes(currentJobStatus)) {
      return undefined;
    }

    const intervalId = window.setInterval(() => {
      refreshJob(currentJob.id);
    }, 3000);

    return () => window.clearInterval(intervalId);
  }, [currentJob?.id, currentJobStatus]);

  async function refreshAi() {
    if (!selectedProjectId) return;

    setLoadingAi(true);
    setError("");

    try {
      const [summaryResult, riskResult] = await Promise.all([
        ai.projectSummary(selectedProjectId),
        ai.riskAnalysis(selectedProjectId),
      ]);
      setSummary(summaryResult);
      setRiskAnalysis(riskResult);
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setLoadingAi(false);
    }
  }

  async function runCommand(event) {
    event.preventDefault();
    if (!selectedWorkspaceId || !command.trim()) return;

    setCommandLoading(true);
    setError("");

    try {
      const result = await ai.command({
        workspaceId: selectedWorkspaceId,
        command: command.trim(),
      });
      setCommandResult(result);
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setCommandLoading(false);
    }
  }

  async function saveProvider(event) {
    event.preventDefault();
    if (!providerForm.providerName.trim() || !providerForm.model.trim() || !providerForm.apiKey.trim()) return;

    setSavingProvider(true);
    setError("");

    try {
      const saved = await ai.saveProvider({
        providerName: providerForm.providerName.trim(),
        baseUrl: providerForm.baseUrl.trim() || null,
        model: providerForm.model.trim(),
        apiKey: providerForm.apiKey.trim(),
        supportsToolCalls: providerForm.supportsToolCalls,
        isDefault: true,
      });
      const providerItems = asArray(await ai.providers());
      setProviders(providerItems);
      setSelectedProviderId(saved.id);
      setProviderForm((current) => ({ ...current, apiKey: "" }));
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setSavingProvider(false);
    }
  }

  async function createAgentJob(event) {
    event.preventDefault();
    if (!canCreateJob) return;

    setCreatingJob(true);
    setError("");

    try {
      const created = await agent.createJob({
        workspaceId: selectedWorkspaceId,
        goal: agentGoal.trim(),
        providerCredentialId: selectedProviderId || null,
      });
      setCurrentJob(created);
      setJobEvents(asArray(await agent.events(created.id)));
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setCreatingJob(false);
    }
  }

  async function refreshJob(jobId = currentJob?.id) {
    if (!jobId) return;

    try {
      const [job, events] = await Promise.all([
        agent.getJob(jobId),
        agent.events(jobId),
      ]);
      setCurrentJob(job);
      setJobEvents(asArray(events));
    } catch (apiError) {
      setError(formatApiError(apiError));
    }
  }

  async function decideApproval(approval, approved) {
    setDecidingApprovalId(approval.id);
    setError("");

    try {
      if (approved) {
        await agent.approve(approval.id);
      } else {
        await agent.reject(approval.id);
      }
      await refreshJob(approval.agentJobId);
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setDecidingApprovalId("");
    }
  }

  async function cancelJob() {
    if (!currentJob?.id) return;

    setError("");
    try {
      const canceled = await agent.cancel(currentJob.id);
      setCurrentJob(canceled);
      setJobEvents(asArray(await agent.events(currentJob.id)));
    } catch (apiError) {
      setError(formatApiError(apiError));
    }
  }

  return (
    <div className="page-stack agent-console">
      <div className="page-heading agent-heading">
        <div>
          <p className="eyebrow">{t("ai.eyebrow")}</p>
          <h1>{t("ai.title")}</h1>
        </div>
        <div className="agent-provider-chip">
          <ShieldCheck size={17} />
          <span>{providerSummary(activeProvider, t)}</span>
        </div>
      </div>

      {error && (
        <section className="panel agent-error" role="alert">
          <strong>{t("ai.apiError")}</strong>
          <p>{error}</p>
        </section>
      )}

      <section className="panel agent-command-bar">
        <div className="agent-command-copy">
          <div className="ai-icon"><Bot size={34} /></div>
          <div>
            <h3>{t("ai.startTitle")}</h3>
            <p>{t("ai.startHelp")}</p>
          </div>
        </div>

        <form className="agent-command-form" onSubmit={createAgentJob}>
          <label>
            {t("ai.goal")}
            <textarea
              value={agentGoal}
              onChange={(event) => setAgentGoal(event.target.value)}
              rows={3}
              aria-describedby="agent-goal-help"
            />
          </label>
          <p id="agent-goal-help" className="agent-muted">
            {t("ai.goalHelp")}
          </p>
          <div className="agent-command-controls">
            <label>
              {t("ai.workspace")}
              <select value={selectedWorkspaceId} onChange={(event) => setSelectedWorkspaceId(event.target.value)}>
                {workspaces.map((workspace) => (
                  <option key={workspace.id} value={workspace.id}>{workspace.name || t("ai.untitledWorkspace")}</option>
                ))}
              </select>
            </label>
            <label>
              {t("ai.provider")}
              <select value={selectedProviderId} onChange={(event) => setSelectedProviderId(event.target.value)}>
                <option value="">{t("ai.localPlanning")}</option>
                {providers.map((provider) => (
                  <option key={provider.id} value={provider.id}>
                    {provider.providerName} / {provider.model}
                  </option>
                ))}
              </select>
            </label>
            <button
              className="primary-button agent-run-button"
              type="submit"
              disabled={creatingJob || !canCreateJob}
              title={!canCreateJob ? t("ai.createTitleDisabled") : t("ai.createJob")}
            >
              <Play size={18} />
              {creatingJob ? t("ai.queueing") : t("ai.createJob")}
            </button>
          </div>
        </form>
      </section>

      <details className="panel agent-settings">
        <summary>
          <span><Settings2 size={18} /> {t("ai.providerSettings")}</span>
          <small>{t("ai.keysBackend")}</small>
        </summary>
        <form className="agent-form" onSubmit={saveProvider}>
          <div className="form-grid-2">
            <label>
              {t("ai.provider")}
              <input
                value={providerForm.providerName}
                onChange={(event) => setProviderForm((current) => ({ ...current, providerName: event.target.value }))}
              />
            </label>
            <label>
              {t("ai.model")}
              <input
                value={providerForm.model}
                onChange={(event) => setProviderForm((current) => ({ ...current, model: event.target.value }))}
              />
            </label>
          </div>
          <label>
            {t("ai.baseUrl")}
            <input
              value={providerForm.baseUrl}
              onChange={(event) => setProviderForm((current) => ({ ...current, baseUrl: event.target.value }))}
              placeholder="https://api.openai.com/v1"
            />
          </label>
          <label>
            {t("ai.apiKey")}
            <input
              value={providerForm.apiKey}
              onChange={(event) => setProviderForm((current) => ({ ...current, apiKey: event.target.value }))}
              type="password"
              autoComplete="off"
            />
          </label>
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={providerForm.supportsToolCalls}
              onChange={(event) => setProviderForm((current) => ({ ...current, supportsToolCalls: event.target.checked }))}
            />
            {t("ai.supportsTools")}
          </label>
          <button
            className="primary-button"
            type="submit"
            disabled={savingProvider || !providerForm.apiKey.trim()}
            title={!providerForm.apiKey.trim() ? t("ai.enterKey") : t("ai.saveProvider")}
          >
            <KeyRound size={18} />
            {savingProvider ? t("ai.saving") : t("ai.saveProvider")}
          </button>
        </form>
      </details>

      <section className="agent-workbench">
        <article className="panel agent-run-panel">
          <div className="panel-header">
            <div>
              <h3>{t("ai.runTimeline")}</h3>
              <span aria-live="polite">
                {currentJob ? `${currentJobStatus} · ${currentJob.currentSubAgent || t("ai.mainAgent")}` : t("ai.noJob")}
              </span>
            </div>
            <Activity size={20} />
          </div>

          <div className="agent-stepper" aria-label={t("ai.lifecycleLabel")}>
            {lifecycleSteps.map((step, index) => (
              <div className={stepClass(step.key, currentJobStatus)} key={step.key}>
                <span>{index + 1}</span>
                <strong>{t(step.labelKey)}</strong>
                <p>{t(step.descriptionKey)}</p>
              </div>
            ))}
          </div>

          {currentJob ? (
            <div className="agent-run-summary">
              <div className="agent-status-row">
                <span className={`status-badge ${statusClass(currentJobStatus)}`}>{currentJobStatus}</span>
                <span>{formatDateTime(currentJob.updatedAtUtc) || t("ai.updatedNow")}</span>
              </div>
              <p>{currentJob.goal}</p>
              <div className="button-row">
                <button className="secondary-button" type="button" onClick={() => refreshJob()}>
                  <RefreshCw size={17} />
                  {t("ai.refresh")}
                </button>
                {hasActiveJob && (
                  <button className="secondary-button danger" type="button" onClick={cancelJob}>
                    <X size={17} />
                    {t("ai.cancel")}
                  </button>
                )}
              </div>
            </div>
          ) : (
            <div className="agent-empty-state">
              <ListChecks size={24} />
              <p>{t("ai.emptyJob")}</p>
            </div>
          )}
        </article>

        <article className="panel agent-approval-panel">
          <div className="panel-header">
            <div>
              <h3>{t("ai.approvalQueue")}</h3>
              <span>{t("ai.pendingActions", { count: pendingApprovals.length, suffix: pendingApprovals.length === 1 ? "" : "s" })}</span>
            </div>
            <ClipboardCheck size={20} />
          </div>

          <div className="approval-list" aria-live="polite">
            {pendingApprovals.length === 0 && (
              <div className="agent-empty-state compact">
                <Check size={22} />
                <p>{t("ai.noApprovals")}</p>
              </div>
            )}
            {pendingApprovals.map((approval) => (
              <ApprovalCard
                approval={approval}
                t={t}
                decidingApprovalId={decidingApprovalId}
                onApprove={() => decideApproval(approval, true)}
                onReject={() => decideApproval(approval, false)}
                key={approval.id}
              />
            ))}
          </div>
        </article>
      </section>

      <section className="agent-output-grid">
        <article className="panel agent-output-panel">
          <div className="panel-header">
            <div>
              <h3>{t("ai.artifacts")}</h3>
              <span>{t("ai.artifactsHelp")}</span>
            </div>
            <FileText size={20} />
          </div>
          <div className="artifact-groups">
            {artifacts.length === 0 && <p className="agent-muted">{t("ai.noArtifacts")}</p>}
            {Object.entries(artifactsByKind).map(([kind, items]) => (
              <div className="artifact-group" key={kind}>
                <h4>{kind}</h4>
                {items.map((artifact) => (
                  <details key={artifact.id} className="artifact-card">
                    <summary>{artifact.name}</summary>
                    <pre>{artifact.content || artifact.storageUrl || t("ai.noArtifactContent")}</pre>
                  </details>
                ))}
              </div>
            ))}
          </div>
        </article>

        <article className="panel agent-output-panel">
          <div className="panel-header">
            <div>
              <h3>{t("ai.eventLog")}</h3>
              <span>{t("ai.eventHelp")}</span>
            </div>
            <Activity size={20} />
          </div>
          <div className="event-list compact-events">
            {jobEvents.length === 0 && <p className="agent-muted">{t("ai.noEvents")}</p>}
            {jobEvents.map((event) => (
              <div className="event-row" key={event.id}>
                <span>{enumLabel(event.eventType, eventTypeLabels)}</span>
                <p>{event.message}</p>
                <time>{formatDateTime(event.createdAtUtc)}</time>
              </div>
            ))}
          </div>
        </article>
      </section>

      <section className="agent-context-grid">
        <article className="panel agent-context-card">
          <div className="panel-header">
            <div>
              <h3>{t("ai.projectContext")}</h3>
              <span>{t("ai.contextHelp")}</span>
            </div>
            <Bot size={20} />
          </div>
          <label>
            {t("ai.project")}
            <select value={selectedProjectId} onChange={(event) => setSelectedProjectId(event.target.value)}>
              {projects.map((project) => (
                <option key={project.id} value={project.id}>{project.name}</option>
              ))}
            </select>
          </label>
          <p>{loadingAi ? t("ai.generatingSummary") : summary?.result || t("ai.noSummary")}</p>
          <div className="recommendation-box">
            <ClipboardCheck size={18} />
            <span>{loadingAi ? t("ai.analyzingRisk") : riskAnalysis?.result || t("ai.noRisk")}</span>
          </div>
          <button className="secondary-button" type="button" onClick={refreshAi} disabled={!selectedProjectId || loadingAi}>
            <RefreshCw size={17} />
            {t("ai.refreshContext")}
          </button>
        </article>

        <article className="panel agent-context-card">
          <div className="panel-header">
            <div>
              <h3>{t("ai.quickCommand")}</h3>
              <span>{t("ai.quickHelp")}</span>
            </div>
            <Send size={20} />
          </div>
          <form className="message-input agent-command-inline" onSubmit={runCommand}>
            <input
              value={command}
              onChange={(event) => setCommand(event.target.value)}
              placeholder={selectedProject ? t("ai.askProject", { name: selectedProject.name }) : t("ai.ask")}
            />
            <button type="submit" disabled={commandLoading || !selectedWorkspaceId || !command.trim()} title={t("ai.runQuick")}>
              <Send size={18} />
            </button>
          </form>
          {commandResult && (
            <div className="agent-result-box">
              <strong>{t("ai.result")}</strong>
              <p>{commandResult.result || t("ai.noCommand")}</p>
            </div>
          )}
        </article>
      </section>

      {loadingProjects && <section className="panel">{t("ai.loadingProjects")}</section>}
      {!loadingProjects && projects.length === 0 && (
        <section className="panel">{t("ai.noProjects")}</section>
      )}
    </div>
  );
}

function ApprovalCard({ approval, decidingApprovalId, onApprove, onReject, t }) {
  const preview = approvalPreview(approval, t);
  const isBusy = decidingApprovalId === approval.id;

  return (
    <div className="approval-card">
      <div className="approval-card-header">
        <div>
          <strong>{approval.title}</strong>
          <p>{preview.actionName} · {preview.targetEntityType}</p>
        </div>
        <span className="status-badge needsapproval">{t("ai.dryRun")}</span>
      </div>

      <div className="approval-impact-grid">
        <div>
          <span>{t("ai.action")}</span>
          <strong>{preview.actionName}</strong>
        </div>
        <div>
          <span>{t("ai.target")}</span>
          <strong>{preview.targetEntityType}</strong>
        </div>
        <div>
          <span>{t("ai.risk")}</span>
          <strong>{preview.risk}</strong>
        </div>
      </div>

      <dl className="approval-fields">
        {preview.fields.map(([key, value]) => (
          <div key={key}>
            <dt>{humanizeKey(key, t)}</dt>
            <dd>{formatPreviewValue(value)}</dd>
          </div>
        ))}
      </dl>

      <details className="raw-json">
        <summary>{t("ai.viewRawJson")}</summary>
        <pre>{formatJson(approval.previewJson)}</pre>
      </details>

      <div className="button-row">
        <button className="primary-button small" type="button" disabled={isBusy} onClick={onApprove}>
          <Check size={16} />
          {t("ai.approve")}
        </button>
        <button className="secondary-button danger" type="button" disabled={isBusy} onClick={onReject}>
          <X size={16} />
          {t("ai.reject")}
        </button>
      </div>
    </div>
  );
}

function approvalPreview(approval, t) {
  const preview = parseJson(approval.previewJson) || {};
  const payload = preview.payload || parseJson(approval.payloadJson) || {};
  const fields = Object.entries(payload).filter(([, value]) => value !== null && typeof value !== "undefined");

  return {
    actionName: preview.actionName || approval.actionName || t("ai.reviewAction"),
    targetEntityType: preview.targetEntityType || approval.targetEntityType || t("ai.taskflowRecord"),
    risk: preview.dryRun ? t("ai.needsConfirmation") : t("ai.reviewRequired"),
    fields: fields.length > 0 ? fields : [["preview", approval.previewJson]],
  };
}

function groupArtifacts(artifacts) {
  return artifacts.reduce((groups, artifact) => {
    const kind = enumLabel(artifact.kind, artifactKindLabels) || "Other";
    return {
      ...groups,
      [kind]: [...(groups[kind] || []), artifact],
    };
  }, {});
}

function providerSummary(provider, t) {
  if (!provider) return t("ai.localPlanning");
  return t("ai.providerSummary", {
    provider: provider.providerName,
    model: provider.model,
    tools: provider.supportsToolCalls ? t("ai.tools") : t("ai.noTools"),
  });
}

function stepClass(stepKey, currentStatus) {
  const currentIndex = lifecycleIndex(currentStatus);
  const stepIndex = lifecycleSteps.findIndex((step) => step.key === stepKey);
  const isCurrent = stepKey === currentStatus || (currentStatus === "Paused" && stepKey === "NeedsApproval");
  const isComplete = currentIndex > stepIndex;
  return ["agent-step", isCurrent ? "current" : "", isComplete ? "complete" : ""].filter(Boolean).join(" ");
}

function lifecycleIndex(status) {
  if (status === "Paused") return lifecycleSteps.findIndex((step) => step.key === "NeedsApproval");
  if (status === "Failed" || status === "Canceled") return lifecycleSteps.findIndex((step) => step.key === "Completed");
  return Math.max(0, lifecycleSteps.findIndex((step) => step.key === status));
}

function statusClass(status) {
  return String(status || "").toLowerCase();
}

function formatJson(value) {
  if (!value) return "";

  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function parseJson(value) {
  if (!value || typeof value !== "string") return null;

  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function enumLabel(value, labels) {
  if (typeof value === "number") return labels[value] || String(value);
  if (typeof value === "string") return value;
  return "";
}

function humanizeKey(value, t) {
  if (value === "preview") return t("ai.field.preview");

  return String(value)
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/id$/i, "ID")
    .replace(/^./, (letter) => letter.toUpperCase());
}

function formatPreviewValue(value) {
  if (typeof value === "string" && value.length > 48 && /^[0-9a-f-]{32,}$/i.test(value)) {
    return value.slice(0, 8);
  }

  if (typeof value === "object" && value !== null) {
    return JSON.stringify(value);
  }

  return String(value);
}

import { useEffect, useMemo, useState } from "react";
import { Bot, Check, ClipboardCheck, FileText, KeyRound, Play, RefreshCw, Send, X } from "lucide-react";
import {
  agent,
  ai,
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject, selectPrimaryWorkspace } from "../api/mappers.js";

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

export default function AIAssistant() {
  const [projects, setProjects] = useState([]);
  const [selectedWorkspaceId, setSelectedWorkspaceId] = useState("");
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [summary, setSummary] = useState(null);
  const [riskAnalysis, setRiskAnalysis] = useState(null);
  const [command, setCommand] = useState("Summarize current workspace priorities for the presentation.");
  const [agentGoal, setAgentGoal] = useState("Summarize this workspace, create follow-up tasks and reminders for the demo.");
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
        const providerItems = asArray(await ai.providers());

        if (active) {
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
    if (!currentJob?.id || ["Completed", "Failed", "Canceled", "Paused"].includes(enumLabel(currentJob.status, jobStatusLabels))) {
      return undefined;
    }

    const intervalId = window.setInterval(() => {
      refreshJob(currentJob.id);
    }, 3000);

    return () => window.clearInterval(intervalId);
  }, [currentJob?.id, currentJob?.status]);

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
    if (!selectedWorkspaceId || !agentGoal.trim()) return;

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

  const pendingApprovals = asArray(currentJob?.approvals)
    .filter((approval) => enumLabel(approval.status, approvalStatusLabels) === "Pending");
  const artifacts = asArray(currentJob?.artifacts);
  const currentJobStatus = enumLabel(currentJob?.status, jobStatusLabels);
  const hasActiveJob = currentJob && !["Completed", "Failed", "Canceled"].includes(currentJobStatus);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">AI Assistant</p>
          <h1>AI project summary</h1>
        </div>
      </div>

      {error && <section className="panel"><strong>AI API error.</strong><p>{error}</p></section>}
      {loadingProjects && <section className="panel">Loading projects from Azure...</section>}
      {!loadingProjects && projects.length === 0 && (
        <section className="panel">No projects found for AI analysis.</section>
      )}

      {!loadingProjects && projects.length > 0 && (
        <section className="ai-layout staggered-ai-layout">
          <article className="panel ai-card ai-summary-card">
            <div className="ai-icon"><Bot size={42} /></div>
            <h3>{selectedProject ? selectedProject.name : "Current summary"}</h3>
            <p>
              {loadingAi ? "Generating project summary..." : summary?.result || "No summary returned yet."}
            </p>
            <div className="recommendation-box">
              <ClipboardCheck size={18} />
              <span>{loadingAi ? "Analyzing project risk..." : riskAnalysis?.result || "No risk analysis returned yet."}</span>
            </div>
          </article>

          <article className="panel ai-chat ai-chat-card">
            <div className="message-row assistant">
              <div className="notification-icon ai-chat-icon">AI</div>
              <div>
                <strong>TaskFlow AI</strong>
                <p>Project summary, risk analysis, and commands are loaded from the deployed AI endpoints.</p>
              </div>
            </div>
            <div className="message-row user-message">
              <p>Summarize the selected project and list the main risks before the presentation.</p>
            </div>
            {commandResult && (
              <div className="message-row assistant">
                <div className="notification-icon ai-chat-icon">AI</div>
                <div>
                  <strong>AI command result</strong>
                  <p>{commandResult.result || "No command result returned yet."}</p>
                </div>
              </div>
            )}
            {summary?.sources?.length > 0 && (
              <div className="message-row">
                <div className="activity-index message-index">S</div>
                <div>
                  <strong>Sources</strong>
                  <p>{summary.sources.join(", ")}</p>
                </div>
              </div>
            )}
            <form className="message-input" onSubmit={runCommand}>
              <input
                value={command}
                onChange={(event) => setCommand(event.target.value)}
                placeholder={selectedProject ? `Ask about ${selectedProject.name}` : "Ask TaskFlow AI"}
              />
              <button type="submit" disabled={commandLoading || !selectedWorkspaceId || !command.trim()}>
                <Send size={18} />
              </button>
            </form>
          </article>
        </section>
      )}

      {!loadingProjects && (
        <section className="agent-grid">
          <article className="panel agent-panel">
            <div className="panel-header">
              <div>
                <h3>Provider</h3>
                <span>Keys are submitted to the backend and never stored in browser storage.</span>
              </div>
              <KeyRound size={20} />
            </div>

            <form className="agent-form" onSubmit={saveProvider}>
              <div className="form-grid-2">
                <label>
                  Provider
                  <input
                    value={providerForm.providerName}
                    onChange={(event) => setProviderForm((current) => ({ ...current, providerName: event.target.value }))}
                  />
                </label>
                <label>
                  Model
                  <input
                    value={providerForm.model}
                    onChange={(event) => setProviderForm((current) => ({ ...current, model: event.target.value }))}
                  />
                </label>
              </div>
              <label>
                Base URL
                <input
                  value={providerForm.baseUrl}
                  onChange={(event) => setProviderForm((current) => ({ ...current, baseUrl: event.target.value }))}
                  placeholder="https://api.openai.com/v1"
                />
              </label>
              <label>
                API Key
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
                Supports tool calls
              </label>
              <button className="primary-button" type="submit" disabled={savingProvider || !providerForm.apiKey.trim()}>
                <KeyRound size={18} />
                {savingProvider ? "Saving..." : "Save provider"}
              </button>
            </form>

            <div className="provider-list">
              {providers.length === 0 && <p>No provider configured. The worker will use deterministic planning.</p>}
              {providers.length > 0 && (
                <label>
                  Active provider
                  <select value={selectedProviderId} onChange={(event) => setSelectedProviderId(event.target.value)}>
                    <option value="">Deterministic local planning</option>
                    {providers.map((provider) => (
                      <option key={provider.id} value={provider.id}>
                        {provider.providerName} / {provider.model} {provider.supportsToolCalls ? "(tools)" : "(no tools)"}
                      </option>
                    ))}
                  </select>
                </label>
              )}
            </div>
          </article>

          <article className="panel agent-panel">
            <div className="panel-header">
              <div>
                <h3>Agent job</h3>
                <span>Long-running work is queued and processed by TaskFlow.AgentWorker.</span>
              </div>
              <Bot size={20} />
            </div>

            <form className="agent-form" onSubmit={createAgentJob}>
              <label>
                Goal
                <textarea
                  value={agentGoal}
                  onChange={(event) => setAgentGoal(event.target.value)}
                  rows={4}
                />
              </label>
              <div className="button-row">
                <button className="primary-button" type="submit" disabled={creatingJob || !selectedWorkspaceId || !agentGoal.trim()}>
                  <Play size={18} />
                  {creatingJob ? "Queueing..." : "Create job"}
                </button>
                {currentJob && (
                  <button className="secondary-button" type="button" onClick={() => refreshJob()}>
                    <RefreshCw size={17} />
                    Refresh
                  </button>
                )}
                {hasActiveJob && (
                  <button className="secondary-button danger" type="button" onClick={cancelJob}>
                    <X size={17} />
                    Cancel
                  </button>
                )}
              </div>
            </form>

            {currentJob && (
              <div className="agent-job-card">
                <div className="agent-status-row">
                  <span className={`status-badge ${currentJobStatus.toLowerCase()}`}>{currentJobStatus}</span>
                  <span>{currentJob.currentSubAgent || "MainAgent"}</span>
                </div>
                {currentJob.errorMessage && <p className="error-text">{currentJob.errorMessage}</p>}
                {currentJob.planJson && (
                  <div className="agent-json-block">
                    <strong>Plan</strong>
                    <pre>{formatJson(currentJob.planJson)}</pre>
                  </div>
                )}
              </div>
            )}
          </article>
        </section>
      )}

      {currentJob && (
        <section className="agent-grid">
          <article className="panel agent-panel">
            <div className="panel-header">
              <div>
                <h3>Approvals</h3>
                <span>Every write action must be previewed and confirmed.</span>
              </div>
              <ClipboardCheck size={20} />
            </div>

            <div className="approval-list">
              {pendingApprovals.length === 0 && <p>No pending approvals.</p>}
              {pendingApprovals.map((approval) => (
                <div className="approval-card" key={approval.id}>
                  <div className="agent-status-row">
                    <strong>{approval.title}</strong>
                    <span className="status-badge planning">{approval.approvalType}</span>
                  </div>
                  <pre>{formatJson(approval.previewJson)}</pre>
                  <div className="button-row">
                    <button
                      className="primary-button small"
                      type="button"
                      disabled={decidingApprovalId === approval.id}
                      onClick={() => decideApproval(approval, true)}
                    >
                      <Check size={16} />
                      Approve
                    </button>
                    <button
                      className="secondary-button danger"
                      type="button"
                      disabled={decidingApprovalId === approval.id}
                      onClick={() => decideApproval(approval, false)}
                    >
                      <X size={16} />
                      Reject
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </article>

          <article className="panel agent-panel">
            <div className="panel-header">
              <div>
                <h3>Artifacts & events</h3>
                <span>Worker output and audit timeline.</span>
              </div>
              <FileText size={20} />
            </div>

            <div className="artifact-list">
              {artifacts.length === 0 && <p>No artifacts yet.</p>}
              {artifacts.map((artifact) => (
                <details key={artifact.id} className="artifact-card">
                  <summary>{artifact.name} · {enumLabel(artifact.kind, artifactKindLabels)}</summary>
                  <pre>{artifact.content || artifact.storageUrl || "No artifact content."}</pre>
                </details>
              ))}
            </div>

            <div className="event-list">
              {jobEvents.map((event) => (
                <div className="event-row" key={event.id}>
                  <span>{enumLabel(event.eventType, eventTypeLabels)}</span>
                  <p>{event.message}</p>
                </div>
              ))}
            </div>
          </article>
        </section>
      )}
    </div>
  );
}

function formatJson(value) {
  if (!value) return "";

  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

function enumLabel(value, labels) {
  if (typeof value === "number") return labels[value] || String(value);
  if (typeof value === "string") return value;
  return "";
}

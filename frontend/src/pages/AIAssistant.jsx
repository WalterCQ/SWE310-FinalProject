import { useEffect, useMemo, useState } from "react";
import { Bot, Send, ClipboardCheck } from "lucide-react";
import {
  ai,
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject } from "../api/mappers.js";

export default function AIAssistant() {
  const [projects, setProjects] = useState([]);
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [summary, setSummary] = useState(null);
  const [riskAnalysis, setRiskAnalysis] = useState(null);
  const [loadingProjects, setLoadingProjects] = useState(true);
  const [loadingAi, setLoadingAi] = useState(false);
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
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));

        if (active) {
          setProjects(mappedProjects);
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
                <p>Project summary and risk analysis are loaded from the deployed AI endpoints.</p>
              </div>
            </div>
            <div className="message-row user-message">
              <p>Summarize the selected project and list the main risks before the presentation.</p>
            </div>
            {summary?.sources?.length > 0 && (
              <div className="message-row">
                <div className="activity-index message-index">S</div>
                <div>
                  <strong>Sources</strong>
                  <p>{summary.sources.join(", ")}</p>
                </div>
              </div>
            )}
            <div className="message-input">
              <input value={selectedProject ? selectedProject.name : "No project selected"} readOnly />
              <button onClick={refreshAi} disabled={loadingAi || !selectedProjectId}><Send size={18} /></button>
            </div>
          </article>
        </section>
      )}
    </div>
  );
}

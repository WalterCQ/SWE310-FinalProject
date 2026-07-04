import { useEffect, useMemo, useRef, useState } from "react";
import { Search } from "lucide-react";
import { useNavigate } from "react-router-dom";
import {
  channels as channelsApi,
  formatApiError,
  projects as projectsApi,
  tasks as tasksApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import {
  asArray,
  mapChannel,
  mapProject,
  mapTask,
  mapWorkspace,
} from "../api/mappers.js";
import { useI18n } from "../i18n.jsx";

const MIN_SEARCH_LENGTH = 2;
const MAX_RESULTS = 8;

function normalizeText(value) {
  return String(value || "").trim().toLowerCase();
}

function buildSearchText(parts) {
  return parts.filter(Boolean).map(normalizeText).join(" ");
}

function byDeadline(left, right) {
  const leftTime = left.deadlineUtc ? new Date(left.deadlineUtc).getTime() : Number.MAX_SAFE_INTEGER;
  const rightTime = right.deadlineUtc ? new Date(right.deadlineUtc).getTime() : Number.MAX_SAFE_INTEGER;
  return leftTime - rightTime;
}

export default function GlobalSearch() {
  const { locale, t } = useI18n();
  const navigate = useNavigate();
  const inputRef = useRef(null);
  const [query, setQuery] = useState("");
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    setItems([]);
    setLoaded(false);
    setError("");
  }, [locale]);

  async function loadSearchIndex() {
    if (loading || loaded) return;

    setLoading(true);
    setError("");

    try {
      const workspaceItems = asArray(await workspacesApi.list()).map(mapWorkspace);
      const workspaceLookup = Object.fromEntries(workspaceItems.map((workspace) => [workspace.id, workspace]));

      const projectGroups = await Promise.all(
        workspaceItems.map(async (workspace) => {
          const data = await projectsApi.listByWorkspace(workspace.id);
          return asArray(data).map((project) => ({
            ...mapProject(project),
            workspaceId: workspace.id,
            workspaceName: workspace.name,
          }));
        })
      );
      const projectItems = projectGroups.flat();
      const projectLookup = Object.fromEntries(projectItems.map((project) => [project.id, project]));

      const channelGroups = await Promise.all(
        workspaceItems.map(async (workspace) => {
          const data = await channelsApi.listByWorkspace(workspace.id);
          return asArray(data).map((channel) => ({
            ...mapChannel(channel),
            workspaceId: workspace.id,
            workspaceName: workspace.name,
          }));
        })
      );

      const taskGroups = await Promise.all(
        projectItems.map(async (project) => {
          const data = await tasksApi.listByProject(project.id);
          return asArray(data).map((task) => ({
            ...mapTask(task, projectLookup),
            projectId: project.id,
            projectName: project.name,
            workspaceId: project.workspaceId,
            workspaceName: project.workspaceName || workspaceLookup[project.workspaceId]?.name || "",
          }));
        })
      );

      const nextItems = [
        ...workspaceItems.map((workspace) => ({
          id: `workspace-${workspace.id}`,
          type: t("globalSearch.type.workspace"),
          title: workspace.name,
          subtitle: t("globalSearch.workspaceSubtitle", {
            projects: workspace.projects,
            members: workspace.members,
          }),
          path: `/workspaces?workspaceId=${workspace.id}`,
          searchText: buildSearchText([workspace.name, workspace.description, "workspace"]),
        })),
        ...projectItems.map((project) => ({
          id: `project-${project.id}`,
          type: t("globalSearch.type.project"),
          title: project.name,
          subtitle: [project.workspaceName, project.owner, project.statusLabel].filter(Boolean).join(" / "),
          path: `/projects?projectId=${project.id}`,
          searchText: buildSearchText([project.name, project.description, project.owner, project.statusLabel, "project"]),
        })),
        ...channelGroups.flat().map((channel) => ({
          id: `channel-${channel.id}`,
          type: t("globalSearch.type.channel"),
          title: `# ${channel.name}`,
          subtitle: channel.workspaceName || t("globalSearch.channelSubtitle"),
          path: `/channels?channelId=${channel.id}`,
          searchText: buildSearchText([channel.name, channel.description, channel.workspaceName, "channel"]),
        })),
        ...taskGroups.flat().sort(byDeadline).map((task) => ({
          id: `task-${task.id}`,
          type: t("globalSearch.type.task"),
          title: task.title,
          subtitle: [task.projectName || task.project, task.assignee, task.statusLabel, task.deadlineLabel]
            .filter(Boolean)
            .join(" / "),
          path: `/tasks?projectId=${task.projectId}&taskId=${task.id}`,
          searchText: buildSearchText([
            task.title,
            task.description,
            task.projectName || task.project,
            task.assignee,
            task.statusLabel,
            task.priorityLabel,
            "task",
          ]),
        })),
      ];

      setItems(nextItems);
      setLoaded(true);
    } catch (apiError) {
      setError(formatApiError(apiError));
    } finally {
      setLoading(false);
    }
  }

  const trimmedQuery = query.trim();
  const results = useMemo(() => {
    const normalizedQuery = normalizeText(trimmedQuery);
    if (normalizedQuery.length < MIN_SEARCH_LENGTH) return [];

    return items
      .filter((item) => item.searchText.includes(normalizedQuery))
      .slice(0, MAX_RESULTS);
  }, [items, trimmedQuery]);

  useEffect(() => {
    setActiveIndex(0);
  }, [trimmedQuery]);

  function openSearch() {
    setOpen(true);
    loadSearchIndex();
  }

  function selectResult(result) {
    if (!result) return;

    setQuery("");
    setOpen(false);
    navigate(result.path);
  }

  function handleInputChange(event) {
    const nextQuery = event.target.value;
    setQuery(nextQuery);
    setOpen(true);
    if (nextQuery.trim().length >= MIN_SEARCH_LENGTH) loadSearchIndex();
  }

  function handleKeyDown(event) {
    if (event.key === "Escape") {
      setOpen(false);
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      setOpen(true);
      setActiveIndex((index) => Math.min(index + 1, Math.max(results.length - 1, 0)));
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex((index) => Math.max(index - 1, 0));
      return;
    }

    if (event.key === "Enter" && open && results.length > 0) {
      event.preventDefault();
      selectResult(results[activeIndex] || results[0]);
    }
  }

  const showPanel = open && trimmedQuery.length >= MIN_SEARCH_LENGTH;

  return (
    <div className="global-search" onBlur={() => window.setTimeout(() => setOpen(false), 120)}>
      <div className="search-box">
        <Search size={18} />
        <input
          ref={inputRef}
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={showPanel}
          aria-controls="global-search-results"
          aria-activedescendant={showPanel && results[activeIndex] ? `global-search-${results[activeIndex].id}` : undefined}
          placeholder={t("topbar.search")}
          value={query}
          onFocus={openSearch}
          onChange={handleInputChange}
          onKeyDown={handleKeyDown}
        />
      </div>

      {showPanel && (
        <div className="global-search-panel" id="global-search-results" role="listbox" aria-label={t("globalSearch.results")}>
          {loading && <div className="global-search-state">{t("globalSearch.loading")}</div>}
          {error && <div className="global-search-state error-text">{error}</div>}
          {!loading && !error && results.length === 0 && (
            <div className="global-search-state">{loaded ? t("globalSearch.noResults") : t("globalSearch.loading")}</div>
          )}
          {!loading && !error && results.map((result, index) => (
            <button
              key={result.id}
              id={`global-search-${result.id}`}
              className={index === activeIndex ? "active" : ""}
              type="button"
              role="option"
              aria-selected={index === activeIndex}
              onMouseEnter={() => setActiveIndex(index)}
              onMouseDown={(event) => {
                event.preventDefault();
                selectResult(result);
              }}
            >
              <span>{result.type}</span>
              <strong>{result.title}</strong>
              <small>{result.subtitle}</small>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

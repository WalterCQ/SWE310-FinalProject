import { useEffect, useMemo, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import Avatar from "../components/Avatar.jsx";
import StatusBadge from "../components/StatusBadge.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import {
  formatApiError,
  projects as projectsApi,
  tasks as tasksApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject, mapTask } from "../api/mappers.js";
import { enumPriorityKey, enumTaskStatusKey, useI18n } from "../i18n.jsx";

const blankForm = {
  title: "",
  description: "",
  status: "0",
  priority: "1",
  dueDate: "",
};

const statusColumns = ["To Do", "In Progress", "Blocked", "Done"];
const statusOptions = [
  { value: "0", label: "To Do" },
  { value: "1", label: "In Progress" },
  { value: "2", label: "Blocked" },
  { value: "3", label: "Done" },
];
const priorityOptions = [
  { value: "0", label: "Low" },
  { value: "1", label: "Medium" },
  { value: "2", label: "High" },
];

function toDeadlineUtc(dateValue) {
  if (!dateValue) return null;
  return new Date(`${dateValue}T00:00:00.000Z`).toISOString();
}

function buildTaskUpdatePayload(task, updates = {}) {
  const priority = typeof updates.priority === "undefined" ? task.priority : updates.priority;

  return {
    title: task.title,
    description: task.description || "",
    priority: Number(priority),
    assigneeId: task.assigneeId || null,
    deadlineUtc: task.deadlineUtc || null,
  };
}

function compareTasksByDeadline(left, right) {
  const leftTime = left.deadlineUtc ? new Date(left.deadlineUtc).getTime() : Number.MAX_SAFE_INTEGER;
  const rightTime = right.deadlineUtc ? new Date(right.deadlineUtc).getTime() : Number.MAX_SAFE_INTEGER;
  return leftTime - rightTime;
}

export default function Tasks() {
  const { t } = useI18n();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedProjectParam = searchParams.get("projectId") || "";
  const selectedTaskId = searchParams.get("taskId") || "";
  const isMyTasksView = searchParams.get("view") === "mine";
  const currentUserId = localStorage.getItem("userId") || "";
  const [projects, setProjects] = useState([]);
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [tasks, setTasks] = useState([]);
  const [form, setForm] = useState(blankForm);
  const [errors, setErrors] = useState({});
  const [loadingProjects, setLoadingProjects] = useState(true);
  const [loadingTasks, setLoadingTasks] = useState(false);
  const [saving, setSaving] = useState(false);
  const [mutatingTaskId, setMutatingTaskId] = useState("");
  const [apiError, setApiError] = useState("");

  const projectLookup = useMemo(() => {
    return Object.fromEntries(projects.map((project) => [project.id, project]));
  }, [projects]);

  const groupedTasks = useMemo(() => {
    return statusColumns.map((status) => ({
      status,
      items: tasks.filter((task) => task.statusLabel === status),
    }));
  }, [tasks]);

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setLoadingProjects(true);
      setApiError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list());
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));

        if (active) {
          setProjects(mappedProjects);
          setSelectedProjectId(
            mappedProjects.some((project) => project.id === selectedProjectParam)
              ? selectedProjectParam
              : mappedProjects[0]?.id || ""
          );
        }
      } catch (error) {
        if (active) setApiError(formatApiError(error));
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
    if (!selectedProjectParam || projects.length === 0) return;
    if (projects.some((project) => project.id === selectedProjectParam)) {
      setSelectedProjectId(selectedProjectParam);
    }
  }, [selectedProjectParam, projects]);

  useEffect(() => {
    let active = true;

    async function loadTasks() {
      setLoadingTasks(true);
      setApiError("");

      try {
        const visibleTasks = await fetchVisibleTasks();
        if (active) setTasks(visibleTasks);
      } catch (error) {
        if (active) setApiError(formatApiError(error));
      } finally {
        if (active) setLoadingTasks(false);
      }
    }

    loadTasks();

    return () => {
      active = false;
    };
  }, [selectedProjectId, projectLookup, projects, isMyTasksView, currentUserId]);

  useEffect(() => {
    if (!selectedTaskId || loadingTasks) return;

    document.getElementById(`task-${selectedTaskId}`)?.scrollIntoView({
      block: "center",
      behavior: "smooth",
    });
  }, [selectedTaskId, loadingTasks, tasks]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!selectedProjectId) nextErrors.project = t("task.projectRequired");
    if (!form.title.trim()) nextErrors.title = t("task.titleRequired");
    if (!form.description.trim()) nextErrors.description = t("task.descriptionRequired");
    if (!form.status) nextErrors.status = t("task.statusRequired");
    if (!form.priority) nextErrors.priority = t("task.priorityRequired");
    if (!form.dueDate) nextErrors.dueDate = t("task.dueDateRequired");
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function fetchVisibleTasks(projectId = selectedProjectId) {
    if (isMyTasksView) {
      if (!currentUserId || projects.length === 0) return [];

      const taskGroups = await Promise.all(projects.map((project) => tasksApi.listByProject(project.id)));
      return taskGroups
        .flatMap((group) => asArray(group).map((task) => mapTask(task, projectLookup)))
        .filter((task) => task.assigneeId === currentUserId)
        .sort(compareTasksByDeadline);
    }

    if (!projectId) return [];

    const data = await tasksApi.listByProject(projectId);
    return asArray(data).map((task) => mapTask(task, projectLookup));
  }

  async function loadProjectTasks(projectId = selectedProjectId) {
    setTasks(await fetchVisibleTasks(projectId));
  }

  async function addTask(event) {
    event.preventDefault();
    if (!validate()) return;

    setSaving(true);
    setApiError("");

    try {
      const createdTask = await tasksApi.create(selectedProjectId, {
        title: form.title.trim(),
        description: form.description.trim(),
        priority: Number(form.priority),
        assigneeId: null,
        deadlineUtc: toDeadlineUtc(form.dueDate),
      });

      if (createdTask?.id && form.status !== "0") {
        await tasksApi.updateStatus(createdTask.id, Number(form.status));
      }

      setForm(blankForm);
      await loadProjectTasks();
    } catch (error) {
      setApiError(formatApiError(error));
    } finally {
      setSaving(false);
    }
  }

  async function updateTaskStatus(task, status) {
    setMutatingTaskId(task.id);
    setApiError("");

    try {
      await tasksApi.updateStatus(task.id, Number(status));
      await loadProjectTasks();
    } catch (error) {
      setApiError(formatApiError(error));
    } finally {
      setMutatingTaskId("");
    }
  }

  async function updateTaskPriority(task, priority) {
    setMutatingTaskId(task.id);
    setApiError("");

    try {
      await tasksApi.update(task.id, buildTaskUpdatePayload(task, { priority }));
      await loadProjectTasks();
    } catch (error) {
      setApiError(formatApiError(error));
    } finally {
      setMutatingTaskId("");
    }
  }

  async function deleteTask(task) {
    setMutatingTaskId(task.id);
    setApiError("");

    try {
      await tasksApi.remove(task.id);
      await loadProjectTasks();
    } catch (error) {
      setApiError(formatApiError(error));
    } finally {
      setMutatingTaskId("");
    }
  }

  return (
    <div className="page-stack">
      {isMyTasksView && (
        <div className="page-actions">
          <button className="secondary-button" type="button" onClick={() => setSearchParams({})}>
            {t("task.viewAll")}
          </button>
        </div>
      )}

      {apiError && <section className="panel"><strong>{t("task.apiError")}</strong><p>{apiError}</p></section>}

      <section className={`task-layout ${isMyTasksView ? "wide" : ""}`}>
        {!isMyTasksView && (
          <article className="panel form-panel">
            <div className="panel-header">
              <h3>{t("task.createTitle")}</h3>
              <span>{t("task.createHelp")}</span>
            </div>

            <form className="task-form" onSubmit={addTask} noValidate>
              <label>
                {t("task.project")}
                <select
                  value={selectedProjectId}
                  onChange={(event) => {
                    setSelectedProjectId(event.target.value);
                    setErrors({ ...errors, project: "" });
                  }}
                  disabled={loadingProjects || projects.length === 0}
                >
                  {projects.length === 0 && <option value="">{t("task.noProjectsOption")}</option>}
                  {projects.map((project) => (
                    <option key={project.id} value={project.id}>{project.name}</option>
                  ))}
                </select>
                <ErrorMessage>{errors.project}</ErrorMessage>
              </label>

              <label>
                {t("task.taskTitle")}
                <input name="title" value={form.title} onChange={updateField} placeholder={t("task.placeholder.title")} />
                <ErrorMessage>{errors.title}</ErrorMessage>
              </label>

              <label>
                {t("task.description")}
                <textarea name="description" value={form.description} onChange={updateField} placeholder={t("task.placeholder.description")} />
                <ErrorMessage>{errors.description}</ErrorMessage>
              </label>

              <div className="form-grid-2">
                <label>
                  {t("task.status")}
                  <select name="status" value={form.status} onChange={updateField}>
                    {statusOptions.map((option) => (
                      <option key={option.value} value={option.value}>{t(enumTaskStatusKey(option.label))}</option>
                    ))}
                  </select>
                  <ErrorMessage>{errors.status}</ErrorMessage>
                </label>

                <label>
                  {t("task.priority")}
                  <select name="priority" value={form.priority} onChange={updateField}>
                    {priorityOptions.map((option) => (
                      <option key={option.value} value={option.value}>{t(enumPriorityKey(option.label))}</option>
                    ))}
                  </select>
                  <ErrorMessage>{errors.priority}</ErrorMessage>
                </label>

                <label>
                  {t("task.dueDate")}
                  <input name="dueDate" type="date" value={form.dueDate} onChange={updateField} />
                  <ErrorMessage>{errors.dueDate}</ErrorMessage>
                </label>
              </div>

              <button className="primary-button" type="submit" disabled={saving || loadingProjects || !selectedProjectId}>
                <Plus size={18} /> {saving ? t("task.adding") : t("task.add")}
              </button>
            </form>
          </article>
        )}

        <section className="kanban-grid">
          {loadingProjects && <article className="panel">{t("task.loadingProjects")}</article>}
          {!loadingProjects && projects.length === 0 && <article className="panel">{t("task.noProjects")}</article>}

          {!loadingProjects && projects.length > 0 && groupedTasks.map((column) => (
            <article className="panel kanban-column" key={column.status}>
              <div className="panel-header">
                <h3>{t(enumTaskStatusKey(column.status))}</h3>
                <span>{column.items.length}</span>
              </div>
              <div className="kanban-list">
                {loadingTasks && column.status === "To Do" && <p>{t("task.loadingTasks")}</p>}
                {!loadingTasks && column.items.length === 0 && <p>{t("task.noTasks")}</p>}
                {!loadingTasks && column.items.map((task) => (
                  <div
                    className={`task-card ${task.id === selectedTaskId ? "target-highlight" : ""}`}
                    id={`task-${task.id}`}
                    key={task.id}
                  >
                    <div className="task-card-top">
                      <h4>{task.title}</h4>
                      <StatusBadge variant={task.priorityLabel}>{t(enumPriorityKey(task.priorityLabel))}</StatusBadge>
                    </div>
                    <p>{task.description}</p>
                    <div className="task-meta">
                      <span>{task.project}</span>
                      <span>{task.deadlineLabel}</span>
                    </div>
                    {task.assigneeId && (
                      <div className="task-assignee">
                        <Avatar className="mini-avatar" seed={task.assigneeId} name={task.assignee} ariaHidden />
                        <span className="person-name">{task.assignee}</span>
                      </div>
                    )}
                    <div className="task-actions">
                      <label>
                        <span>{t("task.status")}</span>
                        <select
                          value={String(task.status)}
                          onChange={(event) => updateTaskStatus(task, event.target.value)}
                          disabled={mutatingTaskId === task.id}
                        >
                          {statusOptions.map((option) => (
                            <option key={option.value} value={option.value}>{t(enumTaskStatusKey(option.label))}</option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <span>{t("task.priority")}</span>
                        <select
                          value={String(task.priority)}
                          onChange={(event) => updateTaskPriority(task, event.target.value)}
                          disabled={mutatingTaskId === task.id}
                        >
                          {priorityOptions.map((option) => (
                            <option key={option.value} value={option.value}>{t(enumPriorityKey(option.label))}</option>
                          ))}
                        </select>
                      </label>
                      <button
                        className="danger-button"
                        type="button"
                        onClick={() => deleteTask(task)}
                        disabled={mutatingTaskId === task.id}
                      >
                        <Trash2 size={14} /> {t("task.delete")}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </article>
          ))}
        </section>
      </section>
    </div>
  );
}

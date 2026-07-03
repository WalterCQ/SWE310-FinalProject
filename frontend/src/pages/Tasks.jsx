import { useEffect, useMemo, useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import StatusBadge from "../components/StatusBadge.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import {
  formatApiError,
  projects as projectsApi,
  tasks as tasksApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject, mapTask } from "../api/mappers.js";

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

export default function Tasks() {
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
          setSelectedProjectId(mappedProjects[0]?.id || "");
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
    let active = true;

    async function loadTasks() {
      if (!selectedProjectId) {
        setTasks([]);
        return;
      }

      setLoadingTasks(true);
      setApiError("");

      try {
        const data = await tasksApi.listByProject(selectedProjectId);
        if (active) setTasks(asArray(data).map((task) => mapTask(task, projectLookup)));
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
  }, [selectedProjectId, projectLookup]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!selectedProjectId) nextErrors.project = "Please select a project.";
    if (!form.title.trim()) nextErrors.title = "Please enter a task title.";
    if (!form.description.trim()) nextErrors.description = "Please enter a task description.";
    if (!form.status) nextErrors.status = "Please select a task status.";
    if (!form.priority) nextErrors.priority = "Please select a task priority.";
    if (!form.dueDate) nextErrors.dueDate = "Please select a due date.";
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function loadProjectTasks(projectId = selectedProjectId) {
    const data = await tasksApi.listByProject(projectId);
    setTasks(asArray(data).map((task) => mapTask(task, projectLookup)));
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
      <div className="page-heading">
        <div>
          <p className="eyebrow">Tasks</p>
          <h1>Create tasks your team can actually follow</h1>
        </div>
      </div>

      {apiError && <section className="panel"><strong>Task API error.</strong><p>{apiError}</p></section>}

      <section className="task-layout">
        <article className="panel form-panel">
          <div className="panel-header">
            <h3>Create task</h3>
            <span>Tasks are created in the selected Azure project</span>
          </div>

          <form className="task-form" onSubmit={addTask} noValidate>
            <label>
              Project
              <select
                value={selectedProjectId}
                onChange={(event) => {
                  setSelectedProjectId(event.target.value);
                  setErrors({ ...errors, project: "" });
                }}
                disabled={loadingProjects || projects.length === 0}
              >
                {projects.length === 0 && <option value="">No projects available</option>}
                {projects.map((project) => (
                  <option key={project.id} value={project.id}>{project.name}</option>
                ))}
              </select>
              <ErrorMessage>{errors.project}</ErrorMessage>
            </label>

            <label>
              Task title
              <input name="title" value={form.title} onChange={updateField} placeholder="Example: Record dashboard walkthrough" />
              <ErrorMessage>{errors.title}</ErrorMessage>
            </label>

            <label>
              Description
              <textarea name="description" value={form.description} onChange={updateField} placeholder="What needs to be done, and what will count as finished?" />
              <ErrorMessage>{errors.description}</ErrorMessage>
            </label>

            <div className="form-grid-2">
              <label>
                Status
                <select name="status" value={form.status} onChange={updateField}>
                  {statusOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
                <ErrorMessage>{errors.status}</ErrorMessage>
              </label>

              <label>
                Priority
                <select name="priority" value={form.priority} onChange={updateField}>
                  {priorityOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
                <ErrorMessage>{errors.priority}</ErrorMessage>
              </label>

              <label>
                Due date
                <input name="dueDate" type="date" value={form.dueDate} onChange={updateField} />
                <ErrorMessage>{errors.dueDate}</ErrorMessage>
              </label>
            </div>

            <button className="primary-button" type="submit" disabled={saving || loadingProjects || !selectedProjectId}>
              <Plus size={18} /> {saving ? "Adding..." : "Add task"}
            </button>
          </form>
        </article>

        <section className="kanban-grid">
          {loadingProjects && <article className="panel">Loading projects from Azure...</article>}
          {!loadingProjects && projects.length === 0 && <article className="panel">No projects found. Create a project before adding tasks.</article>}

          {!loadingProjects && projects.length > 0 && groupedTasks.map((column) => (
            <article className="panel kanban-column" key={column.status}>
              <div className="panel-header">
                <h3>{column.status}</h3>
                <span>{column.items.length}</span>
              </div>
              <div className="kanban-list">
                {loadingTasks && column.status === "To Do" && <p>Loading tasks...</p>}
                {!loadingTasks && column.items.length === 0 && <p>No tasks.</p>}
                {!loadingTasks && column.items.map((task) => (
                  <div className="task-card" key={task.id}>
                    <div className="task-card-top">
                      <h4>{task.title}</h4>
                      <StatusBadge>{task.priorityLabel}</StatusBadge>
                    </div>
                    <p>{task.description}</p>
                    <div className="task-meta">
                      <span>{task.project}</span>
                      <span>{task.deadlineLabel}</span>
                    </div>
                    <div className="task-actions">
                      <label>
                        <span>Status</span>
                        <select
                          value={String(task.status)}
                          onChange={(event) => updateTaskStatus(task, event.target.value)}
                          disabled={mutatingTaskId === task.id}
                        >
                          {statusOptions.map((option) => (
                            <option key={option.value} value={option.value}>{option.label}</option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <span>Priority</span>
                        <select
                          value={String(task.priority)}
                          onChange={(event) => updateTaskPriority(task, event.target.value)}
                          disabled={mutatingTaskId === task.id}
                        >
                          {priorityOptions.map((option) => (
                            <option key={option.value} value={option.value}>{option.label}</option>
                          ))}
                        </select>
                      </label>
                      <button
                        className="danger-button"
                        type="button"
                        onClick={() => deleteTask(task)}
                        disabled={mutatingTaskId === task.id}
                      >
                        <Trash2 size={14} /> Delete
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

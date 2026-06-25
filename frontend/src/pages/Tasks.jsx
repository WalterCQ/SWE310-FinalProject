import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import StatusBadge from "../components/StatusBadge.jsx";
import ErrorMessage from "../components/ErrorMessage.jsx";
import { tasks as initialTasks } from "../data/mockData.js";

const blankForm = {
  title: "",
  description: "",
  status: "To Do",
  priority: "Medium",
  dueDate: "",
  assignee: "",
};

export default function Tasks() {
  const [tasks, setTasks] = useState(initialTasks);
  const [form, setForm] = useState(blankForm);
  const [errors, setErrors] = useState({});

  const groupedTasks = useMemo(() => {
    return ["To Do", "In Progress", "Done"].map((status) => ({
      status,
      items: tasks.filter((task) => task.status === status),
    }));
  }, [tasks]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.title.trim()) nextErrors.title = "Please enter a task title.";
    if (!form.description.trim()) nextErrors.description = "Please enter a task description.";
    if (!form.status) nextErrors.status = "Please select a task status.";
    if (!form.priority) nextErrors.priority = "Please select a task priority.";
    if (!form.dueDate) nextErrors.dueDate = "Please select a due date.";
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  function addTask(event) {
    event.preventDefault();
    if (!validate()) return;

    const newTask = {
      id: Date.now(),
      ...form,
      project: "TaskFlow Connect",
    };

    setTasks([newTask, ...tasks]);
    setForm(blankForm);
  }

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Tasks</p>
          <h1>Create tasks your team can actually follow</h1>
        </div>
      </div>

      <section className="task-layout">
        <article className="panel form-panel">
          <div className="panel-header">
            <h3>Create task</h3>
            <span>Try submitting empty fields during the demo</span>
          </div>

          <form className="task-form" onSubmit={addTask} noValidate>
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
                  <option>To Do</option>
                  <option>In Progress</option>
                  <option>Done</option>
                </select>
                <ErrorMessage>{errors.status}</ErrorMessage>
              </label>

              <label>
                Priority
                <select name="priority" value={form.priority} onChange={updateField}>
                  <option>Low</option>
                  <option>Medium</option>
                  <option>High</option>
                </select>
                <ErrorMessage>{errors.priority}</ErrorMessage>
              </label>
            </div>

            <div className="form-grid-2">
              <label>
                Due date
                <input name="dueDate" type="date" value={form.dueDate} onChange={updateField} />
                <ErrorMessage>{errors.dueDate}</ErrorMessage>
              </label>

              <label>
                Assignee
                <input name="assignee" value={form.assignee} onChange={updateField} placeholder="Optional" />
              </label>
            </div>

            <button className="primary-button" type="submit"><Plus size={18} /> Add task</button>
          </form>
        </article>

        <section className="kanban-grid">
          {groupedTasks.map((column) => (
            <article className="panel kanban-column" key={column.status}>
              <div className="panel-header">
                <h3>{column.status}</h3>
                <span>{column.items.length}</span>
              </div>
              <div className="kanban-list">
                {column.items.map((task) => (
                  <div className="task-card" key={task.id}>
                    <div className="task-card-top">
                      <h4>{task.title}</h4>
                      <StatusBadge>{task.priority}</StatusBadge>
                    </div>
                    <p>{task.description}</p>
                    <div className="task-meta">
                      <span>{task.project}</span>
                      <span>{task.dueDate}</span>
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

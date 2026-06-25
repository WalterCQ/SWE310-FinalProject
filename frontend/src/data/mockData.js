export const currentUser = {
  name: "Oday",
  role: "Frontend / Demo",
  email: "oday@taskflow.com",
};

export const stats = [
  { label: "Total projects", value: 12, change: "+2", tone: "amber" },
  { label: "Total tasks", value: 128, change: "+15", tone: "green" },
  { label: "In progress", value: 45, change: "+8", tone: "yellow" },
  { label: "Completed", value: 68, change: "+25", tone: "done" },
  { label: "Overdue", value: 7, change: "-3", tone: "red" },
];

export const taskStatusData = [
  { name: "To Do", value: 45 },
  { name: "In Progress", value: 38 },
  { name: "Done", value: 45 },
];

export const priorityData = [
  { priority: "High", count: 32 },
  { priority: "Medium", count: 58 },
  { priority: "Low", count: 38 },
];

export const projects = [
  {
    id: 1,
    name: "TaskFlow frontend",
    status: "Active",
    progress: 75,
    owner: "Alice",
    dueDate: "25 Jun 2026",
  },
  {
    id: 2,
    name: "API integration",
    status: "Active",
    progress: 60,
    owner: "John",
    dueDate: "30 Jun 2026",
  },
  {
    id: 3,
    name: "Presentation prep",
    status: "Planning",
    progress: 35,
    owner: "Oday",
    dueDate: "5 Jul 2026",
  },
  {
    id: 4,
    name: "AI summary",
    status: "Blocked",
    progress: 50,
    owner: "Sarah",
    dueDate: "1 Jul 2026",
  },
];

export const tasks = [
  {
    id: 1,
    title: "Replace demo-only dashboard data",
    description: "Map cards and charts to the Dashboard endpoint after backend confirms response names.",
    status: "To Do",
    priority: "High",
    project: "TaskFlow frontend",
    dueDate: "28 Jun 2026",
    assignee: "Oday",
  },
  {
    id: 2,
    title: "Confirm task status values",
    description: "Ask backend whether status expects ToDo, InProgress, Done or readable labels.",
    status: "In Progress",
    priority: "High",
    project: "API integration",
    dueDate: "26 Jun 2026",
    assignee: "John",
  },
  {
    id: 3,
    title: "Record failed form validation",
    description: "Show empty title/date errors in the demo video to prove frontend validation is present.",
    status: "To Do",
    priority: "Medium",
    project: "Presentation prep",
    dueDate: "3 Jul 2026",
    assignee: "Oday",
  },
  {
    id: 4,
    title: "Create AI project summary route",
    description: "Display a clear answer from the Semantic Kernel endpoint using real project data.",
    status: "Done",
    priority: "Low",
    project: "AI summary",
    dueDate: "24 Jun 2026",
    assignee: "Sarah",
  },
  {
    id: 5,
    title: "Prepare backup screenshots",
    description: "Save dashboard, tasks, AI assistant, notifications, and validation screens in case localhost fails.",
    status: "In Progress",
    priority: "Medium",
    project: "Presentation prep",
    dueDate: "6 Jul 2026",
    assignee: "Oday",
  },
];

export const workspaces = [
  { id: 1, name: "Coursework", members: 4, projects: 3, color: "amber" },
  { id: 2, name: "Frontend", members: 2, projects: 2, color: "green" },
  { id: 3, name: "Backend", members: 2, projects: 4, color: "red" },
  { id: 4, name: "Demo", members: 4, projects: 2, color: "yellow" },
];

export const notifications = [
  { id: 1, title: "John changed the Tasks endpoint response", time: "2 minutes ago", type: "task" },
  { id: 2, title: "You were assigned to dashboard validation", time: "10 minutes ago", type: "assignment" },
  { id: 3, title: "Demo video checklist is due tomorrow", time: "1 hour ago", type: "warning" },
  { id: 4, title: "Sarah mentioned you in #frontend", time: "2 hours ago", type: "mention" },
];

export const messages = [
  { id: 1, sender: "John", text: "Swagger is deployed. Use the listed field names exactly.", time: "9:15 AM" },
  { id: 2, sender: "Sarah", text: "Please do not rename status or priority on the frontend before telling backend.", time: "9:18 AM" },
  { id: 3, sender: "Oday", text: "I will finish the dashboard shell first, then connect the API data.", time: "9:30 AM" },
  { id: 4, sender: "Mike", text: "I will prepare the testing checklist before the recording.", time: "9:42 AM" },
];

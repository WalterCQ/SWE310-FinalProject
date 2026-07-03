import { useEffect, useMemo, useState } from "react";
import { Plus, Search } from "lucide-react";
import StatusBadge from "../components/StatusBadge.jsx";
import {
  formatApiError,
  projects as projectsApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapProject } from "../api/mappers.js";

export default function Projects() {
  const [projects, setProjects] = useState([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setLoading(true);
      setError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list());
        const projectGroups = await Promise.all(
          workspaceItems.map((workspace) => projectsApi.listByWorkspace(workspace.id))
        );
        const mappedProjects = projectGroups.flatMap((group) => asArray(group).map(mapProject));

        if (active) setProjects(mappedProjects);
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
  }, []);

  const visibleProjects = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return projects;

    return projects.filter((project) => {
      return [project.name, project.owner, project.statusLabel]
        .filter(Boolean)
        .some((value) => String(value).toLowerCase().includes(query));
    });
  }, [projects, search]);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Projects</p>
          <h1>Every deadline needs an owner</h1>
        </div>
        <button className="primary-button small"><Plus size={18} /> New Project</button>
      </div>

      <section className="panel">
        <div className="toolbar">
          <div className="search-box inline">
            <Search size={18} />
            <input
              placeholder="Search by project or owner..."
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <button className="secondary-button">Filter</button>
        </div>

        {loading && <p>Loading projects from Azure...</p>}
        {error && <div><strong>Unable to load projects.</strong><p>{error}</p></div>}
        {!loading && !error && projects.length === 0 && <p>No projects found.</p>}
        {!loading && !error && projects.length > 0 && visibleProjects.length === 0 && <p>No projects match your search.</p>}

        {!loading && !error && visibleProjects.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Project</th>
                  <th>Status</th>
                  <th>Progress</th>
                  <th>Owner</th>
                  <th>Due date</th>
                </tr>
              </thead>
              <tbody>
                {visibleProjects.map((project) => (
                  <tr key={project.id}>
                    <td>{project.name}</td>
                    <td><StatusBadge>{project.statusLabel}</StatusBadge></td>
                    <td>
                      <div className="progress-cell">
                        <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                        <span>{project.progress}%</span>
                      </div>
                    </td>
                    <td>{project.owner}</td>
                    <td>{project.deadlineLabel}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

import { Plus, Search } from "lucide-react";
import StatusBadge from "../components/StatusBadge.jsx";
import { projects } from "../data/mockData.js";

export default function Projects() {
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
          <div className="search-box inline"><Search size={18} /><input placeholder="Search by project or owner..." /></div>
          <button className="secondary-button">Filter</button>
        </div>

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
              {projects.map((project) => (
                <tr key={project.id}>
                  <td>{project.name}</td>
                  <td><StatusBadge>{project.status}</StatusBadge></td>
                  <td>
                    <div className="progress-cell">
                      <div className="progress-shell"><span style={{ width: `${project.progress}%` }} /></div>
                      <span>{project.progress}%</span>
                    </div>
                  </td>
                  <td>{project.owner}</td>
                  <td>{project.dueDate}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}

import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Lock, Mail } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";

export default function Login() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ email: "john@taskflow.com", password: "123456" });
  const [errors, setErrors] = useState({});

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.email.trim()) nextErrors.email = "Enter the email used by your team.";
    if (!form.email.includes("@")) nextErrors.email = "Use an email format, for example john@taskflow.com.";
    if (!form.password.trim()) nextErrors.password = "Enter your password.";
    if (form.password.length < 6) nextErrors.password = "Password must be at least 6 characters.";
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  function submit(event) {
    event.preventDefault();
    if (!validate()) return;

    localStorage.setItem("token", "fake-demo-jwt-token");
    navigate("/dashboard");
  }

  return (
    <main className="login-page">
      <section className="login-hero">
        <div className="brand large">
          <div className="brand-mark">TF</div>
          <div>
            <h1>TaskFlow</h1>
            <span>Project board for SWE310</span>
          </div>
        </div>

        <div>
          <span className="hero-kicker">Frontend demo</span>
          <h2>Know the owner before the deadline.</h2>
          <p>
            A project management interface for tracking workspaces, tasks, priorities, messages, and the AI project summary required in the final recording.
          </p>
          <div className="hero-orb">
            <div className="mini-board"><strong>07</strong><span>overdue tasks</span></div>
            <div className="mini-board"><strong>15</strong><span>validation checks</span></div>
            <div className="mini-board"><strong>04</strong><span>demo screens</span></div>
          </div>
        </div>
      </section>

      <section className="login-card">
        <p className="eyebrow">Demo login</p>
        <h2>Open the dashboard</h2>
        <form onSubmit={submit} noValidate>
          <label>
            Email
            <div className="input-shell">
              <Mail size={16} />
              <input name="email" value={form.email} onChange={updateField} />
            </div>
            <ErrorMessage>{errors.email}</ErrorMessage>
          </label>

          <label>
            Password
            <div className="input-shell">
              <Lock size={16} />
              <input name="password" type="password" value={form.password} onChange={updateField} />
            </div>
            <ErrorMessage>{errors.password}</ErrorMessage>
          </label>

          <button className="primary-button" type="submit">Sign in</button>
        </form>
        <p className="login-note">
          Prototype mode uses a stored demo token. Replace this with the real JWT login endpoint when backend integration starts.
        </p>
      </section>
    </main>
  );
}

import { useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Lock, Mail } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import axiosClient from "../api/axiosClient.js";
import { storeAuthUser } from "../api/authStorage.js";

export default function Login() {
  const navigate = useNavigate();
  const [form, setForm] = useState({
    email: "",
    password: "",
  });
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(false);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "", form: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.email.trim())
      nextErrors.email = "Enter the email used by your team.";
    if (!form.email.includes("@"))
      nextErrors.email = "Use an email format, for example john@taskflow.com.";
    if (!form.password.trim()) nextErrors.password = "Enter your password.";
    if (form.password.length < 6)
      nextErrors.password = "Password must be at least 6 characters.";
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function submit(event) {
    event.preventDefault();
    if (!validate()) return;

    setLoading(true);
    try {
      const res = await axiosClient.post("/api/auth/login", {
        email: form.email,
        password: form.password,
      });

      const data = res.data.data;
      localStorage.setItem("token", data.token);
      storeAuthUser(data);
      navigate("/dashboard");
    } catch (err) {
      setErrors({
        form: err.response?.data?.message || "Invalid email or password.",
      });
    } finally {
      setLoading(false);
    }
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
            A project management interface for tracking workspaces, tasks,
            priorities, messages, and the AI project summary required in the
            final recording.
          </p>
          <div className="hero-orb">
            <div className="mini-board">
              <strong>07</strong>
              <span>overdue tasks</span>
            </div>
            <div className="mini-board">
              <strong>15</strong>
              <span>validation checks</span>
            </div>
            <div className="mini-board">
              <strong>04</strong>
              <span>demo screens</span>
            </div>
          </div>
        </div>
      </section>

      <section className="login-card">
        <p className="eyebrow">Welcome back</p>
        <h2>Sign in to your account</h2>
        <form onSubmit={submit} noValidate>
          <ErrorMessage>{errors.form}</ErrorMessage>

          <label>
            Email
            <div className="input-shell">
              <Mail size={16} />
              <input name="email" value={form.email} onChange={updateField} placeholder="john@taskflow.com" />
            </div>
            <ErrorMessage>{errors.email}</ErrorMessage>
          </label>

          <label>
            Password
            <div className="input-shell">
              <Lock size={16} />
              <input
                name="password"
                type="password"
                value={form.password}
                onChange={updateField}
                placeholder="••••••"
              />
            </div>
            <ErrorMessage>{errors.password}</ErrorMessage>
          </label>

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Signing in..." : "Sign in"}
          </button>
        </form>
        <p className="login-note">
          Don't have an account?{" "}
          <Link to="/register" style={{ color: "var(--accent)", textDecoration: "underline" }}>
            Register here
          </Link>
        </p>
      </section>
    </main>
  );
}

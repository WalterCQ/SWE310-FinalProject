import { useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Lock, Mail, User } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import axiosClient from "../api/axiosClient.js";

export default function Register() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ name: "", email: "", password: "" });
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(false);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "", form: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.name.trim()) nextErrors.name = "Enter your full name.";
    if (!form.email.trim()) nextErrors.email = "Enter your email address.";
    if (form.email && !form.email.includes("@"))
      nextErrors.email = "Use an email format, for example john@taskflow.com.";
    if (!form.password.trim()) nextErrors.password = "Enter a password.";
    if (form.password && form.password.length < 6)
      nextErrors.password = "Password must be at least 6 characters.";
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function submit(event) {
    event.preventDefault();
    if (!validate()) return;

    setLoading(true);
    try {
      await axiosClient.post("/api/auth/register", {
        name: form.name,
        email: form.email,
        password: form.password,
      });

      // Registration succeeded — go to login page
      navigate("/login");
    } catch (err) {
      setErrors({
        form: err.response?.data?.message || "Registration failed. Try again.",
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
          <span className="hero-kicker">Get started</span>
          <h2>Join your team workspace.</h2>
          <p>
            Create an account to manage projects, track tasks, and collaborate
            with your team in real time.
          </p>
        </div>
      </section>

      <section className="login-card">
        <p className="eyebrow">New account</p>
        <h2>Create your account</h2>
        <form onSubmit={submit} noValidate>
          <ErrorMessage>{errors.form}</ErrorMessage>

          <label>
            Full Name
            <div className="input-shell">
              <User size={16} />
              <input
                name="name"
                value={form.name}
                onChange={updateField}
                placeholder="John Doe"
              />
            </div>
            <ErrorMessage>{errors.name}</ErrorMessage>
          </label>

          <label>
            Email
            <div className="input-shell">
              <Mail size={16} />
              <input
                name="email"
                type="email"
                value={form.email}
                onChange={updateField}
                placeholder="john@taskflow.com"
              />
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
                placeholder="At least 6 characters"
              />
            </div>
            <ErrorMessage>{errors.password}</ErrorMessage>
          </label>

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Creating account..." : "Create account"}
          </button>
        </form>
        <p className="login-note">
          Already have an account?{" "}
          <Link
            to="/login"
            style={{ color: "var(--accent)", textDecoration: "underline" }}
          >
            Sign in
          </Link>
        </p>
      </section>
    </main>
  );
}

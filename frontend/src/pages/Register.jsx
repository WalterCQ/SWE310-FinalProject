import { useEffect, useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Lock, Mail, User } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import { auth, formatApiError } from "../api/taskflowApi.js";
import { useI18n } from "../i18n.jsx";

export default function Register() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [form, setForm] = useState({ name: "", email: "", password: "" });
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    document.title = `${t("auth.register.title")} | TaskFlow`;
  }, [t]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "", form: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.name.trim()) nextErrors.name = t("auth.register.nameRequired");
    if (!form.email.trim()) nextErrors.email = t("auth.register.emailRequired");
    if (form.email && !form.email.includes("@"))
      nextErrors.email = t("auth.emailFormat");
    if (!form.password.trim()) nextErrors.password = t("auth.register.passwordRequired");
    if (form.password && form.password.length < 6)
      nextErrors.password = t("auth.passwordMin");
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function submit(event) {
    event.preventDefault();
    if (!validate()) return;

    setLoading(true);
    try {
      await auth.register({
        name: form.name,
        email: form.email,
        password: form.password,
      });

      // Registration succeeded — go to login page
      navigate("/login");
    } catch (err) {
      setErrors({
        form: formatApiError(err) || t("auth.register.failed"),
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
            <span>{t("app.brand.projectBoardSwe310")}</span>
          </div>
        </div>

        <div>
          <span className="hero-kicker">{t("auth.register.heroKicker")}</span>
          <h2>{t("auth.register.heroTitle")}</h2>
          <p>{t("auth.register.heroBody")}</p>
        </div>
      </section>

      <section className="login-card">
        <p className="eyebrow">{t("auth.register.eyebrow")}</p>
        <h2>{t("auth.register.title")}</h2>
        <form onSubmit={submit} noValidate>
          <ErrorMessage>{errors.form}</ErrorMessage>

          <label>
            {t("auth.fullName")}
            <div className="input-shell">
              <User size={16} />
              <input
                name="name"
                value={form.name}
                onChange={updateField}
                placeholder={t("auth.placeholder.name")}
              />
            </div>
            <ErrorMessage>{errors.name}</ErrorMessage>
          </label>

          <label>
            {t("auth.email")}
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
            {t("auth.password")}
            <div className="input-shell">
              <Lock size={16} />
              <input
                name="password"
                type="password"
                value={form.password}
                onChange={updateField}
                placeholder={t("auth.placeholder.passwordMin")}
              />
            </div>
            <ErrorMessage>{errors.password}</ErrorMessage>
          </label>

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? t("auth.register.submitting") : t("auth.register.submit")}
          </button>
        </form>
        <p className="login-note">
          {t("auth.register.note")}{" "}
          <Link
            to="/login"
            style={{ color: "var(--accent)", textDecoration: "underline" }}
          >
            {t("auth.register.loginLink")}
          </Link>
        </p>
      </section>
    </main>
  );
}

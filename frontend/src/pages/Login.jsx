import { useEffect, useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Lock, Mail } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import BrandMark from "../components/BrandMark.jsx";
import { auth, formatApiError } from "../api/taskflowApi.js";
import { storeAuthUser } from "../api/authStorage.js";
import { useI18n } from "../i18n.jsx";

export default function Login() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const [form, setForm] = useState({
    email: "",
    password: "",
  });
  const [errors, setErrors] = useState({});
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    document.title = `${t("auth.login.title")} | TaskFlow`;
  }, [t]);

  function updateField(event) {
    setForm({ ...form, [event.target.name]: event.target.value });
    setErrors({ ...errors, [event.target.name]: "", form: "" });
  }

  function validate() {
    const nextErrors = {};
    if (!form.email.trim()) {
      nextErrors.email = t("auth.login.emailRequired");
    } else if (!form.email.includes("@")) {
      nextErrors.email = t("auth.emailFormat");
    }
    if (!form.password.trim()) nextErrors.password = t("auth.passwordRequired");
    if (form.password.length < 6)
      nextErrors.password = t("auth.passwordMin");
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }

  async function submit(event) {
    event.preventDefault();
    if (!validate()) return;

    setLoading(true);
    try {
      const data = await auth.login({
        email: form.email,
        password: form.password,
      });

      localStorage.setItem("token", data.token);
      storeAuthUser(data);
      navigate("/dashboard");
    } catch (err) {
      setErrors({
        form: formatApiError(err) || t("auth.login.invalid"),
      });
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="login-page">
      <section className="login-hero">
        <div className="brand large">
          <BrandMark />
          <div>
            <h1>TaskFlow</h1>
            <span>{t("app.brand.projectBoardSwe310")}</span>
          </div>
        </div>

        <div>
          <span className="hero-kicker">{t("auth.login.heroKicker")}</span>
          <h2>{t("auth.login.heroTitle")}</h2>
          <p>{t("auth.login.heroBody")}</p>
          <div className="hero-orb">
            <div className="mini-board">
              <strong>07</strong>
              <span>{t("auth.login.overdueTasks")}</span>
            </div>
            <div className="mini-board">
              <strong>15</strong>
              <span>{t("auth.login.validationChecks")}</span>
            </div>
            <div className="mini-board">
              <strong>04</strong>
              <span>{t("auth.login.demoScreens")}</span>
            </div>
          </div>
        </div>
      </section>

      <section className="login-card">
        <p className="eyebrow">{t("auth.login.eyebrow")}</p>
        <h2>{t("auth.login.title")}</h2>
        <form onSubmit={submit} noValidate>
          <ErrorMessage>{errors.form}</ErrorMessage>

          <label>
            {t("auth.email")}
            <div className="input-shell">
              <Mail size={16} />
              <input name="email" value={form.email} onChange={updateField} placeholder="john@taskflow.com" />
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
                placeholder="••••••"
              />
            </div>
            <ErrorMessage>{errors.password}</ErrorMessage>
          </label>

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? t("auth.login.submitting") : t("auth.login.submit")}
          </button>
        </form>
        <p className="login-note">
          {t("auth.login.note")}{" "}
          <Link to="/register" style={{ color: "var(--accent)", textDecoration: "underline" }}>
            {t("auth.login.registerLink")}
          </Link>
        </p>
      </section>
    </main>
  );
}

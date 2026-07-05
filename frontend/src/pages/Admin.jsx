import { useEffect, useState } from "react";
import { ShieldCheck } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import StatCard from "../components/StatCard.jsx";
import { admin as adminApi, formatApiError } from "../api/taskflowApi.js";
import { useI18n } from "../i18n.jsx";

const emptyState = {
  loading: true,
  error: "",
  metrics: [],
  securityEvidence: [],
};

export default function Admin() {
  const { t } = useI18n();
  const [state, setState] = useState(emptyState);

  useEffect(() => {
    let active = true;

    async function loadAdminOverview() {
      setState((current) => ({ ...current, loading: true, error: "" }));

      try {
        const overview = await adminApi.overview();
        if (active) {
          setState({
            loading: false,
            error: "",
            metrics: overview?.metrics || [],
            securityEvidence: overview?.securityEvidence || [],
          });
        }
      } catch (error) {
        if (active) {
          setState((current) => ({
            ...current,
            loading: false,
            error: formatApiError(error),
          }));
        }
      }
    }

    loadAdminOverview();

    return () => {
      active = false;
    };
  }, []);

  return (
    <div className="page-stack">
      <section className="dashboard-hero">
        <div className="hero-copy">
          <span className="hero-kicker">{t("admin.heroKicker")}</span>
          <h1>{t("admin.heroTitle")}</h1>
          <p>{t("admin.heroBody")}</p>
        </div>
        <aside className="deadline-card">
          <div>
            <small>{t("admin.accessLabel")}</small>
            <strong>{t("admin.accessValue")}</strong>
          </div>
          <ShieldCheck size={42} />
        </aside>
      </section>

      {state.loading && <section className="panel">{t("admin.loading")}</section>}
      {state.error && (
        <section className="panel">
          <ErrorMessage>{state.error}</ErrorMessage>
        </section>
      )}

      {!state.loading && !state.error && (
        <>
          <section className="stats-grid admin-stats-grid">
            {state.metrics.map((metric) => (
              <StatCard
                key={metric.label}
                label={metric.label}
                value={metric.value}
                change="+0"
                sinceText={metric.helpText}
                tone="green"
                icon="completed"
              />
            ))}
          </section>

          <section className="panel">
            <div className="panel-header">
              <div>
                <h3>{t("admin.securityEvidence")}</h3>
                <span>{t("admin.securityEvidenceHelp")}</span>
              </div>
            </div>
            <div className="evidence-list">
              {state.securityEvidence.map((item) => (
                <article key={item.area} className="evidence-item">
                  <strong>{item.area}</strong>
                  <p>{item.evidence}</p>
                </article>
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  );
}

import { useEffect, useState } from "react";
import { Database, KeyRound, LockKeyhole, Route, Save, ShieldCheck } from "lucide-react";
import ErrorMessage from "../components/ErrorMessage.jsx";
import StatCard from "../components/StatCard.jsx";
import { admin as adminApi, formatApiError } from "../api/taskflowApi.js";
import { dateLocale, useI18n } from "../i18n.jsx";

const emptyState = {
  loading: true,
  error: "",
  metrics: [],
  securityEvidence: [],
  users: [],
};

const accessChecks = [
  { icon: ShieldCheck, labelKey: "admin.roleGate", valueKey: "admin.roleGateValue" },
  { icon: Route, labelKey: "admin.routeGate", valueKey: "admin.routeGateValue" },
  { icon: LockKeyhole, labelKey: "admin.apiGate", valueKey: "admin.apiGateValue" },
];

const evidenceIcons = [LockKeyhole, Route, Database];

const globalRoleOptions = [
  { value: "0", labelKey: "enum.globalRole.administrator" },
  { value: "1", labelKey: "enum.globalRole.member" },
];

function roleToFormValue(role) {
  const normalized = String(role || "").trim().toLowerCase();
  return normalized === "administrator" || normalized === "admin" || normalized === "0" ? "0" : "1";
}

function roleValueToRequest(value) {
  return value === "0" ? 0 : 1;
}

function buildUserForms(users) {
  return Object.fromEntries(users.map((user) => [
    user.userId,
    {
      name: user.name || "",
      globalRole: roleToFormValue(user.globalRole),
    },
  ]));
}

function syncUserMetric(metrics, users) {
  const administratorCount = users.filter((user) => roleToFormValue(user.globalRole) === "0").length;

  return metrics.map((metric) => metric.label === "Users"
    ? { ...metric, value: users.length, helpText: `${administratorCount} global administrators` }
    : metric);
}

function formatDate(value, locale) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";

  return new Intl.DateTimeFormat(dateLocale(locale), {
    year: "numeric",
    month: "short",
    day: "numeric",
  }).format(date);
}

export default function Admin() {
  const { locale, t } = useI18n();
  const [state, setState] = useState(emptyState);
  const [userForms, setUserForms] = useState({});
  const [passwordForms, setPasswordForms] = useState({});
  const [savingUserId, setSavingUserId] = useState("");
  const [resettingUserId, setResettingUserId] = useState("");
  const [actionError, setActionError] = useState("");
  const [actionMessage, setActionMessage] = useState("");

  useEffect(() => {
    let active = true;

    async function loadAdminConsole() {
      setState((current) => ({ ...current, loading: true, error: "" }));

      try {
        const [overview, usersResponse] = await Promise.all([
          adminApi.overview(),
          adminApi.users(),
        ]);
        const users = Array.isArray(usersResponse) ? usersResponse : [];

        if (active) {
          setState({
            loading: false,
            error: "",
            metrics: overview?.metrics || [],
            securityEvidence: overview?.securityEvidence || [],
            users,
          });
          setUserForms(buildUserForms(users));
          setPasswordForms({});
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

    loadAdminConsole();

    return () => {
      active = false;
    };
  }, []);

  function updateUserForm(userId, field, value) {
    setActionError("");
    setActionMessage("");
    setUserForms((current) => ({
      ...current,
      [userId]: {
        ...(current[userId] || {}),
        [field]: value,
      },
    }));
  }

  function updatePasswordForm(userId, value) {
    setActionError("");
    setActionMessage("");
    setPasswordForms((current) => ({
      ...current,
      [userId]: value,
    }));
  }

  async function saveUser(user) {
    const form = userForms[user.userId] || {};
    const name = String(form.name || "").trim();

    if (!name) {
      setActionError(t("admin.nameRequired"));
      return;
    }

    setSavingUserId(user.userId);
    setActionError("");
    setActionMessage("");

    try {
      const updatedUser = await adminApi.updateUser(user.userId, {
        name,
        globalRole: roleValueToRequest(form.globalRole),
      });

      if (!updatedUser?.userId) {
        throw new Error(t("admin.invalidUserResponse"));
      }

      setState((current) => {
        const users = current.users.map((item) => item.userId === updatedUser.userId ? updatedUser : item);
        return {
          ...current,
          users,
          metrics: syncUserMetric(current.metrics, users),
        };
      });
      setUserForms((current) => ({
        ...current,
        [updatedUser.userId]: {
          name: updatedUser.name || "",
          globalRole: roleToFormValue(updatedUser.globalRole),
        },
      }));
      setActionMessage(t("admin.userSaved", { name: updatedUser.name }));
    } catch (error) {
      setActionError(formatApiError(error));
    } finally {
      setSavingUserId("");
    }
  }

  async function resetPassword(user) {
    const newPassword = String(passwordForms[user.userId] || "").trim();

    if (newPassword.length < 6) {
      setActionError(t("admin.passwordRequired"));
      return;
    }

    setResettingUserId(user.userId);
    setActionError("");
    setActionMessage("");

    try {
      await adminApi.resetUserPassword(user.userId, { newPassword });
      setPasswordForms((current) => ({ ...current, [user.userId]: "" }));
      setActionMessage(t("admin.passwordReset", { name: user.name }));
    } catch (error) {
      setActionError(formatApiError(error));
    } finally {
      setResettingUserId("");
    }
  }

  return (
    <div className="page-stack admin-console">
      <section className="admin-console-hero">
        <div className="admin-console-copy">
          <span className="admin-kicker">{t("admin.heroKicker")}</span>
          <h1>{t("admin.heroTitle")}</h1>
          <p>{t("admin.heroBody")}</p>
        </div>
        <aside className="admin-access-card" aria-label={t("admin.accessLabel")}>
          <ShieldCheck size={36} />
          <small>{t("admin.accessLabel")}</small>
          <strong>{t("admin.accessValue")}</strong>
          <p>{t("admin.accessBody")}</p>
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
          <section className="admin-section">
            <div className="admin-section-heading">
              <div>
                <h3>{t("admin.systemSnapshot")}</h3>
                <span>{t("admin.systemSnapshotHelp")}</span>
              </div>
            </div>
            <div className="stats-grid admin-stats-grid">
              {state.metrics.map((metric) => (
                <StatCard
                  key={metric.label}
                  label={metric.label}
                  value={metric.value}
                  change=""
                  sinceText={metric.helpText}
                  tone="green"
                  icon="completed"
                  supportingTone="neutral"
                />
              ))}
            </div>
          </section>

          <section className="panel admin-users-panel">
            <div className="panel-header">
              <div>
                <h3>{t("admin.userManagement")}</h3>
                <span>{t("admin.userManagementHelp", { count: state.users.length })}</span>
              </div>
            </div>

            {actionError && <ErrorMessage>{actionError}</ErrorMessage>}
            {actionMessage && <p className="success-text">{actionMessage}</p>}

            <div className="table-wrap admin-user-table-wrap">
              <table className="admin-user-table">
                <thead>
                  <tr>
                    <th>{t("admin.userAccount")}</th>
                    <th>{t("admin.userName")}</th>
                    <th>{t("admin.userRole")}</th>
                    <th>{t("admin.userAccess")}</th>
                    <th>{t("admin.userPassword")}</th>
                    <th>{t("admin.userActions")}</th>
                  </tr>
                </thead>
                <tbody>
                  {state.users.map((user) => {
                    const form = userForms[user.userId] || { name: user.name || "", globalRole: roleToFormValue(user.globalRole) };
                    const createdAt = formatDate(user.createdAtUtc, locale);
                    const isSaving = savingUserId === user.userId;
                    const isResetting = resettingUserId === user.userId;

                    return (
                      <tr key={user.userId}>
                        <td>
                          <div className="admin-account-cell">
                            <strong>{user.email}</strong>
                            {createdAt && <span>{t("admin.createdAt", { date: createdAt })}</span>}
                          </div>
                        </td>
                        <td>
                          <input
                            aria-label={t("admin.userName")}
                            className="admin-table-input"
                            value={form.name}
                            onChange={(event) => updateUserForm(user.userId, "name", event.target.value)}
                          />
                        </td>
                        <td>
                          <select
                            aria-label={t("admin.userRole")}
                            className="admin-table-input"
                            value={form.globalRole}
                            onChange={(event) => updateUserForm(user.userId, "globalRole", event.target.value)}
                          >
                            {globalRoleOptions.map((option) => (
                              <option key={option.value} value={option.value}>{t(option.labelKey)}</option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <div className="admin-access-counts">
                            <span>{t("admin.workspaceCount", { count: user.workspaceCount })}</span>
                            <span>{t("admin.projectCount", { count: user.projectCount })}</span>
                            <span>{t("admin.channelCount", { count: user.channelCount })}</span>
                          </div>
                        </td>
                        <td>
                          <input
                            aria-label={t("admin.newPassword")}
                            className="admin-table-input"
                            type="password"
                            value={passwordForms[user.userId] || ""}
                            onChange={(event) => updatePasswordForm(user.userId, event.target.value)}
                            placeholder={t("admin.newPassword")}
                          />
                        </td>
                        <td>
                          <div className="admin-user-actions">
                            <button
                              className="primary-button compact"
                              type="button"
                              onClick={() => saveUser(user)}
                              disabled={isSaving || isResetting}
                            >
                              <Save size={15} /> {isSaving ? t("admin.saving") : t("admin.saveUser")}
                            </button>
                            <button
                              className="secondary-button compact"
                              type="button"
                              onClick={() => resetPassword(user)}
                              disabled={isSaving || isResetting}
                            >
                              <KeyRound size={15} /> {isResetting ? t("admin.resetting") : t("admin.resetPassword")}
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>

          <div className="admin-console-grid">
            <section className="panel admin-control-panel">
              <div className="panel-header">
                <div>
                  <h3>{t("admin.accessControl")}</h3>
                  <span>{t("admin.accessControlHelp")}</span>
                </div>
              </div>
              <div className="admin-control-list">
                {accessChecks.map((item) => {
                  const Icon = item.icon;
                  return (
                    <article key={item.labelKey} className="admin-control-row">
                      <span className="admin-row-icon"><Icon size={19} /></span>
                      <div>
                        <strong>{t(item.labelKey)}</strong>
                        <p>{t(item.valueKey)}</p>
                      </div>
                    </article>
                  );
                })}
              </div>
            </section>

            <section className="panel admin-evidence-panel">
              <div className="panel-header">
                <div>
                  <h3>{t("admin.securityEvidence")}</h3>
                  <span>{t("admin.securityEvidenceHelp")}</span>
                </div>
              </div>
              <div className="admin-evidence-list">
                {state.securityEvidence.map((item, index) => {
                  const Icon = evidenceIcons[index % evidenceIcons.length];
                  return (
                    <article key={item.area} className="admin-evidence-row">
                      <span className="admin-row-icon"><Icon size={19} /></span>
                      <div>
                        <strong>{item.area}</strong>
                        <p>{item.evidence}</p>
                      </div>
                    </article>
                  );
                })}
              </div>
            </section>
          </div>
        </>
      )}
    </div>
  );
}

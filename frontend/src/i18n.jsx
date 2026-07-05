import { createContext, useContext, useEffect, useMemo, useState } from "react";
import { dictionaries } from "./i18n/messages/index.js";

export const LOCALES = {
  en: "en",
  zh: "zh-CN",
  tg: "tg-Cyrl-TJ",
};

const LOCALE_DIRECTIONS = {
  [LOCALES.en]: "ltr",
  [LOCALES.zh]: "ltr",
  [LOCALES.tg]: "ltr",
};

const DATE_LOCALES = {
  [LOCALES.en]: "en-GB",
  [LOCALES.zh]: "zh-CN",
  [LOCALES.tg]: "tg-Cyrl-TJ",
};

const STORAGE_KEY = "taskflow.locale";

function interpolate(template, params = {}) {
  return String(template).replace(/\{\{(\w+)\}\}/g, (_, key) => {
    return Object.prototype.hasOwnProperty.call(params, key) ? params[key] : "";
  });
}

export function normalizeLocale(value) {
  if (value === "tg" || value === "tg-TJ") return LOCALES.tg;
  return Object.values(LOCALES).includes(value) ? value : LOCALES.en;
}

export function getStoredLocale() {
  if (typeof window === "undefined") return LOCALES.en;
  return normalizeLocale(window.localStorage.getItem(STORAGE_KEY));
}

export function translateForLocale(locale, key, params) {
  const normalizedLocale = normalizeLocale(locale);
  const dictionary = dictionaries[normalizedLocale] || dictionaries.en;
  return interpolate(dictionary[key] || dictionaries.en[key] || key, params);
}

export function translateKey(key, params) {
  return translateForLocale(getStoredLocale(), key, params);
}

export function dateLocale(locale = getStoredLocale()) {
  return DATE_LOCALES[normalizeLocale(locale)];
}

export function localeDirection(locale = getStoredLocale()) {
  return LOCALE_DIRECTIONS[normalizeLocale(locale)];
}

export function enumTaskStatusKey(value) {
  const normalized = String(value || "").trim().toLowerCase().replaceAll(" ", "");
  if (normalized === "inprogress") return "enum.taskStatus.inProgress";
  if (normalized === "blocked") return "enum.taskStatus.blocked";
  if (normalized === "done") return "enum.taskStatus.done";
  return "enum.taskStatus.toDo";
}

export function enumPriorityKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "high") return "enum.priority.high";
  if (normalized === "low") return "enum.priority.low";
  return "enum.priority.medium";
}

export function enumProjectStatusKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "active") return "enum.projectStatus.active";
  if (normalized === "completed") return "enum.projectStatus.completed";
  if (normalized === "archived") return "enum.projectStatus.archived";
  return "enum.projectStatus.planned";
}

export function enumWorkspaceRoleKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "administrator" || normalized === "0") {
    return "enum.workspaceRole.administrator";
  }
  if (normalized === "manager" || normalized === "1") return "enum.workspaceRole.manager";
  return "enum.workspaceRole.member";
}

export function enumProjectRoleKey(value) {
  const normalized = String(value || "").trim().toLowerCase().replaceAll(" ", "");
  if (normalized === "administrator" || normalized === "0") {
    return "enum.projectRole.administrator";
  }
  if (normalized === "member" || normalized === "2") return "enum.projectRole.member";
  return "enum.projectRole.manager";
}

export function enumGlobalRoleKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "administrator") return "enum.globalRole.administrator";
  return "enum.globalRole.member";
}

export function enumNotificationKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  if (normalized === "message") return "enum.notification.message";
  if (normalized === "task") return "enum.notification.task";
  if (normalized === "reminder") return "enum.notification.reminder";
  if (normalized === "ai") return "enum.notification.ai";
  return "enum.notification.general";
}

const I18nContext = createContext(null);

export function I18nProvider({ children }) {
  const [locale, setLocaleState] = useState(getStoredLocale);

  useEffect(() => {
    document.documentElement.lang = locale;
    document.documentElement.dir = localeDirection(locale);
    window.localStorage.setItem(STORAGE_KEY, locale);
  }, [locale]);

  const value = useMemo(() => {
    function setLocale(nextLocale) {
      setLocaleState(normalizeLocale(nextLocale));
    }

    function t(key, params) {
      return translateForLocale(locale, key, params);
    }

    return { locale, setLocale, t };
  }, [locale]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error("useI18n must be used inside I18nProvider.");
  }
  return context;
}

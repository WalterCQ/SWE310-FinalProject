import { commonMessages } from "./common.js";
import { authMessages } from "./auth.js";
import { dashboardMessages } from "./dashboard.js";
import { adminMessages } from "./admin.js";
import { workspaceMessages } from "./workspace.js";
import { projectMessages } from "./project.js";
import { taskMessages } from "./task.js";
import { channelMessages } from "./channel.js";
import { aiMessages } from "./ai.js";
import { notificationsMessages } from "./notifications.js";
import { enumMessages } from "./enums.js";
import { apiMessages } from "./api.js";

const messageModules = [
  commonMessages,
  authMessages,
  dashboardMessages,
  adminMessages,
  workspaceMessages,
  projectMessages,
  taskMessages,
  channelMessages,
  aiMessages,
  notificationsMessages,
  enumMessages,
  apiMessages,
];

function mergeLocale(locale) {
  return Object.assign({}, ...messageModules.map((messages) => messages[locale] || {}));
}

export const dictionaries = {
  en: mergeLocale("en"),
  "zh-CN": mergeLocale("zh-CN"),
  "tg-Cyrl-TJ": mergeLocale("tg-Cyrl-TJ"),
};

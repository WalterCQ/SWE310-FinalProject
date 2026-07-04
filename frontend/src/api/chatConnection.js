import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "./axiosClient.js";

const chatHubUrl = `${API_BASE_URL.replace(/\/$/, "")}/hubs/chat`;

export const chatConnectionState = {
  connected: signalR.HubConnectionState.Connected,
};

export function createChatConnection() {
  return new signalR.HubConnectionBuilder()
    .withUrl(chatHubUrl, {
      accessTokenFactory: () => localStorage.getItem("token") || "",
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.None)
    .build();
}

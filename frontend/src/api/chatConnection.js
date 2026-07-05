import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "./axiosClient.js";

const chatHubUrl = `${API_BASE_URL.replace(/\/$/, "")}/hubs/chat`;
const reconnectDelaysMs = [0, 2000, 5000, 10000, 30000];

export const chatConnectionState = {
  connected: signalR.HubConnectionState.Connected,
};

export function createChatConnection() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(chatHubUrl, {
      accessTokenFactory: () => localStorage.getItem("token") || "",
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets,
    })
    .withAutomaticReconnect(reconnectDelaysMs)
    .configureLogging(signalR.LogLevel.None)
    .build();

  connection.keepAliveIntervalInMilliseconds = 15000;
  connection.serverTimeoutInMilliseconds = 60000;

  return connection;
}

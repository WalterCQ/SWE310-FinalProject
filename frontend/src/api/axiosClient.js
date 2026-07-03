import axios from "axios";
import { clearAuthStorage } from "./authStorage.js";

const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL
    || "https://taskflow-connect-06221341-feb9.azurewebsites.net",
});

axiosClient.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  return config;
});

axiosClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401) {
      clearAuthStorage();
      window.dispatchEvent(new Event("taskflow:auth-expired"));
    }

    return Promise.reject(error);
  }
);

export default axiosClient;

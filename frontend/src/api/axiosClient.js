import axios from "axios";
import { clearAuthStorage } from "./authStorage.js";

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL
  || "";

const axiosClient = axios.create({
  baseURL: API_BASE_URL,
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

import axios from "axios";

const axiosClient = axios.create({
  baseURL: "https://taskflow-connect-06221341-feb9.azurewebsites.net",
});

axiosClient.interceptors.request.use((config) => {
  const token = localStorage.getItem("token");

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  return config;
});

export default axiosClient;

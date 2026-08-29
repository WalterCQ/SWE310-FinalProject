import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiTarget = env.VITE_DEV_PROXY_TARGET || "http://localhost:5134";

  return {
    server: {
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
          secure: apiTarget.startsWith("https://"),
        },
        "/hubs": {
          target: apiTarget,
          changeOrigin: true,
          secure: apiTarget.startsWith("https://"),
          ws: true,
        },
      },
    },
  };
});

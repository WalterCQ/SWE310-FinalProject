export default {
  server: {
    proxy: {
      "/api": {
        target: "https://taskflow-connect-06221341-feb9.azurewebsites.net",
        changeOrigin: true,
        secure: true,
      },
    },
  },
};

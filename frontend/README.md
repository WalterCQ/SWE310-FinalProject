# TaskFlow Connect frontend

React and Vite client for TaskFlow Connect. Product screenshots, architecture, configuration, and full-stack setup are documented in the [project README](../README.md).

## Start locally

Run the ASP.NET Core API on `http://localhost:5134`, then:

```bash
npm ci
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` and `/hubs` to the local API by default.

Use `VITE_DEV_PROXY_TARGET` to point the development proxy at a different API, or `VITE_API_BASE_URL` when the client should call an explicit base URL. Keep credentials in environment variables or ignored local settings files.

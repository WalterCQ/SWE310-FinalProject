# TaskFlow Connect - Frontend UI Prototype

This is a React/Vite frontend prototype for the SWE310 group project.
It connects the dashboard, projects, tasks, workspaces, notifications, and AI assistant screens to the backend Swagger APIs.

## Run it

```bash
npm install
npm run dev
```

Open the localhost link shown in the terminal.

## demo login

Register a demo account, then sign in with that account to load the protected API data.

## What is included

- High-tech minimal dark UI
- Login page
- Dashboard page with cards and charts
- Projects page
- Tasks page with validation
- Workspaces page
- Channels page
- AI Assistant page
- Notifications page
- Sidebar and topbar
- Axios client ready for backend connection

## Backend connection

Local development proxies `/api` and `/hubs` to:

`http://localhost:5134`

Start the backend first:

```bash
cd ../backend/TaskFlow.Api
dotnet ef database update
dotnet run --launch-profile http
```

To point the dev server at another backend, set:

```bash
VITE_DEV_PROXY_TARGET=https://taskflow-connect-06221341-feb9.azurewebsites.net npm run dev
```

For production/static builds, set `VITE_API_BASE_URL` if the API is hosted on another origin.

Mock data has been removed from the frontend. Demo notifications and other sample records are seeded by the backend and loaded through `axiosClient`.

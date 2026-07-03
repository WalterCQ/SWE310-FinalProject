# TaskFlow Connect - Frontend UI Prototype

This is a React/Vite frontend prototype for the SWE310 group project.
It connects the dashboard, projects, tasks, workspaces, notifications, and AI assistant screens to the deployed backend Swagger APIs.

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

API base URL is configured in:

`src/api/axiosClient.js`

Current backend base URL:

`https://taskflow-connect-06221341-feb9.azurewebsites.net`

Older mock data remains in `src/data/mockData.js` only as reference data. The main demo pages call the backend through `axiosClient`.

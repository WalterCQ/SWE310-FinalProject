# TaskFlow Connect - Fake Clean Frontend

This is a React/Vite frontend prototype for the SWE310 group project.
It uses dummy data first, so you can quickly show the dashboard/UI/validation/demo flow.
Later, replace the mock data with calls to your deployed backend Swagger APIs.

## Run it

```bash
npm install
npm run dev
```

Open the localhost link shown in the terminal.

## Fake login

Use any email/password. Example:

- Email: john@taskflow.com
- Password: 123456

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

## Backend connection later

API base URL is configured in:

`src/api/axiosClient.js`

Current backend base URL:

`https://taskflow-connect-06221341-feb9.azurewebsites.net`

The frontend is currently using mock/dummy data. When your backend endpoints are confirmed, replace mock data in `src/data/mockData.js` or call APIs through `axiosClient`.

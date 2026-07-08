# SWE310 - Programming Elective II (2): .NET
## Group Project Submission

**Project Title**: TaskFlow Connect - Full-Stack AI-Powered Project & Collaboration Management Platform

### 👥 Demo Accounts & Credentials
The following credentials can be used for evaluation and grading on both the live site and local instances:
- **Global Administrator / Owner**: `admin@mail.com` / `abc123`
- **Workspace Owner / PM**: `owner@mail.com` / `abc123`
- **Standard Member / Contributor**: `member@mail.com` / `abc123`
*All demo accounts belong to the default `Permission Demo Workspace` for easier evaluation.*

---

### 🔗 Project Links
- **Hosted Azure Web API Swagger**: [Swagger UI](https://taskflow-connect-06221341-feb9.azurewebsites.net/swagger/index.html)
- **OpenAPI Document JSON**: [OpenAPI JSON](https://taskflow-connect-06221341-feb9.azurewebsites.net/swagger/v1/swagger.json)
- **GitHub Repository**: [WalterCQ/SWE310-FinalProject](https://github.com/WalterCQ/SWE310-FinalProject)
- **🎬 Demo Video Link**: [Google Drive Demo Video](https://drive.google.com/file/d/14ktPkSDiBMY9mMcM8AtcEXToi9LFh3Tk/view?usp=sharing)

---

### 🛠️ Technical Stack Overview
- **Frontend**: Built with **React**, **React Router**, **Axios**, and **Tailwind CSS**. Charts rendered via **Recharts**.
- **Backend**: **ASP.NET Core Web API** using **Entity Framework Core** and **SQL Server** (deployed on Azure).
- **Authentication**: Role-based access control with **JWT** token-based authentication. Roles include Admin, PM, and Contributor, enforced at both API level and frontend router paths.
- **AI Integration**: Microsoft Semantic Kernel connected to an LLM provider (Azure OpenAI/Gemini/OpenAI-compatible) using custom plugins that ground prompts in real workspace data.

---

### 🌟 Key Features & Rubric Evidence
1. **User & Access Management (JWT & Role Enforced)**:
   - Full registration, login, and `/api/auth/me` endpoints.
   - Distinct roles (Admin, PM, Contributor) with corresponding granular permissions.
   - Frontend route guarding (`ProtectedShell`) and API controller validation (`[Authorize]` + workspace/project context permission checks via `PermissionService`).

2. **Core Domain Functionality (Full CRUD API)**:
   - Manages Workspaces, Members, Channels, Messages, Projects, Tasks, and Comments.
   - RESTful API structure with standard HTTP verbs and status codes.
   - Complex data relationships mapped via Entity Framework Core.

3. **AI Assistant (Semantic Kernel & LLM)**:
   - `CollaborationAiPlugin` allows natural language queries over workspace data.
   - Features like task generation, channel conversation summarization, requirements analysis, and code recommendation.
   - Prompts are grounded on real application records (e.g., retrieving active messages, project members, or card details).

4. **Dashboard & Insights**:
   - Visual dashboard in the React frontend reporting workspace/project progress.
   - Visual metrics, status distributions, and charts using Recharts.

5. **Validation & Error Handling**:
   - Backend validation using DataAnnotations and custom DTO constraints (empty GUID checks, string validation).
   - Frontend validation showing user-friendly feedback; standard API envelope response (`ApiResponse<T>`).

---

### 📦 How to Run Locally

#### Prerequisites
- .NET 8.0 SDK or later
- Node.js (v18 or later)
- SQL Server (or LocalDB on Windows)

#### Steps:
1. **Database Migration**:
   ```bash
   cd backend/TaskFlow.Api
   dotnet restore
   dotnet build
   # Optional: Configure database connection in appsettings.Development.json
   dotnet ef database update
   ```
2. **Start Backend**:
   ```bash
   dotnet run --launch-profile http
   # API will be running at http://localhost:5134
   ```
3. **Start Frontend**:
   ```bash
   cd ../../frontend
   npm install
   npm run dev
   # Frontend will be running at http://localhost:5173
   ```

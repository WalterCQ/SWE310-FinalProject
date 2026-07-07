FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY backend/TaskFlow.Api/TaskFlow.Api.csproj backend/TaskFlow.Api/
COPY backend/TaskFlow.AgentWorker/TaskFlow.AgentWorker.csproj backend/TaskFlow.AgentWorker/
RUN dotnet restore backend/TaskFlow.Api/TaskFlow.Api.csproj \
    && dotnet restore backend/TaskFlow.AgentWorker/TaskFlow.AgentWorker.csproj

COPY . .
RUN dotnet publish backend/TaskFlow.Api/TaskFlow.Api.csproj --configuration Release --no-restore --output /out/api \
    && dotnet publish backend/TaskFlow.AgentWorker/TaskFlow.AgentWorker.csproj --configuration Release --no-restore --output /out/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        git \
        libcairo2 \
        libffi8 \
        libgdk-pixbuf-2.0-0 \
        libpango-1.0-0 \
        libpangocairo-1.0-0 \
        nodejs \
        npm \
        python3.12 \
        python3.12-venv \
        shared-mime-info \
    && rm -rf /var/lib/apt/lists/*

COPY tools/agent-skills /app/tools/agent-skills
RUN python3.12 -m venv /opt/taskflow/ppt-master-venv \
    && /opt/taskflow/ppt-master-venv/bin/python -m pip install --upgrade pip \
    && /opt/taskflow/ppt-master-venv/bin/pip install --no-cache-dir -r /app/tools/agent-skills/ppt-master/requirements.txt

COPY --from=build /out/api /app/api
COPY --from=build /out/worker /app/worker
COPY deploy/start-taskflow.sh /app/start-taskflow.sh
RUN chmod +x /app/start-taskflow.sh

ENV ASPNETCORE_URLS=http://+:8080 \
    WEBSITES_PORT=8080 \
    AgentSkills__RootPath=/app/tools/agent-skills \
    AgentSkills__PythonExecutable=/opt/taskflow/ppt-master-venv/bin/python

EXPOSE 8080
ENTRYPOINT ["/app/start-taskflow.sh"]

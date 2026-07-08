# TaskFlow Channel AI Verification - 2026-07-08

## Conclusion

Channel AI preview/share approval, source persistence, task creation, DOCX/PPTX artifact generation, and Code advice GitHub PR creation were verified end to end against the real local frontend, local API/worker, Azure SQL, GitHub Models, Pinecone, and the connected GitHub App.

## Cleanup

| Target | Result | Evidence |
| --- | --- | --- |
| Screenshot messages | Removed the screenshot-matching TaskFlow AI/user messages from the active SQL backend. Delete order was `ChannelKnowledgeChunks -> ChannelAttachmentBlobs -> ChannelAttachments -> Messages`. | `artifacts/validation/20260707-190600-azure-deleted-message-ids.csv` |
| Active validation database | The local API and worker were run against the Azure SQL connection used by the deployed app, so frontend validation and cleanup hit the same backend data plane. | Workspace `aeaaa600-ee2e-4b20-aaee-dc6fe2312667`, channel `60f0d988-afe4-40f7-ba6f-0bdf56411d18` |

## AI Provider And RAG

| Item | Result |
| --- | --- |
| Chat model | GitHub Models `openai/gpt-4.1-mini` returned valid chat completions. |
| Embeddings | GitHub Models `openai/text-embedding-3-small` returned 1536-dimensional vectors. |
| Vector index | Pinecone index `taskflow-rag-1536` was created and used for GitHub Models embeddings. |
| Attachments | PDF `3a01ddb2-3383-4a11-a7d5-81af5d0565ba` and image `aeefe3c2-cb76-4298-a85f-ed9d976632e6` were reindexed successfully. |
| Azure settings | App Service settings now include `Pinecone__IndexName1536`, `AI__GitHubEmbeddingModel`, `Pinecone__IndexName3072`, and `Pinecone__GeminiIndexName`. |

## Function Verification

| Function | Frontend action | Backend result |
| --- | --- | --- |
| Summary | Clicked `Summarize`, sent to TaskFlow AI, verified preview did not create a channel message, then clicked `Send to channel`. | Channel message `a6ee4803-4295-4d94-b9be-c703352a5de8` was created only after approval and returned 9 sources after refresh. |
| Requirements | Clicked `Requirements` and sent the default request. | Preview extracted deliverables, grading criteria, risks, deadline, and acceptance checks. This feature is for requirement/rubric extraction; it does not create tasks. |
| Tasks | Clicked `Tasks`, reviewed 5 suggested tasks, then clicked `Create task`. | Project task count increased from 1 to 2; the created card appeared on `/tasks?projectId=5c751f5f-d41e-4ece-a8a3-fc0f29dce486`. |
| Report | Clicked `Report`, approved Agent job `8ad88f01-b010-46c8-8a2a-a57bf3fc7d3e`. | DOCX artifact `23dc21b4-ddff-4050-96a7-cb08ed61d07a` downloaded as `taskflow-ai-report-latest.docx`; `unzip -t` passed; LibreOffice rendered an 8-page PDF. |
| PPT | Clicked `PPT`, approved Agent job `317a8b67-001b-4373-b3a5-b1c8f2c6cf91`. | PPTX artifact `995b3c72-022d-45df-b18d-4a6bd35b039f` downloaded as `taskflow-ai-deck-latest.pptx`; `unzip -t` passed; deck has 8 slides and 8 notes slides; LibreOffice rendered readable slide previews. |
| Code advice | Clicked `Code advice`, approved the plan, inspected patch and validation, then approved GitHub PR creation. | Job `b2a42c2f-7b8e-4cda-812b-2edc0f91e98d` created PR `https://github.com/WalterCQ/SWE310-FinalProject/pull/5` from branch `taskflow-ai/b2a42c2f7b8e4cda812b2edc0f91e98d` to `main`; PR result artifact interpolated repository, branch, URL, diff summary, and validation output correctly. |

## Source Persistence

Root cause: shared AI messages had no persisted AI metadata and SignalR/REST mappings did not return source fields, so refreshed channel cards displayed `No sources`.

Fix:
- Added AI metadata to `Message`: `AiArtifactType`, `AiSourcesJson`, `AiSuggestedTasksJson`, `AiAgentJobId`, `AiRequiresApproval`, `AiCreatedTaskId`, `AiCreatedTaskTitle`.
- Extended message DTOs, REST mapping, SignalR normalization, and frontend message mapping.
- `/api/channels/{id}/ai` returns preview only; `/api/channels/{id}/ai/share` is the only path that writes an AI channel message.

## Artifact Quality

| Artifact | Implementation |
| --- | --- |
| PPTX | Replaced the incomplete local `ppt-master` copy with the upstream `hugohe3/ppt-master` skill tree, tag `v3.1.0` (`b8808a3a`). Worker now builds deck specs, SVG slides, notes, and native editable PPTX through `svg_to_pptx.py`. |
| DOCX | Added a TaskFlow report renderer with cover, contents, requirement matrix, evidence tables, risks, acceptance checks, source appendix, header, and footer. Generation errors now surface as real artifacts instead of fake success files. |
| Azure runtime | `deploy/backend.Dockerfile` now runs API and AgentWorker in one container with .NET 10, Python 3.12 venv, Node/npm, git, and `ppt-master` requirements installed. |

## Code Advice Hardening

Intermediate validation caught real defects before final approval:
- Bad generated endpoint patch was rejected instead of creating a PR.
- Corrupt diff was traced to unsafe trimming of `git diff` stdout and fixed.
- BOM insertion was traced to `Encoding.UTF8` file writes and fixed with `UTF8Encoding(false)`.
- `github-pr-result.md` interpolation was fixed by using the correct raw string interpolation form.

Final Code advice validation:
- Patch artifact `33e37d4b-58c1-4d1e-9754-64ebdca924ea` size 780 bytes, no UTF-8 BOM.
- Validation artifact `f9c45692-740b-4670-9a25-eea85151c347` includes API build, AgentWorker build, npm dependency install, and frontend build.
- GitHub PR `https://github.com/WalterCQ/SWE310-FinalProject/pull/5` is open and targets `main` from a generated branch, not a direct push to `main`.

## Commands

| Command | Result |
| --- | --- |
| `dotnet build backend/TaskFlow.Api/TaskFlow.Api.csproj` | Passed, 0 warnings/errors |
| `dotnet build backend/TaskFlow.AgentWorker/TaskFlow.AgentWorker.csproj` | Passed, 0 warnings/errors |
| `npm --prefix frontend run build` | Passed; only third-party SignalR annotation and chunk-size warnings |
| `git diff --check` | Passed |
| `unzip -t artifacts/validation/downloads/taskflow-ai-report-latest.docx` | Passed |
| `unzip -t artifacts/validation/downloads/taskflow-ai-deck-latest.pptx` | Passed |
| LibreOffice headless DOCX/PPTX render | Passed; rendered previews are under `artifacts/validation/rendered-report-latest/` and `artifacts/validation/rendered-ppt-latest/` |

## Red/Blue Review

| Red-team concern | Blue-team verification |
| --- | --- |
| `/ai` might still post directly to channel. | Message count stayed unchanged after Summary preview and changed only after `Send to channel`. |
| Sources might disappear after refresh. | Shared summary message reloaded with 9 sources instead of `No sources`. |
| Tasks button might create tasks without approval. | Tasks were only created after the per-suggestion `Create task` button was clicked. |
| Report/PPT might output low-quality placeholder files. | DOCX/PPTX were downloaded, zip-validated, converted to PDF, and rendered as readable previews. |
| Code advice might push directly to `main`. | Final flow created branch `taskflow-ai/b2a42c2f7b8e4cda812b2edc0f91e98d` and PR #5. |
| Generated patch might silently corrupt files. | Worker now preserves raw diff output, writes no-BOM UTF-8, runs `git diff --check`, and stores real error artifacts when validation fails. |

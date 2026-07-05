# TaskFlow Agent Runtime Skills

This folder contains vendored runtime skills used by `TaskFlow.AgentWorker`.

## Skills

- `ppt-master`: runtime subset of `hugohe3/ppt-master`, used for PPTX generation through `taskflow_ppt_master.py`.
- `md-to-docx`: vendored `github/awesome-copilot` Markdown-to-DOCX skill.

The worker must call these skills through TaskFlow adapters. Missing dependencies or missing output files are treated as hard failures and should be recorded in Agent events.

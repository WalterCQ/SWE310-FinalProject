# md-to-docx Vendored Source

- Upstream: https://github.com/github/awesome-copilot/blob/main/skills/md-to-docx
- Vendored content: `SKILL.md` and `scripts/`
- TaskFlow use: optional Markdown-to-DOCX runtime skill.

If the Node dependencies are not installed, TaskFlow should either use the explicitly registered `openxml-docx` skill or fail according to the selected skill policy. It must not silently claim that `md-to-docx` ran.

# ppt-master Vendored Source

- Upstream: https://github.com/hugohe3/ppt-master
- License: MIT, copied in `LICENSE`
- Vendored content: runtime subset required by TaskFlow to convert generated SVG slides into PPTX artifacts.
- TaskFlow entrypoint: `taskflow_ppt_master.py`

This is not the full upstream repository. TaskFlow uses this vendored skill as the PPT runtime boundary. The TaskFlow adapter parses Agent markdown, fails on empty slides, writes an inspectable SVG project for traceability, and renders editable PowerPoint text boxes through the `python-pptx` dependency declared by upstream `requirements.txt`.

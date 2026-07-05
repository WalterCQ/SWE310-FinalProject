# ppt-master Vendored Source

- Upstream: https://github.com/hugohe3/ppt-master
- License: MIT, copied in `LICENSE`
- Vendored content: runtime subset required by TaskFlow to convert generated SVG slides into PPTX artifacts.
- TaskFlow entrypoint: `taskflow_ppt_master.py`

This is not the full upstream repository. TaskFlow generates a deterministic SVG project from Agent markdown and calls the vendored `svg_to_pptx` runtime in native mode.

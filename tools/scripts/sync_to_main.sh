#!/bin/zsh
# Copies the validated perception/colour code from the scratch dev project into the main Unity project.
# Additive only (no --delete). Never touches Phase 1 scripts, MainScene or Git. Usage: sync_to_main.sh [--dry-run]
set -e
S=/Users/nayana/Projects/SemanticVR/tools/unity_infer_test
M=/Users/nayana/Projects/SemanticVR/SemanticVR_Unity
OPT=(-a --itemize-changes); [[ "$1" == "--dry-run" ]] && OPT+=(--dry-run)
DIRS=(Scripts/Perception Scripts/Semantics Scripts/UI Scripts/Accessibility Scripts/ColourProcessing Scripts/Editor/Perception Resources/SemanticVR Resources/Golden Settings/SemanticVR Textures/Test)
for d in $DIRS; do
  mkdir -p "$M/Assets/$d"
  rsync $OPT "$S/Assets/$d/" "$M/Assets/$d/"
  # sibling folder .meta files (new folders only; existing ones are left untouched)
  p="$d"; while [[ "$p" != "." ]]; do
    [[ -f "$S/Assets/$p.meta" && ! -f "$M/Assets/$p.meta" ]] && rsync $OPT "$S/Assets/$p.meta" "$M/Assets/$p.meta"
    p=$(dirname "$p")
  done
done
mkdir -p "$M/Assets/Models"
rsync $OPT "$S/Assets/Scenes/PerceptionDemo.unity" "$S/Assets/Scenes/PerceptionDemo.unity.meta" "$M/Assets/Scenes/"
rsync $OPT "$S/Assets/Models/rfdetr_seg_nano.onnx" "$S/Assets/Models/rfdetr_seg_nano.onnx.meta" "$M/Assets/Models/"
# package: add the inference engine line only if missing (all other manifest entries untouched)
python3 - "$M/Packages/manifest.json" "${OPT[*]}" <<'PY'
import json,sys
p=sys.argv[1]; m=json.load(open(p))
if "com.unity.ai.inference" not in m["dependencies"] and "--dry-run" not in sys.argv[2]:
    m["dependencies"]["com.unity.ai.inference"]="2.6.1"
    json.dump(m,open(p,"w"),indent=2); print("manifest.json: added com.unity.ai.inference 2.6.1")
else: print("manifest.json: inference package present or dry-run")
PY

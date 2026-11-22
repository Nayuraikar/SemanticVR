import sys
from pathlib import Path

PY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PY))            # semanticvr_ref
sys.path.insert(0, str(Path(__file__).parent))  # sharma_pairs

#!/usr/bin/env python3
from pathlib import Path
import re
ROOT=Path(__file__).resolve().parents[2]
SKIP_PREFIXES=(".git/",".KataGO/KataGo-master/")
SKIP_EXACT={"AGENTS.md","Tools/RepositoryBoundary/check_repository_boundary.py"}
TEXT_SUFFIXES={".yml",".yaml",".py",".cs",".md",".sh",".ps1",".json",".txt",".hlsl",".shader"}
PATTERNS=[(re.compile(r"github\.com/",re.I),"github.com/"),(re.compile(r"raw\.githubusercontent\.com/",re.I),"raw.githubusercontent.com/"),(re.compile(r"gitlab\.com/",re.I),"gitlab.com/"),(re.compile(r"\bgit\s+clone\b",re.I),"git clone"),(re.compile(r"\bgh\s+repo\s+clone\b",re.I),"gh repo clone"),(re.compile(r"\bgit\s+remote\s+add\b",re.I),"git remote add")]
ALLOWED_SUBSTRINGS=("github.com/cocokoishi/udon-go-ai","41898282+github-actions[bot]@users.noreply.github.com")
violations=[]
for p in ROOT.rglob("*"):
    if not p.is_file(): continue
    rel=p.relative_to(ROOT).as_posix()
    if rel in SKIP_EXACT or any(rel.startswith(x) for x in SKIP_PREFIXES): continue
    if p.suffix.lower() not in TEXT_SUFFIXES: continue
    try:text=p.read_text(encoding="utf-8")
    except UnicodeDecodeError: continue
    for lineno,line in enumerate(text.splitlines(),1):
        if any(a in line for a in ALLOWED_SUBSTRINGS): continue
        for rx,label in PATTERNS:
            if rx.search(line): violations.append((rel,lineno,label,line.strip()))
if violations:
    for v in violations: print(f"BOUNDARY VIOLATION {v[0]}:{v[1]} [{v[2]}] {v[3]}")
    raise SystemExit(1)
print("Repository boundary check: PASS")

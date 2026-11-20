"""Emit the bounded SSH argument from the local, already published repository commit."""
import ast
import base64
import hashlib
import lzma
from pathlib import Path
import subprocess
import sys

COMMIT="8c80d8468834d2e8cdf6353a443ec35fa51e70ae"
SOURCES=("hourly-backup/backup.py","supervised-archive.py","supervised-operations.py",
         "postgres-backup.sh","verify-postgres-backup.sh")
root=Path(__file__).resolve().parent
repo=Path(sys.argv[1])
i=(root/"Inspeccion-DEV532-RC11.py").read_text(encoding="utf-8")
k=(root/"Corte-DEV532-RC11.py").read_text(encoding="utf-8")
base=[]
for node in ast.parse(i).body:
    if isinstance(node,(ast.Import,ast.ImportFrom)) or \
       isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id in
               {"PG","BASE","EXPECTED_EF","SQL","PG_ENTRY"} for t in node.targets) or \
       isinstance(node,ast.FunctionDef) and node.name in {"private_file","dsn_target"}:
        base.append(ast.get_source_segment(i,node))
files={name:hashlib.sha256(subprocess.check_output(["git","-C",str(repo),"cat-file","blob",
                  COMMIT+":scripts/ops/"+name], stderr=subprocess.DEVNULL,timeout=8)).hexdigest() for name in SOURCES}
blobs=repr(files)
code="\n".join(base)+"\nbackup_hashes="+blobs+"\n"+k+"\nsys.exit(main_reconcile(sys.argv[-1]) if sys.argv[-2]=='reconcile' else main_cutover(sys.argv[-1]))\n"
compile(code,"<rc11>","exec")
result=base64.b64encode(lzma.compress(code.encode(),preset=9)).decode()
if len(result)>28000:
    raise SystemExit("PAYLOAD_RC11_EXCEDE_LIMITE_SSH")
print(result)

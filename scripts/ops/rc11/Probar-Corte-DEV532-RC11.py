"""Synthetic state machine and borrowed-lock tests. Never invokes network, Docker or SQL."""
import base64
from contextlib import redirect_stdout
import io
import lzma
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
from unittest.mock import patch
import json
import copy

here=Path(__file__).resolve().parent
repo=sys.argv[1]
payload=subprocess.check_output([sys.executable,"-B",str(here/"Empaquetar-DEV532-RC11.py"),repo])
code=lzma.decompress(base64.b64decode(payload)).decode().rsplit("sys.exit(main_reconcile",1)[0]
fcntl=SimpleNamespace(LOCK_EX=2,LOCK_NB=4,flock=None)
sys.modules["fcntl"]=fcntl
ns={}
exec(compile(code,"<rc11 exact>", "exec"),ns)
assert all(len(d)==64 for d in ns["backup_hashes"].values())
spec=ns["firewall_spec"]("token")
assert 'add table inet' in spec and 'hook prerouting priority -310' in spec
assert 'iifname != "lo" fib daddr type local tcp dport 443 counter drop' in spec and 'tcp dport 22' not in spec
assert ns["image"]("web",ns["NEW"]["web"]).endswith(ns["NEW"]["web"])
initial=dict(ns)

def trial(mode):
 with tempfile.TemporaryDirectory() as d:
    root=Path(d)
    ns.update(initial)
    ns["signal"]=SimpleNamespace(SIGHUP=1,SIGINT=2,SIGTERM=15,SIGALRM=14,signal=lambda *a:None)
    lock=root/"manual.lock"; lock.write_text("")
    env=root/"pilot.env"; original="HFPOS_BACKEND_IMAGE=x\nHFPOS_WEB_IMAGE=x\nHFPOS_MIGRATIONS_IMAGE=x\nHFPOS_RELEASE_VERSION=y\n"
    env.write_text(original,encoding="utf-8")
    ns.update(LOCK=lock,ENV=env,STATE=root/"state",private_file=lambda _:True)
    events=[]; guard=False; stopped=False; applied=False; rebuilt=set(); restarted=set(); acquired=0
    web_private=False
    def flock(fd,flags):
      nonlocal acquired
      acquired+=1
      if mode=="lock-fail": raise BlockingIOError()
    fcntl.flock=flock
    def ask(word,seconds=0):
      events.append(word)
      if mode=="authorization-denied" and word=="AUTORIZO R4 RC11": raise ns["Stop"]("DENIED")
      if mode=="external-proof-fail" and word=="CIERRE DUAL CONFIRMADO": raise ns["Stop"]("PROOF")
      if mode=="backup-fail" and word=="VOLVER A RC8": raise ns["Stop"]("RETURN_DENIED")
    def items():
      result=[]
      for name in ("backend","web","postgres"):
        i=("backend","web","postgres").index(name)
        ref=ns["image"](name,ns["NEW"][name]) if name in rebuilt else ns["image"](name,ns["OLD"][i]) if i<2 else "pg16"
        running=not stopped or i==2 or name in rebuilt or name in restarted
        result.append(dict(Id=name+("_new" if name in rebuilt else ""),Config={"Image":ref},
                    Mounts=[{"Destination":name}],State={"Status":"running" if running else "exited",
                    "Running":running,"ExitCode":0,"OOMKilled":False,"Health":{"Status":"healthy"}}))
      return result
    def cmd(args, failure, data=None,timeout=0):
      nonlocal guard,stopped,applied,web_private
      if args[:2]==["docker","version"]: return "28.1.0"
      if args[:2]==["docker","port"]:
        return (("8443/tcp -> 127.0.0.1:443\n" if web_private else "8443/tcp -> 0.0.0.0:443\n8443/tcp -> [::]:443\n") if "web" in args[-1] else "")
      if args[:2]==["nft","-c"]: events.append("nft-check");return ""
      if args[:2]==["nft","-f"]: events.append("close");guard=True;return ""
      if "stop" in args: assert guard;stopped=True;events.append("drain");return ""
      if "pull" in args: events.append("prefetch");return ""
      if "run" in args:
        assert guard and stopped and "backup-ok" in events
        events.append("ef-write"); applied=True
        if mode=="ef-receipt-lost": raise ns["Stop"]("NO_RECEIPT")
        return "MIGRATION PASS\n"
      if "up" in args:
        assert guard
        if args[-1]=="web":
          web_private=args[0]=="private"
          if web_private:
            events.append("private-web")
            if mode=="guard-lost": guard=False
          else:
            assert ("VALIDADO FUNCIONAL Y ABRIR RC11" in events or "REABRIR RC8" in events)
        events.append("up-"+args[-1])
        (rebuilt if applied else restarted).add(args[-1])
        return ""
      if "start" in args:
        assert guard and not applied
        events.append("return-"+args[-1]);restarted.add(args[-1]);return ""
      if "exec" in args: return ""
      raise AssertionError(args)
    def recover(_):
      nonlocal guard
      assert guard
      guard=False
      events.append("remove-own-guard")
    def sample(ef,drained=False):
      assert (ef==ns["EF1"])==applied
      if drained: assert stopped and not rebuilt
      events.append("read-ef-after" if applied else "read-ef-before")
      return "hash"
    def verify(*a,**kw):
      if not guard: raise ns["Stop"]("GUARD_LOST")
    def backup(*a):
      assert guard and stopped and not applied and acquired==1
      events.append("backup")
      if mode in ("backup-fail","backup-return"): raise ns["Stop"]("BACKUP_FAIL")
      events.append("backup-ok")
      return "0"*32
    ns.update(cmd=cmd,ask=ask,sample=sample,rendered=lambda _: {},check_delta=lambda *a:None,
              check_dsn=lambda *a:None, tls=lambda a:None, check_ingress_priority=lambda:None, containers=items,
              verify_guard=verify, remove_guard=recover, absent=lambda:not guard,
              check_migrator_auth=lambda a: events.append("auth-probe"),
              borrow_and_capture=backup, backup_bindings=lambda *a:None,
              private_compose=lambda *a:["private"],check_private_network=lambda _:None,
              load_worker=lambda:SimpleNamespace(load_config=lambda _:dict(containers=ns["NAMES"])))
    output=io.StringIO()
    with patch.object(os,"O_NOFOLLOW",getattr(os,"O_NOFOLLOW",0),create=True), \
         patch.object(os,"geteuid",lambda:0,create=True), redirect_stdout(output):
      status=ns["main_cutover"]("22")
    if mode=="happy":
      assert status==0 and not guard and env.read_text()!=original
      assert events.count("ef-write")==1 and (root/"state"/"ef-confirmed").is_file()
      assert events.index("backup-ok")<events.index("ef-write")<events.index("read-ef-after")<events.index("up-backend")<events.index("up-web")<events.index("remove-own-guard")
    else:
      assert status==4 and env.read_text()==original
      if mode=="ef-receipt-lost":
        assert events.count("ef-write")==1 and guard and not rebuilt and (root/"state"/"ef-confirmed").is_file()
      elif mode=="guard-lost":
        assert "private-web" in events and web_private and "VALIDADO FUNCIONAL Y ABRIR RC11" not in events
        assert events.count("ef-write")==1 and events.count("up-web")==1
      else:
        assert "ef-write" not in events
      if mode=="external-proof-fail": assert "remove-own-guard" in events and not guard and not stopped
      if mode=="backup-fail": assert guard and stopped and not rebuilt
      if mode=="backup-return":
        assert not guard and restarted=={"backend","web"} and not rebuilt
        assert events.index("return-backend")<events.index("private-web")<events.index("REABRIR RC8")<events.index("remove-own-guard")
      if mode=="authorization-denied": assert acquired==0 and not guard and events[-1]=="AUTORIZO R4 RC11"
      if mode=="lock-fail": assert acquired==1 and not guard and not stopped
    print("PASS",mode)

for mode in ("happy","authorization-denied","lock-fail","external-proof-fail","backup-fail","backup-return","ef-receipt-lost","guard-lost"):
    trial(mode)

ns.update(initial)
with tempfile.TemporaryDirectory() as d:
    root=Path(d)
    lock=root/"lock"; lock.write_text("")
    ns["LOCK"]=lock
    marker="INTEGRITY_OFFSITE_VERIFIED.json"
    fd=os.open(lock,os.O_RDWR)
    calls=[]
    def unexpected(*args): raise AssertionError("must not acquire again")
    fcntl.flock=unexpected
    def capture(c,r,ident):
       assert "retention" not in c and c["state"]==str(root/"private-state")
       with worker.maintenance_lock():
           n=[{"Id":str(i),"Image":"test","Config":{},"Mounts":[],"State":s} for i,s in enumerate([
             {"Status":"exited","ExitCode":0,"OOMKilled":False},
             {"Status":"exited","ExitCode":0,"OOMKilled":False},
             {"Running":True,"Health":{"Status":"healthy"}}])]
           assert len(worker.stable_containers(n))==3
           n[0]["State"]["OOMKilled"]=True
           try: worker.stable_containers(n)
           except ns["Stop"]: calls.append("oom-blocked")
           else: raise AssertionError("unexpected frozen")
           folder=root/ident;folder.mkdir()
           (folder/marker).write_text(json.dumps(dict(recovery_id=ident,completed_utc="2026Z",
               ciphertext_sha256="0"*64,ciphertext_bytes=1)))
       return 0
    worker=SimpleNamespace(backup_attempt=capture, Runner=lambda:None, secure_path=lambda p,**_:p)
    try:
      ns["borrow_and_capture"](worker,{"base":str(root),"state":"ORIGINAL","retention":{}},fd,root/"private-state",["0","1","2"])
      assert calls==["oom-blocked"]
    finally: os.close(fd)
print("PASS borrowed lock and adapter states")

ns.update(initial)
with tempfile.TemporaryDirectory() as d:
    root=Path(d)
    key=root/"keys"; key.mkdir()
    other=root/"wrong"; other.mkdir()
    tls=root/"tls"; tls.mkdir()
    config=root/"config"; config.mkdir()
    env=config/"pilot.env"; env.touch()
    definition={"keys":str(key),"sources":{"tls":str(tls),"config":str(config)}}
    mounts=[{"Mounts":[{"Type":"bind","Source":str(key),"Destination":"/keys"}]},
            {"Mounts":[{"Type":"bind","Source":str(tls),"Destination":"/etc/nginx/tls"}]}]
    ns["backup_bindings"](definition,mounts,env)
    definition["keys"]=str(other)
    try: ns["backup_bindings"](definition,mounts,env)
    except ns["Stop"]: pass
    else: raise AssertionError("unrelated yet valid keyring passed")
print("PASS wrong keyring is rejected")

ns.update(initial)
with tempfile.TemporaryDirectory() as d:
    root=Path(d)
    (root/"ef-attempted").touch()
    ns.update(STATE=root,absent=lambda:True)
    running=False
    def read_only(args,code,data=None,timeout=0):
      if args[:2]==["docker","port"]: return "8443/tcp -> 127.0.0.1:443\n"
      if args[:2]==["docker","ps"]: return ns["MIG_CONTAINER"]+"\n"
      if args[:2]==["docker","inspect"]:
        labels={"com.docker.compose.project":"hf-one-pilot","com.docker.compose.service":"migrations"}
        return json.dumps([{"Config":{"Labels":labels,"Image":ns["image"]("migrations",ns["NEW"]["migrations"])},
                            "State":{"Running":running,"Status":"running" if running else "exited","ExitCode":0}}])
      assert args[:2]==["docker","exec"] and "READ ONLY" in data
      return ns["EF1"]+"\nFIN\n"
    ns["cmd"]=read_only
    with patch.object(os,"geteuid",lambda:0,create=True),patch.object(Path,"stat",lambda *a,**k:SimpleNamespace(st_uid=0,st_mode=0o40700),create=True),patch.object(Path,"is_symlink",lambda _:False):
      output=io.StringIO()
      with redirect_stdout(output):
        assert ns["main_reconcile"]("22")==0
        running=True
        assert ns["main_reconcile"]("22")==4
      assert "EF COMPROMETIDA" in output.getvalue() and "MIGRATOR_PUEDE_SEGUIR_EN_CURSO" in output.getvalue()
print("PASS recovery distinguishes committed and still-running without mutations")

ns.update(initial)
ns["daemon_policy"](["dockerd","--iptables=true","--allow-direct-routing=false"],{})
for argv,cfg in ((["dockerd","--ip6tables=false"],{}),(["dockerd"],{"iptables":False}),
                 (["dockerd"],{"allow-direct-routing":True}),(["dockerd","--allow-direct-routing"],{})):
    try: ns["daemon_policy"](argv,cfg)
    except ns["Stop"]: pass
    else: raise AssertionError("route/filter bypass not blocked")
print("PASS direct routing and disabled filtering are rejected")

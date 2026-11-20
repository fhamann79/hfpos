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
assert len(payload.strip()) <= 28000
code=lzma.decompress(base64.b64decode(payload)).decode().rsplit("\nentry={'reconcile'",1)[0]
fcntl=SimpleNamespace(LOCK_EX=2,LOCK_NB=4,flock=None)
sys.modules["fcntl"]=fcntl
ns={}
exec(compile(code,"<rc11 exact>", "exec"),ns)
assert all(len(d)==64 for d in ns["backup_hashes"].values())
spec=ns["firewall_spec"]("token")
assert 'create table inet' in spec and 'hook prerouting priority -310' in spec
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
    ns.update(LOCK=lock,ENV=env,STATE=root/"state",WINDOW=root/"maintenance.nft",private_file=lambda _:True,
              sync_dir=lambda _:None,
              durable_file=lambda p,s:p.write_text(s,encoding="ascii"))
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
      if args[0] in ("nft",ns["NFT"]) and args[1]=="-c": events.append("nft-check");return ""
      if args[:2]==["nft","-f"]: events.append("close");guard=True;return ""
      if "stop" in args: assert guard and "permanent-installed" in events;stopped=True;events.append("drain");return ""
      if "pull" in args: events.append("prefetch");return ""
      if "run" in args:
        assert guard and stopped and "backup-ok" in events
        events.append("ef-write"); applied=True
        if mode=="ef-receipt-lost": raise ns["Stop"]("NO_RECEIPT")
        return "MIGRATION PASS\n"
      if "up" in args:
        assert guard and "permanent-installed" in events
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
    def install(_):
      events.append("permanent-installed")
      if mode=="install-fail": raise ns["Stop"]("INSTALL")
    def assert_permanent(_=None):
      assert "permanent-installed" in events
      if mode=="permanent-lost" and "private-web" in events: raise ns["Stop"]("PERMANENT_LOST")
    def refresh(*_):
      assert_permanent()
      assert guard and "up-web" in events
      if mode=="refresh-fail": raise ns["Stop"]("REFRESH")
      events.append("permanent-refreshed")
    def sample(ef,drained=False):
      assert (ef==ns["EF1"])==applied
      if drained: assert stopped and not rebuilt
      events.append("read-ef-after" if applied else "read-ef-before")
      return "hash"
    def verify(*a,**kw):
      if not guard: raise ns["Stop"]("GUARD_LOST")
    def network(*a,**kw):
      if mode=="public-proof-fail" and "up-web" in events and not web_private:
        raise ns["Stop"]("WEB_HTTPS_RUTA_IPV6_NO_ACREDITADA")
      return "changed" if mode=="daemon-change" and "auth-probe" in events else "daemon"
    def backup(*a):
      assert guard and stopped and not applied and acquired==1
      events.append("backup")
      if mode in ("backup-fail","backup-return"): raise ns["Stop"]("BACKUP_FAIL")
      events.append("backup-ok")
      return "0"*32
    ns.update(cmd=cmd,ask=ask,sample=sample,rendered=lambda _: {},check_delta=lambda *a:None,
              check_dsn=lambda *a:None, tls=lambda a:None, check_ingress_priority=lambda:None, containers=items,
              verify_guard=verify, remove_guard=recover, absent=lambda:not guard, guard_host_checks=lambda:None,
              install_permanent=install, assert_permanent=assert_permanent, refresh_permanent=refresh,
              network_guard=lambda *a: (network(),{"br-web":["172.30.10.0/24"],"br-db":["172.30.20.0/24"]},
                                        {4:("eth0","192.0.2.1"),6:("eth0","2001:db8::1")},
                                        {4:("dnat","172.30.10.2"),6:("proxy","172.30.10.2")}),
              check_migrator_auth=lambda a: events.append("auth-probe"),
              borrow_and_capture=backup, backup_bindings=lambda *a:None,
              private_compose=lambda *a:["private"],active_docker_identity=network,
              load_worker=lambda:SimpleNamespace(load_config=lambda _:dict(containers=ns["NAMES"])))
    output=io.StringIO()
    with patch.object(os,"O_NOFOLLOW",getattr(os,"O_NOFOLLOW",0),create=True), \
         patch.object(os,"geteuid",lambda:0,create=True), redirect_stdout(output):
      status=ns["main_cutover"]("22","192.0.2.1","2001:db8::1")
    if mode=="happy":
      assert status==0 and not guard and env.read_text()!=original,(output.getvalue(),events)
      assert events.count("ef-write")==1 and (root/"state"/"ef-confirmed").is_file()
      assert events.index("AUTORIZO R4 RC11")<events.index("permanent-installed")<events.index("close")<events.index("drain")
      assert events.index("backup-ok")<events.index("ef-write")<events.index("read-ef-after")<events.index("up-backend")<events.index("up-web")<events.index("remove-own-guard")
      assert events.index("permanent-refreshed")<events.index("remove-own-guard")
    else:
      assert status==4 and (env.read_text()!=original)==(mode in ("refresh-fail","public-proof-fail"))
      if mode=="ef-receipt-lost":
        assert events.count("ef-write")==1 and guard and not rebuilt and (root/"state"/"ef-confirmed").is_file()
      elif mode in ("guard-lost","permanent-lost"):
        assert "private-web" in events and web_private and "VALIDADO FUNCIONAL Y ABRIR RC11" not in events
        assert events.count("ef-write")==1 and events.count("up-web")==1
      elif mode in ("refresh-fail","public-proof-fail"):
        assert guard and "remove-own-guard" not in events
      else:
        assert "ef-write" not in events
      if mode=="external-proof-fail": assert "remove-own-guard" not in events and guard and not stopped
      if mode=="public-proof-fail":
        assert applied and events.count("ef-write")==1
        assert "BLOQUEO=WEB_HTTPS_RUTA_IPV6_NO_ACREDITADA" in output.getvalue()
      if mode=="install-fail": assert not guard and "prefetch" not in events and not stopped
      if mode=="backup-fail": assert guard and stopped and not rebuilt
      if mode=="backup-return":
        assert not guard and restarted=={"backend","web"} and not rebuilt
        assert events.index("return-backend")<events.index("private-web")<events.index("REABRIR RC8")<events.index("remove-own-guard")
      if mode=="authorization-denied": assert acquired==0 and not guard and events[-1]=="AUTORIZO R4 RC11"
      if mode=="lock-fail": assert acquired==1 and not guard and not stopped
    print("PASS",mode)

for mode in ("happy","authorization-denied","lock-fail","external-proof-fail","backup-fail","backup-return",
             "ef-receipt-lost","guard-lost","daemon-change","install-fail","permanent-lost","refresh-fail",
             "public-proof-fail"):
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
       assert r.run(["docker","inspect"])==["/usr/bin/docker","--host","unix:///run/docker.sock","inspect"]
       assert worker.Runner().run(["docker","exec"])==["/usr/bin/docker","--host","unix:///run/docker.sock","exec"]
       assert r.run(["rclone","version"])==["rclone","version"]
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
    class Runner:
      def run(self,args): return args
    worker=SimpleNamespace(backup_attempt=capture, Runner=Runner, secure_path=lambda p,**_:p)
    try:
      ns["borrow_and_capture"](worker,{"base":str(root),"state":"ORIGINAL","retention":{}},fd,root/"private-state",["0","1","2"])
      assert worker.Runner is Runner
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
    window=root/"maintenance.nft"
    ns.update(STATE=root,WINDOW=window,absent=lambda:True,assert_permanent=lambda:None,
              active_docker_identity=lambda:None)
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
    real_stat=Path.stat
    def state_stat(path,*args,**kwargs):
      return SimpleNamespace(st_uid=0,st_mode=0o40700) if path==root else real_stat(path,*args,**kwargs)
    with patch.object(os,"geteuid",lambda:0,create=True),patch.object(Path,"stat",state_stat):
      output=io.StringIO()
      with redirect_stdout(output):
        assert ns["main_reconcile"]("22")==0
        running=True
        assert ns["main_reconcile"]("22")==4
        running=False
        window.write_text(ns["firewall_spec"]("hf-one-rc11-"+"1"*32),encoding="ascii")
        assert ns["main_reconcile"]("22")==4
        assert window.is_file()
        window.unlink()
        def lost_permanent():
          raise ns["Stop"]("GUARD_PERMANENTE_AUSENTE")
        ns["assert_permanent"]=lost_permanent
        assert ns["main_reconcile"]("22")==4
      assert "EF COMPROMETIDA" in output.getvalue() and "MIGRATOR_PUEDE_SEGUIR_EN_CURSO" in output.getvalue()
      assert output.getvalue().count("STOP_INGRESO:")==2
print("PASS recovery distinguishes committed and still-running, missing guard and disk/kernel drift without mutations")

ns.update(initial)
ns["daemon_policy"](["dockerd","--iptables=true","--allow-direct-routing=false"],{})
for argv,cfg in ((["dockerd","--ip6tables=false"],{}),(["dockerd"],{"iptables":False}),
                 (["dockerd"],{"allow-direct-routing":True}),(["dockerd","--allow-direct-routing"],{})):
    try: ns["daemon_policy"](argv,cfg)
    except ns["Stop"]: pass
    else: raise AssertionError("route/filter bypass not blocked")
print("PASS direct routing and disabled filtering are rejected")

ns.update(initial)
def rejects(f,*args):
    try: f(*args)
    except (ns["Stop"],ValueError): return
    raise AssertionError("unsafe network proof passed")

parse=ns["netfilter_rules"]
raw="""*raw
:PREROUTING ACCEPT [0:0]
-A PREROUTING -d 127.0.0.0/8 ! -i lo -j DROP
COMMIT
"""
c={"chain":{"family":"ip","table":"raw","name":"PREROUTING","type":"filter","hook":"prerouting","prio":-300}}
ns["prove_raw_hooks"]([c])
rejects(ns["prove_raw_hooks"],[c,{"flowtable":{}}])
early=copy.deepcopy(c)
early["chain"]["table"]="custom"
rejects(ns["prove_raw_hooks"],[c,early])
ids={"a":"a"*64,"b":"b"*64}
def ep(bridge,v4,v6=""):
    return {"NetworkID":ids[bridge],"IPAddress":v4,"GlobalIPv6Address":v6}
i=[{"NetworkSettings":{"Networks":es}} for es in ({"a":ep("a","172.30.52.11","fd00::2"),"b":ep("b","172.31.0.2")},
   {"a":ep("a","172.30.52.10","fd00::1")},{"b":ep("b","172.31.0.3")})]
n={k:{"Id":k,"Driver":"bridge","Internal":k==ids["b"],"EnableIPv6":k==ids["a"],
      "Labels":{"com.docker.compose.project":"hf-one-pilot"},"Options":{},
      "IPAM":{"Config":[{"Subnet":"172.31.0.0/16"}] if k==ids["b"] else
                       [{"Subnet":"172.30.52.0/24"},{"Subnet":"fd00::/64"}]}} for k in ids.values()}
ns["bridge_shape"](i,n)
bad=copy.deepcopy(n)
bad[ids["a"]]["Options"]["com.docker.network.bridge.gateway_mode_ipv6"]="routed"
rejects(ns["bridge_shape"],i,bad)
bad=copy.deepcopy(n)
bad[ids["a"]]["Options"]["com.docker.network.bridge.trusted_host_interfaces"]="eth0"
rejects(ns["bridge_shape"],i,bad)
bad=copy.deepcopy(i)
bad[1]["NetworkSettings"]["Networks"]["a"]["IPAddress"]="192.0.2.1"
rejects(ns["bridge_shape"],bad,n)
loop="-A PREROUTING -d 127.0.0.1/32 ! -i lo -p tcp -m tcp --dport 443 -j DROP\n"
approvable={4:parse(raw.replace("COMMIT",loop+"COMMIT")),6:{}}
socket="LISTEN 0 1024 127.0.0.1:443 0.0.0.0:*"
ns["prove_private_publication"](approvable,["172.30.52.10"],socket)
rejects(ns["prove_private_publication"],{4:parse(raw),6:{}},["172.30.52.10"],socket)
rejects(ns["prove_private_publication"],approvable,["172.30.52.10"],socket.replace("127.0.0.1","[::]"))
with_nat=copy.deepcopy(approvable)
with_nat[6][("nat","DOCKER")]=[["-j","DNAT","--to-destination","fd00::2"]]
rejects(ns["prove_private_publication"],with_nat,["172.30.52.10"],socket)
print("PASS private publication rejects missing loopback drop, public listener and unsafe bridge options")

ports=[{"ifindex":8,"link_index":2,"linkinfo":{"info_kind":"veth"}},
       {"ifindex":9,"link_index":3,"linkinfo":{"info_kind":"veth"}}]
ns["prove_bridge_members"](ports,{8:2,9:3})
rejects(ns["prove_bridge_members"],ports+[
    {"ifindex":10,"link_index":4,"linkinfo":{"info_kind":"tun"}}],{8:2,9:3})
rejects(ns["prove_bridge_members"],ports,{8:2,9:4})
rejects(ns["prove_bridge_members"],ports[:1],{8:2,9:3})
rejects(ns["prove_bridge_members"],ports, {8:2})
print("PASS bridge membership rejects external ports and unproved peers")


def exec_record(command, ignored="no"):
    return "{ path="+command.split()[0]+" ; argv[]="+command+" ; ignore_errors="+ignored+" ; pid=0 }"


check=ns["BOOT_COMMAND"]+" --check"
assert ns["exact_exec"](exec_record(check),check)
assert not ns["exact_exec"](exec_record(check,"yes"),check)
assert not ns["exact_exec"](exec_record("/bin/true "+check),check)
assert not ns["exact_exec"](exec_record(check)+exec_record("/bin/true"),check)
assert "Requires="+ns["GUARD_UNIT"] in ns["DROPIN_TEXT"] and "After="+ns["GUARD_UNIT"] in ns["DROPIN_TEXT"]
tok="hf-one-rc11-"+"a"*32
table=ns["TABLE"]
def match(op,left,right):
    return {"match":dict(op=op,left=left,right=right)}
gate=[{"table":dict(family="inet",name=table,handle=1)},
      {"chain":dict(family="inet",table=table,name="gate",type="filter",hook="prerouting",
                    prio=-310,policy="accept",handle=2)}]
for v in (4,6):
    gate.append({"rule":dict(family="inet",table=table,chain="gate",comment=tok,expr=[
        match("==",{"meta":{"key":"nfproto"}},f"ipv{v}"),
        match("!=",{"meta":{"key":"iifname"}},"lo"),
        match("==",{"fib":{"result":"type","flags":["daddr"]}},"local"),
        match("==",{"payload":{"protocol":"tcp","field":"dport"}},443),
        {"counter":{"packets":5,"bytes":300}},{"drop":None}])})
ns["verify_gate_rules"](gate,tok,True)
altered=copy.deepcopy(gate)
altered[1]["chain"]["prio"]=-309
reordered=gate[:2]+gate[2:][::-1]
with tempfile.TemporaryDirectory() as d:
    base=Path(d)
    marker,ident,policy,snapshot=[base/name for name in ("maintenance.nft","guard-id","guard.nft","expected")]
    for path,value in ((marker,ns["firewall_spec"](tok)),(ident,tok),(policy,"spec"),(snapshot,"permanent")):
        path.write_text(value,encoding="ascii")
    def nft(args,code,data=None,timeout=0):
        assert args[0]==ns["NFT"]
        if kernel is None:
            raise ns["Stop"](code)
        return json.dumps({"nftables":kernel})
    ns.update(GUARD_DIR=base,STATE=base,WINDOW=marker,POLICY=policy,SNAPSHOT=snapshot,
              private_file=Path.is_file,guard_snapshot=lambda:"permanent",cmd=nft,verify_ufw=lambda:None)
    lst=Path.lstat
    with patch.object(Path,"lstat",lambda p: SimpleNamespace(st_uid=0,st_mode=0o40700) if p==base else lst(p)):
        kernel=gate
        ns["boot_check"]("--check")
        for kernel in (None,gate[:1],altered,reordered,gate+[{"set":{"name":"extra"}}]):
            rejects(ns["boot_check"],"--check")
        kernel=gate
        ident.write_text("hf-one-rc11-"+"b"*32,encoding="ascii")
        rejects(ns["boot_check"],"--check")
        ident.write_text(tok,encoding="ascii")
        marker.write_text(ns["firewall_spec"](tok)+"# extra\n",encoding="ascii")
        rejects(ns["boot_check"],"--check")
print("PASS exact boot command, owned marker, and absent, empty, altered or reordered gate")

ns.update(initial)
with patch.object(subprocess,"run",return_value=SimpleNamespace(returncode=0,stdout="ok")) as invoke:
    with patch.dict(os.environ,{"DOCKER_CONTEXT":"remote","DOCKER_HOST":"tcp://example.invalid:2375"}):
        assert ns["cmd"](["docker","compose","config"],"FAILED")=="ok"
    assert invoke.call_args.args[0]==["/usr/bin/docker","--host","unix:///run/docker.sock","compose","config"]
    assert not ({"DOCKER_CONTEXT","DOCKER_HOST"} & invoke.call_args.kwargs["env"].keys())
row="00000000: 00000002 00000000 00010000 0001 01 1123 "
assert ns["socket_listener_inode"](row+"/run/docker.sock",{"/run/docker.sock"})=="1123"
rejects(ns["socket_listener_inode"],row+"/tmp/wrong.sock",{"/run/docker.sock"})
rejects(ns["socket_listener_inode"],(row+"/run/docker.sock\n")*2,{"/run/docker.sock"})
print("PASS pinned CLI/Compose, backup and cleanup; missing/ambiguous kernel socket rejected")

nat="""*nat
:PREROUTING ACCEPT [0:0]
:DOCKER - [0:0]
-A PREROUTING -m addrtype --dst-type LOCAL -j DOCKER
-A DOCKER ! -i br-app -p tcp -m tcp --dport 443 -j DNAT --to-destination 172.30.52.10:8443
COMMIT
"""
origins={4:("eth0","192.0.2.1"),6:("eth0","2001:db8::1")}
ports={4:"",6:'LISTEN 0 4096 [::]:443 [::]:* users:(("docker-proxy",pid=235,fd=4)) ino:1081'}
published={4:"172.30.52.10"}
def proxy(*a):
    return a==("235","4","1081","231","::","172.30.52.10")
ns["proxy_target"]=proxy
rules={4:parse(nat),6:{}}
proof=ns["prove_public_https"](rules,ports,origins,published,"br-app","231")
assert proof=={4:("dnat","172.30.52.10"),6:("proxy","172.30.52.10")}
for bad in (nat.replace("52.10:8443","52.11:8443"),nat.replace("52.10:8443","52.10:9443"),
            nat.replace("-A PREROUTING -m","-A PREROUTING -j ACCEPT\n-A PREROUTING -m")):
    rejects(ns["prove_public_https"],{4:parse(bad),6:{}},ports,origins,published,"br-app","231")
for bad in ("",ports[6].replace("docker-proxy","nginx"),ports[6].replace("pid=235","pid=999")):
    rejects(ns["prove_public_https"],rules,{4:"",6:bad},origins,published,"br-app","231")
guard=ns["guard_spec"]({"br-app":["172.30.52.0/24"],"br-db":["172.30.53.0/24"]},origins,proof)
assert "input iifname \"eth0\" meta nfproto ipv4" not in guard
assert 'input iifname "eth0" meta nfproto ipv6 ip6 daddr 2001:db8::1 tcp dport 443 return' in guard
assert "172.30.52.10 tcp dport 8443 ct status dnat" in guard
print("PASS IPv4 exact DNAT and IPv6 owned proxy; failed public proof keeps quarantine")

config='IPV6=yes\nMANAGE_BUILTINS=no\nIPT_SYSCTL=\'\'\n'
allowed={"IPV6":("yes",),"MANAGE_BUILTINS":("no",),"IPT_SYSCTL":("","/etc/ufw/sysctl.conf")}
assert ns["ufw_config"](config,allowed)["IPT_SYSCTL"]==""
for unsafe in (config+"IPV6=yes\n",config+"nft flush ruleset\n",config.replace("=no","=yes"),
               config.replace("''",'"$(nft flush ruleset)"'),config.replace("''","/tmp/sysctl.conf"),
               config.replace("IPV6=yes\n","")):
    rejects(ns["ufw_config"],unsafe,allowed)
vendor="/usr/lib/ufw/ufw-init"
props=dict(LoadState="loaded", ActiveState="active", FragmentPath="/usr/lib/systemd/system/ufw.service",
           NeedDaemonReload="no",ExecStart=exec_record(vendor+" start quiet"),ExecStop=exec_record(vendor+" stop"))
ns.update(systemd_props=lambda *a:props,verify_ufw_scripts=lambda:None)
with patch.object(Path,"resolve",lambda *a,**kw:Path(vendor)):
    ns["verify_ufw"]()
    for key,value in (("ExecReload",exec_record("/usr/sbin/nft flush ruleset")),
                      ("ExecStart",exec_record(vendor+" start quiet","yes")),("DropInPaths","/etc/custom.conf")):
        old=props.get(key,"")
        props[key]=value
        rejects(ns["verify_ufw"])
        props[key]=old
print("PASS UFW vendor unit and strict local configuration (mocked; no real reload)")

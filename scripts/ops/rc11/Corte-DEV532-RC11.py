"""One operator-controlled RC11 cutover. Root, confirmed SSH, no automatic retry."""
from contextlib import contextmanager
import copy
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import tempfile
import time
import uuid

TABLE = "hf_one_rc11_maintenance"
LOCK = Path("/run/lock/hf-one-manual-backup.lock")
ENV = Path("/etc/hf-one/pilot/config/pilot.env")
NAMES = ("hf-one-pilot-backend-1","hf-one-pilot-web-1","hf-one-pilot-postgres-1")
OLD = ("sha256:463d6030cf3759f4659fae085a74be948f6fa76d2205f69f5c98c8ae77f11b9e",
       "sha256:383e0575fc86c1410cee7cd03cdcb10b0cb3ea06e9321f36d9f13b38db0fbf2f")
NEW = {"backend":"sha256:7dbbf1f68bddb1653d601f6dc79037bcb9c3bacf4af41993a6345764d753a8b6",
       "web":"sha256:4ee53b0a1b27fd86cf2107de7f43bb6962c6eb7be35807664daaea2fa3241406",
       "migrations":"sha256:655428c5e52d7fc2defc7dc74a2a5b95ece5373030f88ac042d9bdac8fd8d753"}
EF0 = EXPECTED_EF
EF1 = "EF 64 ef329fd8db0880650be0dbff6b7dfc7bd41e0bf7e1b643b8bf5d5331e81b814c"
MIG = "20261119120000_ReconcileTenantAdminBatchPricePermission"
MIG_CONTAINER = "hf-one-rc11-migrator"
TAG = "v0.1.0-rc.11"
ROOT = Path("/var/lib")
STATE = ROOT / "hf-one-rc11-cutover"
SAFE_ENV = {"PATH":"/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin","HOME":"/root","LC_ALL":"C"}


class Stop(Exception):
    pass


def need(ok, code):
    if not ok:
        raise Stop(code)


def cmd(args, code, data=None, timeout=30):
    try:
        p = subprocess.run(args,input=data,text=True,encoding="utf-8",errors="strict",
                           capture_output=True,timeout=timeout,env=SAFE_ENV)
        need(p.returncode==0, code)
        return p.stdout
    except (OSError, UnicodeError, subprocess.TimeoutExpired):
        raise Stop(code) from None


def image(kind,digest):
    return f"ghcr.io/fhamann79/hfpos-{kind}@{digest}"


def compose(env):
    return ["docker","compose","-p","hf-one-pilot","--project-directory",str(BASE),"--env-file",str(env),
            "-f",str(BASE/"deploy/compose/compose.production.example.yml"),
            "-f",str(BASE/"deploy/compose/compose.production.postgres.example.yml"),"--profile","migration"]


def rendered(env):
    return json.loads(cmd(compose(env)+["config","--format","json"],"COMPOSE_CONFIG_NO_VERIFICADA"))


def check_dsn(cfg, observed_db):
    for service,role in (("backend","hf_one_runtime"),("migrations","hf_one_migrator")):
        e=cfg["services"][service]["environment"]
        d=dsn_target(e.get("ConnectionStrings__DefaultConnection"))
        need(d.get("username")==role and d.get("host") in ("postgres",NAMES[2])
             and d.get("port","5432")=="5432"
             and hashlib.sha256(d.get("database","").encode()).hexdigest()==observed_db,
             "COMPOSE_IDENTIDAD_"+service.upper())
    need(str(cfg["services"]["migrations"]["environment"].get("HFPOS_MIGRATION_APPROVED")).upper()=="NO",
         "MIGRATOR_APROBACION_PERSISTENTE")


def new_env(text):
    repl={"HFPOS_BACKEND_IMAGE":image("backend",NEW["backend"]),"HFPOS_WEB_IMAGE":image("web",NEW["web"]),
          "HFPOS_MIGRATIONS_IMAGE":image("migrations",NEW["migrations"]),"HFPOS_RELEASE_VERSION":TAG}
    for key,val in repl.items():
        text,n=re.subn(r"(?m)^"+key+r"=[^\r\n]*",lambda _:key+"="+val,text)
        need(n==1,"ENV_CLAVES_RELEASE_NO_UNICAS")
    return text


def check_delta(before,after):
    want=copy.deepcopy(before)
    for kind,digest in NEW.items():
        service=want["services"][kind]
        service["image"]=image(kind,digest)
        if "HFPOS_RELEASE_VERSION" in service.get("environment",{}):
            service["environment"]["HFPOS_RELEASE_VERSION"]=TAG
    need(want==after,"COMPOSE_DERIVA_NO_LIMITADA_A_RC11")


def private_compose(env, override, expected):
    c=compose(env)+["-f",str(override)]
    model=json.loads(cmd(c+["config","--format","json"],"COMPOSE_PRIVATE_NO_SOPORTADO"))
    want=copy.deepcopy(expected)
    ports=want["services"]["web"]["ports"]
    need(len(ports)==1 and ports[0]["target"]==8443 and str(ports[0]["published"])=="443"
         and not ports[0].get("host_ip"),"COMPOSE_WEB_PUBLICACION_DISTINTA")
    ports[0]["host_ip"]="127.0.0.1"
    need(model==want,"COMPOSE_PRIVATE_NO_AISLADO")
    return c


def firewall_spec(token):
    return (f"add table inet {TABLE}\n"
            f"add chain inet {TABLE} gate {{ type filter hook prerouting priority -310; policy accept; }}\n"
            f'add rule inet {TABLE} gate iifname != "lo" fib daddr type local tcp dport 443 counter drop comment "{token}"\n')


def nft_state():
    return json.loads(cmd(["nft","-j","list","ruleset"],"NFT_LEER_REGLAS"))["nftables"]


def absent():
    return not any(v.get("family")=="inet" and v.get("table",v.get("name"))==TABLE
                   for item in nft_state() for v in item.values() if isinstance(v,dict)
                   and ("table" in v or "name" in v))


def check_ingress_priority():
    items=nft_state()
    need(not any("flowtable" in r for r in items),"NFT_FLOW_OFFLOAD_NO_CONTEMPLADO")
    need(not any(r.get("chain",{}).get("hook")=="prerouting" and
                 (type(r["chain"].get("prio")) is not int or r["chain"]["prio"]<=-310)
                 for r in items),"NFT_PREROUTING_ANTERIOR_NO_CONTEMPLADO")


def verify_guard(token, require_hits=False):
    data=json.loads(cmd(["nft","-j","list","table","inet",TABLE],"NFT_TABLA_NO_VERIFICADA"))["nftables"]
    chain=[r["chain"] for r in data if "chain" in r]
    rules=[r["rule"] for r in data if "rule" in r]
    need(len(chain)==1 and chain[0].get("hook")=="prerouting" and chain[0].get("prio")==-310
         and len(rules)==1 and rules[0].get("comment")==token
         and any("drop" in e for e in rules[0].get("expr",[])),"NFT_GUARD_IDENTIDAD_INCIERTA")
    if require_hits:
        need(any(e.get("counter",{}).get("packets",0)>0 for e in rules[0]["expr"]),
             "NFT_NO_OBSERVO_TRAFICO_RECHAZADO")


def remove_guard(token):
    verify_guard(token)
    cmd(["nft","delete","table","inet",TABLE],"NFT_NO_SE_PUDO_REABRIR")
    need(absent(),"NFT_TABLA_AUN_PRESENTE")


def tls(paths):
    for path in paths:
        out=cmd(["curl","--disable","--noproxy","*","--silent","--show-error","--fail","--max-time","8",
                 "--max-filesize","1048576","--resolve","hfone.app:443:127.0.0.1",
                 "--write-out","\n%{http_code}",f"https://hfone.app{path}"],"TLS_ORIGEN_"+path.replace("/","_"),timeout=10)
        body,_,code=out.rpartition("\n")
        need(code=="200","TLS_ORIGEN_ESTADO")
        if path=="/":
            need(re.search(r'<link\b[^>]*\brel=["\x27]canonical["\x27][^>]*\bhref=["\x27]https://hfone\.app/?["\x27]',body,re.I)
                 is not None,"LANDING_CANONICAL_INESPERADO")


def wait_tls(paths):
    for attempt in range(8):
        try:
            tls(("/health/ready",))
            break
        except Stop:
            if attempt==7:
                raise
            time.sleep(2)
    tls(paths)


def sample(ef, drained=False):
    extra=r"""SELECT 'COBERTURA ' || (SELECT count(*) FROM public."Permissions" WHERE "Code"='CATALOG_PRODUCT_PRICES_BATCH' AND "IsActive")
  || ' ' || count(*) FILTER (WHERE NOT EXISTS (SELECT FROM public."RolePermissions" rp JOIN public."Permissions" p ON p."Id"=rp."PermissionId"
     WHERE rp."RoleId"=r."Id" AND p."Code"='CATALOG_PRODUCT_PRICES_BATCH' AND p."IsActive"))
FROM public."Roles" r WHERE r."Code"='ADMIN';
"""
    q=SQL.replace("ROLLBACK;",extra+"ROLLBACK;")
    lines=set(cmd(["docker","exec","-i",NAMES[2],"sh","-ceu",PG_ENTRY],"PG_LECTURA_NO_VERIFICADA",q,35).splitlines())
    must={"FIN",ef,"COLA 0 0 0","RBAC 0 0 0","FISCAL 0 0 0","MIGRATOR 1"}
    must.update("ACL "+t+" 1" for t in ("COMPANIES","PERMISSIONS","ROLES","ROLE_PERMISSIONS","EF_HISTORY"))
    if ef==EF1:
        must.add("COBERTURA 1 0")
    missing=must-lines
    for line in sorted(missing):
        print("BLOQUEO_SQL",line if not line.startswith("EF ") else "EF_HISTORIAL")
    need(not missing,"EF_COLA_RBAC_O_ACL")
    s=[line for line in lines if re.fullmatch(r"SESIONES \d+ \d+ \d+ \d+",line)]
    need(len(s)==1,"SESIONES_NO_VERIFICADAS")
    runtime,nonidle,migrator,others=map(int,s[0].split()[1:])
    need(not migrator and not others and (not drained or not runtime and not nonidle),"ESCRITORES_NO_DRENADOS")
    hashes=[line.split()[1] for line in lines if re.fullmatch(r"DB [a-f0-9]{64}",line)]
    need(len(hashes)==1,"PG_BASE_NO_VERIFICADA")
    return hashes[0]


def containers():
    return json.loads(cmd(["docker","inspect",*NAMES],"CONTENEDORES_IDENTIDAD"))


def daemon_policy(argv, settings):
    need(not any(x=="--allow-direct-routing" or
                 x.startswith("--allow-direct-routing=") and x!="--allow-direct-routing=false" for x in argv)
         and settings.get("allow-direct-routing",False) is False,
         "DOCKER_DIRECT_ROUTING_ACTIVO_O_INCIERTO")
    for flag in ("iptables","ip6tables"):
        need(not any(x.startswith("--"+flag+"=") and x!="--"+flag+"=true"
                     or x=="--"+flag and i+1<len(argv) and argv[i+1].lower() in ("false","0")
                     for i,x in enumerate(argv)) and settings.get(flag,True) is True,
             "DOCKER_FILTRADO_" + flag.upper() + "_NO_ACREDITADO")


def check_private_network(items):
    pid=cmd(["systemctl","show","-p","MainPID","--value","docker.service"],"DOCKER_DAEMON_NO_ACREDITADO").strip()
    need(re.fullmatch(r"[1-9][0-9]*",pid) is not None,"DOCKER_DAEMON_NO_ACREDITADO")
    process=Path("/proc")/pid
    need(process.stat().st_uid==0 and (process/"exe").resolve().name=="dockerd",
         "DOCKER_DAEMON_NO_ACREDITADO")
    argv=(process/"cmdline").read_bytes().decode().rstrip("\0").split("\0")
    paths=[x.split("=",1)[1] for x in argv if x.startswith("--config-file=")]
    paths.extend(argv[i+1] for i,x in enumerate(argv[:-1]) if x=="--config-file")
    need(len(paths)<=1,"DOCKER_CONFIG_DAEMON_INCIERTA")
    daemon=Path(paths[0] if paths else "/etc/docker/daemon.json")
    need(daemon.is_absolute() and daemon==daemon.resolve(),"DOCKER_CONFIG_DAEMON_INCIERTA")
    # Do not infer daemon state from a file that may have been edited or deleted since startup.
    # For the absent default file, the parent directory must predate dockerd too.
    pid_ticks=int((process/"stat").read_text().rsplit(")",1)[1].split()[19])
    boot=int(next(line[6:] for line in Path("/proc/stat").read_text().splitlines() if line.startswith("btime ")))
    began=boot+pid_ticks/os.sysconf("SC_CLK_TCK")
    found=daemon.exists()
    source=daemon if found else daemon.parent
    if not found:
        need(not paths and source.is_dir(),"DOCKER_CONFIG_DAEMON_NO_ACREDITADA")
    cfg_paths=(source,*source.parents)
    def config_stamp(path):
        s=path.lstat()
        need(s.st_uid==0 and not s.st_mode&0o022 and not stat.S_ISLNK(s.st_mode) and
             max(s.st_mtime,s.st_ctime)<=began-2,"DOCKER_CONFIG_CAMBIO_DESDE_ARRANQUE")
        return s.st_dev,s.st_ino,s.st_mtime_ns,s.st_ctime_ns
    stamps=[config_stamp(path) for path in cfg_paths]
    settings={}
    if found:
        settings=json.loads(daemon.read_text(encoding="utf-8"))
    need([config_stamp(path) for path in cfg_paths]==stamps and daemon.exists()==found and
         cmd(["systemctl","show","-p","MainPID","--value","docker.service"],"DOCKER_DAEMON_NO_ACREDITADO").strip()==pid,
         "DOCKER_CONFIG_O_PROCESO_CAMBIO")
    daemon_policy(argv,settings)
    web=list(items[1]["NetworkSettings"]["Networks"].values())
    joined=items[0]["NetworkSettings"]["Networks"].values()
    need(len(web)==1 and re.fullmatch(r"[a-f0-9]{64}",web[0]["NetworkID"])
         and web[0]["NetworkID"] in {n["NetworkID"] for n in joined},"DOCKER_WEB_RED_PRIVADA_NO_ACREDITADA")
    net=json.loads(cmd(["docker","network","inspect",web[0]["NetworkID"]],"DOCKER_WEB_RED_NO_VERIFICADA"))[0]
    need(net["Driver"]=="bridge" and net["Labels"].get("com.docker.compose.project")=="hf-one-pilot",
         "DOCKER_WEB_RED_NO_PERTENECE_AL_PROYECTO")
    options=net.get("Options",{})
    need(not options.get("com.docker.network.bridge.trusted_host_interfaces") and
         all(options.get("com.docker.network.bridge.gateway_mode_"+family,"nat")=="nat"
             for family in ("ipv4","ipv6")),"DOCKER_RED_DIRECT_ROUTING_ACTIVO")


def backup_bindings(cfg, items, envfile):
    def source(container,destination):
        bind=[x["Source"] for x in container["Mounts"] if x.get("Destination")==destination
              and x.get("Type")=="bind"]
        need(len(bind)==1,"RESPALDO_MONTAJE_NO_ACREDITADO")
        return Path(bind[0]).resolve(strict=True)
    need(Path(cfg["keys"]).resolve(strict=True)==source(items[0],"/keys") and
         Path(cfg["sources"]["tls"]).resolve(strict=True)==source(items[1],"/etc/nginx/tls") and
         Path(cfg["sources"]["config"]).resolve(strict=True) in envfile.resolve(strict=True).parents,
         "RESPALDO_FUENTES_NO_COINCIDEN_CON_CORTE")


def stopped(items):
    return len(items)==3 and all(x["State"]["Status"]=="exited" and x["State"]["ExitCode"]==0
           and x["State"]["OOMKilled"] is False for x in items[:2]) and items[2]["State"]["Running"] \
           and items[2]["State"]["Health"]["Status"]=="healthy"


def wait_backend():
    for _ in range(30):
        b=containers()[0]["State"]
        need(b["Status"]=="running" and not b["OOMKilled"],"BACKEND_NO_EJECUTANDO")
        if b.get("Health",{}).get("Status")=="healthy":
            return
        time.sleep(3)
    raise Stop("BACKEND_HEALTH_AGOTADO")


def borrow_and_capture(w, cfg, fd, state, expected):
    """Only replace the online precondition and lock context, not the capture/export implementation."""
    def owned():
        s,f=os.stat(LOCK,follow_symlinks=False),os.fstat(fd)
        need((s.st_dev,s.st_ino)==(f.st_dev,f.st_ino) and f.st_nlink==1,"LOCK_INODE_CAMBIO")
    @contextmanager
    def borrowed():
        owned()
        yield
        owned()
    def frozen(items):
        owned()
        need(stopped(items) and [x["Id"] for x in items]==expected,"BACKUP_COHERENCIA_CONTENEDORES")
        return [{key:x[key] for key in ("Id","Image","Config","Mounts")} for x in items]
    c=copy.deepcopy(cfg)
    c["state"]=str(state)
    c.pop("retention",None)
    w.maintenance_lock=borrowed
    w.stable_containers=frozen
    ident=uuid.uuid4().hex
    need(w.backup_attempt(c,w.Runner(),ident)==0,"BACKUP_NO_VERIFICADO")
    marker=Path(c["base"])/ident/"INTEGRITY_OFFSITE_VERIFIED.json"
    result=json.loads(w.secure_path(marker,private=True).read_bytes())
    need(result.get("recovery_id")==ident and result.get("completed_utc") and
         re.fullmatch(r"[a-f0-9]{64}",result.get("ciphertext_sha256","")) and result.get("ciphertext_bytes",0)>0,
         "BACKUP_MARCADOR_NO_VALIDO")
    print("RESPALDO_COHERENTE_OFFSITE_VERIFICADO",ident,flush=True)
    return ident


def load_worker():
    root=Path("/usr/local/lib/hf-one-backup-retention-ea633ce")
    entry=cmd(["systemctl","show","--value","--property=ExecStart","hf-one-backup.service"],
              "BACKUP_EXECSTART_NO_ACREDITADO")
    need(re.search(r"(?:^|\s)"+re.escape(str(root/"hourly-backup/backup.py"))+
                   r"\s+--config\s+/etc/hf-one-backup\.json(?:\s|;|$)",entry) is not None,
         "BACKUP_EXECSTART_DIVERGENTE")
    for name,digest in backup_hashes.items():
        target=root/name
        for part in (target,*target.parents):
            info=part.lstat()
            need(info.st_uid==0 and not info.st_mode & 0o022 and not stat.S_ISLNK(info.st_mode),
                 "BACKUP_ORIGEN_NO_CONFIABLE")
        need(target.is_file() and target.stat().st_size<=262144 and
             hashlib.sha256(target.read_bytes()).hexdigest()==digest,"BACKUP_WORKER_O_HELPER_NO_COINCIDE")
    spec=importlib.util.spec_from_file_location("rc11_backup",root/"hourly-backup/backup.py")
    w=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(w)
    return w


def check_migrator_auth(c):
    # The verified entrypoint also lists, but this read-only probe checks effective authentication before closing.
    script='set -eu; W=$(mktemp -d); cp -R /app/. "$W/"; cd "$W"; /opt/ef/dotnet-ef migrations list --configuration Release --no-build --json'
    text=cmd(c+["run","--rm","--no-deps","--pull","never","--entrypoint","sh",
                "-e","HFPOS_MIGRATION_APPROVED=NO","migrations","-ceu",script],"MIGRATOR_LOGIN_NO_PROBADO",timeout=150)
    for match in re.finditer(r"(?m)^\s*\[",text):
        try:
            items,_=json.JSONDecoder().raw_decode(text[match.start():].lstrip())
            if len(items)==64 and all(type(v.get("applied")) is bool for v in items):
                need([v.get("id") for v in items if not v["applied"]]==[MIG],"MIGRATOR_PENDIENTES_INESPERADAS")
                return
        except (ValueError,TypeError,AttributeError):
            continue
    raise Stop("MIGRATOR_AUTENTICACION_NO_ACREDITADA")


def ask(phrase,seconds=180):
    signal.alarm(seconds)
    try:
        need(input("Para continuar escribe "+phrase+": ")==phrase,"OPERADOR_NO_CONFIRMADO")
    finally:
        signal.alarm(0)


def interrupted(*_):
    raise Stop("SESION_INTERRUPCION_O_ESPERA_AGOTADA")


def main_reconcile(ssh_port):
    try:
        need(os.geteuid()==0 and ssh_port=="22" and STATE.is_dir() and not STATE.is_symlink(),
             "ESTADO_DE_CORTE_NO_ENCONTRADO")
        info=STATE.stat()
        need(info.st_uid==0 and info.st_mode & 0o077==0,"ESTADO_NO_PRIVADO")
        if absent():
            print("INGRESO sin tabla RC11; no asumir contencion")
        elif private_file(STATE/"guard-id"):
            token=(STATE/"guard-id").read_text(encoding="ascii")
            need(re.fullmatch(r"hf-one-rc11-[a-f0-9]{32}",token),"TABLA_PROPIEDAD_NO_ACREDITADA")
            verify_guard(token)
            print("INGRESO tabla propia presente")
        else:
            raise Stop("TABLA_PROPIEDAD_NO_ACREDITADA")
        web=cmd(["docker","port",NAMES[1]],"WEB_PUBLICACION_INCIERTA").splitlines()
        print("WEB", "privada" if set(web)=={"8443/tcp -> 127.0.0.1:443"} else
              "sin-publicacion-observada" if not web else "publicada")
        found=cmd(["docker","ps","-a","--filter","name=^/"+MIG_CONTAINER+"$","--format","{{.Names}}"],
                  "MIGRATOR_INVENTARIO_INCIERTO").splitlines()
        if found:
            need(found==[MIG_CONTAINER],"MIGRATOR_IDENTIDAD_INCIERTA")
            doc=json.loads(cmd(["docker","inspect",MIG_CONTAINER],"MIGRATOR_ESTADO_INCIERTO"))[0]
            need(doc["Config"]["Labels"].get("com.docker.compose.project")=="hf-one-pilot"
                 and doc["Config"]["Labels"].get("com.docker.compose.service")=="migrations"
                 and doc["Config"]["Image"]==image("migrations",NEW["migrations"]),
                 "MIGRATOR_IDENTIDAD_INCIERTA")
            s=doc["State"]
            need(not s["Running"] and s["Status"]=="exited","MIGRATOR_PUEDE_SEGUIR_EN_CURSO")
            print("MIGRATOR terminado salida="+str(int(s["ExitCode"])))
        else:
            need(not (STATE/"ef-attempted").exists(),"MIGRATOR_INTENTADO_SIN_CONTENEDOR_EVIDENCIA_INSUFICIENTE")
        read=set(cmd(["docker","exec","-i",NAMES[2],"sh","-ceu",PG_ENTRY],"PG_LECTURA_INCIERTA",SQL,35).splitlines())
        need("FIN" in read,"PG_HISTORIAL_NO_ACREDITADO")
        if EF1 in read:
            print("EF COMPROMETIDA: no repetir ni ejecutar Down")
        elif EF0 in read:
            print("EF PREVIA: no repetir sin reconciliar fase y efectos")
        else:
            raise Stop("PG_HISTORIAL_DIVERGENTE")
        print("RECONCILIACION SOLO LECTURA. No se reabrio ni se restauro nada.")
        return 0
    except Exception as e:
        print("STOP_RECONCILIACION",str(e) if isinstance(e,Stop) else "LECTURA_INCIERTA")
        return 4


def main_cutover(ssh_port):
    phase="SOLO_LECTURA"
    guard=False
    token="hf-one-rc11-"+uuid.uuid4().hex
    fd=None
    for s in (signal.SIGHUP,signal.SIGTERM,signal.SIGINT,signal.SIGALRM):
        signal.signal(s,interrupted)
    try:
        need(os.geteuid()==0 and ssh_port=="22","SSH_O_SUDO_NO_COINCIDE")
        engine=cmd(["docker","version","--format","{{.Server.Version}}"],"DOCKER_VERSION_NO_ACREDITADA").strip()
        need(re.match(r"^\d+\.",engine) and int(engine.split(".")[0])>=28,
             "DOCKER_LOOPBACK_AISLAMIENTO_NO_ACREDITADO")
        need(not STATE.exists() and not STATE.is_symlink() and absent(),"CORTE_ANTERIOR_NO_RECONCILIADO")
        need(private_file(ENV) and private_file(LOCK),"ENV_O_LOCK_NO_PRIVADO")
        old=ENV.read_text(encoding="utf-8")
        cand=new_env(old)
        old_cfg=rendered(ENV)
        before=containers()
        need(all(x["State"]["Running"] for x in before) and
             [x["Config"]["Image"] for x in before[:2]]==[image("backend",OLD[0]),image("web",OLD[1])],
             "PAR_BASE_DISTINTO")
        check_private_network(before)
        orig=[x["Id"] for x in before]
        for svc in NAMES[0],NAMES[2]:
            need(not cmd(["docker","port",svc],"PUERTO_NO_VERIFICADO").strip(),"PUERTO_DIRECTO_WRITER")
        maps=set(cmd(["docker","port",NAMES[1]],"PUBLICACION_WEB_NO_VERIFICADA").splitlines())
        need(maps=={"8443/tcp -> 0.0.0.0:443","8443/tcp -> [::]:443"},"PUBLICACION_DUALSTACK_CAMBIO")
        db_hash=sample(EF0)
        check_dsn(old_cfg,db_hash)
        tls(("/health/live","/health/ready","/","/login","/pos"))
        check_ingress_priority()
        cmd(["nft","-c","-f","-"],"NFT_REGLA_NO_COMPATIBLE",firewall_spec(token))
        for path in ("/etc/hf-one-backup.json",):
            need(private_file(Path(path)),"RESPALDO_CONFIG_NO_PRIVADA")
        worker=load_worker()
        cfg=worker.load_config(Path("/etc/hf-one-backup.json"))
        need(tuple(cfg["containers"])==NAMES,"RESPALDO_TOPOLOGIA_NO_COINCIDE")
        backup_bindings(cfg,before,ENV)
        print("GO_PREFLIGHT | Confirma que el observador externo muestra HTTPS IPv4/IPv6 y SSH accesibles.",flush=True)
        ask("AUTORIZO R4 RC11")
        fd=os.open(LOCK,os.O_RDWR|os.O_NOFOLLOW)
        try:
            fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:
            raise Stop("LOCK_OCUPADO") from None
        s,f=LOCK.lstat(),os.fstat(fd)
        need((s.st_dev,s.st_ino)==(f.st_dev,f.st_ino) and f.st_nlink==1,"LOCK_INODE_CAMBIO")
        os.umask(0o077)
        STATE.mkdir(mode=0o700)
        phase="PREPARACION_AUTORIZADA"
        (STATE/"old.env").write_text(old,encoding="utf-8")
        candidate=STATE/"rc11.env"
        candidate.write_text(cand,encoding="utf-8")
        candidate_cfg=rendered(candidate)
        check_delta(old_cfg,candidate_cfg)
        check_dsn(candidate_cfg,db_hash)
        for kind,d in NEW.items():
            cmd(["docker","pull","--platform","linux/amd64",image(kind,d)],"PULL_"+kind.upper(),timeout=240)
        c=compose(candidate)
        override=STATE/"web-private.yml"
        override.write_text('services:\n  web:\n    ports: !override\n      - "127.0.0.1:443:8443"\n',encoding="utf-8")
        private_c=private_compose(candidate,override,candidate_cfg)
        private_old=private_compose(ENV,override,old_cfg)
        state=STATE/"backup-state"
        state.mkdir(mode=0o700)
        check_migrator_auth(c)
        need([x["Id"] for x in containers()]==orig and absent(),"TOPOLOGIA_CAMBIO_ANTES_DE_CERRAR")
        need(sample(EF0)==db_hash,"PG_IDENTIDAD_CAMBIO_ANTES_DE_CERRAR")
        phase="CIERRE_ANTES_DE_DRENAR"
        (STATE/"guard-id").write_text(token,encoding="ascii")
        # Treat uncertain nft result as potentially applied.
        guard=True
        cmd(["nft","-f","-"],"NFT_CIERRE_NO_CONFIRMADO",firewall_spec(token))
        verify_guard(token)
        tls(("/health/ready",))
        print("CONFIRMA que la ventana externa muestra HTTPS bloqueado en ambas familias y SSH disponible.",flush=True)
        ask("CIERRE DUAL CONFIRMADO",100)
        verify_guard(token,require_hits=True)
        phase="DRENAJE"
        cmd(compose(ENV)+["stop","--timeout","45","web","backend"],"DRENAJE_PAR_FALLO",timeout=110)
        need(stopped(containers()),"DRENAJE_SALIDA_NO_LIMPIA")
        sample(EF0,drained=True)
        phase="RESPALDO"
        backup=borrow_and_capture(worker,cfg,fd,state,orig)
        (STATE/"backup-id").write_text(backup,encoding="ascii")
        verify_guard(token)
        sample(EF0,drained=True)
        phase="EF_RESULTADO_POTENCIALMENTE_COMPROMETIDO"
        (STATE/"ef-attempted").touch(exist_ok=False)
        outcome=None
        try:
            output=cmd(c+["run","--name",MIG_CONTAINER,"--no-deps","--pull","never","-e","HFPOS_MIGRATION_APPROVED=YES","migrations"],
                       "EF_ERROR_O_TIMEOUT_NO_REINTENTAR",timeout=240)
            if "MIGRATION PASS" not in output.splitlines():
                outcome="EF_MARCADOR_AUSENTE_NO_REINTENTAR"
        except Stop as e:
            outcome=str(e)
        # A broken transport or process exit never justifies repeating EF.
        try:
            sample(EF1,drained=True)
        except Stop:
            raise Stop("EF_RESULTADO_INCIERTO_NO_REINTENTAR") from None
        phase="EF_COMPROMETIDA"
        (STATE/"ef-confirmed").touch(exist_ok=False)
        need(outcome is None,outcome)
        for service in ("backend","web"):
            verify_guard(token)
            cmd((private_c if service=="web" else c)+["up","-d","--no-deps","--no-build","--pull","never","--force-recreate",service],
                "ARRANQUE_"+service.upper(),timeout=150)
            if service=="backend":
                wait_backend()
        now=containers()
        check_private_network(now)
        need(now[2]["Id"]==orig[2] and now[2]["Mounts"]==before[2]["Mounts"]
             and all(now[i]["Config"]["Image"]==image(s,NEW[s]) and now[i]["Mounts"]==before[i]["Mounts"]
                     for i,s in enumerate(("backend","web"))),"DIGEST_O_MONTAJES_POSTCORTE")
        cmd(c+["exec","-T","backend","bash","/app/health-probe.sh"],"API_HEALTH",timeout=25)
        sample(EF1)
        need(set(cmd(["docker","port",NAMES[1]],"WEB_PRIVADA_NO_VERIFICADA").splitlines())
             =={"8443/tcp -> 127.0.0.1:443"},"WEB_NO_ES_PRIVADA")
        tls(("/health/live","/health/ready","/","/login","/pos"))
        verify_guard(token)
        print("GO_POSTCORTE | TLS en origen, historial, cola y permisos verificados con el ingreso cerrado.",flush=True)
        print("Usa el navegador privado del monitor para reingresar y abrir ambos selectores sin confirmar lotes.")
        ask("VALIDADO FUNCIONAL Y ABRIR RC11",420)
        verify_guard(token)
        need(ENV.read_text(encoding="utf-8")==old,"ENV_CANONICO_CAMBIO")
        tmp_fd,tmp=tempfile.mkstemp(prefix=".rc11-",dir=ENV.parent)
        with os.fdopen(tmp_fd,"w",encoding="utf-8",newline="") as stream:
            stream.write(cand)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(tmp,ENV)
        phase="APERTURA_NO_REVERTIR_AUTOMATICAMENTE"
        cmd(c+["up","-d","--no-deps","--no-build","--pull","never","--force-recreate","web"],
            "PUBLICACION_WEB",timeout=150)
        wait_tls(("/","/login"))
        remove_guard(token)
        guard=False
        tls(("/health/ready","/","/login"))
        (STATE/"release-open").touch(exist_ok=False)
        print("RC11_ABIERTA | Comprueba el monitor TLS externo y el acceso de solo lectura; no hagas ventas de prueba.")
        return 0
    except (Exception,KeyboardInterrupt) as e:
        detail=str(e) if isinstance(e,Stop) else "ERROR_PRIVADO_NO_DIVULGADO"
        if phase=="CIERRE_ANTES_DE_DRENAR" and guard:
            try:
                need([x["Id"] for x in containers()]==orig and all(x["State"]["Running"] for x in containers()),
                     "PAR_ANTERIOR_NO_VERIFICADO_PARA_REABRIR")
                if not absent():
                    remove_guard(token)
                print("RETORNO_INGRESO_ANTERIOR_SIN_DRENAR")
            except Exception:
                print("NO_SE_CONFIRMÓ_REAPERTURA_NO_TOCAR_OTRAS_REGLAS")
        print(f"NO-GO FASE={phase} BLOQUEO={detail}")
        if phase in ("DRENAJE","RESPALDO") and guard and detail!="SESION_INTERRUPCION_O_ESPERA_AGOTADA":
            try:
                current=containers()
                need(stopped(current) and [x["Id"] for x in current]==orig
                     and not (STATE/"ef-attempted").exists(),"RETORNO_BASE_NO_VERIFICADA")
                verify_guard(token)
                sample(EF0,drained=True)
                print("EF no se intento. Retorno disponible con los contenedores anteriores, sin cambiar datos.")
                ask("VOLVER A RC8",100)
                cmd(compose(ENV)+["start","backend"],"RETORNO_BACKEND",timeout=90)
                wait_backend()
                cmd(private_old+["up","-d","--no-deps","--no-build","--pull","never","--force-recreate","web"],
                    "RETORNO_WEB_PRIVADA",timeout=90)
                wait_tls(("/","/login"))
                ask("REABRIR RC8",100)
                verify_guard(token)
                cmd(compose(ENV)+["up","-d","--no-deps","--no-build","--pull","never","--force-recreate","web"],
                    "RETORNO_WEB_PUBLICA",timeout=90)
                wait_tls(("/","/login"))
                remove_guard(token)
                guard=False
                print("RETORNO_RC8_VERIFICADO_ANTES_DE_EF")
                return 4
            except Exception:
                print("RETORNO_NO_CONFIRMADO; conservar contencion.")
        if phase in ("DRENAJE","RESPALDO","EF_RESULTADO_POTENCIALMENTE_COMPROMETIDO","EF_COMPROMETIDA"):
            print("CONTENCION: no repetir el corte ni EF. Conservar tabla, historial, respaldo y estado. Reconciliar por SSH.")
        if phase=="APERTURA_NO_REVERTIR_AUTOMATICAMENTE":
            print("APERTURA_INCIERTA: no revertir datos ni imágenes automáticamente; pueden existir efectos nuevos.")
        return 4
    finally:
        if fd is not None:
            os.close(fd)


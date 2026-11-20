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


def netfilter_rules(dump):
    rules={}
    table=None
    for line in dump.splitlines():
        if not line or line.startswith("#"):
            continue
        if line.startswith("*"):
            need(table is None,"DOCKER_REGLAS_FORMATO")
            table=line[1:]
        elif line=="COMMIT":
            need(table is not None,"DOCKER_REGLAS_FORMATO")
            table=None
        elif line.startswith(":"):
            need(table is not None,"DOCKER_REGLAS_FORMATO")
            key=(table,line[1:].split()[0])
            need(key not in rules,"DOCKER_REGLAS_FORMATO")
            rules[key]=[]
        elif line.startswith("-A "):
            args=shlex.split(line)
            need((table,args[1]) in rules,"DOCKER_REGLAS_FORMATO")
            rules[table,args[1]].append(args[2:])
        else:
            raise Stop("DOCKER_REGLAS_FORMATO")
    need(table is None,"DOCKER_REGLAS_FORMATO")
    return rules


def prove_endpoint_drop(rules, address, iface):
    # Moby 28's per-endpoint guard runs before Docker DNAT and does not
    # allow directly routed traffic from outside the endpoint's bridge.
    wanted=ipaddress.ip_network(address)
    for args in rules.get(("raw","PREROUTING"),[]):
        # An earlier unknown target could end traversal or rewrite the packet.
        need(args[-2:]==["-j","DROP"],"DOCKER_RAW_REGLA_PREVIA_NO_ACREDITADA")
        if (len(args)==7 and args[0]=="-d" and args[2:4]==["!","-i"]
            and ipaddress.ip_network(args[1])==wanted and args[4]==iface):
            return
    raise Stop("DOCKER_RAW_ENDPOINT_DROP_AUSENTE")


def prove_database_drop(rules, iface):
    # Accept only a positively proved drop independent of source address.
    # Do not assume the Docker Internal attribute or a subnet match prevents spoofed packets.
    def chain(name):
        return rules.get(("filter",name),[])
    forward=chain("FORWARD")
    if forward[:1]==[["-j","DOCKER-USER"]]:
        need(chain("DOCKER-USER") in ([],[["-j","RETURN"]]),"DOCKER_DB_USER_CADENA_INCIERTA")
        forward=forward[1:]
    want=["!","-i",iface,"-o",iface,"-j","DROP"]
    if forward[:1]==[want]:
        return
    need(forward[:1]==[["-j","DOCKER-FORWARD"]],"DOCKER_DB_FORWARD_NO_ACREDITADO")
    entry=chain("DOCKER-FORWARD")
    if entry[:1]==[["-j","DOCKER-CT"]]:
        need(all(len(a)==8 and a[0]=="-o" and a[1]!=iface and
                 a[2:]==["-m","conntrack","--ctstate","RELATED,ESTABLISHED","-j","ACCEPT"]
                 for a in chain("DOCKER-CT")),"DOCKER_DB_CT_NO_ACREDITADO")
        entry=entry[1:]
    need(any(entry[:1]==[["-j",name]] and want in chain(name)[:1]
             for name in ("DOCKER-INTERNAL","DOCKER-ISOLATION-STAGE-1")),
         "DOCKER_DB_DROP_EXTERNO_NO_ACREDITADO")


def bridge_shape(items, nets):
    endpoints=[list(i["NetworkSettings"]["Networks"].values()) for i in items]
    need([len(e) for e in endpoints]==[2,1,1],"DOCKER_RED_TOPOLOGIA_INCIERTA")
    web,db=endpoints[1][0]["NetworkID"],endpoints[2][0]["NetworkID"]
    need(web!=db and {e["NetworkID"] for e in endpoints[0]}=={web,db} and set(nets)=={web,db},
         "DOCKER_RED_TOPOLOGIA_INCIERTA")
    shapes={}
    for network in (web,db):
        n=nets[network]
        options=n.get("Options",{})
        need(n["Id"]==network and n["Driver"]=="bridge" and n["Internal"]==(network==db)
             and n["Labels"].get("com.docker.compose.project")=="hf-one-pilot"
             and not options.get("com.docker.network.bridge.trusted_host_interfaces")
             and all(options.get("com.docker.network.bridge.gateway_mode_"+family,"nat")=="nat"
                     for family in ("ipv4","ipv6")),"DOCKER_RED_INSEGURA_O_DISTINTA")
        iface=options.get("com.docker.network.bridge.name","br-"+network[:12])
        need(re.fullmatch(r"[A-Za-z0-9_.-]{1,15}",iface) is not None,"DOCKER_RED_INTERFAZ_INCIERTA")
        prefixes=[ipaddress.ip_network(p["Subnet"]) for p in n["IPAM"]["Config"]]
        found={4:[],6:[]}
        for endpoint in (e for es in endpoints for e in es if e["NetworkID"]==network):
            for version,key in ((4,"IPAddress"),(6,"GlobalIPv6Address")):
                text=endpoint.get(key)
                if version==6 and n["EnableIPv6"] is False:
                    need(not text,"DOCKER_RED_IPV6_INCONSISTENTE")
                    continue
                address=ipaddress.ip_address(text)
                need(address.version==version and not (address.is_loopback or address.is_unspecified
                     or address.is_multicast or address.is_link_local) and any(address in p for p in prefixes),
                     "DOCKER_RED_IP_INCIERTA")
                found[version].append(str(address))
        shapes[network]=(iface,found)
    need(shapes[web][0]!=shapes[db][0],"DOCKER_RED_INTERFAZ_INCIERTA")
    return shapes[web],shapes[db]


def prove_raw_hooks(objects):
    allowed={("ip","raw","PREROUTING",-300),("ip6","raw","PREROUTING",-300),
             ("inet",TABLE,"gate",-310)}
    hooks=set()
    for item in objects:
        need("flowtable" not in item,"DOCKER_NFT_FLOW_OFFLOAD_INCIERTO")
        c=item.get("chain",{})
        if c.get("hook") in ("ingress","prerouting"):
            need(type(c.get("prio")) is int,"DOCKER_NFT_ORDEN_INCIERTO")
            key=(c.get("family"),c.get("table"),c.get("name"),c.get("prio"))
            if c["hook"]=="ingress" or c["prio"]<=-300:
                need(key in allowed and c.get("type")=="filter" and key not in hooks,
                     "DOCKER_NFT_ORDEN_INCIERTO")
                hooks.add(key)
            else:
                family,table,name,priority=key
                need(family in ("ip","ip6") and name=="PREROUTING" and
                     (table,c.get("type"),priority) in (("nat","nat",-100),("mangle","filter",-150)),
                     "DOCKER_NFT_PREROUTING_NO_ACREDITADO")
    return hooks


def prove_private_publication(v, addresses, sockets):
    variants=(["-d","127.0.0.1/32","!","-i","lo","-p","tcp","-m","tcp","--dport","443","-j","DROP"],
              ["-d","127.0.0.1/32","!","-i","lo","-p","tcp","--dport","443","-j","DROP"])
    early=v[4].get(("raw","PREROUTING"),[])
    for rule in early:
        need(rule[-2:]==["-j","DROP"],"DOCKER_RAW_REGLA_PREVIA_NO_ACREDITADA")
        if rule in variants:
            break
    else:
        raise Stop("DOCKER_LOOPBACK_RAW_DROP_AUSENTE")
    need(all(len(s.split())>=5 and s.split()[3]=="127.0.0.1:443" for s in sockets.splitlines()),
         "DOCKER_WEB_SOCKET_PUBLICO")
    for family, rules in v.items():
        for (table,_), chain in rules.items():
            if table!="nat":
                continue
            for r in chain:
                if any(target in r for target in ("DNAT","REDIRECT","NETMAP")):
                    need(family==4 and "-d" in r and r[r.index("-d")+1]=="127.0.0.1/32"
                         and "--dport" in r and r[r.index("--dport")+1]=="443" and
                         "--to-destination" in r and
                         r[r.index("--to-destination")+1] in {a+":8443" for a in addresses},
                         "DOCKER_NAT_PRIVADO_NO_ACREDITADO")


def active_docker_identity():
    pid=cmd(["systemctl","show","-p","MainPID","--value","docker.service"],"DOCKER_DAEMON_NO_ACREDITADO").strip()
    need(re.fullmatch(r"[1-9][0-9]*",pid) is not None,"DOCKER_DAEMON_NO_ACREDITADO")
    process=Path("/proc")/pid
    need(process.stat().st_uid==0 and (process/"exe").resolve().name=="dockerd"
         and (process/"ns/net").stat().st_ino==Path("/proc/self/ns/net").stat().st_ino,
         "DOCKER_DAEMON_NO_ACREDITADO")
    argv=(process/"cmdline").read_bytes().decode().rstrip("\0").split("\0")
    return (pid,(process/"stat").read_text().rsplit(")",1)[1].split()[19],argv)


def prove_bridge_members(host, peers):
    """Require every live bridge port to be a veth paired with a known Docker endpoint."""
    found=set()
    for port in host:
        idx,peer=port.get("ifindex"),port.get("link_index")
        need(port.get("linkinfo",{}).get("info_kind")=="veth" and
             type(idx) is int and type(peer) is int and idx>0 and peer>0 and idx not in found and
             peers.get(idx)==peer,"DOCKER_BRIDGE_MIEMBRO_NO_IDENTIFICADO")
        found.add(idx)
    need(found==set(peers),"DOCKER_BRIDGE_MIEMBROS_INCOMPLETOS")


def bridge_members(items, nets):
    members={}
    for net in nets.values():
        network=net["Id"]
        opts=net.get("Options",{})
        iface=opts.get("com.docker.network.bridge.name","br-"+network[:12])
        host=json.loads(cmd(["ip","-j","-d","link","show","master",iface],"DOCKER_BRIDGE_MIEMBROS_INCIERTOS"))
        peers={}
        for item in items:
            links=item["NetworkSettings"]["Networks"].values()
            for endpoint in (l for l in links if l["NetworkID"]==network):
                address=ipaddress.ip_address(endpoint["IPAddress"])
                pid=item["State"]["Pid"]
                need(type(pid) is int and pid>1,"DOCKER_ENDPOINT_PID_INCIERTO")
                inside=json.loads(cmd(["nsenter","-t",str(pid),"-n","ip","-j","-d","addr","show"],
                                      "DOCKER_ENDPOINT_PEER_INCIERTO"))
                matched=[p for p in inside if p.get("linkinfo",{}).get("info_kind")=="veth" and
                         any(a.get("family")=="inet" and a.get("local")==str(address)
                             for a in p.get("addr_info",[]))]
                need(len(matched)==1,"DOCKER_ENDPOINT_PEER_INCIERTO")
                p=matched[0]
                idx,peer=p.get("link_index"),p.get("ifindex")
                need(type(idx) is int and type(peer) is int and idx>0 and peer>0 and idx not in peers,
                     "DOCKER_ENDPOINT_PEER_INCIERTO")
                peers[idx]=peer
        prove_bridge_members(host,peers)
        members[network]=peers
    return members


def check_private_network(items, private_web=False):
    def networks():
        return {n:json.loads(cmd(["docker","network","inspect",n],"DOCKER_RED_NO_VERIFICADA"))[0] for n in ids}
    def snapshot():
        return {i:netfilter_rules(cmd([name],"DOCKER_REGLAS_KERNEL_NO_DISPONIBLES"))
                for i,name in ((4,"iptables-save"),(6,"ip6tables-save"))}
    def topology(inspects):
        return [(i["Id"],i["State"]["Pid"],i["HostConfig"]["NetworkMode"],i["NetworkSettings"]["Networks"]) for i in inspects]
    identity=active_docker_identity()
    daemon_policy(identity[2],{})
    ids={e["NetworkID"] for item in items for e in item["NetworkSettings"]["Networks"].values()}
    need(len(ids)==2 and all(re.fullmatch(r"[a-f0-9]{64}",i) for i in ids),"DOCKER_RED_TOPOLOGIA_INCIERTA")
    net=networks()
    (web_iface,web_ips),(db_iface,db_ips)=bridge_shape(items,net)
    for iface in (web_iface,db_iface):
        links=json.loads(cmd(["ip","-j","-d","link","show","dev",iface],"DOCKER_RED_INTERFAZ_INCIERTA"))
        need(len(links)==1 and links[0].get("ifname")==iface and
             links[0].get("linkinfo",{}).get("info_kind")=="bridge","DOCKER_RED_INTERFAZ_INCIERTA")
    members=bridge_members(items,net)
    v=snapshot()
    before_hooks=prove_raw_hooks(nft_state())
    for family,tool in ((4,"iptables"),(6,"ip6tables")):
        version=cmd([tool,"--version"],"DOCKER_BACKEND_FIREWALL_INCIERTO")
        need("(nf_tables)" in version,"DOCKER_BACKEND_FIREWALL_INCIERTO")
        if web_ips[family]:
            need(("ip" if family==4 else "ip6","raw","PREROUTING",-300) in before_hooks,
                 "DOCKER_RAW_HOOK_NO_ACREDITADO")
            for address in web_ips[family]:
                prove_endpoint_drop(v[family],address,web_iface)
        if db_ips[family]:
            prove_database_drop(v[family],db_iface)
    if private_web:
        need(set(cmd(["docker","port",NAMES[1]],"WEB_PRIVADA_NO_VERIFICADA").splitlines())
             =={"8443/tcp -> 127.0.0.1:443"},"WEB_NO_ES_PRIVADA")
        prove_private_publication(v,web_ips[4],
            cmd(["ss","-H","-ltn","sport = :443"],"DOCKER_WEB_SOCKET_INCIERTO"))
    need(identity==active_docker_identity() and topology(items)==topology(containers()) and net==networks()
         and members==bridge_members(items,net) and v==snapshot() and before_hooks==prove_raw_hooks(nft_state()),
         "DOCKER_RED_O_REGLAS_CAMBIARON")
    print("DOCKER_FILTRADO_DIRECTO_VERIFICADO",flush=True)
    return identity


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


def main_verify_network(ssh_port):
    try:
        need(os.geteuid()==0 and ssh_port=="22","SSH_O_SUDO_NO_COINCIDE")
        items=containers()
        for i in (0,2):
            need(not cmd(["docker","port",NAMES[i]],"PUERTO_NO_VERIFICADO").strip(),
                 "PUERTO_DIRECTO_WRITER")
        check_private_network(items)
        return 0
    except Exception as e:
        print("NO-GO FILTRADO_DIRECTO",str(e) if isinstance(e,Stop) else "EVIDENCIA_INCOMPLETA")
        return 4


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
        initial_docker=check_private_network(before)
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
        need(check_private_network(before)==initial_docker,"DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
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
        need(check_private_network(now,private_web=True)==initial_docker,"DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
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
        need(check_private_network(containers(),private_web=True)==initial_docker,"DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
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
                need(active_docker_identity()==initial_docker,"DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
                cmd(compose(ENV)+["start","backend"],"RETORNO_BACKEND",timeout=90)
                wait_backend()
                cmd(private_old+["up","-d","--no-deps","--no-build","--pull","never","--force-recreate","web"],
                    "RETORNO_WEB_PRIVADA",timeout=90)
                need(check_private_network(containers(),private_web=True)==initial_docker,
                     "DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
                wait_tls(("/","/login"))
                ask("REABRIR RC8",100)
                verify_guard(token)
                need(check_private_network(containers(),private_web=True)==initial_docker,
                     "DOCKER_DAEMON_CAMBIO_DURANTE_CORTE")
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


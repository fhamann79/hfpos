"""DEV-532 RC11 inventory only. No firewall edits, backups, or migrations."""
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import stat
import subprocess
import sys

PG = "hf-one-pilot-postgres-1"
WEB = "hf-one-pilot-web-1"
BASE = Path("/home/hfadmin/hfpos-pilot-src")
EXPECTED_EF = "EF 63 94d1cfd4222cf026435da0525af0eee4c4e00b50fd0b08a014240f673736aa43"
BLOCKS = set()
SQL = r"""BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL search_path = pg_catalog;
SET LOCAL statement_timeout = '7s';
SET LOCAL lock_timeout = '1s';
SELECT (current_database() = :'expected_db' AND current_user = :'expected_admin'
  AND current_user = session_user AND current_user NOT IN ('hf_one_runtime','hf_one_migrator')
  AND current_setting('transaction_read_only') = 'on'
  AND current_setting('server_version_num')::int BETWEEN 160000 AND 169999)::text AS identity_ok \gset
\if :identity_ok
\else
\echo PG_IDENTIDAD_NO_VERIFICADA
\quit
\endif
SELECT 'DB ' || encode(sha256(convert_to(current_database(),'UTF8')), 'hex');
SELECT 'EF ' || count(*) || ' ' || encode(sha256(convert_to(
  coalesce(string_agg("MigrationId", chr(10) ORDER BY "MigrationId" COLLATE "C") || chr(10), ''), 'UTF8')), 'hex')
FROM public."__EFMigrationsHistory";
SELECT 'COLA ' || count(*) FILTER (WHERE "AutomaticRequested" AND "State" IN (0,1,2,3))
  || ' ' || count(*) FILTER (WHERE "Phase" IN (2,3,4))
  || ' ' || count(*) FILTER (WHERE "LeaseToken" IS NOT NULL OR "LeaseExpiresAt" IS NOT NULL)
FROM public."ElectronicIssuingJobs";
SELECT 'RBAC ' || ((SELECT count(*) FROM public."Permissions" WHERE "Code"='CATALOG_PRODUCT_PRICES_BATCH') > 1
  OR EXISTS (SELECT FROM public."Permissions" WHERE "Code"='CATALOG_PRODUCT_PRICES_BATCH' AND "IsActive" IS DISTINCT FROM true))::int
  || ' ' || (SELECT count(*) FROM public."RolePermissions" rp
    JOIN public."Permissions" p ON p."Id"=rp."PermissionId" LEFT JOIN public."Roles" r ON r."Id"=rp."RoleId"
    WHERE p."Code"='CATALOG_PRODUCT_PRICES_BATCH' AND r."Code" IS DISTINCT FROM 'ADMIN')
  || ' ' || ((EXISTS (SELECT FROM public."Roles" r LEFT JOIN public."Companies" c ON c."Id"=r."CompanyId"
       WHERE r."Code"='ADMIN' AND (c."Id" IS NULL OR coalesce(r."AuthorizationVersion",0)<=0
          OR (r."AuthorizationVersion"=2147483647 AND NOT EXISTS (SELECT FROM public."RolePermissions" rp
            JOIN public."Permissions" p ON p."Id"=rp."PermissionId"
            WHERE rp."RoleId"=r."Id" AND p."Code"='CATALOG_PRODUCT_PRICES_BATCH'))))
       OR EXISTS (SELECT FROM public."Roles" WHERE "Code"='ADMIN' GROUP BY "CompanyId" HAVING count(*)>1))::int);
SELECT 'FISCAL ' || (SELECT count(*) FROM unnest(ARRAY['CATALOG_PRODUCTS_READ','CATALOG_PRODUCTS_WRITE','FISCAL_SETTINGS_READ','FISCAL_SETTINGS_WRITE']) e(code)
  WHERE NOT EXISTS (SELECT FROM public."Permissions" p WHERE p."Code"=e.code AND p."IsActive"))
  || ' ' || (SELECT count(*) FROM public."Companies" c WHERE c."IsActive" AND NOT EXISTS
      (SELECT FROM public."Roles" r WHERE r."CompanyId"=c."Id" AND r."Code"='ADMIN' AND r."IsActive"))
  || ' ' || (SELECT count(*) FROM public."Roles" r JOIN public."Companies" c ON c."Id"=r."CompanyId"
    WHERE c."IsActive" AND r."Code"='ADMIN' AND EXISTS
      (SELECT FROM unnest(ARRAY['CATALOG_PRODUCTS_READ','CATALOG_PRODUCTS_WRITE','FISCAL_SETTINGS_READ','FISCAL_SETTINGS_WRITE']) e(code)
        WHERE NOT EXISTS (SELECT FROM public."RolePermissions" rp JOIN public."Permissions" p ON p."Id"=rp."PermissionId"
          WHERE rp."RoleId"=r."Id" AND p."Code"=e.code AND p."IsActive")));
SELECT 'MIGRATOR ' || count(*) FILTER (WHERE rolcanlogin AND NOT rolsuper
  AND has_database_privilege(oid,current_database(),'CONNECT') AND has_schema_privilege(oid,'public','USAGE'))
  FROM pg_roles WHERE rolname='hf_one_migrator';
SELECT 'ACL ' || t.code || ' ' || coalesce((SELECT (has_table_privilege(r.oid,t.rel,'SELECT')
  AND has_table_privilege(r.oid,t.rel,'UPDATE,DELETE,TRUNCATE')
  AND (NOT t.ins OR has_table_privilege(r.oid,t.rel,'INSERT'))
  AND (NOT t.upd OR has_column_privilege(r.oid,t.rel,'AuthorizationVersion','UPDATE'))
  AND (NOT t.seq OR has_sequence_privilege(r.oid,pg_get_serial_sequence(t.rel,'Id'),'USAGE,UPDATE'))
  AND NOT (c.relrowsecurity AND NOT r.rolbypassrls AND NOT (NOT c.relforcerowsecurity AND pg_has_role(r.oid,c.relowner,'USAGE'))))::int
  FROM pg_roles r JOIN pg_class c ON c.oid=t.rel::regclass WHERE r.rolname='hf_one_migrator'),0)
FROM (VALUES ('COMPANIES','public."Companies"',false,false,false),
  ('PERMISSIONS','public."Permissions"',true,false,true),('ROLES','public."Roles"',false,true,false),
  ('ROLE_PERMISSIONS','public."RolePermissions"',true,false,false),
  ('EF_HISTORY','public."__EFMigrationsHistory"',true,false,false)) t(code,rel,ins,upd,seq);
SELECT 'SESIONES ' || count(*) FILTER (WHERE usename='hf_one_runtime')
  || ' ' || count(*) FILTER (WHERE usename='hf_one_runtime' AND state IS DISTINCT FROM 'idle')
  || ' ' || count(*) FILTER (WHERE usename='hf_one_migrator')
  || ' ' || count(*) FILTER (WHERE usename NOT IN ('hf_one_runtime','hf_one_migrator'))
FROM pg_stat_activity WHERE datname=current_database() AND backend_type='client backend' AND pid<>pg_backend_pid();
ROLLBACK;
\echo FIN
"""
# The shell only selects the existing identity from inside the known PostgreSQL container.
PG_ENTRY = r"""export PGOPTIONS='-c default_transaction_read_only=on' PGCONNECT_TIMEOUT=5
exec psql -X -w -qAt -v ON_ERROR_STOP=1 -h /var/run/postgresql -p 5432 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v expected_db="$POSTGRES_DB" -v expected_admin="$POSTGRES_USER"
"""


def run(args, payload=None, seconds=12):
    try:
        if args[0] == "docker":
            args = ["/usr/bin/docker", "--host", "unix:///run/docker.sock", *args[1:]]
        p = subprocess.run(args, input=payload or "", text=True, encoding="utf-8",
                           errors="replace", capture_output=True, timeout=seconds,
                           env={"PATH": "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",
                                "HOME": "/root", "LC_ALL": "C", "PYTHONDONTWRITEBYTECODE": "1"})
        return p.stdout if p.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def block(code):
    BLOCKS.add(code)


def safe_label(value):
    if re.fullmatch(r"(?:INPUT|FORWARD|OUTPUT|PREROUTING|POSTROUTING|DOCKER(?:-[A-Z0-9_-]+)?|input|forward|output|prerouting|postrouting|filter|nat|raw|mangle|security|docker-bridges)", value):
        return value
    return "custom-" + hashlib.sha256(value.encode()).hexdigest()[:9]


def binding(value):
    host, sep, number = value.rpartition(":")
    if not sep or not number.isdecimal() or not 0 < int(number) <= 65535:
        return None
    host = host.strip("[]").split("%", 1)[0]
    if host == "*":
        return "v4/v6", "wildcard", int(number)
    try:
        ip = ipaddress.ip_address(host)
    except ValueError:
        return None
    scope = "wildcard" if ip.is_unspecified else "loopback" if ip.is_loopback else "nonloopback"
    return "v" + str(ip.version), scope, int(number)


def inventory_ingress(ssh_port):
    ports = {ssh_port, 80, 443, 8443}
    docker = run(["docker", "port", WEB])
    maps = []
    for line in (docker or "").splitlines():
        service, _, host = line.partition(" -> ")
        item = binding(host)
        if item and re.fullmatch(r"\d+/(?:tcp|udp)", service):
            maps.append((service, *item))
    for service, family, scope, port in sorted(set(maps)):
        ports.add(port)
        print(f"WEB_PUBLICADO {family} {scope} {port} -> {service}")
    if not maps:
        block("PUBLICACION_WEB_NO_IDENTIFICADA")
    for service in ("backend", "postgres"):
        published = run(["docker", "port", "hf-one-pilot-" + service + "-1"])
        print(f"PORT_DIRECTO {service} " + ("no-acreditado" if published is None else "si" if published.strip() else "no"))
        if published is None or published.strip():
            block("PORT_DIRECTO_" + service.upper() + "_NO_DESCARTADO")
    for family in ("4", "6"):
        routes = run(["ip", "-j", "-" + family, "route", "show", "default"])
        try:
            gateways = len(json.loads(routes)) if routes is not None else "no-verificado"
        except (ValueError, TypeError):
            gateways = "no-verificado"
        print(f"RUTA_DEFAULT v{family} cantidad={gateways}")
        sockets = run(["ss", "-H", "-" + family, "-lntup"])
        if sockets is None:
            block("SOCKETS_V" + family + "_NO_ACREDITADOS")
        else:
            for line in sockets.splitlines():
                fields = line.split()
                entry = binding(fields[4]) if len(fields) >= 6 else None
                if entry:
                    names = set(re.findall(r'\("([A-Za-z0-9_.-]+)"', line))
                    known = sorted(names & {"sshd","sshd-session","docker-proxy","nginx","caddy","apache2","httpd","traefik","haproxy","cloudflared","tailscaled","systemd"})
                    process = ",".join(known) or "otro"
                    print(f"SOCKET v{family} {fields[0] if fields[0] in ('tcp','udp') else 'otro'} {entry[1]} {entry[2]} {process}")
    if ssh_port in {x[3] for x in maps}:
        block("SSH_COMPARTE_PUERTO_WEB")
    print(f"SSH_SESION puerto_servidor={ssh_port}")
    units = ("nftables", "firewalld", "ufw", "nginx", "caddy", "apache2", "haproxy", "cloudflared", "tailscaled")
    active = [name for name in units if (run(["systemctl", "is-active", name + ".service"]) or "").strip() == "active"]
    print("SERVICIOS_ACTIVOS " + (",".join(active) or "ninguno-detectado"))
    scan_firewall(ports)
    # Host observation cannot certify Contabo/CDN reachability or a reversible change.
    block("CIERRE_Y_REAPERTURA_V4_V6_CON_SSH_AUN_NO_PROBADOS")
    block("RUTA_TLS_DE_OPERADOR_Y_CONTROL_EXTERNO_AUN_NO_PROBADOS")


def scan_firewall(ports):
    for family, command in (("v4","iptables-save"),("v6","ip6tables-save")):
        data = run([command])
        if data is None:
            print(f"XT {family} no-disponible")
            continue
        table = ""
        rules = {}
        relevant = {}
        for line in data.splitlines():
            if line.startswith("*"):
                table = safe_label(line[1:])
            elif line.startswith(":"):
                name, policy, *_ = line[1:].split()
                if policy in ("ACCEPT","DROP","-"):
                    rules[(table,name)] = [policy, 0]
            elif line.startswith("-A "):
                args = shlex.split(line)
                name = args[1]
                key = (table, name)
                if key in rules:
                    rules[key][1] += 1
                target = next((args[i+1] for i,a in enumerate(args[:-1]) if a in ("-j","-g")), "")
                labels = {"ACCEPT","DROP","REJECT","DNAT","RETURN","REDIRECT"}
                if target not in labels:
                    target = safe_label(target) if target else "sin-salto"
                values = {int(v) for a in args for v in a.split(",") if v.isdecimal() and int(v) in ports}
                if name in ("INPUT","FORWARD","PREROUTING","DOCKER","DOCKER-USER") or values:
                    relevant.setdefault(key, []).append((rules.get(key, ["-",0])[1], target, sorted(values)))
        for (table,name),(policy,count) in sorted(rules.items()):
            if count or policy != "-":
                print(f"XT {family} {table}/{safe_label(name)} policy={policy} reglas={count}")
        for (table,name),items in sorted(relevant.items()):
            for order, target, values in items[:16]:
                print(f"XT_REG {family} {table}/{safe_label(name)} n={order} accion={target} puertos_relevantes={','.join(map(str,values)) or '-'}")
            if len(items)>16:
                block("XT_RESUMEN_LIMITADO_" + family.upper())
    data = run(["nft","-j","list","ruleset"])
    if data is None:
        print("NFT no-disponible")
        return
    chains = 0
    objects = json.loads(data).get("nftables", [])
    nft_chains = set()
    for obj in objects:
        c = obj.get("chain", {})
        if c.get("hook") not in ("input","forward","prerouting","ingress"):
            continue
        family = c.get("family")
        if family in ("inet","ip","ip6","netdev","bridge"):
            priority = c.get("prio")
            policy = c.get("policy")
            print(f"NFT {family} {safe_label(c['table'])}/{safe_label(c['name'])} hook={c['hook']} prioridad={priority if type(priority) is int else 'otra'} policy={policy if policy in ('accept','drop') else 'otra'}")
            nft_chains.add((family, c["table"], c["name"]))
            chains += 1
    counts = {}
    for obj in objects:
        rule = obj.get("rule", {})
        key = (rule.get("family"),rule.get("table"),rule.get("chain"))
        if key not in nft_chains:
            continue
        counts[key] = counts.get(key, 0) + 1
        actions = {k for expr in rule.get("expr",[]) for k in expr
                   if k in ("accept","drop","reject","dnat","redirect","jump","goto","return")}
        if actions and counts[key] <= 16:
            print(f"NFT_REG {key[0]} {safe_label(key[1])}/{safe_label(key[2])} n={counts[key]} accion={','.join(sorted(actions))}")
    if any(n>16 for n in counts.values()):
        block("NFT_RESUMEN_LIMITADO")
    print(f"NFT cadenas_ingreso={chains}")


def postgres():
    output = run(["docker","exec","-i",PG,"sh","-ceu",PG_ENTRY], SQL, 35)
    lines = set((output or "").splitlines())
    if "FIN" not in lines:
        block("PG_IDENTIDAD_DISTINTA" if "PG_IDENTIDAD_NO_VERIFICADA" in lines else "PG_LECTURA_ADMIN_EXISTENTE_NO_VERIFICADA")
        return None
    expected = {EXPECTED_EF: "EF_HISTORIAL_PRE_RC11", "COLA 0 0 0": "SRI_COLA_O_LEASES",
                "RBAC 0 0 0": "RBAC_INCONSISTENTE", "FISCAL 0 0 0": "FISCAL_COBERTURA_INCOMPLETA",
                "MIGRATOR 1": "MIGRATOR_ROL_NO_APTO"}
    expected.update({"ACL "+name+" 1": "ACL_MIGRATOR_"+name for name in
                     ("COMPANIES","PERMISSIONS","ROLES","ROLE_PERMISSIONS","EF_HISTORY")})
    for line, failure in expected.items():
        if line not in lines:
            block(failure)
    for line in sorted(lines):
        if re.fullmatch(r"(?:COLA|RBAC|FISCAL) \d+ \d+ \d+|SESIONES \d+ \d+ \d+ \d+", line):
            print(line)
            if line.startswith("SESIONES ") and tuple(map(int,line.split()[3:])) != (0,0):
                block("MIGRATOR_U_OTRAS_SESIONES_DB_PRESENTES")
    print("PG_LECTURA_COMPLETA admin_solo_lectura=si migrator_login_probado=no")
    print("PG_SIGNIFICADO SESIONES=runtime,runtime-no-idle,migrator,otras; COLA=automaticos,fases-inciertas,leases; RBAC=catalogo,ajenos,admin; FISCAL=catalogo,empresas,roles")
    identity = [line[3:] for line in lines if re.fullmatch(r"DB [0-9a-f]{64}",line)]
    if len(identity) != 1:
        block("PG_BASE_EF_NO_ACREDITADA")
        return None
    return identity[0]


def dsn_target(value):
    if not isinstance(value, str) or len(value)>32768:
        return {}
    # Only parse the subset with unambiguous Npgsql key/value boundaries; never display the DSN.
    matcher = re.compile(r'''\s*([^;=]+?)\s*=\s*(?:"((?:""|[^"])*)"|'((?:''|[^'])*)'|([^;'"]*?))\s*(?:;|$)''')
    aliases = {"server":"host","user name":"username","user id":"username","uid":"username","userid":"username","user":"username","initial catalog":"database"}
    result = {}
    pos = 0
    while value[pos:].strip(" ;\t"):
        m = matcher.match(value,pos)
        if not m:
            return {}
        key=m[1].strip().lower()
        key=aliases.get(key,key)
        if key in ("host","username","port","database"):
            v=next(v for v in m.groups()[1:] if v is not None)
            if key in result or not re.fullmatch(r"[A-Za-z0-9_.-]+",v):
                return {}
            result[key]=v
        pos=m.end()
    return result


def migration_inventory(db_hash):
    args=["docker","compose","-p","hf-one-pilot","--env-file","/etc/hf-one/pilot/config/pilot.env",
          "-f",str(BASE/"deploy/compose/compose.production.example.yml"),
          "-f",str(BASE/"deploy/compose/compose.production.postgres.example.yml"),
          "--profile","migration","config","--format","json"]
    value=run(args,seconds=20)
    if not value or not db_hash:
        block("MIGRATOR_VINCULO_COMPOSE_DB_NO_ACREDITADO")
        return
    services=json.loads(value)["services"]
    for service, role in (("backend","hf_one_runtime"),("migrations","hf_one_migrator")):
        env=services[service]["environment"]
        d=dsn_target(env.get("ConnectionStrings__DefaultConnection"))
        if (d.get("username")!=role or d.get("host") not in ("postgres",PG)
            or d.get("port","5432")!="5432"
            or hashlib.sha256(d.get("database","").encode()).hexdigest()!=db_hash
            or (service=="migrations" and str(env.get("HFPOS_MIGRATION_APPROVED","")).upper()!="NO")):
            block("MIGRATOR_VINCULO_" + service.upper() + "_INCONSISTENTE")
            print(f"COMPOSE_VINCULO {service} no-acreditado")
        else:
            print(f"COMPOSE_VINCULO {service} rol_y_db_coinciden=si")


def private_file(path):
    try:
        f = path.lstat()
        return stat.S_ISREG(f.st_mode) and f.st_uid == 0 and (f.st_mode & 0o077) == 0 and f.st_nlink == 1
    except OSError:
        return False


def backup_inventory():
    lock_path = Path("/run/lock/hf-one-manual-backup.lock")
    lock = private_file(lock_path)
    tools = all(shutil.which(name) for name in ("flock","age","rclone"))
    helpers = all((BASE / "scripts/ops" / name).is_file() for name in
                  ("postgres-backup.sh","verify-postgres-backup.sh","keyring-backup.sh"))
    print(f"RESPALDO lock_privado={int(lock)} herramientas={int(tools)} helpers_repo={int(helpers)}")
    if not lock or not tools or not helpers:
        block("RESPALDO_PRERREQUISITOS_INCOMPLETOS")
    cfg = Path("/etc/hf-one-backup.json")
    if private_file(cfg):
        c = json.loads(cfg.read_text(encoding="utf-8"))
        material = c.get("postgres") == PG and c.get("maintenance_lock")==str(lock_path) and all(isinstance(c.get(name),str)
            and Path(c[name]).is_absolute() and private_file(Path(c[name])) for name in ("recipient","rclone_config"))
        print(f"RESPALDO config_existente_mismo_pg_y_adaptadores_privados={int(material)}")
        if not material:
            block("RESPALDO_CONFIG_O_ADAPTADORES_NO_VERIFICADOS")
        sources = c.get("sources",{})
        roots = (c.get("keys"),sources.get("config"),sources.get("tls"),c.get("base"))
        dirs = all(isinstance(v,str) and Path(v).is_absolute() and Path(v).is_dir()
                   and not Path(v).is_symlink() for v in roots)
        print(f"RESPALDO raices_keyring_config_tls_y_destino={int(dirs)} contenido_no_leido=si")
        if not dirs:
            block("RESPALDO_RAICES_NO_ACREDITADAS")
    else:
        block("RESPALDO_CONFIG_PRIVADA_NO_VERIFICADA")
    seen = sum(1 for base in (Path("/usr/local/sbin"),Path("/opt/hf-one"),BASE/"scripts/ops")
               if base.is_dir() for path in base.iterdir()
               if "backup" in path.name.lower() and re.search("cutover|manual",path.name,re.I)
               and path.is_file() and not path.is_symlink())
    print(f"RESPALDO productores_manuales_candidatos_directos={seen} sin_ejecutar=si")
    block("RESPALDO_COHERENTE_BAJO_LOCK_EXISTENTE_AUN_NO_ACREDITADO")


def main(raw_port):
    if os.geteuid() != 0 or not raw_port.isdecimal() or not 0<int(raw_port)<=65535:
        print("NO-GO IDENTIDAD_SSH_O_SUDO_NO_VERIFICADA")
        return 4
    db_hash = None
    for area, check in (("INGRESO",lambda: inventory_ingress(int(raw_port))),
                        ("RESPALDO",backup_inventory), ("POSTGRESQL",postgres),
                        ("MIGRATOR",lambda: migration_inventory(db_hash))):
        try:
            result = check()
            if area == "POSTGRESQL":
                db_hash = result
        except Exception:
            # Never let exceptions include DSNs, config paths, rules or PostgreSQL error text.
            block(area + "_LECTURA_NO_COMPLETA")
    for name in sorted(BLOCKS):
        print("BLOQUEO "+name)
    print("NO-GO CORTE_RC11 | Inventario de solo lectura; no se intento cerrar ingreso ni iniciar R4.")
    return 4


if __name__ == "__main__":
    sys.exit(main(sys.argv[-1] if len(sys.argv)>1 else ""))


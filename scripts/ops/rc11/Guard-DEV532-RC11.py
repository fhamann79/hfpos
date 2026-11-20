"""Owned dual-stack policy and its narrow systemd installation (embedded in SSH payload)."""

PERMANENT = "hf_one_rc11_guard"
GUARD_DIR = Path("/etc/hf-one-rc11-guard")
GUARD_UNIT = "hf-one-rc11-guard.service"
UNIT_PATH = Path("/etc/systemd/system") / GUARD_UNIT
DOCKER_DROPIN = Path("/etc/systemd/system/docker.service.d/90-hf-one-rc11-guard.conf")
NFT = "/usr/sbin/nft"
POLICY = GUARD_DIR / "guard.nft"
SNAPSHOT = GUARD_DIR / "guard.expected"
WINDOW = GUARD_DIR / "maintenance.nft"
BOOT_CHECK = GUARD_DIR / "boot-check.py"
BOOT_COMMAND = f"/usr/bin/python3 -I {BOOT_CHECK}"

UNIT_TEXT = f"""[Unit]
Description=HF ONE RC11 ingress protection
Before=docker.service
After=ufw.service
RefuseManualStop=yes

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart={BOOT_COMMAND} --load-window
"""
DROPIN_TEXT = f"""[Unit]
Requires={GUARD_UNIT}
After={GUARD_UNIT}

[Service]
ExecStartPre={BOOT_COMMAND} --check
"""


def boot_check(action):
    """Also runs in an isolated Python process before dockerd, independent of oneshot state."""
    d = GUARD_DIR.lstat()
    need(stat.S_ISDIR(d.st_mode) and d.st_uid == 0 and (d.st_mode & 0o077) == 0,
         "GUARD_DIRECTORIO_NO_PRIVADO")
    need(action in ("--load-window", "--check"), "GUARD_BOOT_ACCION_INCIERTA")
    need(private_file(POLICY) and private_file(SNAPSHOT), "GUARD_PERMANENTE_DERIVA")
    if action == "--load-window" and not any(
            row.get("table", {}).get("family") == "inet"
            and row["table"].get("name") == PERMANENT for row in nft_state()):
        cmd([NFT, "-f", str(POLICY)], "GUARD_INSTALACION_INCIERTA")
    need(guard_snapshot() == SNAPSHOT.read_text(encoding="ascii"), "GUARD_PERMANENTE_DERIVA")
    if WINDOW.exists() or WINDOW.is_symlink():
        ident = STATE / "guard-id"
        d = STATE.lstat()
        need(stat.S_ISDIR(d.st_mode) and d.st_uid == 0 and (d.st_mode & 0o077) == 0
             and private_file(ident), "TABLA_PROPIEDAD_NO_ACREDITADA")
        token = ident.read_text(encoding="ascii")
        need(re.fullmatch(r"hf-one-rc11-[a-f0-9]{32}", token) and private_file(WINDOW)
             and WINDOW.read_text(encoding="ascii") == firewall_spec(token),
             "NFT_CIERRE_PERSISTENTE_NO_VERIFICADO")
        if action == "--load-window":
            # Never replace a live table, including one left by an earlier partial start.
            if absent():
                cmd([NFT, "-f", "-"], "NFT_CIERRE_NO_CONFIRMADO", firewall_spec(token))
        verify_guard(token)
    else:
        need(absent(), "NFT_CIERRE_DISCO_KERNEL_DIVERGENTE")
    if action == "--check":
        verify_ufw()


def exact_exec(value, *commands):
    pattern = []
    for command in commands:
        path = command.split()[0]
        pattern.append(r"\{ path=" + re.escape(path) + r" ; argv\[\]=" + re.escape(command)
                       + r" ; ignore_errors=no ; [^{}]*\}")
    return re.fullmatch(r"\s*".join(pattern), value) is not None


def guard_spec(bridges, origins, web):
    """Return one atomic create transaction. All values are validated before interpolation."""
    iface = lambda s: re.fullmatch(r"[A-Za-z0-9_.-]{1,15}", s) and s not in ("lo", ".", "..")
    prefixes = {br: tuple(sorted(map(ipaddress.ip_network, blocks), key=lambda n: (n.version, int(n.network_address))))
                for br, blocks in bridges.items()}
    need(len(prefixes) == 2 and all(iface(br) and blocks for br, blocks in prefixes.items())
         and set(origins) == {4, 6} and set(web) <= {4, 6}, "GUARD_TOPOLOGIA_INCIERTA")
    blocks = [n for nets in prefixes.values() for n in nets]
    need(all(not (a.version == b.version and a.overlaps(b))
             for i, a in enumerate(blocks) for b in blocks[i+1:]), "GUARD_CIDR_SUPERPUESTO")
    rules = []
    for br, nets in prefixes.items():
        for net in nets:
            family = "ip" if net.version == 4 else "ip6"
            rules.append(("prerouting", f'iifname != "{br}" {family} saddr {net} drop'))
    for v, family in ((4, "ip"), (6, "ip6")):
        ingress, host = origins[v]
        host = ipaddress.ip_address(host)
        need(iface(ingress) and ingress not in bridges and host.version == v
             and not (host.is_unspecified or host.is_loopback or host.is_link_local or host.is_multicast)
             and not any(host in n for n in blocks), "GUARD_ORIGEN_INCIERTO")
        incoming = f'iifname "{ingress}" meta nfproto ipv{v}'
        if v in web:
            mode, target = web[v]
            ep = ipaddress.ip_address(target)
            web_br = next(iter(bridges))
            need(any(ep in n for n in prefixes[web_br]), "GUARD_WEB_IP_INCIERTA")
            if mode == "proxy":
                need(ep.version == v or (v,ep.version)==(6,4), "GUARD_WEB_IP_INCIERTA")
                rules.append(("input", f'{incoming} {family} daddr {host} tcp dport 443 return'))
            else:
                need(mode == "dnat" and ep.version == v, "GUARD_WEB_IP_INCIERTA")
                rules.append(("forward", f'{incoming} oifname "{web_br}" {family} daddr {ep} '
                              f'tcp dport 8443 ct status dnat ct direction original '
                              f'ct original {family} daddr {host} ct original proto-dst 443 return'))
        for br, nets in prefixes.items():
            for net in (n for n in nets if n.version == v):
                reply = (f'{incoming} oifname "{br}" {family} daddr {net} '
                         f'ct direction reply ct original {family} saddr {net}')
                rules.append(("forward", f'{reply} ct state established return'))
                icmp = "icmp" if v == 4 else "ipv6-icmp"
                rules.append(("forward", f'{reply} ct state related meta l4proto {icmp} return'))
                pre_reply = (f'{incoming} {family} daddr {net} ct direction reply '
                             f'ct original {family} saddr {net}')
                rules.append(("prerouting", f'{pre_reply} ct state established return'))
                rules.append(("prerouting", f'{pre_reply} ct state related meta l4proto {icmp} return'))
    for br, nets in prefixes.items():
        rules.append(("forward", f'iifname "{br}" oifname "{br}" return'))
        rules.append(("forward", f'oifname "{br}" drop'))
        for net in nets:
            family = "ip" if net.version == 4 else "ip6"
            rules.append(("prerouting", f'iifname != "{br}" {family} daddr {net} drop'))
            rules.append(("forward", f'{family} daddr {net} drop'))
    rules.append(("input", 'iifname != "lo" tcp dport 443 drop'))
    return (f"create table inet {PERMANENT}\n"
            + "".join(f"add chain inet {PERMANENT} {chain} {{ type filter hook {chain} priority {prio}; policy accept; }}\n"
                      for chain, prio in (("prerouting", -110), ("input", -10), ("forward", -10)))
            + "".join(f"add rule inet {PERMANENT} {chain} {rule}\n" for chain, rule in rules))


def systemd_props(unit, *keys):
    result = cmd(["/usr/bin/systemctl", "show", "--all", *("-p"+k for k in keys), unit],
                 "GUARD_SYSTEMD_NO_ACREDITADO")
    return dict(line.split("=", 1) for line in result.splitlines())


def guard_host_checks():
    # A dormant stock loader is not started: its start/reload/stop may flush all tables.
    for unit in ("nftables.service", "firewalld.service",
                 "netfilter-persistent.service", "iptables.service", "ip6tables.service"):
        p = systemd_props(unit, "LoadState", "ActiveState", "UnitFileState", "RequiredBy", "WantedBy", "TriggeredBy")
        need(p.get("ActiveState") == "inactive" and
             (p.get("LoadState") in ("masked", "not-found") or
              (p.get("LoadState") == "loaded" and p.get("UnitFileState") == "disabled" and
               all(p.get(k) == "" for k in ("RequiredBy", "WantedBy", "TriggeredBy")))),
             "GUARD_SERVICIO_FIREWALL_CONFLICTIVO")
    verify_ufw()
    need(Path(NFT).is_file() and cmd(["docker", "info", "--format", "{{.LiveRestoreEnabled}}"],
         "GUARD_LIVE_RESTORE_INCIERTO").strip() == "false", "GUARD_LIVE_RESTORE_O_NFT_INCIERTO")
    prove_raw_hooks(nft_state())


def verify_ufw():
    ufw = systemd_props("ufw.service", "LoadState", "ActiveState", "FragmentPath", "DropInPaths",
                        "ExecStart", "ExecStop", "ExecReload", "ExecStartPre", "ExecStartPost",
                        "ExecStopPost", "ExecCondition", "Environment", "EnvironmentFiles", "NeedDaemonReload")
    init = "/usr/lib/ufw/ufw-init"
    if ufw.get("LoadState") in ("masked", "not-found"):
        need(ufw.get("ActiveState") == "inactive", "GUARD_UFW_UNIDAD_NO_ACREDITADA")
        if not any(p.exists() or p.is_symlink() for p in
                   map(Path,(init,"/lib/ufw/ufw-init","/usr/sbin/ufw","/etc/init.d/ufw"))):
            return
    else:
        need(ufw.get("LoadState") == "loaded"
             and ufw.get("FragmentPath") in ("/lib/systemd/system/ufw.service", "/usr/lib/systemd/system/ufw.service")
             and ufw.get("NeedDaemonReload") == "no"
             and all(ufw.get(k, "") == "" for k in ("DropInPaths", "ExecReload", "ExecStartPre",
                                               "ExecStartPost", "ExecStopPost", "ExecCondition",
                                               "Environment", "EnvironmentFiles")),
             "GUARD_UFW_UNIDAD_NO_ACREDITADA")
        need(any(exact_exec(ufw.get("ExecStart",""), f"{p} start quiet")
                 and exact_exec(ufw.get("ExecStop",""), f"{p} stop")
                 and Path(p).resolve(strict=True)==Path(init)
                 for p in (init,"/lib/ufw/ufw-init")), "GUARD_UFW_ACCION_NO_ACREDITADA")
    verify_ufw_scripts()


def trusted_read(path):
    info = path.lstat()
    need(stat.S_ISREG(info.st_mode) and info.st_uid==0 and not info.st_mode & 0o022
         and info.st_size <= 524288, "GUARD_UFW_ARCHIVO_NO_ACREDITADO")
    return path.read_bytes()


def ufw_config(text, allowed):
    need(not re.search(r"[^\t\n -~]", text), "GUARD_UFW_CONFIG_NO_ACREDITADA")
    found = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        m = re.fullmatch(r"""([A-Z][A-Z0-9_]*)=(?:(["'])([A-Za-z0-9_./-]*)\2|([A-Za-z0-9_./-]+))""", line)
        need(m and m[1] not in found and (m[3] if m[2] else m[4]) in allowed.get(m[1],()),
             "GUARD_UFW_CONFIG_NO_ACREDITADA")
        found[m[1]] = m[3] if m[2] else m[4]
    need(found.keys() == allowed.keys(), "GUARD_UFW_CONFIG_INCOMPLETA")
    return found


def verify_ufw_scripts():
    code = "GUARD_UFW_PAQUETE_NO_ACREDITADO"
    need(cmd(["/usr/bin/dpkg-query","-W","-f=${Status} ${Version}", "ufw"], code)
         == "install ok installed 0.36.2-6", code)
    data = trusted_read(Path("/var/lib/dpkg/info/ufw.md5sums")).decode("ascii")
    hashes = {"/"+name:digest for digest,name in (line.split() for line in data.splitlines())}
    def stock(path, name):
        need(hashes.get(name) == hashlib.md5(trusted_read(Path(path)), usedforsecurity=False).hexdigest(), code)
    prefix = "/usr/lib/python3/dist-packages/ufw/"
    for name in ("/usr/lib/ufw/ufw-init","/usr/lib/ufw/ufw-init-functions","/usr/sbin/ufw",
                 "/usr/lib/systemd/system/ufw.service",*(n for n in hashes if n.startswith(prefix))):
        stock(name,name)
    need(Path("/lib/ufw/ufw-init-functions").resolve(strict=True)==Path("/usr/lib/ufw/ufw-init-functions"), code)
    for hook in ("before.init","after.init"):
        path=Path("/etc/ufw")/hook
        if path.exists() or path.is_symlink():
            stock(path,"/usr/share/ufw/"+hook)
    actions=("ACCEPT","DROP","REJECT")
    defaults = ufw_config(trusted_read(Path("/etc/default/ufw")).decode("ascii"),dict(
        IPV6=("yes",), MANAGE_BUILTINS=("no",), IPT_SYSCTL=("/etc/ufw/sysctl.conf", ""), IPT_MODULES=("",),
        DEFAULT_INPUT_POLICY=actions, DEFAULT_OUTPUT_POLICY=actions, DEFAULT_FORWARD_POLICY=actions,
        DEFAULT_APPLICATION_POLICY=(*actions,"SKIP")))
    if defaults["IPT_SYSCTL"]:
        need(hashlib.sha256(trusted_read(Path(defaults["IPT_SYSCTL"]))).hexdigest()
             == "df05de02ea325b3c6d0d22c06c3dc7ebaf3a4e06e41fb98a0671b430b7da5b7f",
             "GUARD_UFW_SYSCTL_NO_ACREDITADO")
    ufw_config(trusted_read(Path("/etc/ufw/ufw.conf")).decode("ascii"),
               dict(ENABLED=("yes","no"), LOGLEVEL=("off","low","medium","high","full")))
    for base in ("before","after","user"):
        for family in ("","6"):
            rules=trusted_read(Path(f"/etc/ufw/{base}{family}.rules")).decode("utf-8")
            lines=[line.strip() for line in rules.splitlines() if line.strip() and not line.lstrip().startswith("#")]
            need(lines[:1]==["*filter"] and lines[-1:]==["COMMIT"]
                 and all(line.startswith((":ufw-", "-A ufw-")) for line in lines[1:-1]),
                 "GUARD_UFW_RULES_NO_ACREDITADAS")
    for tool in ("iptables","ip6tables","iptables-restore","ip6tables-restore","iptables-save","ip6tables-save"):
        path=Path("/usr/sbin")/tool
        need(path.resolve(strict=True)==Path("/usr/sbin/xtables-nft-multi"), "GUARD_UFW_BACKEND_INCIERTO")


def sync_dir(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def durable_file(path, content):
    fd, tmp = tempfile.mkstemp(prefix=".rc11-", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="ascii", newline="") as f:
            f.write(content)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
        sync_dir(path.parent)
    finally:
        Path(tmp).unlink(missing_ok=True)


def guard_snapshot():
    return cmd([NFT, "-s", "list", "table", "inet", PERMANENT], "GUARD_PERMANENTE_AUSENTE")


def guard_boot_files():
    need(all(private_file(p) and p.read_text(encoding="ascii") == value for p, value in
             ((UNIT_PATH, UNIT_TEXT), (DOCKER_DROPIN, DROPIN_TEXT), (BOOT_CHECK, boot_source))),
         "GUARD_ARCHIVOS_SYSTEMD_INCIERTOS")
    docker = systemd_props("docker.service", "LoadState", "Requires", "After", "DropInPaths",
                           "NeedDaemonReload", "ExecStartPre")
    need(all(GUARD_UNIT in docker.get(k, "").split() for k in ("Requires", "After"))
         and str(DOCKER_DROPIN) in docker.get("DropInPaths", "").split()
         and docker.get("LoadState") == "loaded" and docker.get("NeedDaemonReload") == "no"
         and exact_exec(docker.get("ExecStartPre", ""), BOOT_COMMAND + " --check"),
         "GUARD_ORDEN_DOCKER_NO_ACREDITADO")
    unit = systemd_props(GUARD_UNIT, "FragmentPath", "DropInPaths", "ActiveState", "NeedDaemonReload",
                         "Type", "RemainAfterExit", "ExecStart", "ExecCondition", "Conditions", "Asserts")
    need(unit.get("FragmentPath") == str(UNIT_PATH) and unit.get("ActiveState") == "active"
         and unit.get("NeedDaemonReload") == "no" and unit.get("Type") == "oneshot"
         and unit.get("RemainAfterExit") == "yes"
         and all(unit.get(k, "") == "" for k in ("DropInPaths", "ExecCondition", "Conditions", "Asserts"))
         and exact_exec(unit.get("ExecStart", ""), BOOT_COMMAND + " --load-window"),
         "GUARD_SERVICIO_NO_ACREDITADO")


def assert_permanent(spec=None):
    guard_boot_files()
    need(private_file(POLICY) and private_file(SNAPSHOT)
         and (spec is None or POLICY.read_text(encoding="ascii") == spec)
         and guard_snapshot() == SNAPSHOT.read_text(encoding="ascii"), "GUARD_PERMANENTE_DERIVA")
    boot_check("--check")
    guard_host_checks()


def install_permanent(spec):
    # Never overwrite an existing owner or adopt a partial previous installation.
    need(not any(p.exists() or p.is_symlink() for p in (GUARD_DIR, UNIT_PATH, DOCKER_DROPIN))
         and not any(r.get("table", {}).get("name") == PERMANENT for r in nft_state()),
         "GUARD_INSTALACION_PREVIA_NO_RECONCILIADA")
    guard_host_checks()
    cmd([NFT, "-c", "-f", "-"], "GUARD_NFT_NO_COMPATIBLE", spec)
    GUARD_DIR.mkdir(mode=0o700)
    sync_dir(GUARD_DIR.parent)
    durable_file(POLICY, spec)
    durable_file(BOOT_CHECK, boot_source)
    cmd([NFT, "-f", str(POLICY)], "GUARD_INSTALACION_INCIERTA")
    durable_file(SNAPSHOT, guard_snapshot())
    DOCKER_DROPIN.parent.mkdir(exist_ok=True)
    sync_dir(DOCKER_DROPIN.parent.parent)
    durable_file(DOCKER_DROPIN, DROPIN_TEXT)
    durable_file(UNIT_PATH, UNIT_TEXT)
    cmd(["systemctl", "daemon-reload"], "GUARD_SYSTEMD_RELOAD_INCIERTO")
    cmd(["systemctl", "start", GUARD_UNIT], "GUARD_INSTALACION_INCIERTA")
    assert_permanent(spec)


def refresh_permanent(old, new, token):
    verify_guard(token)
    assert_permanent(old)
    if old != new:
        replace = f"delete table inet {PERMANENT}\n" + new
        cmd([NFT, "-c", "-f", "-"], "GUARD_CAMBIO_NO_COMPATIBLE", replace)
        durable_file(POLICY, new)
        cmd([NFT, "-f", "-"], "GUARD_CAMBIO_INCIERTO", replace)
        durable_file(SNAPSHOT, guard_snapshot())
    assert_permanent(new)
    verify_guard(token)

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

UNIT_TEXT = f"""[Unit]
Description=HF ONE RC11 ingress protection
Before=docker.service
RefuseManualStop=yes

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart={NFT} -f {POLICY}
ExecStart=/bin/sh -ec 'if [ -e {WINDOW} ]; then exec {NFT} -f {WINDOW}; fi'
"""
DROPIN_TEXT = f"""[Unit]
Requires={GUARD_UNIT}
After={GUARD_UNIT}

[Service]
ExecStartPre=/bin/sh -ec '{NFT} -s list table inet {PERMANENT} | /usr/bin/cmp -s {SNAPSHOT} -'
ExecStartPre=/bin/sh -ec 'if [ -e {WINDOW} ]; then exec {NFT} list table inet {TABLE} >/dev/null; fi'
"""


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
        rules.append(("input", f'{incoming} {family} daddr {host} tcp dport 443 return'))
        if v in web:
            ep = ipaddress.ip_address(web[v])
            web_br = next(iter(bridges))
            need(ep.version == v and any(ep in n for n in prefixes[web_br]), "GUARD_WEB_IP_INCIERTA")
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
    result = cmd(["systemctl", "show", *("-p"+k for k in keys), unit], "GUARD_SYSTEMD_NO_ACREDITADO")
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
    ufw = systemd_props("ufw.service", "LoadState", "ActiveState", "FragmentPath", "DropInPaths",
                        "ExecStart", "ExecStop", "ExecReload", "ExecStartPre", "ExecStartPost",
                        "ExecStopPost", "NeedDaemonReload")
    if ufw.get("LoadState") in ("masked", "not-found"):
        need(ufw.get("ActiveState") == "inactive", "GUARD_UFW_UNIDAD_NO_ACREDITADA")
    else:
        need(ufw.get("FragmentPath") in ("/lib/systemd/system/ufw.service", "/usr/lib/systemd/system/ufw.service")
             and ufw.get("NeedDaemonReload") == "no"
             and all(ufw.get(k) == "" for k in ("DropInPaths", "ExecReload", "ExecStartPre",
                                               "ExecStartPost", "ExecStopPost")),
             "GUARD_UFW_UNIDAD_NO_ACREDITADA")
        for key, action in (("ExecStart", "start quiet"), ("ExecStop", "stop")):
            need(re.fullmatch(r"\{ path=(?P<init>/(?:usr/)?lib/ufw/ufw-init) ; argv\[\]=(?P=init) "
                              + action + r" ; ignore_errors=no ; [^{}]*\}", ufw.get(key, "")),
                 "GUARD_UFW_ACCION_NO_ACREDITADA")
        verify_ufw_scripts()
    need(Path(NFT).is_file() and cmd(["docker", "info", "--format", "{{.LiveRestoreEnabled}}"],
         "GUARD_LIVE_RESTORE_INCIERTO").strip() == "false", "GUARD_LIVE_RESTORE_O_NFT_INCIERTO")
    prove_raw_hooks(nft_state())


def verify_ufw_scripts():
    md5s = Path("/var/lib/dpkg/info/ufw.md5sums").read_text(encoding="ascii").splitlines()
    conffiles = cmd(["dpkg-query", "-W", "-f=${Conffiles}", "ufw"], "GUARD_UFW_PAQUETE_NO_ACREDITADO")
    hashes = {"/" + path: digest for digest, path in (line.split() for line in md5s)}
    for line in conffiles.splitlines():
        if line.strip():
            path, digest, *_ = line.split()
            hashes[path] = digest
    for name in ("/lib/ufw/ufw-init", "/lib/ufw/ufw-init-functions",
                 "/etc/ufw/before.init", "/etc/ufw/after.init"):
        path = Path(name)
        if name.endswith(".init") and not path.exists() and not path.is_symlink():
            continue
        digest = hashes.get(name, hashes.get("/usr" + name))
        need(not path.is_symlink() and digest == hashlib.md5(path.read_bytes(), usedforsecurity=False).hexdigest(),
             "GUARD_UFW_HOOK_NO_ACREDITADO")
    for tool in ("iptables", "ip6tables"):
        need("(nf_tables)" in cmd([tool, "--version"], "GUARD_UFW_BACKEND_INCIERTO"),
             "GUARD_UFW_BACKEND_INCIERTO")


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
             ((UNIT_PATH, UNIT_TEXT), (DOCKER_DROPIN, DROPIN_TEXT))),
         "GUARD_ARCHIVOS_SYSTEMD_INCIERTOS")
    docker = systemd_props("docker.service", "Requires", "After", "DropInPaths", "NeedDaemonReload", "ExecStartPre")
    need(all(GUARD_UNIT in docker.get(k, "").split() for k in ("Requires", "After"))
         and str(DOCKER_DROPIN) in docker.get("DropInPaths", "").split()
         and docker.get("NeedDaemonReload") == "no" and all(str(p) in docker.get("ExecStartPre", "")
                                                          for p in (SNAPSHOT, WINDOW)),
         "GUARD_ORDEN_DOCKER_NO_ACREDITADO")
    unit = systemd_props(GUARD_UNIT, "FragmentPath", "DropInPaths", "ActiveState", "NeedDaemonReload")
    need(unit == {"FragmentPath":str(UNIT_PATH), "DropInPaths":"", "ActiveState":"active",
                  "NeedDaemonReload":"no"}, "GUARD_SERVICIO_NO_ACREDITADO")


def assert_permanent(spec=None):
    guard_boot_files()
    need(private_file(POLICY) and private_file(SNAPSHOT)
         and (spec is None or POLICY.read_text(encoding="ascii") == spec)
         and guard_snapshot() == SNAPSHOT.read_text(encoding="ascii"), "GUARD_PERMANENTE_DERIVA")
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
    DOCKER_DROPIN.parent.mkdir(exist_ok=True)
    sync_dir(DOCKER_DROPIN.parent.parent)
    durable_file(DOCKER_DROPIN, DROPIN_TEXT)
    durable_file(UNIT_PATH, UNIT_TEXT)
    cmd(["systemctl", "daemon-reload"], "GUARD_SYSTEMD_RELOAD_INCIERTO")
    cmd(["systemctl", "start", GUARD_UNIT], "GUARD_INSTALACION_INCIERTA")
    durable_file(SNAPSHOT, guard_snapshot())
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

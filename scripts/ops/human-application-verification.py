#!/usr/bin/env python3
"""Human-only restored application verification; never restore, pull or deploy."""
import argparse
import base64
import getpass
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys
import warnings

HERE = Path(__file__).resolve().parent
IMAGE = "ghcr.io/fhamann79/hfpos-backend@sha256:5654a239a032076e72306e1d7057377eaddf30674872195a89068cae6f40cbe1"
REVISION = "99b92e0e0f6f34e5dd3921016f787e73ad1c441d"
FILES = ("human-application-verification.py", "RestoredApplicationProbe.dll")
SOURCE = "backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost/RestoredApplicationProbe/"


def require(value):
    if not value:
        raise ValueError("STOP")


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode("ascii")


def decode(data):
    require(len(data) <= 16 * 1024 * 1024)
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result)
            result[key] = value
        return result
    return json.loads(data, object_pairs_hook=pairs)


def root_path(path, directory=True, mode=None):
    path = Path(path)
    require(path.is_absolute())
    for item in (path, *path.parents):
        info = item.lstat()
        require(info.st_uid == 0 and not info.st_mode & 0o022 and not stat.S_ISLNK(info.st_mode))
    info = path.stat()
    require(stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode))
    require(mode is None or stat.S_IMODE(info.st_mode) == mode)
    return path


def digest(files):
    value = hashlib.sha256()
    for name in FILES:
        value.update(name.encode() + b"\0" + hashlib.sha256(files[name]).digest())
    return value.hexdigest()


def native(command, data=None, timeout=60, check=True):
    result = subprocess.run(command, input=data, capture_output=True, timeout=timeout,
                            env={"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C"})
    if check:
        require(result.returncode == 0)
        return result.stdout
    return result


def docker(*args, data=None, timeout=60, check=True):
    return native(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config",
                   str(HERE / "docker-empty"), *args], data, timeout, check)


def seal(args):
    require(re.fullmatch(r"[a-f0-9]{40}", args.source_sha))
    repo = HERE.parents[1]
    require(native(["git", "-C", str(repo), "rev-parse", "HEAD"]).decode().strip() == args.source_sha)
    sources = ("scripts/ops/human-application-verification.py", SOURCE + "Program.cs", SOURCE + "RestoredApplicationProbe.csproj")
    for name in sources:
        require(native(["git", "-C", str(repo), "show", args.source_sha + ":" + name]).replace(b"\r\n", b"\n") ==
                (repo / name).read_bytes().replace(b"\r\n", b"\n"))
    # The binary hash must also be checked against the reviewed exact-source CI artifact.
    files = {FILES[0]: Path(__file__).read_bytes().replace(b"\r\n", b"\n"), FILES[1]: Path(args.probe_dll).read_bytes()}
    require(0 < len(files[FILES[1]]) <= 65536)
    output = Path(args.output)
    output.mkdir(mode=0o700)
    for name, content in files.items():
        (output / name).write_bytes(content)
        (output / name).chmod(0o600)
    (output / "source.json").write_bytes(canonical({"source_sha": args.source_sha, "code_sha256": digest(files),
        "probe_sha256": hashlib.sha256(files[FILES[1]]).hexdigest()}))
    (output / "source.json").chmod(0o600)
    (output / "docker-empty").mkdir(mode=0o700)
    print("APP_CODE_SHA256=" + digest(files))
    print("PROBE_SHA256=" + hashlib.sha256(files[FILES[1]]).hexdigest())


def load_core(path):
    path = root_path(path, mode=0o700)
    root_path(path / "human-recovery.py", False, 0o600)
    spec = importlib.util.spec_from_file_location("sealed_core", path / "human-recovery.py")
    core = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(core)
    for name in core.FILES:
        core.root_path(path / name, False, 0o600)
    core.root_path(path / "docker-empty", mode=0o700)
    require(not any((path / "docker-empty").iterdir()))
    source = core.decode(core.root_path(path / "source.json", False, 0o600).read_bytes())
    require(source["code_sha256"] == core.digest_code())
    return core, source


def image_identity():
    item = decode(docker("image", "inspect", IMAGE))[0]
    require(item["Os"] == "linux" and item["Architecture"] == "amd64")
    require(IMAGE in item["RepoDigests"] and item["Config"]["Labels"]["org.opencontainers.image.revision"] == REVISION)
    require(item["Config"]["User"] == "1654:1654" and item["Config"]["Entrypoint"] == ["dotnet", "Pos.Backend.Api.dll"])
    require(not item["Config"].get("Volumes"))
    return item["Id"]


def sql(db, statement, role="hfpos_recovery_admin", check=True):
    return docker("exec", "-i", db, "psql", "-X", "-qAt", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=sqlstate",
                  "-U", role, "-d", "hfpos_recovery", data=statement.encode(), check=check)


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


def fingerprint(db):
    relations = decode(sql(db, "SELECT coalesce(json_agg(json_build_array(n.nspname,c.relname,c.relkind) ORDER BY n.nspname,c.relname),'[]') "
        "FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' "
        "AND c.relkind IN ('r','m','S');"))
    require(0 < len(relations) <= 1000)
    queries = ["BEGIN READ ONLY;"]
    for schema, table, kind in relations:
        name = identifier(schema) + "." + identifier(table)
        if kind == "S":
            queries.append("SELECT md5(jsonb_build_array(last_value,is_called)::text) FROM " + name + ";")
        else:
            queries.append("SELECT md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM "
                           "(SELECT md5(to_jsonb(t)::text) h FROM ONLY " + name + " t) rows;")
    queries.append("COMMIT;")
    hashes = sql(db, "\n".join(queries)).strip().splitlines()
    require(len(hashes) == len(relations) and all(re.fullmatch(b"[a-f0-9]{32}", h) for h in hashes))
    return hashlib.sha256(canonical(relations) + b"\n".join(hashes)).hexdigest()


def create_reader(db, role):
    require(re.fullmatch(r"hfpos_app_verify_[a-f0-9]{32}", role))
    # No password, memberships, schema ownership, DML or sequence USAGE. Destination only.
    sql(db, "CREATE ROLE " + role + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT;"
        "ALTER ROLE " + role + " SET default_transaction_read_only=on;"
        "GRANT CONNECT ON DATABASE hfpos_recovery TO " + role + ";GRANT USAGE ON SCHEMA public TO " + role + ";"
        "GRANT SELECT ON ALL TABLES IN SCHEMA public TO " + role + ";GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO " + role + ";")


def verify_reader(db, role):
    query = """
SELECT current_setting('default_transaction_read_only')='on'
 AND (SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole
      AND NOT rolreplication AND NOT rolbypassrls AND NOT rolinherit FROM pg_roles WHERE rolname=current_user)
 AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=(SELECT oid FROM pg_roles WHERE rolname=current_user))
 AND NOT has_database_privilege(current_user,current_database(),'CREATE')
 AND NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname !~ '^pg_' AND nspname<>'information_schema'
                AND has_schema_privilege(current_user,oid,'CREATE'))
 AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
     WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND c.relkind IN ('r','p','m','v','f')
     AND (has_table_privilege(current_user,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
          OR has_any_column_privilege(current_user,c.oid,'INSERT,UPDATE,REFERENCES')))
 AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
     WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND c.relkind='S'
       AND has_sequence_privilege(current_user,c.oid,'USAGE,UPDATE'))
 AND NOT EXISTS(SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
     WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND p.prosecdef
       AND has_function_privilege(current_user,p.oid,'EXECUTE'));
"""
    require(sql(db, query, role).strip() == b"t")
    # SELECT FOR UPDATE requires UPDATE permission even when the queue is empty.
    denied = sql(db, "SET default_transaction_read_only=off;BEGIN;SELECT \"Id\" FROM public.\"ElectronicIssuingJobs\" LIMIT 1 FOR UPDATE;ROLLBACK;", role, False)
    require(denied.returncode != 0 and b"42501" in denied.stderr)


def configuration(role, application):
    return {"ASPNETCORE_ENVIRONMENT": "Production", "ASPNETCORE_URLS": "http://127.0.0.1:8080",
        "AllowedHosts": "127.0.0.1;localhost", "SeedDemoData": "false", "PlatformBootstrap__Enabled": "false",
        "ConnectionStrings__DefaultConnection": "Host=127.0.0.1;Port=5432;Database=hfpos_recovery;Username=" + role + ";Pooling=false;Timeout=5",
        "Jwt__Key": secrets.token_hex(32), "Jwt__Issuer": "hfpos-isolated-verification", "Jwt__Audience": "hfpos-isolated-verification",
        "Jwt__ExpiresMinutes": "5", "DataProtection__KeysPath": "/keys", "DataProtection__ApplicationName": application,
        "ForwardedHeaders_Enabled": "false", "ForwardedHeaders__Enabled": "false", "Observability__Enabled": "false",
        "Sri__Environment": "1", "Sri__AllowProductionSubmission": "false", "Sri__ReceptionTestUrl": "https://sri.invalid/reception",
        "Sri__AuthorizationTestUrl": "https://sri.invalid/authorization", "Sri__ReceptionProductionUrl": "https://sri.invalid/disabled-reception",
        "Sri__AuthorizationProductionUrl": "https://sri.invalid/disabled-authorization", "Logging__LogLevel__Default": "None",
        "DOTNET_EnableDiagnostics": "0", "COMPlus_DbgEnableMiniDump": "0"}


def inspect_app(name, container_id, image_id, db_id, labels, env):
    item = decode(docker("inspect", name))[0]
    host, config = item["HostConfig"], item["Config"]
    require(item["Id"] == container_id and item["Image"] == image_id and config["Image"] == IMAGE)
    require(host["NetworkMode"] == "container:" + db_id and host["ReadonlyRootfs"] and not host["Privileged"])
    require(not host.get("Binds") and not host.get("VolumesFrom") and not host["PortBindings"] and not host.get("Devices"))
    require(host["CapDrop"] == ["ALL"] and "no-new-privileges" in host["SecurityOpt"] and not host.get("PidMode"))
    require(host["LogConfig"]["Type"] == "none" and host["RestartPolicy"]["Name"] == "no")
    require(host["Memory"] == host["MemorySwap"] == 805306368 and host["PidsLimit"] == 128 and host["NanoCpus"] == 500000000)
    image_config = decode(docker("image", "inspect", IMAGE))[0]["Config"]
    inherited = image_config.get("Labels") or {}
    require(config["User"] == "1654:1654" and config["Labels"] == {**inherited, **labels})
    require(config.get("Healthcheck") in (None, {"Test": ["NONE"]}))
    actual_env = dict(entry.split("=", 1) for entry in config["Env"])
    inherited_env = dict(entry.split("=", 1) for entry in image_config.get("Env") or [])
    require(len(actual_env) == len(config["Env"]) and actual_env == {**inherited_env, **env})
    require(host["Tmpfs"] == {"/keys": "rw,noexec,nosuid,size=16m,uid=1654,gid=1654,mode=0700",
        "/tmp": "rw,noexec,nosuid,size=32m,uid=1654,gid=1654,mode=0700"})
    require(all(m["Type"] == "tmpfs" and m["Destination"] in host["Tmpfs"] for m in item["Mounts"]))
    require(config["Entrypoint"] == ["/bin/sh"] and config["Cmd"] == probe_command())
    return item


def probe_command():
    # Literal code only: the quoted variable is public base64 data, never shell syntax.
    # No API depsfile: this framework-only console runs from /tmp, not the API appbase.
    return ["-c", "set -eu; umask 077; set -C; "
        "test -x /usr/bin/base64; test -x /usr/bin/chmod; test -x /usr/bin/sha256sum; "
        "test -r /app/Pos.Backend.Api.runtimeconfig.json; test -r /app/Pos.Backend.Api.dll; "
        "printf '%s' \"$HFPOS_VERIFY_PROBE_BASE64\" | /usr/bin/base64 --decode > /tmp/RestoredApplicationProbe.dll; "
        "/usr/bin/chmod 0400 /tmp/RestoredApplicationProbe.dll; "
        "printf '%s  /tmp/RestoredApplicationProbe.dll\\n' \"$HFPOS_VERIFY_PROBE_SHA256\" | /usr/bin/sha256sum --check --status; "
        "exec dotnet exec --runtimeconfig /app/Pos.Backend.Api.runtimeconfig.json /tmp/RestoredApplicationProbe.dll"]


def probe_environment(expected_sha256):
    content = (HERE / "RestoredApplicationProbe.dll").read_bytes()
    # Bound one Linux env entry below MAX_ARG_STRLEN. No keyring/payload/password/JWT is encoded here.
    require(0 < len(content) <= 65536 and hashlib.sha256(content).hexdigest() == expected_sha256)
    return {"HFPOS_VERIFY_PROBE_BASE64": base64.b64encode(content).decode("ascii"),
            "HFPOS_VERIFY_PROBE_SHA256": expected_sha256}


def hidden_prompt(prompt):
    # getpass warns BEFORE falling back to raw_input with echo. Turn that path into STOP.
    with warnings.catch_warnings():
        warnings.simplefilter("error", getpass.GetPassWarning)
        return getpass.getpass(prompt)


def business_sample(db, tenant):
    if tenant is None:
        return None
    username = base64.b64encode(tenant["Username"].strip().encode()).decode("ascii")
    rows = decode(sql(db, 'SELECT coalesce(json_agg(sample),\'[]\') FROM (SELECT s."Id" id,s."CompanyId" "companyId",'
        's."EstablishmentId" "establishmentId",s."EmissionPointId" "emissionPointId",s."Total"::text total,'
        's."Subtotal"::text subtotal,s."TaxAmount"::text "taxAmount",s."Status" status,s."DocumentStatus" "documentStatus" '
        'FROM public."Sales" s JOIN public."Users" u ON u."CompanyId"=s."CompanyId" '
        'AND u."EstablishmentId"=s."EstablishmentId" AND u."EmissionPointId"=s."EmissionPointId" '
        "WHERE u.\"Username\"=convert_from(decode('" + username + "','base64'),'UTF8') ORDER BY s.\"Id\" LIMIT 1) sample;"))
    require(isinstance(rows, list) and len(rows) <= 1)
    return rows[0] if rows else None


def stop_owned_app(container_id, image_id, db_id, labels, env):
    # Never stop by mutable name, and never stop the DB. A mismatch requires human containment.
    require(container_id != db_id)
    item = inspect_app(container_id, container_id, image_id, db_id, labels, env)
    if item["State"].get("Running"):
        docker("kill", "--signal", "KILL", container_id, timeout=15)
        item = inspect_app(container_id, container_id, image_id, db_id, labels, env)
        require(not item["State"].get("Running"))


def execute_app(name, container_id, image_id, db_id, labels, env, body, before_start):
    try:
        item = inspect_app(name, container_id, image_id, db_id, labels, env)
        require(item["State"]["Status"] == "created")
        before_start()
        result = docker("start", "-a", "-i", container_id, data=body, timeout=180, check=False)
        require(result.returncode == 0)
        proof = decode(result.stdout)
        item = inspect_app(container_id, container_id, image_id, db_id, labels, env)
        require(item["State"]["Status"] == "exited" and item["State"]["ExitCode"] == 0)
        return proof
    except (Exception, KeyboardInterrupt):
        stop_owned_app(container_id, image_id, db_id, labels, env)
        raise


def key_snapshot(core, target):
    folder = core.root_path(target / "keyring-restored", mode=0o700)
    result, identity = {}, {}
    for file in folder.iterdir():
        core.root_path(file, False, 0o600)
        require(re.fullmatch(r"key-[a-zA-Z0-9-]+\.xml", file.name) and file.stat().st_size <= 65536)
        data = file.read_bytes()
        result[file.name] = base64.b64encode(data).decode("ascii")
        info = file.stat()
        identity[file.name] = (info.st_dev, info.st_ino, info.st_uid, stat.S_IMODE(info.st_mode), hashlib.sha256(data).hexdigest())
    require(0 < len(result) <= 128)
    return result, identity


def verify(args):
    require(os.name == "posix" and os.geteuid() == 0 and sys.stdin.isatty())
    root_path(HERE, mode=0o700)
    root_path(HERE / "docker-empty", mode=0o700)
    require(not any((HERE / "docker-empty").iterdir()))
    files = {n: root_path(HERE / n, False, 0o600).read_bytes() for n in FILES}
    source = decode(root_path(HERE / "source.json", False, 0o600).read_bytes())
    require(set(source) == {"source_sha", "code_sha256", "probe_sha256"})
    require(re.fullmatch(r"[a-f0-9]{40}", source["source_sha"]) and source["code_sha256"] == digest(files)
        and source["probe_sha256"] == hashlib.sha256(files[FILES[1]]).hexdigest())
    core, old_source = load_core(args.core_code)
    target = core.workspace(args.recovery_id)
    with core.workspace_lock(target):
        run_locked(args, core, target, old_source, source)


def run_locked(args, core, target, old_source, source):
    saved_bytes = core.root_path(target / "human-checkpoint.json", False, 0o600).read_bytes()
    saved = core.decode(saved_bytes)
    require(saved["stage"] == "core-restored-app-blocked")
    require(saved["labels"]["hfpos.human.source"] == old_source["source_sha"] and saved["labels"]["hfpos.human.code"] == core.digest_code())
    db = "hfpos-human-recovery-" + args.recovery_id
    def db_identity():
        require(core.inspect_manual(db, saved["labels"]) == saved["container_id"])
        item = decode(docker("inspect", db))[0]
        require(item["State"]["Running"] and item["Config"]["Image"] == core.IMAGE)
        require(item["Image"] == decode(docker("image", "inspect", core.IMAGE))[0]["Id"])
    db_identity()
    image_id = image_identity()
    keys, original_keys = key_snapshot(core, target)
    counts = decode(sql(db, 'SELECT json_build_array((SELECT count(*) FROM public."PlatformUsers"),(SELECT count(*) FROM public."Users"),(SELECT count(*) FROM public."Companies"),'
        '(SELECT count(*) FROM public."Sales")+(SELECT count(*) FROM public."PurchaseReceipts")+(SELECT count(*) FROM public."CashSessions")+'
        '(SELECT count(*) FROM public."CreditNotes")+(SELECT count(*) FROM public."ProductStocks"));'))
    require(len(counts) == 4 and all(type(n) is int and n >= 0 for n in counts))
    payloads = decode(sql(db, 'SELECT coalesce(json_agg("SmtpPasswordProtected"),\'[]\') FROM public."CompanyEmailSettings" WHERE "SmtpPasswordProtected" IS NOT NULL AND "SmtpPasswordProtected"<>\'\';'))
    require(len(payloads) <= 128)
    application = input("DataProtection ApplicationName confirmado por operador (public default HFPOS; no importar env): ").strip()
    require(0 < len(application) <= 200 and all(32 <= ord(c) < 127 for c in application))
    credentials = {}
    for scope, count in (("Platform", counts[0]), ("Tenant", counts[1])):
        credentials[scope] = None
        if count:
            credentials[scope] = {"Username": hidden_prompt(scope + " usuario existente (oculto): "),
                                  "Password": hidden_prompt(scope + " password existente (oculto): ")}
    business = business_sample(db, credentials["Tenant"])
    require(input("NUEVA autorizacion humana finita, agentes cerrados. Escriba VERIFICAR APP " + args.recovery_id + ": ") == "VERIFICAR APP " + args.recovery_id)
    nonce = secrets.token_hex(16)
    role, name = "hfpos_app_verify_" + nonce, "hfpos-app-verify-" + nonce
    state = target / "application-checkpoint.json"
    with state.open("xb") as file:
        os.fchmod(file.fileno(), 0o600)
        file.write(canonical({"stage": "started-no-retry", "source": source, "core_container_id": saved["container_id"],
                              "new_app": name, "new_role": role}))
        file.flush()
        os.fsync(file.fileno())
    before = fingerprint(db)
    create_reader(db, role)
    verify_reader(db, role)
    env = configuration(role, application)
    env.update(probe_environment(source["probe_sha256"]))
    labels = {"hfpos.application.verification": nonce, "hfpos.application.source": source["source_sha"]}
    require(not docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}").strip())
    command = ["create", "-i", "--pull=never", "--name", name, "--network", "container:" + saved["container_id"], "--read-only",
        "--user", "1654:1654", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--log-driver", "none",
        "--restart", "no", "--no-healthcheck", "--memory", "768m", "--memory-swap", "768m", "--cpus", "0.5", "--pids-limit", "128",
        "--tmpfs", "/keys:rw,noexec,nosuid,size=16m,uid=1654,gid=1654,mode=0700",
        "--tmpfs", "/tmp:rw,noexec,nosuid,size=32m,uid=1654,gid=1654,mode=0700", "--entrypoint", "/bin/sh"]
    for k, v in env.items():
        command += ["-e", k + "=" + v]
    for k, v in labels.items():
        command += ["--label", k + "=" + v]
    container_id = docker(*command, IMAGE, *probe_command()).decode().strip()
    require(re.fullmatch(r"[a-f0-9]{64}", container_id))
    with state.open("wb") as file:
        file.write(canonical({"stage": "created-no-retry", "source": source, "core_container_id": saved["container_id"],
                              "new_app": name, "new_role": role, "new_app_id": container_id}))
        file.flush()
        os.fsync(file.fileno())
    def before_start():
        db_identity()
        verify_reader(db, role)
    # The console proves both network families before it launches the actual backend child.
    body = canonical({"Keys": keys, "ApplicationName": application, "Payloads": payloads, "Business": business, **credentials})
    proof = execute_app(name, container_id, image_id, saved["container_id"], labels, env, body, before_start)
    require(proof.get("businessRead") in (("PASS_EXISTING_SALE", "PENDING_PERMISSION") if business else ("EMPTY",)))
    require(proof == {"network": "PASS_IPV4_IPV6", "keyring": "PASS_BYTES", "backend": "PASS_HTTP",
        "platform": "PASS_HTTP_LOGIN_ME" if counts[0] else "EMPTY", "tenant": "PASS_HTTP_LOGIN_ME" if counts[1] else "EMPTY",
        "protectedValue": "PASS_EXISTING_SMTP" if payloads else "NOT_PRESENT", "worker": "REGISTERED_DB_CLAIMS_DENIED",
        "authWrites": "NONE_ALLOWED", "businessRead": proof["businessRead"]})
    db_identity()
    verify_reader(db, role)
    require(before == fingerprint(db) and original_keys == key_snapshot(core, target)[1])
    require(saved_bytes == (target / "human-checkpoint.json").read_bytes())
    # Success-only cleanup: the exact new stopped app and its new role, never DB/keyring/core artifacts.
    docker("rm", container_id)
    sql(db, "DROP OWNED BY " + role + ";DROP ROLE " + role + ";")
    complete = bool(all(counts) and payloads and proof["businessRead"] == "PASS_EXISTING_SALE")
    evidence = {"stage": "verified" if complete else "partial-human-gate", "source": source, "image": IMAGE,
        "revision": REVISION, "proof": proof, "fingerprint": "ALL_TABLES_SEQUENCES_UNCHANGED", "core": "PRESERVED",
        "tenants": "PRESENT" if counts[2] else "EMPTY", "history": "PRESENT" if counts[3] else "EMPTY",
        "resources": "NEW_APP_AND_ROLE_REMOVED"}
    # Existing one-shot marker is retained even on a partial result; never reset for retry.
    with state.open("wb") as file:
        file.write(canonical(evidence))
        file.flush()
        os.fsync(file.fileno())
    print("APP_VERIFICATION_PASS" if complete else "APP_VERIFICATION_PARTIAL_HUMAN_GATE")
    print(canonical(evidence).decode(), end="")


def main(argv=None):
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="action", required=True)
    build = sub.add_parser("seal")
    for arg in ("source-sha", "probe-dll", "output"):
        build.add_argument("--" + arg, required=True)
    run = sub.add_parser("verify")
    run.add_argument("--core-code", required=True)
    run.add_argument("--recovery-id", required=True)
    args = parser.parse_args(argv)
    try:
        if args.action == "seal":
            seal(args)
        else:
            verify(args)
        return 0
    except (Exception, KeyboardInterrupt):
        print("STOP_APP_VERIFICATION_NO_RETRY_PRESERVE_CORE")
        return 1


if __name__ == "__main__":
    sys.exit(main())

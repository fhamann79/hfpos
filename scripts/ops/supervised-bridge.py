#!/usr/bin/env python3
"""Foreground human Windows bridge. Never launched by agents or as a service."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tarfile
import tempfile
import time
import zipfile


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


ops = module("supervised_ops", "supervised-operations.py")
archives = module("supervised_archive", "supervised-archive.py")
WORKFLOW = ".github/workflows/supervised-operations.yml"


def gh(path, body=None):
    args = ["gh", "api", "--method", "GET" if body is None else "POST", path]
    if body is not None:
        args += ["--input", "-"]
    return ops.native(args, None if body is None else ops.canonical(body), timeout=30)


def api(path):
    return ops.decode(gh("repos/" + ops.REPOSITORY + "/" + path), 1048576)


def validate_run(run, source_sha):
    ops.require(run["event"] == "workflow_dispatch" and run["head_branch"] == "main")
    ops.require(run["path"] == WORKFLOW and run["head_sha"] == source_sha)
    ops.require(run["run_attempt"] == 1 and run["status"] == "completed" and run["conclusion"] == "success")
    ops.require(run["repository"]["full_name"] == ops.REPOSITORY and
                run["head_repository"]["full_name"] == ops.REPOSITORY)


def read_request(data):
    ops.require(len(data) <= 65536)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        members = archive.infolist()
        ops.require(len(members) == 1 and members[0].filename == "request.json")
        ops.require(members[0].file_size <= 4096 and not members[0].flag_bits & 1)
        ops.require((members[0].external_attr >> 16) & 0o170000 != 0o120000)
        return ops.validate_request(ops.decode(archive.read(members[0])))


def download_request(run, config):
    validate_run(run, config["source_sha"])
    comparison = api("compare/" + config["source_sha"] + "...main")
    ops.require(comparison["status"] in ("identical", "ahead") and
                comparison["merge_base_commit"]["sha"] == config["source_sha"])
    listed = api("actions/runs/" + str(run["id"]) + "/artifacts?per_page=100")
    matches = [item for item in listed["artifacts"] if item["name"] == "supervised-request"]
    ops.require(len(matches) == 1 and not matches[0]["expired"] and matches[0]["size_in_bytes"] <= 65536)
    request = read_request(gh("repos/" + ops.REPOSITORY + "/actions/artifacts/" + str(matches[0]["id"]) + "/zip"))
    ops.require(request["run_id"] == str(run["id"]) and request["source_sha"] == run["head_sha"])
    ops.require(request["bundle_sha256"] == config["bundle_sha256"])
    return request


def validate_local_installation(directory):
    ops.require(os.name == "nt")
    archives.private_windows(directory)
    config_file = directory / "bridge.json"
    archives.private_windows(config_file)
    config = ops.decode(config_file.read_bytes())
    expected = {"version", "source_sha", "bundle_sha256", "agent_sid", "human_sid", "host", "port",
                "transport_key", "signing_key", "known_hosts", "age_identity", "ciphertext_directory"}
    ops.require(set(config) == expected and config["version"] == 1)
    sid = ops.native(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                      "[Security.Principal.WindowsIdentity]::GetCurrent().User.Value"], timeout=15).decode().strip()
    # ACLs under the agent's own Windows identity are not a custody boundary.
    ops.require(sid == config["human_sid"] and sid != config["agent_sid"])
    ops.require(re.fullmatch(r"S-1-5-[0-9-]+", config["agent_sid"]))
    ops.require(ops.hex_value(config["source_sha"], 40) and ops.hex_value(config["bundle_sha256"], 64))
    ops.require(ops.digest_bundle(directory) == config["bundle_sha256"])
    ops.require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.:-]{0,252}", config["host"]))
    ops.require(type(config["port"]) is int and 1 <= config["port"] <= 65535)
    for name in ("transport_key", "signing_key", "known_hosts", "age_identity", "ciphertext_directory"):
        path = Path(config[name])
        ops.require(path.is_absolute())
        ops.require('"' not in config[name] and '\n' not in config[name] and '\r' not in config[name])
        archives.private_windows(path)
    ops.require(config["signing_key"] != config["transport_key"] and config["signing_key"] != config["age_identity"])
    return config


def transfer_blob(export):
    manifest = ops.decode((export / "transfer.json").read_bytes(), 65536)
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for name in sorted(["transfer.json", *manifest["files"]]):
            content = (export / name).read_bytes()
            member = tarfile.TarInfo(name)
            member.size = len(content)
            member.mode = 0o600
            archive.addfile(member, io.BytesIO(content))
    blob = output.getvalue()
    ops.require(len(blob) <= 128 * 1024 * 1024)
    return blob, hashlib.sha256(ops.canonical(manifest)).hexdigest()


def ssh_command(config):
    null = "NUL" if os.name == "nt" else "/dev/null"
    known_hosts = config["known_hosts"].replace("\\", "/")
    return ["ssh", "-F", null, "-T", "-p", str(config["port"]), "-i", config["transport_key"],
            "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes", "-o", "IdentityAgent=none",
            "-o", "StrictHostKeyChecking=yes", "-o", 'UserKnownHostsFile="' + known_hosts + '"',
            "-o", "GlobalKnownHostsFile=" + null, "-o", "ClearAllForwardings=yes",
            "-o", "ForwardAgent=no", "-o", "PermitLocalCommand=no", "-o", "ProxyCommand=none",
            "-o", "ProxyJump=none", "-o", "ConnectTimeout=15", "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=2", "hfpos-supervised@" + config["host"], "hfpos-supervised-v1"]


def validate_report(value, request):
    # Rebuild an exact public schema; never forward arbitrary remote stdout.
    ops.require(isinstance(value, dict) and value.get("status") in ops.STATUSES)
    expected = ops.report(request, value["status"])
    ops.require(value == expected)
    return expected


def operate(request, config, private_directory):
    print(ops.canonical(request).decode("ascii"), end="")
    ops.require(input("Autorizar esta operacion completa (descifrar/transferir/restaurar si aplica): AUTORIZAR " +
                      request["run_id"] + ": ") == "AUTORIZAR " + request["run_id"])
    with archives.private_temporary(private_directory) as temporary:
        folder = Path(temporary)
        blob = b""
        manifest_hash = "0" * 64
        if request["action"] == "isolated-restore":
            ciphertext = Path(config["ciphertext_directory"]) / (request["recovery_id"] + ".age")
            archives.decrypt_export(ciphertext, Path(config["age_identity"]), folder, folder / "export",
                                    request["recovery_id"], request["ciphertext_sha256"])
            blob, manifest_hash = transfer_blob(folder / "export")
        plan = dict(request, expires=int(time.time()) + 300, nonce=secrets.token_hex(16),
                    transfer_size=len(blob), transfer_sha256=hashlib.sha256(blob).hexdigest(),
                    transfer_manifest_sha256=manifest_hash)
        print(ops.canonical(plan).decode("ascii"), end="")
        ops.require(input("Aprobacion humana exacta: escriba FIRMAR " + plan["nonce"] + ": ") == "FIRMAR " + plan["nonce"])
        plan_file = folder / "plan.json"
        plan_file.write_bytes(ops.canonical(plan))
        # Signing may require passphrase/FIDO touch on the human's terminal. No key
        # value or tool stderr is captured into public reports.
        signed = subprocess.run(["ssh-keygen", "-Y", "sign", "-f", config["signing_key"],
                                 "-n", ops.NAMESPACE, str(plan_file)], stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, timeout=120)
        ops.require(signed.returncode == 0)
        signature = Path(str(plan_file) + ".sig").read_text(encoding="ascii")
        ops.validate_plan(plan)
        envelope = {"plan": plan, "signature": signature}
        try:
            remote = subprocess.run(ssh_command(config), input=ops.canonical(envelope) + blob,
                                    stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=930)
            if remote.returncode != 0:
                return ops.report(request, "UNCERTAIN")
            return validate_report(ops.decode(remote.stdout, 4096), request)
        except (subprocess.SubprocessError, OSError, ValueError, ops.Denied):
            return ops.report(request, "UNCERTAIN")


def publish_report(result):
    result = validate_report(result, {key: result[key] for key in ops.REQUEST_KEYS})
    body = "DEV-531 HUMAN_CONTROLLED mediated\n```json\n" + ops.canonical(result).decode("ascii") + "```"
    gh("repos/" + ops.REPOSITORY + "/issues/133/comments", {"body": body})


def main():
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--foreground", action="store_true")
    mode.add_argument("--check-installation", action="store_true")
    args = parser.parse_args()
    try:
        directory = Path(os.path.abspath(__file__)).parent
        config = validate_local_installation(directory)
        if args.check_installation:
            print("LOCAL_INSTALLATION_PASS_EFFECTIVE_AGENT_DENIAL_HUMAN_GATE_PENDING")
            return 0
        ledger = directory / "handled.json"
        handled = ops.decode(ledger.read_bytes()) if ledger.exists() else {}
        print("Puente humano activo. Ctrl+C para detener. App-start no disponible.")
        while True:
            for run_id, result in list(handled.items()):
                if isinstance(result, dict) and result.get("reported") is False:
                    try:
                        publish_report(result["report"])
                        result["reported"] = True
                        ledger.write_bytes(ops.canonical(handled))
                    except Exception:
                        pass
            runs = api("actions/workflows/supervised-operations.yml/runs?event=workflow_dispatch&per_page=20")["workflow_runs"]
            for run in reversed(runs):
                run_id = str(run["id"])
                if run_id in handled or run["status"] != "completed":
                    continue
                try:
                    request = download_request(run, config)
                    # Mark before prompting/executing. Even network/timeout failures
                    # require a NEW GitHub request and a NEW human approval.
                    handled[run_id] = "consumed"
                    ledger.write_bytes(ops.canonical(handled))
                    result = operate(request, config, directory)
                    handled[run_id] = {"report": result, "reported": False}
                    ledger.write_bytes(ops.canonical(handled))
                    publish_report(result)
                    handled[run_id]["reported"] = True
                    ledger.write_bytes(ops.canonical(handled))
                    print(ops.canonical(result).decode("ascii"), end="")
                except Exception:
                    print("Solicitud rechazada o reporte pendiente; no se reejecuta la operacion.")
            time.sleep(30)
    except KeyboardInterrupt:
        return 0
    except Exception:
        print("BRIDGE_DENIED: instalacion/custodia humana no verificada.")
        return 1


if __name__ == "__main__":
    sys.exit(main())

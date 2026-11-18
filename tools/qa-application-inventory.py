"""Versioned local application candidate inventory, never a migration authorization."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = "docs/deployment/qa-application-source-manifest-2026-11-17.json"
PREVIOUS = "docs/deployment/neon-qa-source-manifest-runner-preflight-fix.json"
FROZEN = "docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-manifest.json"
PROGRAM = "src/backend/GameLibrary.Api/Program.cs"
ADDITIONS = {
    "src/backend/GameLibrary.Api/CoverUploads/CoverCleanupConfiguration.cs",
    "tests/backend/GameLibrary.IntegrationTests/PrivateQaCleanupConfigurationTests.cs",
    "tests/backend/GameLibrary.IntegrationTests/PrivateQaJwtTests.cs",
    "deploy/qa/Dockerfile.application",
    "deploy/qa/Dockerfile.application.dockerignore",
    "deploy/qa/openssl-tls13.cnf",
    "deploy/qa/application-environment-plan.json",
    "deploy/qa/check-application-plan.py",
    "docs/architecture/private-qa-application-amendment.md",
    "docs/deployment/qa-runtime-role-plan.md",
    "docs/deployment/qa-application-preparation.md",
    "tools/qa-application-inventory.py",
}
EXCLUDED = {
    "opencode.json": "Local agent/provider configuration; do not publish",
    "render.yaml": "Retired hosting input; do not publish or deploy",
    MANIFEST: "Self-reference excluded; record this manifest's hash separately",
}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def record(name):
    path = ROOT / name
    for parent in [path, *path.parents]:
        if parent == ROOT:
            break
        if parent.is_symlink():
            raise ValueError("Candidate inputs must not use symlinks")
    if not path.resolve().is_relative_to(ROOT):
        raise ValueError("Candidate input outside repository")
    data = path.read_bytes()
    return {"path": name, "bytes": len(data), "mode": "100755" if path.stat().st_mode & 0o111 else "100644",
            "sha256": hashlib.sha256(data).hexdigest(),
            "gitBlobSha1": hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()}


def build():
    prior_bytes = (ROOT / PREVIOUS).read_bytes()
    frozen_bytes = (ROOT / FROZEN).read_bytes()
    if hashlib.sha256(prior_bytes).hexdigest() != "a5ac783d2731812c01487f50d56d37f8956338cc1907ca1144d173d6bbfad0b4":
        raise ValueError("Historical runner inventory changed")
    if hashlib.sha256(frozen_bytes).hexdigest() != "73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb":
        raise ValueError("Historical frozen manifest changed")
    prior = {e["path"]: e for e in json.loads(prior_bytes)["entries"]}
    expected = set(prior) | {PREVIOUS} | ADDITIONS
    eligible = {p.decode() for p in git("ls-files", "-z", "--cached", "--others", "--exclude-standard").split(b"\0") if p}
    if eligible - set(EXCLUDED) != expected:
        raise ValueError("Unreviewed candidate path set; review new/removed files before generating inventory")
    head_blobs = {}
    for row in git("ls-tree", "-r", "-z", "HEAD").split(b"\0"):
        if row:
            meta, path = row.split(b"\t", 1)
            head_blobs[path.decode()] = meta.split()[2].decode()
    index_blobs = {}
    for row in git("ls-files", "--stage", "-z").split(b"\0"):
        if row:
            meta, path = row.split(b"\t", 1)
            _, blob, stage = meta.split()
            if stage != b"0":
                raise ValueError("Resolve unmerged source separately")
            index_blobs[path.decode()] = blob.decode()
    entries = []
    for name in sorted(expected):
        entry = record(name)
        if name in prior and name != PROGRAM:
            if any(entry[k] != prior[name][k] for k in ["bytes", "mode", "sha256"]):
                raise ValueError("Unexpected delta to previously reviewed source: " + name)
        entry.update(origin="local-qa-addition" if name in ADDITIONS else "approved-qa-cleanup-delta" if name == PROGRAM else "previous-reviewed-input",
                     headBlob=head_blobs.get(name), indexBlob=index_blobs.get(name), untracked=name not in index_blobs,
                     hasStagedChanges=index_blobs.get(name) != head_blobs.get(name),
                     hasUnstagedChanges=name in index_blobs and index_blobs[name] != entry["gitBlobSha1"])
        entries.append(entry)
    program = next(e for e in entries if e["path"] == PROGRAM)
    return {
        "status": "local-application-candidate-awaiting-source-release-approval",
        "date": "2026-11-17", "parentCommit": git("rev-parse", "HEAD").decode().strip(),
        "previousInventoryPath": PREVIOUS, "previousInventorySha256": hashlib.sha256(prior_bytes).hexdigest(),
        "frozenManifestPath": FROZEN, "frozenManifestSha256": hashlib.sha256(frozen_bytes).hexdigest(),
        "historicalFrozenSourceArchiveSha256": "b3f2f41a9de588b06f8c28e84398c359522e0ebdc6327f01b1ce1afcdfabb455",
        "modifiedPreviouslyReviewedInputs": [{"path": PROGRAM, "beforeSha256": prior[PROGRAM]["sha256"], "afterSha256": program["sha256"]}],
        "selectedFileCount": len(entries), "entries": entries,
        "excluded": [{"path": p, "reason": reason} for p, reason in EXCLUDED.items()],
        "migrationExecutionApproved": False, "gitPublicationApproved": False, "deploymentApproved": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Create a NEW manifest only; never overwrite historical evidence")
    args = parser.parse_args()
    candidate = build()
    path = ROOT / MANIFEST
    if args.write:
        with path.open("x") as output:
            json.dump(candidate, output, indent=2)
            output.write("\n")
    elif json.loads(path.read_text()) != candidate:
        raise ValueError("Candidate manifest differs from current source/index identity")
    print(f"PASS {candidate['selectedFileCount']} candidate files, exact paths/bytes/modes/index identities; no publication or execution authority.")
    print("Candidate manifest SHA256:", hashlib.sha256(path.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()

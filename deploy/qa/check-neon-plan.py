"""Offline plan/inventory checks. No cloud, credentials, application host or SQL."""

import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[2]
FROZEN = "docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-manifest.json"
PREVIOUS_MANIFEST = "docs/deployment/neon-qa-source-manifest.json"
MANIFEST = "docs/deployment/neon-qa-source-manifest-discovery.json"
EXISTING_COMPANIONS = (
    ".config/dotnet-tools.json",
    "deploy/qa/supabase-project-plan.json",
    "deploy/qa/vercel-personal-project-plan.json",
    "deploy/qa/vercel-project-plan.json",
    "docs/deployment/qa-ef-tooling-alignment.md",
    "docs/deployment/qa-postgres-connectivity-preflight.md",
    "docs/deployment/qa-pre-migration-verification.md",
    "docs/deployment/qa-real-session-pooler-validation.md",
    "docs/deployment/qa-security-application.md",
    "docs/deployment/qa-security-snapshot-review.md",
    "docs/deployment/snapshots/private-qa-2026-11-06/README.md",
    "docs/deployment/snapshots/private-qa-2026-11-06/SHA256SUMS",
    "docs/deployment/snapshots/private-qa-2026-11-06/applied-verification.json",
    "docs/deployment/snapshots/private-qa-2026-11-06/dependency-candidate.patch",
    "docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-manifest.json",
    "docs/deployment/snapshots/private-qa-2026-11-06/proposed-snapshot-summary.json",
    "docs/deployment/snapshots/private-qa-2026-11-06/secret-scan-review.json",
    "docs/deployment/supabase-existing-project-qa-readiness.md",
    "docs/deployment/vercel-personal-qa-readiness.md",
    "docs/deployment/vercel-workspace-qa-readiness.md",
    PREVIOUS_MANIFEST,
)
NEW_COMPANIONS = (
    "docs/architecture/private-qa-neon-amendment.md",
    "docs/deployment/README.md",
    "docs/deployment/neon-qa-plan.md",
    "docs/deployment/neon-qa-source-review.md",
    "docs/deployment/neon-qa-migration-strategy.md",
    "docs/deployment/neon-qa-cleanup-review.md",
    "deploy/qa/neon-project-plan.json",
    "deploy/qa/neon-qa-environment-plan.json",
    "deploy/qa/check-neon-plan.py",
    "docs/deployment/neon-qa-project-discovery.md",
)
EXCLUDED = {
    "opencode.json": "Local agent/provider configuration; do not publish",
    "render.yaml": "Retired Render blueprint; preserve as local historical input",
    MANIFEST: "This candidate inventory; identified separately to avoid self-hashing",
}


def read_json(path):
    return json.loads((ROOT / path).read_text())


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def file_record(name):
    path = ROOT / name
    if path.is_symlink() or not path.resolve().is_relative_to(ROOT):
        raise ValueError("Source inventory must not follow links outside its input")
    data = path.read_bytes()
    return {
        "path": name,
        "mode": "100755" if path.stat().st_mode & 0o111 else "100644",
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "gitBlobSha1": hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest(),
    }


def build_manifest():
    frozen = {e["path"]: e for e in read_json(FROZEN)["entries"]}
    selected = set(frozen) | set(EXISTING_COMPANIONS) | set(NEW_COMPANIONS)
    head_blobs = {}
    for record in git("ls-tree", "-r", "-z", "HEAD").split(b"\0"):
        if record:
            meta, path = record.split(b"\t", 1)
            head_blobs[path.decode()] = meta.split()[2].decode()
    index_blobs = {}
    for record in git("ls-files", "--stage", "-z").split(b"\0"):
        if record:
            meta, path = record.split(b"\t", 1)
            mode, blob, stage = meta.split()
            if stage != b"0":
                raise ValueError("Unmerged index requires owner review")
            index_blobs[path.decode()] = blob.decode()
    entries = []
    for name in sorted(selected):
        entry = file_record(name)
        entry.update({
            "origin": "frozen-407" if name in frozen else (
                "existing-companion" if name in EXISTING_COMPANIONS else "new-neon-plan-companion"),
            "headBlob": head_blobs.get(name),
            "indexBlob": index_blobs.get(name),
            "untracked": name not in index_blobs,
            "hasStagedChanges": index_blobs.get(name) != head_blobs.get(name),
            "hasUnstagedChanges": name in index_blobs and index_blobs[name] != entry["gitBlobSha1"],
        })
        entries.append(entry)
    return {
        "status": "candidate-review-only-not-release-approved",
        "revision": "owner-created-target-discovery-2026-11-15",
        "date": "2026-11-15",
        "parentCommit": git("rev-parse", "HEAD").decode().strip(),
        "frozenManifestPath": FROZEN,
        "frozenManifestSha256": hashlib.sha256((ROOT / FROZEN).read_bytes()).hexdigest(),
        "selectedFileCount": len(entries),
        "entries": entries,
        "excluded": [{"path": p, "reason": reason} for p, reason in EXCLUDED.items()],
        "executionApproved": False,
        "gitPublicationApproved": False,
        "futureCleanupOrGuardImplementationIncluded": False,
        "discoveredTargetConfigurationApprovedForDeployment": False,
    }


class NeonPlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = read_json("deploy/qa/neon-project-plan.json")
        cls.env = read_json("deploy/qa/neon-qa-environment-plan.json")

    def test_owner_creation_does_not_authorize_further_writes(self):
        self.assertTrue(self.plan["ownerApprovedTopology"])
        self.assertTrue(self.plan["ownerApprovedCreation"])
        self.assertFalse(self.plan["createAnotherProject"])
        self.assertFalse(self.plan["applied"])
        self.assertTrue(all(v is False for v in self.plan["executionPolicy"].values()))

    def test_historical_postgres17_proposal_is_not_silently_rewritten(self):
        request = self.plan["proposedCreateProjectInput"]
        self.assertEqual(request["org_id"], "org-falling-fog-81506005")
        self.assertEqual(request["name"], "game-library-qa")
        self.assertEqual(request["pg_version"], 17)
        self.assertEqual(request["region_id"], "aws-us-west-2")
        self.assertEqual(request["branch"], {
            "name": "qa", "database_name": "game_library_qa", "role_name": "game_library_qa_owner"})
        self.assertEqual(request["default_endpoint_settings"], {
            "autoscaling_limit_min_cu": 0.25, "autoscaling_limit_max_cu": 0.25,
            "suspend_timeout_seconds": 300})
        self.assertEqual(request["history_retention_seconds"], 21600)
        self.assertEqual(set(request), {"org_id", "name", "pg_version", "region_id", "branch",
                                       "default_endpoint_settings", "history_retention_seconds", "settings"})

    def test_exact_discovered_target_and_unverified_minor_runtime_role(self):
        target = self.plan["returnedTarget"]
        self.assertEqual(target["projectId"], "super-fire-30188358")
        self.assertEqual(target["projectName"], "GameLibraryQa")
        self.assertEqual(target["organizationId"], "org-falling-fog-81506005")
        self.assertEqual(target["branchId"], "br-muddy-base-be5m73fj")
        self.assertEqual(target["branchName"], "production")
        self.assertEqual(target["computeId"], "ep-wispy-cherry-beqkq4cc")
        self.assertEqual(target["databaseName"], "neondb")
        self.assertEqual(target["databaseProviderId"], 612895)
        self.assertEqual(target["ownerRoleName"], "neondb_owner")
        self.assertEqual(target["directHost"], "ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech")
        self.assertIsNone(target["runtimeRoleName"])
        self.assertIsNone(target["runningPostgresVersion"])
        bootstrap = self.plan["expectedAutomaticResources"]
        self.assertTrue(all(v == 1 for k, v in bootstrap.items() if k.endswith("Count")))
        self.assertFalse(bootstrap["additionalBranchesDatabasesOrRolesApproved"])
        self.assertFalse(self.plan["creationInputReview"]["providerAcceptanceOfCompleteInputVerified"])

    def test_observed_differences_are_not_deployment_approval(self):
        observed = self.plan["discovery"]
        self.assertTrue(observed["readOnlyMetadataVerified"])
        self.assertEqual(observed["organizationPlan"], "free")
        self.assertEqual(observed["postgresMajorFromApi"], 18)
        self.assertEqual(observed["regionId"], "aws-us-east-2")
        self.assertEqual(observed["autoscalingLimitMinCu"], 0.25)
        self.assertEqual(observed["autoscalingLimitMaxCu"], 2)
        self.assertFalse(observed["fixedPoint25Cu"])
        self.assertFalse(observed["matchesApprovedDeploymentConfiguration"])
        for key in ["sqlOrApplicationDataInspected", "emptyApplicationSchemaVerified",
                    "credentialsRetrieved", "databaseConnectivityVerified", "runningMinorVersionQueried",
                    "neonDataApiAndOtherServiceExposureVerified"]:
            self.assertFalse(observed[key], key)
        differences = {d["setting"]: d for d in self.plan["differencesFromHistoricalProposal"]}
        self.assertEqual(len(differences), 7)
        self.assertEqual(differences["postgresMajor"], {"setting": "postgresMajor", "proposed": 17, "observed": 18})
        self.assertEqual(differences["autoscalingLimitMaxCu"]["observed"], 2)

    def test_zero_suspend_setting_is_plan_default_not_immediate_suspend(self):
        observed = self.plan["discovery"]
        self.assertEqual(observed["suspendTimeoutSecondsRaw"], 0)
        self.assertEqual(observed["scaleToZeroPolicySeconds"], 300)
        self.assertEqual(observed["scaleToZeroPolicySource"], "https://neon.com/docs/guides/scale-to-zero-guide")

    def test_protected_targets_cannot_be_reused(self):
        isolation = self.plan["isolation"]
        self.assertTrue(isolation["dedicatedProjectRequired"])
        self.assertTrue(isolation["existingOwnerCreatedProjectDiscovered"])
        self.assertFalse(isolation["cloneOrReuseExistingProjectBranchDatabaseDataRolesOrCredentials"])
        self.assertEqual(set(isolation["protectedNeonProjectIds"]),
                         {"nameless-cherry-10121327", "old-violet-31409208"})
        self.assertEqual(isolation["protectedSupabaseDevelopmentReference"], "iramzxpjbnldhebykzhx")
        self.assertEqual(isolation["authOnlySupabaseQaReference"], "yqngxhryzklrhhryzzoi")
        self.assertNotIn(self.plan["returnedTarget"]["projectId"], isolation["protectedNeonProjectIds"])

    def test_next_gate_requires_actual_target_confirmation_and_readonly_approval(self):
        approval = self.plan["nextApproval"]
        self.assertTrue(approval["ownerConfirmationOfActualTargetPending"])
        self.assertFalse(approval["readOnlyDatabaseVerificationCurrentlyApproved"])
        self.assertTrue(approval["allowReadOnlyDatabaseMetadataQueriesOnlyAfterApproval"])
        self.assertTrue(approval["stopAfterReadOnlyConnectivityReport"])
        self.assertTrue(approval["readOnlyProbeApprovalWouldNotApprovePostgres18RegionOrAutoscalingForDeployment"])
        self.assertTrue(all(v is False for k, v in approval.items()
                            if k.startswith("allow") and "ReadOnly" not in k))

    def test_direct_verified_tls_and_separate_operator(self):
        policy = self.plan["connectionPolicy"]
        self.assertEqual(policy["sslMode"], "VerifyFull")
        self.assertEqual(policy["port"], 5432)
        self.assertEqual(policy["maximumRuntimePoolSize"], 5)
        self.assertFalse(policy["runtimeUsesMigrationOwner"])
        self.assertEqual(self.env["operatorPolicy"]["mapQaMigrationConnectionTo"], "ConnectionStrings__Default")
        self.assertFalse(self.env["operatorPolicy"]["operatorRunnerImplemented"])

    def test_qa_auth_and_current_algorithms_are_not_misrepresented(self):
        auth = self.env["authPolicy"]
        self.assertEqual(auth["expectedQaTokenAlgorithm"], "ES256")
        self.assertEqual(auth["currentApiAllowedAlgorithms"], ["ES256", "RS256"])
        self.assertFalse(auth["es256OnlyEnvironmentOverrideExists"])
        self.assertFalse(auth["configureSupabaseAuthSettingsInThisTask"])
        self.assertEqual(auth["expectedIssuer"], "https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1")

    def test_runtime_variables_match_frozen_safety_defaults(self):
        base = read_json("deploy/qa/railway-service-plan.json")["variables"]
        variables = self.env["railwayPublicVariables"]
        for name, value in base.items():
            self.assertEqual(variables[name], value, name)
        self.assertEqual(set(variables) - set(base), {"Authentication__Schemes__Bearer__MetadataAddress"})
        self.assertEqual(self.env["railwayUnassignedPublicVariables"], ["AllowedOrigins__0"])
        self.assertEqual(self.env["railwaySecretVariableNamesOnly"], ["ConnectionStrings__Default"])
        self.assertFalse(set(variables) & set(self.env["forbiddenQaRuntimeVariableNames"]))

    def test_frontend_has_only_public_allowlisted_names(self):
        names = set(self.env["vercelAssignedPublicVariables"]) | set(self.env["vercelUnassignedPublicVariableNames"])
        self.assertEqual(names, {"QA_API_BASE_URL", "QA_SUPABASE_URL", "QA_SUPABASE_PUBLISHABLE_KEY"})
        self.assertEqual(self.env["vercelAssignedPublicVariables"]["QA_SUPABASE_URL"],
                         "https://yqngxhryzklrhhryzzoi.supabase.co")
        self.assertFalse(names & set(self.env["railwaySecretVariableNamesOnly"]))
        self.assertFalse(self.env["remoteVariableInstallationApproved"])

    def test_cleanup_optout_is_not_installed_or_implemented(self):
        name = "PrivateQa__DisableCoverCleanup"
        proposed = self.env["proposedUnsupportedVariablesDoNotInstall"][name]
        self.assertFalse(proposed["supportedByCurrentCode"])
        self.assertFalse(proposed["implementationApproved"])
        self.assertNotIn(name, self.env["railwayPublicVariables"])
        program = (ROOT / "src/backend/GameLibrary.Api/Program.cs").read_text()
        self.assertNotIn("PrivateQa:DisableCoverCleanup", program)
        self.assertIn("AddHostedService<CoverCleanupService>()", program)

    def test_frozen_bytes_modes_and_manifest_are_unchanged(self):
        frozen = read_json(FROZEN)
        self.assertEqual(len(frozen["entries"]), 407)
        self.assertEqual(hashlib.sha256((ROOT / FROZEN).read_bytes()).hexdigest(),
                         "73899e7fd9732884421edefe0dbb0c65edfaa017655b3868d7286d084437e1fb")
        for entry in frozen["entries"]:
            current = file_record(entry["path"])
            self.assertEqual(current["sha256"], entry["sha256"], entry["path"])
            self.assertEqual(current["mode"], entry["mode"], entry["path"])

    def test_all_eleven_migrations_remain_present(self):
        directory = ROOT / "src/backend/GameLibrary.Core/Data/Migrations"
        migrations = sorted(p.stem for p in directory.glob("*.cs")
                            if not p.name.endswith(".Designer.cs") and p.name != "GameLibraryDbContextModelSnapshot.cs")
        self.assertEqual(len(migrations), 11)
        self.assertEqual(migrations[-1], "20261029134813_AddCoverUploads")
        self.assertEqual(read_json(".config/dotnet-tools.json")["tools"]["dotnet-ef"]["version"], "10.0.11")

    def test_candidate_manifest_matches_every_selected_input(self):
        candidate = read_json(MANIFEST)
        self.assertEqual(candidate, build_manifest())
        self.assertEqual(candidate["selectedFileCount"], 407 + len(EXISTING_COMPANIONS) + len(NEW_COMPANIONS))
        self.assertFalse(candidate["gitPublicationApproved"])
        self.assertFalse(candidate["executionApproved"])

    def test_original_candidate_manifest_is_preserved_as_historical_evidence(self):
        self.assertEqual(hashlib.sha256((ROOT / PREVIOUS_MANIFEST).read_bytes()).hexdigest(),
                         "c85c859bd1396dcb881b259cdaa209d8b015e60c575e972373da9ea6bca64c26")
        updated_companions = {
            "deploy/qa/neon-project-plan.json", "deploy/qa/check-neon-plan.py",
            "docs/deployment/neon-qa-plan.md", "docs/deployment/README.md",
            "docs/deployment/neon-qa-source-review.md", "docs/deployment/neon-qa-migration-strategy.md",
        }
        for entry in read_json(PREVIOUS_MANIFEST)["entries"]:
            if entry["path"] not in updated_companions:
                self.assertEqual(file_record(entry["path"])["sha256"], entry["sha256"], entry["path"])

    def test_no_unreviewed_git_eligible_path_is_silently_selected(self):
        actual = {p.decode() for p in git("ls-files", "-z", "--cached", "--others", "--exclude-standard").split(b"\0") if p}
        selected = {e["path"] for e in read_json(MANIFEST)["entries"]}
        self.assertEqual(actual, selected | set(EXCLUDED))
        self.assertFalse(selected & set(EXCLUDED))

    def test_new_document_relative_links_resolve(self):
        for name in NEW_COMPANIONS:
            if not name.endswith(".md"):
                continue
            path = ROOT / name
            for target in re.findall(r"\]\(([^)]+)\)", path.read_text()):
                if "://" not in target and not target.startswith("#"):
                    self.assertTrue((path.parent / target.split("#", 1)[0]).exists(), f"{name}: {target}")


if __name__ == "__main__":
    if sys.argv[1:] == ["--write-manifest"]:
        # Generation only, never overwrite an existing review artifact.
        manifest = build_manifest()
        with (ROOT / MANIFEST).open("x") as output:
            json.dump(manifest, output, indent=2)
            output.write("\n")
        print(f"Created {MANIFEST}: {manifest['selectedFileCount']} candidate input files; not publication approval")
    else:
        unittest.main()

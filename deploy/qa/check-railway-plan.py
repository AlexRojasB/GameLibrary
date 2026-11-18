"""Offline regression checks only. Never contacts Railway or reads credentials."""

import json
from pathlib import Path
import re
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[2]
PLAN = json.loads((ROOT / "deploy/qa/railway-service-plan.json").read_text())


class RailwayPlanTests(unittest.TestCase):
    def test_approval_and_dedicated_private_project(self):
        self.assertEqual(PLAN["status"], "approval-required")
        self.assertEqual(PLAN["project"]["name"], "game-library-qa")
        self.assertEqual(PLAN["project"]["defaultEnvironmentName"], "qa")
        self.assertFalse(PLAN["project"]["isPublic"])
        self.assertFalse(PLAN["project"]["prDeploys"])
        self.assertNotIn("projectId", PLAN)
        self.assertNotIn("serviceId", PLAN)

    def test_root_context_and_dockerfile(self):
        self.assertEqual(PLAN["settings"]["rootDirectory"], "/")
        self.assertEqual(PLAN["settings"]["dockerfilePath"], "deploy/qa/Dockerfile")
        self.assertEqual(PLAN["variables"]["RAILWAY_DOCKERFILE_PATH"], "deploy/qa/Dockerfile")

    def test_native_runtime_pins_and_publish(self):
        docker = (ROOT / "deploy/qa/Dockerfile").read_text()
        stages = re.findall(r"^FROM (.+)$", docker, re.MULTILINE)
        self.assertEqual(len(stages), 2)
        for stage, family in zip(stages, ["sdk", "aspnet"]):
            self.assertRegex(stage, rf"^--platform=linux/amd64 mcr\.microsoft\.com/dotnet/{family}:10\.0-noble@sha256:[a-f0-9]{{64}}(?: AS build)?$")
        self.assertIn("--locked-mode", docker)
        self.assertIn("-r linux-x64 --self-contained false --no-restore", docker)
        self.assertIn("-p:PublishTrimmed=false -p:PublishSingleFile=false", docker)
        self.assertIn("USER $APP_UID", docker)
        self.assertIn('ENTRYPOINT ["dotnet", "GameLibrary.Api.dll"]', docker)
        self.assertNotRegex(docker, r"(?m)^ARG ")

    def test_port_health_and_shutdown_agree(self):
        variables = PLAN["variables"]
        self.assertEqual(variables["PORT"], "10000")
        self.assertEqual(variables["ASPNETCORE_HTTP_PORTS"], variables["PORT"])
        self.assertEqual(PLAN["publicNetworking"]["targetPort"], int(variables["PORT"]))
        self.assertEqual(PLAN["settings"]["healthcheckPath"], "/health")
        self.assertEqual(PLAN["settings"]["healthcheckTimeout"], 300)
        self.assertEqual(PLAN["settings"]["drainingSeconds"], 60)
        self.assertEqual(variables["RAILWAY_DEPLOYMENT_DRAINING_SECONDS"], "60")

    def test_bounded_single_replica_without_new_infrastructure(self):
        self.assertEqual(PLAN["settings"]["limits"], {"memoryGB": 0.5, "vCPUs": 0.5})
        self.assertEqual(PLAN["settings"]["regions"], {"sfo": {"numReplicas": 1}})
        self.assertEqual(PLAN["settings"]["overlapSeconds"], 0)
        self.assertEqual(PLAN["settings"]["restartPolicyType"], "ON_FAILURE")
        self.assertEqual(PLAN["settings"]["restartPolicyMaxRetries"], 3)
        self.assertFalse(PLAN["settings"]["sleepApplication"])
        self.assertFalse(PLAN["settings"]["cdn"]["enabled"])
        self.assertEqual(PLAN["additionalResources"], [])

    def test_production_auth_and_disabled_uploads(self):
        variables = PLAN["variables"]
        self.assertEqual(variables["ASPNETCORE_ENVIRONMENT"], "Production")
        self.assertEqual(variables["Authentication__Schemes__Bearer__RequireHttpsMetadata"], "true")
        self.assertEqual(variables["Authentication__Schemes__Bearer__TokenValidationParameters__ValidAudiences__0"], "authenticated")
        switches = ["CoverUploads__Enabled", "CoverUploads__AllowLocalHttp", "SteamIntegration__Enabled"]
        switches += [name for name in variables if name.startswith("E2E__")]
        for name in switches:
            self.assertEqual(variables[name], "false", name)
            self.assertIn(f"{name}=false", (ROOT / "deploy/qa/Dockerfile").read_text())

    def test_missing_target_values_are_not_invented_or_secret(self):
        variables = PLAN["variables"]
        self.assertEqual(PLAN["requiredSecretVariableNames"], ["ConnectionStrings__Default"])
        self.assertEqual(set(PLAN["requiredPublicVariableNames"]), {
            "AllowedOrigins__0", "Authentication__Schemes__Bearer__MetadataAddress"})
        for name in PLAN["requiredSecretVariableNames"] + PLAN["requiredPublicVariableNames"]:
            self.assertNotIn(name, variables)
        self.assertFalse(any("Storage" in name for name in variables))
        self.assertNotIn("QA_MIGRATION_CONNECTION", variables)

    def test_no_startup_migrations_or_moving_source(self):
        self.assertEqual(PLAN["settings"]["preDeployCommand"], [])
        self.assertNotIn("startCommand", PLAN["settings"])
        self.assertTrue(PLAN["source"]["requireCommitSha"])
        self.assertFalse(PLAN["source"]["automaticDeploys"])
        self.assertNotIn("branch", PLAN["source"])
        self.assertNotIn("railwayConfigFile", PLAN["settings"])

    def test_railway_origin_is_accepted_by_existing_public_build_allowlist(self):
        result = subprocess.run([
            "node", "--input-type=module", "-e",
            "import { qaEnvironment } from './src/frontend/scripts/build-qa.mjs';"
            "const result = qaEnvironment({"
            "QA_API_BASE_URL:'https://synthetic-qa.up.railway.app',"
            "QA_SUPABASE_URL:'https://abcdefghijklmnopqrst.supabase.co',"
            "QA_SUPABASE_PUBLISHABLE_KEY:'sb_publishable_synthetic_railway_check'});"
            "if(result.apiBaseUrl !== 'https://synthetic-qa.up.railway.app' || result.e2e.enabled) process.exit(1);"
        ], cwd=ROOT, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, "Existing QA build must accept an HTTPS Railway origin")

    def test_context_and_frontend_boundaries(self):
        ignore = (ROOT / "deploy/qa/Dockerfile.dockerignore").read_text()
        for rule in ["**", "**/bin", "**/obj", "**/.env*", "**/*.pem", "**/*.key"]:
            self.assertIn(rule, ignore.splitlines())
        vercel = json.loads((ROOT / "src/frontend/vercel.json").read_text())
        self.assertFalse(vercel["git"]["deploymentEnabled"])
        self.assertEqual(vercel["buildCommand"], "npm run build:qa")
        self.assertEqual(vercel["outputDirectory"], "dist/game-library/browser")
        self.assertEqual(json.loads((ROOT / "src/frontend/ngsw-config.json").read_text()).get("dataGroups", []), [])


if __name__ == "__main__":
    unittest.main()

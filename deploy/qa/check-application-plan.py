"""Offline checks for the new QA application candidate; no host/cloud/DB startup."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ApplicationPlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = json.loads((ROOT / "deploy/qa/application-environment-plan.json").read_text())

    def test_exact_accepted_target_and_separate_uncreated_runtime_role(self):
        db = self.plan["database"]
        self.assertEqual(db["projectId"], "super-fire-30188358")
        self.assertEqual(db["branchId"], "br-muddy-base-be5m73fj")
        self.assertEqual(db["host"], "ep-wispy-cherry-beqkq4cc.c-9.us-east-2.aws.neon.tech")
        self.assertEqual((db["database"], db["postgresVersion"], db["computeCu"]), ("neondb", "18.7", [0.25, 2]))
        self.assertNotEqual(db["migrationRole"], db["proposedRuntimeRole"])
        self.assertFalse(db["runtimeRoleCreated"])
        self.assertFalse(self.plan["remoteActionsApproved"])

    def test_production_auth_and_explicit_no_storage_cleanup_mode(self):
        env = self.plan["railway"]["publicVariables"]
        self.assertEqual(env["ASPNETCORE_ENVIRONMENT"], "Production")
        self.assertEqual(env["PrivateQa__DisableCoverCleanup"], "true")
        for key in ["CoverUploads__Enabled", "CoverUploads__AllowLocalHttp", "E2E__Auth__Enabled",
                    "E2E__CoverUploads__Enabled", "E2E__CoverImageSearch__Enabled", "E2E__SteamIntegration__Enabled", "SteamIntegration__Enabled"]:
            self.assertEqual(env[key], "false")
        self.assertTrue(set(self.plan["forbiddenRuntimeVariableNames"]).isdisjoint(env))
        self.assertEqual(self.plan["railway"]["serverSecretVariableNamesOnly"], ["ConnectionStrings__Default"])

    def test_runtime_tls_and_bounded_pool(self):
        options = self.plan["database"]["connectionOptions"]
        self.assertEqual(options["SSL Mode"], "VerifyFull")
        self.assertEqual(options["Root Certificate"], "/etc/ssl/certs/ca-certificates.crt")
        self.assertEqual((options["Minimum Pool Size"], options["Maximum Pool Size"]), (0, 5))
        self.assertFalse(options["Include Error Detail"])
        policy = (ROOT / "deploy/qa/openssl-tls13.cnf").read_text()
        self.assertIn("MinProtocol = TLSv1.3", policy)
        self.assertIn("MaxProtocol = TLSv1.3", policy)

    def test_new_dockerfile_is_noble_amd64_nonroot_and_does_not_migrate(self):
        dockerfile = (ROOT / self.plan["railway"]["dockerfilePath"]).read_text()
        self.assertEqual(dockerfile.count("FROM --platform=linux/amd64"), 2)
        self.assertIn("--locked-mode", dockerfile)
        self.assertIn("--self-contained false", dockerfile)
        self.assertIn("USER $APP_UID", dockerfile)
        self.assertIn("COPY deploy/qa/openssl-tls13.cnf /etc/ssl/openssl.cnf", dockerfile)
        self.assertIn('ENTRYPOINT ["dotnet", "GameLibrary.Api.dll"]', dockerfile)
        self.assertNotIn("dotnet ef", dockerfile)
        program = (ROOT / "src/backend/GameLibrary.Api/Program.cs").read_text()
        self.assertNotIn("Migrate(", program)
        self.assertNotIn("MigrateAsync(", program)
        self.assertNotIn("EnsureCreated", program)
        self.assertEqual(self.plan["railway"]["preDeployCommand"], [])
        self.assertEqual(self.plan["railway"]["healthcheckPath"], "/health")

    def test_qa_auth_public_metadata_not_a_signing_secret(self):
        auth = self.plan["auth"]
        self.assertEqual(auth["issuer"], "https://yqngxhryzklrhhryzzoi.supabase.co/auth/v1")
        self.assertEqual(auth["jwksUri"], auth["issuer"] + "/.well-known/jwks.json")
        self.assertEqual(auth["unchangedApiAlgorithmAllowlist"], ["ES256", "RS256"])
        self.assertFalse(auth["es256OnlyOverrideExists"])
        self.assertFalse(auth["realUserLoginVerified"])
        self.assertFalse(auth["siteUrlAndRedirectChangesApproved"])

    def test_frontend_build_and_no_automatic_resource_deployment(self):
        web = self.plan["vercel"]
        config = json.loads((ROOT / "src/frontend/vercel.json").read_text())
        self.assertEqual(web["rootDirectory"], "src/frontend")
        self.assertEqual(web["nodeVersion"], "24.x")
        self.assertEqual(web["buildCommand"], config["buildCommand"])
        self.assertEqual(web["outputDirectory"], config["outputDirectory"])
        self.assertFalse(config["git"]["deploymentEnabled"])
        self.assertFalse(web["automaticDeploys"])
        self.assertFalse(self.plan["railway"]["automaticDeploys"])
        self.assertIsNone(web["frontendOrigin"])
        self.assertIsNone(self.plan["railway"]["apiOrigin"])
        self.assertTrue(any(x["destination"] == "/index.html" for x in config["rewrites"]))
        self.assertEqual(set(web["publicVariables"]), {"QA_SUPABASE_URL"})


if __name__ == "__main__":
    unittest.main(verbosity=2)

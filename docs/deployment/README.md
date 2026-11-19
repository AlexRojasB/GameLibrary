# Private QA deployment — current entry point

**2026-11-19 status:** the existing Railway API and Vercel frontend are live in QA
at source `29ba0d97307739484363800450575b2666c37354`. QA uses Neon for application
PostgreSQL and Supabase for Auth. The dated plans below are historical approvals/
preparation, not instructions to recreate resources, reset credentials or rerun migrations.

- [Development / immutable QA / main promotion workflow](../development/release-workflow.md)
  records current branch/deployment alignment and proposed CI/approval gates.
- [Railway logging investigation and local candidate](logging-investigation-2026-11-19.md).

These local documents do not authorize publication, CI/provider settings, a main
merge or production deployment. Preserve real QA data and the owner's current Steam link.

## Historical preparation entry point

**2026-11-15: Neon PostgreSQL 17 + existing Supabase QA Auth architecture approved.
No cloud creation, migrations, deployment or Git publication approved.**

Start with:

1. [Approved private-QA architecture amendment](../architecture/private-qa-neon-amendment.md).
2. [Current Neon QA plan and next approval](neon-qa-plan.md).
3. [Discovered Neon project metadata and differences](neon-qa-project-discovery.md)
   and [actual target plan](../../deploy/qa/neon-project-plan.json). **Do not create
   another project:** the owner-created target differs from the original proposal.
4. [Exact environment-variable plan](../../deploy/qa/neon-qa-environment-plan.json).
5. [Complete intended-source review](neon-qa-source-review.md).
6. [Neon migration/verification strategy](neon-qa-migration-strategy.md).
7. [Proposed QA cleanup opt-out — not implemented](neon-qa-cleanup-review.md).

Original deployment plans/reports are preserved as historical evidence. In
particular, Supabase **application database** and migration-host instructions are
superseded for the approved Neon QA topology. The frozen Railway Docker/runtime
settings still supply the base configuration; the new plan supplies the Neon/Auth
separation. Old Vercel 403 and unapplied-security/tooling findings are historical,
not current conclusions. No historical file/hash or source archive is rewritten.

No plan here is automatically applied by a provider. Follow scope-specific owner
approval, exact returned resource IDs and verified source identity before writes.

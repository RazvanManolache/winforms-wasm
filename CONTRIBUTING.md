# Contributing

Start with the [build instructions](docs/building.md) and [architecture](docs/architecture.md). Run the built-in smoke form before introducing an external application.

## Report a behavior

Include the expected result, the actual result, steps to reproduce, browser and SDK versions, and the relevant error inventory entry. A screenshot helps explain rendering problems; a short interaction sequence helps distinguish rendering from input problems.

Prefer a minimal source example that you can share. Do not attach commercial application files, license keys, customer data, or converted copies of programs you cannot redistribute.

## Make a change

Fix the shared behavior when possible. Avoid changing an application's business rules to make a demo appear to work. Add a focused regression test when the behavior is suitable for an automated check, and exercise the affected interaction in the browser.

Keep generated builds, transformed assemblies, external applications, local caches, and diagnostic logs out of the repository. Preserve upstream copyright and license notices in adapted source files.

Screenshots should come from the live app and identify the demonstrated interaction. Archived desktop captures without provenance do not establish current parity.

# Security policy

PackWise opens files from untrusted sources, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not** open a public issue. Report privately through
[GitHub security advisories](https://github.com/whynotdhyan/PackWise/security/advisories/new).

Include, where possible:

- the PackWise version (Help › About) and Windows version,
- a description of the impact,
- steps or a sample archive that reproduces the issue (zip it with a password and share the
  password in the report).

You can expect an acknowledgement within 5 days and a fix or mitigation plan within 30 days for
confirmed issues. Credit is given in the release notes unless you prefer otherwise.

## Scope

In scope:

- writing outside the chosen destination (path traversal, links, alternate data streams, device names),
- silent overwrites or deletion of user files,
- loss of the Mark-of-the-Web on extracted files,
- leaking passwords, file names or contents (to disk, logs or the network),
- crashes or memory corruption triggered by crafted archives in PackWise's own code.

Vulnerabilities in the 7-Zip engine itself should also be reported to the
[7-Zip project](https://www.7-zip.org). PackWise ships engine updates promptly; the pinned
version is recorded in `third_party/7zip/engine.json`.

## Supported versions

Only the latest release receives security fixes.

# Repository instructions for AI agents

This file applies to the entire repository. System, developer, and current user
instructions always take precedence.

## Feature consolidation is part of the definition of done

After every feature edit, do not stop at the source change. Unless the user
explicitly asks for a narrower handoff, consolidate the feature across every
affected surface before reporting completion:

1. Update the implementation and all affected tests, documentation, examples,
   screenshots, generated previews, and static assets. Keep desktop, browser,
   README, and GitHub Pages descriptions consistent.
2. Run the focused checks for the changed code plus the repository's relevant
   production gates. For Pages changes, run `npm run pages:build`. For Polar
   Stream changes, run the focused JavaScript tests and appropriate Rust format,
   clippy, and test commands before making a production desktop build.
3. Build and install the updated local application into the existing resolved
   per-user installation, then launch or smoke-test that installed artifact when
   the environment supports it. Preserve the previous executable as a rollback
   copy and report the installed path.
4. Stage only files that belong to the feature. Never include unrelated or
   newly discovered user files. Commit the consolidated change on a feature
   branch, push it to GitHub, and create or update a draft pull request. GitHub
   Pages deploys from `main`, so state clearly when the public site is waiting
   for merge; do not merge a pull request without explicit authorization.
5. Report concrete verification results, the branch/commit/PR, the local install
   path, and any surface that could not be synchronized.

If a required build, install, test, push, or Pages step is blocked, investigate
safe in-scope alternatives and report the exact blocker. Do not describe a
feature as fully complete while one of these consolidation steps remains
unresolved.

## Recorded preview data

Hardware-free Polar Stream previews use
`apps/polar-stream/ui/data/preview-recording.json`, an anonymized 60-second real
Polar H10 ECG and accelerometer recording. Keep browser playback and static
waveform previews derived from this canonical fixture. Do not introduce a
NeuroKit or generated-signal fallback for app previews. Scientific references
to NeuroKit may remain where they document analysis-method provenance.

Never commit a BLE address, strap serial number, user name, or other personal
identifier with a replacement recording.

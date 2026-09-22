# M12 — Windows/WPF owner manual acceptance

**Status:** PENDING — automated candidate prepared; owner verification not performed by Codex
**Candidate source:** see the matching WP5 `CODEX_DONE` on the active implementation PR for the exact final SHA and local publish path.
**Scope:** M12 annual archive and historical access only. This checklist does not authorize merge or M13.

Use only a test installation and synthetic/test archive data. Do not point this acceptance run at production business data. Record concise observations and any failure; do not mark a result Passed unless the owner personally performed it against the exact candidate.

## Checklist

- [ ] The Archive tab opens without automatically selecting an archive year.
- [ ] Available local archive year(s) are displayed clearly.
- [ ] Explicitly selecting a year loads only that archive.
- [ ] Search by reference/telephone and date/status controls are understandable.
- [ ] Archived detail is clearly read-only and displays the expected product, options, tax, total and payment facts.
- [ ] Switching French ↔ Chinese keeps the archive state usable and labels readable.
- [ ] Explicit archive copy opens the Windows destination dialog; cancel is harmless; successful copy appears at the chosen destination.
- [ ] Existing-destination overwrite confirmation is clear.
- [ ] Kitchen archived reprint visibly contains `RÉIMPRESSION`.
- [ ] Customer archived reprint visibly contains `DUPLICATA`.
- [ ] Cancelled archived reprint visibly contains `ANNULÉ` plus the appropriate reprint mark.
- [ ] A non-authoritative/read-only device can browse, export and reprint the archive without gaining write controls or authority.
- [ ] Normal `Commandes` live search remains separate and live-only.
- [ ] No unexpected archive editing or deletion controls exist.

Do not repeat automated failure-injection tests manually. Record the candidate SHA, Windows version, language used, each observed result, and any issue requiring follow-up. Owner acceptance remains pending until the owner records their own disposition.

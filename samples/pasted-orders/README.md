# Pasted-order samples

This directory contains sanitized/synthetic source text used to design and test the structured-text order importer.

Rules:

- Do not commit real customer names, phone numbers, addresses, Hiboutik order references, payment data, or other sensitive production information.
- Preserve the structural characteristics of each source format.
- Keep distinct historical/source formats as separate samples when the external order format changes.
- Samples should be usable as parser regression tests once implementation begins.

## M09 Hiboutik source structure

`hiboutik-product-block-synthetic.txt` is a fully synthetic structural example derived from owner-verified Hiboutik automatic-order emails. It preserves only the relevant product-block grammar:

- `<quantity> x <product code> <description> (<source price>)`;
- following `Total : <amount>` lines;
- optional final `TOTAL <amount>`;
- the known `Livraison (0)` service/technical line.

The sample contains no production customer/order content and does not establish source prices as POS pricing authority. The controlling behavior is specified in `docs/paste-order-import.md` and `docs/decisions/m09-hiboutik-paste-operator-workflow-and-source-reference.md`.

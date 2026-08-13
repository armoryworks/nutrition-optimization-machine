# Household Policies & Audience-Scoped Recipes (design)

Status: **design — not yet implemented.** Feedback welcome.

NOM households currently treat every adult member as equal, and recipes as
either private or public. Two real-world situations need more:

1. **Stewardship** — one household member responsibly manages another's dietary
   constraints: a parent and a child's severe allergy, an adult child helping a
   parent manage diabetes, any care relationship. Today nothing stops the
   managed member from editing away their own guardrails.
2. **Scoped sharing** — sharing recipes with a defined group that is neither
   "my household" nor "the whole world": an extended family, a club, a care
   group, or an external nutrition professional's clientele.

Both are useful to self-hosted instances on their own, and both are designed as
generic primitives that external management tooling can drive through the same
tables and APIs.

## 1. Household roles

`household_member` gains `role: steward | member` (creator = first steward) and
an optional opaque `managed_by` marker identifying an external management tool,
when one is in use.

- Only stewards mutate locked restrictions, member policies, membership.
- A steward cannot demote the last steward; members cannot self-escalate.

## 2. Locked restrictions

`person_restriction` gains `locked` (+ `locked_by`). A locked restriction
cannot be removed by the member, and nom-api enforces it at write time:

- recipe edits that introduce the restricted ingredient class into the member's
  planned recipes are rejected;
- plan-slot assignment of non-compliant recipes is rejected;
- shuffle never proposes non-compliant recipes.

Rejections carry a machine-readable reason (`restriction_locked`) so the UI can
say plainly: "this restriction is locked by your household steward."

**Scope honesty:** this is assistance, not a safety guarantee. Ingredient data
is imperfect and free-text is free. The feature reduces accidents; it must
never be described as preventing them.

## 3. Member feature policies

A per-member `member_policy` row: `feature_gates` (jsonb map of known gate keys
to booleans — absent means allowed), `frequency_caps` (tag + max-per-week,
shaping shuffle and warning on manual edits), and `curated_only`.

Gate keys are a NOM-owned enum. Unknown keys are ignored, not errors
(forward compatibility). Initial gate keys: `shuffle`, `recipe_import`,
`recipe_create`, `recipe_edit`.

## 4. Audience-scoped recipe visibility

A third visibility tier between household and public:

```
audience (id, owner, name, managed_by NULL)
audience_member (audience_id, household_id)
recipe.visibility: private | household | audience | public
recipe_audience (recipe_id, audience_id)
```

**Enforcement checklist (normative — every read surface, no exceptions):**
search, browse, recipe detail, shuffle candidate pool, **recipe images**,
share-token creation, export. (The image endpoint shipped without a visibility
check once; that class of gap is why this list is normative.)

Audience-scoped recipes are never curation-eligible and never appear on public
recipe surfaces.

**Departure grace:** when a household leaves an audience, recipes its members
have actually cooked or favorited remain readable (not copyable/sharable) —
their cook history and notes are their own data and always survive. Plan
history referencing recipes that are no longer visible renders a tombstone
(name preserved) instead of breaking.

## 5. Contract stability

These tables are an integration surface for external management tools. A
single-row `policy_contract_version` is bumped on breaking changes to the
shapes above; additive changes (new gate keys, defaulted columns) don't bump.
External tools should refuse to run against an unknown version.

## 6. Non-goals

- DRM. A readable recipe is a copyable recipe; visibility scoping is access
  control, not copy protection.
- Surveillance. Policies gate features and enforce restrictions; they do not
  add tracking beyond what NOM already stores.

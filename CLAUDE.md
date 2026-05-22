<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **AutoBIMFusion** (2962 symbols, 5920 relationships, 258 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/AutoBIMFusion/context` | Codebase overview, check index freshness |
| `gitnexus://repo/AutoBIMFusion/clusters` | All functional areas |
| `gitnexus://repo/AutoBIMFusion/processes` | All execution flows |
| `gitnexus://repo/AutoBIMFusion/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

## ObjectIdCollection Convention

All AutoCAD object batch operations MUST use `ObjectIdCollection`. Follow the two-pass pattern: collect first, then act.

### Deletion

**NEVER delete objects one-by-one in a loop:**
```csharp
// WRONG — N separate transactions
foreach (ObjectId id in ids)
    id.EraseObject();
```

**ALWAYS collect into ObjectIdCollection first, then batch erase in one transaction:**
```csharp
// CORRECT — single transaction, batch erase
using ObjectIdCollection candidates = [];
foreach (...)
{
    if (shouldErase)
        _ = candidates.Add(id);
}
candidates.EraseObjects(trx); // extension in ObjectIdExtensions
```

### Purge vs Erase

| Scenario | Method |
|----------|--------|
| Symbol table records (blocks, layers, styles) / dictionary entries | `PurgeAndErase(db, ids)` — calls `db.Purge()` first |
| Model space entities (curves, text, block refs) | `ids.EraseObjects(trx)` — direct erase, NO `db.Purge()` |

> `db.Purge()` only removes from the collection items that **can** be purged (symbol table records with no references). Calling it on model entities silently empties the collection — use `EraseObjects` instead.

### Other Batch Operations

`ObjectIdCollection` is also required by AutoCAD API for:
- `db.WblockCloneObjects(ids, ...)` — cloning objects between databases
- `db.DeepCloneObjects(ids, ...)` — deep clone within same database
- `DrawOrderTable.MoveBelow(ids, ...)` — draw order manipulation
- `db.CountHardReferences(ids, refs)` — reference counting
- `Hatch.AppendLoop(type, ids)` — hatch boundary association

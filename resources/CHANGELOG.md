# Changelog

## 0.2.0

- Settings are grouped by feature, one section per module with its switch first.
- Server-owned settings are synchronised and locked on clients through Conditional Config Sync,
  so yields, mining speed and collapse strength cannot differ between players.
- Tin deposits are excluded from vein mining by default.
- A swing's damage carries into the next piece once the one it struck breaks, so mining speed now
  scales with the deposit instead of the swing count: with the base 5x work, a copper vein takes
  about 30 swings rather than 130, and extra miners shorten it further.
- Fractional multipliers roll the fraction instead of rounding it up on every drop, so 1.5x gives
  an extra item about half the time rather than a bonus item every time; averaged over a vein the
  yield is the configured multiplier.
- A stack that would exceed the item's own limit spills into an extra drop instead of becoming an
  oversized stack, so no multiplier can produce an illegal stack or lose items.
- Pickable bonuses scale with the pickable multiplier too: previously only the main amount scaled,
  so a flint node (1 + 2 bonus) gave 5 items at 3x instead of 9.

## 0.1.1

- Resource multipliers apply once per drop. Before this, mining a vein with a multiplier above 1
  multiplied the shared drop table again for every piece that broke, so stacks grew with the
  number of pieces instead of the configured factor.

## 0.1.0

- Initial release.

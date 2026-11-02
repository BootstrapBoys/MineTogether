# Changelog

## 0.2.0

- Settings are grouped by feature, one section per module with its switch first.
- Server-owned settings are synchronised and locked on clients through Conditional Config Sync,
  so yields, mining speed and collapse strength cannot differ between players.
- Tin deposits are excluded from vein mining by default.

## 0.1.1

- Resource multipliers apply once per drop. Before this, mining a vein with a multiplier above 1
  multiplied the shared drop table again for every piece that broke, so stacks grew with the
  number of pieces instead of the configured factor.

## 0.1.0

- Initial release.

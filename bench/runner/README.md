# ushqyr-bench-runner

Organiser tools for attempt folders written by the simulator. No dependencies for `verify` and `leaderboard`;
`resim` also needs the SDK (`pip install ../sdk/python`).

```
pip install .

ushqyr-bench verify <attempt folder> --key <key file>
ushqyr-bench resim <attempt folder>
ushqyr-bench leaderboard <results folder> --csv results.csv --board leaderboard.csv --semicolon
```

| Command | What it does | Exit code |
|---|---|---|
| `verify` | Recomputes the SHA-256 of every file and compares with `manifest.json`; with `--key` also checks `manifest.sig` (HMAC-SHA256 of the manifest) | 0 verified, 1 modified or missing, 2 checksums match but the signature was not checked (0 with `--allow-unsigned`) |
| `resim` | Sends the recorded RC packets to a simulator in training mode, with the attempt's seed and track, and compares points and time | 0 same result, 1 different, 2 can't run |
| `leaderboard` | Ranks every `result.json` under a folder: more points first, then less time; a team's best counted attempt per track; all tracks added up unless `--track` is given | 0 |

`--semicolon` writes `;` as the separator and `,` as the decimal mark, which is what spreadsheet programs expect in
Russian and Kazakh regional settings. The files are UTF-8 with a byte-order mark.

The batch runner (`ushqyr-bench batch`), `submit-check`, `validate-drone` and `dataset` arrive with later milestones.

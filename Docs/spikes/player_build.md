# Spikes run inside a player build: database, licence, reports (ED0, 21 November 2026)

**Question.** Do the building blocks work in a built Windows player, where Unity's runtime (Mono) differs from the
editor and from plain .NET?

**How.** `Builds\EduSpike\Ushqyr.exe -edu-spike <folder>` runs `EduSpike` (`Scripts/Edu/Runtime/EduSpike.cs`) and writes
`spike.txt`. Windows 10, v1.5 full-game build.

| Spike | Result in the player | Decision |
|---|---|---|
| **Database** | SQLite 3.43.2 from Windows' own `winsqlite3.dll`: 1000 rows with Kazakh text written in one transaction and read back | D14: no bundled library, no LiteDB |
| **Licence signature** | RSA-2048 with SHA-256: a genuine signature is accepted, an edited licence is refused. **ECDSA is not available in Unity's runtime** (`NotImplementedException`) | The licence is signed with RSA |
| **Hardware fingerprint** | 7 identifiers readable without admin rights: Unity's device id, CPU name, PC model, Windows machine GUID, board name, BIOS version, network card address. The same on a second read | Enough for "valid when N of M match". Only hashes go into the request file |
| **XLSX report** | Opens with a spreadsheet reader; Kazakh, Russian and English cells and numbers come back unchanged, `<`, `>` and `&` included | D15 |
| **PDF report** | Text is real text (it can be selected and searched), the font is inside the file, every Kazakh letter (Ә Ғ Қ Ң Ө Ұ Ү Һ І, both cases) is extracted back correctly; the rendered page was looked at | D15 |

## Limits of what was checked
- **Excel and LibreOffice are not installed here.** The XLSX was read back with the `openpyxl` library and the PDF with
  PyMuPDF. The plan's acceptance ("opens correctly in Excel and LibreOffice") still needs one look in each.
- **The fingerprint was read on one PC.** Which identifiers survive disk cloning (the plan's concern for school PCs)
  cannot be told from one machine: the machine GUID and the disk are copied by cloning, the board name, BIOS version
  and CPU are the same on every PC of one model. The network card address is the only one of the seven that tells two
  cloned PCs of the same model apart. This needs a real pair of cloned school PCs before the licence is designed.
- **Linux:** the database wrapper has a path for `libsqlite3.so.0`, not tried.
- The spike's key pair is made on the spot and thrown away. No real licence key exists (the prompt's rule: ask first).

## What was learnt beyond the question
- Unity's runtime and .NET **sort Kazakh letters differently** (Unity put "Ә" before "А"). Names are now ordered by an
  explicit Kazakh alphabet (D21).
- SQLite's case-insensitive comparison covers Latin letters only: "7Б" and "7б" were two different groups (D21).
- A full-game Windows build after the ambient-occlusion change of 21 November took over three hours, almost all of it
  shader variants. **Spikes and tests should use the AI Bench-only kit build (under a minute) wherever they can.**

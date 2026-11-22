# The class link: notes for school IT

The teacher's PC and the students' PCs ("seats") talk to each other over the school's own network. Nothing goes to
the internet.

| What | Port | Direction |
|---|---|---|
| The class link itself | TCP 47810 | each seat connects to the teacher's PC |
| "A class is open here" (lets seats find the class without typing an address) | UDP 47811, broadcast | from the teacher's PC to the network, once a second while a class is open |

- **Windows Firewall.** The first time the teacher opens a class, Windows may ask whether the program may use the
  network. Allow it for private (school) networks. The installer will open these two ports itself (ED9); until then
  it is this one question on the teacher's PC. Seats only connect outwards and are not asked.
- **If broadcast is blocked** between the PCs, seats do not see the class in the list. They can still join: on the
  seat, type the address shown on the teacher's screen (for example `192.168.1.20`).
- **Who can join.** A seat needs the six-digit code shown on the teacher's screen. A new code is made each time the
  class is opened.
- **Who can command.** Every command to a seat (start, pause, message, weather, failure) is signed with a key that
  exists only in the class database on the teacher's PC. A seat remembers which key its class uses and obeys nothing
  else, so a student's PC cannot command another student's PC, even knowing the code.
- **What a seat sends:** what it is doing (exercise, time, score), a small picture (192 pixels wide, three a second)
  of the simulator's own view, and each finished flight. The picture is taken from what the simulator drew; it cannot
  contain the desktop or another program.
- **What is stored:** results and recordings, in the class database folder on the teacher's PC. Pictures are not
  stored.
- **The limits of this protection.** The code is six digits and the link is not encrypted: it keeps a neighbouring
  classroom out and stops students commanding each other. It is not built to resist someone recording the network
  traffic of the school. Keep the class network a school network.

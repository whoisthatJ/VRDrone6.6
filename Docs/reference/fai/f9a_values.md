# FAI F9A (drone soccer): the values, read from the rules

**Source.** FAI Sporting Code, Section 4 Aeromodelling, Volume F9 Drone Sports, **2026 Edition, Version 2, effective
1 June 2026** (`CIAM-F9-Drones-2026-06-01.pdf` in this folder, 56 pages; drone soccer is pages 28 to 41). Read on
21 November 2026. F9A is a **provisional class**.

The soccer rules file of the simulator takes its numbers from this page, not from the table in `EDU_PLAN.md` §6. Version 1
of the 2026 edition was not read, so what changed between the two versions is not known.

"Recommended" is the rules' own word: an organiser may choose otherwise, within the limits noted.

## The ball
| Value | F9A-A ("40 cm") | F9A-B ("20 cm") | Paragraph |
|---|---|---|---|
| Diameter of the protective sphere | 40 cm, +2 cm (must fit a 42 cm circle) | 20 cm, +2 cm (must fit a 22 cm circle) | F9A.1.1 |
| Greatest weight, ready to fly | 1.2 kg | 300 g | F9A.1.1 |
| Flat part at the bottom, so it stands on the ground | at most 2 cm high | at most 2 cm high | F9A.1.1 |
| Any one opening of the frame | at most 150 cm² | at most 150 cm² | F9A.1.1 |
| Motors | electric, at most four | electric, at most four | F9A.1.2 |
| Battery | up to 6S | up to 4S | F9A.1.2 |
| Cell voltage when full | at most 4.25 V | at most 4.25 V | F9A.1.2 |
| Propellers | up to 6 inches (15.2 cm), not metal | up to 3 inches (7.6 cm), not metal | F9A.1.3 |
| Team light strip around the frame | at least 40 lights | at least 16 lights | F9A.1.5 |
| Rear light of each ball | at least 6 lights, a different colour for each player of a team | the same | F9A.1.5 |

- **No automatic help in flight (F9A.1).** Forbidden: any pre-programmed manoeuvre, and any system that holds or
  corrects position **or height** automatically. Allowed: turning the ball over after a crash, and levelling it after a
  crash on the player's command.
- A fail-safe that stops the motors is required (F9A.1).
- Teams are **red** and **blue**. The Striker's rear light is the **other** team's colour: blue for the red team, red
  for the blue team. The organiser may also ask for a ribbon hanging under the Striker's ball (F9A.1.5, F9A.1.6).

## The field
| Value | F9A-A | F9A-B | Paragraph |
|---|---|---|---|
| Flying zone (recommended) | 14 × 7 m | 6 × 3 m | F9A.2.2 |
| Height of the cage (recommended) | 5 m | 3 m | F9A.2.2 |
| Another size | allowed, but the long side must be twice the short one | the same | F9A.2.2 |
| Start areas | in the middle of each baseline; about 1.5 m wide; length by the ball size and the number of players | the same; no more than 1 m wide | F9A.2.2 |
| Markings | outline, a centre line joining the middles of the long sides, the centre point | the same | F9A.2.2 |
| Players' areas | outside the cage, at the short sides | the same | F9A.2.3 |

The cage is required; nobody is inside it during a set; nothing stands in the flying zone (F9A.2.2).

## The goal rings
| Value | F9A-A | F9A-B | Paragraph |
|---|---|---|---|
| Outer diameter (recommended) | 100 cm | 70 cm | F9A.3.1 |
| Inner diameter (recommended) | 60 cm | 40 cm | F9A.3.1 |
| Thickness (recommended) | at most 20 cm | at most 10 cm | F9A.3.1 |
| Distance inside the baseline | 1.5 m | 1 m | F9A.3.2 |
| **Height** | **2 m from the ground to the bottom of the inner opening** | **the same 2 m** | F9A.3.2 |

- The rings face the centre of the field, are parallel to each other and **hang from the ceiling of the cage** (F9A.3.2).
- One ring is red and one blue (F9A.3.3).
- A sensor in the ring for electronic scoring is allowed; the scoring referee still decides (F9A.3.4, F9A.8.4).

## Teams
| Rule | Value | Paragraph |
|---|---|---|
| Active players | 3 to 5, the same for both teams at the start of the match; the organiser sets the number | F9A.4.1 |
| Players on a team's list | the number of active players plus 2 | F9A.4.2 |
| Striker | one active player; only the Striker's ball can score | F9A.4.1 |
| Team captain | one active player, speaks to the referees | F9A.4.1 |
| Substitution of a player | only in a break between sets | F9A.4.1 |
| Balls per player | up to 2; ball and battery are changed only in a break between sets | F9A.5 |
| Striker and captain | may change between sets and between matches | F9A.4.2 (note) |

## The match
| Rule | Value | Paragraph |
|---|---|---|
| Sets | 3 sets of 3 minutes | F9A.8 |
| Break between sets | about 2 to 3 minutes | F9A.8 |
| Sides | by coin toss; kept for all three sets unless the referee orders a swap (wind, outdoors) | F9A.8.1 |
| Start | referee asks both captains; "Arm your quads"; the start signal sounds about 3 to 5 s later | F9A.8.2 |
| Early start (a ball leaves the ground before the signal) | the start is repeated **and** a penalty shot is given against that team | F9A.8.2 |
| A ball only moving before the signal | a warning, not an early start | F9A.9.2 |
| Goal | the Striker's **whole** ball passes through the **whole** opponents' ring, in the direction of the opponents' baseline | F9A.8.4 |
| Another player's ball through the opponents' ring | no goal, no penalty | F9A.8.4 |
| After a goal | every active player of the scoring team must return to its own half before the team may score again. If not: a penalty shot against the team, and a goal that came from it does not count | F9A.8.4 |
| Holding opponents back from returning | allowed | F9A.8.4 |
| Own ring | the Striker may pass through or stay in its own ring to defend. Any other player who passes through or stays in its own ring **to defend**: penalty shot against the team | F9A.8.4 |
| A ball on the ground that cannot fly | out for the rest of the set; its player leaves the players' area; the team plays one short | F9A.8.4, F9A.8.5 |
| New Striker | **only when the Striker's ball can no longer fly:** the captain calls a time-out, the clock stops, all balls land, another active player becomes Striker and starts from the start area. Once per set per team | F9A.8.5 |
| End of a set | the referee's signal; time spent on penalty shots is added | F9A.8.3 |
| Set result | more goals wins the set; equal goals (or none) is a tied set | F9A.8.6 |
| Match result | the team that wins two sets | F9A.8.6 |
| Tied match | when a winner is needed: coin toss, overtime or penalty shoot-out, announced before the event | F9A.8.6 |
| Overtime | a set in which the first goal wins | F9A.8.6 |
| Penalty shoot-out | a set number of penalty shots (5 is the rules' example), by different players where possible | F9A.8.6 |
| Video recording | may **not** be used by the referee to decide during the match; only for a complaint or protest | F9A.8.7 |
| Wind limit outdoors | play stops if the wind stays above 9 m/s (at 2 m height) for a minute | F9A.10 |
| Interrupted set | played again in full, unless one team caused it: then that team loses the set | F9A.10 |

## Penalties (all given by the match referee; they end with the match)
| Penalty | When | Effect | Paragraph |
|---|---|---|---|
| Penalty shot | early start; not returning after a goal; a non-Striker defending in its own ring | The Striker against **one** defender, 10 seconds from the referee's signal. The time is not part of the set. May be taken at the end of the set if announced before the event | F9A.9.1 |
| Warning | someone not allowed in the players' area; minor misconduct; delaying a start; a ball moving before the signal; **unintentional** contact with a ball on the ground or with a person | None by itself | F9A.9.2 |
| Yellow card | two warnings for the same reason; changing a player during a set; major misconduct; handling a ball during a set; **intentional** contact with a ball on the ground or with a person | The player concerned (or one chosen by the captain) is out for the rest of the **set** | F9A.9.3 |
| Red card | two yellow cards; a player not on the list; severe misconduct; dangerous flying | The player concerned (or one chosen by the captain) is out for the rest of the **match**; the team plays one ball short | F9A.9.4 |

## Officials
| Official | Role | Paragraph |
|---|---|---|
| Event director | Runs the event, names the referees, confirms results | F9A.12.1 |
| Main referee | Final word on the rules; decides before the event how tied matches are settled | F9A.12.2.1 |
| Match referee | Leads the match: start and end of sets, penalties, results. Acts through a microphone | F9A.12.2.2 |
| Two scoring referees | One at each side of the field, outside the flying zone; each follows one Striker and decides goals | F9A.12.2.3, F9A.8.4 |

## Tournament formats (F9A.7)
- **Groups, then knockout:** groups of 3 to 5 teams drawn blind, everyone plays everyone; 3 points for a win, 1 for a
  tied match, 0 for a defeat; ranking by points, then goal difference, then goals scored, then a penalty shoot-out;
  the top two of each group go on to direct elimination, with a match for third place and a final.
- **Successive rounds:** a blind draw each round, with direct elimination or double elimination (out after two lost
  matches).

## Differences from the plan's table (`EDU_PLAN.md` §6)
| Plan | Rules | What it changes |
|---|---|---|
| Ring height "read from the official PDF" | 2 m from the ground to the bottom of the inner opening, both subclasses; rings hang from the cage's ceiling | Arena geometry is now fixed |
| Nothing about flight assistance | Automatic position and height holding are forbidden on a drone ball | **In soccer the Position and Altitude hold modes must be switched off.** Whether self-levelling (Angle mode) is allowed is not said: the rules name position and height only. Soccer bots, if built, fly without those aids too |
| "The Striker can be re-assigned once per set" | Only when the Striker's ball can no longer fly, through a time-out; once per set | Not a free tactical choice during a set. Between sets the Striker may change freely |
| "A non-Striker parking in its own ring" | Passing through **or** staying, when done to defend. Passing through without blocking a score is allowed | The automatic detection needs "was the Striker of the other team attacking this ring", not only "is a ball inside" |
| Referee tool "instant replay of the last 15 s" | A video recording may not be used for decisions during a match | Keep instant replay for class practice; a "strict FAI" switch turns it off for the referee |
| Early start: penalty shot | Also: the start is repeated; a ball that only moves gets a warning | Two cases for the detector, by "left the ground" |
| Cards described per player | A second warning for the same reason is a yellow card; a second yellow card is a red card; when no single player is to blame the captain chooses who leaves | Referee mode needs the counting and the captain's choice |
| Nothing | Contact with a ball that is on the ground: warning if unintentional, yellow card if intentional | A referee decision; the simulator can only report the contact |
| Nothing | Team colours red and blue; the Striker's rear light in the other team's colour | Ball visuals |
| Nothing | Tournament formats with points and ranking rules | Answers part of question 6 of §13 if a soccer tournament is wanted |
| Matches the plan | Sizes, weights, battery and propeller limits, 3 sets of 3 minutes, 2 to 3 minute breaks, "Arm your quads" and 3 to 5 s, the goal definition, return to own half, 10 s penalty shot, tie-break methods, 3 to 5 active players, two scoring referees | Nothing |

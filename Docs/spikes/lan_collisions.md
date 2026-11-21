# Spike: drone-ball contacts over a LAN (ED0, 21 November 2026)

**Question.** For soccer (`EDU_PLAN.md` §6): does each PC simulate its own ball and bounce off copies of the others, or
does the host simulate every ball?

**This is a numerical model, not a test in Unity on two PCs.** Two equal discs of 40 cm, 1 kHz steps, 400 random
meetings per row (closing speed 2 to 14 m/s). Script: `Docs/spikes/data/lan_collisions_model.py`. The balls do not
exist in the game yet, and there is no second PC; the real test belongs to ED8.

## Each PC simulates its own ball
| How the other ball is shown | Hits felt by only one of the two | End position against an ideal bounce, after 0.2 s |
|---|---|---|
| At its last received position (no prediction), 1 to 50 ms latency | 8 to 22% | median 24 to 34 cm, worst cases over 1 m |
| Predicted forward by its speed, **and** a second bounce with the same ball blocked for 0.15 s | 0% | 0 cm in the model |
| Predicted forward, without the block | 0% | median 30 cm: both PCs bounce twice, because each still sees the other one approaching |

Two things follow:
- **Prediction is not optional.** Without it one pilot bounces off a ball the other pilot never touched.
- **One bounce per contact.** Without the block the fix above is worthless.

The 0 cm is too kind: the model's balls fly straight between hits. A pilot accelerating at 15 m/s² adds an error of
half of acceleration × age², about 0.3 cm at 20 ms and 2 cm at 50 ms. Both are far inside the plan's limit of 10 cm.

## The host simulates every ball
The PCs always agree, but every stick movement waits for the trip to the host and back, a host step and the
interpolation buffer:

| Latency one way | State sent 60 times a second | 120 times a second |
|---|---|---|
| 1 ms (wired LAN) | 44 ms | 23 ms |
| 5 ms | 52 ms | 31 ms |
| 20 ms (busy Wi-Fi) | 82 ms | 61 ms |

A racing pilot notices 20 to 30 ms. Hiding it needs prediction and correction of the pilot's own ball, which is the
hard part of the first scheme again, plus corrections the pilot can feel.

## Recommendation
**Each PC simulates its own ball** (the plan's default), with three rules: other balls are predicted forward by their
speed; one bounce per pair until they separate; the host alone decides goals, fouls and the clock. Send state 120 times a
second. Confirm in ED8 with two real instances and the plan's "within 10 cm after 0.2 s" test.
